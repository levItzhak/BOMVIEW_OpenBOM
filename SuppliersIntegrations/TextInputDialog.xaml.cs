using System.ComponentModel;
using System.Windows;

namespace BOMVIEW
{
    public partial class TextInputDialog : Window, INotifyPropertyChanged
    {
        private string _dialogTitle;
        private string _promptText;
        private string _inputText;

        public string DialogTitle
        {
            get => _dialogTitle;
            set
            {
                _dialogTitle = value;
                OnPropertyChanged(nameof(DialogTitle));
            }
        }

        public string PromptText
        {
            get => _promptText;
            set
            {
                _promptText = value;
                OnPropertyChanged(nameof(PromptText));
            }
        }

        public string InputText
        {
            get => _inputText;
            set
            {
                _inputText = value;
                OnPropertyChanged(nameof(InputText));
            }
        }

        public TextInputDialog(string title, string prompt, string defaultText = "")
        {
            InitializeComponent();
            DataContext = this;
            DialogTitle = title;
            PromptText = prompt;
            InputText = defaultText;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}