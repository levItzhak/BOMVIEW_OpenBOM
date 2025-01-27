using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BOMVIEW.Interfaces;
using BOMVIEW.Models;
using BOMVIEW.OpenBOM.Models;

namespace BOMVIEW
{
    /// <summary>
    /// Result class for catalog column search
    /// </summary>
    public class CatalogColumnResult
    {
        public string CatalogId { get; set; }
        public string CatalogName { get; set; }
        public string ColumnName { get; set; }
    }

    /// <summary>
    /// Service to find which catalogs contain a specific column/property
    /// </summary>
    public class CatalogColumnFinder
    {
        private readonly ILogger _logger;
        private readonly RateLimitedOpenBomService _openBomService;

        public CatalogColumnFinder(ILogger logger, RateLimitedOpenBomService openBomService)
        {
            _logger = logger;
            _openBomService = openBomService;
        }

        /// <summary>
        /// Find catalogs containing a specific column/property
        /// </summary>
        /// <param name="columnName">The name of the column/property to search for</param>
        /// <param name="caseSensitive">Whether the search should be case-sensitive</param>
        /// <returns>List of catalogs containing the specified column</returns>
        public async Task<List<CatalogColumnResult>> FindCatalogsWithColumnAsync(string columnName, bool caseSensitive = false)
        {
            if (string.IsNullOrWhiteSpace(columnName))
            {
                throw new ArgumentException("Column name cannot be empty", nameof(columnName));
            }

            try
            {
                _logger.LogInfo($"Searching for catalogs with column: {columnName}");
                var results = new List<CatalogColumnResult>();

                // Get all available catalogs
                var catalogs = await _openBomService.ListCatalogsAsync();
                _logger.LogInfo($"Found {catalogs.Count} catalogs to search");

                // Process each catalog
                foreach (var catalog in catalogs)
                {
                    try
                    {
                        _logger.LogInfo($"Checking catalog: {catalog.Name} (ID: {catalog.Id})");

                        // Get the catalog document to examine its structure
                        var catalogDoc = await _openBomService.GetCatalogAsync(catalog.Id);

                        if (catalogDoc != null)
                        {
                            // Check if the catalog has the specified column
                            var hasColumn = CheckForColumn(catalogDoc, columnName, caseSensitive);

                            if (hasColumn)
                            {
                                _logger.LogInfo($"Found column '{columnName}' in catalog: {catalog.Name}");
                                results.Add(new CatalogColumnResult
                                {
                                    CatalogId = catalog.Id,
                                    CatalogName = catalog.Name,
                                    ColumnName = columnName
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error checking catalog {catalog.Name}: {ex.Message}");
                    }

                    // Add small delay to avoid rate limiting
                    await Task.Delay(100);
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error searching for catalogs with column '{columnName}': {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Check if a catalog contains a specific column in its structure
        /// </summary>
        private bool CheckForColumn(OpenBomDocument catalog, string columnName, bool caseSensitive)
        {
            if (catalog == null)
                return false;

            var comparisonType = caseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            // Check if the column exists in the Columns list
            if (catalog.Columns != null && catalog.Columns.Any())
            {
                foreach (var column in catalog.Columns)
                {
                    if (column.Equals(columnName, comparisonType))
                    {
                        _logger.LogInfo($"Found column '{columnName}' in Columns list");
                        return true;
                    }
                }
            }

            // If we didn't find the column in the main list, check for it in the cells
            // Sometimes columns can exist in the data but not be in the main Columns list
            if (catalog.Cells != null && catalog.Cells.Any() && catalog.Cells[0] != null)
            {
                // Check through all rows for this column
                foreach (var row in catalog.Cells)
                {
                    // In case there's a dictionary of properties in the row
                    if (row.Count > 0 && row[0] is Dictionary<string, object> properties)
                    {
                        if (properties.ContainsKey(columnName))
                        {
                            _logger.LogInfo($"Found column '{columnName}' in cell properties");
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}