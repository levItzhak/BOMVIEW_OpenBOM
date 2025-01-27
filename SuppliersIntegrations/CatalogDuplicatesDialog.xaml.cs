using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using BOMVIEW.Models;
using BOMVIEW.Services;
using BOMVIEW.Interfaces;
using System.Threading.Tasks;
using BOMVIEW.OpenBOM.Models;
using OfficeOpenXml.Style;
using OfficeOpenXml;
using System.IO;
using Microsoft.Win32;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.IO;


namespace BOMVIEW
{
    public partial class CatalogDuplicatesDialog : Window
    {
        private readonly RateLimitedOpenBomService _openBomService;
        private readonly ILogger _logger;
        private readonly ObservableCollection<CatalogDuplicateGroup> _duplicateGroups;
        private ObservableCollection<OpenBomListItem> _catalogs;

        public CatalogDuplicatesDialog(ILogger logger)
        {
            InitializeComponent();
            _logger = logger;
            _openBomService = new RateLimitedOpenBomService(logger);
            _duplicateGroups = new ObservableCollection<CatalogDuplicateGroup>();
            _catalogs = new ObservableCollection<OpenBomListItem>();

            DuplicateGroups.ItemsSource = _duplicateGroups;
            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            try
            {
                LoadingOverlay.Show("Loading catalogs...");
                var catalogs = await _openBomService.ListCatalogsAsync();
                _catalogs.Clear();
                foreach (var catalog in catalogs)
                {
                    _catalogs.Add(catalog);
                }
                CatalogSelector.ItemsSource = _catalogs;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading catalogs: {ex.Message}");
                MessageBox.Show($"Error loading catalogs: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Hide();
            }
        }

        private async void StartSearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (!SearchAllCatalogsCheckbox.IsChecked.Value && CatalogSelector.SelectedItem == null)
            {
                MessageBox.Show("Please select a catalog or check 'Search all catalogs'",
                    "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                StartSearchButton.IsEnabled = false;
                LoadingOverlay.Show("Searching for duplicates...");
                _duplicateGroups.Clear();

                var catalogsToSearch = SearchAllCatalogsCheckbox.IsChecked.Value
                    ? _catalogs.ToList()
                    : new List<OpenBomListItem> { (OpenBomListItem)CatalogSelector.SelectedItem };

                var allParts = new Dictionary<PartNumberKey, CatalogDuplicateGroup>();
                int processedCatalogs = 0;

                foreach (var catalog in catalogsToSearch)
                {
                    processedCatalogs++;
                    LoadingOverlay.LoadingMessage = $"Searching catalog: {catalog.Name}... ({processedCatalogs}/{catalogsToSearch.Count})";

                    var catalogDoc = await _openBomService.GetCatalogAsync(catalog.Id);
                    if (catalogDoc?.Columns == null || catalogDoc.Cells == null)
                        continue;

                    ProcessCatalogData(catalog, catalogDoc, allParts);

                    // Update UI periodically to show progress
                    if (processedCatalogs % 5 == 0 || processedCatalogs == catalogsToSearch.Count)
                    {
                        UpdateDuplicateGroupsUI(allParts);
                    }
                }

                UpdateDuplicateCount();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error searching for duplicates: {ex.Message}");
                MessageBox.Show($"Error searching for duplicates: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                StartSearchButton.IsEnabled = true;
                LoadingOverlay.Hide();
            }
        }


        private void ProcessCatalogData(OpenBomListItem catalog, OpenBomDocument catalogDoc,
      Dictionary<PartNumberKey, CatalogDuplicateGroup> allParts)
        {
            int partNumberIndex = catalogDoc.Columns.IndexOf("Part Number");
            int mfgPartNumberIndex = catalogDoc.Columns.IndexOf("Manufacturer Part Number");
            int descriptionIndex = catalogDoc.Columns.IndexOf("Description");

            foreach (var row in catalogDoc.Cells)
            {
                if (row == null || row.Count() <= partNumberIndex)
                    continue;

                string partNumber = row.ElementAtOrDefault(partNumberIndex)?.ToString();
                if (string.IsNullOrWhiteSpace(partNumber))
                    continue;

                string manufacturerPartNumber = mfgPartNumberIndex >= 0
                    ? row.ElementAtOrDefault(mfgPartNumberIndex)?.ToString()
                    : "";

                string description = descriptionIndex >= 0
                    ? row.ElementAtOrDefault(descriptionIndex)?.ToString() ?? ""
                    : "";

                var key = new PartNumberKey(partNumber, manufacturerPartNumber);

                if (!allParts.TryGetValue(key, out var group))
                {
                    group = new CatalogDuplicateGroup
                    {
                        PartNumber = partNumber,
                        ManufacturerPartNumber = manufacturerPartNumber
                    };
                    allParts.Add(key, group);
                }

                group.Entries.Add(new CatalogDuplicateEntry
                {
                    CatalogId = catalog.Id,
                    CatalogName = catalog.Name,
                    PartNumber = partNumber,
                    ManufacturerPartNumber = manufacturerPartNumber,
                    Description = description
                });
            }
        }


        private void ProcessCatalogData(OpenBomListItem catalog, OpenBomDocument catalogDoc,
     Dictionary<string, CatalogDuplicateGroup> allParts)
        {
            int partNumberIndex = catalogDoc.Columns.IndexOf("Part Number");
            int descriptionIndex = catalogDoc.Columns.IndexOf("Description");

            foreach (var row in catalogDoc.Cells)
            {
                if (row == null || row.Count() <= partNumberIndex)
                    continue;

                string partNumber = row.ElementAtOrDefault(partNumberIndex)?.ToString();
                if (string.IsNullOrWhiteSpace(partNumber))
                    continue;

                string description = string.Empty;
                if (descriptionIndex >= 0)
                {
                    description = row.ElementAtOrDefault(descriptionIndex)?.ToString() ?? "";
                }

                if (!allParts.TryGetValue(partNumber, out var group))
                {
                    group = new CatalogDuplicateGroup { PartNumber = partNumber };
                    allParts.Add(partNumber, group);
                }

                group.Entries.Add(new CatalogDuplicateEntry
                {
                    CatalogId = catalog.Id,
                    CatalogName = catalog.Name,
                    PartNumber = partNumber,
                    Description = description
                });
            }
        }

        private void UpdateDuplicateGroupsUI(Dictionary<string, CatalogDuplicateGroup> allParts)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _duplicateGroups.Clear();
                foreach (var group in allParts.Values.Where(g => g.Count > 1))
                {
                    _duplicateGroups.Add(group);
                }
                UpdateDuplicateCount();
            });
        }

        private void UpdateDuplicateCount()
        {
            DuplicateCountText.Text = $"({_duplicateGroups.Count} duplicate groups found)";
        }

        private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as FrameworkElement;
            var partNumber = button?.Tag as string;
            var group = _duplicateGroups.FirstOrDefault(g => g.PartNumber == partNumber);

            if (group == null)
                return;

            var selectedEntries = group.Entries.Where(e => e.IsSelected).ToList();
            if (!selectedEntries.Any())
            {
                MessageBox.Show("Please select entries to delete",
                    "Selection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to delete {selectedEntries.Count} selected entries?",
                "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                LoadingOverlay.Show("Deleting selected entries...");
                foreach (var entry in selectedEntries)
                {
                    await _openBomService.RemovePartFromCatalogAsync(entry.CatalogId, entry.PartNumber);
                    group.Entries.Remove(entry);
                }

                if (group.Entries.Count < 2)
                {
                    _duplicateGroups.Remove(group);
                }

                UpdateDuplicateCount();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error deleting entries: {ex.Message}");
                MessageBox.Show($"Error deleting entries: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Hide();
            }
        }

        private async void KeepSelected_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as FrameworkElement;
            var partNumber = button?.Tag as string;
            var group = _duplicateGroups.FirstOrDefault(g => g.PartNumber == partNumber);

            if (group == null)
                return;

            var selectedEntries = group.Entries.Where(e => e.IsSelected).ToList();
            if (selectedEntries.Count != 1)
            {
                MessageBox.Show("Please select exactly one entry to keep",
                    "Selection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var entriesToDelete = group.Entries.Except(selectedEntries).ToList();
            var result = MessageBox.Show(
                $"Are you sure you want to delete {entriesToDelete.Count} entries and keep only the selected one?",
                "Confirm Action", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                LoadingOverlay.Show("Processing...");
                foreach (var entry in entriesToDelete)
                {
                    await _openBomService.RemovePartFromCatalogAsync(entry.CatalogId, entry.PartNumber);
                    group.Entries.Remove(entry);
                }

                _duplicateGroups.Remove(group);
                UpdateDuplicateCount();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing entries: {ex.Message}");
                MessageBox.Show($"Error processing entries: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Hide();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }


        private async void ExportToExcel_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                Title = "Export Duplicate Products",
                AddExtension = true,
                DefaultExt = "xlsx"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    LoadingOverlay.Show("Exporting duplicates...");
                    using var package = new ExcelPackage();
                    var worksheet = package.Workbook.Worksheets.Add("Duplicate Products");

                    // Add headers
                    var headers = new[] { "Part Number", "Catalog Name", "Description", "Total Occurrences" };
                    for (int i = 0; i < headers.Length; i++)
                    {
                        worksheet.Cells[1, i + 1].Value = headers[i];
                        worksheet.Cells[1, i + 1].Style.Font.Bold = true;
                        worksheet.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        worksheet.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                    }

                    // Add data
                    int row = 2;
                    foreach (var group in _duplicateGroups)
                    {
                        foreach (var entry in group.Entries)
                        {
                            worksheet.Cells[row, 1].Value = entry.PartNumber;
                            worksheet.Cells[row, 2].Value = entry.CatalogName;
                            worksheet.Cells[row, 3].Value = entry.Description;
                            worksheet.Cells[row, 4].Value = group.Count;

                            // Add alternating row colors
                            if (row % 2 == 0)
                            {
                                var rowRange = worksheet.Cells[row, 1, row, headers.Length];
                                rowRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                                rowRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(242, 242, 242));
                            }

                            row++;
                        }

                        // Add a blank row between groups
                        row++;
                    }

                    // Auto-fit columns
                    worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

                    // Add borders
                    var dataRange = worksheet.Cells[1, 1, row - 1, headers.Length];
                    dataRange.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                    dataRange.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                    dataRange.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                    dataRange.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;

                    // Save the file
                    await package.SaveAsAsync(new FileInfo(saveFileDialog.FileName));

                    MessageBox.Show("Export completed successfully!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error exporting duplicates: {ex.Message}");
                    MessageBox.Show($"Error exporting duplicates: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    LoadingOverlay.Hide();
                }
            }


        }

        private void UpdateDuplicateGroupsUI(Dictionary<PartNumberKey, CatalogDuplicateGroup> allParts)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _duplicateGroups.Clear();
                foreach (var group in allParts.Values.Where(g => g.Count > 1))
                {
                    _duplicateGroups.Add(group);
                }
                UpdateDuplicateCount();
            });
        }
    }


}
