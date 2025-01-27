using System;
using System.Windows;

namespace BOMVIEW
{
    public partial class EmailDialog : Window
    {
        public EmailDialog()
        {
            InitializeComponent();
            SubjectTextBox.Text = $"BOM Price Report - {DateTime.Now:dd-MM-yyyy HH:mm}";
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(SubjectTextBox.Text))
                {
                    MessageBox.Show("Please enter a subject", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Create Gmail compose URL
                var gmailUrl = new System.Text.StringBuilder();
                gmailUrl.Append("https://mail.google.com/mail/?view=cm");
                gmailUrl.Append($"&to={Uri.EscapeDataString(ToEmailTextBox.Text)}");
                gmailUrl.Append($"&su={Uri.EscapeDataString(SubjectTextBox.Text)}"); // Using 'su' parameter for subject
                gmailUrl.Append($"&body={Uri.EscapeDataString(MessageTextBox.Text)}");

                // Open Gmail in default browser
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = gmailUrl.ToString(),
                    UseShellExecute = true
                });

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening Gmail: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}