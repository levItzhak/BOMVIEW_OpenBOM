using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BOMVIEW.Interfaces;
using BOMVIEW.Models;
using BOMVIEW.Services;
using System.Windows.Data;
using BOMVIEW.OpenBOM.Models;

namespace BOMVIEW.Views
{
    public partial class BomTreeView : UserControl
    {
        private OpenBomService _openBomService;
        private ObservableCollection<BomTreeNode> _topLevelItems;
        private ILogger _logger;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _loadingLocks;
        private bool _isCatalogView;
        private bool _isSearching;

        // New property for search loading state
        public bool IsSearching
        {
            get { return _isSearching; }
            set
            {
                _isSearching = value;
                // Update UI on property change
                if (SearchLoadingIndicator != null)
                {
                    SearchLoadingIndicator.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        // Events
        public event Action<string> OnItemSelected;
        public event Action<BomTreeNode> OnNodeSelected;

        public BomTreeView()
        {
            InitializeComponent();
            _topLevelItems = new ObservableCollection<BomTreeNode>();
            _loadingLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
            DataContext = this;


        }

        public void Initialize(ILogger logger)
        {
            _logger = logger;
            _openBomService = new OpenBomService(logger);
            LoadTopLevelItems();
        }

        public ObservableCollection<BomTreeNode> TopLevelItems => _topLevelItems;

        private async void LoadTopLevelItems()
        {
            try
            {
                // Add null check for _openBomService
                if (_openBomService == null)
                {
                    _logger?.LogError("OpenBomService is null, cannot load items");
                    return;
                }

                var items = _isCatalogView
                    ? await _openBomService.ListCatalogsAsync()
                    : await _openBomService.ListTopLevelBomsAsync();

                // Add null check for items
                if (items == null)
                {
                    _logger?.LogError("Received null items from OpenBomService");
                    items = new List<OpenBomListItem>(); // Use empty list instead of null
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    _topLevelItems.Clear();
                    foreach (var item in items)
                    {
                        // Add null check for item
                        if (item == null) continue;

                        var node = _isCatalogView
                            ? BomTreeNode.FromCatalogItem(item)
                            : BomTreeNode.FromBomItem(item);
                        _topLevelItems.Add(node);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error loading top-level items: {ex.Message}");
                MessageBox.Show($"Error loading items: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is not TreeViewItem item || item.DataContext is not BomTreeNode node)
                return;

            if (!node.HasUnloadedChildren || node.Children.Count > 0)
                return;

            try
            {
                _logger?.LogInfo($"Expanding node: {node.Name}");

                var semaphore = _loadingLocks.GetOrAdd(node.Id, _ => new SemaphoreSlim(1, 1));

                if (!await semaphore.WaitAsync(0))
                {
                    _logger?.LogInfo($"Node {node.Name} is already being loaded");
                    return;
                }

                try
                {
                    node.IsLoading = true;
                    var children = _isCatalogView
                        ? await _openBomService.GetCatalogHierarchyAsync(node.Id)
                        : await _openBomService.GetBomHierarchyAsync(node.Id);

                    await Dispatcher.InvokeAsync(() =>
                    {
                        node.Children.Clear();
                        foreach (var child in children)
                        {
                            node.Children.Add(child);
                        }
                        _logger?.LogInfo($"Loaded {children.Count} children for node: {node.Name}");
                    });
                }
                finally
                {
                    node.IsLoading = false;
                    node.HasUnloadedChildren = false;
                    semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error expanding node {node.Name}: {ex.Message}");
                MessageBox.Show($"Error loading items: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ViewSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewSelector.SelectedItem is ComboBoxItem item)
            {
                _isCatalogView = item.Content.ToString() == "Catalogs";
                LoadTopLevelItems();
            }
        }

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is BomTreeNode node)
            {
                OnItemSelected?.Invoke(node.Id);
                OnNodeSelected?.Invoke(node);
                _logger?.LogInfo($"Selected item: {node.Name}");
            }
        }

        private async void TreeSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var searchText = TreeSearchBox.Text?.ToLower() ?? "";

            // Show loading indicator for search
            IsSearching = true;

            // Add slight delay to avoid too many searches while typing
            await Task.Delay(700);

            // Check if text is still the same after delay (user might have typed more)
            string currentText = TreeSearchBox.Text?.ToLower() ?? "";
            if (searchText != currentText)
                return;

            await FilterTreeViewAsync(searchText);

            // Hide loading indicator when search is complete
            IsSearching = false;
        }

        private async Task FilterTreeViewAsync(string searchText)
        {
            await Task.Run(() =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (string.IsNullOrWhiteSpace(searchText))
                    {
                        foreach (BomTreeNode node in TopLevelItems)
                        {
                            SetNodeVisibility(node, true);
                        }
                        return;
                    }

                    foreach (BomTreeNode node in TopLevelItems)
                    {
                        bool isVisible = FilterNode(node, searchText);
                        SetNodeVisibility(node, isVisible);
                    }
                });
            });
        }

        private bool FilterNode(BomTreeNode node, string searchText)
        {
            if (node == null) return false;

            bool matchFound = node.Name?.ToLower().Contains(searchText) ?? false;
            matchFound |= node.PartNumber?.ToLower().Contains(searchText) ?? false;
            matchFound |= node.Description?.ToLower().Contains(searchText) ?? false;

            foreach (BomTreeNode child in node.Children)
            {
                matchFound |= FilterNode(child, searchText);
            }

            return matchFound;
        }

        private void SetNodeVisibility(BomTreeNode node, bool isVisible)
        {
            node.IsVisible = isVisible;
            foreach (BomTreeNode child in node.Children)
            {
                SetNodeVisibility(child, isVisible);
            }
        }

        private async void UpdateFromDigiKey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var menuItem = sender as MenuItem;
                var node = (menuItem?.DataContext as BomTreeNode) ??
                          ((menuItem?.Parent as ContextMenu)?.PlacementTarget as FrameworkElement)?.DataContext as BomTreeNode;

                if (node == null || string.IsNullOrEmpty(node.PartNumber))
                {
                    MessageBox.Show("No part number found.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dialog = new DigiKeyUpdateDialog(
                    node,
                    _logger,
                    new DigiKeyService(_logger, new ApiCredentials()),
                    _openBomService
                )
                {
                    Owner = Window.GetWindow(this)
                };

                if (dialog.ShowDialog() == true)
                {
                    // Refresh the node after update
                    await RefreshNode(node);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error updating from DigiKey: {ex.Message}");
                MessageBox.Show($"Error updating from DigiKey: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task RefreshNode(BomTreeNode node)
        {
            try
            {
                node.IsLoading = true;
                var parent = node.Parent as BomTreeNode;
                if (parent != null)
                {
                    var children = await _openBomService.GetCatalogHierarchyAsync(parent.Id);
                    var updatedNode = children.FirstOrDefault(c => c.PartNumber == node.PartNumber);
                    if (updatedNode != null)
                    {
                        node.Properties = updatedNode.Properties;
                        node.Description = updatedNode.Description;
                        node.Name = updatedNode.Name;
                    }
                }
            }
            finally
            {
                node.IsLoading = false;
            }
        }
    }
}