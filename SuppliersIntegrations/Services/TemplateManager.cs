using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CsvHelper;
using System.Globalization;
using BOMVIEW.Models;
using BOMVIEW.Interfaces;
using OfficeOpenXml;

namespace BOMVIEW.Services
{
    public class TemplateManager
    {
        private const string CSV_FOLDER = "CSV";
        private const string TEMPLATE_FILENAME = "bom_templates.csv";
        private readonly ILogger _logger;
        private List<TemplateDefinition> _cachedTemplates;
        public event EventHandler TemplatesChanged;

        public class TemplateDefinition
        {
            public bool UseQuantityBuffer { get; set; }
            public string Name { get; set; }
            public int StartRow { get; set; }
            public decimal AssemblyQuantity { get; set; } = 1m;
            public Dictionary<string, string> ColumnMappings { get; set; } = new();
            public HashSet<string> RequiredFields { get; set; } = new();
        }

        public TemplateManager(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cachedTemplates = null;
            EnsureTemplateFileExists();
        }

        private void EnsureTemplateFileExists()
        {
            try
            {
                string fullPath = GetTemplateFilePath();
                string directory = Path.GetDirectoryName(fullPath);

                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!File.Exists(fullPath))
                {
                    CreateDefaultTemplates();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error ensuring template file exists: {ex.Message}");
                throw;
            }
        }

        private void CreateDefaultTemplates()
        {
            try
            {
                var defaultTemplates = new List<TemplateDefinition>
        {
            new TemplateDefinition
            {
                Name = "Altium BOM",
                StartRow = 13,
                AssemblyQuantity = 1,
                ColumnMappings = new Dictionary<string, string>
                {
                    { "Designator", "A" },
                    { "OrderingCode", "D" },
                    { "QuantityForOne", "B" },
                    { "Value", "J" },
                    { "PcbFootprint", "E" }
                },
                RequiredFields = new HashSet<string> { "OrderingCode", "QuantityForOne" }
            },
            new TemplateDefinition
            {
                Name = "OrCAD BOM",
                StartRow = 2,
                AssemblyQuantity = 1,
                ColumnMappings = new Dictionary<string, string>
                {
                    { "OrderingCode", "G" },
                    { "Designator", "D" },
                    { "Value", "E" },
                    { "PcbFootprint", "H" },
                    { "QuantityForOne", "C" }
                },
                RequiredFields = new HashSet<string> { "OrderingCode", "QuantityForOne" }
            }
        };

                string csvDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CSV");
                Directory.CreateDirectory(csvDirectory);
                string fullPath = Path.Combine(csvDirectory, TEMPLATE_FILENAME);

                using (var writer = new StreamWriter(fullPath, false, Encoding.UTF8))
                using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                {
                    // Write templates
                    foreach (var template in defaultTemplates)
                    {
                        // Write template header
                        csv.WriteField("Template");
                        csv.WriteField(template.Name);
                        csv.WriteField(template.StartRow);
                        csv.WriteField(template.AssemblyQuantity);
                        csv.NextRecord();

                        // Write mappings
                        foreach (var mapping in template.ColumnMappings)
                        {
                            csv.WriteField("Mapping");
                            csv.WriteField(mapping.Key);
                            csv.WriteField(mapping.Value);
                            csv.WriteField(template.RequiredFields.Contains(mapping.Key));
                            csv.NextRecord();
                        }
                    }
                }

                _cachedTemplates = defaultTemplates;
                _logger.LogSuccess("Default templates created successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error creating default templates: {ex.Message}");
                throw;
            }
        }

        private List<TemplateDefinition> LoadTemplatesFromFile()
        {
            using var reader = new StreamReader(GetTemplateFilePath());
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            var templates = new List<TemplateDefinition>();
            var currentTemplate = new TemplateDefinition();

            while (csv.Read())
            {
                var recordType = csv.GetField(0);

                if (recordType == "Template")
                {
                    if (currentTemplate.Name != null)
                    {
                        templates.Add(currentTemplate);
                    }

                    currentTemplate = new TemplateDefinition
                    {
                        Name = csv.GetField(1),
                        StartRow = int.Parse(csv.GetField(2)),
                        AssemblyQuantity = decimal.Parse(csv.GetField(3))
                    };

                    // Check if the buffer flag exists (for backward compatibility)
                    if (csv.Parser.Count > 4)
                    {
                        currentTemplate.UseQuantityBuffer = bool.Parse(csv.GetField(4));
                    }
                }
                else if (recordType == "Mapping")
                {
                    var field = csv.GetField(1);
                    var column = csv.GetField(2);
                    var required = bool.Parse(csv.GetField(3));

                    currentTemplate.ColumnMappings[field] = column;
                    if (required)
                    {
                        currentTemplate.RequiredFields.Add(field);
                    }
                }
            }

            if (currentTemplate.Name != null)
            {
                templates.Add(currentTemplate);
            }

            return templates;
        }
        public void SaveTemplates(List<TemplateDefinition> templates)
        {
            try
            {
                using var writer = new StreamWriter(GetTemplateFilePath(), false, Encoding.UTF8);
                using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

                foreach (var template in templates)
                {
                    // Write template header
                    csv.WriteField("Template");
                    csv.WriteField(template.Name);
                    csv.WriteField(template.StartRow);
                    csv.WriteField(template.AssemblyQuantity);
                    csv.NextRecord();

                    // Write mappings
                    foreach (var mapping in template.ColumnMappings)
                    {
                        csv.WriteField("Mapping");
                        csv.WriteField(mapping.Key);
                        csv.WriteField(mapping.Value);
                        csv.WriteField(template.RequiredFields.Contains(mapping.Key));
                        csv.NextRecord();
                    }
                }

                _cachedTemplates = new List<TemplateDefinition>(templates);
                TemplatesChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving templates: {ex.Message}");
                throw;
            }

            try
            {
                using var writer = new StreamWriter(GetTemplateFilePath(), false, Encoding.UTF8);
                using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

                foreach (var template in templates)
                {
                    // Write template header with buffer flag added
                    csv.WriteField("Template");
                    csv.WriteField(template.Name);
                    csv.WriteField(template.StartRow);
                    csv.WriteField(template.AssemblyQuantity);
                    csv.WriteField(template.UseQuantityBuffer); // Add buffer flag
                    csv.NextRecord();

                    // Write mappings
                    foreach (var mapping in template.ColumnMappings)
                    {
                        csv.WriteField("Mapping");
                        csv.WriteField(mapping.Key);
                        csv.WriteField(mapping.Value);
                        csv.WriteField(template.RequiredFields.Contains(mapping.Key));
                        csv.NextRecord();
                    }
                }

                _cachedTemplates = new List<TemplateDefinition>(templates);
                TemplatesChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving templates: {ex.Message}");
                throw;
            }
        }

        // In TemplateManager.cs
        public bool SaveTemplate(TemplateDefinition template, bool overwrite = false)
        {
            try
            {
                var templates = LoadTemplates();
                var existingTemplate = templates.FirstOrDefault(t => t.Name.Equals(template.Name, StringComparison.OrdinalIgnoreCase));

                if (existingTemplate != null)
                {
                    if (!overwrite)
                    {
                        return false;
                    }
                    templates.Remove(existingTemplate);
                }

                templates.Add(template);
                SaveTemplates(templates);

                // Important: Invalidate the cache after saving to ensure fresh data on next load
                InvalidateCache();

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving template {template.Name}: {ex.Message}");
                return false;
            }
        }

        // Also ensure that the cached templates are properly refreshed
       

        public bool DeleteTemplate(string templateName)
        {
            try
            {
                var templates = LoadTemplates();
                var template = templates.FirstOrDefault(t => t.Name == templateName);
                if (template != null)
                {
                    templates.Remove(template);
                    SaveTemplates(templates);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error deleting template {templateName}: {ex.Message}");
                return false;
            }
        }

        public bool RenameTemplate(string oldName, string newName)
        {
            try
            {
                var templates = LoadTemplates();
                var template = templates.FirstOrDefault(t => t.Name == oldName);
                if (template != null)
                {
                    // Check if new name already exists
                    if (templates.Any(t => t.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                    {
                        return false;
                    }

                    template.Name = newName;
                    SaveTemplates(templates);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error renaming template {oldName}: {ex.Message}");
                return false;
            }
        }

        public ExcelMappingConfiguration ConvertToMappingConfiguration(TemplateDefinition template)
        {
            return new ExcelMappingConfiguration
            {
                StartRow = template.StartRow,
                AssemblyQuantity = template.AssemblyQuantity,
                ColumnMappings = new Dictionary<string, string>(template.ColumnMappings),
                MandatoryFields = new HashSet<string>(template.RequiredFields),
                UseQuantityBuffer = template.UseQuantityBuffer  
            };
        }

        public ExcelMappingConfiguration ConvertToMappingConfiguration(TemplateDefinition template, string filePath)
        {
            string sheetName = GetFirstSheetName(filePath);

            return new ExcelMappingConfiguration
            {
                SelectedSheet = sheetName,
                StartRow = template.StartRow,
                AssemblyQuantity = template.AssemblyQuantity,
                ColumnMappings = new Dictionary<string, string>(template.ColumnMappings),
                MandatoryFields = new HashSet<string>(template.RequiredFields),
                UseQuantityBuffer = template.UseQuantityBuffer  
            };
        }

        public TemplateDefinition ConvertFromMappingConfiguration(
            ExcelMappingConfiguration config,
            string templateName)
        {
            return new TemplateDefinition
            {
                Name = templateName,
                StartRow = config.StartRow,
                AssemblyQuantity = config.AssemblyQuantity,
                ColumnMappings = new Dictionary<string, string>(config.ColumnMappings),
                RequiredFields = new HashSet<string>(config.MandatoryFields)
            };
        }

        private string GetFirstSheetName(string filePath)
        {
            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                if (package.Workbook.Worksheets.Count > 0)
                {
                    return package.Workbook.Worksheets[0].Name;
                }
            }
            throw new Exception("No sheets found in Excel file");
        }

        public void InvalidateCache()
        {
            _cachedTemplates = null;
        }

        public TemplateDefinition GetTemplateByName(string templateName)
        {
            return LoadTemplates().FirstOrDefault(t => t.Name.Equals(templateName, StringComparison.OrdinalIgnoreCase));
        }

        public bool TemplateExists(string templateName)
        {
            return LoadTemplates().Any(t => t.Name.Equals(templateName, StringComparison.OrdinalIgnoreCase));
        }

        private string GetTemplateFilePath()
        {
            try
            {
                string projectDirectory = GetProjectDirectory();
                string csvDirectory = Path.Combine(projectDirectory, CSV_FOLDER);

                // Ensure directory exists
                if (!Directory.Exists(csvDirectory))
                {
                    Directory.CreateDirectory(csvDirectory);
                }

                string templatePath = Path.Combine(csvDirectory, TEMPLATE_FILENAME);
                _logger.LogInfo($"Template file path: {templatePath}");
                return templatePath;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting template file path: {ex.Message}");
                throw;
            }
        }

        public List<TemplateDefinition> LoadTemplates()
        {
            try
            {
                if (_cachedTemplates == null)
                {
                    string templateFilePath = GetTemplateFilePath();

                    // Check if file exists
                    if (!File.Exists(templateFilePath))
                    {
                        _logger.LogWarning($"Template file not found at {templateFilePath}. Creating default templates.");
                        CreateDefaultTemplates();
                    }

                    _cachedTemplates = LoadTemplatesFromFile();

                    // Debugging: Log template names
                    foreach (var template in _cachedTemplates)
                    {
                        _logger.LogInfo($"Loaded template: {template.Name} with {template.ColumnMappings.Count} mappings");
                    }
                }
                return new List<TemplateDefinition>(_cachedTemplates);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading templates: {ex.Message}");
                return new List<TemplateDefinition>();
            }
        }


        private string GetProjectDirectory()
        {
            // Get the executing assembly's path
            string assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
            // Go up from bin/Debug/netX.X/assembly.dll to project folder
            string projectDirectory = Directory.GetParent(assemblyLocation)?.Parent?.Parent?.Parent?.FullName;

            if (string.IsNullOrEmpty(projectDirectory))
            {
                throw new DirectoryNotFoundException("Could not find project directory");
            }

            return projectDirectory;
        }
    }
}