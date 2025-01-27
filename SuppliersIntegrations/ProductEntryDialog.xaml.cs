using System;
using System.Windows;
using System.Windows.Input;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.ComponentModel;
using BOMVIEW.Models;

namespace BOMVIEW
{
    public partial class ProductEntryDialog : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public BomEntry Result { get; private set; }
        private BomEntry _existingEntry;

        public ProductEntryDialog(BomEntry existingEntry = null)
        {
            InitializeComponent();
            DataContext = this;
            _existingEntry = existingEntry;

            if (existingEntry != null)
            {
                // Populate fields for editing
                txtOrderingCode.Text = existingEntry.OrderingCode;
                txtDesignator.Text = existingEntry.Designator;
                txtValue.Text = existingEntry.Value;
                txtPcbFootprint.Text = existingEntry.PcbFootprint;
                txtQuantityOne.Text = existingEntry.QuantityForOne.ToString();
                Title = "Edit Product";
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
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

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(txtOrderingCode?.Text))
                {
                    MessageBox.Show("Ordering Code is required", "Validation Error",
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Improved decimal parsing with culture handling
                string qtyText = txtQuantityOne?.Text?.Replace(',', '.') ?? "";
                if (!decimal.TryParse(qtyText, System.Globalization.NumberStyles.Any,
                                     System.Globalization.CultureInfo.InvariantCulture, out decimal qty) || qty <= 0)
                {
                    MessageBox.Show("Please enter a valid quantity", "Validation Error",
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Result = _existingEntry ?? new BomEntry();

                // Set values and mark each field as user-entered if it's been modified
                if (_existingEntry == null || txtOrderingCode.Text != _existingEntry.OrderingCode)
                {
                    Result.OrderingCode = txtOrderingCode.Text;
                    Result.IsUserEntered = true;
                }

                if (_existingEntry == null || txtDesignator.Text != _existingEntry.Designator)
                {
                    Result.Designator = txtDesignator?.Text ?? "";
                    Result.IsUserEntered = true;
                }

                if (_existingEntry == null || txtValue.Text != _existingEntry.Value)
                {
                    Result.Value = txtValue?.Text ?? "";
                    Result.IsUserEntered = true;
                }

                if (_existingEntry == null || txtPcbFootprint.Text != _existingEntry.PcbFootprint)
                {
                    Result.PcbFootprint = txtPcbFootprint?.Text ?? "";
                    Result.IsUserEntered = true;
                }

                if (_existingEntry == null || qty != _existingEntry.QuantityForOne)
                {
                    Result.QuantityForOne = (int)qty;  // Store the base value as an integer
                    Result.QuantityTotal = (int)Math.Ceiling(qty);  // Round up for total quantity
                    Result.IsUserEntered = true;
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving product: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}