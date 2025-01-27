using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BOMVIEW.Interfaces;
using BOMVIEW.Models;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace BOMVIEW.Services
{
    public class ExternalSupplierExporter
    {
        private readonly ILogger _logger;
        private readonly ExternalSupplierService _externalSupplierService;

        public ExternalSupplierExporter(ILogger logger, ExternalSupplierService externalSupplierService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _externalSupplierService = externalSupplierService ?? throw new ArgumentNullException(nameof(externalSupplierService));
        }

        public async Task ExportExternalSupplierItemsAsync(
            List<BomEntry> entries,
            string filePath)
        {
            try
            {
                using var package = new ExcelPackage();
                var worksheet = package.Workbook.Worksheets.Add("External Suppliers");

                // Set headers
                worksheet.Cells[1, 1].Value = "Ordering Code";
                worksheet.Cells[1, 2].Value = "Designator";
                worksheet.Cells[1, 3].Value = "Value";
                worksheet.Cells[1, 4].Value = "PCB Footprint";
                worksheet.Cells[1, 5].Value = "Quantity";
                worksheet.Cells[1, 6].Value = "Supplier Name";
                worksheet.Cells[1, 7].Value = "Unit Price";
                worksheet.Cells[1, 8].Value = "Total Price";
                worksheet.Cells[1, 9].Value = "Availability";
                worksheet.Cells[1, 10].Value = "Estimated Delivery Date";

                // Style headers
                var headerRange = worksheet.Cells[1, 1, 1, 10];
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                headerRange.Style.Fill.BackgroundColor.SetColor(Color.LightGray);

                int row = 2;
                foreach (var entry in entries)
                {
                    // Only include entries with external suppliers
                    if (!entry.HasExternalSupplier)
                        continue;

                    // Find the corresponding external supplier entry
                    var externalSupplier = _externalSupplierService.GetByBomEntryNum(entry.Num);
                    if (externalSupplier == null)
                        continue;

                    // Write data
                    worksheet.Cells[row, 1].Value = entry.OrderingCode;
                    worksheet.Cells[row, 2].Value = entry.Designator;
                    worksheet.Cells[row, 3].Value = entry.Value;
                    worksheet.Cells[row, 4].Value = entry.PcbFootprint;
                    worksheet.Cells[row, 5].Value = entry.QuantityTotal;
                    worksheet.Cells[row, 6].Value = externalSupplier.SupplierName;
                    worksheet.Cells[row, 7].Value = externalSupplier.UnitPrice;
                    worksheet.Cells[row, 8].Value = externalSupplier.TotalPrice;
                    worksheet.Cells[row, 9].Value = externalSupplier.Availability;
                    worksheet.Cells[row, 10].Value = externalSupplier.EstimatedDeliveryDate;

                    // Style rows
                    var rowRange = worksheet.Cells[row, 1, row, 10];
                    rowRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    rowRange.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 245, 243, 255)); // Light purple for external suppliers

                    // Format price columns
                    worksheet.Cells[row, 7].Style.Numberformat.Format = "$#,##0.00000";
                    worksheet.Cells[row, 8].Style.Numberformat.Format = "$#,##0.00000";

                    // Format date column
                    if (externalSupplier.EstimatedDeliveryDate.HasValue)
                    {
                        worksheet.Cells[row, 10].Style.Numberformat.Format = "yyyy-mm-dd";
                    }

                    row++;
                }

                // Add borders and auto-fit
                var dataRange = worksheet.Cells[1, 1, Math.Max(2, row - 1), 10];
                dataRange.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                dataRange.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                dataRange.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                dataRange.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                dataRange.AutoFitColumns();

                // Add a note
                if (row > 2)  // Only add the note if there are external supplier entries
                {
                    var noteRow = row + 2;
                    worksheet.Cells[noteRow, 1].Value = "Note:";
                    worksheet.Cells[noteRow, 1].Style.Font.Bold = true;
                    worksheet.Cells[noteRow + 1, 1].Value = "These parts are sourced from external suppliers and require manual ordering.";
                    worksheet.Cells[noteRow + 2, 1].Value = $"Last updated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                }

                await package.SaveAsAsync(new FileInfo(filePath));
                _logger.LogSuccess($"Successfully exported external supplier items to {filePath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error exporting external supplier items: {ex.Message}");
                throw;
            }
        }
    }
}