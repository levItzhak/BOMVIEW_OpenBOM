using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using BOMVIEW.Interfaces;
using BOMVIEW.OpenBOM.Models;
using BOMVIEW.Services;
using BOMVIEW.Models;
using System.Collections.Generic;
using BOMVIEW.Controls;

namespace BOMVIEW
{
    public partial class CatalogBulkUpdateDialog : Window
    {
        private readonly ILogger _logger;
        private readonly DigiKeyService _digiKeyService;
        private readonly OpenBomService _openBomService;
        private readonly ObservableCollection<CatalogViewModel> _catalogs;
        private ICollectionView _catalogsView;
        private bool _isUpdatingChecks;
        private bool _isCancellationRequested;

        public class CatalogViewModel : INotifyPropertyChanged
        {
            private bool _isSelected;
            private bool _isVisible = true;

            public string Id { get; set; }
            public string Name { get; set; }
            public DateTime? LastUpdated { get; set; }
            public int ItemsCount { get; set; }

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }

            public bool IsVisible
            {
                get => _isVisible;
                set
                {
                    _isVisible = value;
                    OnPropertyChanged(nameof(IsVisible));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected virtual void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public CatalogBulkUpdateDialog(ILogger logger, DigiKeyService digiKeyService, OpenBomService openBomService)
        {
            InitializeComponent();
            _logger = logger;
            _digiKeyService = digiKeyService;
            _openBomService = openBomService;
            _catalogs = new ObservableCollection<CatalogViewModel>();

            CatalogsGrid.ItemsSource = _catalogs;
            _catalogsView = CollectionViewSource.GetDefaultView(_catalogs);
            _catalogsView.Filter = CatalogFilter;

            LoadingOverlay.CancelRequested += OnCancelRequested;
            LoadCatalogs();
        }

        private void OnCancelRequested(object sender, EventArgs e)
        {
            _isCancellationRequested = true;
        }


        private async Task LoadCatalogsWithThrottlingAsync()
        {
            try
            {
                LoadingOverlay.Show("Loading catalogs...");
                var catalogs = await _openBomService.ListCatalogsAsync();

                foreach (var catalog in catalogs)
                {
                    if (_isCancellationRequested) break;

                    try
                    {
                        // Add delay between requests to avoid rate limiting
                        await Task.Delay(2000); // 2 second delay between requests

                        var catalogDoc = await _openBomService.GetCatalogAsync(catalog.Id);
                        int itemCount = catalogDoc?.Cells?.Count ?? 0;

                        _catalogs.Add(new CatalogViewModel
                        {
                            Id = catalog.Id,
                            Name = catalog.Name,
                            LastUpdated = DateTime.Now.AddDays(-30),
                            ItemsCount = itemCount
                        });
                    }
                    catch (Exception ex) when (ex.Message.Contains("TooManyRequests"))
                    {
                        _logger.LogWarning($"Rate limit hit for catalog {catalog.Id}, waiting longer...");
                        await Task.Delay(5000); // Wait 5 seconds on rate limit

                        // Retry the request
                        var catalogDoc = await _openBomService.GetCatalogAsync(catalog.Id);
                        int itemCount = catalogDoc?.Cells?.Count ?? 0;

                        _catalogs.Add(new CatalogViewModel
                        {
                            Id = catalog.Id,
                            Name = catalog.Name,
                            LastUpdated = DateTime.Now.AddDays(-30),
                            ItemsCount = itemCount
                        });
                    }
                }
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

        private async void LoadCatalogs()
        {
            await LoadCatalogsWithThrottlingAsync();
        }

        private bool CatalogFilter(object item)
        {
            if (string.IsNullOrEmpty(SearchBox.Text))
                return true;

            if (item is CatalogViewModel catalog)
            {
                return catalog.Name.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _catalogsView.Refresh();
        }

        private void SelectAllCheckbox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingChecks) return;

            try
            {
                _isUpdatingChecks = true;
                bool isChecked = SelectAllCheckbox.IsChecked ?? false;

                foreach (var catalog in _catalogs.Where(c => c.IsVisible))
                {
                    catalog.IsSelected = isChecked;
                }
            }
            finally
            {
                _isUpdatingChecks = false;
            }
        }

        private Dictionary<string, string> GetPropertiesToUpdate(DigiKeyProductResponse digiKeyData, string partNumber, string catalogName)
        {
            var properties = new Dictionary<string, string>();
            if (digiKeyData?.Product == null) return properties;

            // Description
            if (!string.IsNullOrEmpty(digiKeyData.Product.Description?.ProductDescription))
            {
                properties["Description"] = digiKeyData.Product.Description.ProductDescription.Trim();
            }

            // Cost
            properties["Cost"] = digiKeyData.Product.UnitPrice.ToString("F2");

            // Lead Time
            if (!string.IsNullOrEmpty(digiKeyData.Product.ManufacturerLeadWeeks))
            {
                properties["Lead time"] = digiKeyData.Product.ManufacturerLeadWeeks.Trim();
            }

            // Manufacturer
            if (!string.IsNullOrEmpty(digiKeyData.Product.Manufacturer?.Name))
            {
                properties["Manufacturer"] = digiKeyData.Product.Manufacturer.Name.Trim();
            }

            // Manufacturer Part Number
            var mpn = digiKeyData.Product.Parameters?
     .FirstOrDefault(p => p.ParameterText?.Trim()
         .Equals("Manufacturer Part Number", StringComparison.OrdinalIgnoreCase) == true)
     ?.ValueText?.Trim();

            if (!string.IsNullOrEmpty(mpn))
            {
                properties["Manufacturer Part Number"] = mpn;
            }

            // Vendor
            properties["Vendor"] = "DIGI-KEY CORPORATION";
            properties["Catalog Indicator"] = catalogName; 

            // Product URL (Link) - Using both property names for compatibility
            if (!string.IsNullOrEmpty(digiKeyData.Product.ProductUrl))
            {
                var url = digiKeyData.Product.ProductUrl.Trim();
                properties["Link"] = url;
            }

            // Datasheet
            if (!string.IsNullOrEmpty(digiKeyData.Product.DatasheetUrl))
            {
                properties["Data Sheet"] = digiKeyData.Product.DatasheetUrl.Trim();
            }

            // Quantity Available
            properties["Quantity Available"] = digiKeyData.Product.QuantityAvailable.ToString();


            // Category (catalog type)
            if (digiKeyData.Product.Category != null && !string.IsNullOrEmpty(digiKeyData.Product.Category.Name))
            {
                properties["Catalog supplier"] = digiKeyData.Product.Category.Name;
            }

            // MOQ
            var moq = digiKeyData.Product.ProductVariations?.FirstOrDefault()?.MinimumOrderQuantity ?? 1;
            properties["Minimum Order Quantity"] = moq.ToString();

            return properties;
        }


        private async Task UpdateCatalogAsync(string catalogId)
        {
            try
            {
                var catalog = await _openBomService.GetCatalogAsync(catalogId);
                if (catalog?.Cells == null) return;

                int partNumberIndex = catalog.Columns.IndexOf("Part Number");
                if (partNumberIndex < 0) return;

                int totalProducts = catalog.Cells.Count;
                int processedProducts = 0;
                int updatedProducts = 0;
                int failedProducts = 0;

                foreach (var row in catalog.Cells)
                {
                    if (_isCancellationRequested)
                    {
                        _logger.LogInfo("Update process cancelled by user");
                        break;
                    }

                    processedProducts++;
                    UpdateProgress(processedProducts, totalProducts, updatedProducts, failedProducts);

                    try
                    {
                        string partNumber = row.ElementAtOrDefault(partNumberIndex)?.ToString();
                        if (string.IsNullOrEmpty(partNumber)) continue;

                        var supplierData = await _digiKeyService.GetPriceAndAvailabilityAsync(partNumber);
                        if (supplierData == null || !supplierData.IsAvailable) continue;

                        _logger.LogInfo($"Processing part {partNumber}");

                        // Convert SupplierData to DigiKeyProductResponse
                        var digiKeyData = ConvertToDigiKeyResponse(supplierData, partNumber);

                        // First handle image update separately
                        if (!string.IsNullOrEmpty(supplierData.ImageUrl))
                        {
                            _logger.LogInfo($"Starting image update for part {partNumber}");
                            try
                            {
                                var imageBytes = await _digiKeyService.GetImageBytesAsync(supplierData.ImageUrl);
                                if (imageBytes != null && imageBytes.Length > 0)
                                {
                                    await _openBomService.UploadCatalogImageAsync(
                                        catalogId,
                                        partNumber,
                                        imageBytes,
                                        "Thumbnail image"
                                    );
                                    _logger.LogInfo($"Successfully updated image for part {partNumber}");
                                }
                            }
                            catch (Exception imageEx)
                            {
                                _logger.LogError($"Error updating image for part {partNumber}: {imageEx.Message}");
                            }
                        }

                        // Then handle property updates separately
                        await Task.Delay(500); // Add a small delay between image and property updates

                        var properties = GetPropertiesToUpdate(digiKeyData, partNumber, catalog.Name);
                        _logger.LogInfo($"Updating properties for part {partNumber}");
                        foreach (var prop in properties)
                        {
                            _logger.LogInfo($"Property {prop.Key}: {prop.Value}");
                        }

                        await _openBomService.UpdateCatalogPartAsync(catalogId, new OpenBomPartRequest
                        {
                            PartNumber = partNumber,
                            Properties = properties
                        });

                        updatedProducts++;
                        _logger.LogSuccess($"Successfully updated part {partNumber}");
                    }
                    catch (Exception ex)
                    {
                        failedProducts++;
                        _logger.LogError($"Error updating product: {ex.Message}");
                    }

                    UpdateProgress(processedProducts, totalProducts, updatedProducts, failedProducts);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating catalog: {ex.Message}");
                throw;
            }
        }

        private DigiKeyProductResponse ConvertToDigiKeyResponse(SupplierData supplierData, string partNumber)
        {
            _logger.LogInfo($"Converting supplier data with URL: {supplierData.ProductUrl}");

            return new DigiKeyProductResponse
            {
                Product = new DigiKeyProduct
                {
                    IsAvailable = supplierData.IsAvailable,
                    ProductUrl = !string.IsNullOrEmpty(supplierData.ProductUrl) ? supplierData.ProductUrl.Trim() : null,
                    UnitPrice = supplierData.Price,
                    QuantityAvailable = supplierData.Availability,
                    Description = new DigiKeyDescription
                    {
                        ProductDescription = supplierData.Description ?? "No description available"
                    },
                    Manufacturer = new DigiKeyManufacturer
                    {
                        Name = supplierData.Manufacturer ?? "Unknown Manufacturer"
                    },
                    Parameters = supplierData.Parameters?.Select(p => new DigiKeyParameter
                    {
                        ParameterText = p.Name,
                        ValueText = p.Value
                    }).ToArray() ?? Array.Empty<DigiKeyParameter>(),
                    ProductVariations = new List<DigiKeyProductVariation>
            {
                new DigiKeyProductVariation
                {
                    MinimumOrderQuantity = supplierData.PriceBreaks.Any()
                        ? supplierData.PriceBreaks.Min(pb => pb.Quantity)
                        : 1,
                    StandardPricing = supplierData.PriceBreaks.Select(pb => new DigiKeyStandardPricing
                    {
                        BreakQuantity = pb.Quantity,
                        UnitPrice = pb.UnitPrice,
                        TotalPrice = pb.UnitPrice * pb.Quantity
                    }).ToList(),
                    QuantityAvailableforPackageType = supplierData.Availability
                }
            },
                    PhotoUrl = supplierData.ImageUrl,
                    DatasheetUrl = supplierData.DatasheetUrl,
                    ManufacturerLeadWeeks = supplierData.LeadTime?.ToString() ?? "Unknown",
                    Category = new DigiKeyCategory
                    {
                        Name = supplierData.Category // Use the Category string directly
                    }
                }
            };
        }



        private void UpdateProgress(int processed, int total, int updated, int failed)
        {
            Dispatcher.Invoke(() =>
            {
                double percentage = (double)processed / total * 100;
                ProgressBar.Value = percentage;
                StatusText.Text = $"Processing catalog items... ({processed}/{total})";
                UpdateCountText.Text = $"Processed: {processed}/{total} | Updated: {updated} | Failed: {failed}";
            });
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedCatalogs = _catalogs.Where(c => c.IsSelected).ToList();
            if (!selectedCatalogs.Any())
            {
                MessageBox.Show("Please select at least one catalog to update.",
                    "Selection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                StartButton.IsEnabled = false;
                _isCancellationRequested = false;
                ProgressBar.Value = 0;
                LoadingOverlay.Show("Starting update process...");

                foreach (var catalog in selectedCatalogs)
                {
                    if (_isCancellationRequested) break;
                    StatusText.Text = $"Processing catalog: {catalog.Name}";
                    await UpdateCatalogAsync(catalog.Id);
                }

                if (!_isCancellationRequested)
                {
                    MessageBox.Show("Update process completed successfully!",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during bulk update: {ex.Message}");
                MessageBox.Show($"Error during update process: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                StartButton.IsEnabled = true;
                LoadingOverlay.Hide();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}