using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using BOMVIEW.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System;
using System.Threading.Tasks;
using System.Linq;
using BOMVIEW.Services;
using System.Windows.Controls;
using System.Globalization;

namespace BOMVIEW
{
    public partial class InventoryManagementWindow : Window, INotifyPropertyChanged
    {
        private readonly ObservableCollection<BomEntry> _entries;
        private bool _updatingEntries;
        private readonly object _updateLock = new object();

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public InventoryManagementWindow(ObservableCollection<BomEntry> entries)
        {
            InitializeComponent();
            _entries = entries;
            dgInventory.ItemsSource = _entries;

            // Initialize assembly quantity with first entry's data
            txtAssemblyQty.Text = (_entries.FirstOrDefault()?.QuantityTotal /
                Math.Max(1, _entries.FirstOrDefault()?.QuantityForOne ?? 1)).ToString();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var entry in _entries)
                {
                    entry.AdjustedOrderQuantity = Math.Max(0, entry.QuantityTotal - entry.StockQuantity);
                    entry.DigiKeyOrderQuantity = entry.AdjustedOrderQuantity;
                    entry.MouserOrderQuantity = entry.AdjustedOrderQuantity;
                    entry.FarnellOrderQuantity = entry.AdjustedOrderQuantity; // ADD THIS LINE
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving inventory data: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            // Allow digits, decimal point, and comma (for different cultures)
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

        private async void ApplyAssemblyQty_Click(object sender, RoutedEventArgs e)
        {
            // Ensure decimal parsing handles both culture-specific formats
            string assemblyQtyText = txtAssemblyQty.Text.Replace(',', '.');

            if (!decimal.TryParse(assemblyQtyText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal newAssemblyQty) || newAssemblyQty <= 0)
            {
                MessageBox.Show("Please enter a valid assembly quantity greater than 0.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _updatingEntries = true;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    lock (_updateLock)
                    {
                        foreach (var entry in _entries)
                        {
                            // Calculate with decimal precision and then ceiling
                            entry.QuantityTotal = (int)Math.Ceiling(entry.QuantityForOne * newAssemblyQty);
                            UpdateEntryQuantities(entry);
                        }
                    }
                });
                dgInventory.Items.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating quantities: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _updatingEntries = false;
            }
        }

        private void UpdateEntryQuantities(BomEntry entry)
        {
            if (_updatingEntries) return;

            try
            {
                lock (_updateLock)
                {
                    // Calculate required quantity considering stock
                    entry.AdjustedOrderQuantity = Math.Max(0, entry.QuantityTotal - entry.StockQuantity);

                    if (entry.AdjustedOrderQuantity > 0)
                    {
                        // Calculate DigiKey minimum order quantity
                        if (entry.DigiKeyData?.IsAvailable ?? false)
                        {
                            int digiKeyMinQty = QuantityCalculator.CalculateMinimumOrderQuantity(
                                entry.DigiKeyCurrentUnitPrice,
                                entry.AdjustedOrderQuantity
                            );
                            entry.DigiKeyOrderQuantity = digiKeyMinQty;
                        }
                        else
                        {
                            entry.DigiKeyOrderQuantity = entry.AdjustedOrderQuantity;
                        }

                        // Calculate Mouser minimum order quantity
                        if (entry.MouserData?.IsAvailable ?? false)
                        {
                            int mouserMinQty = QuantityCalculator.CalculateMinimumOrderQuantity(
                                entry.MouserCurrentUnitPrice,
                                entry.AdjustedOrderQuantity
                            );
                            entry.MouserOrderQuantity = mouserMinQty;
                        }
                        else
                        {
                            entry.MouserOrderQuantity = entry.AdjustedOrderQuantity;
                        }

                        // Calculate Farnell minimum order quantity - ADD THIS CODE
                        if (entry.FarnellData?.IsAvailable ?? false)
                        {
                            int farnellMinQty = QuantityCalculator.CalculateMinimumOrderQuantity(
                                entry.FarnellCurrentUnitPrice,
                                entry.AdjustedOrderQuantity
                            );
                            entry.FarnellOrderQuantity = farnellMinQty;
                        }
                        else
                        {
                            entry.FarnellOrderQuantity = entry.AdjustedOrderQuantity;
                        }
                    }
                    else
                    {
                        entry.DigiKeyOrderQuantity = 0;
                        entry.MouserOrderQuantity = 0;
                        entry.FarnellOrderQuantity = 0; // ADD THIS LINE
                    }

                    // Update prices based on new quantities
                    if (entry.DigiKeyData != null)
                    {
                        var digiKeyPricing = entry.DigiKeyData.GetPriceForQuantity(entry.DigiKeyOrderQuantity);
                        entry.DigiKeyCurrentUnitPrice = digiKeyPricing.currentPrice;
                        entry.DigiKeyCurrentTotalPrice = digiKeyPricing.currentPrice * entry.DigiKeyOrderQuantity;
                        entry.DigiKeyNextBreakQty = digiKeyPricing.nextBreakQuantity;
                        entry.DigiKeyNextBreakUnitPrice = digiKeyPricing.nextBreakPrice;
                        entry.DigiKeyNextBreakTotalPrice = digiKeyPricing.nextBreakPrice * entry.DigiKeyOrderQuantity;
                    }

                    if (entry.MouserData != null)
                    {
                        var mouserPricing = entry.MouserData.GetPriceForQuantity(entry.MouserOrderQuantity);
                        entry.MouserCurrentUnitPrice = mouserPricing.currentPrice;
                        entry.MouserCurrentTotalPrice = mouserPricing.currentPrice * entry.MouserOrderQuantity;
                        entry.MouserNextBreakQty = mouserPricing.nextBreakQuantity;
                        entry.MouserNextBreakUnitPrice = mouserPricing.nextBreakPrice;
                        entry.MouserNextBreakTotalPrice = mouserPricing.nextBreakPrice * entry.MouserOrderQuantity;
                    }

                    // Add Farnell pricing update - ADD THIS CODE
                    if (entry.FarnellData != null)
                    {
                        var farnellPricing = entry.FarnellData.GetPriceForQuantity(entry.FarnellOrderQuantity);
                        entry.FarnellCurrentUnitPrice = farnellPricing.currentPrice;
                        entry.FarnellCurrentTotalPrice = farnellPricing.currentPrice * entry.FarnellOrderQuantity;
                        entry.FarnellNextBreakQty = farnellPricing.nextBreakQuantity;
                        entry.FarnellNextBreakUnitPrice = farnellPricing.nextBreakPrice;
                        entry.FarnellNextBreakTotalPrice = farnellPricing.nextBreakPrice * entry.FarnellOrderQuantity;
                    }

                    // Update best supplier information
                    UpdateBestSupplierInfo(entry);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating quantities: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateBestSupplierInfo(BomEntry entry)
        {
            bool digiKeyAvailable = entry.DigiKeyData?.IsAvailable ?? false;
            bool mouserAvailable = entry.MouserData?.IsAvailable ?? false;
            bool farnellAvailable = entry.FarnellData?.IsAvailable ?? false; // ADD THIS LINE

            decimal bestPrice = decimal.MaxValue;
            string bestSupplier = "N/A";

            // Check DigiKey
            if (digiKeyAvailable && entry.DigiKeyCurrentTotalPrice > 0 && entry.DigiKeyCurrentTotalPrice < bestPrice)
            {
                bestPrice = entry.DigiKeyCurrentTotalPrice;
                bestSupplier = "DigiKey";
            }

            // Check Mouser
            if (mouserAvailable && entry.MouserCurrentTotalPrice > 0 && entry.MouserCurrentTotalPrice < bestPrice)
            {
                bestPrice = entry.MouserCurrentTotalPrice;
                bestSupplier = "Mouser";
            }

            // Check Farnell - ADD THIS BLOCK
            if (farnellAvailable && entry.FarnellCurrentTotalPrice > 0 && entry.FarnellCurrentTotalPrice < bestPrice)
            {
                bestPrice = entry.FarnellCurrentTotalPrice;
                bestSupplier = "Farnell";
            }

            entry.BestCurrentSupplier = bestSupplier;
            entry.CurrentTotalPrice = bestPrice == decimal.MaxValue ? 0 : bestPrice;

            // Set current unit price based on best supplier
            if (bestSupplier == "DigiKey")
            {
                entry.CurrentUnitPrice = entry.DigiKeyCurrentUnitPrice;
            }
            else if (bestSupplier == "Mouser")
            {
                entry.CurrentUnitPrice = entry.MouserCurrentUnitPrice;
            }
            else if (bestSupplier == "Farnell") // ADD THIS BLOCK
            {
                entry.CurrentUnitPrice = entry.FarnellCurrentUnitPrice;
            }
            else
            {
                entry.CurrentUnitPrice = 0;
            }

            // Similar logic for best next break supplier
            bestPrice = decimal.MaxValue;
            bestSupplier = "N/A";

            if (digiKeyAvailable && entry.DigiKeyNextBreakTotalPrice > 0 && entry.DigiKeyNextBreakTotalPrice < bestPrice)
            {
                bestPrice = entry.DigiKeyNextBreakTotalPrice;
                bestSupplier = "DigiKey";
            }

            if (mouserAvailable && entry.MouserNextBreakTotalPrice > 0 && entry.MouserNextBreakTotalPrice < bestPrice)
            {
                bestPrice = entry.MouserNextBreakTotalPrice;
                bestSupplier = "Mouser";
            }

            if (farnellAvailable && entry.FarnellNextBreakTotalPrice > 0 && entry.FarnellNextBreakTotalPrice < bestPrice) // ADD THIS BLOCK
            {
                bestPrice = entry.FarnellNextBreakTotalPrice;
                bestSupplier = "Farnell";
            }

            entry.BestNextBreakSupplier = bestSupplier;
        }
    }
}