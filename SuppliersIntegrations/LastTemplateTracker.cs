using System;
using System.IO;
using System.Text;
using BOMVIEW.Interfaces;

namespace BOMVIEW
{
    /// <summary>
    /// A simple class to track the name of the last selected template
    /// </summary>
    public class LastTemplateTracker
    {
        private const string FILENAME = "last_template.txt";
        private readonly ILogger _logger;

        public LastTemplateTracker(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Saves the name of the last selected template
        /// </summary>
        public void SaveLastTemplateName(string templateName)
        {
            if (string.IsNullOrEmpty(templateName))
            {
                _logger.LogWarning("Cannot save empty template name");
                return;
            }

            try
            {
                string directory = GetStorageDirectory();
                string filePath = Path.Combine(directory, FILENAME);

                // Create directory if it doesn't exist
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Write the template name to file
                File.WriteAllText(filePath, templateName, Encoding.UTF8);
                _logger.LogInfo($"Saved last template name: {templateName}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving last template name: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the name of the last selected template
        /// </summary>
        public string GetLastTemplateName()
        {
            try
            {
                string filePath = Path.Combine(GetStorageDirectory(), FILENAME);
                if (!File.Exists(filePath))
                {
                    _logger.LogInfo("No last template file found");
                    return null;
                }

                string templateName = File.ReadAllText(filePath, Encoding.UTF8).Trim();
                _logger.LogInfo($"Retrieved last template name: {templateName}");
                return templateName;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving last template name: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the storage directory for the last template file
        /// </summary>
        private string GetStorageDirectory()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings");
        }
    }
}