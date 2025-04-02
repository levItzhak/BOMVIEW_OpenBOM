using BOMVIEW.Interfaces;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace BOMVIEW.Services
{
    public class CurrencyExchangeService
    {
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;
        private DateTime _lastUpdate = DateTime.MinValue;
        private decimal _usdToIlsRate = 3.6m; // Default fallback rate

        public decimal UsdToIlsRate => _usdToIlsRate;
        public DateTime LastUpdate => _lastUpdate;
        
        public bool HasValidRate => _lastUpdate.AddHours(4) > DateTime.UtcNow;

        public CurrencyExchangeService(ILogger logger)
        {
            _logger = logger;
            _httpClient = new HttpClient();
        }

        public async Task<bool> UpdateExchangeRateAsync()
        {
            try
            {
                // Only update if rate is older than 4 hours
                if (HasValidRate)
                {
                    return true;
                }

                _logger.LogInfo("Fetching USD to ILS exchange rate...");
                
                // Using free ExchangeRate API
                var response = await _httpClient.GetAsync("https://open.er-api.com/v6/latest/USD");
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                
                if (doc.RootElement.TryGetProperty("rates", out var rates) && 
                    rates.TryGetProperty("ILS", out var ilsRate))
                {
                    _usdToIlsRate = ilsRate.GetDecimal();
                    _lastUpdate = DateTime.UtcNow;
                    _logger.LogSuccess($"Exchange rate updated: 1 USD = {_usdToIlsRate} ILS");
                    return true;
                }
                
                throw new Exception("Failed to parse exchange rate data");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating exchange rate: {ex.Message}");
                
                // If we've never fetched a rate, use the default
                if (_lastUpdate == DateTime.MinValue)
                {
                    _lastUpdate = DateTime.UtcNow;
                    _logger.LogWarning($"Using default exchange rate: 1 USD = {_usdToIlsRate} ILS");
                }
                
                return false;
            }
        }

        public decimal ConvertIlsToUsd(decimal ilsAmount)
        {
            if (_usdToIlsRate <= 0)
                return 0;
                
            return ilsAmount / _usdToIlsRate;
        }

        public decimal ConvertUsdToIls(decimal usdAmount)
        {
            return usdAmount * _usdToIlsRate;
        }
    }
} 