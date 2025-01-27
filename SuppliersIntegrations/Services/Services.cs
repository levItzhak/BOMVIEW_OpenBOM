using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using CsvHelper;
using BOMVIEW.Models;
using BOMVIEW.Interfaces;

namespace BOMVIEW.Services
{
    public class ConfigurationManager
    {
        private const string CONFIG_FILE = "mapping_config.csv";
        private readonly ILogger _logger;

        public ConfigurationManager(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            EnsureConfigFileExists();
        }

        private void EnsureConfigFileExists()
        {
            if (!File.Exists(CONFIG_FILE))
            {
                // Create empty config file with headers
                using var writer = new StreamWriter(CONFIG_FILE);
                using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

                // Write headers
                csv.WriteHeader<ConfigRecord>();
                csv.NextRecord();
            }
        }

        private class ConfigRecord
        {
            public string Identifier { get; set; }
            public string SelectedSheet { get; set; }
            public int StartRow { get; set; }
            public decimal AssemblyQuantity { get; set; }
            public string ColumnMappings { get; set; }
            public string MandatoryFields { get; set; }
        }

        public void SaveMappingConfiguration(ExcelMappingConfiguration config, string identifier)
        {
            try
            {
                var configs = LoadAllConfigurations();
                configs.RemoveAll(c => c.Identifier == identifier);

                var record = new ConfigRecord
                {
                    Identifier = identifier,
                    SelectedSheet = config.SelectedSheet,
                    StartRow = config.StartRow,
                    AssemblyQuantity = config.AssemblyQuantity,
                    ColumnMappings = SerializeDictionary(config.ColumnMappings),
                    MandatoryFields = SerializeHashSet(config.MandatoryFields)
                };

                configs.Add(record);
                SaveAllConfigurations(configs);

                _logger.LogSuccess($"Successfully saved configuration for identifier: {identifier}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to save configuration: {ex.Message}");
                throw;
            }
        }

        public ExcelMappingConfiguration LoadMappingConfiguration(string identifier)
        {
            try
            {
                var configs = LoadAllConfigurations();
                var record = configs.Find(c => c.Identifier == identifier);

                if (record == null)
                {
                    _logger.LogInfo($"No configuration found for identifier: {identifier}");
                    return null;
                }

                var config = new ExcelMappingConfiguration
                {
                    SelectedSheet = record.SelectedSheet,
                    StartRow = record.StartRow,
                    AssemblyQuantity = record.AssemblyQuantity,
                    ColumnMappings = DeserializeDictionary(record.ColumnMappings),
                    MandatoryFields = DeserializeHashSet(record.MandatoryFields)
                };

                _logger.LogSuccess($"Successfully loaded configuration for identifier: {identifier}");
                return config;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to load configuration: {ex.Message}");
                return null;
            }
        }

        private List<ConfigRecord> LoadAllConfigurations()
        {
            using var reader = new StreamReader(CONFIG_FILE);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
            return csv.GetRecords<ConfigRecord>().ToList();
        }

        private void SaveAllConfigurations(List<ConfigRecord> configs)
        {
            using var writer = new StreamWriter(CONFIG_FILE, false);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            csv.WriteRecords(configs);
        }

        private string SerializeDictionary(Dictionary<string, string> dict)
        {
            return string.Join(";", dict.Select(kv => $"{kv.Key}={kv.Value}"));
        }

        private Dictionary<string, string> DeserializeDictionary(string serialized)
        {
            var dict = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(serialized)) return dict;

            foreach (var pair in serialized.Split(';'))
            {
                var parts = pair.Split('=');
                if (parts.Length == 2)
                {
                    dict[parts[0]] = parts[1];
                }
            }
            return dict;
        }

        private string SerializeHashSet(HashSet<string> set)
        {
            return string.Join(";", set);
        }

        private HashSet<string> DeserializeHashSet(string serialized)
        {
            return string.IsNullOrEmpty(serialized)
                ? new HashSet<string>()
                : new HashSet<string>(serialized.Split(';'));
        }

        public void DeleteConfiguration(string identifier)
        {
            try
            {
                var configs = LoadAllConfigurations();
                configs.RemoveAll(c => c.Identifier == identifier);
                SaveAllConfigurations(configs);
                _logger.LogSuccess($"Successfully deleted configuration for identifier: {identifier}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to delete configuration: {ex.Message}");
                throw;
            }
        }

        public IEnumerable<string> ListConfigurations()
        {
            try
            {
                var configs = LoadAllConfigurations();
                return configs.Select(c => c.Identifier).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to list configurations: {ex.Message}");
                return Enumerable.Empty<string>();
            }
        }
    }
}