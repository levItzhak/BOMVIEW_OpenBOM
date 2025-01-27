using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.IO;
using Microsoft.Win32;
using System.Text;
using System.ComponentModel;
using System.Threading;

namespace BOMVIEW.Controls
{
    /// <summary>
    /// Interaction logic for ProgressWindow.xaml
    /// </summary>
    public partial class ProgressWindow : Window
    {
        private DateTime _startTime;
        private DispatcherTimer _timer;
        private CancellationTokenSource _cancellationSource;
        private List<string> _logEntries = new List<string>();
        private bool _isCancelled = false;
        private bool _isComplete = false;

        public event EventHandler OperationCancelled;

        public ProgressWindow(string title = "Operation Progress")
        {
            InitializeComponent();
            OperationTitleText.Text = title;

            // Initialize the timer for elapsed time tracking
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            UpdateElapsedTime();
        }

        /// <summary>
        /// Starts the operation with a status message
        /// </summary>
        public void StartOperation(string statusMessage, CancellationTokenSource cancellationSource = null)
        {
            _startTime = DateTime.Now;
            _timer.Start();
            _cancellationSource = cancellationSource;
            _isCancelled = false;
            _isComplete = false;

            StatusText.Text = statusMessage;
            ProgressDetailsText.Text = "Operation started";
            ProgressBar.Value = 0;
            ProgressPercentText.Text = "0%";

            LogMessage($"Operation started: {statusMessage}");

            // Close button is hidden at start
            CloseButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Updates the progress of the operation
        /// </summary>
        public void UpdateProgress(int current, int total, string details = null)
        {
            if (current < 0) current = 0;
            if (total <= 0) total = 1;
            if (current > total) current = total;

            double percentage = (double)current / total * 100;

            ProgressBar.Value = percentage;
            ProgressPercentText.Text = $"{percentage:0}%";
            ProgressDetailsText.Text = details ?? $"{current} of {total} completed";

            if (!string.IsNullOrEmpty(details))
            {
                LogMessage(details);
            }
        }

        /// <summary>
        /// Updates the status text of the operation
        /// </summary>
        public void UpdateStatus(string statusMessage)
        {
            StatusText.Text = statusMessage;
            LogMessage(statusMessage);
        }

        /// <summary>
        /// Logs a message to the operation log
        /// </summary>
        public void LogMessage(string message)
        {
            string logEntry = $"[{DateTime.Now:HH:mm:ss}] {message}";
            _logEntries.Add(logEntry);

            // Update the log text box
            LogTextBox.AppendText(logEntry + Environment.NewLine);
            LogTextBox.ScrollToEnd();
        }

        /// <summary>
        /// Completes the operation
        /// </summary>
        public void CompleteOperation(string statusMessage = "Operation completed successfully")
        {
            _timer.Stop();
            _isComplete = true;

            StatusText.Text = statusMessage;
            LogMessage(statusMessage);

            // Show close button, hide cancel button
            CloseButton.Visibility = Visibility.Visible;
            CancelButton.Visibility = Visibility.Collapsed;

            // Set progress to 100%
            ProgressBar.Value = 100;
            ProgressPercentText.Text = "100%";
        }

        /// <summary>
        /// Cancels the operation
        /// </summary>
        private void CancelOperation()
        {
            if (_isCancelled) return;

            _isCancelled = true;
            _timer.Stop();

            // Try to signal cancellation
            try
            {
                _cancellationSource?.Cancel();
            }
            catch (Exception ex)
            {
                LogMessage($"Error cancelling operation: {ex.Message}");
            }

            StatusText.Text = "Operation cancelled";
            LogMessage("Operation was cancelled by user");

            // Show close button, hide cancel button
            CloseButton.Visibility = Visibility.Visible;
            CancelButton.Visibility = Visibility.Collapsed;

            // Fire event
            OperationCancelled?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateElapsedTime()
        {
            if (_startTime == default) return;

            TimeSpan elapsed = DateTime.Now - _startTime;
            ElapsedTimeText.Text = $"{elapsed.Hours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        }

        private void SaveLogButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Text files (*.txt)|*.txt|Log files (*.log)|*.log|All files (*.*)|*.*",
                    DefaultExt = ".log",
                    Title = "Save Operation Log"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    // Get timestamp for header
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                    // Build log content
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine($"--- Operation Log: {OperationTitleText.Text} ---");
                    sb.AppendLine($"--- Generated: {timestamp} ---");
                    sb.AppendLine();

                    foreach (string logEntry in _logEntries)
                    {
                        sb.AppendLine(logEntry);
                    }

                    // Write to file
                    File.WriteAllText(saveDialog.FileName, sb.ToString());

                    LogMessage($"Log saved to: {saveDialog.FileName}");
                    MessageBox.Show(
                        $"Log saved successfully to:\n{saveDialog.FileName}",
                        "Log Saved",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error saving log: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to cancel the operation?",
                "Confirm Cancellation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                CancelOperation();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // If operation is still running, ask for confirmation
            if (!_isComplete && !_isCancelled)
            {
                var result = MessageBox.Show(
                    "The operation is still in progress. Are you sure you want to close this window?",
                    "Confirm Close",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }

                // Cancel the operation if user confirms closing
                CancelOperation();
            }

            base.OnClosing(e);
        }
    }
}