using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Diagnostics;
using BOMVIEW.Models;
using BOMVIEW.Services;
using System.ComponentModel;

namespace BOMVIEW
{
    public partial class ExternalSupplierManager : Window
    {
        private readonly ExternalSupplierService _externalSupplierService;
        private readonly MainWindow _mainWindow;
        private ICollectionView _supplierView;

        public ExternalSupplierManager(MainWindow mainWindow, ExternalSupplierService externalSupplierService)
        {
            InitializeComponent();

            _mainWindow = mainWindow;
            _externalSupplierService = externalSupplierService;

            // Set the DataContext
            DataContext = this;

            // Initialize the view
            InitializeDataView();
        }

        private void InitializeDataView()
        {
            try
            {
                // Set the ItemsSource of the DataGrid
                ExternalSuppliersGrid.ItemsSource = _externalSupplierService.ExternalSupplierEntries;

                // Create a collection view for filtering and sorting
                _supplierView = CollectionViewSource.GetDefaultView(_externalSupplierService.ExternalSupplierEntries);

                // Apply initial sort by date added (descending)
                _supplierView.SortDescriptions.Add(new System.ComponentModel.SortDescription("DateAdded", System.ComponentModel.ListSortDirection.Descending));

                // Refresh the view
                _supplierView.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing data view: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void OpenUrlButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                var entry = button?.DataContext as ExternalSupplierEntry;

                if (entry != null && !string.IsNullOrWhiteSpace(entry.SupplierUrl))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = entry.SupplierUrl,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error opening URL: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                var entry = button?.DataContext as ExternalSupplierEntry;

                if (entry != null)
                {
                    var dialog = new ExternalSupplierDialog(entry)
                    {
                        Owner = this
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        // Update the entry
                        _externalSupplierService.UpdateExternalSupplierEntry(dialog.ExternalSupplierEntry);

                        // Refresh the view
                        _supplierView.Refresh();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error editing entry: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                var entry = button?.DataContext as ExternalSupplierEntry;

                if (entry != null)
                {
                    var result = MessageBox.Show(
                        $"Are you sure you want to remove the external supplier for {entry.OrderingCode}?\n\nThis will mark the part as missing again in your BOM.",
                        "Confirm Removal",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        // Remove the entry
                        _externalSupplierService.RemoveExternalSupplierEntry(entry.OriginalBomEntryNum);

                        // Update the main window
                        _mainWindow.RefreshAfterExternalSupplierChange(entry.OriginalBomEntryNum);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error removing entry: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}