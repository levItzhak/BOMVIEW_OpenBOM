using System.ComponentModel;
using System.Windows.Controls;

namespace BOMVIEW.Controls
{
    public partial class ProgressOverlay : UserControl, INotifyPropertyChanged
    {
        private string _title;
        private double _progress;
        private string _status;
        private string _detailedStatus;

        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(nameof(Title)); }
        }

        public double Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(nameof(Progress)); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(nameof(Status)); }
        }

        public string DetailedStatus
        {
            get => _detailedStatus;
            set { _detailedStatus = value; OnPropertyChanged(nameof(DetailedStatus)); }
        }

        public ProgressOverlay()
        {
            InitializeComponent();
            DataContext = this;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}