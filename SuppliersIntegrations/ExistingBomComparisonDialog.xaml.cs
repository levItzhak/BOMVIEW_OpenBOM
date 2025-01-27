using BOMVIEW.Interfaces;
using BOMVIEW.Models;
using BOMVIEW.OpenBOM.Models;
using BOMVIEW.Services;
using Microsoft.Win32;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace BOMVIEW
{
    public partial class ExistingBomComparisonDialog : Window, INotifyPropertyChanged
    {
        private readonly ILogger _logger;
        private readonly OpenBomService _openBomService;
        private ObservableCollection<OpenBomListItem> _firstBomResults;
        private ObservableCollection<OpenBomListItem> _secondBomResults;
        private OpenBomListItem _selectedFirstBom;
        private OpenBomListItem _selectedSecondBom;
        private List<BomComparisonItem> _commonParts;
        private List<BomEntry> _firstBomUniqueParts;
        private List<BomEntry> _secondBomUniqueParts;
        private CancellationTokenSource _cancellationTokenSource;
        private RateLimitedOpenBomService _rateLimitedOpenBomService;
        private bool _isComparing;

        public ExistingBomComparisonDialog(ILogger logger)
        {
            InitializeComponent();
            DataContext = this;

            _logger = logger;
            _openBomService = new OpenBomService(logger);
            _rateLimitedOpenBomService = new RateLimitedOpenBomService(logger, null);

            _firstBomResults = new ObservableCollection<OpenBomListItem>();
            _secondBomResults = new ObservableCollection<OpenBomListItem>();

            lstFirstBomResults.ItemsSource = _firstBomResults;
            lstSecondBomResults.ItemsSource = _secondBomResults;

            _commonParts = new List<BomComparisonItem>();
            _firstBomUniqueParts = new List<BomEntry>();
            _secondBomUniqueParts = new List<BomEntry>();
        }

        private async void btnSearchFirstBom_Click(object sender, RoutedEventArgs e)
        {
            await SearchBoms(txtFirstBomSearch.Text, _firstBomResults);
        }

        private async void btnSearchSecondBom_Click(object sender, RoutedEventArgs e)
        {
            await SearchBoms(txtSecondBomSearch.Text, _secondBomResults);
        }

        private async Task SearchBoms(string searchTerm, ObservableCollection<OpenBomListItem> resultsList)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                MessageBox.Show("Please enter a search term", "Search Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Disable buttons during search
                btnSearchFirstBom.IsEnabled = false;
                btnSearchSecondBom.IsEnabled = false;

                resultsList.Clear();
                txtComparisonStatus.Text = "Searching for BOMs...";

                var allBoms = await _openBomService.ListBomsAsync();
                var filteredBoms = allBoms
                    .Where(b => b.MatchesSearch(searchTerm))
                    .ToList();

                foreach (var bom in filteredBoms)
                {
                    resultsList.Add(bom);
                }

                if (filteredBoms.Count == 0)
                {
                    txtComparisonStatus.Text = "No BOMs found matching your search";
                }
                else
                {
                    txtComparisonStatus.Text = $"Found {filteredBoms.Count} BOMs matching your search";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error searching BOMs: {ex.Message}");
                MessageBox.Show($"Error searching BOMs: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                txtComparisonStatus.Text = "Error searching for BOMs";
            }
            finally
            {
                btnSearchFirstBom.IsEnabled = true;
                btnSearchSecondBom.IsEnabled = true;
            }
        }

        private void txtFirstBomSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            // You could add immediate search functionality here
        }

        private void txtSecondBomSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            // You could add immediate search functionality here
        }

        private void lstFirstBomResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedFirstBom = lstFirstBomResults.SelectedItem as OpenBomListItem;
            UpdateSelectedBomText(txtSelectedFirstBom, _selectedFirstBom);
            UpdateCompareButtonState();
        }

        private void lstSecondBomResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedSecondBom = lstSecondBomResults.SelectedItem as OpenBomListItem;
            UpdateSelectedBomText(txtSelectedSecondBom, _selectedSecondBom);
            UpdateCompareButtonState();
        }

        private void UpdateSelectedBomText(TextBlock textBlock, OpenBomListItem bomItem)
        {
            if (bomItem != null)
            {
                textBlock.Text = $"Selected: {bomItem.Name}";

                // Add part number if available
                if (!string.IsNullOrEmpty(bomItem.PartNumber))
                {
                    textBlock.Text += $" (Part #: {bomItem.PartNumber})";
                }
            }
            else
            {
                textBlock.Text = string.Empty;
            }
        }

        private void UpdateCompareButtonState()
        {
            btnCompare.IsEnabled = _selectedFirstBom != null && _selectedSecondBom != null && !_isComparing;
        }

        private async void btnCompare_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFirstBom == null || _selectedSecondBom == null)
            {
                MessageBox.Show("Please select two BOMs to compare", "Selection Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _isComparing = true;
                btnCompare.IsEnabled = false;
                txtLoadingMessage.Visibility = Visibility.Visible;
                tabComparisonResults.Visibility = Visibility.Collapsed;
                txtComparisonStatus.Text = "Comparing BOMs...";

                // Create cancellation token
                _cancellationTokenSource = new CancellationTokenSource();

                // Perform comparison
                await PerformComparison(_cancellationTokenSource.Token);

                // Show results
                UpdateComparisonResults();

                txtComparisonStatus.Text = "Comparison complete";
                txtLoadingMessage.Visibility = Visibility.Collapsed;
                tabComparisonResults.Visibility = Visibility.Visible;
                btnExportResults.IsEnabled = true;
            }
            catch (OperationCanceledException)
            {
                txtComparisonStatus.Text = "Comparison cancelled";
                MessageBox.Show("The comparison operation was cancelled", "Cancelled",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error comparing BOMs: {ex.Message}");
                MessageBox.Show($"Error comparing BOMs: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                txtComparisonStatus.Text = $"Error: {ex.Message}";
            }
            finally
            {
                _isComparing = false;
                btnCompare.IsEnabled = true;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private async Task PerformComparison(CancellationToken cancellationToken)
        {
            try
            {
                txtComparisonStatus.Text = "Retrieving first BOM data...";
                var firstBomData = await _rateLimitedOpenBomService.GetBomHierarchyAsync(_selectedFirstBom.Id);
                _logger.LogInfo($"Retrieved {firstBomData.Count} items from first BOM");

                if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();

                txtComparisonStatus.Text = "Retrieving second BOM data...";
                var secondBomData = await _rateLimitedOpenBomService.GetBomHierarchyAsync(_selectedSecondBom.Id);
                _logger.LogInfo($"Retrieved {secondBomData.Count} items from second BOM");

                if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();

                txtComparisonStatus.Text = "Processing comparison...";

                // Convert BomTreeNode to BomEntry for easier comparison
                var firstBomEntries = ConvertTreeNodesToEntries(firstBomData);
                var secondBomEntries = ConvertTreeNodesToEntries(secondBomData);

                // Create lookup dictionaries for efficient comparison
                var firstBomLookup = CreatePartLookup(firstBomEntries);
                var secondBomLookup = CreatePartLookup(secondBomEntries);

                // Clear previous results
                _commonParts.Clear();
                _firstBomUniqueParts.Clear();
                _secondBomUniqueParts.Clear();

                // Find parts in both BOMs and unique to first BOM
                foreach (var part in firstBomEntries)
                {
                    if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();

                    var normalizedPartNumber = NormalizePartNumber(part.OrderingCode);
                    if (string.IsNullOrEmpty(normalizedPartNumber)) continue;

                    if (secondBomLookup.TryGetValue(normalizedPartNumber, out var matchingPart))
                    {
                        // Common part - add to common parts list with quantities from both BOMs
                        _commonParts.Add(new BomComparisonItem
                        {
                            OrderingCode = part.OrderingCode,
                            Designator = part.Designator,
                            Value = part.Value,
                            PcbFootprint = part.PcbFootprint,
                            FirstBomQuantity = part.QuantityTotal,
                            SecondBomQuantity = matchingPart.QuantityTotal,
                            QuantityDifference = part.QuantityTotal - matchingPart.QuantityTotal
                        });
                    }
                    else
                    {
                        // Part only in first BOM
                        _firstBomUniqueParts.Add(part);
                    }
                }

                // Find parts unique to second BOM
                foreach (var part in secondBomEntries)
                {
                    if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();

                    var normalizedPartNumber = NormalizePartNumber(part.OrderingCode);
                    if (string.IsNullOrEmpty(normalizedPartNumber)) continue;

                    if (!firstBomLookup.ContainsKey(normalizedPartNumber))
                    {
                        // Part only in second BOM
                        _secondBomUniqueParts.Add(part);
                    }
                }

                _logger.LogInfo($"Comparison complete: {_commonParts.Count} common parts, " +
                    $"{_firstBomUniqueParts.Count} unique to first BOM, " +
                    $"{_secondBomUniqueParts.Count} unique to second BOM");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during comparison: {ex.Message}");
                throw;
            }
        }

        private Dictionary<string, BomEntry> CreatePartLookup(List<BomEntry> entries)
        {
            var lookup = new Dictionary<string, BomEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                var normalizedPartNumber = NormalizePartNumber(entry.OrderingCode);
                if (!string.IsNullOrEmpty(normalizedPartNumber) && !lookup.ContainsKey(normalizedPartNumber))
                {
                    lookup.Add(normalizedPartNumber, entry);
                }
            }

            return lookup;
        }

        private List<BomEntry> ConvertTreeNodesToEntries(List<BomTreeNode> nodes)
        {
            var entries = new List<BomEntry>();

            foreach (var node in nodes)
            {
                if (string.IsNullOrEmpty(node.PartNumber)) continue;

                // Extract quantity from properties
                int quantity = 0;
                foreach (var prop in node.Properties)
                {
                    if (prop.Key.Contains("quantity", StringComparison.OrdinalIgnoreCase) ||
                        prop.Key.Contains("qty", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(prop.Value, out int parsedQty))
                        {
                            quantity = parsedQty;
                            break;
                        }
                    }
                }

                // Create BomEntry from BomTreeNode
                var entry = new BomEntry
                {
                    OrderingCode = node.PartNumber,
                    QuantityTotal = quantity > 0 ? quantity : 1,
                    QuantityForOne = 1, // Default value
                    Designator = node.Name,
                    Value = node.Description ?? string.Empty,
                    PcbFootprint = node.Properties.ContainsKey("Footprint") ? node.Properties["Footprint"] : string.Empty
                };

                entries.Add(entry);
            }

            return entries;
        }

        private string NormalizePartNumber(string partNumber)
        {
            if (string.IsNullOrWhiteSpace(partNumber))
                return string.Empty;

            // Remove extra spaces, dashes, and trim
            return partNumber.Trim().Replace(" ", "").Replace("-", "");
        }

        private void UpdateComparisonResults()
        {
            // Update data grids with comparison results
            gridCommonParts.ItemsSource = _commonParts;
            gridFirstUnique.ItemsSource = _firstBomUniqueParts;
            gridSecondUnique.ItemsSource = _secondBomUniqueParts;
        }

        private async void btnExportResults_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Excel Files|*.xlsx",
                    Title = "Save BOM Comparison"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    btnExportResults.IsEnabled = false;
                    txtComparisonStatus.Text = "Exporting comparison results...";

                    await ExportToExcel(saveFileDialog.FileName);

                    MessageBox.Show("Comparison results exported successfully.", "Export Complete",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    txtComparisonStatus.Text = "Export completed successfully";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error exporting comparison: {ex.Message}");
                MessageBox.Show($"Error exporting comparison: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                txtComparisonStatus.Text = $"Export error: {ex.Message}";
            }
            finally
            {
                btnExportResults.IsEnabled = true;
            }
        }

        private async Task ExportToExcel(string filePath)
        {
            ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

            using (var package = new ExcelPackage())
            {
                // Create the summary sheet
                var summarySheet = package.Workbook.Worksheets.Add("Summary");
                summarySheet.Cells[1, 1].Value = "BOM Comparison Summary";
                summarySheet.Cells[1, 1].Style.Font.Bold = true;
                summarySheet.Cells[1, 1].Style.Font.Size = 14;

                summarySheet.Cells[3, 1].Value = "First BOM";
                summarySheet.Cells[3, 2].Value = _selectedFirstBom.Name;
                if (!string.IsNullOrEmpty(_selectedFirstBom.PartNumber))
                {
                    summarySheet.Cells[4, 1].Value = "First BOM Part Number";
                    summarySheet.Cells[4, 2].Value = _selectedFirstBom.PartNumber;
                }

                summarySheet.Cells[6, 1].Value = "Second BOM";
                summarySheet.Cells[6, 2].Value = _selectedSecondBom.Name;
                if (!string.IsNullOrEmpty(_selectedSecondBom.PartNumber))
                {
                    summarySheet.Cells[7, 1].Value = "Second BOM Part Number";
                    summarySheet.Cells[7, 2].Value = _selectedSecondBom.PartNumber;
                }

                summarySheet.Cells[9, 1].Value = "Common Parts Count";
                summarySheet.Cells[9, 2].Value = _commonParts.Count;

                summarySheet.Cells[10, 1].Value = "Parts Unique to First BOM";
                summarySheet.Cells[10, 2].Value = _firstBomUniqueParts.Count;

                summarySheet.Cells[11, 1].Value = "Parts Unique to Second BOM";
                summarySheet.Cells[11, 2].Value = _secondBomUniqueParts.Count;

                // Common Parts Sheet
                if (_commonParts.Count > 0)
                {
                    var commonSheet = package.Workbook.Worksheets.Add("Common Parts");

                    // Headers
                    commonSheet.Cells[1, 1].Value = "Part Number";
                    commonSheet.Cells[1, 2].Value = "First BOM Quantity";
                    commonSheet.Cells[1, 3].Value = "Second BOM Quantity";
                    commonSheet.Cells[1, 4].Value = "Difference";
                    commonSheet.Cells[1, 5].Value = "Designator";
                    commonSheet.Cells[1, 6].Value = "Value";
                    commonSheet.Cells[1, 7].Value = "PCB Footprint";

                    // Format headers
                    for (int i = 1; i <= 7; i++)
                    {
                        commonSheet.Cells[1, i].Style.Font.Bold = true;
                    }

                    // Apply styling to header row
                    var headerRange = commonSheet.Cells[1, 1, 1, 7];
                    headerRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    headerRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                    headerRange.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;

                    // Data
                    for (int i = 0; i < _commonParts.Count; i++)
                    {
                        var part = _commonParts[i];
                        int row = i + 2;

                        commonSheet.Cells[row, 1].Value = part.OrderingCode;
                        commonSheet.Cells[row, 2].Value = part.FirstBomQuantity;
                        commonSheet.Cells[row, 3].Value = part.SecondBomQuantity;
                        commonSheet.Cells[row, 4].Value = part.QuantityDifference;
                        commonSheet.Cells[row, 5].Value = part.Designator;
                        commonSheet.Cells[row, 6].Value = part.Value;
                        commonSheet.Cells[row, 7].Value = part.PcbFootprint;

                        // Highlight quantity differences
                        if (part.QuantityDifference != 0)
                        {
                            commonSheet.Cells[row, 4].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                            commonSheet.Cells[row, 4].Style.Fill.BackgroundColor.SetColor(
                                part.QuantityDifference > 0 ? System.Drawing.Color.LightGreen : System.Drawing.Color.LightPink);
                        }
                    }

                    // Auto-fit columns
                    commonSheet.Cells.AutoFitColumns();
                }

                // First BOM Unique Parts Sheet
                if (_firstBomUniqueParts.Count > 0)
                {
                    var firstUniqueSheet = package.Workbook.Worksheets.Add("First BOM Unique");

                    // Headers
                    firstUniqueSheet.Cells[1, 1].Value = "Part Number";
                    firstUniqueSheet.Cells[1, 2].Value = "Quantity";
                    firstUniqueSheet.Cells[1, 3].Value = "Designator";
                    firstUniqueSheet.Cells[1, 4].Value = "Value";
                    firstUniqueSheet.Cells[1, 5].Value = "PCB Footprint";

                    // Format headers
                    var headerRange = firstUniqueSheet.Cells[1, 1, 1, 5];
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    headerRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                    headerRange.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;

                    // Data
                    for (int i = 0; i < _firstBomUniqueParts.Count; i++)
                    {
                        var part = _firstBomUniqueParts[i];
                        int row = i + 2;

                        firstUniqueSheet.Cells[row, 1].Value = part.OrderingCode;
                        firstUniqueSheet.Cells[row, 2].Value = part.QuantityTotal;
                        firstUniqueSheet.Cells[row, 3].Value = part.Designator;
                        firstUniqueSheet.Cells[row, 4].Value = part.Value;
                        firstUniqueSheet.Cells[row, 5].Value = part.PcbFootprint;
                    }

                    // Auto-fit columns
                    firstUniqueSheet.Cells.AutoFitColumns();
                }

                // Second BOM Unique Parts Sheet
                if (_secondBomUniqueParts.Count > 0)
                {
                    var secondUniqueSheet = package.Workbook.Worksheets.Add("Second BOM Unique");

                    // Headers
                    secondUniqueSheet.Cells[1, 1].Value = "Part Number";
                    secondUniqueSheet.Cells[1, 2].Value = "Quantity";
                    secondUniqueSheet.Cells[1, 3].Value = "Designator";
                    secondUniqueSheet.Cells[1, 4].Value = "Value";
                    secondUniqueSheet.Cells[1, 5].Value = "PCB Footprint";

                    // Format headers
                    var headerRange = secondUniqueSheet.Cells[1, 1, 1, 5];
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    headerRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                    headerRange.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;

                    // Data
                    for (int i = 0; i < _secondBomUniqueParts.Count; i++)
                    {
                        var part = _secondBomUniqueParts[i];
                        int row = i + 2;

                        secondUniqueSheet.Cells[row, 1].Value = part.OrderingCode;
                        secondUniqueSheet.Cells[row, 2].Value = part.QuantityTotal;
                        secondUniqueSheet.Cells[row, 3].Value = part.Designator;
                        secondUniqueSheet.Cells[row, 4].Value = part.Value;
                        secondUniqueSheet.Cells[row, 5].Value = part.PcbFootprint;
                    }

                    // Auto-fit columns
                    secondUniqueSheet.Cells.AutoFitColumns();
                }

                // Save the workbook
                await Task.Run(() => {
                    package.SaveAs(new FileInfo(filePath));
                });
            }
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Class to hold comparison data
    public class BomComparisonItem
    {
        public string OrderingCode { get; set; }
        public string Designator { get; set; }
        public string Value { get; set; }
        public string PcbFootprint { get; set; }
        public int FirstBomQuantity { get; set; }
        public int SecondBomQuantity { get; set; }
        public int QuantityDifference { get; set; }
    }
}