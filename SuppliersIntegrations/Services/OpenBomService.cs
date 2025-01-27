using BOMVIEW.Interfaces;
using BOMVIEW.Models;
using BOMVIEW.OpenBOM.Models;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Net.Http.Headers;

public class OpenBomService
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private const string OpenBomAppKey = "6745a64065aed45611f30677";
    private const string OpenBomUsername = "eliana@testview.co.il";
    private const string OpenBomPassword = "Openbom2025";
    private const string OpenBomBaseUrl = "https://developer-api.openbom.com";
    private string _accessToken;
    private Dictionary<string, string> _accessTokenCache = new();
    private DateTime _tokenExpiry = DateTime.MinValue;


    public OpenBomService(ILogger logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(10); // Increase timeout to 3 minutes

    }




    private async Task EnsureAuthenticatedAsync()
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry)
        {
            // Use existing token if it's still valid
            return;
        }

        try
        {
            _logger.LogInfo("Obtaining new OpenBOM access token");
            var loginUrl = $"{OpenBomBaseUrl}/login";
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-OpenBOM-AppKey", OpenBomAppKey);

            var requestBody = new { username = OpenBomUsername, password = OpenBomPassword };
            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync(loginUrl, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Authentication failed: {response.StatusCode}");
            }

            var authResponse = JsonSerializer.Deserialize<OpenBomAuthResponse>(responseContent);
            _accessToken = authResponse?.AccessToken;

            if (string.IsNullOrEmpty(_accessToken))
            {
                throw new Exception("Failed to retrieve access token");
            }

            // Set token expiry to 1 hour from now (adjust based on actual token lifetime)
            _tokenExpiry = DateTime.UtcNow.AddHours(1);

            _logger.LogSuccess("Successfully authenticated with OpenBOM");
        }
        catch (Exception ex)
        {
            _logger.LogError($"OpenBOM authentication error: {ex.Message}");
            throw;
        }
    }

    // In OpenBomService.cs
    public async Task<List<OpenBomListItem>> ListTopLevelBomsAsync()
    {
        try
        {
            await EnsureAuthenticatedAsync();
            var url = $"{OpenBomBaseUrl}/toplevelboms";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-OpenBOM-AppKey", OpenBomAppKey);
            request.Headers.Add("X-OpenBOM-AccessToken", _accessToken);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var content = await response.Content.ReadAsStringAsync();

            return JsonSerializer.Deserialize<List<OpenBomListItem>>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error: {ex.Message}");
            return new List<OpenBomListItem>();
        }
    }

    public async Task<OpenBomUploadResult> UploadOrderingCodesAsync(string cardId, IEnumerable<BomEntry> entries)
    {
        var result = new OpenBomUploadResult
        {
            TotalParts = entries.Count()
        };

        try
        {
            await EnsureAuthenticatedAsync();

            var encodedCardId = Uri.EscapeDataString(cardId);
            var url = $"{OpenBomBaseUrl}/bom/{encodedCardId}/parts";

            // Prepare the request parts
            var parts = entries.Select(entry => new OpenBomPartRequest
            {
                PartNumber = entry.OrderingCode,
                Properties = new Dictionary<string, string>
                {
                    { "Part Number", entry.OrderingCode }
                }
            }).ToList();

            // Setup request
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-OpenBOM-AppKey", OpenBomAppKey);
            _httpClient.DefaultRequestHeaders.Add("X-OpenBOM-AccessToken", _accessToken);

            var content = new StringContent(
                JsonSerializer.Serialize(parts),
                Encoding.UTF8,
                "application/json"
            );

            _logger.LogInfo($"Uploading {parts.Count} parts to OpenBOM");
            _logger.LogInfo($"Request URL: {url}");
            _logger.LogInfo($"Request Content: {await content.ReadAsStringAsync()}");

            var response = await _httpClient.PutAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            _logger.LogInfo($"Response Status: {response.StatusCode}");
            _logger.LogInfo($"Response Content: {responseContent}");

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Upload failed: {response.StatusCode} - {responseContent}");
            }

            // Parse response to track successes and failures
            var uploadResponses = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(responseContent);

            foreach (var uploadResponse in uploadResponses)
            {
                if (uploadResponse.ContainsValue("OK"))
                {
                    result.SuccessfulUploads++;
                }
                else
                {
                    var partNumber = uploadResponse.Keys.FirstOrDefault();
                    result.Errors.Add($"Failed to upload part: {partNumber}");
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error uploading to OpenBOM: {ex.Message}");
            result.Errors.Add(ex.Message);
            return result;
        }
    }

    public async Task<bool> ValidateCardIdAsync(string cardId)
    {
        try
        {
            await EnsureAuthenticatedAsync();

            var encodedCardId = Uri.EscapeDataString(cardId);
            var url = $"{OpenBomBaseUrl}/bom/{encodedCardId}";

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-OpenBOM-AppKey", OpenBomAppKey);
            _httpClient.DefaultRequestHeaders.Add("X-OpenBOM-AccessToken", _accessToken);

            var response = await _httpClient.GetAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error validating card ID: {ex.Message}");
            return false;
        }
    }

    public async Task<List<OpenBomListItem>> ListBomsAsync()
    {
        try
        {
            await EnsureAuthenticatedAsync();

            var url = $"{OpenBomBaseUrl}/boms";
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-OpenBOM-AppKey", OpenBomAppKey);
            _httpClient.DefaultRequestHeaders.Add("X-OpenBOM-AccessToken", _accessToken);

            _logger.LogInfo($"Fetching BOM list from: {url}");

            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            _logger.LogInfo($"Response Status: {response.StatusCode}");
            _logger.LogInfo($"Response Content: {content}");

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to get BOM list: {response.StatusCode} - {content}");
            }

            return JsonSerializer.Deserialize<List<OpenBomListItem>>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error listing BOMs: {ex.Message}");
            throw;
        }
    }


    public async Task<List<BomTreeNode>> GetBomHierarchyAsync(string bomId)
    {
        try
        {
            await EnsureAuthenticatedAsync();

            _logger.LogInfo($"Fetching hierarchy for BOM: {bomId}");

            // Using the working endpoint
            var url = $"{OpenBomBaseUrl}/bom/{Uri.EscapeDataString(bomId)}";
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-OpenBOM-AppKey", OpenBomAppKey);
            _httpClient.DefaultRequestHeaders.Add("X-OpenBOM-AccessToken", _accessToken);

            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            _logger.LogInfo($"Response Status: {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to get BOM details: {response.StatusCode} - {content}");
                return new List<BomTreeNode>();
            }

            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var bomDetails = JsonSerializer.Deserialize<OpenBomDocument>(content, options);

                if (bomDetails?.Columns == null || bomDetails.Cells == null)
                {
                    _logger.LogError("Invalid BOM document structure");
                    return new List<BomTreeNode>();
                }

                _logger.LogInfo($"Found columns: {string.Join(", ", bomDetails.Columns)}");

                var children = new List<BomTreeNode>();

                // Get the column indexes for important fields
                int partNumberIndex = bomDetails.Columns.IndexOf("Part Number");
                int nameIndex = bomDetails.Columns.IndexOf("Name");
                int descriptionIndex = bomDetails.Columns.IndexOf("Description");

                _logger.LogInfo($"Column indices - PartNumber: {partNumberIndex}, Name: {nameIndex}, Description: {descriptionIndex}");

                foreach (var row in bomDetails.Cells)
                {
                    try
                    {
                        if (row == null || row.Count == 0)
                        {
                            _logger.LogWarning("Skipping empty row");
                            continue;
                        }

                        string partNumber = partNumberIndex >= 0 && partNumberIndex < row.Count
                            ? row[partNumberIndex]?.ToString()
                            : null;

                        string name = nameIndex >= 0 && nameIndex < row.Count
                            ? row[nameIndex]?.ToString()
                            : null;

                        string description = descriptionIndex >= 0 && descriptionIndex < row.Count
                            ? row[descriptionIndex]?.ToString()
                            : null;

                        string displayName = string.Join(" - ", new[] { partNumber, name, description }
                            .Where(s => !string.IsNullOrEmpty(s)));

                        if (string.IsNullOrEmpty(displayName))
                        {
                            _logger.LogWarning($"Skipping row due to empty display name. PartNumber: {partNumber}, Name: {name}");
                            continue;
                        }

                        var node = new BomTreeNode
                        {
                            Id = partNumber ?? Guid.NewGuid().ToString(),
                            Name = displayName,
                            Type = "file",  // Default to file type
                            IsBom = false,
                            HasUnloadedChildren = false
                        };

                        // Check if this node might have children (optional)
                        try
                        {
                            if (!string.IsNullOrEmpty(partNumber))
                            {
                                var hasChildren = await ValidateCardIdAsync(partNumber);
                                if (hasChildren)
                                {
                                    node.Type = "folder";
                                    node.IsBom = true;
                                    node.HasUnloadedChildren = true;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Error checking for children of {partNumber}: {ex.Message}");
                        }

                        children.Add(node);
                        _logger.LogInfo($"Added node: {node.Name} ({node.Type})");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error processing row: {ex.Message}");
                    }
                }

                _logger.LogInfo($"Successfully loaded {children.Count} items from BOM {bomId}");
                return children;
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError($"JSON deserialization error: {jsonEx.Message}");
                _logger.LogError($"Response content that failed to parse: {content}");
                return new List<BomTreeNode>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in GetBomHierarchyAsync: {ex.Message}");
            return new List<BomTreeNode>();
        }
    }


    public async Task<List<OpenBomListItem>> ListCatalogsAsync()
    {
        try
        {
            await EnsureAuthenticatedAsync();
            var url = $"{OpenBomBaseUrl}/catalogs";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-OpenBOM-AppKey", OpenBomAppKey);
            request.Headers.Add("X-OpenBOM-AccessToken", _accessToken);

            using var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            _logger.LogInfo($"Catalog Response: {content}");

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to get catalogs: {response.StatusCode} - {content}");
            }

            return JsonSerializer.Deserialize<List<OpenBomListItem>>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error listing catalogs: {ex.Message}");
            return new List<OpenBomListItem>();
        }
    }

    public async Task<OpenBomDocument> GetCatalogAsync(string catalogId)
    {
        try
        {
            await EnsureAuthenticatedAsync();
            var url = $"{OpenBomBaseUrl}/catalog/{Uri.EscapeDataString(catalogId)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-OpenBOM-AppKey", OpenBomAppKey);
            request.Headers.Add("X-OpenBOM-AccessToken", _accessToken);

            using var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            _logger.LogInfo($"Catalog Document Response: {content}");

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to get catalog document: {response.StatusCode} - {content}");
            }

            return JsonSerializer.Deserialize<OpenBomDocument>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting catalog document: {ex.Message}");
            throw;
        }
    }

    public async Task<List<BomTreeNode>> GetCatalogHierarchyAsync(string catalogId)
    {
        try
        {
            await EnsureAuthenticatedAsync();
            var catalog = await GetCatalogAsync(catalogId);
            var children = new List<BomTreeNode>();

            if (catalog?.Columns == null || catalog.Cells == null)
            {
                _logger.LogError("Invalid catalog document structure");
                return children;
            }

            // Get column indices
            int partNumberIndex = catalog.Columns.IndexOf("Part Number");
            int nameIndex = catalog.Columns.IndexOf("Name");
            int descriptionIndex = catalog.Columns.IndexOf("Description");

            foreach (var row in catalog.Cells)
            {
                try
                {
                    if (row == null || row.Count == 0) continue;

                    string partNumber = partNumberIndex >= 0 && partNumberIndex < row.Count
                        ? row[partNumberIndex]?.ToString()
                        : null;

                    string name = nameIndex >= 0 && nameIndex < row.Count
                        ? row[nameIndex]?.ToString()
                        : null;

                    string description = descriptionIndex >= 0 && descriptionIndex < row.Count
                        ? row[descriptionIndex]?.ToString()
                        : null;

                    string displayName = string.Join(" - ", new[] { partNumber, name, description }
                        .Where(s => !string.IsNullOrEmpty(s)));

                    if (string.IsNullOrEmpty(displayName)) continue;

                    var node = new BomTreeNode
                    {
                        Id = catalogId,  // Use the catalog ID here
                        Name = displayName,
                        Type = "file",
                        TreeNodeType = BomTreeNode.NodeType.Item,
                        PartNumber = partNumber,
                        Description = description,
                        HasUnloadedChildren = false
                    };

                    // Add all properties to the node
                    for (int i = 0; i < catalog.Columns.Count; i++)
                    {
                        if (i < row.Count && row[i] != null)
                        {
                            node.Properties[catalog.Columns[i]] = row[i].ToString();
                        }
                    }

                    children.Add(node);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error processing catalog row: {ex.Message}");
                }
            }

            return children;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in GetCatalogHierarchyAsync: {ex.Message}");
            return new List<BomTreeNode>();
        }
    }

    public async Task<OpenBomDocument> GetCatalogItemByPartNumberAsync(string catalogId, string partNumber)
    {
        try
        {
            await EnsureAuthenticatedAsync();
            var url = $"{OpenBomBaseUrl}/catalog/{Uri.EscapeDataString(catalogId)}/item?partNumber={Uri.EscapeDataString(partNumber)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-OpenBOM-AppKey", OpenBomAppKey);
            request.Headers.Add("X-OpenBOM-AccessToken", _accessToken);

            using var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to get catalog item: {response.StatusCode} - {content}");
                throw new Exception($"Failed to get catalog item: {response.StatusCode}");
            }

            return JsonSerializer.Deserialize<OpenBomDocument>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting catalog item: {ex.Message}");
            throw;
        }
    }

    public async Task RemovePartFromCatalogAsync(string catalogId, string partNumber)
    {
        try
        {
            await EnsureAuthenticatedAsync();
            var url = $"{OpenBomBaseUrl}/catalog/{Uri.EscapeDataString(catalogId)}/removepart?partNumber={Uri.EscapeDataString(partNumber)}";

            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Add("X-OpenBOM-AppKey", OpenBomAppKey);
            request.Headers.Add("X-OpenBOM-AccessToken", _accessToken);

            using var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to remove part: {response.StatusCode} - {content}");
                throw new Exception($"Failed to remove part: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error removing part from catalog: {ex.Message}");
            throw;
        }
    }

    public async Task UploadCatalogImageAsync(string catalogId, string partNumber, byte[] imageData, string imageProperty)
    {
        try
        {
            await EnsureAuthenticatedAsync();

            // Log the incoming parameters
            _logger.LogInfo($"Uploading image for Catalog: {catalogId}, Part: {partNumber}");

            var url = $"{OpenBomBaseUrl}/catalog/{Uri.EscapeDataString(catalogId)}/image";

            using var content = new MultipartFormDataContent();

            // Add the image file with a unique boundary
            var imageContent = new ByteArrayContent(imageData);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            content.Add(imageContent, "file", $"{partNumber}_image.jpg");

            // Add part information with explicit content types
            var partNumberContent = new StringContent(partNumber);
            partNumberContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            content.Add(partNumberContent, "partNumber");

            var propertyContent = new StringContent(imageProperty);
            propertyContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            content.Add(propertyContent, "imageProperty");

            // Add catalog ID to form data explicitly
            var catalogIdContent = new StringContent(catalogId);
            catalogIdContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            content.Add(catalogIdContent, "catalogId");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-OpenBOM-AppKey", OpenBomAppKey);
            _httpClient.DefaultRequestHeaders.Add("X-OpenBOM-AccessToken", _accessToken);

            // Log the request URL and headers
            _logger.LogInfo($"Request URL: {url}");
            _logger.LogInfo("Request Headers:");
            foreach (var header in _httpClient.DefaultRequestHeaders)
            {
                _logger.LogInfo($"{header.Key}: {string.Join(", ", header.Value)}");
            }

            var response = await _httpClient.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            // Log the response
            _logger.LogInfo($"Response Status: {response.StatusCode}");
            _logger.LogInfo($"Response Content: {responseContent}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to upload image: {response.StatusCode} - {responseContent}");
                throw new Exception($"Failed to upload image: {response.StatusCode} - {responseContent}");
            }

            // Clear any cached data that might be related to this catalog
            if (_accessTokenCache.ContainsKey(catalogId))
            {
                _accessTokenCache.Remove(catalogId);
            }

            _logger.LogSuccess($"Successfully uploaded image for part {partNumber} in catalog {catalogId}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error uploading catalog image: {ex.Message}");
            if (ex.InnerException != null)
            {
                _logger.LogError($"Inner Exception: {ex.InnerException.Message}");
            }
            throw;
        }
    }

    public async Task UpdateCatalogPartAsync(string catalogId, OpenBomPartRequest request)
    {
        try
        {
            // Force a fresh authentication
            _accessToken = null;
            await EnsureAuthenticatedAsync();

            var url = $"{OpenBomBaseUrl}/catalog/{Uri.EscapeDataString(catalogId)}/parts";

            // Log the request payload
            _logger.LogInfo($"Updating catalog part with payload:");
            _logger.LogInfo($"Catalog ID: {catalogId}");
            _logger.LogInfo($"Part Number: {request.PartNumber}");
            _logger.LogInfo("Properties:");
            foreach (var prop in request.Properties)
            {
                _logger.LogInfo($"  {prop.Key}: {prop.Value}");
            }

            var jsonString = JsonSerializer.Serialize(
                new[] { request },
                new JsonSerializerOptions { WriteIndented = true }
            );
            _logger.LogInfo($"Request JSON: {jsonString}");

            var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-OpenBOM-AppKey", OpenBomAppKey);
            _httpClient.DefaultRequestHeaders.Add("X-OpenBOM-AccessToken", _accessToken);

            var response = await _httpClient.PutAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            // Log the response
            _logger.LogInfo($"Response Status: {response.StatusCode}");
            _logger.LogInfo($"Response Content: {responseContent}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to update part: {response.StatusCode} - {responseContent}");
                throw new Exception($"Failed to update part: {response.StatusCode} - {responseContent}");
            }

            // Try to parse the response to verify the update
            if (!string.IsNullOrEmpty(responseContent))
            {
                try
                {
                    var responseObject = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    if (responseObject.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in responseObject.EnumerateArray())
                        {
                            if (item.TryGetProperty(request.PartNumber, out var status))
                            {
                                _logger.LogInfo($"Update status for part {request.PartNumber}: {status}");
                            }
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning($"Could not parse response JSON: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error updating catalog part: {ex.Message}");
            if (ex.InnerException != null)
            {
                _logger.LogError($"Inner Exception: {ex.InnerException.Message}");
            }
            throw;
        }
    }

    public async Task<OpenBomUploadResult> UploadPartsAsync(string cardId, IEnumerable<OpenBomPartRequest> parts)
    {
        var result = new OpenBomUploadResult
        {
            TotalParts = parts.Count()
        };

        try
        {
            await EnsureAuthenticatedAsync();

            var encodedCardId = Uri.EscapeDataString(cardId);
            var url = $"{OpenBomBaseUrl}/bom/{encodedCardId}/parts";

            // Setup request
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-OpenBOM-AppKey", OpenBomAppKey);
            _httpClient.DefaultRequestHeaders.Add("X-OpenBOM-AccessToken", _accessToken);

            var content = new StringContent(
                JsonSerializer.Serialize(parts),
                Encoding.UTF8,
                "application/json"
            );

            _logger.LogInfo($"Uploading {parts.Count()} parts to OpenBOM");
            _logger.LogInfo($"Request URL: {url}");
            _logger.LogInfo($"Request Content: {await content.ReadAsStringAsync()}");

            var response = await _httpClient.PutAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            _logger.LogInfo($"Response Status: {response.StatusCode}");
            _logger.LogInfo($"Response Content: {responseContent}");

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Upload failed: {response.StatusCode} - {responseContent}");
            }

            // Parse response to track successes and failures
            var uploadResponses = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(responseContent);

            foreach (var uploadResponse in uploadResponses)
            {
                if (uploadResponse.ContainsValue("OK"))
                {
                    result.SuccessfulUploads++;
                }
                else
                {
                    var partNumber = uploadResponse.Keys.FirstOrDefault();
                    result.Errors.Add($"Failed to upload part: {partNumber}");
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error uploading to OpenBOM: {ex.Message}");
            result.Errors.Add(ex.Message);
            return result;
        }
    }


    // Add to OpenBomService.cs
    public async Task<OpenBomDocument> CreateBomAsync(object requestData)
    {
        try
        {
            await EnsureAuthenticatedAsync();
            var url = $"{OpenBomBaseUrl}/bom/create";

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-OpenBOM-AppKey", OpenBomAppKey);
            _httpClient.DefaultRequestHeaders.Add("X-OpenBOM-AccessToken", _accessToken);

            var content = new StringContent(
                JsonSerializer.Serialize(requestData),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to create BOM: {response.StatusCode} - {responseContent}");
                throw new Exception($"Failed to create BOM: {response.StatusCode} - {responseContent}");
            }

            return JsonSerializer.Deserialize<OpenBomDocument>(responseContent);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error creating BOM: {ex.Message}");
            throw;
        }
    }

    public async Task<OpenBomDocument> GetBomInfoAsync(string bomId)
    {
        try
        {
            await EnsureAuthenticatedAsync();
            var url = $"{OpenBomBaseUrl}/bom/{Uri.EscapeDataString(bomId)}";

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-OpenBOM-AppKey", OpenBomAppKey);
            _httpClient.DefaultRequestHeaders.Add("X-OpenBOM-AccessToken", _accessToken);

            _logger.LogInfo($"Getting BOM info for: {bomId}");
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to get BOM info: {response.StatusCode} - {content}");

                // Return a default document instead of throwing an exception
                return new OpenBomDocument
                {
                    Id = bomId,
                    Name = $"BOM {bomId}",
                    PartNumber = bomId,
                    Columns = new List<string>(),
                    Cells = new List<List<object>>()
                };
            }

            // Check if content is empty or not JSON
            if (string.IsNullOrWhiteSpace(content) || !IsValidJson(content))
            {
                _logger.LogWarning($"BOM API returned non-JSON response for {bomId}: {content}");

                // Return a default document
                return new OpenBomDocument
                {
                    Id = bomId,
                    Name = $"BOM {bomId}",
                    PartNumber = bomId,
                    Columns = new List<string>(),
                    Cells = new List<List<object>>()
                };
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var bomDoc = JsonSerializer.Deserialize<OpenBomDocument>(content, options);

            // Handle null response
            if (bomDoc == null)
            {
                _logger.LogWarning($"Deserialized BOM document is null for {bomId}");
                return new OpenBomDocument
                {
                    Id = bomId,
                    Name = $"BOM {bomId}",
                    PartNumber = bomId,
                    Columns = new List<string>(),
                    Cells = new List<List<object>>()
                };
            }

            // Ensure essential properties
            bomDoc.Id = bomDoc.Id ?? bomId;
            bomDoc.Name = bomDoc.Name ?? $"BOM {bomId}";
            bomDoc.Columns = bomDoc.Columns ?? new List<string>();
            bomDoc.Cells = bomDoc.Cells ?? new List<List<object>>();

            // Try to extract part number from cells if available
            if (bomDoc.Cells != null && bomDoc.Cells.Count > 0 && bomDoc.Columns != null)
            {
                int partNumberIndex = bomDoc.Columns.IndexOf("Part Number");
                if (partNumberIndex >= 0 && bomDoc.Cells[0].Count > partNumberIndex)
                {
                    bomDoc.PartNumber = bomDoc.Cells[0][partNumberIndex]?.ToString();
                }
            }

            // If we still don't have a part number, use the BOM ID as a fallback
            if (string.IsNullOrEmpty(bomDoc.PartNumber))
            {
                bomDoc.PartNumber = bomId;
            }

            _logger.LogInfo($"Successfully retrieved BOM info for {bomId}");
            return bomDoc;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting BOM info: {ex.Message}");

            // Return a default document instead of re-throwing
            return new OpenBomDocument
            {
                Id = bomId,
                Name = $"BOM {bomId}",
                PartNumber = bomId,
                Columns = new List<string>(),
                Cells = new List<List<object>>()
            };
        }
    }

    private bool IsValidJson(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        content = content.Trim();

        // Check if it starts with either { or [ which indicates JSON
        if (!content.StartsWith("{") && !content.StartsWith("["))
            return false;

        try
        {
            // Try to parse as JsonDocument
            using (JsonDocument.Parse(content))
            {
                return true;
            }
        }
        catch
        {
            return false;
        }
    }



}

public class OpenBomDocument
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("columns")]
        public List<string> Columns { get; set; }

        [JsonPropertyName("cells")]
        public List<List<object>> Cells { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("modifiedDate")]
        public string ModifiedDate { get; set; }

        [JsonPropertyName("createdDate")]
        public string CreatedDate { get; set; }

        [JsonPropertyName("modifiedBy")]
        public string ModifiedBy { get; set; }

        [JsonPropertyName("createdBy")]
        public string CreatedBy { get; set; }

    [JsonPropertyName("partNumber")]
    public string PartNumber { get; set; }
}


