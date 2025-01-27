// Create a new file called CatalogSelectionDialog.xaml.cs
using BOMVIEW.Interfaces;
using BOMVIEW.OpenBOM.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BOMVIEW
{
    public partial class CatalogSelectionDialog : Window
    {
        private readonly ILogger _logger;
        private readonly List<OpenBomListItem> _allCatalogs;

        public string PartNumber { get; set; }
        public ObservableCollection<OpenBomListItem> FilteredCatalogs { get; private set; }
        public OpenBomListItem SelectedCatalog => CatalogsListBox.SelectedItem as OpenBomListItem;

        public CatalogSelectionDialog(ILogger logger, List<OpenBomListItem> catalogs, string partNumber)
        {
            InitializeComponent();

            _logger = logger;
            _allCatalogs = catalogs;
            PartNumber = partNumber;

            FilteredCatalogs = new ObservableCollection<OpenBomListItem>(_allCatalogs);

            DataContext = this;
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterCatalogs();
        }

        private void FilterCatalogs()
        {
            string searchText = SearchTextBox.Text?.Trim().ToLower() ?? "";

            FilteredCatalogs.Clear();

            foreach (var catalog in _allCatalogs)
            {
                if (string.IsNullOrEmpty(searchText) ||
                    catalog.Name?.ToLower().Contains(searchText) == true ||
                    catalog.Id?.ToLower().Contains(searchText) == true)
                {
                    FilteredCatalogs.Add(catalog);
                }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (CatalogsListBox.SelectedItem == null)
            {
                MessageBox.Show("Please select a catalog", "Selection Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }
    }
}