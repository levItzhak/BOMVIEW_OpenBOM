using BOMVIEW.Interfaces;
using BOMVIEW.Models;
using BOMVIEW.Services;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;
using BOMVIEW.Exceptions;
using System.Text;
using System.Linq;

namespace BOMVIEW.Services
{
    public class IsraelService : BaseSupplierService, ISupplierService
    {
        private readonly ApiCredentials _credentials;
        private readonly Dictionary<string, SupplierData> _cache = new();
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly CurrencyExchangeService _currencyService;
        private string _accessToken;
        private DateTime _tokenExpiry = DateTime.MinValue;

        // Locale settings specifically for Israel
        private const string LOCALE = "he";  // Hebrew
        private const string CURRENCY = "ILS"; // Israeli Shekel
        private const string COUNTRY = "IL";   // Israel

        public SupplierType SupplierType => SupplierType.Israel;
        
        // Expose the currency service for UI display
        public CurrencyExchangeService CurrencyService => _currencyService;

        public IsraelService(ILogger logger, ApiCredentials credentials) : base(logger)
        {
            _credentials = credentials;
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _currencyService = new CurrencyExchangeService(logger);
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
                // Update exchange rate
                await _currencyService.UpdateExchangeRateAsync();
            
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

                // Convert ILS price to USD
                decimal ilsPrice = digiKeyResponse?.Product?.UnitPrice ?? 0;
                decimal usdPrice = _currencyService.ConvertIlsToUsd(ilsPrice);

                // Process price breaks (convert all to USD)
                var priceBreaks = new List<PriceBreak>();
                DigiKeyProductVariation selectedVariation = null;

                if (digiKeyResponse?.Product?.ProductVariations != null && digiKeyResponse.Product.ProductVariations.Any())
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
                                _logger.LogInfo($"  Price Break: Qty {priceBreak.BreakQuantity}, Unit Price ₪ {priceBreak.UnitPrice}");
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
                        string selectedPartNumber = selectedVariation.DigiKeyProductNumber;
                        _logger.LogInfo($"Selected variation: {selectedPartNumber} with MOQ: {selectedVariation.MinimumOrderQuantity}");

                        // Process the price breaks from the selected variation
                        if (selectedVariation.StandardPricing != null)
                        {
                            foreach (var breakpoint in selectedVariation.StandardPricing)
                            {
                                priceBreaks.Add(new PriceBreak 
                                { 
                                    Quantity = breakpoint.BreakQuantity,
                                    UnitPrice = _currencyService.ConvertIlsToUsd(breakpoint.UnitPrice)
                                });
                            }
                            
                            // Make sure price breaks are sorted by quantity
                            priceBreaks = priceBreaks.OrderBy(pb => pb.Quantity).ToList();
                            
                            _logger.LogInfo($"Price breaks for {partNumber}: {priceBreaks.Count} tiers found");
                            foreach (var pb in priceBreaks)
                            {
                                _logger.LogInfo($"  Qty {pb.Quantity}: ${pb.UnitPrice:F5} USD");
                            }
                        }
                    }
                }

                var israelData = new SupplierData
                {
                    ProductUrl = digiKeyResponse?.Product?.ProductUrl ?? $"https://www.digikey.co.il/en/products/result?keywords={partNumber}",
                    Price = usdPrice, // Converted to USD
                    Availability = (int)(digiKeyResponse?.Product?.QuantityAvailable ?? 0),
                    IsAvailable = digiKeyResponse?.Product?.IsAvailable ?? false,
                    Description = digiKeyResponse?.Product?.Description?.ProductDescription ?? "",
                    Manufacturer = digiKeyResponse?.Product?.Manufacturer?.Name ?? "",
                    LeadTime = digiKeyResponse?.Product?.ManufacturerLeadWeeks ?? "",
                    ImageUrl = digiKeyResponse?.Product?.PhotoUrl ?? "",
                    DatasheetUrl = digiKeyResponse?.Product?.DatasheetUrl ?? "",
                    Category = digiKeyResponse?.Product?.Category?.Name ?? "",
                    IsraelPartNumber = selectedVariation?.DigiKeyProductNumber ?? "",
                    PriceBreaks = priceBreaks, // Converted to USD
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