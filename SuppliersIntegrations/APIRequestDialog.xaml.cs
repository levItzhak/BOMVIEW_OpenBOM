using System.Windows;

namespace BOMVIEW
{
    public partial class APIRequestDialog : Window
    {
        public bool ShouldFetchData { get; private set; }

        public APIRequestDialog()
        {
            InitializeComponent();
        }

        private void UpdateNumberOnly_Click(object sender, RoutedEventArgs e)
        {
            ShouldFetchData = false;
            DialogResult = true;
            Close();
        }

        private void FetchNewData_Click(object sender, RoutedEventArgs e)
        {
            ShouldFetchData = true;
            DialogResult = true;
            Close();
        }
    }
}