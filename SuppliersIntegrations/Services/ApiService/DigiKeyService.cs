using BOMVIEW.Interfaces;
using BOMVIEW.Models;
using BOMVIEW.Services;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;
using BOMVIEW.Exceptions;
using System.Text;
using System.Reflection;
using System.Linq;

public class DigiKeyService : BaseSupplierService, ISupplierService
{
    private const string BASE_URL = "https://api.digikey.com/v1";
    private string _accessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;
    private readonly ApiCredentials _credentials;
    private readonly Dictionary<string, SupplierData> _cache = new();
    private string _authorizationCode;
    private string _productAccessToken;
    private string _listsAccessToken;
    private DateTime _productTokenExpiry;
    private DateTime _listsTokenExpiry;

 


    public void SetAuthorizationCode(string code)
    {
        _authorizationCode = code;
    }

    // Add constants for locale settings
    private const string LOCALE = "en";  
    private const string CURRENCY = "USD";
    private const string COUNTRY = "IL";   

    public SupplierType SupplierType => SupplierType.DigiKey;


    public DigiKeyService(ILogger logger, ApiCredentials credentials) : base(logger)
    {
        _credentials = credentials;
        _accessToken = null;
        _tokenExpiry = DateTime.MinValue;
    }

    private async Task EnsureAuthenticatedAsync()
    {
        try
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry)
                return;





            _logger.LogInfo("Getting new DigiKey access token...");

            var formData = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", _credentials.DigiKeyClientId),
                new KeyValuePair<string, string>("client_secret", _credentials.DigiKeyClientSecret),
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-DIGIKEY-Client-Id", _credentials.DigiKeyClientId);

            var response = await _httpClient.PostAsync(
                "https://api.digikey.com/v1/oauth2/token",
                formData
            );

            var responseContent = await response.Content.ReadAsStringAsync();
            HandleHttpError(response, responseContent);

            var tokenResponse = JsonSerializer.Deserialize<DigiKeyTokenResponse>(
                responseContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            _accessToken = tokenResponse.AccessToken;
            _tokenExpiry = DateTime.UtcNow.AddHours(1);

            _logger.LogSuccess("Successfully obtained DigiKey access token");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Authentication error: {ex.Message}");
            throw;
        }
        _tokenExpiry = DateTime.UtcNow.AddHours(1);
    }

    public async Task<SupplierData> GetPriceAndAvailabilityAsync(string partNumber)
    {
        try
        {
            await EnsureAuthenticatedAsync();

            // Keep the original URL
            string url = $"https://api.digikey.com/products/v4/search/{partNumber}/productdetails";
            url += $"?locale={LOCALE}&currency={CURRENCY}&country={COUNTRY}";

            _logger.LogInfo($"Making request to DigiKey API: {url}");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            _httpClient.DefaultRequestHeaders.Add("X-DIGIKEY-Client-Id", _credentials.DigiKeyClientId);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Add locale headers
            _httpClient.DefaultRequestHeaders.Add("X-DIGIKEY-Locale-Site", COUNTRY);
            _httpClient.DefaultRequestHeaders.Add("X-DIGIKEY-Locale-Language", LOCALE);
            _httpClient.DefaultRequestHeaders.Add("X-DIGIKEY-Locale-Currency", CURRENCY);
            _httpClient.DefaultRequestHeaders.Add("X-DIGIKEY-Customer-Id", "0");

            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"DigiKey API error for {partNumber}: {response.StatusCode}");
                return new SupplierData { Price = 0, Availability = 0, IsAvailable = false };
            }

            var digiKeyResponse = JsonSerializer.Deserialize<DigiKeyProductResponse>(content);

            if (digiKeyResponse?.Product == null)
            {
                _logger.LogWarning($"No product data found for {partNumber}");
                return new SupplierData { Price = 0, Availability = 0, IsAvailable = false };
            }

            string digiKeyPartNumber = "";
            DigiKeyProductVariation selectedVariation = null;

            if (digiKeyResponse.Product?.ProductVariations != null && digiKeyResponse.Product.ProductVariations.Any())
            {
                // Log product variations to assist with debugging
                _logger.LogInfo($"Found {digiKeyResponse.Product.ProductVariations.Count} product variations for {partNumber}");
                foreach (var variation in digiKeyResponse.Product.ProductVariations)
                {
                    _logger.LogInfo($"Variation: {variation.DigiKeyProductNumber}, " +
                                    $"PackageType: {variation.PackagingType ?? "Unknown"}, " +
                                    $"MOQ: {variation.MinimumOrderQuantity}, " +
                                    $"Qty Available: {variation.QuantityAvailableforPackageType}, " +
                                    $"Price Breaks: {(variation.StandardPricing?.Count ?? 0)}");
                    
                    // Log the price breaks for each variation to help with debugging
                    if (variation.StandardPricing != null && variation.StandardPricing.Any())
                    {
                        foreach (var priceBreak in variation.StandardPricing)
                        {
                            _logger.LogInfo($"  Price Break: Qty {priceBreak.BreakQuantity}, Unit Price {priceBreak.UnitPrice:C}");
                        }
                    }
                }

                // Try to find the Cut Tape (CT) variation first
                // In DigiKey API, CT products often have "CT" in their name or have a specific packaging type identifier
                selectedVariation = digiKeyResponse.Product.ProductVariations
                    .FirstOrDefault(v => 
                        (v.DigiKeyProductNumber != null && v.DigiKeyProductNumber.Contains("CT")) || 
                        (v.PackagingType != null && (
                            v.PackagingType.Contains("Cut Tape") || 
                            v.PackagingType.Contains("CT") || 
                            v.PackagingType.Contains("Tape & Reel (Cut Tape)")
                        )) ||
                        (v.MinimumOrderQuantity <= 10)); // Cut Tape typically has lower MOQ
                        
                // If no CT variation found, use the one with the lowest MOQ (which is likely CT or similar small quantity option)
                if (selectedVariation == null)
                {
                    selectedVariation = digiKeyResponse.Product.ProductVariations
                        .OrderBy(v => v.MinimumOrderQuantity)
                        .FirstOrDefault();
                }

                // If still no selection, just use the first one (this is the original behavior as fallback)
                if (selectedVariation == null && digiKeyResponse.Product.ProductVariations.Any())
                {
                    selectedVariation = digiKeyResponse.Product.ProductVariations[0];
                }

                if (selectedVariation != null)
                {
                    digiKeyPartNumber = selectedVariation.DigiKeyProductNumber;
                    _logger.LogInfo($"Selected variation: {digiKeyPartNumber} with MOQ: {selectedVariation.MinimumOrderQuantity}");
                }
            }

            return new SupplierData
            {
                ProductUrl = digiKeyResponse.Product.ProductUrl ?? $"https://www.digikey.co.il/en/products/result?keywords={partNumber}",
                Price = digiKeyResponse.Product.UnitPrice,
                Availability = (int)digiKeyResponse.Product.QuantityAvailable,
                IsAvailable = digiKeyResponse.Product.IsAvailable,
                Description = digiKeyResponse.Product.Description?.ProductDescription,
                Manufacturer = digiKeyResponse.Product.Manufacturer?.Name,
                LeadTime = digiKeyResponse.Product.ManufacturerLeadWeeks,
                ImageUrl = digiKeyResponse.Product.PhotoUrl,
                DatasheetUrl = digiKeyResponse.Product.DatasheetUrl,
                Category = digiKeyResponse.Product.Category?.Name,
                DigiKeyPartNumber = digiKeyPartNumber,
                Parameters = digiKeyResponse.Product.Parameters?.Select(p => new DigiKeyParameterInfo
                {
                    Name = p.ParameterText,
                    Value = p.ValueText
                }).ToList() ?? new List<DigiKeyParameterInfo>(),
                // Use the selected variation's price breaks instead of always using the first one
                PriceBreaks = selectedVariation?.StandardPricing?
                    .Select(p => new PriceBreak
                    {
                        Quantity = p.BreakQuantity,
                        UnitPrice = p.UnitPrice
                    }).ToList() ?? new List<PriceBreak>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error retrieving data for {partNumber}: {ex.Message}");
            return new SupplierData { Price = 0, Availability = 0, IsAvailable = false };
        }
    }




    private void CheckRateLimit(HttpResponseMessage response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            throw new RateLimitException(SupplierType, "API rate limit exceeded");
        }
    }


    public async Task<string> CreateListAsync(string listName, bool autoCorrectQty, bool attritionEnabled, bool autoPopulateCref)
    {
        try
        {
            await EnsureAuthenticatedAsync();

            var request = new DigiKeyListRequest
            {
                ListName = listName,
                CreatedBy = "BOM Price Comparison Tool",
                Source = "other",
                ListSettings = new DigiKeyListSettings
                {
                    InternalOnly = false,
                    Visibility = "Private",
                    PackagePreference = "CutTapeOrTR",
                    ColumnPreferences = new[]
                    {
                    new DigiKeyColumnPreference { ColumnName = "MfrPartNumber", IsVisible = true },
                    new DigiKeyColumnPreference { ColumnName = "CustomerReference", IsVisible = true },
                    new DigiKeyColumnPreference { ColumnName = "ReferenceDesignator", IsVisible = true },
                    new DigiKeyColumnPreference { ColumnName = "OrderQuantity", IsVisible = true }
                },
                    AutoCorrectQuantities = autoCorrectQty,
                    AttritionEnabled = attritionEnabled,
                    AutoPopulateCref = autoPopulateCref
                }
            };

            // Exactly match the header setup from GetPriceAndAvailabilityAsync
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            _httpClient.DefaultRequestHeaders.Add("X-DIGIKEY-Client-Id", _credentials.DigiKeyClientId);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Add the same locale headers used in GetPriceAndAvailabilityAsync
            _httpClient.DefaultRequestHeaders.Add("X-DIGIKEY-Locale-Site", COUNTRY);
            _httpClient.DefaultRequestHeaders.Add("X-DIGIKEY-Locale-Language", LOCALE);
            _httpClient.DefaultRequestHeaders.Add("X-DIGIKEY-Locale-Currency", CURRENCY);
            _httpClient.DefaultRequestHeaders.Add("X-DIGIKEY-Customer-Id", "0");

            var jsonContent = JsonSerializer.Serialize(request);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // Use the complete URL like in GetPriceAndAvailabilityAsync
            var response = await _httpClient.PostAsync(
                "https://api.digikey.com/mylists/v1/lists",
                content
            );

            var responseContent = await response.Content.ReadAsStringAsync();
            HandleHttpError(response, responseContent);

            var listResponse = JsonSerializer.Deserialize<DigiKeyListResponse>(responseContent);

            if (listResponse?.ListId == null)
            {
                throw new Exception($"Failed to create list: {responseContent}");
            }

            return listResponse.ListId;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error creating DigiKey list: {ex.Message}");
            throw;
        }
    }
    public async Task AddPartsToListAsync(string listId, IEnumerable<BomEntry> entries)
    {
        try
        {
            await EnsureListsAuthenticatedAsync();

            var parts = entries.Select(e => new
            {
                MfrPartNumber = e.OrderingCode,
                CustomerReference = e.Designator,
                ReferenceDesignator = e.Designator,
                OrderQuantity = e.QuantityTotal
            }).ToList();

            var jsonContent = JsonSerializer.Serialize(new { Parts = parts });
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            _httpClient.DefaultRequestHeaders.Add("X-DIGIKEY-Client-Id", _credentials.DigiKeyClientId);

            var response = await _httpClient.PostAsync(
                $"https://api.digikey.com/mylists/v1/lists/{listId}/parts",
                content
            );

            var responseContent = await response.Content.ReadAsStringAsync();
            HandleHttpError(response, responseContent);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error adding parts to DigiKey list: {ex.Message}");
            throw;
        }
    }

    // Add to DigiKeyService.cs
    private async Task EnsureListsAuthenticatedAsync()
    {
        // Check if we have a valid lists token
        if (!string.IsNullOrEmpty(_listsAccessToken) && DateTime.UtcNow < _listsTokenExpiry)
        {
            return;
        }

        if (string.IsNullOrEmpty(_authorizationCode))
        {
            throw new InvalidOperationException(
                "Authorization code not set. Please authenticate with DigiKey first by visiting: " +
                $"https://api.digikey.com/v1/oauth2/authorize?response_type=code&client_id={_credentials.DigiKeyClientId}" +
                $"&redirect_uri={Uri.EscapeDataString(_credentials.RedirectUri)}"
            );
        }

        _logger.LogInfo("Getting new DigiKey MyLists access token...");

        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", _credentials.DigiKeyClientId),
            new KeyValuePair<string, string>("client_secret", _credentials.DigiKeyClientSecret),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", _authorizationCode),
            new KeyValuePair<string, string>("redirect_uri", _credentials.RedirectUri)
        });

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("X-DIGIKEY-Client-Id", _credentials.DigiKeyClientId);

        var response = await _httpClient.PostAsync(
            "https://api.digikey.com/v1/oauth2/token",
            formData
        );

        var responseContent = await response.Content.ReadAsStringAsync();
        HandleHttpError(response, responseContent);

        var tokenResponse = JsonSerializer.Deserialize<DigiKeyTokenResponse>(
            responseContent,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        _listsAccessToken = tokenResponse.AccessToken;
        _listsTokenExpiry = DateTime.UtcNow.AddHours(1);
    }

    public async Task<byte[]> GetImageBytesAsync(string imageUrl)
    {
        try
        {
            // Create a new HttpClient with specific configuration for image downloads
            using var imageClient = new HttpClient();

            // Add headers that might be needed to access DigiKey images
            imageClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
            imageClient.DefaultRequestHeaders.Add("Accept", "image/jpeg,image/*");

            // Try to download the image
            var response = await imageClient.GetAsync(imageUrl);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to download image with status: {response.StatusCode}");
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            _logger.LogInfo($"Downloaded image with content type: {contentType}");

            // Get the image bytes
            var imageBytes = await response.Content.ReadAsByteArrayAsync();
            _logger.LogInfo($"Successfully downloaded image: {imageBytes.Length} bytes");

            return imageBytes;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error downloading image: {ex.Message}");
            throw;
        }
    }

}