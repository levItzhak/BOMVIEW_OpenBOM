// MouserService.cs
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using BOMVIEW.Interfaces;
using BOMVIEW.Models;
using System.Globalization;
using System.Text.RegularExpressions;
using BOMVIEW.Exceptions;

namespace BOMVIEW.Services
{
    public class MouserService : BaseSupplierService, ISupplierService
    {
        private const string BASE_URL = "https://api.mouser.com/api/v1";
        private readonly ApiCredentials _credentials;
        private readonly Dictionary<string, SupplierData> _cache = new();

        public SupplierType SupplierType => SupplierType.Mouser;

        public MouserService(ILogger logger, ApiCredentials credentials) : base(logger)
        {
            _credentials = credentials;
        }

        public async Task<SupplierData> GetPriceAndAvailabilityAsync(string partNumber)
        {
            try
            {
                var endpoint = $"{BASE_URL}/search/partnumber?apiKey={_credentials.MouserApiKey}";

                var searchRequest = new
                {
                    SearchByPartRequest = new
                    {
                        mouserPartNumber = partNumber
                    }
                };

                var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(searchRequest),
                        Encoding.UTF8,
                        "application/json"
                    )
                };

                var response = await _httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"Mouser API error for {partNumber}: {response.StatusCode}");
                    return new SupplierData { Price = 0, Availability = 0, IsAvailable = false };
                }

                var mouserResponse = JsonSerializer.Deserialize<MouserProductResponse>(content);
                var part = mouserResponse?.SearchResults?.Parts?.FirstOrDefault();

                if (part == null)
                {
                    _logger.LogWarning($"No product data found for {partNumber}");
                    return new SupplierData { Price = 0, Availability = 0, IsAvailable = false };
                }

                decimal price = 0;
                if (part.PriceBreaks?.FirstOrDefault()?.Price != null)
                {
                    var priceStr = part.PriceBreaks[0].Price.Trim();
                    priceStr = Regex.Replace(priceStr, @"[^\d.,]", "");
                    decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out price);
                }

                int availability = 0;
                if (!string.IsNullOrEmpty(part.AvailabilityInStock))
                {
                    int.TryParse(Regex.Replace(part.AvailabilityInStock, @"[^\d]", ""), out availability);
                }

                var supplierData = new SupplierData
                {
                    ProductUrl = $"https://www.mouser.com/c/?q={partNumber}",
                    Price = price,
                    Availability = availability,
                    IsAvailable = availability > 0,
                    MouserPartNumber = part.MouserPartNumber,  
                    PriceBreaks = new List<PriceBreak>()
                };

                // Extract price breaks from the response
                if (part.PriceBreaks != null)
                {
                    foreach (var mousePriceBreak in part.PriceBreaks)
                    {
                        if (mousePriceBreak.Price != null)
                        {
                            var priceStr = mousePriceBreak.Price.Trim();
                            priceStr = Regex.Replace(priceStr, @"[^\d.,]", "");
                            if (decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal breakPrice))
                            {
                                supplierData.PriceBreaks.Add(new PriceBreak
                                {
                                    Quantity = mousePriceBreak.Quantity,
                                    UnitPrice = breakPrice
                                });
                            }
                        }
                    }
                }

                return supplierData;
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


    }
}