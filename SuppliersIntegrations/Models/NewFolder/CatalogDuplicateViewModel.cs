using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BOMVIEW.Models
{
    public class CatalogDuplicateEntry : INotifyPropertyChanged
    {
        private bool _isSelected;
        public string CatalogId { get; set; }
        public string CatalogName { get; set; }
        public string PartNumber { get; set; }
        public string ManufacturerPartNumber { get; set; }
        public string Description { get; set; }
        public Dictionary<string, string> Properties { get; set; } = new();
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class CatalogDuplicateGroup : INotifyPropertyChanged
    {
        private ObservableCollection<CatalogDuplicateEntry> _entries;
        public string PartNumber { get; set; }
        public string ManufacturerPartNumber { get; set; }
        public int Count => Entries.Count;
        public ObservableCollection<CatalogDuplicateEntry> Entries
        {
            get => _entries;
            set
            {
                _entries = value;
                OnPropertyChanged();
            }
        }

        public CatalogDuplicateGroup()
        {
            Entries = new ObservableCollection<CatalogDuplicateEntry>();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Helper class for creating a composite key
    public class PartNumberKey
    {
        public string PartNumber { get; }
        public string ManufacturerPartNumber { get; }

        public PartNumberKey(string partNumber, string manufacturerPartNumber)
        {
            PartNumber = partNumber ?? "";
            ManufacturerPartNumber = manufacturerPartNumber ?? "";
        }

        public override bool Equals(object obj)
        {
            if (obj is not PartNumberKey other)
                return false;

            return string.Equals(PartNumber, other.PartNumber, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(ManufacturerPartNumber, other.ManufacturerPartNumber, StringComparison.OrdinalIgnoreCase);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                PartNumber?.ToUpperInvariant() ?? "",
                ManufacturerPartNumber?.ToUpperInvariant() ?? ""
            );
        }
    }
}