using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BOMVIEW.Models
{
    public class BomTotals : INotifyPropertyChanged
    {
        private decimal _digiKeyUnitTotal;
        private decimal _digiKeyTotalPrice;
        private decimal _mouserUnitTotal;
        private decimal _mouserTotalPrice;
        private decimal _farnellUnitTotal;
        private decimal _farnellTotalPrice;
        private decimal _israelUnitTotal;
        private decimal _israelTotalPrice;
        private decimal _bestSupplierTotal;
        private decimal _bestSupplierNextBreakTotal;
        private int _digiKeyMissingCount;
        private int _mouserMissingCount;
        private int _farnellMissingCount;
        private int _israelMissingCount;
        private int _bestSupplierMissingCount;
        private int _duplicateCount;

        public decimal DigiKeyUnitTotal
        {
            get => _digiKeyUnitTotal;
            set { _digiKeyUnitTotal = value; OnPropertyChanged(); }
        }

        public decimal DigiKeyTotalPrice
        {
            get => _digiKeyTotalPrice;
            set { _digiKeyTotalPrice = value; OnPropertyChanged(); }
        }

        public decimal MouserUnitTotal
        {
            get => _mouserUnitTotal;
            set { _mouserUnitTotal = value; OnPropertyChanged(); }
        }

        public decimal MouserTotalPrice
        {
            get => _mouserTotalPrice;
            set { _mouserTotalPrice = value; OnPropertyChanged(); }
        }

        public decimal FarnellUnitTotal
        {
            get => _farnellUnitTotal;
            set { _farnellUnitTotal = value; OnPropertyChanged(); }
        }

        public decimal FarnellTotalPrice
        {
            get => _farnellTotalPrice;
            set { _farnellTotalPrice = value; OnPropertyChanged(); }
        }

        public decimal IsraelUnitTotal
        {
            get => _israelUnitTotal;
            set { _israelUnitTotal = value; OnPropertyChanged(); }
        }

        public decimal IsraelTotalPrice
        {
            get => _israelTotalPrice;
            set { _israelTotalPrice = value; OnPropertyChanged(); }
        }

        public decimal BestSupplierTotal
        {
            get => _bestSupplierTotal;
            set { _bestSupplierTotal = value; OnPropertyChanged(); }
        }

        public decimal BestSupplierNextBreakTotal
        {
            get => _bestSupplierNextBreakTotal;
            set { _bestSupplierNextBreakTotal = value; OnPropertyChanged(); }
        }

        public int DigiKeyMissingCount
        {
            get => _digiKeyMissingCount;
            set { _digiKeyMissingCount = value; OnPropertyChanged(); }
        }

        public int MouserMissingCount
        {
            get => _mouserMissingCount;
            set { _mouserMissingCount = value; OnPropertyChanged(); }
        }

        public int FarnellMissingCount
        {
            get => _farnellMissingCount;
            set { _farnellMissingCount = value; OnPropertyChanged(); }
        }

        public int IsraelMissingCount
        {
            get => _israelMissingCount;
            set { _israelMissingCount = value; OnPropertyChanged(); }
        }

        public int BestSupplierMissingCount
        {
            get => _bestSupplierMissingCount;
            set { _bestSupplierMissingCount = value; OnPropertyChanged(); }
        }

        public int DuplicateCount
        {
            get => _duplicateCount;
            set { _duplicateCount = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}