using BOMVIEW.Interfaces;
using BOMVIEW.Models;
using BOMVIEW.Services;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;
using BOMVIEW.Exceptions;
using System.Text;

namespace BOMVIEW.Services
{
    public class IsraelService : BaseSupplierService, ISupplierService
    {
        private readonly ApiCredentials _credentials;
        private readonly Dictionary<string, SupplierData> _cache = new();
        private readonly HttpClient _httpClient = new HttpClient();
        private string _accessToken;
        private DateTime _tokenExpiry = DateTime.MinValue;

        // Locale settings specifically for Israel
        private const string LOCALE = "he";  // Hebrew
        private const string CURRENCY = "ILS"; // Israeli Shekel
        private const string COUNTRY = "IL";   // Israel

        public SupplierType SupplierType => SupplierType.Israel;

        public IsraelService(ILogger logger, ApiCredentials credentials) : base(logger)
        {
            _credentials = credentials;
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private async Task EnsureAuthenticatedAsync()
        {
            try
            {
                if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry)
                    return;

                _logger.LogInfo("Getting new DigiKey Israel access token...");

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

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        throw new RateLimitException(SupplierType, "Rate limit exceeded for DigiKey Israel API");
                    }

                    _logger.LogError($"Error from DigiKey Israel API: {response.StatusCode}");
                    throw new Exception($"Authentication failed: {response.StatusCode}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<DigiKeyTokenResponse>(
                    responseContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                _accessToken = tokenResponse.AccessToken;
                _tokenExpiry = DateTime.UtcNow.AddHours(1);

                _logger.LogSuccess("Successfully obtained DigiKey Israel access token");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Authentication error: {ex.Message}");
                throw;
            }
        }

        public async Task<SupplierData> GetPriceAndAvailabilityAsync(string partNumber)
        {
            try
            {
                // Check cache first
                if (_cache.TryGetValue(partNumber, out var cachedData))
                {
                    return cachedData;
                }

                await EnsureAuthenticatedAsync();

                _logger.LogInfo($"Getting price and availability for {partNumber} from DigiKey Israel");

                // Use DigiKey API with Israel-specific settings
                string url = $"https://api.digikey.com/products/v4/search/{partNumber}/productdetails";
                url += $"?locale={LOCALE}&currency={CURRENCY}&country={COUNTRY}";
                
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                _httpClient.DefaultRequestHeaders.Add("X-DIGIKEY-Client-Id", _credentials.DigiKeyClientId);
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                // Add locale headers
                _httpClient.DefaultRequestHeaders.Add("X-DIGIKEY-Locale-Site", COUNTRY);
                _httpClient.DefaultRequestHeaders.Add("X-DIGIKEY-Locale-Language", LOCALE);
                _httpClient.DefaultRequestHeaders.Add("X-DIGIKEY-Locale-Currency", CURRENCY);

                var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        throw new RateLimitException(SupplierType, "Rate limit exceeded for DigiKey Israel API");
                    }

                    _logger.LogError($"Error from DigiKey Israel API: {response.StatusCode}");
                    return new SupplierData { Price = 0, Availability = 0, IsAvailable = false };
                }

                var digiKeyResponse = JsonSerializer.Deserialize<DigiKeyProductResponse>(content, 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (digiKeyResponse?.Product == null)
                {
                    _logger.LogError($"Failed to deserialize DigiKey Israel response for {partNumber}");
                    return new SupplierData { Price = 0, Availability = 0, IsAvailable = false };
                }

                string digiKeyPartNumber = "";
                if (digiKeyResponse.Product?.ProductVariations != null)
                {
                    // Check if there are at least 2 variations and get the second one ([1])
                    if (digiKeyResponse.Product.ProductVariations.Count >= 2)
                    {
                        digiKeyPartNumber = digiKeyResponse.Product.ProductVariations[1].DigiKeyProductNumber;
                    }
                    // Fallback to the first one if there's only one variation
                    else if (digiKeyResponse.Product.ProductVariations.Any())
                    {
                        digiKeyPartNumber = digiKeyResponse.Product.ProductVariations[0].DigiKeyProductNumber;
                    }
                }

                var israelData = new SupplierData
                {
                    ProductUrl = digiKeyResponse?.Product?.ProductUrl ?? $"https://www.digikey.co.il/en/products/result?keywords={partNumber}",
                    Price = digiKeyResponse?.Product?.UnitPrice ?? 0,
                    Availability = (int)(digiKeyResponse?.Product?.QuantityAvailable ?? 0),
                    IsAvailable = digiKeyResponse?.Product?.IsAvailable ?? false,
                    Description = digiKeyResponse?.Product?.Description?.ProductDescription ?? "",
                    Manufacturer = digiKeyResponse?.Product?.Manufacturer?.Name ?? "",
                    LeadTime = digiKeyResponse?.Product?.ManufacturerLeadWeeks ?? "",
                    ImageUrl = digiKeyResponse?.Product?.PhotoUrl ?? "",
                    DatasheetUrl = digiKeyResponse?.Product?.DatasheetUrl ?? "",
                    Category = digiKeyResponse?.Product?.Category?.Name ?? "",
                    IsraelPartNumber = digiKeyPartNumber ?? "",
                    PriceBreaks = digiKeyResponse?.Product?.ProductVariations?.FirstOrDefault()?.StandardPricing?.Select(p => new PriceBreak
                    {
                        Quantity = p.BreakQuantity,
                        UnitPrice = p.UnitPrice
                    }).ToList() ?? new List<PriceBreak>(),
                    Parameters = digiKeyResponse?.Product?.Parameters?.Select(p => new DigiKeyParameterInfo
                    {
                        Name = p.ParameterText,
                        Value = p.ValueText
                    }).ToList() ?? new List<DigiKeyParameterInfo>()
                };

                // Cache the result
                _cache[partNumber] = israelData;
                
                return israelData;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving data for {partNumber} from DigiKey Israel: {ex.Message}");
                return new SupplierData { Price = 0, Availability = 0, IsAvailable = false };
            }
        }
    }
} 