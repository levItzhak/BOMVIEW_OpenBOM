using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BOMVIEW.Controls
{
    /// <summary>
    /// A lightweight inline processing indicator that can be embedded directly within UI elements
    /// </summary>
    public class ProcessingIndicator : ContentControl  // Changed from Control to ContentControl
    {
        private Grid _mainGrid;
        private TextBlock _statusText;
        private ProgressBar _progressBar;

        static ProcessingIndicator()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ProcessingIndicator),
                new FrameworkPropertyMetadata(typeof(ProcessingIndicator)));
        }

        public ProcessingIndicator()
        {
            Loaded += ProcessingIndicator_Loaded;
        }

        private void ProcessingIndicator_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeControls();
        }

        private void InitializeControls()
        {
            _mainGrid = new Grid
            {
                Margin = new Thickness(5),
                Visibility = IsProcessing ? Visibility.Visible : Visibility.Collapsed
            };

            _mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _statusText = new TextBlock
            {
                Text = StatusText,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333")),
                Margin = new Thickness(0, 0, 0, 5),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            _progressBar = new ProgressBar
            {
                Height = 3,
                IsIndeterminate = true,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2196F3")),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0")),
                BorderThickness = new Thickness(0)
            };

            Grid.SetRow(_statusText, 0);
            Grid.SetRow(_progressBar, 1);

            _mainGrid.Children.Add(_statusText);
            _mainGrid.Children.Add(_progressBar);
            
            this.Content = _mainGrid;  // This is now valid since we're inheriting from ContentControl
        }

        #region Dependency Properties

        public static readonly DependencyProperty IsProcessingProperty =
            DependencyProperty.Register("IsProcessing", typeof(bool), typeof(ProcessingIndicator),
                new PropertyMetadata(false, OnIsProcessingChanged));

        public static readonly DependencyProperty StatusTextProperty =
            DependencyProperty.Register("StatusText", typeof(string), typeof(ProcessingIndicator),
                new PropertyMetadata("Processing...", OnStatusTextChanged));

        public bool IsProcessing
        {
            get { return (bool)GetValue(IsProcessingProperty); }
            set { SetValue(IsProcessingProperty, value); }
        }

        public string StatusText
        {
            get { return (string)GetValue(StatusTextProperty); }
            set { SetValue(StatusTextProperty, value); }
        }

        private static void OnIsProcessingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (ProcessingIndicator)d;
            if (control._mainGrid != null)
            {
                bool isProcessing = (bool)e.NewValue;
                control._mainGrid.Visibility = isProcessing ? Visibility.Visible : Visibility.Collapsed;

                // Animate fade in/out
                var animation = new DoubleAnimation
                {
                    To = isProcessing ? 1.0 : 0.0,
                    Duration = TimeSpan.FromMilliseconds(250)
                };

                control._mainGrid.BeginAnimation(OpacityProperty, animation);
            }
        }

        private static void OnStatusTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (ProcessingIndicator)d;
            if (control._statusText != null)
            {
                control._statusText.Text = (string)e.NewValue;
            }
        }

        #endregion
    }
}