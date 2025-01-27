using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BOMVIEW.Exceptions;
using BOMVIEW.Interfaces;
using BOMVIEW.Models;

namespace BOMVIEW.Services
{
    public class FarnellService : BaseSupplierService, ISupplierService
    {
        private const string BASE_URL = "https://api.element14.com/catalog/products";
        private readonly ApiCredentials _credentials;
        private readonly Dictionary<string, SupplierData> _cache = new();

        public BOMVIEW.Interfaces.SupplierType SupplierType => BOMVIEW.Interfaces.SupplierType.Farnell;

        public FarnellService(ILogger logger, ApiCredentials credentials) : base(logger)
        {
            _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
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

                _logger.LogInfo($"Fetching Farnell data for part number: {partNumber}");

                // Build the request URL - First try by manufacturer part number
                var queryParams = new Dictionary<string, string>
                {
                    { "callInfo.responseDataFormat", "JSON" },
                    { "term", $"manuPartNum:{partNumber}" },
                    { "storeInfo.id", "il.farnell.com" },
                    { "resultsSettings.responseGroup", "medium" },
                    { "callInfo.apiKey", _credentials.FarnellApiKey }
                };

                string queryString = string.Join("&", queryParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
                string url = $"{BASE_URL}?{queryString}";

                _logger.LogInfo($"Farnell API URL: {url}");

                // Make the request
                var response = await _httpClient.GetAsync(url);

                // Check for rate limiting
                CheckRateLimit(response);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"Farnell API error for {partNumber}: {response.StatusCode}");
                    return new SupplierData { Price = 0, Availability = 0, IsAvailable = false };
                }

                var content = await response.Content.ReadAsStringAsync();
                _logger.LogInfo($"Farnell API response: {content.Substring(0, Math.Min(content.Length, 1000))}...");

                // Parse response and find product
                FarnellProduct product = null;
                var farnellResponse = JsonSerializer.Deserialize<FarnellProductResponse>(content);

                // Try all possible response types
                if (farnellResponse?.ManufacturerPartNumberSearchReturn?.Products?.Count > 0)
                {
                    product = farnellResponse.ManufacturerPartNumberSearchReturn.Products.FirstOrDefault();
                    _logger.LogInfo("Found product in ManufacturerPartNumberSearchReturn");
                }
                else if (farnellResponse?.PremierFarnellPartNumberReturn?.Products?.Count > 0)
                {
                    product = farnellResponse.PremierFarnellPartNumberReturn.Products.FirstOrDefault();
                    _logger.LogInfo("Found product in PremierFarnellPartNumberReturn");
                }
                else if (farnellResponse?.KeywordSearchReturn?.Products?.Count > 0)
                {
                    product = farnellResponse.KeywordSearchReturn.Products.FirstOrDefault();
                    _logger.LogInfo("Found product in KeywordSearchReturn");
                }

                if (product == null)
                {
                    _logger.LogWarning($"No product data found in Farnell for {partNumber}");
                    return new SupplierData { Price = 0, Availability = 0, IsAvailable = false };
                }

                // Extract price breaks
                var priceBreaks = new List<PriceBreak>();
                if (product.Prices != null && product.Prices.Any())
                {
                    foreach (var price in product.Prices)
                    {
                        priceBreaks.Add(new PriceBreak
                        {
                            Quantity = price.From,
                            UnitPrice = price.Cost
                        });
                    }

                    // Sort price breaks by quantity
                    priceBreaks = priceBreaks.OrderBy(p => p.Quantity).ToList();
                }

                // Parse availability - default to 0 if not available
                int availability = 0;
                bool isAvailable = false;

                if (product.Stock != null)
                {
                    availability = product.Stock.Level;
                    // Status 1 means in stock according to the documentation
                    isAvailable = product.Stock.Status == 1 && availability > 0;
                }
                else
                {
                    // If stock info is missing but the productStatus is "STOCKED", consider it available
                    if (product.ProductStatus == "STOCKED")
                    {
                        isAvailable = true;
                        // Assuming a default stock level
                        availability = 100;
                    }
                }

                // Construct product URL
                string productUrl = $"https://il.farnell.com/productDetail.html?sku={product.Sku}";

                // Construct image URL if available
                string imageUrl = null;
                if (product.Image != null && !string.IsNullOrEmpty(product.Image.BaseName))
                {
                    string locale = "en_GB"; // Default locale
                    if (product.Image.VrntPath == "nio/")
                    {
                        locale = "en_US";
                    }
                    imageUrl = $"https://il.farnell.com/productimages/standard/{locale}{product.Image.BaseName}";
                }

                // Construct datasheet URL
                string datasheetUrl = null;
                if (product.Datasheets != null && product.Datasheets.Any())
                {
                    datasheetUrl = product.Datasheets.FirstOrDefault()?.Url;
                }

                // Create supplier data
                var supplierData = new SupplierData
                {
                    ProductUrl = productUrl,
                    Price = priceBreaks.FirstOrDefault()?.UnitPrice ?? 0,
                    Availability = availability,
                    IsAvailable = isAvailable,
                    Description = product.DisplayName,
                    Manufacturer = product.BrandName,
                    DatasheetUrl = datasheetUrl,
                    ImageUrl = imageUrl,
                    FarnellPartNumber = product.Sku,
                    PriceBreaks = priceBreaks
                };

                // Cache the result
                _cache[partNumber] = supplierData;

                return supplierData;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving Farnell data for {partNumber}: {ex.Message}");
                return new SupplierData { Price = 0, Availability = 0, IsAvailable = false };
            }
        }

        private void CheckRateLimit(HttpResponseMessage response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                throw new RateLimitException(BOMVIEW.Interfaces.SupplierType.Farnell, "API rate limit exceeded");
            }
        }
    }
}