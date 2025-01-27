using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BOMVIEW.Models;

namespace BOMVIEW
{
    public partial class ExternalSupplierDialog : Window
    {
        private ExternalSupplierEntry _externalSupplierEntry;

        public ExternalSupplierEntry ExternalSupplierEntry => _externalSupplierEntry;

        public ExternalSupplierDialog(BomEntry bomEntry)
        {
            InitializeComponent();

            // Create a new ExternalSupplierEntry from the BomEntry
            _externalSupplierEntry = ExternalSupplierEntry.FromBomEntry(bomEntry);

            // Set default delivery date to today
            _externalSupplierEntry.EstimatedDeliveryDate = DateTime.Today;

            // Set the DataContext for binding
            DataContext = _externalSupplierEntry;
        }

        public ExternalSupplierDialog(ExternalSupplierEntry existingEntry)
        {
            InitializeComponent();

            // Use the existing ExternalSupplierEntry
            _externalSupplierEntry = existingEntry;

            // Set the DataContext for binding
            DataContext = _externalSupplierEntry;

            // Change the title to reflect that we're editing
            Title = "Edit External Supplier";
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Basic validation
            if (string.IsNullOrWhiteSpace(_externalSupplierEntry.SupplierName))
            {
                MessageBox.Show("Supplier name is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Validate Availability (Stock)
            if (_externalSupplierEntry.Availability <= 0)
            {
                MessageBox.Show("You must enter a quantity. If you do not know what the quantity is, enter 1.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Calculate total price if not already set
            if (_externalSupplierEntry.TotalPrice == 0 && _externalSupplierEntry.UnitPrice > 0)
            {
                _externalSupplierEntry.TotalPrice = _externalSupplierEntry.UnitPrice * _externalSupplierEntry.QuantityTotal;
            }

            // Set the dialog result to true (OK)
            DialogResult = true;
        }
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Set the dialog result to false (Cancel)
            DialogResult = false;
        }

        private void ClearDateButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear the estimated delivery date
            if (_externalSupplierEntry != null)
            {
                _externalSupplierEntry.EstimatedDeliveryDate = null;
            }
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            // Allow only digits and one decimal point
            if (!char.IsDigit(e.Text, 0) && e.Text != "." && e.Text != ",")
            {
                e.Handled = true;
                return;
            }

            // If the text is a decimal point, check if there's already one
            if (e.Text == "." || e.Text == ",")
            {
                TextBox textBox = sender as TextBox;
                if (textBox != null)
                {
                    e.Handled = textBox.Text.Contains(".") || textBox.Text.Contains(",");
                }
            }
        }

        private void PositiveIntegerValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            // Allow only digits
            if (!char.IsDigit(e.Text, 0))
            {
                e.Handled = true;
            }
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            // Open the URL in the default browser
            if (!string.IsNullOrEmpty(e.Uri.AbsoluteUri))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = e.Uri.AbsoluteUri,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error opening URL: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            e.Handled = true;
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            // Set minimal default values
            _externalSupplierEntry.SupplierName = $"External: {_externalSupplierEntry.OrderingCode}";
            _externalSupplierEntry.UnitPrice = 0;
            _externalSupplierEntry.Availability = 1;
            _externalSupplierEntry.DateAdded = DateTime.Now;

            // Set the dialog result to true (OK) - same as if we saved normally
            DialogResult = true;
        }
    }
}