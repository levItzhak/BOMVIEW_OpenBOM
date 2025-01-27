using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using BOMVIEW.Models;
using ClosedXML.Excel;
using System.IO;
using BOMVIEW.Interfaces;
using BOMVIEW.OpenBOM.Models;

namespace BOMVIEW.Services
{
    public class UpdateResult
    {
        public string CatalogId { get; set; }
        public string CatalogName { get; set; }
        public string PartNumber { get; set; }
        public bool WasUpdated { get; set; }
        public bool WasFound { get; set; }
        public string Status { get; set; }
        public Dictionary<string, string> OldValues { get; set; }
        public Dictionary<string, string> NewValues { get; set; }
        public Exception Error { get; set; }

        public UpdateResult()
        {
            OldValues = new Dictionary<string, string>();
            NewValues = new Dictionary<string, string>();
        }
    }

    public class BulkUpdateProgress
    {
        public int TotalProducts { get; set; }
        public int ProcessedProducts { get; set; }
        public int UpdatedProducts { get; set; }
        public int FailedProducts { get; set; }
        public string CurrentOperation { get; set; }
        public double ProgressPercentage => TotalProducts == 0 ? 0 : (ProcessedProducts * 100.0 / TotalProducts);
    }

    public class BulkUpdateService
    {
        private readonly ILogger _logger;
        private readonly DigiKeyService _digiKeyService;
        private readonly OpenBomService _openBomService;
        private readonly List<UpdateResult> _updateResults;
        private CancellationTokenSource _cancellationTokenSource;

        public event Action<BulkUpdateProgress> ProgressUpdated;

        private const int RATE_LIMIT_DELAY_MS = 1000; // 1 second delay between API calls
        private const int MAX_RETRIES = 3;

        public BulkUpdateService(
            ILogger logger,
            DigiKeyService digiKeyService,
            OpenBomService openBomService)
        {
            _logger = logger;
            _digiKeyService = digiKeyService;
            _openBomService = openBomService;
            _updateResults = new List<UpdateResult>();
        }

        public void CancelUpdate()
        {
            _cancellationTokenSource?.Cancel();
        }

        public async Task<List<UpdateResult>> UpdateAllCatalogsAsync()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _updateResults.Clear();

            try
            {
                var catalogs = await _openBomService.ListCatalogsAsync();

                foreach (var catalog in catalogs)
                {
                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                        break;

                    await UpdateCatalogAsync(catalog.Id);
                }

                return _updateResults;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in bulk update: {ex.Message}");
                throw;
            }
        }

        public async Task<List<UpdateResult>> UpdateCatalogAsync(string catalogId)
        {
            if (_cancellationTokenSource == null || _cancellationTokenSource.Token.IsCancellationRequested)
            {
                _cancellationTokenSource = new CancellationTokenSource();
            }

            try
            {
                var progress = new BulkUpdateProgress();
                var catalog = await _openBomService.GetCatalogAsync(catalogId);
                var parts = await _openBomService.GetCatalogHierarchyAsync(catalogId);

                progress.TotalProducts = parts.Count;
                ProgressUpdated?.Invoke(progress);

                foreach (var part in parts)
                {
                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                        break;

                    try
                    {
                        progress.CurrentOperation = $"Processing {part.PartNumber}";
                        ProgressUpdated?.Invoke(progress);

                        var result = await UpdatePartAsync(catalogId, part);
                        _updateResults.Add(result);

                        progress.ProcessedProducts++;
                        if (result.WasUpdated)
                            progress.UpdatedProducts++;
                        else if (result.Error != null)
                            progress.FailedProducts++;

                        ProgressUpdated?.Invoke(progress);

                        // Rate limiting delay
                        await Task.Delay(RATE_LIMIT_DELAY_MS, _cancellationTokenSource.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error updating part {part.PartNumber}: {ex.Message}");
                        progress.FailedProducts++;
                        ProgressUpdated?.Invoke(progress);
                    }
                }

                return _updateResults;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating catalog {catalogId}: {ex.Message}");
                throw;
            }
        }

        private async Task<UpdateResult> UpdatePartAsync(string catalogId, BomTreeNode part)
        {
            var result = new UpdateResult
            {
                CatalogId = catalogId,
                PartNumber = part.PartNumber
            };

            try
            {
                // Store old values
                foreach (var prop in part.Properties)
                {
                    result.OldValues[prop.Key] = prop.Value;
                }

                // Get DigiKey data
                var digiKeyData = await GetDigiKeyDataWithRetryAsync(part.PartNumber);

                if (digiKeyData == null || !digiKeyData.IsAvailable)
                {
                    result.WasFound = false;
                    result.Status = "Part not found in DigiKey";
                    return result;
                }

                // Prepare update data
                var propertiesToUpdate = new Dictionary<string, string>
                {
                    ["Description"] = digiKeyData.Description?.Trim() ?? "",
                    ["Cost"] = digiKeyData.Price.ToString("F2"),
                    ["Lead time"] = digiKeyData.LeadTime?.ToString() ?? "",
                    ["Manufacturer"] = digiKeyData.Manufacturer?.Trim() ?? "",
                    ["Link"] = digiKeyData.ProductUrl?.Trim() ?? "",
                    ["Data Sheet"] = digiKeyData.DatasheetUrl?.Trim() ?? "",
                    ["Quantity Available"] = digiKeyData.Availability.ToString(),
                    ["Catalog Type"] = "Electronic Component",
                    ["Vendor"] = "DigiKey"
                };

                // Update the part
                await _openBomService.UpdateCatalogPartAsync(catalogId, new OpenBomPartRequest
                {
                    PartNumber = part.PartNumber,
                    Properties = propertiesToUpdate
                });

                // Store new values
                foreach (var prop in propertiesToUpdate)
                {
                    result.NewValues[prop.Key] = prop.Value;
                }

                // Update thumbnail if available
                if (!string.IsNullOrEmpty(digiKeyData.ImageUrl))
                {
                    var imageBytes = await _digiKeyService.GetImageBytesAsync(digiKeyData.ImageUrl);
                    if (imageBytes != null && imageBytes.Length > 0)
                    {
                        await _openBomService.UploadCatalogImageAsync(
                            catalogId,
                            part.PartNumber,
                            imageBytes,
                            "Thumbnail image"
                        );
                    }
                }

                result.WasUpdated = true;
                result.WasFound = true;
                result.Status = "Successfully updated";
            }
            catch (Exception ex)
            {
                result.Error = ex;
                result.Status = $"Error: {ex.Message}";
            }

            return result;
        }

        private async Task<SupplierData> GetDigiKeyDataWithRetryAsync(string partNumber)
        {
            for (int i = 0; i < MAX_RETRIES; i++)
            {
                try
                {
                    return await _digiKeyService.GetPriceAndAvailabilityAsync(partNumber);
                }
                catch (Exception ex) when (i < MAX_RETRIES - 1)
                {
                    _logger.LogWarning($"Retry {i + 1} for part {partNumber}: {ex.Message}");
                    await Task.Delay(RATE_LIMIT_DELAY_MS * (i + 1));
                }
            }

            return await _digiKeyService.GetPriceAndAvailabilityAsync(partNumber);
        }

        public async Task GenerateReportAsync(string filePath)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Update Results");

            // Add headers
            worksheet.Cell(1, 1).Value = "Catalog ID";
            worksheet.Cell(1, 2).Value = "Part Number";
            worksheet.Cell(1, 3).Value = "Status";
            worksheet.Cell(1, 4).Value = "Was Updated";
            worksheet.Cell(1, 5).Value = "Was Found";

            // Add property columns
            var propertyColumns = new Dictionary<string, int>();
            var nextColumn = 6;

            foreach (var result in _updateResults.Where(r => r.WasUpdated))
            {
                foreach (var prop in result.NewValues.Keys)
                {
                    if (!propertyColumns.ContainsKey(prop))
                    {
                        propertyColumns[prop] = nextColumn++;
                        worksheet.Cell(1, propertyColumns[prop]).Value = prop;
                    }
                }
            }

            // Add data
            for (int i = 0; i < _updateResults.Count; i++)
            {
                var result = _updateResults[i];
                var row = i + 2;

                worksheet.Cell(row, 1).Value = result.CatalogId;
                worksheet.Cell(row, 2).Value = result.PartNumber;
                worksheet.Cell(row, 3).Value = result.Status;
                worksheet.Cell(row, 4).Value = result.WasUpdated;
                worksheet.Cell(row, 5).Value = result.WasFound;

                if (result.WasUpdated)
                {
                    foreach (var prop in result.NewValues)
                    {
                        worksheet.Cell(row, propertyColumns[prop.Key]).Value = prop.Value;
                    }
                }
            }

            // Format as table
            var range = worksheet.Range(1, 1, _updateResults.Count + 1, nextColumn - 1);
            var table = range.CreateTable();
            table.Theme = XLTableTheme.TableStyleMedium2;

            // Auto-fit columns
            worksheet.Columns().AdjustToContents();

            await Task.Run(() => workbook.SaveAs(filePath));
        }
    }
}