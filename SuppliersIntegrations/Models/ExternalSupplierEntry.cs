using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BOMVIEW.Models
{
    public class ExternalSupplierEntry : INotifyPropertyChanged
    {
        private int _originalBomEntryNum;
        private string _orderingCode;
        private string _designator;
        private string _value;
        private string _pcbFootprint;
        private int _quantityForOne;
        private int _quantityTotal;

        // External supplier specific fields
        private string _supplierName;
        private decimal _unitPrice;
        private decimal _totalPrice;
        private int _availability;
        private string _supplierUrl;
        private string _notes;
        private DateTime _dateAdded;
        private DateTime? _estimatedDeliveryDate; // Changed to nullable DateTime
        private string _contactInfo;

        // Basic BOM Properties (linked from original BomEntry)
        public int OriginalBomEntryNum
        {
            get => _originalBomEntryNum;
            set { _originalBomEntryNum = value; OnPropertyChanged(); }
        }

        public string OrderingCode
        {
            get => _orderingCode;
            set { _orderingCode = value; OnPropertyChanged(); }
        }

        public string Designator
        {
            get => _designator;
            set { _designator = value; OnPropertyChanged(); }
        }

        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); }
        }

        public string PcbFootprint
        {
            get => _pcbFootprint;
            set { _pcbFootprint = value; OnPropertyChanged(); }
        }

        public int QuantityForOne
        {
            get => _quantityForOne;
            set { _quantityForOne = value; OnPropertyChanged(); }
        }

        public int QuantityTotal
        {
            get => _quantityTotal;
            set { _quantityTotal = value; OnPropertyChanged(); }
        }

        // External supplier specific properties
        public string SupplierName
        {
            get => _supplierName;
            set { _supplierName = value; OnPropertyChanged(); }
        }

        public decimal UnitPrice
        {
            get => _unitPrice;
            set
            {
                _unitPrice = value;
                OnPropertyChanged();
                // Update total price when unit price changes
                TotalPrice = UnitPrice * QuantityTotal;
            }
        }

        public decimal TotalPrice
        {
            get => _totalPrice;
            set { _totalPrice = value; OnPropertyChanged(); }
        }

        public int Availability
        {
            get => _availability;
            set { _availability = value; OnPropertyChanged(); }
        }

        public string SupplierUrl
        {
            get => _supplierUrl;
            set { _supplierUrl = value; OnPropertyChanged(); }
        }

        public string Notes
        {
            get => _notes;
            set { _notes = value; OnPropertyChanged(); }
        }

        public DateTime DateAdded
        {
            get => _dateAdded;
            set { _dateAdded = value; OnPropertyChanged(); }
        }

        // Changed to nullable DateTime
        public DateTime? EstimatedDeliveryDate
        {
            get => _estimatedDeliveryDate;
            set { _estimatedDeliveryDate = value; OnPropertyChanged(); }
        }

        public string ContactInfo
        {
            get => _contactInfo;
            set { _contactInfo = value; OnPropertyChanged(); }
        }

        // Creates an ExternalSupplierEntry from a BomEntry
        public static ExternalSupplierEntry FromBomEntry(BomEntry entry)
        {
            return new ExternalSupplierEntry
            {
                OriginalBomEntryNum = entry.Num,
                OrderingCode = entry.OrderingCode,
                Designator = entry.Designator,
                Value = entry.Value,
                PcbFootprint = entry.PcbFootprint,
                QuantityForOne = entry.QuantityForOne,
                QuantityTotal = entry.QuantityTotal,
                DateAdded = DateTime.Now,
                EstimatedDeliveryDate = DateTime.Today // Set default to today
            };
        }

        // INotifyPropertyChanged Implementation
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}