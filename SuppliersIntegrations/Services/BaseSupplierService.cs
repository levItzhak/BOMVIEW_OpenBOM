using System.Net.Http;
using BOMVIEW.Interfaces;
using System;

namespace BOMVIEW.Services
{
    public abstract class BaseSupplierService
    {
        protected readonly ILogger _logger;
        protected readonly HttpClient _httpClient;

        protected BaseSupplierService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = new HttpClient();
        }


        protected void HandleHttpError(HttpResponseMessage response, string content)
        {
            if (!response.IsSuccessStatusCode)
            {
                string errorMessage = $"HTTP Error {(int)response.StatusCode} ({response.StatusCode}): {content}";
                _logger.LogError(errorMessage);
                throw new HttpRequestException(errorMessage);
            }
        }

    }
}