using BOMVIEW.Interfaces;
using BOMVIEW.OpenBOM.Models;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace BOMVIEW
{
    public partial class OpenBomListDialog : Window
    {
        private readonly ILogger _logger;
        private readonly OpenBomService _openBomService;

        public string PromptText { get; set; }
        public ObservableCollection<OpenBomListItem> FilteredItems { get; private set; }
        public OpenBomListItem SelectedItem => ItemsListBox.SelectedItem as OpenBomListItem;

        public OpenBomListDialog(ILogger logger, OpenBomService openBomService, string promptText)
        {
            InitializeComponent();

            _logger = logger;
            _openBomService = openBomService;
            PromptText = promptText;

            FilteredItems = new ObservableCollection<OpenBomListItem>();

            DataContext = this;
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Real-time filtering can be implemented here if needed
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string searchTerm = SearchTextBox.Text?.Trim();
                if (string.IsNullOrEmpty(searchTerm))
                {
                    MessageBox.Show("Please enter a search term", "Search Required",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                BtnSearch.IsEnabled = false;
                FilteredItems.Clear();

                // Get all BOMs
                var items = await _openBomService.ListBomsAsync();

                // Filter items
                foreach (var item in items)
                {
                    if (item.MatchesSearch(searchTerm))
                    {
                        FilteredItems.Add(item);
                    }
                }

                if (FilteredItems.Count == 0)
                {
                    MessageBox.Show("No items found matching your search", "No Results",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error searching OpenBOM items: {ex.Message}");
                MessageBox.Show($"Error searching: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnSearch.IsEnabled = true;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (ItemsListBox.SelectedItem == null)
            {
                MessageBox.Show("Please select an item", "Selection Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }
    }
}