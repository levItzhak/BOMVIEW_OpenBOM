using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BOMVIEW.Models
{
    public class BomEntry : INotifyPropertyChanged
    {
        // Basic BOM properties
        private int _num;
        private string _orderingCode;
        private string _designator;
        private string _value;
        private string _pcbFootprint;
        private int _quantityForOne;
        private int _quantityTotal;
        private bool _isDuplicate;
        private string _duplicateGroupId;
        private bool _needsApiRefresh;
        private int _stockQuantity;
        private int _adjustedOrderQuantity;
        private string _digiKeyPartNumber;
        private string _mouserPartNumber;
        private SupplierData _farnellData;
        private decimal _farnellCurrentUnitPrice;
        private decimal _farnellCurrentTotalPrice;
        private int _farnellNextBreakQty;
        private decimal _farnellNextBreakUnitPrice;
        private decimal _farnellNextBreakTotalPrice;
        private string _farnellProductUrl;
        private decimal _farnellUnitPrice;
        private int _farnellOrderQuantity;
        private bool _hasExternalSupplier;

        // Supplier data
        private SupplierData _digiKeyData;
        private SupplierData _mouserData;

        // State
        private bool _isLoading;

        // Current pricing and supplier info
        private decimal _currentUnitPrice;
        private decimal _currentTotalPrice;
        private string _bestCurrentSupplier;
        private string _bestNextBreakSupplier;

        // DigiKey specific prices
        private decimal _digiKeyCurrentUnitPrice;
        private decimal _digiKeyCurrentTotalPrice;
        private int _digiKeyNextBreakQty;
        private decimal _digiKeyNextBreakUnitPrice;
        private decimal _digiKeyNextBreakTotalPrice;

        // Product URLs
        private string _digiKeyProductUrl;
        private string _mouserProductUrl;

        // Mouser specific prices
        private decimal _mouserCurrentUnitPrice;
        private decimal _mouserCurrentTotalPrice;
        private int _mouserNextBreakQty;
        private decimal _mouserNextBreakUnitPrice;
        private decimal _mouserNextBreakTotalPrice;



        private int _digiKeyOrderQuantity;
        private int _mouserOrderQuantity;



        public bool HasExternalSupplier
        {
            get => _hasExternalSupplier;
            set { _hasExternalSupplier = value; OnPropertyChanged(); }
        }

        public bool NeedsApiRefresh
        {
            get => _needsApiRefresh;
            set
            {
                _needsApiRefresh = value;
                OnPropertyChanged();
            }
        }
        public bool IsDuplicate
        {
            get => _isDuplicate;
            set { _isDuplicate = value; OnPropertyChanged(); }
        }

        public string DuplicateGroupId
        {
            get => _duplicateGroupId;
            set { _duplicateGroupId = value; OnPropertyChanged(); }
        }

        public int DigiKeyOrderQuantity
        {
            get => _digiKeyOrderQuantity;
            set
            {
                _digiKeyOrderQuantity = value;
                OnPropertyChanged();
                // Update related prices
                UpdatePriceInformation();
            }
        }

        public int MouserOrderQuantity
        {
            get => _mouserOrderQuantity;
            set
            {
                _mouserOrderQuantity = value;
                OnPropertyChanged();
                // Update related prices
                UpdatePriceInformation();
            }
        }

        private void UpdatePriceInformation()
        {
            if (DigiKeyData != null)
            {
                var digiKeyPricing = DigiKeyData.GetPriceForQuantity(DigiKeyOrderQuantity);
                DigiKeyCurrentUnitPrice = digiKeyPricing.currentPrice;
                DigiKeyUnitPrice = digiKeyPricing.currentPrice; // Just the currentPrice, without multiplying by QuantityForOne
                DigiKeyCurrentTotalPrice = digiKeyPricing.currentPrice * DigiKeyOrderQuantity;
                DigiKeyNextBreakQty = digiKeyPricing.nextBreakQuantity;
                DigiKeyNextBreakUnitPrice = digiKeyPricing.nextBreakPrice;
                DigiKeyNextBreakTotalPrice = digiKeyPricing.nextBreakPrice * DigiKeyOrderQuantity;
            }

            if (MouserData != null)
            {
                var mouserPricing = MouserData.GetPriceForQuantity(MouserOrderQuantity);
                MouserCurrentUnitPrice = mouserPricing.currentPrice;
                MouserUnitPrice = mouserPricing.currentPrice; // Just the currentPrice, without multiplying by QuantityForOne
                MouserCurrentTotalPrice = mouserPricing.currentPrice * MouserOrderQuantity;
                MouserNextBreakQty = mouserPricing.nextBreakQuantity;
                MouserNextBreakUnitPrice = mouserPricing.nextBreakPrice;
                MouserNextBreakTotalPrice = mouserPricing.nextBreakPrice * MouserOrderQuantity;
            }

            if (FarnellData != null)
            {
                var farnellPricing = FarnellData.GetPriceForQuantity(FarnellOrderQuantity);
                FarnellCurrentUnitPrice = farnellPricing.currentPrice;
                FarnellUnitPrice = farnellPricing.currentPrice; // Just the currentPrice, without multiplying by QuantityForOne
                FarnellCurrentTotalPrice = farnellPricing.currentPrice * FarnellOrderQuantity;
                FarnellNextBreakQty = farnellPricing.nextBreakQuantity;
                FarnellNextBreakUnitPrice = farnellPricing.nextBreakPrice;
                FarnellNextBreakTotalPrice = farnellPricing.nextBreakPrice * FarnellOrderQuantity;
            }
        }


        // Basic BOM Properties
        public int Num
        {
            get => _num;
            set { _num = value; OnPropertyChanged(); }
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
            set
            {
                if (_quantityForOne != value)
                {
                    _quantityForOne = value;
                    NeedsApiRefresh = true;  // Mark for API refresh when quantity changes
                    OnPropertyChanged();
                }
            }
        }

        public int QuantityTotal
        {
            get => _quantityTotal;
            set { _quantityTotal = value; OnPropertyChanged(); }
        }


        private bool _isUserEntered;
        public bool IsUserEntered
        {
            get => _isUserEntered;
            set
            {
                _isUserEntered = value;
                OnPropertyChanged();
            }
        }


        // Supplier Data Properties
        public SupplierData DigiKeyData
        {
            get => _digiKeyData;
            set { _digiKeyData = value; OnPropertyChanged(); }
        }

        public SupplierData MouserData
        {
            get => _mouserData;
            set { _mouserData = value; OnPropertyChanged(); }
        }

        // State Properties
        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        // Current Pricing Properties
        public decimal CurrentUnitPrice
        {
            get => _currentUnitPrice;
            set { _currentUnitPrice = value; OnPropertyChanged(); }
        }

        public decimal CurrentTotalPrice
        {
            get => _currentTotalPrice;
            set { _currentTotalPrice = value; OnPropertyChanged(); }
        }

        public string BestCurrentSupplier
        {
            get => _bestCurrentSupplier;
            set { _bestCurrentSupplier = value; OnPropertyChanged(); }
        }

        public string BestNextBreakSupplier
        {
            get => _bestNextBreakSupplier;
            set { _bestNextBreakSupplier = value; OnPropertyChanged(); }
        }

        // DigiKey Specific Properties
        public decimal DigiKeyCurrentUnitPrice
        {
            get => _digiKeyCurrentUnitPrice;
            set { _digiKeyCurrentUnitPrice = value; OnPropertyChanged(); }
        }

        public decimal DigiKeyCurrentTotalPrice
        {
            get => _digiKeyCurrentTotalPrice;
            set { _digiKeyCurrentTotalPrice = value; OnPropertyChanged(); }
        }

        public int DigiKeyNextBreakQty
        {
            get => _digiKeyNextBreakQty;
            set { _digiKeyNextBreakQty = value; OnPropertyChanged(); }
        }

        public decimal DigiKeyNextBreakUnitPrice
        {
            get => _digiKeyNextBreakUnitPrice;
            set { _digiKeyNextBreakUnitPrice = value; OnPropertyChanged(); }
        }

        public decimal DigiKeyNextBreakTotalPrice
        {
            get => _digiKeyNextBreakTotalPrice;
            set { _digiKeyNextBreakTotalPrice = value; OnPropertyChanged(); }
        }

        // Mouser Specific Properties
        public decimal MouserCurrentUnitPrice
        {
            get => _mouserCurrentUnitPrice;
            set { _mouserCurrentUnitPrice = value; OnPropertyChanged(); }
        }

        public decimal MouserCurrentTotalPrice
        {
            get => _mouserCurrentTotalPrice;
            set { _mouserCurrentTotalPrice = value; OnPropertyChanged(); }
        }

        public int MouserNextBreakQty
        {
            get => _mouserNextBreakQty;
            set { _mouserNextBreakQty = value; OnPropertyChanged(); }
        }

        public decimal MouserNextBreakUnitPrice
        {
            get => _mouserNextBreakUnitPrice;
            set { _mouserNextBreakUnitPrice = value; OnPropertyChanged(); }
        }

        public decimal MouserNextBreakTotalPrice
        {
            get => _mouserNextBreakTotalPrice;
            set { _mouserNextBreakTotalPrice = value; OnPropertyChanged(); }
        }


        public string DigiKeyProductUrl
        {
            get => _digiKeyProductUrl;
            set { _digiKeyProductUrl = value; OnPropertyChanged(); }
        }

        public string MouserProductUrl
        {
            get => _mouserProductUrl;
            set { _mouserProductUrl = value; OnPropertyChanged(); }
        }

        // INotifyPropertyChanged Implementation
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void MarkFieldAsUserEntered(string fieldName)
        {
            IsUserEntered = true;
            OnPropertyChanged(fieldName);
        }

        // Add these private fields
        private decimal _digiKeyUnitPrice;
        private decimal _mouserUnitPrice;
        private bool _isBestPrice;

        // Add these public properties
        public decimal DigiKeyUnitPrice
        {
            get => _digiKeyUnitPrice;
            set { _digiKeyUnitPrice = value; OnPropertyChanged(); }
        }

        public decimal MouserUnitPrice
        {
            get => _mouserUnitPrice;
            set { _mouserUnitPrice = value; OnPropertyChanged(); }
        }

        public bool IsBestPrice
        {
            get => _isBestPrice;
            set { _isBestPrice = value; OnPropertyChanged(); }
        }

        public bool IsSameProduct(BomEntry other)
        {
            if (other == null) return false;
            return OrderingCode?.Trim().ToLower() == other.OrderingCode?.Trim().ToLower();
        }
        public string DigiKeyPartNumber
        {
            get => _digiKeyPartNumber;
            set { _digiKeyPartNumber = value; OnPropertyChanged(); }
        }

        public string MouserPartNumber
        {
            get => _mouserPartNumber;
            set { _mouserPartNumber = value; OnPropertyChanged(); }
        }

        public SupplierData FarnellData
        {
            get => _farnellData;
            set { _farnellData = value; OnPropertyChanged(); }
        }

        public decimal FarnellCurrentUnitPrice
        {
            get => _farnellCurrentUnitPrice;
            set { _farnellCurrentUnitPrice = value; OnPropertyChanged(); }
        }

        public decimal FarnellCurrentTotalPrice
        {
            get => _farnellCurrentTotalPrice;
            set { _farnellCurrentTotalPrice = value; OnPropertyChanged(); }
        }

        public int FarnellNextBreakQty
        {
            get => _farnellNextBreakQty;
            set { _farnellNextBreakQty = value; OnPropertyChanged(); }
        }

        public decimal FarnellNextBreakUnitPrice
        {
            get => _farnellNextBreakUnitPrice;
            set { _farnellNextBreakUnitPrice = value; OnPropertyChanged(); }
        }

        public decimal FarnellNextBreakTotalPrice
        {
            get => _farnellNextBreakTotalPrice;
            set { _farnellNextBreakTotalPrice = value; OnPropertyChanged(); }
        }

        public string FarnellProductUrl
        {
            get => _farnellProductUrl;
            set { _farnellProductUrl = value; OnPropertyChanged(); }
        }

        public decimal FarnellUnitPrice
        {
            get => _farnellUnitPrice;
            set { _farnellUnitPrice = value; OnPropertyChanged(); }
        }

        public int FarnellOrderQuantity
        {
            get => _farnellOrderQuantity;
            set
            {
                _farnellOrderQuantity = value;
                OnPropertyChanged();
                // Update related prices
                UpdatePriceInformation();
            }
        }



        public int StockQuantity
        {
            get => _stockQuantity;
            set
            {
                if (_stockQuantity != value)
                {
                    _stockQuantity = value;
                    OnPropertyChanged();
                    UpdateAdjustedOrderQuantity();
                }
            }
        }

        public int AdjustedOrderQuantity
        {
            get => _adjustedOrderQuantity;
            set
            {
                _adjustedOrderQuantity = value;
                OnPropertyChanged();
            }
        }

        private void UpdateAdjustedOrderQuantity()
        {
            AdjustedOrderQuantity = Math.Max(0, QuantityTotal - StockQuantity);

            DigiKeyOrderQuantity = AdjustedOrderQuantity;
            MouserOrderQuantity = AdjustedOrderQuantity;
        }


        // Add this method to the BomEntry class
        // Add this method to the BomEntry class
        public BomEntry Clone()
        {
            return new BomEntry
            {
                Num = this.Num,
                OrderingCode = this.OrderingCode,
                Designator = this.Designator,
                Value = this.Value,
                PcbFootprint = this.PcbFootprint,
                QuantityForOne = this.QuantityForOne,
                QuantityTotal = this.QuantityTotal,
                IsUserEntered = this.IsUserEntered,
                DigiKeyData = this.DigiKeyData,
                MouserData = this.MouserData,
                DigiKeyUnitPrice = this.DigiKeyUnitPrice,
                MouserUnitPrice = this.MouserUnitPrice,
                DigiKeyOrderQuantity = this.DigiKeyOrderQuantity,
                MouserOrderQuantity = this.MouserOrderQuantity,
                StockQuantity = this.StockQuantity,
                 FarnellData = this.FarnellData,
                FarnellUnitPrice = this.FarnellUnitPrice,
                FarnellOrderQuantity = this.FarnellOrderQuantity,
                FarnellCurrentUnitPrice = this.FarnellCurrentUnitPrice,
                FarnellCurrentTotalPrice = this.FarnellCurrentTotalPrice,
                FarnellNextBreakQty = this.FarnellNextBreakQty,
                FarnellNextBreakUnitPrice = this.FarnellNextBreakUnitPrice,
                FarnellNextBreakTotalPrice = this.FarnellNextBreakTotalPrice,
                FarnellProductUrl = this.FarnellProductUrl,
                 HasExternalSupplier = this.HasExternalSupplier

            };
        }
    }
}