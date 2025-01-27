using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using BOMVIEW.Models;

namespace BOMVIEW
{
    public partial class DuplicateManagementDialog : Window, INotifyPropertyChanged
    {
        private readonly MainWindow _mainWindow;
        private readonly ObservableCollection<DuplicateGroup> _duplicateGroups;

        private int _duplicateGroupsCount;
        public int DuplicateGroupsCount
        {
            get => _duplicateGroupsCount;
            set
            {
                _duplicateGroupsCount = value;
                OnPropertyChanged();
            }
        }

        public class DuplicateItemViewModel : BomEntry
        {
            public string GroupId { get; set; }
            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }

            public DuplicateItemViewModel()
            {
                IsSelected = false;
            }

            public DuplicateItemViewModel(BomEntry source) : this()
            {
                OrderingCode = source.OrderingCode;
                Designator = source.Designator;
                Value = source.Value;
                PcbFootprint = source.PcbFootprint;
                QuantityForOne = source.QuantityForOne;
                QuantityTotal = source.QuantityTotal;
                DigiKeyData = source.DigiKeyData;
                MouserData = source.MouserData;
                Num = source.Num;
                IsDuplicate = source.IsDuplicate;
                DuplicateGroupId = source.DuplicateGroupId;
            }
        }

        public class DuplicateGroup : INotifyPropertyChanged
        {
            private string _groupId;
            private string _orderingCode;
            private ObservableCollection<DuplicateItemViewModel> _items;
            private bool _areAllSelected;

            public string GroupId
            {
                get => _groupId;
                set
                {
                    _groupId = value;
                    OnPropertyChanged();
                }
            }


            public string OrderingCode
            {
                get => _orderingCode;
                set
                {
                    _orderingCode = value;
                    OnPropertyChanged();
                }
            }

            public ObservableCollection<DuplicateItemViewModel> Items
            {
                get => _items;
                set
                {
                    _items = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Count));
                }
            }

            public int Count => Items?.Count ?? 0;

            public bool AreAllSelected
            {
                get => _areAllSelected;
                set
                {
                    _areAllSelected = value;
                    OnPropertyChanged();
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        // Add these event handlers to the DuplicateManagementDialog class
        private void SelectAll_Checked(object sender, RoutedEventArgs e)
        {
            var checkbox = sender as CheckBox;
            if (checkbox == null) return;

            string groupId = checkbox.Tag as string;
            if (string.IsNullOrEmpty(groupId)) return;

            var group = _duplicateGroups.FirstOrDefault(g => g.GroupId == groupId);
            if (group == null) return;

            foreach (var item in group.Items)
            {
                item.IsSelected = true;
            }
            group.AreAllSelected = true;
        }

        private void SelectAll_Unchecked(object sender, RoutedEventArgs e)
        {
            var checkbox = sender as CheckBox;
            if (checkbox == null) return;

            string groupId = checkbox.Tag as string;
            if (string.IsNullOrEmpty(groupId)) return;

            var group = _duplicateGroups.FirstOrDefault(g => g.GroupId == groupId);
            if (group == null) return;

            foreach (var item in group.Items)
            {
                item.IsSelected = false;
            }
            group.AreAllSelected = false;
        }
        

        public DuplicateManagementDialog(MainWindow mainWindow)
        {
            InitializeComponent();
            DataContext = this;
            _mainWindow = mainWindow;
            _duplicateGroups = new ObservableCollection<DuplicateGroup>();
            LoadDuplicateGroups();
            DuplicateGroups.ItemsSource = _duplicateGroups;
            UpdateDuplicateGroupsCount();
        }

        private void UpdateDuplicateGroupsCount()
        {
            DuplicateGroupsCount = _duplicateGroups.Count;
        }

        private void LoadDuplicateGroups()
        {
            _duplicateGroups.Clear();
            var groups = _mainWindow._bomEntries
                .Where(e => e.IsDuplicate)
                .GroupBy(e => e.DuplicateGroupId)
                .ToList();

            foreach (var group in groups)
            {
                var items = new ObservableCollection<DuplicateItemViewModel>();
                foreach (var entry in group)
                {
                    var duplicateItem = new DuplicateItemViewModel(entry)
                    {
                        GroupId = entry.DuplicateGroupId
                    };
                    items.Add(duplicateItem);
                }

                _duplicateGroups.Add(new DuplicateGroup
                {
                    GroupId = group.Key,
                    OrderingCode = group.First().OrderingCode,
                    Items = items
                });
            }
            UpdateDuplicateGroupsCount();
        }

        private async void ExecuteGroupOperation(string groupId, Action<DuplicateGroup> operation, string operationName)
{
    try
    {
        var group = _duplicateGroups.FirstOrDefault(g => g.GroupId == groupId);
        if (group == null) return;

        var selectedItems = group.Items.Where(i => i.IsSelected).ToList();
        if (!selectedItems.Any())
        {
            MessageBox.Show($"Please select items for {operationName}.",
                "Selection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _mainWindow.HandleDuplicateOperation(async () =>
        {
            operation(group);
            await _mainWindow.RefreshAfterDuplicateManagementAsync();
            UpdateDuplicateGroupsCount();
        }, operationName);
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Error during {operationName}: {ex.Message}",
            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

        private void KeepSelected_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var groupId = button?.Tag as string;
            if (string.IsNullOrEmpty(groupId)) return;

            ExecuteGroupOperation(groupId, group =>
            {
                var selectedItem = group.Items.First(i => i.IsSelected);
                var entriesToRemove = _mainWindow._bomEntries
                    .Where(e => e.DuplicateGroupId == groupId &&
                               e.Num != selectedItem.Num)
                    .ToList();

                foreach (var entry in entriesToRemove)
                {
                    _mainWindow._bomEntries.Remove(entry);
                }

                var remainingEntry = _mainWindow._bomEntries
                    .First(e => e.DuplicateGroupId == groupId);
                remainingEntry.IsDuplicate = false;
                remainingEntry.DuplicateGroupId = null;

                _duplicateGroups.Remove(group);
            }, "keep selected operation");
        }

        private void MergeSelected_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var groupId = button?.Tag as string;
            if (string.IsNullOrEmpty(groupId)) return;

            ExecuteGroupOperation(groupId, group =>
            {
                var selectedItems = group.Items.Where(i => i.IsSelected).ToList();
                var entries = selectedItems
                    .Select(si => _mainWindow._bomEntries.First(e => e.Num == si.Num))
                    .ToList();

                var primaryEntry = entries.First();
                var oldQty = primaryEntry.QuantityForOne;

                // Update quantities
                primaryEntry.QuantityForOne = entries.Sum(e => e.QuantityForOne);
                primaryEntry.NeedsApiRefresh = true;  // Always mark for refresh when merging

                primaryEntry.Designator = string.Join(", ",
                    entries.Select(e => e.Designator).Where(d => !string.IsNullOrEmpty(d)));
                primaryEntry.IsDuplicate = false;
                primaryEntry.DuplicateGroupId = null;

                foreach (var entry in entries.Skip(1))
                {
                    _mainWindow._bomEntries.Remove(entry);
                }

                _duplicateGroups.Remove(group);
            }, "merge operation");
        }

        private void EditSelected_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var groupId = button?.Tag as string;
            if (string.IsNullOrEmpty(groupId)) return;

            ExecuteGroupOperation(groupId, group =>
            {
                var selectedItem = group.Items.First(i => i.IsSelected);
                var entry = _mainWindow._bomEntries.First(e => e.Num == selectedItem.Num);

                var dialog = new ProductEntryDialog(entry)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() == true)
                {
                    // Update the entry with new values
                    entry.OrderingCode = dialog.Result.OrderingCode;
                    entry.Designator = dialog.Result.Designator;
                    entry.Value = dialog.Result.Value;
                    entry.PcbFootprint = dialog.Result.PcbFootprint;
                    entry.QuantityForOne = dialog.Result.QuantityForOne;
                    entry.QuantityTotal = dialog.Result.QuantityTotal;

                    // Refresh UI
                    LoadDuplicateGroups();
                }
            }, "edit operation");
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var groupId = button?.Tag as string;
            if (string.IsNullOrEmpty(groupId)) return;

            ExecuteGroupOperation(groupId, group =>
            {
                var selectedItems = group.Items.Where(i => i.IsSelected).ToList();

                var result = MessageBox.Show(
                    $"Are you sure you want to delete {selectedItems.Count} selected item(s)?",
                    "Confirm Deletion",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

                if (result == MessageBoxResult.Yes)
                {
                    foreach (var item in selectedItems)
                    {
                        var entryToRemove = _mainWindow._bomEntries.First(e => e.Num == item.Num);
                        _mainWindow._bomEntries.Remove(entryToRemove);
                    }

                    var remainingEntries = _mainWindow._bomEntries
                        .Where(e => e.DuplicateGroupId == groupId)
                        .ToList();

                    if (remainingEntries.Count == 1)
                    {
                        var lastEntry = remainingEntries.First();
                        lastEntry.IsDuplicate = false;
                        lastEntry.DuplicateGroupId = null;
                    }

                    if (!remainingEntries.Any())
                    {
                        _duplicateGroups.Remove(group);
                    }
                    else
                    {
                        LoadDuplicateGroups();
                    }
                }
            }, "delete operation");
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}