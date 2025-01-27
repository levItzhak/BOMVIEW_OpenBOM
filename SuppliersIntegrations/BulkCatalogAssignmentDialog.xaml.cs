using BOMVIEW.Controls;
using BOMVIEW.Interfaces;
using BOMVIEW.Models;
using BOMVIEW.OpenBOM.Models;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace BOMVIEW
{
    public partial class BulkCatalogAssignmentDialog : Window
    {
        private readonly ILogger _logger;
        private readonly DigiKeyService _digiKeyService;
        private readonly RateLimitedOpenBomService _openBomService;
        private readonly List<OpenBomListItem> _availableCatalogs;

        // ViewModel for parts that need catalog assignment
        public class PartAssignmentItem : INotifyPropertyChanged
        {
            private string _partNumber;
            private string _description;
            private OpenBomListItem _selectedCatalog;
            private ObservableCollection<OpenBomListItem> _suggestedCatalogs;
            private BomEntry _originalPart;

            public string PartNumber
            {
                get => _partNumber;
                set { _partNumber = value; OnPropertyChanged(); }
            }

            public string Description
            {
                get => _description;
                set { _description = value; OnPropertyChanged(); }
            }

            public OpenBomListItem SelectedCatalog
            {
                get => _selectedCatalog;
                set { _selectedCatalog = value; OnPropertyChanged(); }
            }

            public ObservableCollection<OpenBomListItem> SuggestedCatalogs
            {
                get => _suggestedCatalogs;
                set { _suggestedCatalogs = value; OnPropertyChanged(); }
            }

            public BomEntry OriginalPart { get; set; }

            // Implement INotifyPropertyChanged interface
            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public ObservableCollection<PartAssignmentItem> PartsToAssign { get; private set; }

        public BulkCatalogAssignmentDialog(ILogger logger, DigiKeyService digiKeyService,
            RateLimitedOpenBomService openBomService, List<BomEntry> parts, List<OpenBomListItem> availableCatalogs)
        {
            InitializeComponent();

            _logger = logger;
            _digiKeyService = digiKeyService;
            _openBomService = openBomService;
            _availableCatalogs = availableCatalogs;

            PartsToAssign = new ObservableCollection<PartAssignmentItem>();

            // Initialize with the parts passed in
            InitializePartsList(parts);

            // Set DataContext
            DataContext = this;
        }

        private async void InitializePartsList(List<BomEntry> parts)
        {
            try
            {
                LoadingOverlay.Visibility = Visibility.Visible;
                LoadingText.Text = "Processing parts...";

                foreach (var part in parts)
                {
                    var item = new PartAssignmentItem
                    {
                        PartNumber = part.OrderingCode,
                        Description = part.Value ?? "",
                        OriginalPart = part,
                        SuggestedCatalogs = new ObservableCollection<OpenBomListItem>(_availableCatalogs)
                    };

                    // Set the first catalog as default
                    if (_availableCatalogs.Count > 0)
                    {
                        item.SelectedCatalog = _availableCatalogs[0];
                    }

                    PartsToAssign.Add(item);

                    // Try to get DigiKey data in the background for a better suggestion
                    Task.Run(async () =>
                    {
                        await GetDigiKeySuggestionForPart(item);
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error initializing parts: {ex.Message}");
                MessageBox.Show($"Error initializing parts: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async Task GetDigiKeySuggestionForPart(PartAssignmentItem item)
        {
            try
            {
                var digiKeyData = await _digiKeyService.GetPriceAndAvailabilityAsync(item.PartNumber);

                if (digiKeyData != null && digiKeyData.IsAvailable)
                {
                    // Extract category info from DigiKey
                    string category = digiKeyData.Category ?? "";

                    // Get the best catalog match based on the category
                    await Dispatcher.InvokeAsync(() =>
                    {
                        var suggestionsList = new List<OpenBomListItem>(_availableCatalogs);

                        // Try to find a matching catalog based on the category
                        var bestMatch = FindBestCatalogMatch(category, suggestionsList);

                        if (bestMatch != null)
                        {
                            // Move the best match to the top of the list
                            suggestionsList.Remove(bestMatch);
                            suggestionsList.Insert(0, bestMatch);

                            // Update the item with the new list and selected catalog
                            item.SuggestedCatalogs = new ObservableCollection<OpenBomListItem>(suggestionsList);
                            item.SelectedCatalog = bestMatch;

                            // Update description from DigiKey data
                            if (!string.IsNullOrEmpty(digiKeyData.Description))
                            {
                                item.Description = digiKeyData.Description;
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error getting DigiKey suggestion for {item.PartNumber}: {ex.Message}");
                // Continue without a suggestion
            }
        }

        private OpenBomListItem FindBestCatalogMatch(string category, List<OpenBomListItem> catalogs)
        {
            // Simple string matching between DigiKey category and catalog names
            // Could be made more sophisticated with ML or better matching algorithms

            if (string.IsNullOrEmpty(category))
                return null;

            category = category.ToLower();

            // Look for exact matches first
            foreach (var catalog in catalogs)
            {
                if (catalog.Name.ToLower().Contains(category) ||
                    category.Contains(catalog.Name.ToLower()))
                {
                    return catalog;
                }
            }

            // Category keywords to look for
            var keywordMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "resistor", new List<string> { "resistor", "resistance" } },
                { "capacitor", new List<string> { "capacitor", "cap", "bypass" } },
                { "inductor", new List<string> { "inductor", "coil", "choke" } },
                { "connector", new List<string> { "connector", "terminal", "header" } },
                { "integrated circuit", new List<string> { "ic", "mcu", "processor", "logic" } },
                { "transistor", new List<string> { "transistor", "mosfet", "fet", "bjt" } },
                { "diode", new List<string> { "diode", "rectifier", "zener" } }
            };

            foreach (var keyword in keywordMap)
            {
                if (keyword.Value.Any(k => category.Contains(k)))
                {
                    // Look for a catalog that matches this keyword
                    foreach (var catalog in catalogs)
                    {
                        if (catalog.Name.ToLower().Contains(keyword.Key))
                        {
                            return catalog;
                        }
                    }
                }
            }

            // If no matches found, return a general catalog if one exists
            return catalogs.FirstOrDefault(c => c.Name.Contains("General") || c.Name.Contains("Component"));
        }

        private void AssignAllButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}