using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BOMVIEW.Models;
using BOMVIEW.Interfaces;
using BOMVIEW.Services;

namespace BOMVIEW
{
    public partial class MissingProductsDialog : Window
    {
        private readonly ObservableCollection<MissingProductViewModel> _missingProducts;
        private readonly ISupplierService _digiKeyService;
        private readonly ISupplierService _mouserService;
        private readonly ISupplierService _farnellService;
        private readonly MainWindow _mainWindow;
        private readonly ILogger _logger;
        private readonly ExternalSupplierService _externalSupplierService;

        public class MissingProductViewModel : BomEntry
        {
            public string OriginalOrderingCode { get; set; }
            public string MissingFrom { get; set; }
        }

        public MissingProductsDialog(MainWindow mainWindow,
      ObservableCollection<BomEntry> entries,
      ISupplierService digiKeyService,
      ISupplierService mouserService,
      ISupplierService farnellService,
      ExternalSupplierService externalSupplierService,
      ILogger logger)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            _digiKeyService = digiKeyService;
            _mouserService = mouserService;
            _farnellService = farnellService;
            _externalSupplierService = externalSupplierService;
            _logger = logger;

            _missingProducts = new ObservableCollection<MissingProductViewModel>();
            LoadMissingProducts(entries);
            MissingProductsGrid.ItemsSource = _missingProducts;
        }


        private void LoadMissingProducts(ObservableCollection<BomEntry> entries)
        {
            foreach (var entry in entries)
            {
                // Skip entries that already have an external supplier
                if (_externalSupplierService.HasExternalSupplier(entry.Num))
                {
                    continue;
                }

                bool isDigiKeyMissing = !(entry.DigiKeyData?.IsAvailable ?? false);
                bool isMouserMissing = !(entry.MouserData?.IsAvailable ?? false);
                bool isFarnellMissing = !(entry.FarnellData?.IsAvailable ?? false);

                if (isDigiKeyMissing || isMouserMissing || isFarnellMissing)
                {
                    // Determine which suppliers are missing
                    string missingFrom;
                    if (isDigiKeyMissing && isMouserMissing && isFarnellMissing)
                        missingFrom = "All";
                    else if (isDigiKeyMissing && isMouserMissing)
                        missingFrom = "DigiKey & Mouser";
                    else if (isDigiKeyMissing && isFarnellMissing)
                        missingFrom = "DigiKey & Farnell";
                    else if (isMouserMissing && isFarnellMissing)
                        missingFrom = "Mouser & Farnell";
                    else if (isDigiKeyMissing)
                        missingFrom = "DigiKey";
                    else if (isMouserMissing)
                        missingFrom = "Mouser";
                    else
                        missingFrom = "Farnell";

                    var missingProduct = new MissingProductViewModel
                    {
                        Num = entry.Num,
                        OrderingCode = entry.OrderingCode,
                        OriginalOrderingCode = entry.OrderingCode,
                        Designator = entry.Designator,
                        Value = entry.Value,
                        PcbFootprint = entry.PcbFootprint,
                        QuantityForOne = entry.QuantityForOne,
                        QuantityTotal = entry.QuantityTotal,
                        MissingFrom = missingFrom
                    };

                    _missingProducts.Add(missingProduct);
                }
            }
        }
        private async void MissingProductsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            try
            {
                var missingProduct = e.Row.Item as MissingProductViewModel;
                if (missingProduct == null) return;

                var editedElement = e.EditingElement as TextBox;
                if (editedElement == null) return;

                var newOrderingCode = editedElement.Text.Trim();
                if (string.IsNullOrEmpty(newOrderingCode) || newOrderingCode == missingProduct.OriginalOrderingCode)
                    return;

                // Check if the product already has an external supplier
                if (_externalSupplierService.HasExternalSupplier(missingProduct.Num))
                {
                    // Remove from missing products list
                    _missingProducts.Remove(missingProduct);
                    return;
                }

                // Find the corresponding entry in the main grid
                var mainEntry = _mainWindow.GetBomEntryByNum(missingProduct.Num);
                if (mainEntry == null) return;

                // Update ordering code and fetch new data
                mainEntry.OrderingCode = newOrderingCode;

                // Fetch new data based on which supplier was missing
                if (missingProduct.MissingFrom.Contains("DigiKey") || missingProduct.MissingFrom == "All")
                {
                    mainEntry.DigiKeyData = await _digiKeyService.GetPriceAndAvailabilityAsync(newOrderingCode);
                }

                if (missingProduct.MissingFrom.Contains("Mouser") || missingProduct.MissingFrom == "All")
                {
                    mainEntry.MouserData = await _mouserService.GetPriceAndAvailabilityAsync(newOrderingCode);
                }

                if (missingProduct.MissingFrom.Contains("Farnell") || missingProduct.MissingFrom == "All")
                {
                    mainEntry.FarnellData = await _farnellService.GetPriceAndAvailabilityAsync(newOrderingCode);
                }

                // Update price information
                await _mainWindow.UpdatePriceInformation(mainEntry);

                // Check if still missing with the new code
                bool isDigiKeyMissing = !(mainEntry.DigiKeyData?.IsAvailable ?? false);
                bool isMouserMissing = !(mainEntry.MouserData?.IsAvailable ?? false);
                bool isFarnellMissing = !(mainEntry.FarnellData?.IsAvailable ?? false);

                // Update the missing products list
                bool isStillMissing = false;

                if (missingProduct.MissingFrom == "All")
                {
                    isStillMissing = isDigiKeyMissing && isMouserMissing && isFarnellMissing;
                }
                else if (missingProduct.MissingFrom == "DigiKey & Mouser")
                {
                    isStillMissing = isDigiKeyMissing && isMouserMissing;
                }
                else if (missingProduct.MissingFrom == "DigiKey & Farnell")
                {
                    isStillMissing = isDigiKeyMissing && isFarnellMissing;
                }
                else if (missingProduct.MissingFrom == "Mouser & Farnell")
                {
                    isStillMissing = isMouserMissing && isFarnellMissing;
                }
                else if (missingProduct.MissingFrom == "DigiKey")
                {
                    isStillMissing = isDigiKeyMissing;
                }
                else if (missingProduct.MissingFrom == "Mouser")
                {
                    isStillMissing = isMouserMissing;
                }
                else if (missingProduct.MissingFrom == "Farnell")
                {
                    isStillMissing = isFarnellMissing;
                }

                if (!isStillMissing)
                {
                    _missingProducts.Remove(missingProduct);
                }

                _logger.LogSuccess($"Successfully updated product {newOrderingCode}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating product: {ex.Message}");
                MessageBox.Show($"Error updating product: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                // Revert the change in the grid
                var missingProduct = e.Row.Item as MissingProductViewModel;
                if (missingProduct != null)
                {
                    missingProduct.OrderingCode = missingProduct.OriginalOrderingCode;
                }
                MissingProductsGrid.Items.Refresh();
            }
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!NetworkHelper.CheckInternetWithMessage())
                    return;

                var visibleProducts = _missingProducts.ToList();
                if (!visibleProducts.Any())
                {
                    MessageBox.Show("No products to update", "Information",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                UpdateButton.IsEnabled = false;
                _mainWindow.IsLoading = true;
                _mainWindow.LoadingOverlay.Show("Updating products...");

                int updatedCount = 0;
                int totalCount = visibleProducts.Count;

                foreach (var product in visibleProducts.ToList())
                {
                    try
                    {
                        _mainWindow.LoadingOverlay.LoadingMessage =
                            $"Updating product {updatedCount + 1} of {totalCount}...";

                        // Skip products that already have an external supplier
                        if (_externalSupplierService.HasExternalSupplier(product.Num))
                        {
                            // Remove from missing products list if it's still there
                            if (_missingProducts.Contains(product))
                            {
                                _missingProducts.Remove(product);
                            }
                            continue;
                        }

                        // Find the corresponding entry in the main grid
                        var mainEntry = _mainWindow.GetBomEntryByNum(product.Num);
                        if (mainEntry == null) continue;

                        // Get the new ordering code that was entered in the dialog
                        string newOrderingCode = product.OrderingCode;
                        if (string.IsNullOrWhiteSpace(newOrderingCode) || newOrderingCode == product.OriginalOrderingCode)
                        {
                            continue; // Skip if no new code was entered
                        }

                        // Update the main entry's ordering code
                        mainEntry.OrderingCode = newOrderingCode;

                        // Fetch new data based on which supplier was missing using the NEW ordering code
                        if (product.MissingFrom.Contains("DigiKey") || product.MissingFrom == "All")
                        {
                            mainEntry.DigiKeyData =
                                await _digiKeyService.GetPriceAndAvailabilityAsync(newOrderingCode);
                        }

                        if (product.MissingFrom.Contains("Mouser") || product.MissingFrom == "All")
                        {
                            mainEntry.MouserData =
                                await _mouserService.GetPriceAndAvailabilityAsync(newOrderingCode);
                        }

                        if (product.MissingFrom.Contains("Farnell") || product.MissingFrom == "All")
                        {
                            mainEntry.FarnellData =
                                await _farnellService.GetPriceAndAvailabilityAsync(newOrderingCode);
                        }

                        // Update price information
                        await _mainWindow.UpdatePriceInformation(mainEntry);

                        // Check if still missing with the new code
                        bool isDigiKeyMissing = !(mainEntry.DigiKeyData?.IsAvailable ?? false);
                        bool isMouserMissing = !(mainEntry.MouserData?.IsAvailable ?? false);
                        bool isFarnellMissing = !(mainEntry.FarnellData?.IsAvailable ?? false);

                        // Determine if product is still missing based on the original missing status
                        bool isStillMissing = false;

                        if (product.MissingFrom == "All")
                        {
                            isStillMissing = isDigiKeyMissing && isMouserMissing && isFarnellMissing;
                        }
                        else if (product.MissingFrom == "DigiKey & Mouser")
                        {
                            isStillMissing = isDigiKeyMissing && isMouserMissing;
                        }
                        else if (product.MissingFrom == "DigiKey & Farnell")
                        {
                            isStillMissing = isDigiKeyMissing && isFarnellMissing;
                        }
                        else if (product.MissingFrom == "Mouser & Farnell")
                        {
                            isStillMissing = isMouserMissing && isFarnellMissing;
                        }
                        else if (product.MissingFrom == "DigiKey")
                        {
                            isStillMissing = isDigiKeyMissing;
                        }
                        else if (product.MissingFrom == "Mouser")
                        {
                            isStillMissing = isMouserMissing;
                        }
                        else if (product.MissingFrom == "Farnell")
                        {
                            isStillMissing = isFarnellMissing;
                        }

                        // Remove from missing products if now available
                        if (!isStillMissing)
                        {
                            _missingProducts.Remove(product);
                        }

                        updatedCount++;

                        // Refresh the main grid for this entry
                        _mainWindow.RefreshBomEntry(mainEntry);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error updating product {product.OrderingCode}: {ex.Message}");
                    }
                }

                MessageBox.Show(
                    $"Update complete.\nSuccessfully updated {updatedCount} of {totalCount} products.",
                    "Update Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during bulk update: {ex.Message}");
                MessageBox.Show(
                    $"Error during update: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                UpdateButton.IsEnabled = true;
                _mainWindow.IsLoading = false;
                MissingProductsGrid.Items.Refresh();
            }
        }
        private void DigiKeyLink_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var product = button?.DataContext as MissingProductViewModel;
            if (product == null) return;

            try
            {
                var url = $"https://www.digikey.co.il/en/products/result?keywords={Uri.EscapeDataString(product.OrderingCode)}";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening link: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MouserLink_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var product = button?.DataContext as MissingProductViewModel;
            if (product == null) return;

            try
            {
                var url = $"https://www.mouser.com/c/?q={Uri.EscapeDataString(product.OrderingCode)}";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening link: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void FarnellLink_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var product = button?.DataContext as MissingProductViewModel;
            if (product == null) return;

            try
            {
                var url = $"https://il.farnell.com/search?st={Uri.EscapeDataString(product.OrderingCode)}";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening link: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExternalSupplierButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var missingProduct = button?.DataContext as MissingProductViewModel;

            // If button is clicked from outside the DataGrid row, use the selected item
            if (missingProduct == null && MissingProductsGrid.SelectedItem != null)
            {
                missingProduct = MissingProductsGrid.SelectedItem as MissingProductViewModel;
            }

            if (missingProduct == null)
            {
                MessageBox.Show("Please select a product first.", "No Product Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // Find the corresponding entry in the main grid
                var mainEntry = _mainWindow.GetBomEntryByNum(missingProduct.Num);
                if (mainEntry == null) return;

                // Open the external supplier dialog
                var dialog = new ExternalSupplierDialog(mainEntry)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() == true)
                {
                    // Add the external supplier entry
                    _externalSupplierService.AddExternalSupplierEntry(dialog.ExternalSupplierEntry);

                    // Remove the product from the missing products list
                    _missingProducts.Remove(missingProduct);

                    // Update the main window
                    _mainWindow.RefreshAfterExternalSupplierChange(missingProduct.Num);

                    // Refresh the grid to reflect changes
                    MissingProductsGrid.Items.Refresh();

                    _logger.LogSuccess($"Added external supplier for product {missingProduct.OrderingCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error adding external supplier: {ex.Message}");
                MessageBox.Show($"Error adding external supplier: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}