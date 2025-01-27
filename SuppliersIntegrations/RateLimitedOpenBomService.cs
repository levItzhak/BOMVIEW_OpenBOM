using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using BOMVIEW.Interfaces;
using BOMVIEW.OpenBOM.Models;
using BOMVIEW.Models;
using System.Net.Http;
using System.Linq;
using System.Collections.Concurrent;

namespace BOMVIEW
{
    public class RateLimitedOpenBomService
    {
        private readonly OpenBomService _openBomService;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _throttler;
        private readonly int _maxRetries;
        private readonly int _baseDealyBetweenRequestsMs;
        private readonly Random _random = new Random();

        // Improved cache for API responses
        private readonly ConcurrentDictionary<string, (object Data, DateTime Expiry)> _responseCache =
            new ConcurrentDictionary<string, (object, DateTime)>();

        // Track recent response times to dynamically adjust delay
        private readonly ConcurrentQueue<long> _recentResponseTimes = new ConcurrentQueue<long>();
        private readonly int _responseHistorySize = 15; // Increased from 10
        private int _consecutiveErrors = 0;
        private DateTime _lastRateLimitError = DateTime.MinValue;
        private readonly DigiKeyService _digiKeyService;

        // Track rate limit state
        private bool _isInRateLimitCooldown = false;
        private readonly object _rateLimitLock = new object();
        private readonly TimeSpan _cooldownPeriod = TimeSpan.FromSeconds(30);

        public RateLimitedOpenBomService(ILogger logger)
        {
            _openBomService = new OpenBomService(logger);
            _logger = logger;

            // Reduced from 2 to 1 to be more conservative
            _concurrentRequests = 1;
            _throttler = new SemaphoreSlim(_concurrentRequests);

            _maxRetries = 5;
            _baseDealyBetweenRequestsMs = 300; // Increased from 50ms
        }

        public RateLimitedOpenBomService(ILogger logger, DigiKeyService digiKeyService) : this(logger)
        {
            _digiKeyService = digiKeyService;
        }

        public int _concurrentRequests { get; private set; }

        // Adaptive delay calculation based on recent performance and error history
        private int GetCurrentDelayMs()
        {
            // Start with base delay
            int delay = _baseDealyBetweenRequestsMs;

            // If we're in cooldown mode due to rate limiting, use a much higher delay
            if (_isInRateLimitCooldown)
            {
                return Math.Max(delay, 2000); // Minimum 2 seconds during cooldown
            }

            // Calculate average response time if we have data
            if (_recentResponseTimes.Count > 0)
            {
                long sum = 0;
                int count = 0;

                foreach (var time in _recentResponseTimes)
                {
                    sum += time;
                    count++;
                }

                if (count > 0)
                {
                    long avgResponseTime = sum / count;
                    // Use at least 75% of the average response time as delay
                    delay = Math.Max(delay, (int)(avgResponseTime * 0.75));
                }
            }

            // Add more delay if we've seen consecutive errors
            if (_consecutiveErrors > 0)
            {
                // Exponential backoff based on consecutive errors
                delay = Math.Min(10000, delay * (1 << Math.Min(_consecutiveErrors, 5)));
            }

            // Add jitter to prevent request synchronization
            delay += _random.Next(0, Math.Max(50, delay / 5));

            return delay;
        }

        // Record a response time and update our adaptive parameters
        private void RecordResponseTime(long responseTimeMs, bool isSuccess)
        {
            _recentResponseTimes.Enqueue(responseTimeMs);

            // Keep only the most recent response times
            while (_recentResponseTimes.Count > _responseHistorySize)
            {
                _recentResponseTimes.TryDequeue(out _);
            }

            // Reset consecutive errors on success
            if (isSuccess)
            {
                _consecutiveErrors = 0;
            }
        }

        // Handle rate limit detection and cooldown management
        private void HandleRateLimitDetected()
        {
            _consecutiveErrors++;
            _lastRateLimitError = DateTime.UtcNow;

            lock (_rateLimitLock)
            {
                if (!_isInRateLimitCooldown)
                {
                    _isInRateLimitCooldown = true;

                    // Start a task to clear the cooldown after the period
                    Task.Run(async () => {
                        await Task.Delay(_cooldownPeriod);

                        lock (_rateLimitLock)
                        {
                            _isInRateLimitCooldown = false;
                            _logger.LogInfo("Rate limit cooldown period ended");
                        }
                    });

                    // Temporarily reduce concurrent requests if we have more than 1
                    if (_concurrentRequests > 1)
                    {
                        // Keep track of original value to restore later
                        int originalConcurrency = _concurrentRequests;
                        _concurrentRequests = 1;

                        // Restore original concurrency after double the cooldown period
                        Task.Run(async () => {
                            await Task.Delay(_cooldownPeriod.Add(_cooldownPeriod));
                            _concurrentRequests = originalConcurrency;
                            _throttler.Release(originalConcurrency - 1);
                            _logger.LogInfo($"Restored original concurrency to {originalConcurrency}");
                        });
                    }
                }
            }
        }

        // Calculate backoff with jitter for more effective retries
        private TimeSpan CalculateBackoffWithJitter(int attempt, bool isRateLimitError = true)
        {
            // Base retry delay differs based on error type
            double baseDelayMs = isRateLimitError ? 2000 : 1000;

            // Calculate exponential delay
            double exponentialDelayMs = baseDelayMs * Math.Pow(isRateLimitError ? 2.5 : 2.0, attempt);

            // Cap at a reasonable maximum
            double cappedDelayMs = Math.Min(isRateLimitError ? 60000 : 30000, exponentialDelayMs);

            // Add jitter by randomizing within 80-120% of calculated delay
            double jitterMultiplier = 0.8 + (_random.NextDouble() * 0.4); // 0.8 to 1.2
            double finalDelayMs = cappedDelayMs * jitterMultiplier;

            return TimeSpan.FromMilliseconds(finalDelayMs);
        }

        // Generic method to execute operations with retry and caching
        private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, string operationName,
                                                      string cacheKey = null, TimeSpan? cacheDuration = null)
        {
            // Check cache first if a cache key is provided
            if (cacheKey != null && _responseCache.TryGetValue(cacheKey, out var cachedItem))
            {
                if (cachedItem.Expiry > DateTime.UtcNow)
                {
                    _logger.LogInfo($"Cache hit for {operationName} ({cacheKey})");
                    return (T)cachedItem.Data;
                }
                // Remove expired cache entry
                _responseCache.TryRemove(cacheKey, out _);
            }

            for (int attempt = 0; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    // Wait for a slot in the throttling semaphore
                    await _throttler.WaitAsync();
                    long startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    try
                    {
                        _logger.LogInfo($"Starting operation {operationName}, attempt {attempt + 1}/{_maxRetries + 1}");
                        var result = await operation();
                        _logger.LogInfo($"Operation {operationName} completed successfully");

                        // Record metrics for successful operation
                        long endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        RecordResponseTime(endTime - startTime, true);

                        // Cache the successful result if caching is requested
                        if (cacheKey != null && cacheDuration.HasValue)
                        {
                            _responseCache[cacheKey] = (result, DateTime.UtcNow.Add(cacheDuration.Value));
                        }

                        return result;
                    }
                    finally
                    {
                        // Always add delay before releasing semaphore
                        int delay = GetCurrentDelayMs();
                        await Task.Delay(delay);
                        _throttler.Release();
                    }
                }
                catch (Exception ex) when (ex.Message.Contains("TooManyRequests") ||
                                          ex.Message.Contains("429") ||
                                          ex.Message.Contains("Rate limit"))
                {
                    // Handle rate limit error
                    HandleRateLimitDetected();

                    if (attempt == _maxRetries)
                        throw;

                    // Calculate backoff with jitter
                    var backoffDelay = CalculateBackoffWithJitter(attempt, true);
                    _logger.LogWarning($"{operationName} rate limited. Retrying in {backoffDelay.TotalSeconds:F1} seconds...");
                    await Task.Delay(backoffDelay);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error in {operationName}: {ex.Message}");
                    _consecutiveErrors++;

                    if (attempt == _maxRetries)
                        throw;

                    // Backoff for other errors
                    var backoffDelay = CalculateBackoffWithJitter(attempt, false);
                    _logger.LogWarning($"{operationName} failed. Retrying in {backoffDelay.TotalSeconds:F1} seconds...");
                    await Task.Delay(backoffDelay);
                }
            }

            throw new Exception($"Failed to execute {operationName} after {_maxRetries} retries");
        }

        // Non-generic version for void returns
        private async Task ExecuteWithRetryAsync(Func<Task> operation, string operationName)
        {
            for (int attempt = 0; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    await _throttler.WaitAsync();
                    long startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    try
                    {
                        _logger.LogInfo($"Starting operation {operationName}, attempt {attempt + 1}/{_maxRetries + 1}");
                        await operation();
                        _logger.LogInfo($"Operation {operationName} completed successfully");

                        long endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        RecordResponseTime(endTime - startTime, true);

                        return;
                    }
                    finally
                    {
                        int delay = GetCurrentDelayMs();
                        await Task.Delay(delay);
                        _throttler.Release();
                    }
                }
                catch (Exception ex) when (ex.Message.Contains("TooManyRequests") ||
                                         ex.Message.Contains("429") ||
                                         ex.Message.Contains("Rate limit"))
                {
                    HandleRateLimitDetected();

                    if (attempt == _maxRetries)
                        throw;

                    var backoffDelay = CalculateBackoffWithJitter(attempt);
                    _logger.LogWarning($"{operationName} rate limited. Retrying in {backoffDelay.TotalSeconds:F1} seconds...");
                    await Task.Delay(backoffDelay);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error in {operationName}: {ex.Message}");
                    _consecutiveErrors++;

                    if (attempt == _maxRetries)
                        throw;

                    var backoffDelay = CalculateBackoffWithJitter(attempt, false);
                    _logger.LogWarning($"{operationName} failed. Retrying in {backoffDelay.TotalSeconds:F1} seconds...");
                    await Task.Delay(backoffDelay);
                }
            }

            throw new Exception($"Failed to execute {operationName} after {_maxRetries} retries");
        }

        // Add cache durations to frequently called read methods
        public Task<List<OpenBomListItem>> ListCatalogsAsync() =>
            ExecuteWithRetryAsync(() => _openBomService.ListCatalogsAsync(),
                                 "ListCatalogs",
                                 "catalogs",
                                 TimeSpan.FromMinutes(5));

        public Task<OpenBomDocument> GetCatalogAsync(string catalogId) =>
            ExecuteWithRetryAsync(() => _openBomService.GetCatalogAsync(catalogId),
                                 "GetCatalog",
                                 $"catalog_{catalogId}",
                                 TimeSpan.FromMinutes(5));

        // Write operations don't use caching
        public Task RemovePartFromCatalogAsync(string catalogId, string partNumber) =>
            ExecuteWithRetryAsync(() =>
            {
                // Invalidate any related cached data
                string cacheKey = $"catalog_{catalogId}";
                _responseCache.TryRemove(cacheKey, out _);
                return _openBomService.RemovePartFromCatalogAsync(catalogId, partNumber);
            },
            "RemovePartFromCatalog");

        public Task<List<OpenBomListItem>> ListBomsAsync() =>
            ExecuteWithRetryAsync(() => _openBomService.ListBomsAsync(),
                                 "ListBoms",
                                 "boms",
                                 TimeSpan.FromMinutes(5));

        public Task<List<BomTreeNode>> GetBomHierarchyAsync(string bomId) =>
            ExecuteWithRetryAsync(() => _openBomService.GetBomHierarchyAsync(bomId),
                                 "GetBomHierarchy",
                                 $"bomHierarchy_{bomId}",
                                 TimeSpan.FromMinutes(5));

        // Helper method to create a normalized cache key for part numbers
        private string CreatePartCacheKey(string catalogId, string partNumber)
        {
            string normalizedPart = NormalizePartNumber(partNumber);
            return $"part_{catalogId}_{normalizedPart}";
        }

        // Add caching to catalog item queries
        public async Task<BomTreeNode> GetCatalogItemWithRetryAsync(string catalogId, string partNumber)
        {
            try
            {
                var normalizedPartNumber = NormalizePartNumber(partNumber);
                string cacheKey = CreatePartCacheKey(catalogId, normalizedPartNumber);

                return await ExecuteWithRetryAsync(async () =>
                {
                    try
                    {
                        var partItem = await _openBomService.GetCatalogItemByPartNumberAsync(catalogId, normalizedPartNumber);

                        if (partItem != null && partItem.Cells?.Count > 0)
                        {
                            // Convert to BomTreeNode for consistent handling
                            return new BomTreeNode
                            {
                                Id = catalogId,
                                PartNumber = normalizedPartNumber,
                                Type = "item",
                                TreeNodeType = BomTreeNode.NodeType.Item
                            };
                        }
                        return null;
                    }
                    catch
                    {
                        return null;
                    }
                }, $"GetCatalogItem-{normalizedPartNumber}", cacheKey, TimeSpan.FromMinutes(10));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting catalog item {partNumber}: {ex.Message}");
                return null;
            }
        }

        private string NormalizePartNumber(string partNumber)
        {
            if (string.IsNullOrWhiteSpace(partNumber))
                return string.Empty;

            // Remove extra spaces, dashes, and trim
            return partNumber.Trim().Replace(" ", "").Replace("-", "");
        }

        public async Task<bool> AddPartToCatalogWithRetryAsync(string catalogId, BomEntry part)
        {
            // Clear any cached entries related to this part
            string cacheKey = CreatePartCacheKey(catalogId, part.OrderingCode);
            _responseCache.TryRemove(cacheKey, out _);
            _responseCache.TryRemove($"catalog_{catalogId}", out _);

            return await ExecuteWithRetryAsync(async () =>
            {
                var partRequest = new OpenBomPartRequest
                {
                    PartNumber = part.OrderingCode,
                    Properties = new Dictionary<string, string>
                    {
                        { "Part Number", part.OrderingCode }
                    }
                };


                await _openBomService.UpdateCatalogPartAsync(catalogId, partRequest);
                return true;
            }, $"AddPartToCatalog-{part.OrderingCode}");
        }


        // Modified version of the EnsurePartInCatalogAsync method to fix the issues

        public async Task<bool> EnsurePartInCatalogAsync(string catalogId, BomEntry part, DigiKeyProductResponse digiKeyData)
        {
            // Clear any cached entries related to this part
            string cacheKey = CreatePartCacheKey(catalogId, part.OrderingCode);
            _responseCache.TryRemove(cacheKey, out _);
            _responseCache.TryRemove($"catalog_{catalogId}", out _);

            return await ExecuteWithRetryAsync(async () =>
            {
                try
                {
                    _logger.LogInfo($"Ensuring part {part.OrderingCode} exists in catalog {catalogId}");

                    // Set a reasonable timeout for the entire operation
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                    // Get catalog information for the name - with timeout
                    OpenBomDocument catalogDetail = null;
                    try
                    {
                        var getCatalogTask = GetCatalogAsync(catalogId);
                        if (await Task.WhenAny(getCatalogTask, Task.Delay(10000, timeoutCts.Token)) == getCatalogTask)
                        {
                            catalogDetail = await getCatalogTask;
                        }
                        else
                        {
                            _logger.LogWarning($"Timeout getting catalog details for {catalogId}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Error getting catalog details: {ex.Message}");
                        // Continue despite error
                    }

                    string catalogName = catalogDetail?.Name ?? "Unknown Catalog";

                    // Check if part already exists - with timeout
                    BomTreeNode existingPart = null;
                    try
                    {
                        var getPartTask = GetCatalogItemWithRetryAsync(catalogId, part.OrderingCode);
                        if (await Task.WhenAny(getPartTask, Task.Delay(10000, timeoutCts.Token)) == getPartTask)
                        {
                            existingPart = await getPartTask;
                        }
                        else
                        {
                            _logger.LogWarning($"Timeout checking if part {part.OrderingCode} exists");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Error checking if part exists: {ex.Message}");
                        // Continue despite error
                    }

                    if (existingPart != null)
                    {
                        _logger.LogInfo($"Part {part.OrderingCode} already exists in catalog {catalogId}");
                        return true;
                    }

                    // Prepare properties
                    Dictionary<string, string> properties = new Dictionary<string, string>
            {
                { "Part Number", part.OrderingCode }
            };

                    // Add supplier-specific properties if available
                    if (part.DigiKeyData != null && part.DigiKeyData.IsAvailable)
                    {
                        if (!string.IsNullOrEmpty(part.DigiKeyData.Description))
                        {
                            properties["Description"] = part.DigiKeyData.Description.Trim();
                        }

                        properties["Cost"] = part.DigiKeyData.Price.ToString("F2");

                        properties["Revision"] = DateTime.Now.ToString("dd-MM-yyyy");


                        if (!string.IsNullOrEmpty(part.DigiKeyData.LeadTime?.ToString()))
                        {
                            properties["Lead time"] = part.DigiKeyData.LeadTime.ToString();
                        }

                        if (!string.IsNullOrEmpty(part.DigiKeyData.Manufacturer))
                        {
                            properties["Manufacturer"] = part.DigiKeyData.Manufacturer.Trim();
                        }

                        properties["Vendor"] = "DIGI-KEY CORPORATION";

                        // Fix 1: Ensure catalog name is used properly
                        properties["Catalog Indicator"] = catalogName;

         

                        // Fix 3: Ensure proper Link handling - use ProductUrl for Link
                        if (!string.IsNullOrEmpty(part.DigiKeyData.ProductUrl))
                        {
                            properties["Link"] = part.DigiKeyData.ProductUrl.Trim();
                            // Add a separate Product URL field
                        }

                        // Datasheet
                        if (!string.IsNullOrEmpty(part.DigiKeyData.DatasheetUrl))
                        {
                            string dataSheetUrl = part.DigiKeyData.DatasheetUrl.Trim();
                            if (dataSheetUrl.StartsWith("//"))
                            {
                                dataSheetUrl = "https:" + dataSheetUrl;
                            }
                            properties["Data Sheet"] = $"<Link> {dataSheetUrl}";
                            _logger.LogInfo($"Setting Data Sheet URL: {properties["Data Sheet"]}");
                        }

                        properties["Quantity Available"] = part.DigiKeyData.Availability.ToString();

                        if (!string.IsNullOrEmpty(part.DigiKeyData.Category))
                        {
                            properties["Catalog supplier"] = part.DigiKeyData.Category;
                        }

                        // MOQ
                        int moq = 1;
                        if (part.DigiKeyData.PriceBreaks.Any())
                        {
                            moq = part.DigiKeyData.PriceBreaks.Min(pb => pb.Quantity);
                        }
                        properties["Minimum Order Quantity"] = moq.ToString();
                    }
                    else if (part.MouserData != null && part.MouserData.IsAvailable)
                    {
                        if (!string.IsNullOrEmpty(part.MouserData.Description))
                        {
                            properties["Description"] = part.MouserData.Description.Trim();
                        }

                        properties["Cost"] = part.MouserData.Price.ToString("F2");
                        properties["Vendor"] = "MOUSER ELECTRONICS";
                        properties["Catalog Indicator"] = catalogName;

           
                        if (!string.IsNullOrEmpty(part.MouserData.ProductUrl))
                        {
                            properties["Link"] = part.MouserData.ProductUrl.Trim();
                            // Add a separate Product URL field
                        }

                        if (!string.IsNullOrEmpty(part.MouserData.DatasheetUrl))
                        {
                            properties["Data Sheet"] = part.MouserData.DatasheetUrl.Trim();
                        }

                        properties["Quantity Available"] = part.MouserData.Availability.ToString();
                    }

                    // Log the properties for debugging
                    _logger.LogInfo($"Updating properties for part {part.OrderingCode}");
                    foreach (var prop in properties)
                    {
                        _logger.LogInfo($"Property {prop.Key}: {prop.Value}");
                    }

                    // Create part request
                    var partRequest = new OpenBomPartRequest
                    {
                        PartNumber = part.OrderingCode,
                        Properties = properties
                    };

                    // Update catalog part with timeout protection
                    try
                    {
                        var updateTask = _openBomService.UpdateCatalogPartAsync(catalogId, partRequest);
                        if (await Task.WhenAny(updateTask, Task.Delay(20000, timeoutCts.Token)) == updateTask)
                        {
                            await updateTask;
                            _logger.LogSuccess($"Successfully added part {part.OrderingCode} to catalog {catalogId}");
                        }
                        else
                        {
                            _logger.LogWarning($"Timeout updating catalog part {part.OrderingCode}");
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error updating catalog part: {ex.Message}");
                        return false;
                    }

                    // Only attempt image upload if DigiKeyService is available and we have image URL
                    if (_digiKeyService != null && part.DigiKeyData?.IsAvailable == true && !string.IsNullOrEmpty(part.DigiKeyData.ImageUrl))
                    {
                        _logger.LogInfo($"Attempting to upload image for part {part.OrderingCode}");
                        try
                        {
                            // Get image with timeout
                            var getImageTask = _digiKeyService.GetImageBytesAsync(part.DigiKeyData.ImageUrl);
                            byte[] imageBytes = null;

                            if (await Task.WhenAny(getImageTask, Task.Delay(10000, timeoutCts.Token)) == getImageTask)
                            {
                                imageBytes = await getImageTask;
                            }
                            else
                            {
                                _logger.LogWarning($"Timeout getting image for part {part.OrderingCode}");
                            }

                            // Upload image if we got it
                            if (imageBytes != null && imageBytes.Length > 0)
                            {
                                var uploadImageTask = _openBomService.UploadCatalogImageAsync(
                                    catalogId,
                                    part.OrderingCode,
                                    imageBytes,
                                    "Thumbnail image"
                                );

                                if (await Task.WhenAny(uploadImageTask, Task.Delay(15000, timeoutCts.Token)) == uploadImageTask)
                                {
                                    await uploadImageTask;
                                    _logger.LogInfo($"Successfully uploaded image for part {part.OrderingCode}");
                                }
                                else
                                {
                                    _logger.LogWarning($"Timeout uploading image for part {part.OrderingCode}");
                                }
                            }
                        }
                        catch (Exception imageEx)
                        {
                            _logger.LogWarning($"Error uploading image for part {part.OrderingCode}: {imageEx.Message}");
                            // Continue despite image error
                        }
                    }
                    var linkProperties = new Dictionary<string, string>();
                    if (part.DigiKeyData?.IsAvailable == true)
                    {
                        if (!string.IsNullOrEmpty(part.DigiKeyData.ProductUrl))
                        {
                            linkProperties["Link"] = part.DigiKeyData.ProductUrl.Trim();
                        }
                        if (!string.IsNullOrEmpty(part.DigiKeyData.DatasheetUrl))
                        {
                            string dataSheetUrl = part.DigiKeyData.DatasheetUrl.Trim();
                            if (dataSheetUrl.StartsWith("//"))
                            {
                                dataSheetUrl = "https:" + dataSheetUrl;
                            }
                            linkProperties["Data Sheet"] = $"<Link> {dataSheetUrl}";
                        }
                    }

                    if (linkProperties.Count > 0)
                    {
                        var linkUpdateRequest = new OpenBomPartRequest
                        {
                            PartNumber = part.OrderingCode,
                            Properties = linkProperties
                        };

                        await _openBomService.UpdateCatalogPartAsync(catalogId, linkUpdateRequest);
                        _logger.LogInfo($"Updated links separately for part {part.OrderingCode}");
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error adding part {part.OrderingCode} to catalog: {ex.Message}");
                    return false;
                }
            }, $"EnsurePartInCatalog-{part.OrderingCode}");
        }



        public async Task<bool> AddPartToBomWithRetryAsync(string bomId, BomEntry part)
        {
            // Clear cache for this BOM
            _responseCache.TryRemove($"bomHierarchy_{bomId}", out _);

            return await ExecuteWithRetryAsync(async () =>
            {
                // Prepare properties with BOM-specific data
                var properties = new Dictionary<string, string>
        {
            { "Part Number", part.OrderingCode }
        };

                // Add BOM-specific properties
                if (part.QuantityTotal > 0)
                {
                    properties["Quantity"] = part.QuantityTotal.ToString();
                }

                if (!string.IsNullOrEmpty(part.Designator))
                {
                    properties["Designator"] = part.Designator;
                }

                if (!string.IsNullOrEmpty(part.Value))
                {
                    properties["Value"] = part.Value;
                }

                if (!string.IsNullOrEmpty(part.PcbFootprint))
                {
                    properties["Footprint"] = part.PcbFootprint;
                }



                var partRequest = new OpenBomPartRequest
                {
                    PartNumber = part.OrderingCode,
                    Properties = properties
                };

                var parts = new List<OpenBomPartRequest> { partRequest };
                await _openBomService.UploadPartsAsync(bomId, parts);
                return true;
            }, $"AddPartToBom-{part.OrderingCode}");
        }

        public Task UpdateCatalogPartAsync(string catalogId, OpenBomPartRequest partRequest)
        {
            // Clear cache for related items
            string cacheKey = CreatePartCacheKey(catalogId, partRequest.PartNumber);
            _responseCache.TryRemove(cacheKey, out _);
            _responseCache.TryRemove($"catalog_{catalogId}", out _);

            return ExecuteWithRetryAsync(() => _openBomService.UpdateCatalogPartAsync(catalogId, partRequest),
                $"UpdateCatalogPart-{partRequest.PartNumber}");
        }

        public async Task<OpenBomDocument> CreateBomAsync(string bomName, string partNumber, string catalogId = "", string templateId = "")
        {
            // Invalidate BOM cache after creating a new BOM
            _responseCache.TryRemove("boms", out _);

            return await ExecuteWithRetryAsync(async () =>
            {
                var requestData = new
                {
                    docName = bomName,
                    bomPartNumber = partNumber,
                    catalogId = catalogId,
                    templateId = templateId
                };

                return await _openBomService.CreateBomAsync(requestData);
            }, $"CreateBom-{bomName}");
        }

        public async Task<OpenBomDocument> GetBomInfoAsync(string bomId)
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                try
                {
                    _logger.LogInfo($"Fetching BOM info for ID: {bomId}");
                    var bomInfo = await _openBomService.GetBomInfoAsync(bomId);

                    _logger.LogInfo($"Successfully retrieved BOM info: {bomInfo.Name} (Part: {bomInfo.PartNumber})");
                    return bomInfo;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error retrieving BOM info for {bomId}: {ex.Message}");
                    throw new Exception($"Failed to retrieve information for BOM {bomId}", ex);
                }
            }, $"GetBomInfo-{bomId}", $"bomInfo_{bomId}", TimeSpan.FromMinutes(5));
        }

        public async Task<bool> UploadPartsAsync(string bomId, List<OpenBomPartRequest> parts)
        {
            // Invalidate BOM hierarchy cache
            _responseCache.TryRemove($"bomHierarchy_{bomId}", out _);
            _responseCache.TryRemove($"bomInfo_{bomId}", out _);

            return await ExecuteWithRetryAsync(async () =>
            {
                try
                {
                    _logger.LogInfo($"Uploading {parts.Count} parts to BOM {bomId}");

                    const int maxPartsToLog = 3;
                    for (int i = 0; i < Math.Min(parts.Count, maxPartsToLog); i++)
                    {
                        var part = parts[i];
                        _logger.LogInfo($"  Part {i + 1}: {part.PartNumber} with {part.Properties.Count} properties");
                    }

                    var result = await _openBomService.UploadPartsAsync(bomId, parts);

                    _logger.LogInfo($"Upload results for BOM {bomId}: " +
                                   $"Total: {result.TotalParts}, " +
                                   $"Successful: {result.SuccessfulUploads}, " +
                                   $"Skipped: {result.SkippedParts}, " +
                                   $"Errors: {result.Errors.Count}");

                    if (result.Errors.Count > 0)
                    {
                        _logger.LogWarning($"Encountered {result.Errors.Count} errors during upload:");
                        foreach (var error in result.Errors.Take(5))
                        {
                            _logger.LogWarning($"  Error: {error}");
                        }

                        if (result.Errors.Count > 5)
                        {
                            _logger.LogWarning($"  ... and {result.Errors.Count - 5} more errors");
                        }
                    }

                    bool isSuccessful = result.SuccessfulUploads > 0;

                    if (isSuccessful)
                    {
                        _logger.LogSuccess($"Successfully uploaded {result.SuccessfulUploads} parts to BOM {bomId}");
                    }
                    else
                    {
                        _logger.LogError($"Failed to upload any parts to BOM {bomId}");

                        if (result.Errors.Count > 0)
                        {
                            string errorSummary = string.Join("; ", result.Errors.Take(3));
                            throw new Exception($"Upload failed: {errorSummary}");
                        }
                        else
                        {
                            throw new Exception("Upload failed with no specific error message");
                        }
                    }

                    return isSuccessful;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error in UploadPartsAsync for BOM {bomId}: {ex.Message}");
                    throw;
                }
            }, $"UploadParts-{bomId}");
        }

        // Method to clear all cache
        public void ClearCache()
        {
            _responseCache.Clear();
            _logger.LogInfo("Cache cleared");
        }

        // Method to adjust concurrency at runtime if needed
        public void SetConcurrentRequests(int concurrency)
        {
            if (concurrency < 1)
                concurrency = 1;

            int oldConcurrency = _concurrentRequests;
            _concurrentRequests = concurrency;

            // Adjust semaphore
            if (concurrency > oldConcurrency)
            {
                _throttler.Release(concurrency - oldConcurrency);
            }
            // If reducing concurrency, the semaphore will automatically adjust as permits are requested

            _logger.LogInfo($"Adjusted concurrent requests from {oldConcurrency} to {concurrency}");
        }
    }
}