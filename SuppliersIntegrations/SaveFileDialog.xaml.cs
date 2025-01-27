using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace BOMVIEW
{
    public partial class SaveFileDialog : Window
    {
        public string SelectedFilePath { get; private set; }
        public bool OpenAfterSave => chkOpenAfterSave.IsChecked ?? false;
        private readonly string _originalFilePath;

        public SaveFileDialog(string originalFilePath = null)
        {
            InitializeComponent();
            _originalFilePath = originalFilePath;

            if (!string.IsNullOrEmpty(_originalFilePath))
            {
                string directory = Path.GetDirectoryName(_originalFilePath);
                string originalFileName = Path.GetFileNameWithoutExtension(_originalFilePath);
                string suggestedFileName = $"{originalFileName}_BOMVIEW.xlsx";
                SelectedFilePath = Path.Combine(directory, suggestedFileName);
                txtFileName.Text = suggestedFileName;
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel Files|*.xlsx",
                Title = "Save BOM Files",
                FileName = txtFileName.Text
            };

            if (dialog.ShowDialog() == true)
            {
                SelectedFilePath = dialog.FileName;
                txtFileName.Text = Path.GetFileName(SelectedFilePath);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(SelectedFilePath))
            {
                MessageBox.Show("Please select a file location", "Error",
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
    }
}