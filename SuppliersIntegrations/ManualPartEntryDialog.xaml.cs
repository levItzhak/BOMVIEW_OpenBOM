// Create a new file called ManualPartEntryDialog.xaml.cs
using BOMVIEW.Interfaces;
using BOMVIEW.OpenBOM.Models;
using System;
using System.Collections.Generic;
using System.Windows;

namespace BOMVIEW
{
    public partial class ManualPartEntryDialog : Window
    {
        private readonly ILogger _logger;

        public string PartNumber { get; set; }
        public OpenBomPartRequest PartInfo { get; private set; }

        public ManualPartEntryDialog(ILogger logger, string partNumber)
        {
            InitializeComponent();

            _logger = logger;
            PartNumber = partNumber;

            DataContext = this;
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate required fields
                if (string.IsNullOrWhiteSpace(VendorTextBox.Text) ||
                    string.IsNullOrWhiteSpace(DescriptionTextBox.Text))
                {
                    MessageBox.Show("Vendor and Description are required fields.", "Missing Information",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Create part info dictionary
                var properties = new Dictionary<string, string>
                {
                    { "Part Number", PartNumber },
                    { "Vendor", VendorTextBox.Text },
                    { "Description", DescriptionTextBox.Text }
                };

                // Add optional fields if provided
                if (!string.IsNullOrWhiteSpace(ManufacturerTextBox.Text))
                    properties.Add("Manufacturer", ManufacturerTextBox.Text);

                if (!string.IsNullOrWhiteSpace(ManufacturerPartTextBox.Text))
                    properties.Add("Manufacturer Part Number", ManufacturerPartTextBox.Text);

                if (!string.IsNullOrWhiteSpace(CostTextBox.Text))
                {
                    if (decimal.TryParse(CostTextBox.Text, out decimal cost))
                    {
                        properties.Add("Cost", cost.ToString());
                    }
                    else
                    {
                        MessageBox.Show("Cost must be a valid number.", "Invalid Input",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                if (!string.IsNullOrWhiteSpace(LeadTimeTextBox.Text))
                    properties.Add("Lead Time", LeadTimeTextBox.Text);

                if (!string.IsNullOrWhiteSpace(LinkTextBox.Text))
                    properties.Add("Link", LinkTextBox.Text);

                if (!string.IsNullOrWhiteSpace(DatasheetTextBox.Text))
                    properties.Add("Datasheet", DatasheetTextBox.Text);

                // Create part request
                PartInfo = new OpenBomPartRequest
                {
                    PartNumber = PartNumber,
                    Properties = properties
                };

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving part info: {ex.Message}");
                MessageBox.Show($"Error saving part info: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}