using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace BOMVIEW.Controls
{
    /// <summary>
    /// Interaction logic for EnhancedLoadingOverlay.xaml
    /// </summary>
    public partial class EnhancedLoadingOverlay : UserControl
    {
        private DispatcherTimer _updateTimer;
        private DateTime _startTime;
        private System.Threading.CancellationTokenSource _cancellationTokenSource;
        private Storyboard _spinnerAnimation;

        public event EventHandler CancellationRequested;

        public EnhancedLoadingOverlay()
        {
            InitializeComponent();

            // Set up spinner animation in code
            var rotateAnimation = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(1.5),
                RepeatBehavior = RepeatBehavior.Forever
            };

            Storyboard.SetTarget(rotateAnimation, Spinner);
            Storyboard.SetTargetProperty(rotateAnimation,
                new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));

            _spinnerAnimation = new Storyboard();
            _spinnerAnimation.Children.Add(rotateAnimation);

            // Create a timer to update the UI even when the main thread is busy
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _updateTimer.Tick += UpdateTimer_Tick;

            // Start the spinner animation when the control is loaded
            this.Loaded += (s, e) => {
                _spinnerAnimation.Begin();
            };
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            // Calculate elapsed time since the operation started
            if (_startTime != default)
            {
                TimeSpan elapsed = DateTime.Now - _startTime;
                ElapsedTimeText.Text = $"Time elapsed: {elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
                ElapsedTimeText.Visibility = Visibility.Visible;
            }

            // Make sure UI updates are processed
            Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        }

        /// <summary>
        /// Shows the loading overlay with a specific title and message
        /// </summary>
        public void Show(string title, string message = "Loading...")
        {
            TitleText.Text = title;
            StatusText.Text = message;
            DetailText.Text = string.Empty;
            DetailText.Visibility = Visibility.Collapsed;
            ProgressStatsText.Visibility = Visibility.Collapsed;
            ElapsedTimeText.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;

            // Reset progress state
            ProgressIndicator.Value = 0;
            ProgressIndicator.Visibility = Visibility.Collapsed;
            IndeterminateIndicator.Visibility = Visibility.Visible;

            // Start timing
            _startTime = DateTime.Now;
            _updateTimer.Start();

            // Start spinner animation
            _spinnerAnimation.Begin();

            // Show the overlay
            Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Shows the loading overlay with support for cancellation
        /// </summary>
        public void ShowWithCancellation(string title, string message, System.Threading.CancellationTokenSource cancellationSource)
        {
            Show(title, message);

            // Set up cancellation
            _cancellationTokenSource = cancellationSource;
            CancelButton.Visibility = Visibility.Visible;
            CancelButton.IsEnabled = true;
            CancelButton.Content = "Cancel";
        }

        /// <summary>
        /// Updates the status message
        /// </summary>
        public void UpdateStatus(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;
            });
        }

        /// <summary>
        /// Updates both the main status and a detail message
        /// </summary>
        public void UpdateStatus(string message, string detailMessage)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;
                DetailText.Text = detailMessage;
                DetailText.Visibility = Visibility.Visible;
            });
        }

        /// <summary>
        /// Updates the progress information
        /// </summary>
        public void UpdateProgress(int current, int total, string operationName = null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Switch to determinate progress bar
                IndeterminateIndicator.Visibility = Visibility.Collapsed;
                ProgressIndicator.Visibility = Visibility.Visible;

                // Calculate percentage and update progress bar
                double percentage = (double)current / total * 100;
                ProgressIndicator.Value = percentage;

                // Show progress stats
                string statsText = operationName != null
                    ? $"{operationName}: {current} of {total} ({percentage:0}%)"
                    : $"Progress: {current} of {total} ({percentage:0}%)";

                ProgressStatsText.Text = statsText;
                ProgressStatsText.Visibility = Visibility.Visible;
            });
        }

        /// <summary>
        /// Displays a pulsing animation on the progress bar to indicate activity
        /// </summary>
        public void PulseProgressBar()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Switch to determinate progress but with animation
                IndeterminateIndicator.Visibility = Visibility.Collapsed;
                ProgressIndicator.Visibility = Visibility.Visible;

                // Create pulsing animation
                DoubleAnimation pulseAnimation = new DoubleAnimation
                {
                    From = 20,
                    To = 80,
                    Duration = TimeSpan.FromSeconds(1.5),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                };

                ProgressIndicator.BeginAnimation(ProgressBar.ValueProperty, pulseAnimation);
            });
        }

        /// <summary>
        /// Hides the loading overlay
        /// </summary>
        public void Hide()
        {
            _updateTimer.Stop();
            _spinnerAnimation.Stop();

            // Stop any animations
            ProgressIndicator.BeginAnimation(ProgressBar.ValueProperty, null);

            Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            _cancellationTokenSource = null;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                var result = MessageBox.Show(
                    "Are you sure you want to cancel the current operation?",
                    "Confirm Cancellation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _cancellationTokenSource.Cancel();
                    CancellationRequested?.Invoke(this, EventArgs.Empty);

                    // Update UI to show cancellation is in progress
                    CancelButton.IsEnabled = false;
                    CancelButton.Content = "Cancelling...";
                    UpdateStatus("Cancelling operation...", "Please wait while the operation is safely terminated.");
                }
            }
        }
    }
}