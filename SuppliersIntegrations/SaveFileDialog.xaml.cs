using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Controls;

namespace BOMVIEW
{
    public partial class SaveFileDialog : Window
    {
        public string SelectedFilePath { get; private set; }
        public bool OpenAfterSave => chkOpenAfterSave.IsChecked ?? false;
        private readonly string _originalFilePath;

        // Collection of file types that can be selected for saving
        public ObservableCollection<SaveFileOption> SaveOptions { get; private set; }

        public SaveFileDialog(string originalFilePath = null)
        {
            InitializeComponent();
            _originalFilePath = originalFilePath;

            // Initialize the save options collection
            SaveOptions = new ObservableCollection<SaveFileOption>
            {
                new SaveFileOption { Name = "Main BOM with prices", Key = "MainBom", IsSelected = true, Description = "All BOM data with pricing information" },
                new SaveFileOption { Name = "DigiKey List", Key = "DigiKeyList", IsSelected = true, Description = "Complete parts list for ordering from DigiKey" },
                new SaveFileOption { Name = "DigiKey Best Prices", Key = "DigiKeyBestPrices", IsSelected = true, Description = "Optimized parts list for DigiKey with best pricing" },
                new SaveFileOption { Name = "Mouser List", Key = "MouserList", IsSelected = true, Description = "Complete parts list for ordering from Mouser" },
                new SaveFileOption { Name = "Mouser Best Prices", Key = "MouserBestPrices", IsSelected = true, Description = "Optimized parts list for Mouser with best pricing" },
                new SaveFileOption { Name = "Farnell List", Key = "FarnellList", IsSelected = true, Description = "Complete parts list for ordering from Farnell" },
                new SaveFileOption { Name = "Farnell Best Prices", Key = "FarnellBestPrices", IsSelected = true, Description = "Optimized parts list for Farnell with best pricing" },
                new SaveFileOption { Name = "External Suppliers", Key = "ExternalSuppliers", IsSelected = true, Description = "List of parts from external suppliers" },
                new SaveFileOption { Name = "Missing Parts", Key = "MissingParts", IsSelected = true, Description = "List of parts that are not available from any supplier" }
            };

            // Bind the save options to the list view
            listFilesToSave.ItemsSource = SaveOptions;

            if (!string.IsNullOrEmpty(_originalFilePath))
            {
                string directory = Path.GetDirectoryName(_originalFilePath);
                string originalFileName = Path.GetFileNameWithoutExtension(_originalFilePath);
                string suggestedFileName = $"{originalFileName}_BOMVIEW.xlsx";
                SelectedFilePath = Path.Combine(directory, suggestedFileName);
                txtFileName.Text = suggestedFileName;
            }

            DataContext = this;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            // Start with the current directory if available, otherwise use the original file directory
            string initialDirectory = null;

            if (!string.IsNullOrEmpty(SelectedFilePath))
            {
                initialDirectory = Path.GetDirectoryName(SelectedFilePath);
            }
            else if (!string.IsNullOrEmpty(_originalFilePath))
            {
                initialDirectory = Path.GetDirectoryName(_originalFilePath);
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel Files|*.xlsx",
                Title = "Save BOM Files",
                FileName = txtFileName.Text,
                InitialDirectory = initialDirectory
            };

            if (dialog.ShowDialog() == true)
            {
                SelectedFilePath = dialog.FileName;
                txtFileName.Text = Path.GetFileName(SelectedFilePath);
            }
        }

        private void TxtFileName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtFileName.Text))
                return;

            try
            {
                // Only update the path if it was already set
                if (!string.IsNullOrEmpty(SelectedFilePath))
                {
                    string directory = Path.GetDirectoryName(SelectedFilePath);
                    string newFileName = txtFileName.Text;

                    // Ensure it has the proper extension
                    if (!newFileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                    {
                        newFileName += ".xlsx";
                    }

                    // Update the selected path with the new filename
                    SelectedFilePath = Path.Combine(directory, newFileName);
                }
            }
            catch (Exception ex)
            {
                // Just log the error but don't show it to avoid disrupting the UI
                // In a real app, you might want to disable the Save button or show an indicator
                Console.WriteLine($"Error updating file path: {ex.Message}");
            }
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            // We need to modify each item individually to trigger property change notifications
            foreach (var option in SaveOptions)
            {
                option.IsSelected = true;
            }

            // Force refresh the list view
            listFilesToSave.Items.Refresh();
        }

        private void UnselectAllButton_Click(object sender, RoutedEventArgs e)
        {
            // We need to modify each item individually to trigger property change notifications
            foreach (var option in SaveOptions)
            {
                option.IsSelected = false;
            }

            // Force refresh the list view
            listFilesToSave.Items.Refresh();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Ensure the filename is valid and update the path
            if (string.IsNullOrWhiteSpace(txtFileName.Text))
            {
                MessageBox.Show("Please enter a valid filename", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                txtFileName.Focus();
                return;
            }

            // Update the file path with the current filename text
            try
            {
                string directory;

                if (!string.IsNullOrEmpty(SelectedFilePath))
                {
                    directory = Path.GetDirectoryName(SelectedFilePath);
                }
                else if (!string.IsNullOrEmpty(_originalFilePath))
                {
                    directory = Path.GetDirectoryName(_originalFilePath);
                }
                else
                {
                    directory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                }

                string filename = txtFileName.Text;

                // Ensure it has the proper extension
                if (!filename.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    filename += ".xlsx";
                    txtFileName.Text = filename;
                }

                SelectedFilePath = Path.Combine(directory, filename);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Invalid filename: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Check if at least one option is selected
            bool anySelected = false;
            foreach (var option in SaveOptions)
            {
                if (option.IsSelected)
                {
                    anySelected = true;
                    break;
                }
            }

            if (!anySelected)
            {
                MessageBox.Show("Please select at least one file type to save", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Create directory for all files
                string directory = Path.GetDirectoryName(SelectedFilePath);
                string baseFileName = Path.GetFileNameWithoutExtension(SelectedFilePath);
                string bomFolderPath = Path.Combine(directory, baseFileName);

                // Create directory if it doesn't exist
                if (!Directory.Exists(bomFolderPath))
                {
                    Directory.CreateDirectory(bomFolderPath);
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error preparing save location: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // Helper method to get the selected save options
        public Dictionary<string, bool> GetSelectedOptions()
        {
            var options = new Dictionary<string, bool>();
            foreach (var option in SaveOptions)
            {
                options[option.Key] = option.IsSelected;
            }
            return options;
        }
    }

    // Class to represent a file option for saving
    public class SaveFileOption : INotifyPropertyChanged
    {
        private string _name;
        private string _key;
        private bool _isSelected;
        private string _description;

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }

        public string Key
        {
            get => _key;
            set
            {
                if (_key != value)
                {
                    _key = value;
                    OnPropertyChanged(nameof(Key));
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public string Description
        {
            get => _description;
            set
            {
                if (_description != value)
                {
                    _description = value;
                    OnPropertyChanged(nameof(Description));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}