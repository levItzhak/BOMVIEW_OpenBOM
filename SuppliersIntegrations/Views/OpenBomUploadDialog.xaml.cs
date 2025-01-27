using BOMVIEW.Controls;
using BOMVIEW.Interfaces;
using BOMVIEW.Models;
using BOMVIEW.OpenBOM.Models;
using BOMVIEW.Services;
using BOMVIEW.Views;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;


namespace BOMVIEW
{
    public partial class OpenBomUploadDialog : Window
    {
        private ConcurrentDictionary<string, bool> _partExistsCache = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private ConcurrentDictionary<string, CatalogPartInfo> _catalogPartInfoCache = new ConcurrentDictionary<string, CatalogPartInfo>(StringComparer.OrdinalIgnoreCase);
        private ConcurrentDictionary<string, OpenBomListItem> _catalogCache = new ConcurrentDictionary<string, OpenBomListItem>(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastCatalogRefresh = DateTime.MinValue;
        private TimeSpan _catalogCacheExpiration = TimeSpan.FromMinutes(3);
        private SemaphoreSlim _catalogSemaphore = new SemaphoreSlim(1, 1);
        private SemaphoreSlim _apiSemaphore = new SemaphoreSlim(5, 5); // Allow 5 concurrent API calls
        private readonly OpenBomService _openBomService;
        private readonly ObservableCollection<BomEntry> _bomEntries;
        private OpenBomListItem _selectedBom;
        private readonly ILogger _logger;
        private readonly DigiKeyService _digiKeyService;
        private OpenBomListItem _selectedComparisonBom;
        private List<BomEntry> _comparisonResults;
        private List<BomEntry> _finalPartsToUpload;
        private List<BomEntry> _skippedParts;
        private List<BomEntry> _modifiedParts;
        private RateLimitedOpenBomService _rateLimitedOpenBomService;
        private bool _comparisonCompleted = false;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly object _cacheLock = new object();
        private bool _isInitializing = true;
        private List<OpenBomListItem> _availableCatalogs = new List<OpenBomListItem>();
        private List<OpenBomListItem> _availableParentBoms = new List<OpenBomListItem>();
        private OpenBomListItem _selectedParentBom;
        private ObservableCollection<OpenBomListItem> _filteredParentBoms = new ObservableCollection<OpenBomListItem>();
        private ConcurrentDictionary<string, bool> _bomExistsCache = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);



        public OpenBomUploadDialog(ILogger logger, ObservableCollection<BomEntry> bomEntries, DigiKeyService digiKeyService)
        {
            try
            {
                _logger = logger;
                _digiKeyService = digiKeyService;
                _openBomService = new OpenBomService(logger);
                _bomEntries = bomEntries;

                // Initialize collections that might be null
                _finalPartsToUpload = new List<BomEntry>();
                _skippedParts = new List<BomEntry>();
                _modifiedParts = new List<BomEntry>();
                _comparisonResults = new List<BomEntry>();
                _rateLimitedOpenBomService = new RateLimitedOpenBomService(_logger, _digiKeyService);

                // Initialize component AFTER initializing fields
                InitializeComponent();

                // These settings had been causing problems by triggering events before initialization
                rbNewBom.Checked -= rbNewBom_Checked;
                rbExistingBom.Checked -= rbExistingBom_Checked;
                rbChildBom.Checked -= rbChildBom_Checked;

                // Now initialize the radio buttons explicitly
                gridNewBom.Visibility = Visibility.Visible;
                gridChildBom.Visibility = Visibility.Collapsed;

                // Now reconnect events
                rbNewBom.Checked += rbNewBom_Checked;
                rbExistingBom.Checked += rbExistingBom_Checked;
                rbChildBom.Checked += rbChildBom_Checked;
                lstParentBomResults.ItemsSource = _filteredParentBoms;

                InitializeTreeView();

                if (!_bomEntries.Any())
                {
                    StatusText.Text = "No BOM entries to upload.";
                    UploadButton.IsEnabled = false;
                }

                // Load catalogs and parent BOMs in the background
                Task.Run(async () =>
                {
                    await LoadCatalogsAsync();
                    await LoadParentBomsAsync();

                    await Dispatcher.InvokeAsync(() =>
                    {
                        _isInitializing = false;
                    });
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing OpenBomUploadDialog: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                    "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InitializeTreeView()
        {
            try
            {
                if (BomTreeView != null)
                {
                    BomTreeView.Initialize(_logger);
                    BomTreeView.OnItemSelected += HandleItemSelected;
                    BomTreeView.OnNodeSelected += HandleNodeSelected;
                }
                else
                {
                    _logger?.LogError("BomTreeView is null in InitializeTreeView");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error in InitializeTreeView: {ex.Message}");
            }
        }

        private void FilterParentBoms(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                // If no search text, show all parent BOMs
                _filteredParentBoms.Clear();
                foreach (var bom in _availableParentBoms)
                {
                    _filteredParentBoms.Add(bom);
                }
                return;
            }

            // Filter BOMs by name or part number containing the search text
            searchText = searchText.ToLower();
            _filteredParentBoms.Clear();

            foreach (var bom in _availableParentBoms)
            {
                bool nameMatch = !string.IsNullOrEmpty(bom.Name) && bom.Name.ToLower().Contains(searchText);
                bool idMatch = !string.IsNullOrEmpty(bom.Id) && bom.Id.ToLower().Contains(searchText);
                bool partNumberMatch = !string.IsNullOrEmpty(bom.PartNumber) && bom.PartNumber.ToLower().Contains(searchText);

                if (nameMatch || idMatch || partNumberMatch)
                {
                    _filteredParentBoms.Add(bom);
                }
            }
        }

        // Event handler for search text changes
        private void txtParentBomSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterParentBoms(txtParentBomSearch.Text);
        }

        // Event handler for search button click
        private void btnSearchParentBom_Click(object sender, RoutedEventArgs e)
        {
            FilterParentBoms(txtParentBomSearch.Text);
        }

        // Event handler for parent BOM selection
        private void lstParentBomResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedParentBom = lstParentBomResults.SelectedItem as OpenBomListItem;

            if (_selectedParentBom != null)
            {
                txtSelectedParentBom.Text = $"Selected: {_selectedParentBom.Name}";

                // Add part number if available
                if (!string.IsNullOrEmpty(_selectedParentBom.PartNumber))
                {
                    txtSelectedParentBom.Text += $" (Part #: {_selectedParentBom.PartNumber})";
                }
            }
            else
            {
                txtSelectedParentBom.Text = string.Empty;
            }
        }

        private void HandleItemSelected(string itemId)
        {
            // This is called when an item is selected, but we'll defer the logic
            // to HandleNodeSelected which has more information
        }

        // This is the updated version to keep:
        private void HandleNodeSelected(BomTreeNode node)
        {
            if (node == null || string.IsNullOrEmpty(node.Id))
            {
                UploadButton.IsEnabled = rbNewBom.IsChecked ?? false;
                _selectedBom = null;
                return;
            }

            // Only enable upload for BOM folders when using existing BOM
            if (node.TreeNodeType == BomTreeNode.NodeType.Bom && node.Type == "folder")
            {
                _selectedBom = new OpenBomListItem { Id = node.Id };
                if (rbExistingBom.IsChecked ?? false)
                {
                    UploadButton.IsEnabled = true;
                }
            }
            else
            {
                if (rbExistingBom.IsChecked ?? false)
                {
                    UploadButton.IsEnabled = false;
                }
                _selectedBom = null;
            }
        }


        private async void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool skipCatalogProcessing = chkSkipCatalogProcessing.IsChecked ?? false;
                bool isNewBom = rbNewBom.IsChecked ?? false;
                bool isChildBom = rbChildBom.IsChecked ?? false;

                // Validation based on selected mode
                if (isNewBom)
                {
                    if (string.IsNullOrWhiteSpace(txtNewBomPartNumber.Text))
                    {
                        MessageBox.Show("Please enter a part number for the new BOM", "Part Number Required",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
                else if (isChildBom)
                {
                    if (string.IsNullOrWhiteSpace(txtChildBomPartNumber.Text))
                    {
                        MessageBox.Show("Please enter a part number for the child BOM", "Part Number Required",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (_selectedParentBom == null)
                    {
                        MessageBox.Show("Please select a parent BOM", "Parent BOM Required",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
                else if (!isNewBom && _selectedBom == null)
                {
                    MessageBox.Show("Please select a BOM first", "Selection Required",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Check if comparison is enabled but no comparison BOM selected
                if (IsComparisonMode && _selectedComparisonBom == null && !_comparisonCompleted)
                {
                    MessageBox.Show("Please select a BOM to compare against", "Comparison BOM Required",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Confirmation dialog
                string message;
                if (_comparisonCompleted)
                {
                    // If we've completed a comparison, show detailed stats
                    message = $"Ready to upload {_finalPartsToUpload.Count} parts" +
                             $" ({_finalPartsToUpload.Count - _modifiedParts.Count} new, {_modifiedParts.Count} modified).\n\n" +
                             $"{_skippedParts.Count} parts will be skipped.\n\nContinue with upload?";
                }
                else
                {
                    if (isNewBom)
                    {
                        message = $"Are you sure you want to create a new BOM with part number {txtNewBomPartNumber.Text} and upload {_bomEntries.Count} parts?";
                    }
                    else if (isChildBom)
                    {
                        var parentBom = _selectedParentBom;
                        message = $"Are you sure you want to create a child BOM ({txtChildBomPartNumber.Text}) under parent {parentBom.Name} and upload {_bomEntries.Count} parts?";
                    }
                    else
                    {
                        message = $"Are you sure you want to upload {_bomEntries.Count} parts to the selected BOM?";
                    }
                }

                var result = MessageBox.Show(message, "Confirm Upload",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;

                // Disable controls and show loading
                UploadButton.IsEnabled = false;
                BomTreeView.IsEnabled = false;
                StatusText.Text = "Processing in progress...";

                // Initialize cancellation token
                _cancellationTokenSource = new CancellationTokenSource();
                LoadingOverlay.ShowWithCancellation("BOM Upload", "Processing...", _cancellationTokenSource);
                LoadingOverlay.CancellationRequested += (s, e) => {
                    StatusText.Text = "Operation cancelled by user.";
                };

                // If comparison is enabled and not yet completed, perform comparison
                if (IsComparisonMode && !_comparisonCompleted)
                {
                    try
                    {
                        await PerformComparison();

                        // Show summary after comparison
                        SummaryButton.Visibility = Visibility.Visible;
                        _comparisonCompleted = true;

                        // Give user a chance to view the summary before uploading
                        LoadingOverlay.Hide();
                        StatusText.Text = "Comparison completed. View the summary and click Upload to continue.";
                        UploadButton.IsEnabled = true;
                        BomTreeView.IsEnabled = true;
                        UploadButton.Content = "Proceed with Upload";

                        // Show a prompt
                        MessageBox.Show("Comparison completed. Please review the summary before proceeding with upload.",
                            "Comparison Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                        // Automatically show the summary dialog
                        ShowSummaryDialog();

                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error during comparison: {ex.Message}");
                        MessageBox.Show($"Error during comparison: {ex.Message}. Please try again.",
                            "Comparison Error", MessageBoxButton.OK, MessageBoxImage.Error);

                        UploadButton.IsEnabled = true;
                        BomTreeView.IsEnabled = true;
                        LoadingOverlay.Hide();
                        return;
                    }
                }
                else if (!IsComparisonMode && !_comparisonCompleted)
                {
                    // No comparison, use all parts
                    _finalPartsToUpload = _bomEntries.ToList();
                    _skippedParts = new List<BomEntry>();
                    _modifiedParts = new List<BomEntry>();
                }

                // Proceed with the upload
                if (isChildBom)
                {
                    await PerformChildBomUpload();
                }
                else
                {
                    await PerformUpload();
                }
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show("Operation was cancelled", "Cancelled",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                StatusText.Text = "Operation cancelled.";
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during upload: {ex.Message}");
                MessageBox.Show($"Error during upload: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = $"Error: {ex.Message}";
            }
            finally
            {
                // Re-enable controls and hide loading
                UploadButton.IsEnabled = true;
                BomTreeView.IsEnabled = true;
                LoadingOverlay.Hide();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private async void ProcessCatalogFirstButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Disable buttons during processing
                UploadButton.IsEnabled = false;

                // Show loading indicator
                LoadingOverlay.Show("Catalog Processing", "Preparing parts catalog data...");

                // Step 1: Collect all parts needing catalog definition
                var partsNeedingCatalog = _bomEntries.Where(part => !string.IsNullOrWhiteSpace(part.OrderingCode)).ToList();

                if (partsNeedingCatalog.Count == 0)
                {
                    MessageBox.Show("No parts found with valid ordering codes.",
                        "No Parts Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    LoadingOverlay.Hide();
                    UploadButton.IsEnabled = true;
                    return;
                }

                // Step 2: Get all catalogs
                var catalogs = await _rateLimitedOpenBomService.ListCatalogsAsync();

                // Step 3: Show catalog assignment dialog
                var catalogAssignmentDialog = new BulkCatalogAssignmentDialog(
                    _logger,
                    _digiKeyService,
                    _rateLimitedOpenBomService,
                    partsNeedingCatalog,
                    catalogs)
                {
                    Owner = this
                };

                if (catalogAssignmentDialog.ShowDialog() != true)
                {
                    LoadingOverlay.Hide();
                    UploadButton.IsEnabled = true;
                    return;
                }

                // Step 4: Process catalog assignments
                var catalogAssignments = catalogAssignmentDialog.PartsToAssign;

                if (!catalogAssignments.Any(p => p.SelectedCatalog != null))
                {
                    MessageBox.Show("No catalogs were assigned to parts.",
                        "No Assignments", MessageBoxButton.OK, MessageBoxImage.Warning);
                    LoadingOverlay.Hide();
                    UploadButton.IsEnabled = true;
                    return;
                }

                // Initialize the cancellation token
                _cancellationTokenSource = new CancellationTokenSource();
                LoadingOverlay.ShowWithCancellation("Catalog Update", "Adding parts to catalogs...", _cancellationTokenSource);

                // Process catalog assignments - grouped by catalog
                var catalogGroups = catalogAssignments
                    .Where(p => p.SelectedCatalog != null)
                    .GroupBy(p => p.SelectedCatalog.Id);

                int totalCatalogs = catalogGroups.Count();
                int catalogsProcessed = 0;

                foreach (var catalogGroup in catalogGroups)
                {
                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                        break;

                    catalogsProcessed++;
                    string catalogId = catalogGroup.Key;
                    var catalogName = catalogGroup.First().SelectedCatalog.Name;
                    var partsForCatalog = catalogGroup.ToList();

                    LoadingOverlay.UpdateStatus($"Processing catalog {catalogsProcessed}/{totalCatalogs}: {catalogName}",
                        $"Adding/updating {partsForCatalog.Count} parts");

                    int partsProcessed = 0;
                    int partsSuccess = 0;
                    int partsError = 0;

                    foreach (var partInfo in partsForCatalog)
                    {
                        if (_cancellationTokenSource.Token.IsCancellationRequested)
                            break;

                        partsProcessed++;
                        LoadingOverlay.UpdateProgress(partsProcessed, partsForCatalog.Count, "Parts");

                        try
                        {
                            // Find matching BOM entry for this part
                            var bomEntry = _bomEntries.FirstOrDefault(p => p.OrderingCode == partInfo.PartNumber);
                            if (bomEntry == null) continue;

                            // Step 1: First check if part exists in the catalog
                            bool partExists = await CheckPartExistsInCatalogWithCacheAsync(partInfo.PartNumber);

                            if (!partExists)
                            {
                                // Add part to catalog
                                LoadingOverlay.UpdateStatus($"Adding part {partInfo.PartNumber} to catalog {catalogName}...");

                                // Use the method from CatalogBulkUpdateDialog to define the part properties
                                bool success = await _rateLimitedOpenBomService.AddPartToCatalogWithRetryAsync(catalogId, bomEntry);

                                if (!success)
                                {
                                    _logger.LogWarning($"Failed to add part {partInfo.PartNumber} to catalog {catalogName}");
                                    partsError++;
                                    continue;
                                }
                            }

                            // Step 2: Update DigiKey data for all parts (even existing ones)
                            LoadingOverlay.UpdateStatus($"Updating DigiKey data for {partInfo.PartNumber}...");

                            // Get DigiKey data
                            var supplierData = await _digiKeyService.GetPriceAndAvailabilityAsync(partInfo.PartNumber);

                            if (supplierData != null && supplierData.IsAvailable)
                            {
                                // First update image if available
                                if (!string.IsNullOrEmpty(supplierData.ImageUrl))
                                {
                                    try
                                    {
                                        var imageBytes = await _digiKeyService.GetImageBytesAsync(supplierData.ImageUrl);
                                        if (imageBytes != null && imageBytes.Length > 0)
                                        {
                                            await _openBomService.UploadCatalogImageAsync(
                                                catalogId,
                                                partInfo.PartNumber,
                                                imageBytes,
                                                "Thumbnail image"
                                            );
                                        }
                                    }
                                    catch (Exception imageEx)
                                    {
                                        _logger.LogWarning($"Error updating image for {partInfo.PartNumber}: {imageEx.Message}");
                                    }
                                }

                                // Update part properties using same logic as CatalogBulkUpdateDialog
                                var properties = new Dictionary<string, string>();

                                // Description
                                if (!string.IsNullOrEmpty(supplierData.Description))
                                {
                                    properties["Description"] = supplierData.Description.Trim();
                                }

                                // Cost
                                properties["Cost"] = supplierData.Price.ToString("F2");

                                // Lead Time
                                if (!string.IsNullOrEmpty(supplierData.LeadTime?.ToString()))
                                {
                                    properties["Lead time"] = supplierData.LeadTime.ToString();
                                }

                                // Manufacturer
                                if (!string.IsNullOrEmpty(supplierData.Manufacturer))
                                {
                                    properties["Manufacturer"] = supplierData.Manufacturer.Trim();
                                }

                                // Vendor
                                properties["Vendor"] = "DIGI-KEY CORPORATION";
                                properties["Catalog Indicator"] = catalogName;

                                // Links
                                if (!string.IsNullOrEmpty(supplierData.ProductUrl))
                                {
                                    properties["Link"] = supplierData.ProductUrl.Trim();
                                }

                                // Datasheet
                                if (!string.IsNullOrEmpty(supplierData.DatasheetUrl))
                                {
                                    properties["Data Sheet"] = supplierData.DatasheetUrl.Trim();
                                }

                                // Quantity Available
                                properties["Quantity Available"] = supplierData.Availability.ToString();

                                // Category
                                if (!string.IsNullOrEmpty(supplierData.Category))
                                {
                                    properties["Catalog supplier"] = supplierData.Category;
                                }

                                // MOQ
                                int moq = 1;
                                if (supplierData.PriceBreaks.Any())
                                {
                                    moq = supplierData.PriceBreaks.Min(pb => pb.Quantity);
                                }
                                properties["Minimum Order Quantity"] = moq.ToString();

                                // Update the catalog part with these properties
                                await _openBomService.UpdateCatalogPartAsync(catalogId, new OpenBomPartRequest
                                {
                                    PartNumber = partInfo.PartNumber,
                                    Properties = properties
                                });
                            }

                            partsSuccess++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Error processing part {partInfo.PartNumber}: {ex.Message}");
                            partsError++;
                        }

                        // Add delay to avoid rate limiting
                        await Task.Delay(700, _cancellationTokenSource.Token);
                    }

                    _logger.LogInfo($"Completed catalog {catalogName}: {partsSuccess} successful, {partsError} errors");

                    // Add delay between catalogs
                    await Task.Delay(700, _cancellationTokenSource.Token);
                }

                // Completion message
                if (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    MessageBox.Show("Catalog processing complete! Ready to proceed with BOM upload.",
                        "Catalog Processing Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Enable upload button with updated text
                    UploadButton.Content = "Upload BOM (Catalogs Updated)";
                }
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show("Operation was cancelled", "Cancelled",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during catalog processing: {ex.Message}");
                MessageBox.Show($"Error during catalog processing: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Re-enable controls and hide loading
                UploadButton.IsEnabled = true;
                LoadingOverlay.Hide();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private void ViewButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedBom == null)
                {
                    MessageBox.Show("No BOM selected", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var url = $"https://app.openbom.com/bom/{_selectedBom.Id}";
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening OpenBOM: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CatalogDuplicatesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new CatalogDuplicatesDialog(_logger)
                {
                    Owner = this
                };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error showing catalog duplicates dialog: {ex.Message}");
                MessageBox.Show($"Error showing dialog: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void BulkUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedBom == null)
                {
                    MessageBox.Show("Please select a catalog first", "Selection Required",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dialog = new BulkUpdateDialog(_logger, _digiKeyService, _openBomService, _selectedBom.Id)
                {
                    Owner = this
                };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error showing bulk update dialog: {ex.Message}");
                MessageBox.Show($"Error showing dialog: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CatalogBulkUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new CatalogBulkUpdateDialog(_logger, _digiKeyService, _openBomService)
                {
                    Owner = this
                };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error showing catalog bulk update dialog: {ex.Message}");
                MessageBox.Show($"Error showing dialog: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public bool IsComparisonMode => chkCompareWithExisting.IsChecked ?? false;

        private void rbNewBom_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (gridNewBom != null)
                {
                    gridNewBom.Visibility = Visibility.Visible;
                    gridChildBom.Visibility = Visibility.Collapsed;
                }

                // If creating a new BOM, we don't need to select a BOM in the tree
                if (UploadButton != null)
                    UploadButton.IsEnabled = true;

                _selectedBom = null;
            }
            catch (Exception ex)
            {
                // Log but don't crash
                _logger?.LogError($"Error in rbNewBom_Checked: {ex.Message}");
            }
        }

        private void rbExistingBom_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (gridNewBom != null)
                {
                    gridNewBom.Visibility = Visibility.Collapsed;
                    gridChildBom.Visibility = Visibility.Collapsed;
                }

                // If using existing BOM, require selection from the tree
                if (UploadButton != null)
                    UploadButton.IsEnabled = _selectedBom != null;
            }
            catch (Exception ex)
            {
                // Log but don't crash
                _logger?.LogError($"Error in rbExistingBom_Checked: {ex.Message}");
            }
        }

        private void chkCompareWithExisting_Checked(object sender, RoutedEventArgs e)
        {
            gridComparison.Visibility = Visibility.Visible;
        }

        private void chkCompareWithExisting_Unchecked(object sender, RoutedEventArgs e)
        {
            gridComparison.Visibility = Visibility.Collapsed;
            _selectedComparisonBom = null;
            txtSelectedComparison.Text = string.Empty;
        }

        private void txtComparisonSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            // This will be implemented to filter results as the user types
        }

        private async void btnSearchBom_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string searchTerm = txtComparisonSearch.Text?.Trim();
                if (string.IsNullOrEmpty(searchTerm))
                {
                    MessageBox.Show("Please enter a search term", "Search Required",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                btnSearchBom.IsEnabled = false;
                LoadingOverlay.Show("BOM Search", "Searching BOMs...");

                var allBoms = await _openBomService.ListBomsAsync();
                var filteredBoms = allBoms
                    .Where(b => b.MatchesSearch(searchTerm))
                    .ToList();

                lstComparisonResults.ItemsSource = filteredBoms;

                if (filteredBoms.Count == 0)
                {
                    MessageBox.Show("No BOMs found matching your search", "No Results",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error searching BOMs: {ex.Message}");
                MessageBox.Show($"Error searching BOMs: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnSearchBom.IsEnabled = true;
                LoadingOverlay.Hide();
            }
        }

        private void lstComparisonResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedComparisonBom = lstComparisonResults.SelectedItem as OpenBomListItem;
            if (_selectedComparisonBom != null)
            {
                txtSelectedComparison.Text = $"Selected BOM: {_selectedComparisonBom.Name} (ID: {_selectedComparisonBom.Id})";
            }
            else
            {
                txtSelectedComparison.Text = string.Empty;
            }
        }

        private void SummaryButton_Click(object sender, RoutedEventArgs e)
        {
            // Will show the summary dialog
            ShowSummaryDialog();
        }



        private void ShowSummaryDialog()
        {
            try
            {
                // Ensure we have our parts lists
                if (_finalPartsToUpload == null)
                    _finalPartsToUpload = new List<BomEntry>();

                if (_skippedParts == null)
                    _skippedParts = new List<BomEntry>();

                if (_modifiedParts == null)
                    _modifiedParts = new List<BomEntry>();

                // Log what we're showing in the dialog
                _logger?.LogInfo($"Showing summary dialog with {_finalPartsToUpload.Count} parts to upload, " +
                                $"{_skippedParts.Count} skipped parts, and {_modifiedParts.Count} modified parts");

                var dialog = new BomSummaryDialog(_logger,
                    _bomEntries.ToList(),
                    _finalPartsToUpload,
                    _skippedParts,
                    _modifiedParts)
                {
                    Owner = this
                };

                // Show the dialog and get result
                if (dialog.ShowDialog() == true)
                {
                    // If user closed with the dialog result = true, update our list
                    var updatedParts = dialog.GetModifiedPartsToUpload();
                    if (updatedParts != null && updatedParts.Any())
                    {
                        _finalPartsToUpload = updatedParts;
                        _logger.LogInfo($"Updated final parts list after summary review: {_finalPartsToUpload.Count} parts");

                        // Update the upload button text to reflect the final count
                        UploadButton.Content = $"Upload {_finalPartsToUpload.Count} Parts";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error showing summary dialog: {ex.Message}");
                MessageBox.Show($"Error showing summary dialog: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private async Task PerformComparison()
        {
            try
            {
                LoadingOverlay.UpdateStatus("Retrieving comparison BOM data...");
                // Get the comparison BOM data
                var comparisonBomData = await _rateLimitedOpenBomService.GetBomHierarchyAsync(_selectedComparisonBom.Id);
                _logger.LogInfo($"Retrieved {comparisonBomData.Count} items from comparison BOM");

                // Initialize collections
                _finalPartsToUpload = new List<BomEntry>();
                _skippedParts = new List<BomEntry>();
                _modifiedParts = new List<BomEntry>();

                LoadingOverlay.UpdateStatus("Comparing BOMs...");

                // Dictionary for fast lookup of parts in comparison BOM
                var partLookup = new Dictionary<string, BomTreeNode>(StringComparer.OrdinalIgnoreCase);

                foreach (var part in comparisonBomData)
                {
                    if (!string.IsNullOrEmpty(part.PartNumber))
                    {
                        var normalizedPartNumber = NormalizePartNumber(part.PartNumber);
                        if (!partLookup.ContainsKey(normalizedPartNumber))
                        {
                            partLookup.Add(normalizedPartNumber, part);
                        }
                    }
                }

                _logger.LogInfo($"Created lookup dictionary with {partLookup.Count} unique parts");

                // Perform comparison with improved progress reporting
                int totalCount = _bomEntries.Count;
                int processedCount = 0;
                int matchedCount = 0;
                int skippedCount = 0;
                int modifiedCount = 0;
                int newCount = 0;

                LoadingOverlay.UpdateProgress(0, totalCount, "Comparison");
                LoadingOverlay.UpdateStatus($"Starting comparison of {totalCount} parts...");

                foreach (var part in _bomEntries)
                {
                    processedCount++;

                    // Update progress every 5 items or on last item
                    if (processedCount % 5 == 0 || processedCount == totalCount)
                    {
                        LoadingOverlay.UpdateProgress(processedCount, totalCount, "Comparison");
                        LoadingOverlay.UpdateStatus($"Comparing part {processedCount} of {totalCount}...");
                    }

                    if (string.IsNullOrWhiteSpace(part.OrderingCode))
                    {
                        // Skip entries with no ordering code
                        _logger.LogWarning($"Skipping part at position {processedCount} - no ordering code");
                        continue;
                    }

                    var normalizedPartNumber = NormalizePartNumber(part.OrderingCode);
                    _logger.LogInfo($"Checking part {part.OrderingCode} (normalized: {normalizedPartNumber})");

                    // Check if part exists in comparison BOM
                    if (partLookup.TryGetValue(normalizedPartNumber, out var matchingPart))
                    {
                        matchedCount++;
                        _logger.LogInfo($"Found matching part in comparison BOM: {matchingPart.PartNumber}");

                        // Parse quantity from matching part
                        int comparisonQuantity = 0;

                        // Look for quantity in properties
                        foreach (var prop in matchingPart.Properties)
                        {
                            if (prop.Key.Contains("quantity", StringComparison.OrdinalIgnoreCase) ||
                                prop.Key.Contains("qty", StringComparison.OrdinalIgnoreCase))
                            {
                                if (int.TryParse(prop.Value, out int parsedQty))
                                {
                                    comparisonQuantity = parsedQty;
                                    break;
                                }
                            }
                        }

                        _logger.LogInfo($"Quantities - New: {part.QuantityTotal}, Existing: {comparisonQuantity}");

                        if (comparisonQuantity == part.QuantityTotal)
                        {
                            // Part exists with same quantity - skip
                            _logger.LogInfo($"Part {part.OrderingCode} exists with same quantity - skipping");
                            _skippedParts.Add(part);
                            skippedCount++;
                        }
                        else
                        {
                            // Show quantity discrepancy dialog
                            _logger.LogInfo($"Quantity discrepancy detected for part {part.OrderingCode}");

                            var dialogResult = await ShowQuantityDiscrepancyDialog(part, comparisonQuantity);

                            if (dialogResult.ShouldSkip)
                            {
                                _logger.LogInfo($"User chose to skip part {part.OrderingCode}");
                                _skippedParts.Add(part);
                                skippedCount++;
                            }
                            else
                            {
                                // Add with adjusted quantity if needed
                                var adjustedPart = part.Clone();
                                if (dialogResult.UseAdjustedQuantity)
                                {
                                    int newQuantity = part.QuantityTotal - comparisonQuantity;
                                    if (newQuantity <= 0)
                                    {
                                        _logger.LogWarning($"Calculated quantity {newQuantity} for {part.OrderingCode} is not positive - skipping");
                                        _skippedParts.Add(part);
                                        skippedCount++;
                                        continue;
                                    }

                                    _logger.LogInfo($"User chose to upload with difference quantity: {newQuantity}");
                                    adjustedPart.QuantityTotal = newQuantity;
                                }
                                else
                                {
                                    _logger.LogInfo($"User chose to upload with original quantity: {part.QuantityTotal}");
                                }

                                _finalPartsToUpload.Add(adjustedPart);
                                _modifiedParts.Add(part);
                                modifiedCount++;
                            }
                        }
                    }
                    else
                    {
                        // Part doesn't exist in comparison BOM
                        _logger.LogInfo($"Part {part.OrderingCode} not found in comparison BOM - will upload as new");
                        _finalPartsToUpload.Add(part);
                        newCount++;
                    }
                }

                // Generate summary report
                _comparisonResults = new List<BomEntry>();
                _comparisonResults.AddRange(_finalPartsToUpload);
                _comparisonResults.AddRange(_skippedParts);

                _logger.LogInfo($"Comparison complete: {_finalPartsToUpload.Count} to upload ({newCount} new, {modifiedCount} modified), " +
                               $"{_skippedParts.Count} skipped");

                LoadingOverlay.UpdateStatus($"Comparison complete: {_finalPartsToUpload.Count} parts to upload, " +
                                          $"{_skippedParts.Count} parts skipped");

                StatusText.Text = $"Comparison complete. {_finalPartsToUpload.Count} parts to upload, " +
                    $"{_skippedParts.Count} parts skipped, {_modifiedParts.Count} parts modified.";
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during comparison: {ex.Message}");
                throw;
            }
        }



        // Update the ShowQuantityDiscrepancyDialog method in OpenBomUploadDialog.xaml.cs
        private async Task<QuantityDiscrepancyResult> ShowQuantityDiscrepancyDialog(BomEntry part, int comparisonQuantity)
        {
            // For interactive UI, we'll need to invoke on UI thread
            var tcs = new TaskCompletionSource<QuantityDiscrepancyResult>();

            await Dispatcher.InvokeAsync(() => {
                try
                {
                    var dialog = new QuantityDiscrepancyDialog(part, comparisonQuantity)
                    {
                        Owner = this
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        tcs.SetResult(dialog.Result);
                    }
                    else
                    {
                        // Default to skip if dialog is cancelled
                        tcs.SetResult(new QuantityDiscrepancyResult { ShouldSkip = true });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error showing quantity discrepancy dialog: {ex.Message}");
                    // Default to skip on error
                    tcs.SetResult(new QuantityDiscrepancyResult { ShouldSkip = true });
                }
            });

            return await tcs.Task;
        }

        // Modify the PerformUpload method in OpenBomUploadDialog.cs with this implementation
        private async Task PerformUpload()
        {
            try
            {
                string bomId;
                string bomName;
                bool skipCatalogProcessing = chkSkipCatalogProcessing.IsChecked ?? false;

                // Step 1: Determine BOM ID (create new or use existing)
                if (rbNewBom.IsChecked ?? false)
                {
                    // Create new BOM with provided part number
                    LoadingOverlay.UpdateStatus("Creating new BOM...");
                    bomName = !string.IsNullOrWhiteSpace(txtNewBomName.Text) ?
                              txtNewBomName.Text : txtNewBomPartNumber.Text;
                    bomId = txtNewBomPartNumber.Text;
                    _logger.LogInfo($"Creating new BOM with part number: {bomId}, name: {bomName}");

                    // הוסף: שימוש בפונקציה החדשה לאימות מספר חלק BOM
                    await ValidateAndPrepareForBomCreation(bomId);

                    var newBom = await _rateLimitedOpenBomService.CreateBomAsync(
                        bomName, bomId);

                    if (newBom == null || string.IsNullOrEmpty(newBom.Id))
                    {
                        throw new Exception("Failed to create new BOM");
                    }

                    bomId = newBom.Id;
                }
                else
                {
                    // Use selected BOM
                    bomId = _selectedBom.Id;
                    bomName = _selectedBom.Name ?? _selectedBom.Id;
                    _logger.LogInfo($"Using existing BOM: {bomName} (ID: {bomId})");
                }

                // Use the pre-selected catalog if specified
                OpenBomListItem defaultCatalog = null;
                if (rbPreSelectCatalog.IsChecked == true)
                {
                    defaultCatalog = comboDefaultCatalog.SelectedItem as OpenBomListItem;
                }

                // Process and upload parts
                await ProcessAndUploadPartsImproved(bomId, defaultCatalog, skipCatalogProcessing);

                // Final success message
                MessageBox.Show(
                    $"Upload completed!\n\n" +
                    $"Total parts processed: {_finalPartsToUpload.Count}\n" +
                    $"Successfully uploaded: {_finalPartsToUpload.Count - _skippedParts.Count}\n" +
                    $"Skipped (already exist or by choice): {_skippedParts.Count}",
                    "Upload Result",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Show view button
                ViewButton.Visibility = Visibility.Visible;

                // Prompt for export
                var exportResult = MessageBox.Show(
                    "Would you like to export a final summary report to Excel?",
                    "Export Summary",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (exportResult == MessageBoxResult.Yes)
                {
                    
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during upload: {ex.Message}");
                throw;
            }
        }

      

        private async Task<(bool Found, string CatalogId, string CatalogName, BomTreeNode Node)> CheckCatalogForPartAsync(OpenBomListItem catalog, string normalizedPartNumber)
        {
            await _apiSemaphore.WaitAsync();
            try
            {
                var node = await _rateLimitedOpenBomService.GetCatalogItemWithRetryAsync(
                    catalog.Id, normalizedPartNumber);

                if (node != null)
                {
                    return (true, catalog.Id, catalog.Name, node);
                }

                return (false, null, null, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error checking catalog {catalog.Name}: {ex.Message}");
                return (false, null, null, null);
            }
            finally
            {
                _apiSemaphore.Release();
            }
        }
        private bool IsPotentialMatch(string partNumber, string catalogName)
        {
            // Capacitor checks
            if ((partNumber.StartsWith("CL", StringComparison.OrdinalIgnoreCase) ||
                 partNumber.Contains("CAP", StringComparison.OrdinalIgnoreCase) ||
                 partNumber.Contains("GCM", StringComparison.OrdinalIgnoreCase)) &&
                catalogName.Contains("Capacitor", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Resistor checks
            if ((partNumber.StartsWith("RC", StringComparison.OrdinalIgnoreCase) ||
                 partNumber.StartsWith("RES", StringComparison.OrdinalIgnoreCase) ||
                 partNumber.StartsWith("CR", StringComparison.OrdinalIgnoreCase)) &&
                catalogName.Contains("Resistor", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Inductor checks
            if ((partNumber.StartsWith("IND", StringComparison.OrdinalIgnoreCase) ||
                 partNumber.StartsWith("LQG", StringComparison.OrdinalIgnoreCase) ||
                 partNumber.StartsWith("LQW", StringComparison.OrdinalIgnoreCase)) &&
                catalogName.Contains("Inductor", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }


        private string GetLikelyPartCatalog(string partNumber)
        {
            // More comprehensive logic to determine the most likely catalog

            // Capacitors
            if (partNumber.StartsWith("CL", StringComparison.OrdinalIgnoreCase) ||
                partNumber.StartsWith("C0", StringComparison.OrdinalIgnoreCase) ||
                partNumber.StartsWith("GCM", StringComparison.OrdinalIgnoreCase) ||
                partNumber.Contains("CAP", StringComparison.OrdinalIgnoreCase) ||
                partNumber.Contains("UF", StringComparison.OrdinalIgnoreCase) ||
                partNumber.Contains("PF", StringComparison.OrdinalIgnoreCase))
            {
                return "Capacitors";
            }

            // Resistors
            if (partNumber.StartsWith("RES", StringComparison.OrdinalIgnoreCase) ||
                partNumber.StartsWith("RC", StringComparison.OrdinalIgnoreCase) ||
                partNumber.StartsWith("CR", StringComparison.OrdinalIgnoreCase) ||
                partNumber.StartsWith("R", StringComparison.OrdinalIgnoreCase) && char.IsDigit(partNumber[1]))
            {
                return "Resistors";
            }

            // Inductors
            if (partNumber.Contains("IND", StringComparison.OrdinalIgnoreCase) ||
                partNumber.StartsWith("LQG", StringComparison.OrdinalIgnoreCase) ||
                partNumber.StartsWith("LQW", StringComparison.OrdinalIgnoreCase) ||
                partNumber.StartsWith("LMK", StringComparison.OrdinalIgnoreCase) ||
                partNumber.StartsWith("L", StringComparison.OrdinalIgnoreCase) && char.IsDigit(partNumber[1]))
            {
                return "Inductors";
            }

            // ICs
            if (partNumber.Contains("IC", StringComparison.OrdinalIgnoreCase) ||
                partNumber.StartsWith("SN", StringComparison.OrdinalIgnoreCase) ||
                partNumber.StartsWith("LM", StringComparison.OrdinalIgnoreCase) ||
                partNumber.Contains("MCU", StringComparison.OrdinalIgnoreCase) ||
                partNumber.Contains("PROC", StringComparison.OrdinalIgnoreCase))
            {
                return "Integrated Circuits";
            }

            // Connectors
            if (partNumber.Contains("CONN", StringComparison.OrdinalIgnoreCase) ||
                partNumber.Contains("HDR", StringComparison.OrdinalIgnoreCase) ||
                partNumber.Contains("SOCKET", StringComparison.OrdinalIgnoreCase))
            {
                return "Connectors";
            }

            // Default to a general catalog
            return "Passive Components";
        }

       
        private async Task<List<OpenBomListItem>> GetCatalogsWithCacheAsync()
        {
            // Check if cache exists and is still valid
            if (_catalogCache.Count > 0 && (DateTime.Now - _lastCatalogRefresh) < _catalogCacheExpiration)
            {
                return _catalogCache.Values.ToList();
            }

            // Wait for semaphore to prevent multiple requests
            await _catalogSemaphore.WaitAsync();

            try
            {
                // Check again after waiting for semaphore
                if (_catalogCache.Count > 0 && (DateTime.Now - _lastCatalogRefresh) < _catalogCacheExpiration)
                {
                    return _catalogCache.Values.ToList();
                }

                // Get new catalog list
                var catalogs = await _rateLimitedOpenBomService.ListCatalogsAsync();

                // Update cache
                _catalogCache.Clear();
                foreach (var catalog in catalogs)
                {
                    _catalogCache[catalog.Id] = catalog;
                }

                _lastCatalogRefresh = DateTime.Now;

                return catalogs;
            }
            finally
            {
                _catalogSemaphore.Release();
            }
        }



        private async Task LoadCatalogsAsync()
        {
            try
            {
                var catalogs = await _openBomService.ListCatalogsAsync();

                await Dispatcher.InvokeAsync(() =>
                {
                    _availableCatalogs = catalogs;
                    comboDefaultCatalog.ItemsSource = _availableCatalogs;

                    if (_availableCatalogs.Count > 0)
                    {
                        comboDefaultCatalog.SelectedIndex = 0;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading catalogs: {ex.Message}");
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Error loading catalogs: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private async Task LoadParentBomsAsync()
        {
            try
            {
                var boms = await _openBomService.ListBomsAsync();

                await Dispatcher.InvokeAsync(() =>
                {
                    _availableParentBoms = boms;

                    // Clear and refill filtered list
                    _filteredParentBoms.Clear();
                    foreach (var bom in _availableParentBoms)
                    {
                        _filteredParentBoms.Add(bom);
                    }

                    if (_availableParentBoms.Count > 0)
                    {
                        // Select the first BOM in the list
                        lstParentBomResults.SelectedIndex = 0;
                        _selectedParentBom = _availableParentBoms[0];
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading parent BOMs: {ex.Message}");
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Error loading parent BOMs: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private void rbPreSelectCatalog_Checked(object sender, RoutedEventArgs e)
        {
            comboDefaultCatalog.IsEnabled = true;
        }

        private async void RefreshCatalogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadingOverlay.Show("Refresh", "Refreshing catalogs...");

                await LoadCatalogsAsync();

                LoadingOverlay.Hide();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error refreshing catalogs: {ex.Message}");
                MessageBox.Show($"Error refreshing catalogs: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                LoadingOverlay.Hide();
            }
        }

        private async void RefreshParentBoms_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadingOverlay.Show("Refresh", "Refreshing parent BOMs...");

                await LoadParentBomsAsync();

                LoadingOverlay.Hide();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error refreshing parent BOMs: {ex.Message}");
                MessageBox.Show($"Error refreshing parent BOMs: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                LoadingOverlay.Hide();
            }
        }

        private void rbChildBom_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (gridChildBom != null)
                {
                    gridNewBom.Visibility = Visibility.Collapsed;
                    gridChildBom.Visibility = Visibility.Visible;
                }

                // For child BOMs, we don't need to select an existing BOM from the tree
                if (UploadButton != null)
                    UploadButton.IsEnabled = true;

                _selectedBom = null;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error in rbChildBom_Checked: {ex.Message}");
            }
        }

        private void OpenBomInBrowser(string bomId)
        {
            try
            {
                var url = $"https://app.openbom.com/bom/{bomId}";
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error opening OpenBOM URL: {ex.Message}");
                MessageBox.Show(
                    $"Could not open browser. Please navigate manually to:\nhttps://app.openbom.com/bom/{bomId}",
                    "Browser Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private async Task PerformChildBomUpload()
        {
            try
            {
                string childBomName = txtChildBomName.Text.Trim();
                string childBomPartNumber = txtChildBomPartNumber.Text.Trim();
                var parentBom = _selectedParentBom;
                bool skipCatalogProcessing = chkSkipCatalogProcessing.IsChecked ?? false;

                if (parentBom == null)
                {
                    throw new Exception("No parent BOM selected");
                }

                await ValidateAndPrepareForBomCreation(childBomPartNumber);

                // Step 1: Create the child BOM
                LoadingOverlay.UpdateStatus("Creating child BOM...");
                _logger.LogInfo($"Creating child BOM with part number {childBomPartNumber}");
                var childBom = await _rateLimitedOpenBomService.CreateBomAsync(
                    childBomName, childBomPartNumber);

                if (childBom == null || string.IsNullOrEmpty(childBom.Id))
                {
                    throw new Exception("Failed to create child BOM");
                }

                // Log the actual IDs so we know what's happening
                _logger.LogInfo($"Child BOM created with ID: {childBom.Id}, Name: {childBomName}, Part Number: {childBomPartNumber}");

                // Step 2: Process part existence in batches
                string bomId = childBom.Id;

                // Use the pre-selected catalog if specified
                OpenBomListItem defaultCatalog = null;
                if (rbPreSelectCatalog.IsChecked == true)
                {
                    defaultCatalog = comboDefaultCatalog.SelectedItem as OpenBomListItem;
                }

                // Upload parts to the child BOM first - הוסף: שימוש בשיטה המשופרת
                await ProcessAndUploadPartsImproved(bomId, defaultCatalog, skipCatalogProcessing);

                // Step 3: Add the child BOM to the parent - THIS IS THE KEY PART
                LoadingOverlay.UpdateStatus("Adding child BOM to parent...");

                // Use the childBomPartNumber for the part number in the parent-child relationship
                await AddBomAsChildToParent(parentBom.Id, bomId, childBomPartNumber, childBomName);

                // Final success message
                MessageBox.Show($"Successfully created child BOM '{childBomName}' with {_finalPartsToUpload.Count} parts and added it to parent BOM '{parentBom.Name}'." +
                                $"\n\nNote: To see the child BOM contents within the parent, you may need to use the 'Flattened View' option in OpenBOM.",
                    "Upload Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                // Offer to open the child BOM
                var openResult = MessageBox.Show(
                    "Would you like to open the new child BOM in OpenBOM?",
                    "Open in Browser",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (openResult == MessageBoxResult.Yes)
                {
                    OpenBomInBrowser(bomId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during child BOM upload: {ex.Message}");
                throw;
            }
        }

        private async Task<Dictionary<string, CatalogPartInfo>> CheckPartsExistInCatalogsAsync(List<string> partNumbers, CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<string, CatalogPartInfo>(StringComparer.OrdinalIgnoreCase);
            var partsToCheck = new List<string>();

            _logger.LogInfo($"Checking existence of {partNumbers.Count} parts in catalogs");

            // Check cache first for all parts
            foreach (var partNumber in partNumbers)
            {
                var normalizedPart = NormalizePartNumber(partNumber);
                if (_catalogPartInfoCache.TryGetValue(normalizedPart, out var cachedInfo))
                {
                    _logger.LogInfo($"Cache hit for part {partNumber}");
                    results[normalizedPart] = cachedInfo;
                }
                else
                {
                    partsToCheck.Add(normalizedPart);
                }
            }

            if (!partsToCheck.Any())
            {
                _logger.LogInfo("All parts found in cache");
                return results;
            }

            _logger.LogInfo($"Need to check {partsToCheck.Count} parts in catalogs");

            // Get all catalogs in one call - use the RateLimitedService for caching benefit
            var catalogs = await _rateLimitedOpenBomService.ListCatalogsAsync();

            if (catalogs == null || !catalogs.Any())
            {
                _logger.LogWarning("No catalogs available to search");
                return results;
            }

            _logger.LogInfo($"Found {catalogs.Count} catalogs to search");

            // Create a processing function for use with ProcessWithThrottling
            async Task<(string PartNumber, CatalogPartInfo Info)> CheckPartInCatalogsAsync(string partNumber, CancellationToken token)
            {
                foreach (var catalog in catalogs)
                {
                    if (token.IsCancellationRequested)
                        break;

                    try
                    {
                        // Use the rate-limited service which has built-in retry and caching
                        var node = await _rateLimitedOpenBomService.GetCatalogItemWithRetryAsync(catalog.Id, partNumber);

                        if (node != null)
                        {
                            var info = new CatalogPartInfo
                            {
                                CatalogId = catalog.Id,
                                CatalogName = catalog.Name,
                                Node = node
                            };

                            return (partNumber, info);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Error checking part {partNumber} in catalog {catalog.Name}: {ex.Message}");
                    }
                }

                return (partNumber, null);
            }

            // Check parts in parallel with throttling
            var processingResults = await ProcessWithThrottling(
                partsToCheck,
                CheckPartInCatalogsAsync,
                1, // Concurrency of 3 - adjusted to be conservative
                cancellationToken
            );

            // Process results
            foreach (var result in processingResults)
            {
                var normalizedPart = NormalizePartNumber(result.PartNumber);

                if (result.Info != null)
                {
                    results[normalizedPart] = result.Info;
                    _catalogPartInfoCache[normalizedPart] = result.Info;
                    _partExistsCache[normalizedPart] = true;
                    _logger.LogInfo($"Found part {normalizedPart} in catalog {result.Info.CatalogName}");
                }
                else
                {
                    _partExistsCache[normalizedPart] = false;
                    _logger.LogInfo($"Part {normalizedPart} not found in any catalog");
                }
            }

            return results;
        }

        private async Task<List<TResult>> ProcessWithThrottling<TItem, TResult>(
            IEnumerable<TItem> items,
            Func<TItem, CancellationToken, Task<TResult>> processingFunc,
            int maxConcurrency = 3,
            CancellationToken cancellationToken = default)
        {
            var results = new ConcurrentBag<TResult>();
            var semaphore = new SemaphoreSlim(maxConcurrency);
            var tasks = new List<Task>();
            int processed = 0;
            int total = items.Count();

            _logger.LogInfo($"Starting parallel processing of {total} items with concurrency {maxConcurrency}");

            foreach (var item in items)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInfo("Processing cancelled by request");
                    break;
                }

                await semaphore.WaitAsync(cancellationToken);

                tasks.Add(Task.Run(async () => {
                    try
                    {
                        var result = await processingFunc(item, cancellationToken);
                        results.Add(result);

                        int count = Interlocked.Increment(ref processed);
                        if (count % 10 == 0 || count == total)
                        {
                            _logger.LogInfo($"Processed {count}/{total} items");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error processing item: {ex.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);
            _logger.LogInfo($"Completed processing {processed}/{total} items");
            return results.ToList();
        }

        // Create a new method specifically for adding a child BOM with the correct part number
        private async Task AddBomAsChildToParent(string parentBomId, string childBomId, string childPartNumber, string childBomName)
        {
            try
            {
                // Log what we're doing with detailed parameters
                _logger.LogInfo($"Adding BOM {childBomId} as child to parent {parentBomId} with part number {childPartNumber}");

                // Create a more detailed part request with emphasis on the Assembly type
                // Use the supplied part number instead of the BOM ID
                var partRequest = new OpenBomPartRequest
                {
                    PartNumber = childPartNumber, // IMPORTANT: Use the original part number here, not the BOM ID
                    Properties = new Dictionary<string, string>
            {
                { "Part Number", childPartNumber }, // Use part number here too
            }
                };

                // Create a single-item list and use the rate-limited service
                var parts = new List<OpenBomPartRequest> { partRequest };
                var result = await _rateLimitedOpenBomService.UploadPartsAsync(parentBomId, parts);

                if (!result)
                {
                    throw new Exception($"Failed to add child BOM {childBomId} to parent {parentBomId}");
                }

                _logger.LogSuccess($"Successfully added BOM {childBomId} to parent {parentBomId} with part number {childPartNumber}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error adding BOM {childBomId} to parent {parentBomId}: {ex.Message}");
                throw;
            }
        }
        private string NormalizePartNumber(string partNumber)
        {
            if (string.IsNullOrWhiteSpace(partNumber))
                return string.Empty;

            // Remove extra spaces, dashes, and trim
            return partNumber.Trim().Replace(" ", "").Replace("-", "");
        }


        private void btnFindColumn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new CatalogColumnFinderDialog(_logger, _rateLimitedOpenBomService)
                {
                    Owner = this
                };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error showing Find Column dialog: {ex.Message}");
                MessageBox.Show($"Error showing dialog: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }




        private async Task<bool> CheckPartExistsInCatalogWithCacheAsync(string partNumber)
        {
            var normalizedPartNumber = NormalizePartNumber(partNumber);

            // Check cache first
            if (_partExistsCache.TryGetValue(normalizedPartNumber, out bool exists))
            {
                _logger.LogInfo($"Using cached result for part {partNumber}: exists={exists}");
                return exists;
            }

            try
            {
                // Get the cached part info if available
                if (_catalogPartInfoCache.TryGetValue(normalizedPartNumber, out var cachedInfo))
                {
                    _partExistsCache[normalizedPartNumber] = true;
                    _logger.LogInfo($"Using cached catalog info for part {partNumber}");
                    return true;
                }

                // Get catalogs
                var catalogs = await GetCatalogsWithCacheAsync();
                if (catalogs == null || !catalogs.Any())
                {
                    _logger.LogWarning("No catalogs available to search");
                    return false;
                }

                // Prioritize catalogs based on part number
                var priorityCatalogName = GetLikelyPartCatalog(normalizedPartNumber);
                var priorityCatalog = catalogs.FirstOrDefault(c => c.Name.Equals(priorityCatalogName, StringComparison.OrdinalIgnoreCase));

                // Reorder catalogs to check most likely ones first
                var orderedCatalogs = new List<OpenBomListItem>();
                if (priorityCatalog != null)
                {
                    orderedCatalogs.Add(priorityCatalog);
                }

                // Add any catalogs that might match by part number prefix
                foreach (var catalog in catalogs.Where(c => !c.Name.Equals(priorityCatalogName, StringComparison.OrdinalIgnoreCase)))
                {
                    if (IsPotentialMatch(normalizedPartNumber, catalog.Name))
                    {
                        orderedCatalogs.Add(catalog);
                    }
                }

                // Add remaining catalogs
                foreach (var catalog in catalogs.Where(c =>
                    !c.Name.Equals(priorityCatalogName, StringComparison.OrdinalIgnoreCase) &&
                    !orderedCatalogs.Contains(c)))
                {
                    orderedCatalogs.Add(catalog);
                }

                // First check the catalogs sequentially for the most likely ones
                // This helps avoid hitting rate limits for the more promising searches
                for (int i = 0; i < Math.Min(2, orderedCatalogs.Count); i++)
                {
                    var catalog = orderedCatalogs[i];
                    if (_cancellationTokenSource?.Token.IsCancellationRequested == true)
                        break;

                    await _apiSemaphore.WaitAsync();
                    try
                    {
                        var node = await _rateLimitedOpenBomService.GetCatalogItemWithRetryAsync(
                            catalog.Id, normalizedPartNumber);

                        if (node != null)
                        {
                            _partExistsCache[normalizedPartNumber] = true;
                            _catalogPartInfoCache[normalizedPartNumber] = new CatalogPartInfo
                            {
                                CatalogId = catalog.Id,
                                CatalogName = catalog.Name,
                                Node = node
                            };
                            _logger.LogInfo($"Found part {partNumber} in catalog {catalog.Name}");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Error checking catalog {catalog.Name}: {ex.Message}");
                    }
                    finally
                    {
                        _apiSemaphore.Release();
                    }
                }

                // Then check the remaining catalogs in parallel
                if (orderedCatalogs.Count > 2)
                {
                    var remainingCatalogs = orderedCatalogs.Skip(2).ToList();

                    // Create a list of tasks to check catalogs concurrently
                    var tasks = new List<Task<(bool Found, string CatalogId, string CatalogName, BomTreeNode Node)>>();

                    foreach (var catalog in remainingCatalogs)
                    {
                        tasks.Add(CheckCatalogForPartAsync(catalog, normalizedPartNumber));
                    }

                    // Wait for first successful result or all to complete
                    while (tasks.Count > 0 && !_cancellationTokenSource?.Token.IsCancellationRequested == true)
                    {
                        var completedTask = await Task.WhenAny(tasks);
                        tasks.Remove(completedTask);

                        var result = await completedTask;
                        if (result.Found)
                        {
                            _partExistsCache[normalizedPartNumber] = true;
                            _catalogPartInfoCache[normalizedPartNumber] = new CatalogPartInfo
                            {
                                CatalogId = result.CatalogId,
                                CatalogName = result.CatalogName,
                                Node = result.Node
                            };
                            _logger.LogInfo($"Found part {partNumber} in catalog {result.CatalogName}");
                            return true;
                        }
                    }
                }

                // Not found in any catalog
                _partExistsCache[normalizedPartNumber] = false;
                _logger.LogInfo($"Part {partNumber} not found in any catalog");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error checking part {partNumber} in catalogs: {ex.Message}");
                return false;
            }
        }

       
        private async Task<bool> BomExistsWithCacheAsync(string partNumber)
        {
            if (string.IsNullOrWhiteSpace(partNumber))
                return false;

            // Check cache first
            if (_bomExistsCache.TryGetValue(partNumber, out bool exists))
                return exists;

            // Not in cache, check from service
            bool bomExists = await CheckBomExistsAsync(partNumber);

            // Cache the result
            _bomExistsCache[partNumber] = bomExists;

            return bomExists;
        }

        private async Task ValidateAndPrepareForBomCreation(string partNumber)
        {
            if (string.IsNullOrWhiteSpace(partNumber))
                throw new ArgumentException("Part number is required");

            _logger.LogInfo($"Validating BOM part number: {partNumber}");
            bool bomExists = await BomExistsWithCacheAsync(partNumber);
            if (bomExists)
            {
                _logger.LogWarning($"BOM with part number '{partNumber}' already exists");
                throw new Exception($"A BOM with part number '{partNumber}' already exists. Please use a different part number.");
            }
            _logger.LogInfo($"Part number {partNumber} validation passed");
        }

        private async Task<bool> CheckBomExistsAsync(string partNumber)
        {
            try
            {
                _logger.LogInfo($"Checking if BOM with part number {partNumber} already exists");

                // Get all BOMs
                var allBoms = await _openBomService.ListBomsAsync();

                // Check if any BOM has the same part number (case-insensitive)
                bool exists = allBoms.Any(b =>
                    !string.IsNullOrEmpty(b.PartNumber) &&
                    b.PartNumber.Equals(partNumber, StringComparison.OrdinalIgnoreCase));

                _logger.LogInfo($"BOM with part number {partNumber} exists: {exists}");
                return exists;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error checking if BOM exists: {ex.Message}");
                return false; // Assume it doesn't exist in case of error
            }
        }


        // Modified version of the ProcessAndUploadPartsImproved method in OpenBomUploadDialog.xaml.cs

        private async Task ProcessAndUploadPartsImproved(string bomId, OpenBomListItem defaultCatalog = null, bool skipCatalogProcessing = false)
        {
            try
            {
                // Initialize tracking variables
                int processedCount = 0, successCount = 0, failureCount = 0;

                _logger.LogInfo($"Starting improved upload process for BOM {bomId} with {_finalPartsToUpload.Count} parts");

                // STEP 1: Check catalog assignments unless explicitly skipped
                if (!skipCatalogProcessing)
                {
                    _logger.LogInfo("Checking catalog assignments for parts");
                    LoadingOverlay.UpdateStatus("Checking parts in catalogs...");

                    // Get all part numbers to check
                    var partNumbers = _finalPartsToUpload
                        .Where(p => !string.IsNullOrWhiteSpace(p.OrderingCode))
                        .Select(p => p.OrderingCode)
                        .ToList();

                    // Use the improved method to check parts in catalogs - identifies which parts already exist in catalogs
                    var catalogPartsInfo = await CheckPartsExistInCatalogsAsync(partNumbers, _cancellationTokenSource.Token);

                    // Find parts that don't exist in any catalog
                    var partsNotInCatalog = _finalPartsToUpload
                        .Where(p => !string.IsNullOrWhiteSpace(p.OrderingCode) &&
                                    !catalogPartsInfo.ContainsKey(NormalizePartNumber(p.OrderingCode)))
                        .ToList();

                    if (partsNotInCatalog.Count > 0)
                    {
                        _logger.LogInfo($"Found {partsNotInCatalog.Count} parts that don't exist in any catalog");
                        LoadingOverlay.UpdateStatus($"Found {partsNotInCatalog.Count} parts that need catalog assignment");
                        LoadingOverlay.Hide();

                        // Get available catalogs
                        var catalogs = await _rateLimitedOpenBomService.ListCatalogsAsync();

                        // STEP 2: If we have parts not in catalogs and a list of available catalogs, show the assignment dialog
                        if (partsNotInCatalog.Count > 0 && catalogs.Count > 0)
                        {
                            // Check if we're using a pre-selected catalog
                            if (defaultCatalog != null)
                            {
                                // Option 1: Auto-assign all parts to default catalog
                                _logger.LogInfo($"Auto-assigning all parts to pre-selected catalog: {defaultCatalog.Name}");

                                // Process each part with the pre-selected catalog
                                foreach (var part in partsNotInCatalog)
                                {
                                    await ProcessPartCatalog(part, defaultCatalog);
                                }
                            }
                            else
                            {
                                // Option 2: Show the catalog assignment dialog
                                _logger.LogInfo("Showing catalog assignment dialog");

                                var catalogAssignmentDialog = new BulkCatalogAssignmentDialog(
                                    _logger,
                                    _digiKeyService,
                                    _rateLimitedOpenBomService,
                                    partsNotInCatalog,
                                    catalogs)
                                {
                                    Owner = this
                                };

                                if (catalogAssignmentDialog.ShowDialog() == true)
                                {
                                    // Process the catalog assignments from the dialog
                                    LoadingOverlay.Show("Catalog Processing", "Adding parts to catalogs...");

                                    // Get the assignments from the dialog
                                    var catalogAssignments = catalogAssignmentDialog.PartsToAssign;
                                    int processedCatalogParts = 0;

                                    // Process each assignment
                                    foreach (var assignment in catalogAssignments)
                                    {
                                        if (_cancellationTokenSource.Token.IsCancellationRequested)
                                            break;

                                        processedCatalogParts++;
                                        LoadingOverlay.UpdateProgress(processedCatalogParts, catalogAssignments.Count, "Catalog Parts");

                                        if (assignment.SelectedCatalog != null)
                                        {
                                            await ProcessPartCatalog(assignment.OriginalPart, assignment.SelectedCatalog);
                                        }
                                    }
                                }
                                else
                                {
                                    // User cancelled the dialog - ask if they want to continue without catalog processing
                                    var result = MessageBox.Show(
                                        "Catalog assignment was cancelled. Continue with BOM upload without adding parts to catalogs?",
                                        "Continue Upload?",
                                        MessageBoxButton.YesNo,
                                        MessageBoxImage.Question);

                                    if (result == MessageBoxResult.No)
                                    {
                                        _logger.LogInfo("Upload cancelled by user after declining catalog assignment");
                                        throw new OperationCanceledException("Upload cancelled by user");
                                    }
                                }
                            }
                        }

                        // Show loading overlay again for the BOM upload phase
                        LoadingOverlay.Show("BOM Upload", "Uploading parts to BOM...");
                    }
                    else
                    {
                        _logger.LogInfo("All parts already exist in catalogs");
                    }
                }
                else
                {
                    _logger.LogInfo("Catalog processing skipped by user preference");
                }

                // Pre-selected catalog processing (for parts that might or might not already exist in catalog)
                if (defaultCatalog != null && !skipCatalogProcessing)
                {
                    try
                    {
                        string selectedCatalogId = defaultCatalog.Id;
                        _logger.LogInfo($"Processing with pre-selected catalog: {defaultCatalog.Name} (ID: {selectedCatalogId})");

                        // Get catalog details to use consistent catalog name
                        var catalogDetail = await _rateLimitedOpenBomService.GetCatalogAsync(selectedCatalogId);
                        string catalogName = catalogDetail?.Name ?? defaultCatalog.Name;
                        _logger.LogInfo($"Using catalog name: {catalogName}");

                        // Process each part individually for more reliable handling
                        int partIndex = 0;
                        foreach (var part in _finalPartsToUpload)
                        {
                            partIndex++;
                            if (_cancellationTokenSource.Token.IsCancellationRequested)
                            {
                                _logger.LogInfo("Catalog processing cancelled by user");
                                break;
                            }

                            LoadingOverlay.UpdateStatus(
                                $"Processing catalog batch 1 of 1",
                                $"Parts {partIndex} of {_finalPartsToUpload.Count}"
                            );
                            LoadingOverlay.UpdateProgress(partIndex, _finalPartsToUpload.Count, "Catalog Processing");

                            // Wrap each part processing in its own try-catch to continue even if one fails
                            try
                            {
                                // Ensure part exists in catalog with timeout protection
                                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

                                // Check if we have DigiKey data for this part
                                DigiKeyProductResponse digiKeyData = null;

                                var catalogTask = _rateLimitedOpenBomService.EnsurePartInCatalogAsync(selectedCatalogId, part, digiKeyData);
                                if (await Task.WhenAny(catalogTask, Task.Delay(400, timeoutCts.Token)) == catalogTask)
                                {
                                    bool success = await catalogTask;
                                    _logger.LogInfo($"Catalog processing for part {part.OrderingCode} complete: {success}");
                                }
                                else
                                {
                                    _logger.LogWarning($"Timeout processing catalog for part {part.OrderingCode} - continuing with next part");
                                }
                            }
                            catch (Exception partEx)
                            {
                                _logger.LogError($"Error processing part {part.OrderingCode} for catalog: {partEx.Message}");
                                // Continue to next part despite error
                            }

                            // Short delay between parts
                            await Task.Delay(500, _cancellationTokenSource.Token);
                        }

                        // Reset counters for BOM upload phase
                        processedCount = 0;
                    }
                    catch (Exception catalogEx)
                    {
                        _logger.LogError($"Error during catalog processing phase: {catalogEx.Message}");
                        MessageBox.Show($"Warning: Encountered issues during catalog processing. Will continue with BOM upload phase.\n\nError: {catalogEx.Message}",
                            "Catalog Processing Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }

         
                // Create batches of parts for BOM upload
                const int bomBatchSize = 5; // Smaller batch size for better error handling
                var bomBatches = _finalPartsToUpload
                    .Select((part, index) => new { Part = part, Index = index })
                    .GroupBy(x => x.Index / bomBatchSize)
                    .Select(group => group.Select(x => x.Part).ToList())
                    .ToList();

                _logger.LogInfo($"Created {bomBatches.Count} batches of parts for BOM upload");

                // Process each BOM batch
                int batchNum = 0;
                foreach (var batch in bomBatches)
                {
                    batchNum++;
                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        _logger.LogInfo("Upload cancelled by user");
                        break;
                    }

                    LoadingOverlay.UpdateStatus(
                        $"Processing BOM batch {batchNum} of {bomBatches.Count}",
                        $"Parts {processedCount + 1}-{processedCount + batch.Count} of {_finalPartsToUpload.Count}"
                    );

                    try
                    {
                        // Process batch in parallel
                        var batchResults = await ProcessBatchWithRetry(bomId, batch);

                        // Update counters
                        processedCount += batch.Count;
                        successCount += batchResults.Item1;
                        failureCount += batchResults.Item2;

                        // Update UI
                        LoadingOverlay.UpdateProgress(processedCount, _finalPartsToUpload.Count, "Parts");
                        LoadingOverlay.UpdateStatus(
                            $"Processed {processedCount} of {_finalPartsToUpload.Count} parts",
                            $"Success: {successCount}, Failures: {failureCount}"
                        );

                        // Delay between batches to reduce server load
                        await Task.Delay(500, _cancellationTokenSource.Token);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error processing batch {batchNum}: {ex.Message}");
                        failureCount += batch.Count;

                        // Show error but continue with next batch
                        var continueResult = MessageBox.Show(
                            $"Error processing batch {batchNum}: {ex.Message}\n\nContinue with remaining batches?",
                            "Batch Processing Error",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning
                        );

                        if (continueResult == MessageBoxResult.No)
                        {
                            _logger.LogInfo("User chose to stop processing after batch error");
                            break;
                        }
                    }
                }

                // Final status update
                _logger.LogInfo($"Upload complete: {successCount} successful, {failureCount} failed");
                LoadingOverlay.UpdateStatus("Upload Complete",
                    $"Successfully processed {successCount} parts with {failureCount} failures");

                // Wait a moment to show the final status
                await Task.Delay(150);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in ProcessAndUploadPartsImproved: {ex.Message}");
                throw;
            }
        }

        // Helper method to process a part's catalog assignment
        private async Task ProcessPartCatalog(BomEntry part, OpenBomListItem catalog)
        {
            try
            {
                _logger.LogInfo($"Processing part {part.OrderingCode} for catalog {catalog.Name}");

                // Get DigiKey data for the part
                try
                {
                    var supplierData = await _digiKeyService.GetPriceAndAvailabilityAsync(part.OrderingCode);
                    if (supplierData != null && supplierData.IsAvailable)
                    {
                        _logger.LogInfo($"Retrieved DigiKey data for part {part.OrderingCode}");

                        // Set the DigiKey data on the part object
                        part.DigiKeyData = supplierData;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error getting DigiKey data for part {part.OrderingCode}: {ex.Message}");
                }

                // Use the rate-limited service to ensure the part exists in the catalog
                await _rateLimitedOpenBomService.EnsurePartInCatalogAsync(catalog.Id, part, null);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing part {part.OrderingCode} for catalog {catalog.Name}: {ex.Message}");
            }
        }


        private async Task<Tuple<int, int>> ProcessBatchWithRetry(string bomId, List<BomEntry> parts)
        {
            int success = 0;
            int failure = 0;
            int maxRetries = 3;

            // Create a local tracking list for failed parts
            var failedParts = new List<BomEntry>();

            // First attempt - process all parts with single concurrency
            foreach (var part in parts)
            {
                if (_cancellationTokenSource.Token.IsCancellationRequested)
                    break;

                try
                {
                    _logger.LogInfo($"Adding part {part.OrderingCode} to BOM {bomId}");
                    bool result = await _rateLimitedOpenBomService.AddPartToBomWithRetryAsync(bomId, part);

                    if (result)
                        success++;
                    else
                        failedParts.Add(part);

                    // Add delay between uploads (crucial)
                    await Task.Delay(1000, _cancellationTokenSource.Token);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error adding part {part.OrderingCode} to BOM: {ex.Message}");
                    failedParts.Add(part);
                }
            }

            // Retry mechanism - try up to maxRetries for failed parts
            for (int attempt = 1; attempt <= maxRetries && failedParts.Any(); attempt++)
            {
                _logger.LogWarning($"Retry attempt {attempt}/{maxRetries} for {failedParts.Count} failed parts");

                // Wait longer between retry attempts (exponential backoff)
                await Task.Delay(3000 * attempt, _cancellationTokenSource.Token);

                var stillFailedParts = new List<BomEntry>();

                foreach (var part in failedParts)
                {
                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                        break;

                    try
                    {
                        _logger.LogInfo($"Retry {attempt}: Adding part {part.OrderingCode} to BOM {bomId}");
                        bool result = await _rateLimitedOpenBomService.AddPartToBomWithRetryAsync(bomId, part);

                        if (result)
                            success++;
                        else
                            stillFailedParts.Add(part);

                        // Even longer delay between retries
                        await Task.Delay(2000, _cancellationTokenSource.Token);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error in retry {attempt} for part {part.OrderingCode}: {ex.Message}");
                        stillFailedParts.Add(part);
                    }
                }

                // Update our failed parts list for the next retry attempt
                failedParts = stillFailedParts;
            }

            // Count final failures
            failure = failedParts.Count;

            return new Tuple<int, int>(success, failure);
        }

        private void btnCompareBoms_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new ExistingBomComparisonDialog(_logger)
                {
                    Owner = this
                };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error showing BOM comparison dialog: {ex.Message}");
                MessageBox.Show($"Error showing dialog: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private class CatalogPartInfo
        {
            public string CatalogId { get; set; }
            public string CatalogName { get; set; }
            public BomTreeNode Node { get; set; }
        }

    }
}