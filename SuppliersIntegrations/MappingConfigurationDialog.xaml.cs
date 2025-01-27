using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using BOMVIEW.Models;
using BOMVIEW.Services;

namespace BOMVIEW
{
    public partial class MappingConfigurationDialog : Window
    {
        private readonly string _filePath;
        private readonly TemplateManager _templateManager;
        private readonly ConsoleLogger _logger;
        private readonly List<MappingRow> _mappingRows = new();
        private bool _isInitializing = true;

        public ExcelMappingConfiguration Configuration { get; private set; }

        public class MappingRow : INotifyPropertyChanged
        {
            private string _field;
            private string _selectedColumn;
            private bool _isRequired;

            public string Field
            {
                get => _field;
                set
                {
                    _field = value;
                    OnPropertyChanged(nameof(Field));
                }
            }

            public string SelectedColumn
            {
                get => _selectedColumn;
                set
                {
                    _selectedColumn = value;
                    OnPropertyChanged(nameof(SelectedColumn));
                }
            }

            public bool IsRequired
            {
                get => _isRequired;
                set
                {
                    _isRequired = value;
                    OnPropertyChanged(nameof(IsRequired));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected virtual void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public MappingConfigurationDialog(string filePath, ExcelMappingConfiguration existingConfig = null)
        {
            InitializeComponent();
            _filePath = filePath;
            _logger = new ConsoleLogger();
            _templateManager = new TemplateManager(_logger);

            // Use existing config if provided, otherwise create a new one
            Configuration = existingConfig ?? new ExcelMappingConfiguration();

            InitializeButtons();
            InitializeColumnLetters();
            InitializeMappingGrid();

            // Load the existing configuration if provided
            if (existingConfig != null)
            {
                LoadExistingConfiguration(existingConfig);
            }

            HeaderText.Text = System.IO.Path.GetFileName(filePath);

            _isInitializing = false;
            LoadTemplates();

        }



        // Add this new method to load an existing configuration
        private void LoadExistingConfiguration(ExcelMappingConfiguration config)
        {
            StartRowInput.Text = config.StartRow.ToString();
            AssemblyQuantityInput.Text = config.AssemblyQuantity.ToString();
            UseBufferCheckbox.IsChecked = config.UseQuantityBuffer;

            // Set the mapping values
            foreach (var row in _mappingRows)
            {
                if (config.ColumnMappings.TryGetValue(row.Field, out string column))
                {
                    row.SelectedColumn = column;
                }

                // Always mark OrderingCode and QuantityForOne as required
                if (row.Field == "OrderingCode" || row.Field == "QuantityForOne")
                {
                    row.IsRequired = true;
                }
                else
                {
                    row.IsRequired = config.MandatoryFields.Contains(row.Field);
                }
            }

            MappingGrid.Items.Refresh();
        }


        private void InitializeButtons()
        {
            btnRename.IsEnabled = false;
            btnDelete.IsEnabled = false;
            btnUpdate.IsEnabled = false;
            btnSaveAs.IsEnabled = true;
        }

        private void InitializeColumnLetters()
        {
            var columnLetters = Enumerable.Range(0, 26).Select(i => ((char)('A' + i)).ToString()).ToList();
            ColumnLetterColumn.ItemsSource = columnLetters;
        }

        // Add these methods to MappingConfigurationDialog.xaml.cs

        private void LoadTemplates()
        {
            try
            {
                _logger.LogInfo("Starting to load templates");

                // Reset the selection first to avoid selection change events
                TemplateSelector.SelectedIndex = -1;

                // Get templates from template manager
                var templates = _templateManager.LoadTemplates();
                _logger.LogInfo($"Loaded {templates.Count} templates from manager");

                // Clear and set the item source
                TemplateSelector.Items.Clear();
                TemplateSelector.ItemsSource = templates;

                // Ensure the display member path is set
                TemplateSelector.DisplayMemberPath = "Name";

                // Force refresh the display
                TemplateSelector.UpdateLayout();

                // Get the last template name
                var lastTemplateTracker = new LastTemplateTracker(_logger);
                string lastTemplateName = lastTemplateTracker.GetLastTemplateName();

                if (!string.IsNullOrEmpty(lastTemplateName))
                {
                    _logger.LogInfo($"Looking for last used template: {lastTemplateName}");

                    // Find the template with this name
                    var lastTemplate = templates.FirstOrDefault(t =>
                        t.Name.Equals(lastTemplateName, StringComparison.OrdinalIgnoreCase));

                    if (lastTemplate != null)
                    {
                        _logger.LogInfo($"Found last used template: {lastTemplateName}");
                        TemplateSelector.SelectedItem = lastTemplate;
                        return;
                    }
                    else
                    {
                        _logger.LogInfo($"Last used template not found: {lastTemplateName}");
                    }
                }

                // If no last template found or it doesn't exist, select the first one
                if (templates.Count > 0)
                {
                    _logger.LogInfo("Selecting first template as fallback");
                    TemplateSelector.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading templates: {ex.Message}");
                MessageBox.Show($"Error loading templates: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Update the TemplateSelector_SelectionChanged method to save the last selected template
        private void TemplateSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            var selectedTemplate = TemplateSelector.SelectedItem as TemplateManager.TemplateDefinition;

            // Update button states
            btnRename.IsEnabled = selectedTemplate != null;
            btnDelete.IsEnabled = selectedTemplate != null;
            btnUpdate.IsEnabled = selectedTemplate != null;

            // Update UI with template data
            if (selectedTemplate != null)
            {
                // Save the last selected template name
                var lastTemplateTracker = new LastTemplateTracker(_logger);
                lastTemplateTracker.SaveLastTemplateName(selectedTemplate.Name);

                UpdateUIFromTemplate(selectedTemplate);
            }
            else
            {
                // Clear the UI if no template is selected
                StartRowInput.Text = "";
                AssemblyQuantityInput.Text = "";
                foreach (var row in _mappingRows)
                {
                    row.SelectedColumn = "";
                    row.IsRequired = false;
                }
                MappingGrid.Items.Refresh();
            }
        }
        private void InitializeMappingGrid()
        {
            _mappingRows.Clear();
            var defaultFields = new[] { "OrderingCode", "Designator", "Value", "PcbFootprint", "QuantityForOne" };

            foreach (var field in defaultFields)
            {
                _mappingRows.Add(new MappingRow
                {
                    Field = field,
                    SelectedColumn = Configuration.GetColumnForField(field),
                    IsRequired = Configuration.MandatoryFields.Contains(field)
                });
            }

            MappingGrid.ItemsSource = _mappingRows;
        }

       

        private void UpdateUIFromTemplate(TemplateManager.TemplateDefinition template)
        {
            if (template == null) return;

            try
            {
                StartRowInput.Text = template.StartRow.ToString();
                AssemblyQuantityInput.Text = template.AssemblyQuantity.ToString();
                UseBufferCheckbox.IsChecked = template.UseQuantityBuffer;

                foreach (var row in _mappingRows)
                {
                    row.SelectedColumn = template.ColumnMappings.TryGetValue(row.Field, out string column) ? column : "";
                    row.IsRequired = template.RequiredFields.Contains(row.Field);
                }

                MappingGrid.Items.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading template data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnRename_Click(object sender, RoutedEventArgs e)
        {
            var selectedTemplate = TemplateSelector.SelectedItem as TemplateManager.TemplateDefinition;
            if (selectedTemplate == null) return;

            var dialog = new TextInputDialog("Rename Template", "New template name:", selectedTemplate.Name)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var newName = dialog.InputText.Trim();
                    if (string.IsNullOrEmpty(newName))
                    {
                        MessageBox.Show("Template name cannot be empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var templates = _templateManager.LoadTemplates();
                    if (templates.Any(t => t.Name.Equals(newName, StringComparison.OrdinalIgnoreCase) &&
                                       !t.Name.Equals(selectedTemplate.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        MessageBox.Show("A template with this name already exists.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (_templateManager.RenameTemplate(selectedTemplate.Name, newName))
                    {
                        RefreshTemplatesList();
                        TemplateSelector.SelectedItem = ((List<TemplateManager.TemplateDefinition>)TemplateSelector.ItemsSource)
                            .FirstOrDefault(t => t.Name == newName);

                        MessageBox.Show("Template renamed successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error renaming template: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void btnDelete_Click(object sender, RoutedEventArgs e)
        {
            var selectedTemplate = TemplateSelector.SelectedItem as TemplateManager.TemplateDefinition;
            if (selectedTemplate == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete the template '{selectedTemplate.Name}'?\nThis action cannot be undone.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    if (_templateManager.DeleteTemplate(selectedTemplate.Name))
                    {
                        RefreshTemplatesList();
                        MessageBox.Show("Template deleted successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting template: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void btnUpdate_Click(object sender, RoutedEventArgs e)
        {
            var selectedTemplate = TemplateSelector.SelectedItem as TemplateManager.TemplateDefinition;
            if (selectedTemplate == null)
            {
                MessageBox.Show("Please select a template to update.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!ValidateTemplateData()) return;

            try
            {
                var updatedTemplate = CreateTemplateFromCurrentSettings(selectedTemplate.Name);
                if (_templateManager.SaveTemplate(updatedTemplate, true))
                {
                    RefreshTemplatesList();

                    // Maintain the selection after refresh
                    TemplateSelector.SelectedItem = ((List<TemplateManager.TemplateDefinition>)TemplateSelector.ItemsSource)
                        .FirstOrDefault(t => t.Name == selectedTemplate.Name);

                    MessageBox.Show("Template updated successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating template: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void btnSaveAs_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateTemplateData()) return;

            var dialog = new TextInputDialog("Save Template As", "Template name:")
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var templateName = dialog.InputText.Trim();
                    if (string.IsNullOrEmpty(templateName))
                    {
                        MessageBox.Show("Template name cannot be empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var templates = _templateManager.LoadTemplates();
                    if (templates.Any(t => t.Name.Equals(templateName, StringComparison.OrdinalIgnoreCase)))
                    {
                        var updateResult = MessageBox.Show(
                            "A template with this name already exists. Do you want to update it?",
                            "Template Exists",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (updateResult != MessageBoxResult.Yes)
                        {
                            return;
                        }
                    }

                    var template = CreateTemplateFromCurrentSettings(templateName);
                    if (_templateManager.SaveTemplate(template, true))
                    {
                        RefreshTemplatesList();
                        TemplateSelector.SelectedItem = ((List<TemplateManager.TemplateDefinition>)TemplateSelector.ItemsSource)
                            .FirstOrDefault(t => t.Name == templateName);

                        MessageBox.Show("Template saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving template: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void RefreshTemplatesList()
        {
            var selectedTemplate = TemplateSelector.SelectedItem as TemplateManager.TemplateDefinition;
            var selectedName = selectedTemplate?.Name;

            var templates = _templateManager.LoadTemplates();
            TemplateSelector.ItemsSource = templates;

            if (!string.IsNullOrEmpty(selectedName))
            {
                TemplateSelector.SelectedItem = templates.FirstOrDefault(t => t.Name == selectedName);
            }
            else if (templates.Any())
            {
                TemplateSelector.SelectedIndex = 0;
            }
        }

        private bool ValidateTemplateData()
        {
            if (string.IsNullOrWhiteSpace(StartRowInput.Text) ||
                !int.TryParse(StartRowInput.Text, out int startRow) ||
                startRow < 1)
            {
                MessageBox.Show("Please enter a valid start row number (must be greater than 0).",
                    "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(AssemblyQuantityInput.Text) ||
                !decimal.TryParse(AssemblyQuantityInput.Text, out decimal qty) ||
                qty <= 0)
            {
                MessageBox.Show("Please enter a valid assembly quantity (must be greater than 0).",
                    "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // Verify at least one column mapping is set
            if (!_mappingRows.Any(r => !string.IsNullOrWhiteSpace(r.SelectedColumn)))
            {
                MessageBox.Show("Please map at least one column.",
                    "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private TemplateManager.TemplateDefinition CreateTemplateFromCurrentSettings(string templateName)
        {
            decimal assemblyQty = decimal.Parse(AssemblyQuantityInput.Text);

            var template = new TemplateManager.TemplateDefinition
            {
                Name = templateName,
                StartRow = int.Parse(StartRowInput.Text),
                AssemblyQuantity = assemblyQty,
                UseQuantityBuffer = UseBufferCheckbox.IsChecked ?? false,
            };

            foreach (var row in _mappingRows)
            {
                if (!string.IsNullOrEmpty(row.SelectedColumn))
                {
                    template.ColumnMappings[row.Field] = row.SelectedColumn;
                }

                if (row.IsRequired)
                {
                    template.RequiredFields.Add(row.Field);
                }
            }

            return template;
        }

        private void MappingGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit && e.Row.Item is MappingRow row)
            {
                if (e.Column == MappingGrid.Columns[1]) // Column mapping changed
                {
                    var editElement = e.EditingElement as ComboBox;
                    if (editElement != null)
                    {
                        var newValue = editElement.SelectedItem as string;
                        row.SelectedColumn = newValue;
                    }
                }
                else if (e.Column == MappingGrid.Columns[2]) // Required status changed
                {
                    var editElement = e.EditingElement as CheckBox;
                    if (editElement != null)
                    {
                        row.IsRequired = editElement.IsChecked ?? false;
                    }
                }
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateConfiguration())
                return;

            try
            {
                // Get selected sheet name
                string selectedSheet = GetFirstSheetName();
                if (string.IsNullOrEmpty(selectedSheet))
                {
                    MessageBox.Show("No sheet available in the Excel file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Parse assembly quantity as decimal instead of integer
                string assemblyQtyText = AssemblyQuantityInput.Text.Replace(',', '.');
                if (!decimal.TryParse(assemblyQtyText, System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out decimal assemblyQty))
                {
                    MessageBox.Show("Please enter a valid assembly quantity.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Create a dictionary only including required fields
                var columnMappings = new Dictionary<string, string>();

                // First add the always-required fields
                foreach (var row in _mappingRows.Where(r => r.Field == "OrderingCode" || r.Field == "QuantityForOne"))
                {
                    if (!string.IsNullOrEmpty(row.SelectedColumn))
                    {
                        columnMappings[row.Field] = row.SelectedColumn;
                    }
                }

                // Then add other fields that are marked as required
                foreach (var row in _mappingRows.Where(r => r.IsRequired && r.Field != "OrderingCode" && r.Field != "QuantityForOne"))
                {
                    if (!string.IsNullOrEmpty(row.SelectedColumn))
                    {
                        columnMappings[row.Field] = row.SelectedColumn;
                    }
                }

                Configuration = new ExcelMappingConfiguration
                {
                    SelectedSheet = selectedSheet,
                    StartRow = int.Parse(StartRowInput.Text),
                    AssemblyQuantity = assemblyQty,
                    ColumnMappings = columnMappings,
                    MandatoryFields = new HashSet<string>(
                        _mappingRows.Where(r => r.IsRequired).Select(r => r.Field)),
                    UseQuantityBuffer = UseBufferCheckbox.IsChecked ?? false,
                };

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetFirstSheetName()
        {
            try
            {
                using (var package = new OfficeOpenXml.ExcelPackage(new FileInfo(_filePath)))
                {
                    if (package.Workbook.Worksheets.Count > 0)
                    {
                        return package.Workbook.Worksheets[0].Name;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading Excel file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return null;
        }

        private bool ValidateConfiguration()
        {
            // Validate start row
            if (string.IsNullOrEmpty(StartRowInput.Text) || !int.TryParse(StartRowInput.Text, out int startRow) || startRow < 1)
            {
                MessageBox.Show("Please enter a valid start row number.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // Validate assembly quantity - Parse as decimal
            string assemblyQtyText = AssemblyQuantityInput.Text.Replace(',', '.');
            if (string.IsNullOrWhiteSpace(assemblyQtyText) ||
                !decimal.TryParse(assemblyQtyText, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out decimal assemblyQty) ||
                assemblyQty <= 0)
            {
                MessageBox.Show("Please enter a valid assembly quantity (must be greater than 0).",
                    "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // Ensure OrderingCode and QuantityForOne are mapped
            var orderingCodeRow = _mappingRows.FirstOrDefault(r => r.Field == "OrderingCode");
            var quantityForOneRow = _mappingRows.FirstOrDefault(r => r.Field == "QuantityForOne");

            if (orderingCodeRow == null || string.IsNullOrEmpty(orderingCodeRow.SelectedColumn))
            {
                MessageBox.Show("OrderingCode field must be mapped. This is a required field.",
                    "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (quantityForOneRow == null || string.IsNullOrEmpty(quantityForOneRow.SelectedColumn))
            {
                MessageBox.Show("QuantityForOne field must be mapped. This is a required field.",
                    "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // Ensure OrderingCode and QuantityForOne are marked as required
            orderingCodeRow.IsRequired = true;
            quantityForOneRow.IsRequired = true;

            // Validate other required field mappings (ones the user checked)
            var requiredFields = _mappingRows.Where(r => r.IsRequired).ToList();
            var unmappedRequired = requiredFields.Where(r => string.IsNullOrEmpty(r.SelectedColumn)).ToList();

            if (unmappedRequired.Any())
            {
                var fields = string.Join(", ", unmappedRequired.Select(r => r.Field));
                MessageBox.Show($"Please map all required fields: {fields}",
                    "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }


        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            var regex = new Regex("[^0-9.,]+");
            e.Handled = regex.IsMatch(e.Text);

            // Additional validation to ensure only one decimal point
            if (e.Text == "." || e.Text == ",")
            {
                TextBox textBox = sender as TextBox;
                if (textBox != null)
                {
                    e.Handled = textBox.Text.Contains(".") || textBox.Text.Contains(",");
                }
            }
        }


        private void btnMappingConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var mappingDialog = new MappingConfigurationDialog(null)
                {
                    Owner = this
                };

                if (mappingDialog.ShowDialog() == true)
                {
                    // Store the configuration for future use
                    var config = mappingDialog.Configuration;
                    // You could potentially save this configuration for later use
                    MessageBox.Show("Mapping configuration saved successfully!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening mapping configuration: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void btnAutoDetect_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                MessageBox.Show("No Excel file selected.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                // Get the header row from user input
                if (!int.TryParse(HeaderRowInput.Text, out int headerRow) || headerRow < 1)
                {
                    MessageBox.Show("Please enter a valid header row number.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Mouse.OverrideCursor = null;
                    return;
                }

                // Create auto-detector and run enhanced detection with the specified header row
                var detector = new ColumnAutoDetector(_filePath, _logger);
                var detectionResult = detector.DetectColumnMappingsAndFirstDataRow(headerRow);
                var mappings = detectionResult.ColumnMappings;
                int firstDataRow = detectionResult.FirstDataRow;

                if (mappings.Count == 0)
                {
                    MessageBox.Show("No columns could be automatically detected. Please map columns manually.",
                        "Auto-Detection", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Apply detected mappings to the UI
                foreach (var mapping in mappings)
                {
                    var row = _mappingRows.FirstOrDefault(r => r.Field == mapping.Key);
                    if (row != null)
                    {
                        row.SelectedColumn = mapping.Value;

                        // Mark any found/detected field as required automatically
                        // This ensures that if a field is detected, it's automatically marked as required
                        row.IsRequired = true;
                    }
                }

                // Refresh the grid to show the updated values
                MappingGrid.Items.Refresh();

                // Set the start row to the detected first data row
                StartRowInput.Text = firstDataRow.ToString();

                // Set a default assembly quantity if not already set
                if (string.IsNullOrEmpty(AssemblyQuantityInput.Text))
                {
                    AssemblyQuantityInput.Text = "1";
                }

                MessageBox.Show($"Auto-detection complete! Found {mappings.Count} column mappings.\n" +
                    $"First data row detected at row {firstDataRow}.\n\n" +
                    "Please review and adjust the mappings as needed.",
                    "Auto-Detection", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during auto-detection: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

    }
}