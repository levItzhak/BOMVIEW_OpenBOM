using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using BOMVIEW.Services;
using BOMVIEW.Models;
using BOMVIEW.Interfaces;
using OfficeOpenXml;
using System.IO;
using System.Windows.Input;
using BOMVIEW.Controls;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using BOMVIEW.Exceptions;
using BOMVIEW;
using System.Windows.Data;
using System.Windows.Threading;
using System.Text;
using System.Windows.Media;



namespace BOMVIEW
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly IExcelService _excelService;
        private readonly ISupplierService _digiKeyService;
        private readonly ISupplierService _mouserService;
        private readonly ILogger _logger;
        private readonly TemplateManager _templateManager;
        public ObservableCollection<BomEntry> _bomEntries;
        private string _currentFilePath;
        private bool _isLoading;
        private TemplateManager.TemplateDefinition _selectedTemplate;
        private readonly MouserExporter _mouserExporter;
        private readonly DigiKeyExporter _digiKeyExporter;
        private CancellationTokenSource _cancellationTokenSource;
        private ObservableCollection<string> _duplicateGroups = new();
        private ICollectionView _bomEntriesView;
        private bool _isRefreshing = false;
        private readonly ISupplierService _farnellService;
        private readonly FarnellExporter _farnellExporter;
        private ObservableCollection<string> _duplicateOrderingCodes = new ObservableCollection<string>();
        private readonly ExternalSupplierService _externalSupplierService;
        private readonly ExternalSupplierExporter _externalSupplierExporter;
        private readonly ISupplierService _israelService;
        private readonly IsraelExporter _israelExporter;
        private readonly DispatcherTimer _exchangeRateTimer;
        private CurrencyExchangeService _currencyService;
        public ObservableCollection<string> DuplicateOrderingCodes
        {
            get => _duplicateOrderingCodes;
            set
            {
                _duplicateOrderingCodes = value;
                OnPropertyChanged(nameof(DuplicateOrderingCodes));
            }
        }

        private ObservableCollection<string> _missingPartOrderingCodes = new ObservableCollection<string>();
        public ObservableCollection<string> MissingPartOrderingCodes
        {
            get => _missingPartOrderingCodes;
            set
            {
                _missingPartOrderingCodes = value;
                OnPropertyChanged(nameof(MissingPartOrderingCodes));
            }
        }

        public bool HasMissingParts => Totals?.BestSupplierMissingCount > 0;

        private bool _hasDuplicates => _duplicateGroups.Any();


        private BomTotals _totals;
        public BomTotals Totals
        {
            get => _totals;
            set
            {
                _totals = value;
                OnPropertyChanged(nameof(Totals));
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                if (value)
                    LoadingOverlay.Show();
                else
                    LoadingOverlay.Hide();
                OnPropertyChanged(nameof(IsLoading));
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            Totals = new BomTotals();
            // Initialize the collections
            DuplicateOrderingCodes = new ObservableCollection<string>();
            MissingPartOrderingCodes = new ObservableCollection<string>();
            _logger = new ConsoleLogger();
            var credentials = new ApiCredentials();

            _excelService = new ExcelService();
            _digiKeyService = new DigiKeyService(_logger, credentials);
            _mouserService = new MouserService(_logger, credentials);
            _farnellService = new FarnellService(_logger, credentials);
            _israelService = new IsraelService(_logger, credentials);
            _templateManager = new TemplateManager(_logger);

            _bomEntries = new ObservableCollection<BomEntry>();
            dgBomEntries.ItemsSource = _bomEntries;
            _bomEntriesView = CollectionViewSource.GetDefaultView(_bomEntries);
            _bomEntriesView.Filter = BomEntryFilter;

            // Initialize the currency service from the Israel service
            _currencyService = ((IsraelService)_israelService).CurrencyService;

            // Initialize exporters
            _digiKeyExporter = new DigiKeyExporter(_logger, (DigiKeyService)_digiKeyService, _currentFilePath);
            _mouserExporter = new MouserExporter(_logger, (MouserService)_mouserService, _currentFilePath);
            _farnellExporter = new FarnellExporter(_logger, (FarnellService)_farnellService, _currentFilePath);
            _israelExporter = new IsraelExporter(_logger, (IsraelService)_israelService, _currentFilePath);
            _externalSupplierService = new ExternalSupplierService(_logger);
            _externalSupplierExporter = new ExternalSupplierExporter(_logger, _externalSupplierService);
            // Load templates into the combo box
            LoadTemplates();
            LoadingOverlay.CancelRequested += LoadingOverlay_CancelRequested;
            
            // Initialize the exchange rate display
            InitializeExchangeRateDisplay();
            
            // Set up timer to update exchange rate periodically
            _exchangeRateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(30) // Update every 30 minutes
            };
            _exchangeRateTimer.Tick += ExchangeRateTimer_Tick;
            _exchangeRateTimer.Start();
        }
        
        // Add this method to handle the timer tick
        private async void ExchangeRateTimer_Tick(object sender, EventArgs e)
        {
            await UpdateExchangeRateDisplay();
        }
        
        // Add this method to initialize the exchange rate display
        private async void InitializeExchangeRateDisplay()
        {
            await UpdateExchangeRateDisplay();
        }
        
        // Add this method to update the exchange rate display
        private async Task UpdateExchangeRateDisplay()
        {
            try
            {
                await _currencyService.UpdateExchangeRateAsync();
                
                Application.Current.Dispatcher.Invoke(() =>
                {
                    txtExchangeRate.Text = _currencyService.UsdToIlsRate.ToString("F2");
                    
                    // Format the timestamp
                    string timeFormat = _currencyService.LastUpdate.Date == DateTime.Today
                        ? "Today " + _currencyService.LastUpdate.ToString("HH:mm")
                        : _currencyService.LastUpdate.ToString("dd/MM HH:mm");
                        
                    txtExchangeRateTimestamp.Text = $"({timeFormat})";
                });
                
                _logger.LogInfo($"Exchange rate display updated: {_currencyService.UsdToIlsRate:F2} ILS");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating exchange rate display: {ex.Message}");
                
                // Show default values in case of error
                Application.Current.Dispatcher.Invoke(() =>
                {
                    txtExchangeRate.Text = "3.60";
                    txtExchangeRateTimestamp.Text = "(default)";
                });
            }
        }

        public void RefreshAfterExternalSupplierChange(int bomEntryNum)
        {
            try
            {
                var bomEntry = GetBomEntryByNum(bomEntryNum);
                if (bomEntry == null) return;

                // If the part has an external supplier, flag it visually
                bomEntry.HasExternalSupplier = _externalSupplierService.HasExternalSupplier(bomEntryNum);

                // Get external supplier data if available
                if (bomEntry.HasExternalSupplier)
                {
                    var externalSupplier = _externalSupplierService.GetByBomEntryNum(bomEntryNum);
                    if (externalSupplier != null)
                    {
                        // Apply external supplier data to all supplier columns
                        ApplyExternalSupplierData(bomEntry, externalSupplier);
                    }
                }

                // Refresh the entry in the UI
                RefreshBomEntry(bomEntry);

                // Update totals and missing parts list
                UpdateTotalItems();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error refreshing after external supplier change: {ex.Message}");
            }
        }

        private void ApplyExternalSupplierData(BomEntry bomEntry, ExternalSupplierEntry externalSupplier)
        {
            // Create mock supplier data for all suppliers with the external supplier info
            var mockSupplierData = new SupplierData
            {
                IsAvailable = true,
                Availability = externalSupplier.Availability,
                Price = externalSupplier.UnitPrice,
                PartNumber = externalSupplier.OrderingCode,
                Supplier = $"External: {externalSupplier.SupplierName}"
            };

            // Set external supplier price info
            decimal unitPrice = externalSupplier.UnitPrice;
            decimal totalPrice = externalSupplier.TotalPrice;

            // Apply to DigiKey
            bomEntry.DigiKeyData = mockSupplierData;
            bomEntry.DigiKeyCurrentUnitPrice = unitPrice;
            bomEntry.DigiKeyUnitPrice = unitPrice;
            bomEntry.DigiKeyCurrentTotalPrice = totalPrice;
            bomEntry.DigiKeyOrderQuantity = bomEntry.QuantityTotal;

            // Apply to Mouser
            bomEntry.MouserData = mockSupplierData;
            bomEntry.MouserCurrentUnitPrice = unitPrice;
            bomEntry.MouserUnitPrice = unitPrice;
            bomEntry.MouserCurrentTotalPrice = totalPrice;
            bomEntry.MouserOrderQuantity = bomEntry.QuantityTotal;

            // Apply to Farnell
            bomEntry.FarnellData = mockSupplierData;
            bomEntry.FarnellCurrentUnitPrice = unitPrice;
            bomEntry.FarnellUnitPrice = unitPrice;
            bomEntry.FarnellCurrentTotalPrice = totalPrice;
            bomEntry.FarnellOrderQuantity = bomEntry.QuantityTotal;

            // Apply to Israel
            bomEntry.IsraelData = mockSupplierData;
            bomEntry.IsraelCurrentUnitPrice = unitPrice;
            bomEntry.IsraelUnitPrice = unitPrice;
            bomEntry.IsraelCurrentTotalPrice = totalPrice;
            bomEntry.IsraelOrderQuantity = bomEntry.QuantityTotal;

            // Set best supplier
            bomEntry.BestCurrentSupplier = $"External: {externalSupplier.SupplierName}";
            bomEntry.CurrentUnitPrice = unitPrice;
            bomEntry.CurrentTotalPrice = totalPrice;
        }

        private void LoadingOverlay_CancelRequested(object sender, EventArgs e)
        {
            _cancellationTokenSource?.Cancel();
        }

        private void LoadTemplates()
        {
            try
            {
                var templates = _templateManager.LoadTemplates();
                cmbTemplates.ItemsSource = templates;

                // Get the last template name
                var lastTemplateTracker = new LastTemplateTracker(_logger);
                string lastTemplateName = lastTemplateTracker.GetLastTemplateName();

                if (!string.IsNullOrEmpty(lastTemplateName))
                {
                    // Find the template with this name
                    var lastTemplate = templates.FirstOrDefault(t =>
                        t.Name.Equals(lastTemplateName, StringComparison.OrdinalIgnoreCase));

                    if (lastTemplate != null)
                    {
                        _logger.LogInfo($"Found last used template for Quick BOM: {lastTemplateName}");
                        cmbTemplates.SelectedItem = lastTemplate;
                        return;
                    }
                }

                // If no last template found or it doesn't exist, select the first one
                if (templates.Any())
                {
                    cmbTemplates.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading templates: {ex.Message})");
            }
        }


        private bool BomEntryFilter(object item)
        {
            if (string.IsNullOrEmpty(txtPartNumber.Text))
                return true;

            if (item is BomEntry entry)
            {
                return entry.OrderingCode?.IndexOf(txtPartNumber.Text, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return false;
        }
        private void btnQuickBom_Click(object sender, RoutedEventArgs e)
        {
            LoadTemplates(); // This will load templates and select the last used one
            quickBomPopup.IsOpen = true;
        }
        private void txtPartNumber_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isRefreshing)
            {
                try
                {
                    _isRefreshing = true;
                    _bomEntriesView?.Refresh();

                    if (dgBomEntries.SelectedItem != null)
                    {
                        dgBomEntries.ScrollIntoView(dgBomEntries.SelectedItem);
                    }
                }
                finally
                {
                    _isRefreshing = false;
                }
            }
        }

        // Update the btnSelectBomFile_Click method in MainWindow.xaml.cs

        private async void btnSelectBomFile_Click(object sender, RoutedEventArgs e)
        {
            _selectedTemplate = cmbTemplates.SelectedItem as TemplateManager.TemplateDefinition;
            if (_selectedTemplate == null)
            {
                MessageBox.Show("Please select a template first", "Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Save the selected template name
            var lastTemplateTracker = new LastTemplateTracker(_logger);
            lastTemplateTracker.SaveLastTemplateName(_selectedTemplate.Name);
            _logger.LogInfo($"Saved last template selection: {_selectedTemplate.Name}");

            var openFileDialog = new OpenFileDialog
            {
                Filter = "Excel Files|*.xlsx",
                Title = "Select BOM File"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    quickBomPopup.IsOpen = false;
                    _currentFilePath = openFileDialog.FileName;
                    txtSelectedFile.Text = "Loading BOM data...";
                    IsLoading = true;

                    var config = _templateManager.ConvertToMappingConfiguration(_selectedTemplate, _currentFilePath);
                    config.UseQuantityBuffer = QuickLoadBufferCheckbox.IsChecked ?? false;

                    // Improved decimal parsing with culture handling
                    string assemblyQtyText = txtAssemblyQty.Text.Replace(',', '.');
                    if (decimal.TryParse(assemblyQtyText, System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture, out decimal qty) && qty > 0)
                    {
                        config.AssemblyQuantity = qty;
                    }

                    _bomEntries.Clear();
                    var entries = await _excelService.ReadBomFileAsync(_currentFilePath, config);

                    foreach (var entry in entries)
                    {
                        _bomEntries.Add(entry);
                    }

                    UpdateTotalItems();
                    _logger.LogInfo($"Loaded {_bomEntries.Count} entries from BOM file");

                    await FetchSupplierDataForEntries();

                    txtSelectedFile.Text = System.IO.Path.GetFileName(_currentFilePath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading file: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    txtSelectedFile.Text = string.Empty;
                }
                finally
                {
                    IsLoading = false;
                }
            }
        }
        private async void btnSelectFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Excel Files|*.xlsx",
                Title = "Select BOM File"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    _currentFilePath = openFileDialog.FileName;
                    btnSelectFile.IsEnabled = false;
                    txtSelectedFile.Text = "Loading BOM data...";

                    var mappingDialog = new MappingConfigurationDialog(_currentFilePath)
                    {
                        Owner = this // Set owner to MainWindow
                    };
                    if (mappingDialog.ShowDialog() != true)
                    {
                        txtSelectedFile.Text = string.Empty;
                        return;
                    }

                    IsLoading = true;
                    _bomEntries.Clear();
                    var entries = await _excelService.ReadBomFileAsync(_currentFilePath, mappingDialog.Configuration);

                    foreach (var entry in entries)
                    {
                        _bomEntries.Add(entry);
                    }

                    UpdateTotalItems();
                    _logger.LogInfo($"Loaded {_bomEntries.Count} entries from BOM file");

                    await FetchSupplierDataForEntries();

                    txtSelectedFile.Text = System.IO.Path.GetFileName(_currentFilePath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    txtSelectedFile.Text = string.Empty;
                }
                finally
                {
                    btnSelectFile.IsEnabled = true;
                    IsLoading = false;
                }
            }
        }

        private void UpdateTotalItems()
        {
            if (_bomEntries != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateTotals();
                });
            }
        }

        public async Task UpdatePriceInformation(BomEntry entry)
        {
            try
            {
                // DigiKey optimization and pricing
                if (entry.DigiKeyData?.IsAvailable ?? false)
                {
                    // Initial minimum quantity based on base price
                    var dkBasePrice = entry.DigiKeyData.GetPriceForQuantity(entry.QuantityTotal);
                    var dkMinQty = QuantityCalculator.CalculateMinimumOrderQuantity(
                        dkBasePrice.currentPrice,
                        entry.QuantityTotal
                    );

                    // Get optimized quantity considering price breaks
                    var dkOptimized = QuantityCalculator.GetOptimizedQuantity(
                        dkBasePrice.currentPrice,
                        entry.QuantityTotal,
                        dkBasePrice.nextBreakPrice,
                        dkBasePrice.nextBreakQuantity
                    );

                    // Update DigiKey quantities and prices
                    entry.DigiKeyOrderQuantity = dkOptimized.optimizedQuantity;
                    var finalDkPricing = entry.DigiKeyData.GetPriceForQuantity(entry.DigiKeyOrderQuantity);

                    entry.DigiKeyCurrentUnitPrice = finalDkPricing.currentPrice;
                    // This should be the price for a single unit, not multiplied by QuantityForOne
                    entry.DigiKeyUnitPrice = finalDkPricing.currentPrice;
                    entry.DigiKeyCurrentTotalPrice = finalDkPricing.currentPrice * entry.DigiKeyOrderQuantity;

                    if (finalDkPricing.nextBreakQuantity > 0)
                    {
                        var nextBreakOptimized = QuantityCalculator.GetOptimizedQuantity(
                            finalDkPricing.nextBreakPrice,
                            finalDkPricing.nextBreakQuantity,
                            null,
                            null
                        );

                        entry.DigiKeyNextBreakQty = nextBreakOptimized.optimizedQuantity;
                        entry.DigiKeyNextBreakUnitPrice = finalDkPricing.nextBreakPrice;
                        entry.DigiKeyNextBreakTotalPrice = finalDkPricing.nextBreakPrice * entry.DigiKeyNextBreakQty;
                    }
                }
                else
                {
                    ResetDigiKeyPrices(entry);
                }

                // Mouser optimization and pricing
                if (entry.MouserData?.IsAvailable ?? false)
                {
                    // Initial minimum quantity based on base price
                    var msBasePrice = entry.MouserData.GetPriceForQuantity(entry.QuantityTotal);
                    var msMinQty = QuantityCalculator.CalculateMinimumOrderQuantity(
                        msBasePrice.currentPrice,
                        entry.QuantityTotal
                    );

                    // Get optimized quantity considering price breaks
                    var msOptimized = QuantityCalculator.GetOptimizedQuantity(
                        msBasePrice.currentPrice,
                        entry.QuantityTotal,
                        msBasePrice.nextBreakPrice,
                        msBasePrice.nextBreakQuantity
                    );

                    // Update Mouser quantities and prices
                    entry.MouserOrderQuantity = msOptimized.optimizedQuantity;
                    var finalMsPricing = entry.MouserData.GetPriceForQuantity(entry.MouserOrderQuantity);

                    entry.MouserCurrentUnitPrice = finalMsPricing.currentPrice;
                    // This should be the price for a single unit, not multiplied by QuantityForOne
                    entry.MouserUnitPrice = finalMsPricing.currentPrice;
                    entry.MouserCurrentTotalPrice = finalMsPricing.currentPrice * entry.MouserOrderQuantity;

                    if (finalMsPricing.nextBreakQuantity > 0)
                    {
                        var nextBreakOptimized = QuantityCalculator.GetOptimizedQuantity(
                            finalMsPricing.nextBreakPrice,
                            finalMsPricing.nextBreakQuantity,
                            null,
                            null
                        );

                        entry.MouserNextBreakQty = nextBreakOptimized.optimizedQuantity;
                        entry.MouserNextBreakUnitPrice = finalMsPricing.nextBreakPrice;
                        entry.MouserNextBreakTotalPrice = finalMsPricing.nextBreakPrice * entry.MouserNextBreakQty;
                    }
                }
                else
                {
                    ResetMouserPrices(entry);
                }

                if (entry.FarnellData?.IsAvailable ?? false)
                {
                    // Initial minimum quantity based on base price
                    var farnellBasePrice = entry.FarnellData.GetPriceForQuantity(entry.QuantityTotal);
                    var farnellMinQty = QuantityCalculator.CalculateMinimumOrderQuantity(
                        farnellBasePrice.currentPrice,
                        entry.QuantityTotal
                    );

                    // Get optimized quantity considering price breaks
                    var farnellOptimized = QuantityCalculator.GetOptimizedQuantity(
                        farnellBasePrice.currentPrice,
                        entry.QuantityTotal,
                        farnellBasePrice.nextBreakPrice,
                        farnellBasePrice.nextBreakQuantity
                    );

                    // Update Farnell quantities and prices
                    entry.FarnellOrderQuantity = farnellOptimized.optimizedQuantity;
                    var finalFarnellPricing = entry.FarnellData.GetPriceForQuantity(entry.FarnellOrderQuantity);

                    entry.FarnellCurrentUnitPrice = finalFarnellPricing.currentPrice;
                    // This should be the price for a single unit, not multiplied by QuantityForOne
                    entry.FarnellUnitPrice = finalFarnellPricing.currentPrice;
                    entry.FarnellCurrentTotalPrice = finalFarnellPricing.currentPrice * entry.FarnellOrderQuantity;

                    if (finalFarnellPricing.nextBreakQuantity > 0)
                    {
                        var nextBreakOptimized = QuantityCalculator.GetOptimizedQuantity(
                            finalFarnellPricing.nextBreakPrice,
                            finalFarnellPricing.nextBreakQuantity,
                            null,
                            null
                        );

                        entry.FarnellNextBreakQty = nextBreakOptimized.optimizedQuantity;
                        entry.FarnellNextBreakUnitPrice = finalFarnellPricing.nextBreakPrice;
                        entry.FarnellNextBreakTotalPrice = finalFarnellPricing.nextBreakPrice * entry.FarnellNextBreakQty;
                    }
                }
                else
                {
                    ResetFarnellPrices(entry);
                }

                // Israel optimization and pricing
                if (entry.IsraelData?.IsAvailable ?? false)
                {
                    // Initial minimum quantity based on base price
                    var israelBasePrice = entry.IsraelData.GetPriceForQuantity(entry.QuantityTotal);
                    var israelMinQty = QuantityCalculator.CalculateMinimumOrderQuantity(
                        israelBasePrice.currentPrice,
                        entry.QuantityTotal
                    );

                    // Get optimized quantity considering price breaks
                    var israelOptimized = QuantityCalculator.GetOptimizedQuantity(
                        israelBasePrice.currentPrice,
                        entry.QuantityTotal,
                        israelBasePrice.nextBreakPrice,
                        israelBasePrice.nextBreakQuantity
                    );

                    // Update Israel quantities and prices
                    entry.IsraelOrderQuantity = israelOptimized.optimizedQuantity;
                    var finalIsraelPricing = entry.IsraelData.GetPriceForQuantity(entry.IsraelOrderQuantity);

                    entry.IsraelCurrentUnitPrice = finalIsraelPricing.currentPrice;
                    // This should be the price for a single unit, not multiplied by QuantityForOne
                    entry.IsraelUnitPrice = finalIsraelPricing.currentPrice;
                    entry.IsraelCurrentTotalPrice = finalIsraelPricing.currentPrice * entry.IsraelOrderQuantity;

                    if (finalIsraelPricing.nextBreakQuantity > 0)
                    {
                        var nextBreakOptimized = QuantityCalculator.GetOptimizedQuantity(
                            finalIsraelPricing.nextBreakPrice,
                            finalIsraelPricing.nextBreakQuantity,
                            null,
                            null
                        );

                        entry.IsraelNextBreakQty = nextBreakOptimized.optimizedQuantity;
                        entry.IsraelNextBreakUnitPrice = finalIsraelPricing.nextBreakPrice;
                        entry.IsraelNextBreakTotalPrice = finalIsraelPricing.nextBreakPrice * entry.IsraelNextBreakQty;
                    }
                }
                else
                {
                    ResetIsraelPrices(entry);
                }

                // Determine best supplier based on optimized total prices
                DetermineBestSupplier(entry);
                UpdateTotals();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating price information for {entry.OrderingCode}: {ex.Message}");
            }
        }
        private void ResetDigiKeyPrices(BomEntry entry)
        {
            entry.DigiKeyOrderQuantity = entry.QuantityTotal;
            entry.DigiKeyCurrentUnitPrice = 0;
            entry.DigiKeyUnitPrice = 0;  // Reset the unit price properly
            entry.DigiKeyCurrentTotalPrice = 0;
            entry.DigiKeyNextBreakQty = 0;
            entry.DigiKeyNextBreakUnitPrice = 0;
            entry.DigiKeyNextBreakTotalPrice = 0;
        }

        private void ResetMouserPrices(BomEntry entry)
        {
            entry.MouserOrderQuantity = entry.QuantityTotal;
            entry.MouserCurrentUnitPrice = 0;
            entry.MouserUnitPrice = 0;  // Reset the unit price properly
            entry.MouserCurrentTotalPrice = 0;
            entry.MouserNextBreakQty = 0;
            entry.MouserNextBreakUnitPrice = 0;
            entry.MouserNextBreakTotalPrice = 0;
        }

        private void ResetFarnellPrices(BomEntry entry)
        {
            entry.FarnellOrderQuantity = entry.QuantityTotal;
            entry.FarnellCurrentUnitPrice = 0;
            entry.FarnellUnitPrice = 0;  // Reset the unit price properly
            entry.FarnellCurrentTotalPrice = 0;
            entry.FarnellNextBreakQty = 0;
            entry.FarnellNextBreakUnitPrice = 0;
            entry.FarnellNextBreakTotalPrice = 0;
        }

        private void ResetIsraelPrices(BomEntry entry)
        {
            entry.IsraelOrderQuantity = entry.QuantityTotal;
            entry.IsraelCurrentUnitPrice = 0;
            entry.IsraelUnitPrice = 0;  // Reset the unit price properly
            entry.IsraelCurrentTotalPrice = 0;
            entry.IsraelNextBreakQty = 0;
            entry.IsraelNextBreakUnitPrice = 0;
            entry.IsraelNextBreakTotalPrice = 0;
        }

        private void DetermineBestSupplier(BomEntry entry)
        {
            bool digiKeyAvailable = (entry.DigiKeyData?.IsAvailable ?? false) && entry.DigiKeyCurrentTotalPrice > 0;
            bool mouserAvailable = (entry.MouserData?.IsAvailable ?? false) && entry.MouserCurrentTotalPrice > 0;
            bool farnellAvailable = (entry.FarnellData?.IsAvailable ?? false) && entry.FarnellCurrentTotalPrice > 0;
            bool israelAvailable = (entry.IsraelData?.IsAvailable ?? false) && entry.IsraelCurrentTotalPrice > 0;

            // Create a list of available suppliers with their prices
            var availableSuppliers = new List<(string supplier, decimal totalPrice)>();

            if (digiKeyAvailable)
                availableSuppliers.Add(("DigiKey", entry.DigiKeyCurrentTotalPrice));

            if (mouserAvailable)
                availableSuppliers.Add(("Mouser", entry.MouserCurrentTotalPrice));

            if (farnellAvailable)
                availableSuppliers.Add(("Farnell", entry.FarnellCurrentTotalPrice));
            
            if (israelAvailable)
                availableSuppliers.Add(("Israel", entry.IsraelCurrentTotalPrice));

            if (availableSuppliers.Count > 0)
            {
                // Find the supplier with the lowest price
                var bestSupplier = availableSuppliers.OrderBy(s => s.totalPrice).First();

                entry.BestCurrentSupplier = bestSupplier.supplier;
                entry.CurrentUnitPrice = GetCurrentUnitPrice(entry, bestSupplier.supplier);
                entry.CurrentTotalPrice = bestSupplier.totalPrice;

                // Determine best next break supplier
                decimal digiKeyNextBreakPrice = digiKeyAvailable ? entry.DigiKeyNextBreakTotalPrice : decimal.MaxValue;
                decimal mouserNextBreakPrice = mouserAvailable ? entry.MouserNextBreakTotalPrice : decimal.MaxValue;
                decimal farnellNextBreakPrice = farnellAvailable ? entry.FarnellNextBreakTotalPrice : decimal.MaxValue;
                decimal israelNextBreakPrice = israelAvailable ? entry.IsraelNextBreakTotalPrice : decimal.MaxValue;

                if (digiKeyNextBreakPrice <= mouserNextBreakPrice && 
                    digiKeyNextBreakPrice <= farnellNextBreakPrice &&
                    digiKeyNextBreakPrice <= israelNextBreakPrice)
                    entry.BestNextBreakSupplier = "DigiKey";
                else if (mouserNextBreakPrice <= digiKeyNextBreakPrice && 
                         mouserNextBreakPrice <= farnellNextBreakPrice &&
                         mouserNextBreakPrice <= israelNextBreakPrice)
                    entry.BestNextBreakSupplier = "Mouser";
                else if (farnellNextBreakPrice <= digiKeyNextBreakPrice && 
                         farnellNextBreakPrice <= mouserNextBreakPrice &&
                         farnellNextBreakPrice <= israelNextBreakPrice)
                    entry.BestNextBreakSupplier = "Farnell";
                else
                    entry.BestNextBreakSupplier = "Israel";
            }
            else
            {
                entry.BestCurrentSupplier = "N/A";
                entry.BestNextBreakSupplier = "N/A";
                entry.CurrentUnitPrice = 0;
                entry.CurrentTotalPrice = 0;
            }
        }

        private decimal GetCurrentUnitPrice(BomEntry entry, string supplier)
        {
            return supplier switch
            {
                "DigiKey" => entry.DigiKeyCurrentUnitPrice,
                "Mouser" => entry.MouserCurrentUnitPrice,
                "Farnell" => entry.FarnellCurrentUnitPrice,
                "Israel" => entry.IsraelCurrentUnitPrice,
                _ => 0
            };
        }


        private async Task FetchSupplierDataForEntries()
        {
            try
            {
                if (!NetworkHelper.CheckInternetWithMessage())
                    return;

                _logger.LogInfo("Starting to fetch supplier data");
                int processedCount = 0;
                int totalCount = _bomEntries.Count(e => !string.IsNullOrWhiteSpace(e.OrderingCode));

                bool skipDigiKey = false;
                bool skipMouser = false;
                bool skipFarnell = false;
                bool skipIsrael = false;

                _cancellationTokenSource = new CancellationTokenSource();

                foreach (var entry in _bomEntries.ToList())
                {
                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        _logger.LogInfo("Data fetch cancelled by user");
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(entry.OrderingCode))
                    {
                        _logger.LogWarning($"Skipping entry {entry.Num}: Empty ordering code");
                        continue;
                    }

                    try
                    {
                        int currentDisplayCount = processedCount + 1;
                        LoadingOverlay.LoadingMessage = $"Processing part {currentDisplayCount} of {totalCount}...";
                        entry.IsLoading = true;
                        Application.Current.Dispatcher.Invoke(() => dgBomEntries.Items.Refresh());

                        var tasks = new List<Task<SupplierData>>();
                        Task<SupplierData> digiKeyTask = null;
                        Task<SupplierData> mouserTask = null;
                        Task<SupplierData> farnellTask = null;
                        Task<SupplierData> israelTask = null;

                        if (!skipDigiKey)
                        {
                            digiKeyTask = _digiKeyService.GetPriceAndAvailabilityAsync(entry.OrderingCode);
                            tasks.Add(digiKeyTask);
                        }

                        if (!skipMouser)
                        {
                            mouserTask = _mouserService.GetPriceAndAvailabilityAsync(entry.OrderingCode);
                            tasks.Add(mouserTask);
                        }

                        if (!skipFarnell)
                        {
                            farnellTask = _farnellService.GetPriceAndAvailabilityAsync(entry.OrderingCode);
                            tasks.Add(farnellTask);
                        }

                        if (!skipIsrael)
                        {
                            israelTask = _israelService.GetPriceAndAvailabilityAsync(entry.OrderingCode);
                            tasks.Add(israelTask);
                        }

                        try
                        {
                            await Task.WhenAll(tasks);

                            if (!skipDigiKey && digiKeyTask != null)
                                entry.DigiKeyData = await digiKeyTask;
                            if (!skipMouser && mouserTask != null)
                                entry.MouserData = await mouserTask;
                            if (!skipFarnell && farnellTask != null)
                                entry.FarnellData = await farnellTask;
                            if (!skipIsrael && israelTask != null)
                                entry.IsraelData = await israelTask;
                        }
                        catch (RateLimitException rex)
                        {
                            var supplier = rex.Supplier;
                            string supplierName = supplier.ToString();
                            string otherSuppliers = GetOtherSupplierString(supplier, skipDigiKey, skipMouser, skipFarnell, skipIsrael);

                            var message = $"Rate limit exceeded for {supplierName}. Do you want to continue with only {otherSuppliers}?";

                            var result = MessageBox.Show(
                                message,
                                "Rate Limit Exceeded",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning
                            );

                            if (result == MessageBoxResult.Yes)
                            {
                                if (supplier == SupplierType.DigiKey)
                                {
                                    skipDigiKey = true;
                                    _logger.LogWarning("Continuing without DigiKey due to rate limit");
                                }
                                else if (supplier == SupplierType.Mouser)
                                {
                                    skipMouser = true;
                                    _logger.LogWarning("Continuing without Mouser due to rate limit");
                                }
                                else if (supplier == SupplierType.Farnell)
                                {
                                    skipFarnell = true;
                                    _logger.LogWarning("Continuing without Farnell due to rate limit");
                                }
                                else if (supplier == SupplierType.Israel)
                                {
                                    skipIsrael = true;
                                    _logger.LogWarning("Continuing without Israel due to rate limit");
                                }
                            }
                            else
                            {
                                throw new OperationCanceledException("Operation cancelled due to rate limit");
                            }
                        }

                        await UpdatePriceInformation(entry);
                        DetectDuplicates();
                    }
                    catch (Exception ex)
                    {
                        if (ex is OperationCanceledException)
                            throw;

                        if (_cancellationTokenSource.Token.IsCancellationRequested)
                            break;

                        _logger.LogError($"Error processing {entry.OrderingCode}: {ex.Message}");
                        entry.DigiKeyData = new SupplierData { Price = 0, Availability = 0, IsAvailable = false };
                        entry.MouserData = new SupplierData { Price = 0, Availability = 0, IsAvailable = false };
                        entry.FarnellData = new SupplierData { Price = 0, Availability = 0, IsAvailable = false };
                        entry.IsraelData = new SupplierData { Price = 0, Availability = 0, IsAvailable = false };
                    }
                    finally
                    {
                        processedCount++;
                        entry.IsLoading = false;
                        Application.Current.Dispatcher.Invoke(() => dgBomEntries.Items.Refresh());
                    }
                    DetectDuplicates();

                    await Task.Delay(500, _cancellationTokenSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("Operation cancelled");
                MessageBox.Show(
                    "Operation cancelled.",
                    "Cancelled",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                _logger.LogError($"Fatal error during supplier data fetch: {ex.Message}");
                MessageBox.Show(
                    "An error occurred while fetching supplier data. Check the log for details.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                IsLoading = false;
            }
        }
        private void UpdateTotals()
        {
            if (_bomEntries == null || !_bomEntries.Any())
            {
                Totals = new BomTotals();
                return;
            }

            var totals = new BomTotals
            {
                DigiKeyUnitTotal = _bomEntries.Sum(e => e.DigiKeyUnitPrice),
                DigiKeyTotalPrice = _bomEntries.Sum(e => e.DigiKeyCurrentTotalPrice),
                MouserUnitTotal = _bomEntries.Sum(e => e.MouserUnitPrice),
                MouserTotalPrice = _bomEntries.Sum(e => e.MouserCurrentTotalPrice),
                FarnellUnitTotal = _bomEntries.Sum(e => e.FarnellUnitPrice),
                FarnellTotalPrice = _bomEntries.Sum(e => e.FarnellCurrentTotalPrice),
                IsraelUnitTotal = _bomEntries.Sum(e => e.IsraelUnitPrice),
                IsraelTotalPrice = _bomEntries.Sum(e => e.IsraelCurrentTotalPrice),
                DigiKeyMissingCount = _bomEntries.Count(e => !(e.DigiKeyData?.IsAvailable ?? false)),
                MouserMissingCount = _bomEntries.Count(e => !(e.MouserData?.IsAvailable ?? false)),
                FarnellMissingCount = _bomEntries.Count(e => !(e.FarnellData?.IsAvailable ?? false)),
                IsraelMissingCount = _bomEntries.Count(e => !(e.IsraelData?.IsAvailable ?? false))
            };

            // Calculate best supplier totals and missing count
            var bestSupplierTotalPrice = 0m;
            var bestSupplierNextBreakTotal = 0m;
            var bestSupplierMissingCount = 0;

            foreach (var entry in _bomEntries)
            {
                var digiKeyAvailable = entry.DigiKeyData?.IsAvailable ?? false;
                var mouserAvailable = entry.MouserData?.IsAvailable ?? false;
                var farnellAvailable = entry.FarnellData?.IsAvailable ?? false;
                var israelAvailable = entry.IsraelData?.IsAvailable ?? false;

                // Count as missing if best supplier can't supply the part
                if (!digiKeyAvailable && !mouserAvailable && !farnellAvailable && !israelAvailable)
                {
                    bestSupplierMissingCount++;
                    continue;
                }

                switch (entry.BestCurrentSupplier)
                {
                    case "DigiKey":
                        bestSupplierTotalPrice += entry.DigiKeyCurrentTotalPrice;
                        break;
                    case "Mouser":
                        bestSupplierTotalPrice += entry.MouserCurrentTotalPrice;
                        break;
                    case "Farnell":
                        bestSupplierTotalPrice += entry.FarnellCurrentTotalPrice;
                        break;
                    case "Israel":
                        bestSupplierTotalPrice += entry.IsraelCurrentTotalPrice;
                        break;
                }

                switch (entry.BestNextBreakSupplier)
                {
                    case "DigiKey":
                        bestSupplierNextBreakTotal += entry.DigiKeyNextBreakTotalPrice;
                        break;
                    case "Mouser":
                        bestSupplierNextBreakTotal += entry.MouserNextBreakTotalPrice;
                        break;
                    case "Farnell":
                        bestSupplierNextBreakTotal += entry.FarnellNextBreakTotalPrice;
                        break;
                    case "Israel":
                        bestSupplierNextBreakTotal += entry.IsraelNextBreakTotalPrice;
                        break;
                }
            }

            totals.BestSupplierTotal = bestSupplierTotalPrice;
            totals.BestSupplierNextBreakTotal = bestSupplierNextBreakTotal;
            totals.BestSupplierMissingCount = bestSupplierMissingCount;

            Totals = totals;
            btnDuplicates.Visibility = Totals.DuplicateCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            btnMissingProducts.Visibility =
                (Totals.DigiKeyMissingCount > 0 || Totals.MouserMissingCount > 0 || Totals.FarnellMissingCount > 0 || Totals.IsraelMissingCount > 0)
                ? Visibility.Visible : Visibility.Collapsed;

            // Add this line at the end
            UpdateMissingPartsList();
        }
        private void DuplicatesSummaryPanel_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            btnDuplicates_Click(sender, new RoutedEventArgs());
        }

        private void MissingPartsSummaryPanel_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ShowMissingProducts_Click(sender, new RoutedEventArgs());
        }
        private void DetectDuplicates()
        {
            _duplicateGroups.Clear();
            DuplicateOrderingCodes.Clear();

            var groups = _bomEntries
                .GroupBy(e => e.OrderingCode?.Trim().ToLower())
                .Where(g => !string.IsNullOrEmpty(g.Key) && g.Count() > 1);

            foreach (var group in groups)
            {
                string groupId = Guid.NewGuid().ToString();
                _duplicateGroups.Add(groupId);

                // Add to the observable collection for display in the summary
                if (!DuplicateOrderingCodes.Contains(group.First().OrderingCode))
                {
                    DuplicateOrderingCodes.Add(group.First().OrderingCode);
                }

                foreach (var entry in group)
                {
                    entry.IsDuplicate = true;
                    entry.DuplicateGroupId = groupId;
                }
            }

            // Update UI elements based on duplicate status
            UpdateDuplicateIndicators();
        }


        private void UpdateMissingPartsList()
        {
            MissingPartOrderingCodes.Clear();

            // Get items that are missing from all suppliers
            var missingParts = _bomEntries.Where(e =>
                !(e.DigiKeyData?.IsAvailable ?? false) &&
                !(e.MouserData?.IsAvailable ?? false) &&
                !(e.FarnellData?.IsAvailable ?? false) &&
                !(e.IsraelData?.IsAvailable ?? false)).ToList();

            foreach (var part in missingParts)
            {
                if (!string.IsNullOrWhiteSpace(part.OrderingCode) &&
                    !MissingPartOrderingCodes.Contains(part.OrderingCode))
                {
                    MissingPartOrderingCodes.Add(part.OrderingCode);
                }
            }

            OnPropertyChanged(nameof(HasMissingParts));
        }
        private void UpdateDuplicateIndicators()
        {
            if (_hasDuplicates)
            {
                Totals.DuplicateCount = _duplicateGroups.Count;
                btnDuplicates.Visibility = Visibility.Visible;
            }
            else
            {
                Totals.DuplicateCount = 0;
                btnDuplicates.Visibility = Visibility.Collapsed;
            }
            dgBomEntries.Items.Refresh();
        }






        private void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            try
            {
                if (e.Row.DataContext is BomEntry bomEntry)
                {
                    e.Row.Background = bomEntry.IsLoading ?
                        System.Windows.Media.Brushes.LightGray :
                        System.Windows.Media.Brushes.Transparent;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Row loading error: {ex.Message}");
            }
        }

        private void dgBomEntries_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                e.Handled = true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Selection error: {ex.Message}");
            }
        }

        private async void DgBomEntries_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            try
            {
                var bomEntry = e.Row.Item as BomEntry;
                if (bomEntry == null) return;

                var editedElement = e.EditingElement as TextBox;
                if (editedElement == null) return;

                var column = e.Column as DataGridColumn;
                if (column == null || string.IsNullOrEmpty(column.Header?.ToString())) return;

                var headerText = column.Header.ToString();
                var updatedValue = editedElement.Text;

                // Allow the current edit operation to complete
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        if (_isRefreshing) return;

                        _isRefreshing = true;

                        switch (headerText)
                        {
                            case "Ordering Code":
                                if (!string.IsNullOrWhiteSpace(updatedValue))
                                {
                                    var dialog = new APIRequestDialog { Owner = this };
                                    var dialogResult = dialog.ShowDialog();

                                    if (dialogResult != true)
                                        return;

                                    bomEntry.OrderingCode = updatedValue;
                                    bomEntry.IsUserEntered = true;

                                    if (dialog.ShouldFetchData)
                                    {
                                        if (!NetworkHelper.CheckInternetWithMessage())
                                            return;

                                        try
                                        {
                                            IsLoading = true;
                                            LoadingOverlay.Show($"Updating data for {updatedValue}...");

                                            bomEntry.DigiKeyData = null;
                                            bomEntry.MouserData = null;
                                            bomEntry.FarnellData = null;
                                            bomEntry.IsraelData = null;

                                            var digiKeyTask = _digiKeyService.GetPriceAndAvailabilityAsync(updatedValue);
                                            var mouserTask = _mouserService.GetPriceAndAvailabilityAsync(updatedValue);
                                            var farnellTask = _farnellService.GetPriceAndAvailabilityAsync(updatedValue);
                                            var israelTask = _israelService.GetPriceAndAvailabilityAsync(updatedValue);

                                            await Task.WhenAll(digiKeyTask, mouserTask, farnellTask, israelTask);

                                            bomEntry.DigiKeyData = await digiKeyTask;
                                            bomEntry.MouserData = await mouserTask;
                                            bomEntry.FarnellData = await farnellTask;
                                            bomEntry.IsraelData = await israelTask;

                                            await UpdatePriceInformation(bomEntry);

                                            _logger.LogSuccess($"Successfully updated data for {updatedValue}");
                                        }
                                        finally
                                        {
                                            IsLoading = false;
                                        }
                                    }
                                }
                                break;

                            case "QTY (1)":
                                if (decimal.TryParse(updatedValue, out decimal qty))
                                {
                                    bomEntry.IsUserEntered = true;
                                    bomEntry.QuantityForOne = (int)qty;
                                    bomEntry.QuantityTotal = (int)Math.Ceiling(qty * GetAssemblyQuantity());
                                    await UpdatePriceInformation(bomEntry);
                                }
                                break;

                            case "QTY (TOTAL)":
                                if (int.TryParse(updatedValue, out int totalQty))
                                {
                                    bomEntry.IsUserEntered = true;
                                    bomEntry.QuantityTotal = totalQty;
                                    await UpdatePriceInformation(bomEntry);
                                }
                                break;

                            default:
                                if (updatedValue != null)
                                {
                                    bomEntry.IsUserEntered = true;
                                }
                                break;
                        }

                        // Use BeginInvoke for UI updates to ensure they happen on the UI thread after edit completion
                        await Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                DetectDuplicates();
                                _bomEntriesView?.Refresh();
                            }
                            finally
                            {
                                _isRefreshing = false;
                            }
                        }, DispatcherPriority.Background);

                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Cell edit error: {ex.Message}");
                        MessageBox.Show($"Error updating cell: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        _isRefreshing = false;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Cell edit error: {ex.Message}");
                MessageBox.Show($"Error updating cell: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private decimal GetAssemblyQuantity()
        {
            if (decimal.TryParse(txtAssemblyQty.Text, out decimal qty) && qty > 0)
                return qty;
            return 1;
        }

        private async void btnAddProduct_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new ProductEntryDialog()
                {
                    Owner = this // Set owner to MainWindow
                };

                if (dialog.ShowDialog() == true && dialog.Result != null)
                {
                    var newEntry = dialog.Result;
                    newEntry.Num = _bomEntries.Count + 1;

                    try
                    {
                        IsLoading = true;
                        LoadingOverlay.Show("Fetching supplier data...");

                        try
                        {
                            newEntry.DigiKeyData = await _digiKeyService.GetPriceAndAvailabilityAsync(newEntry.OrderingCode);
                        }
                        catch (Exception dex)
                        {
                            _logger.LogError($"DigiKey data fetch failed: {dex.Message}");
                            newEntry.DigiKeyData = new Models.SupplierData();
                        }

                        try
                        {
                            newEntry.MouserData = await _mouserService.GetPriceAndAvailabilityAsync(newEntry.OrderingCode);
                        }
                        catch (Exception mex)
                        {
                            _logger.LogError($"Mouser data fetch failed: {mex.Message}");
                            newEntry.MouserData = new Models.SupplierData();
                        }

                        await UpdatePriceInformation(newEntry);

                        _bomEntries.Add(newEntry);
                        _logger.LogSuccess($"Added new entry: {newEntry.OrderingCode}");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error adding product: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        IsLoading = false;
                        DetectDuplicates();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error showing dialog: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _logger.LogError($"Dialog error: {ex.Message}");
            }
            DetectDuplicates();

        }


        private async void btnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_bomEntries == null || _bomEntries.Count == 0)
            {
                MessageBox.Show("Please load a BOM file first", "Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var saveDialog = new SaveFileDialog(_currentFilePath)
            {
                Owner = this
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    btnSave.IsEnabled = false;
                    IsLoading = true;
                    LoadingOverlay.Show("Saving files...");

                    string directory = Path.GetDirectoryName(saveDialog.SelectedFilePath);
                    string baseFileName = Path.GetFileNameWithoutExtension(saveDialog.SelectedFilePath);
                    string bomFolderPath = Path.Combine(directory, baseFileName);

                    // Ensure directory exists
                    Directory.CreateDirectory(bomFolderPath);

                    // Get the selected file options
                    var selectedOptions = saveDialog.GetSelectedOptions();
                    StringBuilder exportLog = new StringBuilder();
                    exportLog.AppendLine($"Export started at {DateTime.Now}");

                    // Save main BOM file
                    if (selectedOptions["MainBom"])
                    {
                        string mainBomPath = Path.Combine(bomFolderPath, $"{baseFileName}_PRICES.xlsx");
                        await _excelService.SaveBomFileAsync(mainBomPath, _bomEntries.ToList());
                        exportLog.AppendLine("Main BOM file exported.");

                        // If external suppliers are selected and exist, add them to the main file as well
                        if (selectedOptions["ExternalSuppliers"] && _externalSupplierService.ExternalSupplierEntries.Any())
                        {
                            try
                            {
                                await _excelService.SaveExternalSuppliersSheetAsync(
                                    mainBomPath,
                                    _externalSupplierService.ExternalSupplierEntries.ToList());

                                exportLog.AppendLine("External suppliers exported to separate sheet in main file.");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError($"Error exporting external suppliers to main file: {ex.Message}");
                                exportLog.AppendLine($"Error exporting external suppliers to main file: {ex.Message}");
                            }
                        }
                    }

                    // Export missing parts to a separate file
                    if (selectedOptions["MissingParts"])
                    {
                        var missingPartsCount = _bomEntries.Count(e =>
                            !(e.DigiKeyData?.IsAvailable ?? false) &&
                            !(e.MouserData?.IsAvailable ?? false) &&
                            !(e.FarnellData?.IsAvailable ?? false) &&
                            !(e.IsraelData?.IsAvailable ?? false));

                        if (missingPartsCount > 0)
                        {
                            try
                            {
                                string missingPartsFilePath = await ExportMissingPartsFile(bomFolderPath, baseFileName);
                                exportLog.AppendLine($"Missing parts exported to {Path.GetFileName(missingPartsFilePath)}");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError($"Error exporting missing parts: {ex.Message}");
                                exportLog.AppendLine($"Error exporting missing parts: {ex.Message}");
                            }
                        }
                        else
                        {
                            exportLog.AppendLine("No missing parts to export.");
                        }
                    }

                    // Export DigiKey files
                    if (selectedOptions["DigiKeyList"])
                    {
                        string dkListPath = Path.Combine(bomFolderPath, $"{baseFileName}_DK_List.xlsx");
                        await _digiKeyExporter.ExportDigiKeyItemsAsync(_bomEntries.ToList(), dkListPath,
                            Path.GetFileNameWithoutExtension(_currentFilePath), baseFileName, false);
                        exportLog.AppendLine("DigiKey list exported.");
                    }

                    if (selectedOptions["DigiKeyBestPrices"])
                    {
                        string dkBestPricesPath = Path.Combine(bomFolderPath, $"{baseFileName}_DK_Best_Prices.xlsx");
                        await _digiKeyExporter.ExportDigiKeyItemsAsync(_bomEntries.ToList(), dkBestPricesPath,
                            Path.GetFileNameWithoutExtension(_currentFilePath), baseFileName, true);
                        exportLog.AppendLine("DigiKey best prices exported.");
                    }

                    // Export Mouser files
                    if (selectedOptions["MouserList"])
                    {
                        string msListPath = Path.Combine(bomFolderPath, $"{baseFileName}_MS_List.xlsx");
                        await _mouserExporter.ExportMouserItemsAsync(_bomEntries.ToList(), msListPath,
                            Path.GetFileNameWithoutExtension(_currentFilePath), baseFileName, false);
                        exportLog.AppendLine("Mouser list exported.");
                    }

                    if (selectedOptions["MouserBestPrices"])
                    {
                        string msBestPricesPath = Path.Combine(bomFolderPath, $"{baseFileName}_MS_Best_Prices.xlsx");
                        await _mouserExporter.ExportMouserItemsAsync(_bomEntries.ToList(), msBestPricesPath,
                            Path.GetFileNameWithoutExtension(_currentFilePath), baseFileName, true);
                        exportLog.AppendLine("Mouser best prices exported.");
                    }

                    // Export Farnell files
                    if (selectedOptions["FarnellList"])
                    {
                        string frListPath = Path.Combine(bomFolderPath, $"{baseFileName}_FR_List.xlsx");
                        await _farnellExporter.ExportFarnellItemsAsync(_bomEntries.ToList(), frListPath,
                            Path.GetFileNameWithoutExtension(_currentFilePath), baseFileName, false);
                        exportLog.AppendLine("Farnell list exported.");
                    }

                    if (selectedOptions["FarnellBestPrices"])
                    {
                        string frBestPricesPath = Path.Combine(bomFolderPath, $"{baseFileName}_FR_Best_Prices.xlsx");
                        await _farnellExporter.ExportFarnellItemsAsync(_bomEntries.ToList(), frBestPricesPath,
                            Path.GetFileNameWithoutExtension(_currentFilePath), baseFileName, true);
                        exportLog.AppendLine("Farnell best prices exported.");
                    }

                    // Export Israel files
                    if (selectedOptions["IsraelList"])
                    {
                        string ilListPath = Path.Combine(bomFolderPath, $"{baseFileName}_DK-IL_List.xlsx");
                        await _israelExporter.ExportIsraelItemsAsync(_bomEntries.ToList(), ilListPath,
                            Path.GetFileNameWithoutExtension(_currentFilePath), baseFileName, false);
                        exportLog.AppendLine("DigiKey Israel list exported.");
                    }

                    if (selectedOptions["IsraelBestPrices"])
                    {
                        string ilBestPricesPath = Path.Combine(bomFolderPath, $"{baseFileName}_DK-IL_Best_Prices.xlsx");
                        await _israelExporter.ExportIsraelItemsAsync(_bomEntries.ToList(), ilBestPricesPath,
                            Path.GetFileNameWithoutExtension(_currentFilePath), baseFileName, true);
                        exportLog.AppendLine("DigiKey Israel best prices exported.");
                    }

                    // Export External Supplier files
                    if (selectedOptions["ExternalSuppliers"] && _externalSupplierService.ExternalSupplierEntries.Any())
                    {
                        string externalSuppliersPath = Path.Combine(bomFolderPath, $"{baseFileName}_External_Suppliers.xlsx");
                        await _externalSupplierExporter.ExportExternalSupplierItemsAsync(_bomEntries.ToList(), externalSuppliersPath);
                        exportLog.AppendLine("External suppliers list exported to separate file.");
                    }

                    // Save export log
                    string logPath = Path.Combine(bomFolderPath, $"{baseFileName}_export_log.txt");
                    await File.WriteAllTextAsync(logPath, exportLog.ToString());

                    MessageBox.Show($"Files saved successfully to:\n{bomFolderPath}",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                    if (saveDialog.OpenAfterSave)
                    {
                        try
                        {
                            var psi = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = bomFolderPath,
                                UseShellExecute = true
                            };
                            System.Diagnostics.Process.Start(psi);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Error opening folder: {ex.Message}");
                            MessageBox.Show("Files were saved but could not open the folder automatically.",
                                "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving files: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    _logger.LogError($"Save error: {ex.Message}");
                }
                finally
                {
                    btnSave.IsEnabled = true;
                    IsLoading = false;
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            var regex = new Regex("[^0-9.,]+");
            e.Handled = regex.IsMatch(e.Text);

            // Additional validation to ensure only one decimal point
            if (e.Text == "." || e.Text == ",")
            {
                TextBox textBox = sender as TextBox;
                if (textBox != null)
                {
                    e.Handled = textBox.Text.Contains(".") || textBox.Text.Contains(",");
                }
            }
        }

        // Update this method to make sure it properly instantiates the dialog
        private void ShowMissingProducts_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new MissingProductsDialog(
                this,
                _bomEntries,
                _digiKeyService,
                _mouserService,
                _farnellService,
                _israelService,
                _externalSupplierService,
                _logger);
            dialog.Owner = this;
            dialog.ShowDialog();
        }


        private void btnExternalSuppliers_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var manager = new ExternalSupplierManager(this, _externalSupplierService)
                {
                    Owner = this
                };
                manager.ShowDialog();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error opening External Supplier Manager: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        // This method needs to be public so MissingProductsDialog can access it
        public BomEntry GetBomEntryByNum(int num)
        {
            return _bomEntries.FirstOrDefault(e => e.Num == num);
        }

        // This method needs to be public so MissingProductsDialog can access it
        public void RefreshBomEntry(BomEntry entry)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                dgBomEntries.Items.Refresh();
                UpdateTotals();
            });
        }

        private void btnEmail_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new EmailDialog { Owner = this };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error showing email dialog: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnHelp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var helpWindow = new UserGuideWindow
                {
                    Owner = this
                };
                helpWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error showing help guide: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }




        private void btnDuplicates_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new DuplicateManagementDialog(this)
            {
                Owner = this
            };
            dialog.ShowDialog();
        }




        public void HandleDuplicateOperation(Action operation, string operationName)
        {
            try
            {
                operation.Invoke();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during {operationName}: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _logger.LogError($"Error during {operationName}: {ex.Message}");
            }
            finally
            {
                UpdateTotalItems();
                DetectDuplicates();
            }
        }





        public void RefreshAfterDuplicateManagement()
        {
            HandleDuplicateOperation(() =>
            {
                dgBomEntries.Items.Refresh();
                UpdateTotalItems();
                DetectDuplicates();
            }, "duplicate management refresh");
        }



        public async Task RefreshAfterDuplicateManagementAsync()
        {
            try
            {
                // Only refresh entries that have been marked for API refresh
                var entry = _bomEntries.FirstOrDefault(e => e.NeedsApiRefresh);
                if (entry != null)
                {
                    try
                    {
                        entry.IsLoading = true;
                        dgBomEntries.Items.Refresh();

                        var selectedTemplate = cmbTemplates.SelectedItem as TemplateManager.TemplateDefinition;
                        bool useBuffer = selectedTemplate?.UseQuantityBuffer ?? QuickLoadBufferCheckbox.IsChecked ?? false;
                        decimal assemblyQty = selectedTemplate?.AssemblyQuantity ??
                            (decimal.TryParse(txtAssemblyQty.Text, out decimal qty) ? qty : 1m);

                        // Create a temporary config to use for calculation
                        var tempConfig = new ExcelMappingConfiguration
                        {
                            AssemblyQuantity = assemblyQty,
                            UseQuantityBuffer = useBuffer
                        };

                        // Calculate base quantity
                        decimal baseQty = entry.QuantityForOne * assemblyQty;

                        // Apply buffer if enabled
                        if (useBuffer)
                        {
                            decimal bufferedQty = baseQty * 1.1m; // Add 10%
                            entry.QuantityTotal = tempConfig.CalculateQuantityWithAssembly(entry.QuantityForOne);
                        }
                        else
                        {
                            entry.QuantityTotal = tempConfig.CalculateQuantityWithAssembly(entry.QuantityForOne);
                        }
                        // Make new API calls only for this specific entry
                        if (NetworkHelper.CheckInternetWithMessage())
                        {
                            try
                            {
                                entry.DigiKeyData = await _digiKeyService.GetPriceAndAvailabilityAsync(entry.OrderingCode);
                            }
                            catch (Exception dex)
                            {
                                _logger.LogError($"DigiKey refresh failed for {entry.OrderingCode}: {dex.Message}");
                            }

                            try
                            {
                                entry.MouserData = await _mouserService.GetPriceAndAvailabilityAsync(entry.OrderingCode);
                            }
                            catch (Exception mex)
                            {
                                _logger.LogError($"Mouser refresh failed for {entry.OrderingCode}: {mex.Message}");
                            }

                            try
                            {
                                entry.FarnellData = await _farnellService.GetPriceAndAvailabilityAsync(entry.OrderingCode);
                            }
                            catch (Exception fex)
                            {
                                _logger.LogError($"Farnell refresh failed for {entry.OrderingCode}: {fex.Message}");
                            }
                            
                            try
                            {
                                entry.IsraelData = await _israelService.GetPriceAndAvailabilityAsync(entry.OrderingCode);
                            }
                            catch (Exception iex)
                            {
                                _logger.LogError($"Israel refresh failed for {entry.OrderingCode}: {iex.Message}");
                            }

                            await UpdatePriceInformation(entry);
                        }
                    }
                    finally
                    {
                        entry.IsLoading = false;
                        entry.NeedsApiRefresh = false;
                        dgBomEntries.Items.Refresh();
                    }
                }

                // Refresh UI and calculations
                dgBomEntries.Items.Refresh();
                UpdateTotalItems();
                DetectDuplicates();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error refreshing after duplicate management: {ex.Message}");
                throw;
            }
        }

        private void btnOpenBom_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_bomEntries == null)
                {
                    MessageBox.Show("BOM entries collection is null.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (_logger == null)
                {
                    MessageBox.Show("Logger is null.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (_digiKeyService == null)
                {
                    MessageBox.Show("DigiKey service is null.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var dialog = new OpenBomUploadDialog(_logger, _bomEntries, (DigiKeyService)_digiKeyService)
                {
                    Owner = this
                };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error showing OpenBOM dialog: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnManageInventory_Click(object sender, RoutedEventArgs e)
        {
            if (_bomEntries.Count == 0)
            {
                MessageBox.Show("Please load a BOM file first", "Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new InventoryManagementWindow(_bomEntries)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                // Refresh the grid and update calculations
                dgBomEntries.Items.Refresh();
                UpdateTotalItems();
                DetectDuplicates(); // Add this line to recheck for duplicates
            }
        }

        private void btnCatalogDuplicates_Click(object sender, RoutedEventArgs e)
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


        private async Task<string> ExportMissingPartsFile(string folderPath, string baseFileName)
        {
            try
            {
                // Get all missing parts
                var missingParts = _bomEntries.Where(e =>
                    !(e.DigiKeyData?.IsAvailable ?? false) &&
                    !(e.MouserData?.IsAvailable ?? false) &&
                    !(e.FarnellData?.IsAvailable ?? false) &&
                    !(e.IsraelData?.IsAvailable ?? false)).ToList();

                if (missingParts.Count == 0)
                    return null; // No missing parts to export

                // Create the file path
                string missingPartsFilePath = Path.Combine(folderPath, $"{baseFileName}_MISSING_PARTS.xlsx");

                // Create the Excel file
                ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
                using (var package = new ExcelPackage())
                {
                    var sheet = package.Workbook.Worksheets.Add("Missing Parts");

                    // Set headers
                    sheet.Cells[1, 1].Value = "Order Code";
                    sheet.Cells[1, 2].Value = "Designator";
                    sheet.Cells[1, 3].Value = "Value";
                    sheet.Cells[1, 4].Value = "PCB Footprint";
                    sheet.Cells[1, 5].Value = "Quantity";
                    sheet.Cells[1, 6].Value = "DigiKey Available";
                    sheet.Cells[1, 7].Value = "Mouser Available";
                    sheet.Cells[1, 8].Value = "Farnell Available";
                    sheet.Cells[1, 9].Value = "Israel Available";
                    sheet.Cells[1, 10].Value = "Error/Reason";

                    // Format headers
                    for (int i = 1; i <= 10; i++)
                    {
                        sheet.Cells[1, i].Style.Font.Bold = true;
                        sheet.Cells[1, i].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                        sheet.Cells[1, i].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                    }

                    // Add data
                    for (int row = 0; row < missingParts.Count; row++)
                    {
                        var part = missingParts[row];
                        int excelRow = row + 2;

                        sheet.Cells[excelRow, 1].Value = part.OrderingCode;
                        sheet.Cells[excelRow, 2].Value = part.Designator;
                        sheet.Cells[excelRow, 3].Value = part.Value;
                        sheet.Cells[excelRow, 4].Value = part.PcbFootprint;
                        sheet.Cells[excelRow, 5].Value = part.QuantityTotal;
                        sheet.Cells[excelRow, 6].Value = part.DigiKeyData?.IsAvailable ?? false;
                        sheet.Cells[excelRow, 7].Value = part.MouserData?.IsAvailable ?? false;
                        sheet.Cells[excelRow, 8].Value = part.FarnellData?.IsAvailable ?? false;
                        sheet.Cells[excelRow, 9].Value = part.IsraelData?.IsAvailable ?? false;

                        // Determine the error reason
                        string errorReason = "Not found in supplier databases";

                        if (part.DigiKeyData != null && !part.DigiKeyData.IsAvailable)
                        {
                            errorReason = "DigiKey: Part not available. ";
                        }

                        if (part.MouserData != null && !part.MouserData.IsAvailable)
                        {
                            errorReason += "Mouser: Part not available. ";
                        }

                        if (part.FarnellData != null && !part.FarnellData.IsAvailable)
                        {
                            errorReason += "Farnell: Part not available. ";
                        }

                        if (part.IsraelData != null && !part.IsraelData.IsAvailable)
                        {
                            errorReason += "Israel: Part not available.";
                        }

                        sheet.Cells[excelRow, 10].Value = errorReason;

                        // Color the row red for visibility
                        var rowRange = sheet.Cells[excelRow, 1, excelRow, 10];
                        rowRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                        rowRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(255, 255, 230, 230));
                    }

                    // Auto fit columns
                    sheet.Cells.AutoFitColumns();

                    // Add a summary sheet
                    var summarySheet = package.Workbook.Worksheets.Add("Summary");
                    summarySheet.Cells[1, 1].Value = "Missing Parts Summary";
                    summarySheet.Cells[1, 1].Style.Font.Bold = true;
                    summarySheet.Cells[1, 1].Style.Font.Size = 14;

                    summarySheet.Cells[3, 1].Value = "Total Missing Parts";
                    summarySheet.Cells[3, 2].Value = missingParts.Count;

                    summarySheet.Cells[5, 1].Value = "Missing from DigiKey";
                    summarySheet.Cells[5, 2].Value = missingParts.Count(p => !(p.DigiKeyData?.IsAvailable ?? false));

                    summarySheet.Cells[6, 1].Value = "Missing from Mouser";
                    summarySheet.Cells[6, 2].Value = missingParts.Count(p => !(p.MouserData?.IsAvailable ?? false));

                    summarySheet.Cells[7, 1].Value = "Missing from Farnell";
                    summarySheet.Cells[7, 2].Value = missingParts.Count(p => !(p.FarnellData?.IsAvailable ?? false));

                    summarySheet.Cells[8, 1].Value = "Missing from Israel";
                    summarySheet.Cells[8, 2].Value = missingParts.Count(p => !(p.IsraelData?.IsAvailable ?? false));

                    // Save the file
                    await package.SaveAsAsync(new FileInfo(missingPartsFilePath));

                    return missingPartsFilePath;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error exporting missing parts: {ex.Message}");
                throw;
            }
        }

        private void LoadingOverlay_Loaded(object sender, RoutedEventArgs e)
        {

        }

        // Add this method to your code-behind file
        private void btnToggleButtons_Click(object sender, RoutedEventArgs e)
        {
            // Toggle visibility of the buttons panel
            if (buttonsPanel.Visibility == Visibility.Visible)
            {
                // Hide the panel
                buttonsPanel.Visibility = Visibility.Collapsed;
                toggleText.Text = "Show Buttons";

                // Rotate the icon to point down (show)
                RotateTransform rotateTransform = new RotateTransform(180);
                toggleIcon.RenderTransform = rotateTransform;
            }
            else
            {
                // Show the panel
                buttonsPanel.Visibility = Visibility.Visible;
                toggleText.Text = "Hide Buttons";

                // Reset icon rotation (hide - point up)
                toggleIcon.RenderTransform = null;
            }
        }

        // You might want to add this to your Window constructor or Loaded event 
        // to remember the user's preference across sessions
        private void InitializeButtonsPanel()
        {
            // You can use application settings or another method to save the state
            bool buttonsVisible = true; // Default or from settings

            if (!buttonsVisible)
            {
                buttonsPanel.Visibility = Visibility.Collapsed;
                toggleText.Text = "Show Buttons";

                RotateTransform rotateTransform = new RotateTransform(180);
                toggleIcon.RenderTransform = rotateTransform;
            }
        }

        private void btnReconfigureMapping_Click(object sender, RoutedEventArgs e)
        {
            // Check if we have a loaded file
            if (string.IsNullOrEmpty(_currentFilePath) || !File.Exists(_currentFilePath))
            {
                MessageBox.Show("Please load a BOM file first", "Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // First, try to load the last used configuration
                ExcelMappingConfiguration currentConfig = LoadLastUsedConfiguration();

                // If no last used configuration is found, try to recreate from current data
                if (currentConfig == null && _bomEntries != null && _bomEntries.Count > 0)
                {
                    // Create a basic configuration based on the loaded data
                    currentConfig = new ExcelMappingConfiguration();

                    // Use the selected template if available
                    var selectedTemplate = cmbTemplates.SelectedItem as TemplateManager.TemplateDefinition;
                    if (selectedTemplate != null)
                    {
                        currentConfig = _templateManager.ConvertToMappingConfiguration(selectedTemplate, _currentFilePath);
                        // Override with actual assembly quantity if using quick load
                        if (decimal.TryParse(txtAssemblyQty.Text, out decimal qty))
                        {
                            currentConfig.AssemblyQuantity = qty;
                        }
                        currentConfig.UseQuantityBuffer = QuickLoadBufferCheckbox.IsChecked ?? false;
                    }
                }

                // Show the mapping dialog with the current configuration
                var mappingDialog = new MappingConfigurationDialog(_currentFilePath, currentConfig)
                {
                    Owner = this
                };

                if (mappingDialog.ShowDialog() == true)
                {
                    // Save the new configuration before processing
                    SaveLastUsedConfiguration(mappingDialog.Configuration);

                    // Process the new configuration
                    ReloadWithNewConfiguration(mappingDialog.Configuration);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening mapping configuration: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _logger.LogError($"Error in reconfigure mapping: {ex.Message}");
            }
        }
        // Add this method to reload the file with the new configuration
        private async void ReloadWithNewConfiguration(ExcelMappingConfiguration newConfig)
        {
            try
            {
                IsLoading = true;
                btnSelectFile.IsEnabled = false;
                txtSelectedFile.Text = "Reloading BOM data...";

                // Save the configuration for future use
                SaveLastUsedConfiguration(newConfig);

                // Clear existing entries
                _bomEntries.Clear();

                // Load the file with the new configuration
                var entries = await _excelService.ReadBomFileAsync(_currentFilePath, newConfig);

                // Add the new entries
                foreach (var entry in entries)
                {
                    _bomEntries.Add(entry);
                }

                // Update UI
                UpdateTotalItems();
                _logger.LogInfo($"Reloaded {_bomEntries.Count} entries from BOM file with new configuration");

                // Fetch supplier data
                await FetchSupplierDataForEntries();

                txtSelectedFile.Text = System.IO.Path.GetFileName(_currentFilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reloading file: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                _logger.LogError($"Error reloading file with new configuration: {ex.Message}");
                txtSelectedFile.Text = string.Empty;
            }
            finally
            {
                btnSelectFile.IsEnabled = true;
                IsLoading = false;
            }
        }
        // In MainWindow.xaml.cs
        private void SaveLastUsedConfiguration(ExcelMappingConfiguration config)
        {
            try
            {
                const string lastUsedTemplateName = "LastUsedConfiguration";

                // Convert configuration to template
                var template = _templateManager.ConvertFromMappingConfiguration(config, lastUsedTemplateName);

                // Set the quantity buffer flag
                template.UseQuantityBuffer = config.UseQuantityBuffer;

                // Log before saving
                _logger.LogInfo($"Saving LastUsedConfiguration template with {template.ColumnMappings.Count} mappings");

                // Make sure to call SaveTemplate with overwrite=true to replace any existing template
                bool success = _templateManager.SaveTemplate(template, true);

                if (success)
                {
                    _logger.LogInfo("Last used configuration saved successfully as template");
                }
                else
                {
                    _logger.LogError("Failed to save LastUsedConfiguration template");
                }

                // Force refresh templates after saving
                _templateManager.InvalidateCache();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving last used configuration: {ex.Message}");
            }
        }

        private ExcelMappingConfiguration LoadLastUsedConfiguration()
        {
            try
            {
                const string lastUsedTemplateName = "LastUsedConfiguration";

                // Get the template by name
                var template = _templateManager.GetTemplateByName(lastUsedTemplateName);

                if (template == null)
                {
                    _logger.LogInfo("No last used configuration found");
                    return null;
                }

                // Convert template to configuration
                var config = _templateManager.ConvertToMappingConfiguration(template, _currentFilePath);

                // Set the quantity buffer flag
                config.UseQuantityBuffer = template.UseQuantityBuffer;

                _logger.LogInfo("Last used configuration loaded successfully");
                return config;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading last used configuration: {ex.Message}");
                return null;
            }
        }


        // Add these methods to the MainWindow class in MainWindow.xaml.cs

        private void MenuItemMarkAsExternalSupplier_Click(object sender, RoutedEventArgs e)
        {
            BomEntry selectedEntry = dgBomEntries.SelectedItem as BomEntry;
            if (selectedEntry == null) return;

            OpenExternalSupplierDialog(selectedEntry);
        }

        private void MenuItemOpenInDigiKey_Click(object sender, RoutedEventArgs e)
        {
            BomEntry selectedEntry = dgBomEntries.SelectedItem as BomEntry;
            if (selectedEntry == null || string.IsNullOrWhiteSpace(selectedEntry.OrderingCode)) return;

            try
            {
                var url = $"https://www.digikey.co.il/en/products/result?keywords={Uri.EscapeDataString(selectedEntry.OrderingCode)}";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error opening DigiKey link: {ex.Message}");
                MessageBox.Show($"Error opening link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuItemOpenInMouser_Click(object sender, RoutedEventArgs e)
        {
            BomEntry selectedEntry = dgBomEntries.SelectedItem as BomEntry;
            if (selectedEntry == null || string.IsNullOrWhiteSpace(selectedEntry.OrderingCode)) return;

            try
            {
                var url = $"https://www.mouser.com/c/?q={Uri.EscapeDataString(selectedEntry.OrderingCode)}";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error opening Mouser link: {ex.Message}");
                MessageBox.Show($"Error opening link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuItemOpenInFarnell_Click(object sender, RoutedEventArgs e)
        {
            BomEntry selectedEntry = dgBomEntries.SelectedItem as BomEntry;
            if (selectedEntry == null || string.IsNullOrWhiteSpace(selectedEntry.OrderingCode)) return;

            try
            {
                var url = $"https://il.farnell.com/search?st={Uri.EscapeDataString(selectedEntry.OrderingCode)}";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error opening Farnell link: {ex.Message}");
                MessageBox.Show($"Error opening link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenExternalSupplierDialog(BomEntry entry)
        {
            try
            {
                var dialog = new ExternalSupplierDialog(entry)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() == true)
                {
                    // If Skip was used, we'll still have an entry but with minimal info
                    _externalSupplierService.AddExternalSupplierEntry(dialog.ExternalSupplierEntry);

                    // Update the main window
                    RefreshAfterExternalSupplierChange(entry.Num);

                    _logger.LogSuccess($"Added external supplier for {entry.OrderingCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error showing external supplier dialog: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetOtherSupplierString(SupplierType excludedSupplier, bool skipDigiKey, bool skipMouser, bool skipFarnell, bool skipIsrael)
        {
            var availableSuppliers = new List<string>();
            
            if (!skipDigiKey && excludedSupplier != SupplierType.DigiKey)
                availableSuppliers.Add("DigiKey");
                
            if (!skipMouser && excludedSupplier != SupplierType.Mouser)
                availableSuppliers.Add("Mouser");
                
            if (!skipFarnell && excludedSupplier != SupplierType.Farnell)
                availableSuppliers.Add("Farnell");
                
            if (!skipIsrael && excludedSupplier != SupplierType.Israel)
                availableSuppliers.Add("Israel");
                
            return string.Join(", ", availableSuppliers);
        }

    }
}