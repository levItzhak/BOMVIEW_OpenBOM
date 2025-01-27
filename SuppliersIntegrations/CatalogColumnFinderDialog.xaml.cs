using BOMVIEW.Interfaces;
using BOMVIEW.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BOMVIEW
{
    /// <summary>
    /// Interaction logic for CatalogColumnFinderDialog.xaml
    /// </summary>
    public partial class CatalogColumnFinderDialog : Window
    {
        private readonly ILogger _logger;
        private readonly CatalogColumnFinder _columnFinder;
        private bool _isSearching = false;

        public ObservableCollection<CatalogColumnResult> Results { get; private set; } = new ObservableCollection<CatalogColumnResult>();

        public CatalogColumnFinderDialog(ILogger logger, RateLimitedOpenBomService openBomService)
        {
            InitializeComponent();
            _logger = logger;
            _columnFinder = new CatalogColumnFinder(logger, openBomService);
            DataContext = this;
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isSearching)
                return;

            string columnName = ColumnNameTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(columnName))
            {
                MessageBox.Show("Please enter a column name to search for", "Input Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _isSearching = true;
                SearchButton.IsEnabled = false;
                ResultsGrid.Visibility = Visibility.Collapsed;
                LoadingIndicator.Visibility = Visibility.Visible;
                StatusText.Text = "Searching catalogs...";

                // Clear previous results
                Results.Clear();

                // Perform the search
                bool caseSensitive = CaseSensitiveCheckbox.IsChecked ?? false;
                var results = await _columnFinder.FindCatalogsWithColumnAsync(columnName, caseSensitive);

                // Update UI with results
                foreach (var result in results)
                {
                    Results.Add(result);
                }

                StatusText.Text = $"Found {results.Count} catalogs containing column '{columnName}'";
                ResultsGrid.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error searching for column: {ex.Message}");
                MessageBox.Show($"Error searching for catalogs: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Search failed";
            }
            finally
            {
                LoadingIndicator.Visibility = Visibility.Collapsed;
                SearchButton.IsEnabled = true;
                _isSearching = false;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}