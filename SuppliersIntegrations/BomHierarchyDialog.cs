using System.Windows;

namespace BOMVIEW
{

    public class BomHierarchyDialog : Window
    {
        public bool IsFirstBomParent { get; private set; } = true;

        public BomHierarchyDialog(string firstBomName, string secondBomName)
        {
            Title = "Select Parent-Child Relationship";
            Width = 500;
            Height = 250;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            // Create the main content grid
            var grid = new System.Windows.Controls.Grid();
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            grid.Margin = new Thickness(20);

            // Create the options panel
            var optionsPanel = new System.Windows.Controls.StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            // Add explanation text
            var explanationText = new System.Windows.Controls.TextBlock
            {
                Text = "Select the parent-child relationship between the two BOMs:",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20),
                FontSize = 14
            };
            optionsPanel.Children.Add(explanationText);

            // Add first option radio button
            var option1 = new System.Windows.Controls.RadioButton
            {
                Content = $"{firstBomName} as parent, {secondBomName} as child",
                IsChecked = true,
                GroupName = "ParentOption",
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 13
            };
            option1.Checked += (s, e) => IsFirstBomParent = true;
            optionsPanel.Children.Add(option1);

            // Add second option radio button
            var option2 = new System.Windows.Controls.RadioButton
            {
                Content = $"{secondBomName} as parent, {firstBomName} as child",
                GroupName = "ParentOption",
                Margin = new Thickness(0, 0, 0, 20),
                FontSize = 13
            };
            option2.Checked += (s, e) => IsFirstBomParent = false;
            optionsPanel.Children.Add(option2);

            // Add diagram or visual representation (optional)
            var relationshipDiagram = new System.Windows.Controls.Border
            {
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LightGray),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 20),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.AliceBlue)
            };

            var diagramText = new System.Windows.Controls.TextBlock
            {
                Text = "Parent BOM contains child BOMs as components.\nThis creates a hierarchical relationship in OpenBOM.",
                TextAlignment = TextAlignment.Center,
                FontStyle = FontStyles.Italic
            };

            relationshipDiagram.Child = diagramText;
            optionsPanel.Children.Add(relationshipDiagram);

            grid.Children.Add(optionsPanel);
            System.Windows.Controls.Grid.SetRow(optionsPanel, 0);

            // Add buttons panel
            var buttonsPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };

            // OK button
            var okButton = new System.Windows.Controls.Button
            {
                Content = "OK",
                Padding = new Thickness(20, 5, 20, 5),
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };
            okButton.Click += (s, e) => { DialogResult = true; };
            buttonsPanel.Children.Add(okButton);

            // Cancel button
            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "Cancel",
                Padding = new Thickness(20, 5, 20, 5),
                IsCancel = true
            };
            buttonsPanel.Children.Add(cancelButton);

            grid.Children.Add(buttonsPanel);
            System.Windows.Controls.Grid.SetRow(buttonsPanel, 1);

            Content = grid;
        }
    }
}