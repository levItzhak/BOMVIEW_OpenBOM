using System;
using System.Windows;
using System.Windows.Controls;

namespace BOMVIEW.Controls
{
    public partial class LoadingOverlay : UserControl
    {
        public event EventHandler CancelRequested;

        public LoadingOverlay()
        {
            InitializeComponent();
        }

        public void Show(string message = "Loading...")
        {
            LoadingText.Text = message;
            MainGrid.Visibility = Visibility.Visible;
            CancelButton.IsEnabled = true;
        }

        public void Hide()
        {
            MainGrid.Visibility = Visibility.Collapsed;
        }

        public string LoadingMessage
        {
            get { return LoadingText.Text; }
            set { LoadingText.Text = value; }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelButton.IsEnabled = false;  // Prevent multiple clicks
            CancelRequested?.Invoke(this, EventArgs.Empty);
            LoadingMessage = "Cancelling...";
        }
    }
}