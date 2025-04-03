using System.Drawing;
using System.IO;
using System.Windows;
using BOMVIEW.Interfaces;
using BOMVIEW.Models;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace BOMVIEW.Services
{
    public class DigiKeyExporter
    {
        private readonly ILogger _logger;
        private readonly DigiKeyService _digiKeyService;
        private readonly string _originalFilePath;



        public DigiKeyExporter(ILogger logger, DigiKeyService digiKeyService, string originalFilePath)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _digiKeyService = digiKeyService ?? throw new ArgumentNullException(nameof(digiKeyService));
            _originalFilePath = originalFilePath;
        }

        public async Task ExportDigiKeyListsAsync(List<BomEntry> entries, string originalFilePath)
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
                string dkListPath = Path.Combine(bomFolderPath, $"{baseFileName}_DK_List.xlsx");
                string dkBestPricesPath = Path.Combine(bomFolderPath, $"{baseFileName}_DK_Best_Prices.xlsx");
                string dkILListPath = Path.Combine(bomFolderPath, $"{baseFileName}_DK_IL_List.xlsx");
                string dkILBestPricesPath = Path.Combine(bomFolderPath, $"{baseFileName}_DK_IL_Best_Prices.xlsx");

                // Export all DigiKey items
                await ExportDigiKeyItemsAsync(
                    entries,
                    dkListPath,
                    originalFileName,
                    baseFileName,
                    onlyBestPrice: false
                );

                // Export best price DigiKey items
                await ExportDigiKeyItemsAsync(
                    entries,
                    dkBestPricesPath,
                    originalFileName,
                    baseFileName,
                    onlyBestPrice: true
                );

                // Export all DigiKey IL items
                await ExportDigiKeyILItemsAsync(
                    entries,
                    dkILListPath,
                    originalFileName,
                    baseFileName,
                    onlyBestPrice: false
                );

                // Export best price DigiKey IL items
                await ExportDigiKeyILItemsAsync(
                    entries,
                    dkILBestPricesPath,
                    originalFileName,
                    baseFileName,
                    onlyBestPrice: true
                );

                _logger.LogSuccess("Successfully exported DigiKey lists");

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
                _logger.LogError($"Error exporting DigiKey lists: {ex.Message}");
                throw;
            }
        }

        public async Task ExportDigiKeyItemsAsync(
      List<BomEntry> entries,
      string filePath,
      string originalFileName,
      string savedFileName,
      bool onlyBestPrice)
        {
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("DigiKey List");

            // Set headers
            worksheet.Cells[1, 1].Value = "Ordering Code";
            worksheet.Cells[1, 2].Value = "DigiKey Part Number";
            worksheet.Cells[1, 3].Value = "Customer Reference";
            worksheet.Cells[1, 4].Value = "Designator";
            worksheet.Cells[1, 5].Value = "Quantity";
            worksheet.Cells[1, 6].Value = "Original Quantity";

            // Style headers
            var headerRange = worksheet.Cells[1, 1, 1, 6];
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
            headerRange.Style.Fill.BackgroundColor.SetColor(Color.LightGray);

            int row = 2;
            bool hasData = false;

            foreach (var entry in entries)
            {
                if (!(entry.DigiKeyData?.IsAvailable ?? false) ||
                    entry.DigiKeyOrderQuantity <= 0 ||
                    entry.HasExternalSupplier ||
                    (onlyBestPrice && entry.BestCurrentSupplier != "DigiKey"))
                    continue;

                hasData = true;

                // Write data
                worksheet.Cells[row, 1].Value = entry.OrderingCode;
                worksheet.Cells[row, 2].Value = entry.DigiKeyData?.DigiKeyPartNumber ?? "";
                worksheet.Cells[row, 3].Value = savedFileName;
                worksheet.Cells[row, 4].Value = entry.Designator;
                worksheet.Cells[row, 5].Value = entry.DigiKeyOrderQuantity;
                worksheet.Cells[row, 6].Value = entry.QuantityTotal;

                // Style rows based on conditions
                var rowRange = worksheet.Cells[row, 1, row, 6];
                rowRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                rowRange.Style.Fill.BackgroundColor.SetColor(Color.White);

                if (onlyBestPrice)
                {
                    if (!(entry.MouserData?.IsAvailable ?? false))
                    {
                        // Red for products only available in DigiKey
                        rowRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        rowRange.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 255, 200, 200));
                    }
                    else if (entry.DigiKeyCurrentTotalPrice < entry.MouserCurrentTotalPrice)
                    {
                        // Green for better price
                        rowRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        rowRange.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 232, 245, 233));
                    }

                    // Highlight minimum quantity adjustments
                    if (entry.DigiKeyOrderQuantity > entry.QuantityTotal)
                    {
                        worksheet.Cells[row, 5].Style.Font.Bold = true;
                        worksheet.Cells[row, 5].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        worksheet.Cells[row, 5].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 255, 235, 156));
                    }
                }

                row++;
            }

            // If no data entries, add a placeholder empty row to ensure valid Excel structure
            if (!hasData)
            {
                // Add a single empty data row to ensure Excel structure is valid
                row = 2;
                var emptyRowRange = worksheet.Cells[row, 1, row, 6];
                emptyRowRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                emptyRowRange.Style.Fill.BackgroundColor.SetColor(Color.White);
                row++;
            }

            // Add borders and styling to all rows including headers
            var dataRange = worksheet.Cells[1, 1, row - 1, 6];
            StyleSheetBorders(dataRange);
            dataRange.Style.Font.Color.SetColor(Color.Black);

            // Auto-fit columns for better readability
            for (int i = 1; i <= 6; i++)
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

        private void AddBestPricesLegend(ExcelWorksheet sheet, bool isIL = false)
        {
            // Header
            sheet.Cells[1, 1].Value = $"{(isIL ? "DigiKey IL" : "DigiKey")} Best Prices Legend";
            sheet.Cells[1, 1].Style.Font.Bold = true;
            sheet.Cells[1, 1].Style.Font.Size = 14;

            var legendItems = new (string Text, Color Color)[]
            {
        ("Best Price (Better than Mouser)", Color.FromArgb(255, 232, 245, 233)),
        ($"Only Available from {(isIL ? "DigiKey IL" : "DigiKey")}", Color.FromArgb(255, 255, 200, 200)),
        ("Minimum Order Quantity Applied", Color.FromArgb(255, 255, 235, 156))
            };

            // Add legend items
            sheet.Cells[3, 1].Value = "Color Coding:";
            sheet.Cells[3, 1].Style.Font.Bold = true;

            for (int i = 0; i < legendItems.Length; i++)
            {
                var row = i + 4;
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

        public async Task ExportDigiKeyILItemsAsync(
    List<BomEntry> entries,
    string filePath,
    string originalFileName,
    string savedFileName,
    bool onlyBestPrice)
        {
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("DigiKey IL List");

            // Set headers - EXACTLY the same as regular DigiKey
            worksheet.Cells[1, 1].Value = "Ordering Code";
            worksheet.Cells[1, 2].Value = "DigiKey Part Number";
            worksheet.Cells[1, 3].Value = "Customer Reference";
            worksheet.Cells[1, 4].Value = "Designator";
            worksheet.Cells[1, 5].Value = "Quantity";
            worksheet.Cells[1, 6].Value = "Original Quantity";

            // Style headers
            var headerRange = worksheet.Cells[1, 1, 1, 6];
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
            headerRange.Style.Fill.BackgroundColor.SetColor(Color.LightGray);

            int row = 2;
            bool hasData = false;

            foreach (var entry in entries)
            {
                if (!(entry.IsraelData?.IsAvailable ?? false) ||
                    entry.IsraelOrderQuantity <= 0 ||
                    entry.HasExternalSupplier ||
                    (onlyBestPrice && entry.BestCurrentSupplier != "DigiKeyIL"))
                    continue;

                hasData = true;

                // Write data
                worksheet.Cells[row, 1].Value = entry.OrderingCode;
                worksheet.Cells[row, 2].Value = entry.IsraelData?.DigiKeyPartNumber ?? "";
                worksheet.Cells[row, 3].Value = savedFileName;
                worksheet.Cells[row, 4].Value = entry.Designator;
                worksheet.Cells[row, 5].Value = entry.IsraelOrderQuantity;
                worksheet.Cells[row, 6].Value = entry.QuantityTotal;

                // Style rows based on conditions
                var rowRange = worksheet.Cells[row, 1, row, 6];
                rowRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                rowRange.Style.Fill.BackgroundColor.SetColor(Color.White);

                if (onlyBestPrice)
                {
                    if (!(entry.MouserData?.IsAvailable ?? false))
                    {
                        // Red for products only available in DigiKeyIL
                        rowRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        rowRange.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 255, 200, 200));
                    }
                    else if (entry.IsraelCurrentTotalPrice < entry.MouserCurrentTotalPrice)
                    {
                        // Green for better price
                        rowRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        rowRange.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 232, 245, 233));
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

            // If no data entries, add a placeholder empty row to ensure valid Excel structure
            if (!hasData)
            {
                // Add a single empty data row to ensure Excel structure is valid
                row = 2;
                var emptyRowRange = worksheet.Cells[row, 1, row, 6];
                emptyRowRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                emptyRowRange.Style.Fill.BackgroundColor.SetColor(Color.White);
                row++;
            }

            // Add borders and styling to all rows including headers
            var dataRange = worksheet.Cells[1, 1, row - 1, 6];
            StyleSheetBorders(dataRange);
            dataRange.Style.Font.Color.SetColor(Color.Black);

            // Auto-fit columns for better readability
            for (int i = 1; i <= 6; i++)
            {
                worksheet.Column(i).AutoFit();
            }

            if (onlyBestPrice)
            {
                var legendSheet = package.Workbook.Worksheets.Add("Legend");
                AddBestPricesLegend(legendSheet, true);
            }

            await package.SaveAsAsync(new FileInfo(filePath));
        }
    }
}