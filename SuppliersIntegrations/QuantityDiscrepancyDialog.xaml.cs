using BOMVIEW.Models;
using System.Windows;

namespace BOMVIEW
{
    public partial class QuantityDiscrepancyDialog : Window
    {
        public QuantityDiscrepancyResult Result { get; private set; }

        public string PartNumber { get; set; }
        public string PartDescription { get; set; }
        public int NewQuantity { get; set; }
        public int ExistingQuantity { get; set; }
        public int DifferenceQuantity { get; set; }

        public QuantityDiscrepancyDialog(BomEntry part, int comparisonQuantity)
        {
            InitializeComponent();

            PartNumber = part.OrderingCode;
            PartDescription = !string.IsNullOrEmpty(part.Value) ? part.Value : "(No description)";
            NewQuantity = part.QuantityTotal;
            ExistingQuantity = comparisonQuantity;
            DifferenceQuantity = NewQuantity - ExistingQuantity;

            DataContext = this;
        }

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            Result = new QuantityDiscrepancyResult
            {
                ShouldSkip = rbSkip.IsChecked ?? false,
                UseAdjustedQuantity = rbDifference.IsChecked ?? false
            };

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class QuantityDiscrepancyResult
    {
        public bool ShouldSkip { get; set; }
        public bool UseAdjustedQuantity { get; set; }
    }
}