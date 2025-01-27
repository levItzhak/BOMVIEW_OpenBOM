using BOMVIEW.Interfaces;
using BOMVIEW.Models;
using Microsoft.Win32;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

namespace BOMVIEW
{
    public partial class BomSummaryDialog : Window, INotifyPropertyChanged
    {
        private readonly ILogger _logger;

        public List<BomEntryViewModel> PartsToUpload { get; set; }
        public List<BomEntryViewModel> SkippedParts { get; set; }
        public List<BomEntryViewModel> ModifiedParts { get; set; }
        public List<BomEntry> AllParts { get; set; }
        private Dictionary<string, BomEntry> _originalEntries = new Dictionary<string, BomEntry>();

        private int _totalParts;
        public int TotalParts
        {
            get => _totalParts;
            set
            {
                _totalParts = value;
                OnPropertyChanged(nameof(TotalParts));
            }
        }

        private int _uploadCount;
        public int UploadCount
        {
            get => _uploadCount;
            set
            {
                _uploadCount = value;
                OnPropertyChanged(nameof(UploadCount));
            }
        }

        private int _skippedCount;
        public int SkippedCount
        {
            get => _skippedCount;
            set
            {
                _skippedCount = value;
                OnPropertyChanged(nameof(SkippedCount));
            }
        }

        private int _modifiedCount;
        public int ModifiedCount
        {
            get => _modifiedCount;
            set
            {
                _modifiedCount = value;
                OnPropertyChanged(nameof(ModifiedCount));
            }
        }

        public BomSummaryDialog(ILogger logger, List<BomEntry> allParts, List<BomEntry> partsToUpload, List<BomEntry> skippedParts, List<BomEntry> modifiedParts)
        {
            InitializeComponent();

            _logger = logger;
            AllParts = allParts;

            // Store original entries for reference
            foreach (var part in allParts)
            {
                if (!string.IsNullOrEmpty(part.OrderingCode))
                {
                    _originalEntries[part.OrderingCode] = part;
                }
            }

            // Convert to view models
            PartsToUpload = partsToUpload.Select(p => CreateViewModel(p)).ToList();
            SkippedParts = skippedParts.Select(p => CreateViewModel(p)).ToList();
            ModifiedParts = modifiedParts.Select(p =>
            {
                var vm = CreateViewModel(p);

                // Find the corresponding part in the upload list to get the modified quantity
                var uploadPart = partsToUpload.FirstOrDefault(up => up.OrderingCode == p.OrderingCode);
                if (uploadPart != null)
                {
                    vm.ModifiedQuantity = uploadPart.QuantityTotal;
                }

                return vm;
            }).ToList();

            // Set the counts explicitly
            TotalParts = allParts?.Count ?? 0;
            UploadCount = partsToUpload?.Count ?? 0;
            SkippedCount = skippedParts?.Count ?? 0;
            ModifiedCount = modifiedParts?.Count ?? 0;

            DataContext = this;
        }

        private BomEntryViewModel CreateViewModel(BomEntry part)
        {
            return new BomEntryViewModel
            {
                OrderingCode = part.OrderingCode,
                Designator = part.Designator,
                Value = part.Value,
                PcbFootprint = part.PcbFootprint,
                QuantityTotal = part.QuantityTotal,
                ModifiedQuantity = part.QuantityTotal // Default to same, will be overridden for modified parts
            };
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Excel Files|*.xlsx",
                    Title = "Save BOM Summary",
                    FileName = "BOM_Comparison_Summary.xlsx"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    ExportToExcel(saveFileDialog.FileName);
                    MessageBox.Show("Summary exported successfully.", "Export Complete",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error exporting summary: {ex.Message}");
                MessageBox.Show($"Error exporting summary: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportToExcel(string filePath)
        {
            try
            {
                ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
                using (var package = new ExcelPackage())
                {
                    // Create the summary sheet
                    var summarySheet = package.Workbook.Worksheets.Add("Summary");
                    summarySheet.Cells[1, 1].Value = "BOM Comparison Summary";
                    summarySheet.Cells[1, 1].Style.Font.Bold = true;
                    summarySheet.Cells[1, 1].Style.Font.Size = 14;

                    summarySheet.Cells[3, 1].Value = "Total Parts";
                    summarySheet.Cells[3, 2].Value = TotalParts;

                    summarySheet.Cells[4, 1].Value = "Parts to Upload";
                    summarySheet.Cells[4, 2].Value = UploadCount;

                    summarySheet.Cells[5, 1].Value = "Parts Skipped";
                    summarySheet.Cells[5, 2].Value = SkippedCount;

                    summarySheet.Cells[6, 1].Value = "Parts Modified";
                    summarySheet.Cells[6, 2].Value = ModifiedCount;

                    // Format summary header
                    using (var range = summarySheet.Cells[1, 1, 1, 2])
                    {
                        range.Merge = true;
                        range.Style.Font.Bold = true;
                        range.Style.Font.Size = 14;
                        range.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                        range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                        range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(230, 230, 250));
                    }

                    // Format summary stats
                    for (int i = 3; i <= 6; i++)
                    {
                        summarySheet.Cells[i, 1].Style.Font.Bold = true;

                        // Add color coding for values
                        if (i == 3) // Total
                            summarySheet.Cells[i, 2].Style.Font.Color.SetColor(System.Drawing.Color.Navy);
                        else if (i == 4) // Upload
                            summarySheet.Cells[i, 2].Style.Font.Color.SetColor(System.Drawing.Color.Green);
                        else if (i == 5) // Skipped
                            summarySheet.Cells[i, 2].Style.Font.Color.SetColor(System.Drawing.Color.DarkOrange);
                        else if (i == 6) // Modified
                            summarySheet.Cells[i, 2].Style.Font.Color.SetColor(System.Drawing.Color.Purple);

                        summarySheet.Cells[i, 2].Style.Font.Bold = true;
                    }

                    // Create the parts to upload sheet
                    if (PartsToUpload.Count > 0)
                    {
                        AddPartsSheet(package, "Parts To Upload", PartsToUpload, System.Drawing.Color.FromArgb(232, 245, 233));
                    }

                    // Create the skipped parts sheet
                    if (SkippedParts.Count > 0)
                    {
                        AddPartsSheet(package, "Skipped Parts", SkippedParts, System.Drawing.Color.FromArgb(255, 243, 224));
                    }

                    // Create the modified parts sheet
                    if (ModifiedParts.Count > 0)
                    {
                        AddModifiedPartsSheet(package, "Modified Parts", ModifiedParts, System.Drawing.Color.FromArgb(232, 234, 246));
                    }

                    // Save the file
                    package.SaveAs(new FileInfo(filePath));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error creating Excel file: {ex.Message}");
                throw;
            }
        }

        private void AddPartsSheet(ExcelPackage package, string sheetName, List<BomEntryViewModel> parts, System.Drawing.Color headerColor)
        {
            var sheet = package.Workbook.Worksheets.Add(sheetName);

            // Set headers
            sheet.Cells[1, 1].Value = "Order Code";
            sheet.Cells[1, 2].Value = "Designator";
            sheet.Cells[1, 3].Value = "Value";
            sheet.Cells[1, 4].Value = "PCB Footprint";
            sheet.Cells[1, 5].Value = "Quantity";

            // Format headers
            using (var range = sheet.Cells[1, 1, 1, 5])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(headerColor);
                range.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            }

            // Add data
            for (int row = 0; row < parts.Count; row++)
            {
                var part = parts[row];
                int excelRow = row + 2;

                sheet.Cells[excelRow, 1].Value = part.OrderingCode;
                sheet.Cells[excelRow, 2].Value = part.Designator;
                sheet.Cells[excelRow, 3].Value = part.Value;
                sheet.Cells[excelRow, 4].Value = part.PcbFootprint;
                sheet.Cells[excelRow, 5].Value = part.QuantityTotal;

                // Add alternating row coloring
                if (row % 2 == 1)
                {
                    using (var range = sheet.Cells[excelRow, 1, excelRow, 5])
                    {
                        range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                        range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(245, 245, 245));
                    }
                }
            }

            // Auto fit columns
            sheet.Cells.AutoFitColumns();
        }

        private void AddModifiedPartsSheet(ExcelPackage package, string sheetName, List<BomEntryViewModel> parts, System.Drawing.Color headerColor)
        {
            var sheet = package.Workbook.Worksheets.Add(sheetName);

            // Set headers
            sheet.Cells[1, 1].Value = "Order Code";
            sheet.Cells[1, 2].Value = "Designator";
            sheet.Cells[1, 3].Value = "Value";
            sheet.Cells[1, 4].Value = "PCB Footprint";
            sheet.Cells[1, 5].Value = "Original Quantity";
            sheet.Cells[1, 6].Value = "Modified Quantity";
            sheet.Cells[1, 7].Value = "Difference";

            // Format headers
            using (var range = sheet.Cells[1, 1, 1, 7])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(headerColor);
                range.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            }

            // Add data
            for (int row = 0; row < parts.Count; row++)
            {
                var part = parts[row];
                int excelRow = row + 2;

                sheet.Cells[excelRow, 1].Value = part.OrderingCode;
                sheet.Cells[excelRow, 2].Value = part.Designator;
                sheet.Cells[excelRow, 3].Value = part.Value;
                sheet.Cells[excelRow, 4].Value = part.PcbFootprint;
                sheet.Cells[excelRow, 5].Value = part.QuantityTotal;
                sheet.Cells[excelRow, 6].Value = part.ModifiedQuantity;

                // Calculate difference
                int diff = part.ModifiedQuantity - part.QuantityTotal;
                sheet.Cells[excelRow, 7].Value = diff;

                // Format the difference cell
                if (diff > 0)
                {
                    sheet.Cells[excelRow, 7].Style.Font.Color.SetColor(System.Drawing.Color.Green);
                    sheet.Cells[excelRow, 7].Value = "+" + diff;
                }
                else if (diff < 0)
                {
                    sheet.Cells[excelRow, 7].Style.Font.Color.SetColor(System.Drawing.Color.Red);
                }

                // Add alternating row coloring
                if (row % 2 == 1)
                {
                    using (var range = sheet.Cells[excelRow, 1, excelRow, 7])
                    {
                        range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                        range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(245, 245, 245));
                    }
                }
            }

            // Auto fit columns
            sheet.Cells.AutoFitColumns();
        }

        public List<BomEntry> GetModifiedPartsToUpload()
        {
            // Return a list that should include all parts to be uploaded with their updated quantities
            var modifiedList = new List<BomEntry>();

            // Convert view models back to BomEntry objects
            foreach (var partVm in PartsToUpload)
            {
                if (_originalEntries.TryGetValue(partVm.OrderingCode, out var originalEntry))
                {
                    // Clone the original entry but use the potentially modified quantity
                    var entry = originalEntry.Clone();
                    entry.QuantityTotal = partVm.QuantityTotal;
                    modifiedList.Add(entry);
                }
            }

            return modifiedList;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Add this view model class to display modified quantities
    public class BomEntryViewModel
    {
        public string OrderingCode { get; set; }
        public string Designator { get; set; }
        public string Value { get; set; }
        public string PcbFootprint { get; set; }
        public int QuantityTotal { get; set; }
        public int ModifiedQuantity { get; set; }
    }
}