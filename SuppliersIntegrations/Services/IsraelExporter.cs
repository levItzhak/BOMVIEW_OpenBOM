using System.Drawing;
using System.IO;
using System.Windows;
using BOMVIEW.Interfaces;
using BOMVIEW.Models;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace BOMVIEW.Services
{
    public class IsraelExporter
    {
        private readonly ILogger _logger;
        private readonly IsraelService _israelService;
        private readonly string _originalFilePath;
        private readonly CurrencyExchangeService _currencyService;

        public IsraelExporter(ILogger logger, IsraelService israelService, string originalFilePath)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _israelService = israelService ?? throw new ArgumentNullException(nameof(israelService));
            _originalFilePath = originalFilePath;
            _currencyService = israelService.CurrencyService;
        }

        public async Task ExportIsraelListsAsync(List<BomEntry> entries, string originalFilePath)
        {
            var saveDialog = new SaveFileDialog(originalFilePath);
            if (saveDialog.ShowDialog() != true) return;

            try
            {
                string selectedDirectory = Path.GetDirectoryName(saveDialog.SelectedFilePath);
                string baseFileName = Path.GetFileNameWithoutExtension(saveDialog.SelectedFilePath);
                string originalFileName = Path.GetFileNameWithoutExtension(originalFilePath);

                // Create a folder for the BOM
                string bomFolderPath = Path.Combine(selectedDirectory, baseFileName);
                Directory.CreateDirectory(bomFolderPath);

                // Create the export files in the BOM folder
                string israelListPath = Path.Combine(bomFolderPath, $"{baseFileName}_DK-IL_List.xlsx");
                string israelBestPricesPath = Path.Combine(bomFolderPath, $"{baseFileName}_DK-IL_Best_Prices.xlsx");

                // Export all Israel items
                await ExportIsraelItemsAsync(
                    entries,
                    israelListPath,
                    originalFileName,
                    baseFileName,
                    onlyBestPrice: false
                );

                // Export best price Israel items
                await ExportIsraelItemsAsync(
                    entries,
                    israelBestPricesPath,
                    originalFileName,
                    baseFileName,
                    onlyBestPrice: true
                );

                _logger.LogSuccess("Successfully exported DigiKey Israel lists");

                // Open file if requested
                if (saveDialog.OpenAfterSave)
                {
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = bomFolderPath,
                            UseShellExecute = true,
                            Verb = "open"
                        };
                        System.Diagnostics.Process.Start(psi);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error opening folder: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error exporting Israel lists: {ex.Message}");
                throw;
            }
        }

        public async Task ExportIsraelItemsAsync(
            List<BomEntry> entries,
            string filePath,
            string originalFileName,
            string savedFileName,
            bool onlyBestPrice)
        {
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("DigiKey Israel List");

            // Set headers
            worksheet.Cells[1, 1].Value = "Ordering Code";
            worksheet.Cells[1, 2].Value = "DK-IL Part Number";
            worksheet.Cells[1, 3].Value = "Customer Reference";
            worksheet.Cells[1, 4].Value = "Designator";
            worksheet.Cells[1, 5].Value = "Quantity";
            worksheet.Cells[1, 6].Value = "Original Quantity";
            worksheet.Cells[1, 7].Value = "Unit Price ($)";
            worksheet.Cells[1, 8].Value = "Total Price ($)";
            worksheet.Cells[1, 9].Value = "Exchange Rate (USD/ILS)";

            // Add exchange rate information
            worksheet.Cells[1, 9].Value = "Exchange Rate (USD/ILS)";
            worksheet.Cells[2, 9].Value = _currencyService.UsdToIlsRate;
            worksheet.Cells[3, 9].Value = "Last Updated";
            worksheet.Cells[4, 9].Value = _currencyService.LastUpdate.ToString("g");

            // Style headers
            var headerRange = worksheet.Cells[1, 1, 1, 9];
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
            headerRange.Style.Fill.BackgroundColor.SetColor(Color.LightGray);

            int row = 2;
            foreach (var entry in entries)
            {
                if (!(entry.IsraelData?.IsAvailable ?? false) ||
                    entry.IsraelOrderQuantity <= 0 ||
                    entry.HasExternalSupplier ||
                    (onlyBestPrice && entry.BestCurrentSupplier != "Israel"))
                    continue;

                // Write data
                worksheet.Cells[row, 1].Value = entry.OrderingCode;
                worksheet.Cells[row, 2].Value = entry.IsraelData?.IsraelPartNumber ?? "";
                worksheet.Cells[row, 3].Value = savedFileName;
                worksheet.Cells[row, 4].Value = entry.Designator;
                worksheet.Cells[row, 5].Value = entry.IsraelOrderQuantity;
                worksheet.Cells[row, 6].Value = entry.QuantityTotal;
                worksheet.Cells[row, 7].Value = entry.IsraelData?.Price ?? 0;
                worksheet.Cells[row, 8].Value = (entry.IsraelData?.Price ?? 0) * entry.IsraelOrderQuantity;

                // Style rows based on conditions
                var rowRange = worksheet.Cells[row, 1, row, 8];
                rowRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                rowRange.Style.Fill.BackgroundColor.SetColor(Color.White);

                if (onlyBestPrice)
                {
                    bool digiKeyAvailable = entry.DigiKeyData?.IsAvailable ?? false;
                    bool mouserAvailable = entry.MouserData?.IsAvailable ?? false;
                    bool farnellAvailable = entry.FarnellData?.IsAvailable ?? false;

                    if (!digiKeyAvailable && !mouserAvailable && !farnellAvailable)
                    {
                        // Red for products only available in Israel
                        rowRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        rowRange.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 255, 200, 200));
                    }
                    else
                    {
                        bool isBestPrice = true;
                        
                        if (digiKeyAvailable && entry.DigiKeyCurrentTotalPrice <= entry.IsraelCurrentTotalPrice)
                            isBestPrice = false;
                        
                        if (mouserAvailable && entry.MouserCurrentTotalPrice <= entry.IsraelCurrentTotalPrice)
                            isBestPrice = false;
                        
                        if (farnellAvailable && entry.FarnellCurrentTotalPrice <= entry.IsraelCurrentTotalPrice)
                            isBestPrice = false;

                        if (isBestPrice)
                        {
                            // Green for better price
                            rowRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                            rowRange.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 232, 245, 233));
                        }
                    }

                    // Highlight minimum quantity adjustments
                    if (entry.IsraelOrderQuantity > entry.QuantityTotal)
                    {
                        worksheet.Cells[row, 5].Style.Font.Bold = true;
                        worksheet.Cells[row, 5].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        worksheet.Cells[row, 5].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 255, 235, 156));
                    }
                }

                row++;
            }

            // Add note about USD pricing
            worksheet.Cells[row + 1, 1].Value = "NOTE: All prices are displayed in USD (converted from ILS)";
            worksheet.Cells[row + 1, 1, row + 1, 8].Merge = true;
            worksheet.Cells[row + 1, 1].Style.Font.Bold = true;
            worksheet.Cells[row + 1, 1].Style.Font.Italic = true;
            worksheet.Cells[row + 1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // Format currency columns
            var priceColumns = worksheet.Cells[2, 7, row - 1, 8];
            priceColumns.Style.Numberformat.Format = "$#,##0.00";
            
            // Format exchange rate cell
            worksheet.Cells[2, 9].Style.Numberformat.Format = "#,##0.00";

            // Add borders and ensure text visibility without overriding colors
            var dataRange = worksheet.Cells[1, 1, row - 1, 9];
            StyleSheetBorders(dataRange);
            dataRange.Style.Font.Color.SetColor(Color.Black);

            // Auto-fit columns
            for (int i = 1; i <= 9; i++)
            {
                worksheet.Column(i).AutoFit();
            }

            if (onlyBestPrice)
            {
                var legendSheet = package.Workbook.Worksheets.Add("Legend");
                AddBestPricesLegend(legendSheet);
            }

            await package.SaveAsAsync(new FileInfo(filePath));
        }

        private void AddBestPricesLegend(ExcelWorksheet sheet)
        {
            // Header
            sheet.Cells[1, 1].Value = "DigiKey Israel Best Prices Legend";
            sheet.Cells[1, 1].Style.Font.Bold = true;
            sheet.Cells[1, 1].Style.Font.Size = 14;

            // Add currency note
            sheet.Cells[2, 1].Value = "All prices are in USD (converted from ILS at rate: " + _currencyService.UsdToIlsRate.ToString("F2") + ")";
            sheet.Cells[2, 1].Style.Font.Italic = true;

            var legendItems = new (string Text, Color Color)[]
            {
                ("Best Price (Better than DigiKey, Mouser, and Farnell)", Color.FromArgb(255, 232, 245, 233)),
                ("Only Available from DigiKey Israel", Color.FromArgb(255, 255, 200, 200)),
                ("Minimum Order Quantity Applied", Color.FromArgb(255, 255, 235, 156))
            };

            // Add legend items
            sheet.Cells[4, 1].Value = "Color Coding:";
            sheet.Cells[4, 1].Style.Font.Bold = true;

            for (int i = 0; i < legendItems.Length; i++)
            {
                var row = i + 5;
                sheet.Cells[row, 2].Value = legendItems[i].Text;
                sheet.Cells[row, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
                sheet.Cells[row, 2].Style.Fill.BackgroundColor.SetColor(legendItems[i].Color);
            }

            // Format the legend sheet
            sheet.Column(1).Width = 15;
            sheet.Column(2).Width = 60;
        }

        private void StyleSheetBorders(ExcelRange range)
        {
            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
        }
    }
} 