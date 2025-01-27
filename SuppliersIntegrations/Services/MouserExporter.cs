using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;
using BOMVIEW.Models;
using BOMVIEW.Interfaces;
using System.IO;
using System.Windows;

namespace BOMVIEW.Services
{
    public class MouserExporter
    {
        private readonly ILogger _logger;
        private readonly MouserService _mouserService;
        private readonly string _originalFilePath;


        public MouserExporter(ILogger logger, MouserService mouserService, string originalFilePath)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mouserService = mouserService ?? throw new ArgumentNullException(nameof(mouserService));
            _originalFilePath = originalFilePath;
        }

        public async Task ExportMouserListsAsync(List<BomEntry> entries, string originalFilePath)
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
                string mouserListPath = Path.Combine(bomFolderPath, $"{baseFileName}_MS_List.xlsx");
                string mouserBestPricesPath = Path.Combine(bomFolderPath, $"{baseFileName}_MS_Best_Prices.xlsx");

                // Export all Mouser items
                await ExportMouserItemsAsync(
                    entries,
                    mouserListPath,
                    originalFileName,
                    baseFileName,
                    onlyBestPrice: false
                );

                // Export best price Mouser items
                await ExportMouserItemsAsync(
                    entries,
                    mouserBestPricesPath,
                    originalFileName,
                    baseFileName,
                    onlyBestPrice: true
                );

                _logger.LogSuccess("Successfully exported Mouser lists");

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
                _logger.LogError($"Error exporting Mouser lists: {ex.Message}");
                throw;
            }
        }

        public async Task ExportMouserItemsAsync(
    List<BomEntry> entries,
    string filePath,
    string originalFileName,
    string savedFileName,
    bool onlyBestPrice)
        {
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Mouser List");

            // Set headers with specific columns
            worksheet.Cells[1, 1].Value = "Ordering Code";
            worksheet.Cells[1, 2].Value = "Mouser Part Number";
            worksheet.Cells[1, 3].Value = "Description";
            worksheet.Cells[1, 4].Value = "Quantity";
            worksheet.Cells[1, 5].Value = "Original Quantity";
            worksheet.Cells[1, 6].Value = "PCB Footprint";

            // Style headers
            var headerRange = worksheet.Cells[1, 1, 1, 6];
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
            headerRange.Style.Fill.BackgroundColor.SetColor(Color.LightGray);

            int row = 2;
            foreach (var entry in entries)
            {
                // Skip if not in Mouser inventory or has external supplier
                if (!(entry.MouserData?.IsAvailable ?? false) ||
                   entry.MouserOrderQuantity <= 0 ||
                   entry.HasExternalSupplier ||
                   (onlyBestPrice && entry.BestCurrentSupplier != "Mouser"))
                    continue;

                // Write data with specific columns
                worksheet.Cells[row, 1].Value = entry.OrderingCode;
                worksheet.Cells[row, 2].Value = entry.MouserData?.MouserPartNumber ?? "";
                worksheet.Cells[row, 3].Value = savedFileName;  // Using saved filename for Description
                worksheet.Cells[row, 4].Value = entry.MouserOrderQuantity;
                worksheet.Cells[row, 5].Value = entry.QuantityTotal;
                worksheet.Cells[row, 6].Value = entry.PcbFootprint;

                // Style rows based on conditions
                var rowRange = worksheet.Cells[row, 1, row, 6];
                rowRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                rowRange.Style.Fill.BackgroundColor.SetColor(Color.White); // הוסף את זה

                if (onlyBestPrice)
                {
                    if (!(entry.DigiKeyData?.IsAvailable ?? false))
                    {
                        // Red for products only available in Mouser
                        rowRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        rowRange.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 255, 200, 200));
                    }
                    else if (entry.MouserCurrentTotalPrice < entry.DigiKeyCurrentTotalPrice)
                    {
                        // Green for better price
                        rowRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        rowRange.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 232, 245, 233));
                    }

                    // Highlight minimum quantity adjustments
                    if (entry.MouserOrderQuantity > entry.QuantityTotal)
                    {
                        worksheet.Cells[row, 4].Style.Font.Bold = true;
                        worksheet.Cells[row, 4].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        worksheet.Cells[row, 4].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 255, 235, 156));
                    }
                }

                row++;
            }

            // Add borders and ensure text visibility without overriding colors
            var dataRange = worksheet.Cells[1, 1, row - 1,6];
            StyleSheetBorders(dataRange);
            dataRange.Style.Font.Color.SetColor(Color.Black);


            

            if (onlyBestPrice)
            {
                var legendSheet = package.Workbook.Worksheets.Add("Legend");
                AddMouserBestPricesLegend(legendSheet);
            }

            await package.SaveAsAsync(new FileInfo(filePath));
        }

        private void AddMouserBestPricesLegend(ExcelWorksheet sheet)
        {
            // Header
            sheet.Cells[1, 1].Value = "Mouser Best Prices Legend";
            sheet.Cells[1, 1].Style.Font.Bold = true;
            sheet.Cells[1, 1].Style.Font.Size = 14;

            var legendItems = new (string Text, Color Color)[]
            {
                ("Best Price (Better than DigiKey)", Color.FromArgb(255, 232, 245, 233)),
                ("Only Available from Mouser", Color.FromArgb(255, 255, 200, 200)),
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

            // Add explanation section
            int explanationStartRow = legendItems.Length + 6;
            sheet.Cells[explanationStartRow, 1].Value = "Notes:";
            sheet.Cells[explanationStartRow, 1].Style.Font.Bold = true;

            sheet.Cells[explanationStartRow + 1, 1].Value =
                "- Order Quantity may be adjusted to meet minimum order requirements based on unit price\n" +
                "- Original quantity is preserved if it already meets the minimum\n" +
                "- Highlighted quantities indicate where minimum order requirements were applied";

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