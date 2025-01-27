using System;
using System.Windows;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Win32;
using System.IO;
using BOMVIEW.Services;
using BOMVIEW.Controls;
using BOMVIEW.Interfaces;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Windows.Controls;

namespace BOMVIEW
{
    public partial class BulkUpdateDialog : Window
    {
        private readonly ILogger _logger;
        private readonly BulkUpdateService _bulkUpdateService;
        private readonly LoadingOverlay _loadingOverlay;
        private List<UpdateResult> _updateResults;
        private readonly string _selectedCatalogId;

        public BulkUpdateDialog(
            ILogger logger,
            DigiKeyService digiKeyService,
            OpenBomService openBomService,
            string catalogId = null)
        {
            InitializeComponent();
            _logger = logger;
            _selectedCatalogId = catalogId;
            _bulkUpdateService = new BulkUpdateService(logger, digiKeyService, openBomService);
            _loadingOverlay = new LoadingOverlay();
            MainGrid.Children.Add(_loadingOverlay);

            _bulkUpdateService.ProgressUpdated += UpdateProgress;
            _loadingOverlay.CancelRequested += LoadingOverlay_CancelRequested;

            // Update UI based on mode
            UpdateModeText.Text = _selectedCatalogId == null
                ? "Update Mode: All Catalogs"
                : "Update Mode: Single Catalog";
        }

        private void LoadingOverlay_CancelRequested(object sender, EventArgs e)
        {
            _bulkUpdateService.CancelUpdate();
        }

        private void UpdateProgress(BulkUpdateProgress progress)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.Value = progress.ProgressPercentage;
                StatusText.Text = progress.CurrentOperation;
                UpdateCountText.Text = $"Processed: {progress.ProcessedProducts}/{progress.TotalProducts} | " +
                                     $"Updated: {progress.UpdatedProducts} | " +
                                     $"Failed: {progress.FailedProducts}";
            });
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StartButton.IsEnabled = false;
                _loadingOverlay.Show("Starting update process...");

                _updateResults = _selectedCatalogId == null
                    ? await _bulkUpdateService.UpdateAllCatalogsAsync()
                    : await _bulkUpdateService.UpdateCatalogAsync(_selectedCatalogId);

                await GenerateReport();

                MessageBox.Show(
                    $"Update complete. {_updateResults.Count} parts processed.\n\n" +
                    "An Excel report has been generated with the results.",
                    "Update Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (TaskCanceledException)
            {
                MessageBox.Show(
                    "Update process was cancelled.",
                    "Cancelled",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during bulk update: {ex.Message}");
                MessageBox.Show(
                    $"An error occurred during the update process:\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _loadingOverlay.Hide();
                StartButton.IsEnabled = true;
            }
        }

        private async Task GenerateReport()
        {
            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Excel Files (*.xlsx)|*.xlsx",
                    FileName = $"DigiKey_Update_Report_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                    DefaultExt = ".xlsx"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    _loadingOverlay.Show("Generating report...");
                    await _bulkUpdateService.GenerateReportAsync(saveDialog.FileName);
                    _logger.LogInfo($"Report generated successfully: {saveDialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error generating report: {ex.Message}");
                MessageBox.Show(
                    $"Error generating report: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}