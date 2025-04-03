using OfficeOpenXml;
using BOMVIEW.Models;
using BOMVIEW.Services;
using BOMVIEW.Exceptions;
using System.Data;
using System.IO;
using OfficeOpenXml.Style;
using System.Drawing;
public class ExcelService : IExcelService
{
    private const int MAX_PREVIEW_ROWS = 5;


    private static class ExcelFormats
    {
        public const string Currency = "$#,##0.00000";
        public const string Quantity = "#,##0";

        public static class Colors
        {
            public static readonly System.Drawing.Color DigiKeyBackground = System.Drawing.Color.FromArgb(255, 227, 242, 253);
            public static readonly System.Drawing.Color DigiKeyILBackground = System.Drawing.Color.FromArgb(255, 217, 232, 243);
            public static readonly System.Drawing.Color MouserBackground = System.Drawing.Color.FromArgb(255, 232, 245, 233);
            public static readonly System.Drawing.Color FarnellBackground = System.Drawing.Color.FromArgb(255, 255, 243, 224);
            public static readonly System.Drawing.Color OutOfStockBackground = System.Drawing.Color.FromArgb(255, 255, 200, 200);
            public static readonly System.Drawing.Color UserEnteredBackground = System.Drawing.Color.FromArgb(255, 245, 245, 220);
            public static readonly System.Drawing.Color HeaderBackground = System.Drawing.Color.LightGray;
            public static readonly System.Drawing.Color TotalRowBackground = System.Drawing.Color.FromArgb(255, 242, 242, 242);
            public static readonly System.Drawing.Color BetterPriceBackground = System.Drawing.Color.FromArgb(255, 232, 245, 233);
            public static readonly System.Drawing.Color ExternalSupplierBackground = System.Drawing.Color.FromArgb(225, 215, 213, 255);

        }
    }
    public ExcelService()
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    public ExcelSheetPreview GetExcelPreview(string filePath)
    {
        if (!ValidateExcelFile(filePath))
            throw new ExcelServiceException("Invalid file format or file does not exist");

        var preview = new ExcelSheetPreview();
        using var package = new ExcelPackage(new FileInfo(filePath));

        // Get all sheet names
        preview.SheetNames = package.Workbook.Worksheets.Select(ws => ws.Name).ToList();

        if (preview.SheetNames.Any())
        {
            var worksheet = package.Workbook.Worksheets[0];
            LoadSheetPreview(worksheet, preview);
        }

        return preview;
    }

    private void LoadSheetPreview(ExcelWorksheet worksheet, ExcelSheetPreview preview)
    {
        try
        {
            int colCount = worksheet.Dimension?.End.Column ?? 0;
            int rowCount = worksheet.Dimension?.End.Row ?? 0;

            // Read headers
            for (int col = 1; col <= colCount; col++)
            {
                var headerText = worksheet.Cells[1, col].Text?.Trim() ?? string.Empty;
                preview.Headers.Add(headerText);
                preview.ColumnLetters.Add(GetExcelColumnName(col));
            }

            // Read preview rows
            for (int row = 2; row <= Math.Min(rowCount, MAX_PREVIEW_ROWS + 1); row++)
            {
                var rowData = new List<string>();
                for (int col = 1; col <= colCount; col++)
                {
                    rowData.Add(worksheet.Cells[row, col].Text?.Trim() ?? string.Empty);
                }
                preview.PreviewRows.Add(rowData);
            }
        }
        catch (Exception ex)
        {
            throw new ExcelServiceException($"Error loading sheet preview: {ex.Message}");
        }
    }

    public async Task<List<BomEntry>> ReadBomFileAsync(string filePath, ExcelMappingConfiguration config)
    {
        if (!ValidateExcelFile(filePath))
            throw new ExcelServiceException("Invalid file format or file does not exist");

        var bomEntries = new List<BomEntry>();
        using var package = new ExcelPackage(new FileInfo(filePath));

        var worksheet = package.Workbook.Worksheets[config.SelectedSheet]
            ?? throw new ExcelServiceException($"Sheet '{config.SelectedSheet}' not found");

        int currentRow = config.StartRow;
        int currentNum = 1;
        const int maxEmptyRowsToCheck = 5;
        int worksheetEndRow = worksheet.Dimension.End.Row;

        while (currentRow <= worksheetEndRow)
        {
            // Get the ordering code for the current row
            string orderingCode = GetCellValue(worksheet, currentRow, config.GetColumnForField("OrderingCode"));

            if (string.IsNullOrWhiteSpace(orderingCode))
            {
                // Look ahead for data
                bool foundDataAhead = false;
                int lastCheckedRow = Math.Min(currentRow + maxEmptyRowsToCheck, worksheetEndRow);

                for (int futureRow = currentRow + 1; futureRow <= lastCheckedRow; futureRow++)
                {
                    string futureOrderingCode = GetCellValue(worksheet, futureRow, config.GetColumnForField("OrderingCode"));
                    if (!string.IsNullOrWhiteSpace(futureOrderingCode))
                    {
                        foundDataAhead = true;
                        break;
                    }
                }

                if (!foundDataAhead)
                {
                    break;
                }

                currentRow++;
                continue;
            }

            try
            {
                var quantityForOne = ParseIntOrDefault(GetCellValue(worksheet, currentRow, config.GetColumnForField("QuantityForOne")));

                var entry = new BomEntry
                {
                    Num = currentNum++,
                    OrderingCode = orderingCode,
                    QuantityForOne = quantityForOne,
                    QuantityTotal = config.CalculateQuantityWithAssembly(quantityForOne)
                };

                // Only populate fields that are in the mandatory fields set
                if (config.MandatoryFields.Contains("Designator"))
                    entry.Designator = GetCellValue(worksheet, currentRow, config.GetColumnForField("Designator"));

                if (config.MandatoryFields.Contains("Value"))
                    entry.Value = GetCellValue(worksheet, currentRow, config.GetColumnForField("Value"));

                if (config.MandatoryFields.Contains("PcbFootprint"))
                    entry.PcbFootprint = GetCellValue(worksheet, currentRow, config.GetColumnForField("PcbFootprint"));

                entry.IsUserEntered = IsUserEnteredData(entry);

                if (ValidateEntry(entry, config.MandatoryFields))
                {
                    bomEntries.Add(entry);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing row {currentRow}: {ex.Message}");
            }

            currentRow++;
        }

        return bomEntries;
    }
    private bool IsUserEnteredData(BomEntry entry)
    {
        return !string.IsNullOrWhiteSpace(entry.OrderingCode) ||
               !string.IsNullOrWhiteSpace(entry.Designator) ||
               !string.IsNullOrWhiteSpace(entry.Value) ||
               !string.IsNullOrWhiteSpace(entry.PcbFootprint) ||
               entry.QuantityForOne > 0;
    }

    private bool ValidateEntry(BomEntry entry, HashSet<string> mandatoryFields)
    {
        if (mandatoryFields.Contains("OrderingCode") && string.IsNullOrWhiteSpace(entry.OrderingCode))
            return false;

        return true;
    }

    private string GetCellValue(ExcelWorksheet worksheet, int row, string columnLetter)
    {
        if (string.IsNullOrEmpty(columnLetter))
            return string.Empty;

        int col = ConvertColumnLetterToNumber(columnLetter);
        return worksheet.Cells[row, col].Text;
    }

    private int ConvertColumnLetterToNumber(string columnLetter)
    {
        int column = 0;
        int mul = 1;

        for (int i = columnLetter.Length - 1; i >= 0; i--)
        {
            column += (columnLetter[i] - 'A' + 1) * mul;
            mul *= 26;
        }

        return column;
    }

    public async Task SaveBomFileAsync(string filePath, List<BomEntry> entries)
    {
        try
        {
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("BOM");
            var missingComponentsSheet = package.Workbook.Worksheets.Add("Missing Components");

            var headers = new[]
            {
            "Ordering Code", "Designator", "Value", "PCB Footprint",
            "Original Quantity", "Testview Stock",
            "DK Order Quantity", "DK Unit Price", "DK Total Price", "DK Stock",
            "DK-IL Order Quantity", "DK-IL Unit Price", "DK-IL Total Price", "DK-IL Stock",
            "MS Order Quantity", "MS Unit Price", "MS Total Price", "MS Stock",
            "FR Order Quantity", "FR Unit Price", "FR Total Price", "FR Stock",
            "Best Price"
        };
            // Write headers
            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cells[1, i + 1].Value = headers[i];
            }

            // Style headers
            using (var range = worksheet.Cells[1, 1, 1, headers.Length])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(ExcelFormats.Colors.HeaderBackground);
            }

            // Data rows
            int row = 2;
            foreach (var entry in entries)
            {
                WriteEntryRow(worksheet, row, entry);
                row++;
            }

            // Ensure we have at least one data row for calculations
            if (row <= 2)
            {
                row = 3; // Add at least one empty row if there are no entries
            }

            // Add totals row
            var totalRow = row;
            worksheet.Cells[totalRow, 1].Value = "TOTALS";
            worksheet.Cells[totalRow, 1].Style.Font.Bold = true;

            // Only calculate totals if there's at least one data row
            if (row > 2)
            {
                // Calculate totals for Unit Price columns
                worksheet.Cells[totalRow, 8].Formula = $"SUM(H2:H{row - 1})";  // DK Unit Price
                worksheet.Cells[totalRow, 12].Formula = $"SUM(L2:L{row - 1})"; // DK-IL Unit Price
                worksheet.Cells[totalRow, 16].Formula = $"SUM(P2:P{row - 1})"; // MS Unit Price
                worksheet.Cells[totalRow, 20].Formula = $"SUM(T2:T{row - 1})"; // FR Unit Price

                // Calculate totals for Total Price columns
                worksheet.Cells[totalRow, 9].Formula = $"SUM(I2:I{row - 1})";  // DK Total Price
                worksheet.Cells[totalRow, 13].Formula = $"SUM(M2:M{row - 1})"; // DK-IL Total Price
                worksheet.Cells[totalRow, 17].Formula = $"SUM(Q2:Q{row - 1})"; // MS Total Price
                worksheet.Cells[totalRow, 21].Formula = $"SUM(U2:U{row - 1})"; // FR Total Price
                worksheet.Cells[totalRow, 23].Formula = $"SUM(W2:W{row - 1})"; // Best Price
            }
            else
            {
                // Set zeros for all totals if no data rows
                var totalColumns = new[] { 8, 9, 12, 13, 16, 17, 20, 21, 23 };
                foreach (var col in totalColumns)
                {
                    worksheet.Cells[totalRow, col].Value = 0;
                }
            }

            // Format totals for Unit Price columns
            worksheet.Cells[totalRow, 8].Style.Numberformat.Format = ExcelFormats.Currency;
            worksheet.Cells[totalRow, 12].Style.Numberformat.Format = ExcelFormats.Currency;
            worksheet.Cells[totalRow, 16].Style.Numberformat.Format = ExcelFormats.Currency;
            worksheet.Cells[totalRow, 20].Style.Numberformat.Format = ExcelFormats.Currency;

            // Format totals for Total Price columns
            worksheet.Cells[totalRow, 9].Style.Numberformat.Format = ExcelFormats.Currency;
            worksheet.Cells[totalRow, 13].Style.Numberformat.Format = ExcelFormats.Currency;
            worksheet.Cells[totalRow, 17].Style.Numberformat.Format = ExcelFormats.Currency;
            worksheet.Cells[totalRow, 21].Style.Numberformat.Format = ExcelFormats.Currency;
            worksheet.Cells[totalRow, 23].Style.Numberformat.Format = ExcelFormats.Currency;

            // Style the totals row based on availability across suppliers
            if (entries.Any())
            {
                StyleTotalsRow(worksheet, totalRow, entries);
            }
            else
            {
                // Default styling for empty totals row
                var totalsRange = worksheet.Cells[totalRow, 1, totalRow, headers.Length];
                totalsRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                totalsRange.Style.Fill.BackgroundColor.SetColor(ExcelFormats.Colors.TotalRowBackground);
                totalsRange.Style.Font.Bold = true;
            }

            // Add missing components sheet
            WriteMissingComponentsSheet(missingComponentsSheet, entries);

            // Add legend
            AddLegend(worksheet, totalRow + 3);

            // Auto-fit columns - only if we have data
            if (totalRow > 2)
            {
                worksheet.Cells[1, 1, totalRow, headers.Length].AutoFitColumns();
            }
            else
            {
                worksheet.Cells[1, 1, 2, headers.Length].AutoFitColumns();
            }

            // Set specific column widths
            worksheet.Column(2).Width = 13;  // B
            worksheet.Column(3).Width = 10;  // C
            worksheet.Column(4).Width = 13;  // D
            worksheet.Column(8).Width = 14;  // H - DK Unit Price
            worksheet.Column(9).Width = 14;  // I - DK Total Price
            worksheet.Column(12).Width = 14; // L - DK-IL Unit Price
            worksheet.Column(13).Width = 14; // M - DK-IL Total Price
            worksheet.Column(16).Width = 14; // P - MS Unit Price
            worksheet.Column(17).Width = 14; // Q - MS Total Price
            worksheet.Column(20).Width = 14; // T - FR Unit Price
            worksheet.Column(21).Width = 14; // U - FR Total Price
            worksheet.Column(23).Width = 14; // W - Best Price

            // Add borders - ensure valid range
            var endRow = Math.Max(2, totalRow);
            var dataRange = worksheet.Cells[1, 1, endRow, headers.Length];
            dataRange.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;

            await package.SaveAsAsync(new FileInfo(filePath));
        }
        catch (Exception ex)
        {
            throw new Exception($"Save error: {ex.Message}", ex);
        }
    }


    private void StyleTotalsRow(ExcelWorksheet worksheet, int totalRow, List<BomEntry> entries)
    {
        // Default styling for the whole row
        // Check if worksheet.Dimension is null (which happens with empty sheets)
        int endColumn = worksheet.Dimension?.End.Column ?? 23; // Default to the number of columns we know we're using
        var totalsRange = worksheet.Cells[totalRow, 1, totalRow, endColumn];
        totalsRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
        totalsRange.Style.Fill.BackgroundColor.SetColor(ExcelFormats.Colors.TotalRowBackground);
        totalsRange.Style.Font.Bold = true;

        // DigiKey section styling
        bool anyDigiKeyAvailable = entries.Any(e => e.DigiKeyData?.IsAvailable ?? false);
        var dkTotalsRange = worksheet.Cells[totalRow, 7, totalRow, 10];
        dkTotalsRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
        dkTotalsRange.Style.Fill.BackgroundColor.SetColor(
            anyDigiKeyAvailable ?
                ExcelFormats.Colors.DigiKeyBackground :
                ExcelFormats.Colors.OutOfStockBackground);

        // DigiKey-IL section styling
        bool anyDigiKeyILAvailable = entries.Any(e => e.IsraelData?.IsAvailable ?? false);
        var dkILTotalsRange = worksheet.Cells[totalRow, 11, totalRow, 14];
        dkILTotalsRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
        dkILTotalsRange.Style.Fill.BackgroundColor.SetColor(
            anyDigiKeyILAvailable ?
                ExcelFormats.Colors.DigiKeyILBackground :
                ExcelFormats.Colors.OutOfStockBackground);

        // Mouser section styling
        bool anyMouserAvailable = entries.Any(e => e.MouserData?.IsAvailable ?? false);
        var msTotalsRange = worksheet.Cells[totalRow, 15, totalRow, 18];
        msTotalsRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
        msTotalsRange.Style.Fill.BackgroundColor.SetColor(
            anyMouserAvailable ?
                ExcelFormats.Colors.MouserBackground :
                ExcelFormats.Colors.OutOfStockBackground);

        // Farnell section styling
        bool anyFarnellAvailable = entries.Any(e => e.FarnellData?.IsAvailable ?? false);
        var frTotalsRange = worksheet.Cells[totalRow, 19, totalRow, 22];
        frTotalsRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
        frTotalsRange.Style.Fill.BackgroundColor.SetColor(
            anyFarnellAvailable ?
                ExcelFormats.Colors.FarnellBackground :
                ExcelFormats.Colors.OutOfStockBackground);

        // Best Price cell styling
        var bestPriceTotal = worksheet.Cells[totalRow, 23];
        bestPriceTotal.Style.Fill.PatternType = ExcelFillStyle.Solid;

        // Determine which supplier has the best overall price (lowest non-zero sum)
        decimal dkTotal = entries.Sum(e => e.DigiKeyCurrentTotalPrice);
        decimal dkILTotal = entries.Sum(e => e.IsraelCurrentTotalPrice);
        decimal msTotal = entries.Sum(e => e.MouserCurrentTotalPrice);
        decimal frTotal = entries.Sum(e => e.FarnellCurrentTotalPrice);

        // Find the lowest non-zero total
        var validTotals = new List<(string supplier, decimal total)>();
        if (dkTotal > 0 && anyDigiKeyAvailable) validTotals.Add(("DigiKey", dkTotal));
        if (dkILTotal > 0 && anyDigiKeyILAvailable) validTotals.Add(("DigiKey-IL", dkILTotal));
        if (msTotal > 0 && anyMouserAvailable) validTotals.Add(("Mouser", msTotal));
        if (frTotal > 0 && anyFarnellAvailable) validTotals.Add(("Farnell", frTotal));

        if (validTotals.Any())
        {
            var bestSupplier = validTotals.OrderBy(t => t.total).First().supplier;
            switch (bestSupplier)
            {
                case "DigiKey":
                    bestPriceTotal.Style.Fill.BackgroundColor.SetColor(ExcelFormats.Colors.DigiKeyBackground);
                    break;
                case "DigiKey-IL":
                    bestPriceTotal.Style.Fill.BackgroundColor.SetColor(ExcelFormats.Colors.DigiKeyILBackground);
                    break;
                case "Mouser":
                    bestPriceTotal.Style.Fill.BackgroundColor.SetColor(ExcelFormats.Colors.MouserBackground);
                    break;
                case "Farnell":
                    bestPriceTotal.Style.Fill.BackgroundColor.SetColor(ExcelFormats.Colors.FarnellBackground);
                    break;
            }
        }
        else
        {
            bestPriceTotal.Style.Fill.BackgroundColor.SetColor(ExcelFormats.Colors.OutOfStockBackground);
        }
    }

    private void StyleSupplierSections(ExcelWorksheet worksheet, int row, BomEntry entry)
    {
        // DigiKey section (columns 7-10)
        var digiKeyRange = worksheet.Cells[row, 7, row, 10];
        digiKeyRange.Style.Fill.PatternType = ExcelFillStyle.Solid;

        // Handle out-of-stock condition more explicitly
        bool digiKeyAvailable = entry.DigiKeyData?.IsAvailable ?? false;
        bool digiKeyHasStock = (entry.DigiKeyData?.Availability ?? 0) > 0;

        digiKeyRange.Style.Fill.BackgroundColor.SetColor(
            digiKeyAvailable && digiKeyHasStock ?
                ExcelFormats.Colors.DigiKeyBackground :
                ExcelFormats.Colors.OutOfStockBackground);

        // DigiKey-IL section (columns 11-14)
        var digiKeyILRange = worksheet.Cells[row, 11, row, 14];
        digiKeyILRange.Style.Fill.PatternType = ExcelFillStyle.Solid;

        bool digiKeyILAvailable = entry.IsraelData?.IsAvailable ?? false;
        bool digiKeyILHasStock = (entry.IsraelData?.Availability ?? 0) > 0;

        digiKeyILRange.Style.Fill.BackgroundColor.SetColor(
            digiKeyILAvailable && digiKeyILHasStock ?
                ExcelFormats.Colors.DigiKeyILBackground :
                ExcelFormats.Colors.OutOfStockBackground);

        // Mouser section (columns 15-18)
        var mouserRange = worksheet.Cells[row, 15, row, 18];
        mouserRange.Style.Fill.PatternType = ExcelFillStyle.Solid;

        bool mouserAvailable = entry.MouserData?.IsAvailable ?? false;
        bool mouserHasStock = (entry.MouserData?.Availability ?? 0) > 0;

        mouserRange.Style.Fill.BackgroundColor.SetColor(
            mouserAvailable && mouserHasStock ?
                ExcelFormats.Colors.MouserBackground :
                ExcelFormats.Colors.OutOfStockBackground);

        // Farnell section (columns 19-22)
        var farnellRange = worksheet.Cells[row, 19, row, 22];
        farnellRange.Style.Fill.PatternType = ExcelFillStyle.Solid;

        bool farnellAvailable = entry.FarnellData?.IsAvailable ?? false;
        bool farnellHasStock = (entry.FarnellData?.Availability ?? 0) > 0;

        farnellRange.Style.Fill.BackgroundColor.SetColor(
            farnellAvailable && farnellHasStock ?
                ExcelFormats.Colors.FarnellBackground :
                ExcelFormats.Colors.OutOfStockBackground);

        // Best Price column - color based on supplier
        worksheet.Cells[row, 23].Style.Fill.PatternType = ExcelFillStyle.Solid;

        // Only color Best Price based on supplier if that supplier actually has stock
        bool supplierHasStock = false;

        switch (entry.BestCurrentSupplier)
        {
            case "DigiKey":
                supplierHasStock = digiKeyAvailable && digiKeyHasStock;
                worksheet.Cells[row, 23].Style.Fill.BackgroundColor.SetColor(
                    supplierHasStock ?
                        ExcelFormats.Colors.DigiKeyBackground :
                        ExcelFormats.Colors.OutOfStockBackground);
                break;
            case "DigiKey-IL":
                supplierHasStock = digiKeyILAvailable && digiKeyILHasStock;
                worksheet.Cells[row, 23].Style.Fill.BackgroundColor.SetColor(
                    supplierHasStock ?
                        ExcelFormats.Colors.DigiKeyILBackground :
                        ExcelFormats.Colors.OutOfStockBackground);
                break;
            case "Mouser":
                supplierHasStock = mouserAvailable && mouserHasStock;
                worksheet.Cells[row, 23].Style.Fill.BackgroundColor.SetColor(
                    supplierHasStock ?
                        ExcelFormats.Colors.MouserBackground :
                        ExcelFormats.Colors.OutOfStockBackground);
                break;
            case "Farnell":
                supplierHasStock = farnellAvailable && farnellHasStock;
                worksheet.Cells[row, 23].Style.Fill.BackgroundColor.SetColor(
                    supplierHasStock ?
                        ExcelFormats.Colors.FarnellBackground :
                        ExcelFormats.Colors.OutOfStockBackground);
                break;
            default:
                worksheet.Cells[row, 23].Style.Fill.BackgroundColor.SetColor(ExcelFormats.Colors.OutOfStockBackground);
                break;
        }

        // External supplier styling - override all previous styling if external supplier
        if (entry.HasExternalSupplier)
        {
            var rowRange = worksheet.Cells[row, 1, row, 23]; // Update the column number for the entire row
            rowRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
            rowRange.Style.Fill.BackgroundColor.SetColor(ExcelFormats.Colors.ExternalSupplierBackground);

            // Add a comment to indicate external supplier
            if (!string.IsNullOrEmpty(entry.BestCurrentSupplier) && entry.BestCurrentSupplier.StartsWith("External:"))
            {
                worksheet.Cells[row, 23].AddComment($"External Supplier: {entry.BestCurrentSupplier.Substring(9)}", "BOMVIEW");
            }
        }

        // All out of stock styling - override all previous styling if all suppliers are out of stock
        if ((!digiKeyAvailable || !digiKeyHasStock) &&
            (!digiKeyILAvailable || !digiKeyILHasStock) &&
            (!mouserAvailable || !mouserHasStock) &&
            (!farnellAvailable || !farnellHasStock))
        {
            var rowRange = worksheet.Cells[row, 1, row, 23];
            rowRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
            rowRange.Style.Fill.BackgroundColor.SetColor(ExcelFormats.Colors.OutOfStockBackground);
        }
    }

    private void WriteEntryRow(ExcelWorksheet worksheet, int row, BomEntry entry)
    {
        // Basic component information in the exact requested order
        var orderCodeCell = worksheet.Cells[row, 1];
        orderCodeCell.Value = entry.OrderingCode;
        worksheet.Cells[row, 2].Value = entry.Designator;
        worksheet.Cells[row, 3].Value = entry.Value;
        worksheet.Cells[row, 4].Value = entry.PcbFootprint;
        worksheet.Cells[row, 5].Value = entry.QuantityTotal;
        worksheet.Cells[row, 6].Value = entry.StockQuantity;
        worksheet.Cells[row, 7].Value = entry.DigiKeyOrderQuantity;

        // Add hyperlink for the order code
        if (!string.IsNullOrEmpty(entry.OrderingCode))
        {
            string url;
            if (entry.BestCurrentSupplier == "DigiKey" && !string.IsNullOrEmpty(entry.DigiKeyProductUrl))
            {
                url = entry.DigiKeyProductUrl;
            }
            else if (entry.BestCurrentSupplier == "DigiKey-IL" && !string.IsNullOrEmpty(entry.IsraelProductUrl))
            {
                url = entry.IsraelProductUrl;
            }
            else if (entry.BestCurrentSupplier == "Mouser" && !string.IsNullOrEmpty(entry.MouserProductUrl))
            {
                url = entry.MouserProductUrl;
            }
            else if (entry.BestCurrentSupplier == "Farnell" && !string.IsNullOrEmpty(entry.FarnellProductUrl))
            {
                url = entry.FarnellProductUrl;
            }
            else
            {
                url = entry.BestCurrentSupplier == "DigiKey"
                    ? $"https://www.digikey.co.il/en/products/result?keywords={Uri.EscapeDataString(entry.OrderingCode)}"
                    : (entry.BestCurrentSupplier == "DigiKey-IL"
                        ? $"https://www.digikey.co.il/en/products/result?keywords={Uri.EscapeDataString(entry.OrderingCode)}"
                        : (entry.BestCurrentSupplier == "Mouser"
                            ? $"https://www.mouser.com/c/?q={Uri.EscapeDataString(entry.OrderingCode)}"
                            : $"https://il.farnell.com/search?st={Uri.EscapeDataString(entry.OrderingCode)}"));
            }

            orderCodeCell.Hyperlink = new Uri(url);
            orderCodeCell.Style.Font.Color.SetColor(System.Drawing.Color.Blue);
            orderCodeCell.Style.Font.UnderLine = true;
        }

        // DigiKey columns
        worksheet.Cells[row, 8].Value = entry.DigiKeyUnitPrice;
        worksheet.Cells[row, 9].Value = entry.DigiKeyCurrentTotalPrice;
        worksheet.Cells[row, 10].Value = entry.DigiKeyData?.Availability ?? 0;

        // DigiKey-IL columns (using Israel properties)
        worksheet.Cells[row, 11].Value = entry.IsraelOrderQuantity;
        worksheet.Cells[row, 12].Value = entry.IsraelUnitPrice;
        worksheet.Cells[row, 13].Value = entry.IsraelCurrentTotalPrice;
        worksheet.Cells[row, 14].Value = entry.IsraelData?.Availability ?? 0;

        // Mouser columns
        worksheet.Cells[row, 15].Value = entry.MouserOrderQuantity;
        worksheet.Cells[row, 16].Value = entry.MouserCurrentUnitPrice;
        worksheet.Cells[row, 17].Value = entry.MouserCurrentTotalPrice;
        worksheet.Cells[row, 18].Value = entry.MouserData?.Availability ?? 0;

        // Farnell columns
        worksheet.Cells[row, 19].Value = entry.FarnellOrderQuantity;
        worksheet.Cells[row, 20].Value = entry.FarnellCurrentUnitPrice;
        worksheet.Cells[row, 21].Value = entry.FarnellCurrentTotalPrice;
        worksheet.Cells[row, 22].Value = entry.FarnellData?.Availability ?? 0;

        // Best Price (combined column)
        worksheet.Cells[row, 23].Value = entry.CurrentTotalPrice;

        // Format prices
        var priceColumns = new[] { 8, 9, 12, 13, 16, 17, 20, 21, 23 };
        foreach (var col in priceColumns)
        {
            worksheet.Cells[row, col].Style.Numberformat.Format = ExcelFormats.Currency;
        }

        // Format quantities and stock
        var quantityColumns = new[] { 5, 6, 7, 10, 11, 14, 15, 18, 19, 22 };
        foreach (var col in quantityColumns)
        {
            worksheet.Cells[row, col].Style.Numberformat.Format = ExcelFormats.Quantity;
        }

        // Apply supplier-specific styling
        StyleSupplierSections(worksheet, row, entry);
    }

    private void WriteMissingComponentsSheet(ExcelWorksheet sheet, List<BomEntry> entries)
    {
        // Headers
        var headers = new[] {
        "Ordering Code",
        "Designator",
        "Value",
        "PCB Footprint",
        "Quantity (One)",
        "Total Quantity",
        "DigiKey Available",
        "DigiKey-IL Available",
        "Mouser Available",
        "Farnell Available"
    };

        for (int i = 0; i < headers.Length; i++)
        {
            sheet.Cells[1, i + 1].Value = headers[i];
            sheet.Cells[1, i + 1].Style.Font.Bold = true;
            sheet.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(ExcelFormats.Colors.HeaderBackground);
        }

        int row = 2;
        bool foundMissingComponents = false;
        foreach (var entry in entries)
        {
            bool digiKeyMissing = !(entry.DigiKeyData?.IsAvailable ?? false);
            bool digiKeyILMissing = !(entry.IsraelData?.IsAvailable ?? false);
            bool mouserMissing = !(entry.MouserData?.IsAvailable ?? false);
            bool farnellMissing = !(entry.FarnellData?.IsAvailable ?? false);

            if (digiKeyMissing || digiKeyILMissing || mouserMissing || farnellMissing)
            {
                foundMissingComponents = true;
                sheet.Cells[row, 1].Value = entry.OrderingCode;
                sheet.Cells[row, 2].Value = entry.Designator;
                sheet.Cells[row, 3].Value = entry.Value;
                sheet.Cells[row, 4].Value = entry.PcbFootprint;
                sheet.Cells[row, 5].Value = entry.QuantityForOne;
                sheet.Cells[row, 6].Value = entry.QuantityTotal;
                sheet.Cells[row, 7].Value = !digiKeyMissing;
                sheet.Cells[row, 8].Value = !digiKeyILMissing;
                sheet.Cells[row, 9].Value = !mouserMissing;
                sheet.Cells[row, 10].Value = !farnellMissing;

                // Style the row based on availability
                if (digiKeyMissing && digiKeyILMissing && mouserMissing && farnellMissing)
                {
                    var rowRange = sheet.Cells[row, 1, row, 10];
                    rowRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    rowRange.Style.Fill.BackgroundColor.SetColor(ExcelFormats.Colors.OutOfStockBackground);
                }

                // Format quantity columns
                sheet.Cells[row, 5].Style.Numberformat.Format = ExcelFormats.Quantity;
                sheet.Cells[row, 6].Style.Numberformat.Format = ExcelFormats.Quantity;

                row++;
            }
        }

        // If no entries have been added, add a note
        if (!foundMissingComponents)
        {
            sheet.Cells[row, 1].Value = "No missing components found";
            sheet.Cells[row, 1, row, 10].Merge = true;
            sheet.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            sheet.Cells[row, 1].Style.Font.Italic = true;
            row++;
        }

        // Format sheet - ensure valid range even if empty
        var endRow = Math.Max(2, row - 1);
        var dataRange = sheet.Cells[1, 1, endRow, headers.Length];
        dataRange.AutoFitColumns();
        StyleSheetBorders(dataRange);
    }


    // Replace both AddBestPricesLegend and AddLegend with this unified method
    private void AddLegend(ExcelWorksheet sheet, int startRow)
    {
        sheet.Cells[startRow, 1].Value = "Legend:";
        sheet.Cells[startRow, 1].Style.Font.Bold = true;
        sheet.Cells[startRow, 1].Style.Font.Size = 12;

        var legendItems = new[]
   {
    ("DigiKey Best Price", ExcelFormats.Colors.DigiKeyBackground),
    ("DigiKey-IL Best Price", ExcelFormats.Colors.DigiKeyILBackground),
    ("Mouser Best Price", ExcelFormats.Colors.MouserBackground),
    ("Farnell Best Price", ExcelFormats.Colors.FarnellBackground),
    ("External Supplier", ExcelFormats.Colors.ExternalSupplierBackground),
    ("Out of Stock", ExcelFormats.Colors.OutOfStockBackground),
    ("User Entered Data", ExcelFormats.Colors.UserEnteredBackground)
};

        for (int i = 0; i < legendItems.Length; i++)
        {
            var row = startRow + i + 1;
            sheet.Cells[row, 2].Value = legendItems[i].Item1;
            sheet.Cells[row, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[row, 2].Style.Fill.BackgroundColor.SetColor(legendItems[i].Item2);
        }

        // Add note
        var infoRow = startRow + legendItems.Length + 2;
        sheet.Cells[infoRow, 1].Value = "Notes:";
        sheet.Cells[infoRow, 1].Style.Font.Bold = true;

        var notes = new[]
        {
        "This file contains pricing and availability information from DigiKey, DigiKey-IL, Mouser and Farnell.",
        "Color coding indicates the best price supplier for each component.",
        $"Last updated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
    };

        for (int i = 0; i < notes.Length; i++)
        {
            sheet.Cells[infoRow + i + 1, 1].Value = notes[i];
            sheet.Cells[infoRow + i + 1, 1, infoRow + i + 1, 5].Merge = true;
        }
    }


    private void StyleSheetBorders(ExcelRange range)
    {
        range.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
        range.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
        range.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
        range.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
    }

    private string GetExcelColumnName(int columnNumber)
    {
        string columnName = "";
        while (columnNumber > 0)
        {
            int remainder = (columnNumber - 1) % 26;
            columnName = Convert.ToChar('A' + remainder) + columnName;
            columnNumber = (columnNumber - 1) / 26;
        }
        return columnName;
    }

    public bool ValidateExcelFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return false;

        var extension = Path.GetExtension(filePath).ToLower();
        return extension == ".xlsx";
    }

    private int ParseIntOrDefault(string value)
    {
        return int.TryParse(value, out int result) ? result : 0;
    }


    public async Task SaveExternalSuppliersSheetAsync(string filePath, List<ExternalSupplierEntry> entries)
    {
        try
        {
            // Check if the file exists
            FileInfo fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                throw new FileNotFoundException("Excel file not found", filePath);
            }

            using var package = new ExcelPackage(fileInfo);

            // Check if the External Suppliers sheet already exists
            var sheet = package.Workbook.Worksheets.FirstOrDefault(ws => ws.Name == "External Suppliers");

            // If the sheet exists, delete it to replace with updated data
            if (sheet != null)
            {
                package.Workbook.Worksheets.Delete(sheet);
            }

            // Create a new External Suppliers sheet
            sheet = package.Workbook.Worksheets.Add("External Suppliers");

            // Add headers
            var headers = new[]
            {
                "Order Code",
                "Designator",
                "Value",
                "PCB Footprint",
                "Quantity",
                "Supplier Name",
                "Unit Price",
                "Total Price",
                "Availability",
                "URL",
                "Added On",
                "Estimated Delivery",
                "Notes",
                "Contact Info"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                sheet.Cells[1, i + 1].Value = headers[i];
                sheet.Cells[1, i + 1].Style.Font.Bold = true;
                sheet.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                sheet.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(ExcelFormats.Colors.HeaderBackground);
            }

            // Add data
            int row = 2;
            foreach (var entry in entries)
            {
                sheet.Cells[row, 1].Value = entry.OrderingCode;
                sheet.Cells[row, 2].Value = entry.Designator;
                sheet.Cells[row, 3].Value = entry.Value;
                sheet.Cells[row, 4].Value = entry.PcbFootprint;
                sheet.Cells[row, 5].Value = entry.QuantityTotal;
                sheet.Cells[row, 6].Value = entry.SupplierName;
                sheet.Cells[row, 7].Value = entry.UnitPrice;
                sheet.Cells[row, 8].Value = entry.TotalPrice;
                sheet.Cells[row, 9].Value = entry.Availability;
                sheet.Cells[row, 10].Value = entry.SupplierUrl;
                sheet.Cells[row, 11].Value = entry.DateAdded;
                sheet.Cells[row, 12].Value = entry.EstimatedDeliveryDate;
                sheet.Cells[row, 13].Value = entry.Notes;
                sheet.Cells[row, 14].Value = entry.ContactInfo;

                // Create URL hyperlink
                if (!string.IsNullOrWhiteSpace(entry.SupplierUrl))
                {
                    try
                    {
                        // Make sure the URL is valid before creating a hyperlink
                        Uri uri;
                        if (Uri.TryCreate(entry.SupplierUrl, UriKind.Absolute, out uri))
                        {
                            sheet.Cells[row, 10].Hyperlink = uri;
                            sheet.Cells[row, 10].Style.Font.Color.SetColor(System.Drawing.Color.Blue);
                            sheet.Cells[row, 10].Style.Font.UnderLine = true;
                        }
                    }
                    catch
                    {
                        // If the URL is not valid, just show it as text
                    }
                }

                // Format price columns
                sheet.Cells[row, 7].Style.Numberformat.Format = ExcelFormats.Currency;
                sheet.Cells[row, 8].Style.Numberformat.Format = ExcelFormats.Currency;
                sheet.Cells[row, 9].Style.Numberformat.Format = ExcelFormats.Quantity;

                // Apply styling to the row
                var rowRange = sheet.Cells[row, 1, row, headers.Length];
                rowRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                rowRange.Style.Fill.BackgroundColor.SetColor(ExcelFormats.Colors.ExternalSupplierBackground);

                row++;
            }

            // If no entries were added, add a note
            if (entries.Count == 0)
            {
                sheet.Cells[2, 1].Value = "No external supplier entries found";
                sheet.Cells[2, 1, 2, headers.Length].Merge = true;
                sheet.Cells[2, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                sheet.Cells[2, 1].Style.Font.Italic = true;
                row = 3; // Update row for correct styling below
            }

            // Auto-fit columns and apply borders
            var endRow = Math.Max(2, row - 1);
            var dataRange = sheet.Cells[1, 1, endRow, headers.Length];
            dataRange.AutoFitColumns();
            StyleSheetBorders(dataRange);

            await package.SaveAsync();
        }
        catch (Exception ex)
        {
            throw new Exception($"Error saving external suppliers sheet: {ex.Message}", ex);
        }
    }


}