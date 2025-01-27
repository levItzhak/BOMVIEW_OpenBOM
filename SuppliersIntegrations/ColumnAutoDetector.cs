using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BOMVIEW.Services;
using OfficeOpenXml;

namespace BOMVIEW
{
    /// <summary>
    /// Class to handle auto-detection of Excel columns for BOM mapping with enhanced first data row detection
    /// </summary>
    public class ColumnAutoDetector
    {
        private readonly string _filePath;
        private readonly ConsoleLogger _logger;

        // Dictionary mapping BOM fields to potential header matches
        private readonly Dictionary<string, List<string>> _fieldMatches = new Dictionary<string, List<string>>
        {
            ["OrderingCode"] = new List<string> {
                "ordering code", "order code", "part number", "partnumber", "part no", "partno",
                "manufacturer part", "mpn", "item number", "component id", "sku"
            },
            ["Designator"] = new List<string> {
                "designator", "reference designator", "ref des", "refdes", "reference", "ref",
                "pcb reference", "location", "position", "id"
            },
            ["Value"] = new List<string> {
                "value", "component value", "val", "resistance", "capacitance", "rating",
                "description", "comp value"
            },
            ["PcbFootprint"] = new List<string> {
                "footprint", "pcb footprint", "package", "case", "size", "form factor",
                "pcb package", "smd", "smt package", "through hole"
            },
            ["QuantityForOne"] = new List<string> {
                "quantity", "qty", "count", "number of", "amount", "pcs", "pieces", "units",
                "quantity per unit", "qty per", "usage count"
            }
        };

        // Regex patterns for content-based detection
        private readonly Dictionary<string, Regex> _contentPatterns = new Dictionary<string, Regex>
        {
            ["Designator"] = new Regex(@"^[A-Z][0-9]+$|^[A-Z]{1,2}\d+(_\d+)?$", RegexOptions.Compiled),
            ["Value"] = new Regex(@"^\d+(\.\d+)?\s*(k|m|u|n|p|f|r|l|ohm|Ω|Hz|F|H|V|A).*$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            ["OrderingCode"] = new Regex(@"^[A-Z0-9\-]{5,}$", RegexOptions.Compiled),
            ["QuantityForOne"] = new Regex(@"^\d+$", RegexOptions.Compiled)
        };

        public ColumnAutoDetector(string filePath, ConsoleLogger logger)
        {
            _filePath = filePath;
            _logger = logger;
        }

        /// <summary>
        /// Result of column auto-detection including column mappings and first data row
        /// </summary>
        public class DetectionResult
        {
            public Dictionary<string, string> ColumnMappings { get; set; } = new Dictionary<string, string>();
            public int FirstDataRow { get; set; }
        }

        /// <summary>
        /// Detects column mappings and first data row
        /// </summary>
        /// <param name="headerRow">The row number containing headers (1-based)</param>
        /// <returns>DetectionResult with column mappings and first data row</returns>
        public DetectionResult DetectColumnMappingsAndFirstDataRow(int headerRow = 1)
        {
            _logger.LogInfo("Starting column and first data row auto-detection");
            var result = new DetectionResult
            {
                FirstDataRow = headerRow + 1 // Default fallback
            };

            try
            {
                using (var package = new ExcelPackage(new FileInfo(_filePath)))
                {
                    if (package.Workbook.Worksheets.Count == 0)
                    {
                        _logger.LogInfo("No worksheets found in the Excel file");
                        return result;
                    }

                    var worksheet = package.Workbook.Worksheets[0]; // Use first sheet

                    // Get column count and verify file has content
                    int colCount = worksheet.Dimension?.Columns ?? 0;
                    int rowCount = worksheet.Dimension?.Rows ?? 0;

                    if (colCount == 0 || rowCount <= headerRow)
                    {
                        _logger.LogInfo("Worksheet appears to be empty or has no data rows");
                        return result;
                    }

                    // Detect based on headers first
                    var headerMappings = DetectFromHeaders(worksheet, headerRow, colCount);
                    foreach (var mapping in headerMappings)
                    {
                        result.ColumnMappings[mapping.Key] = mapping.Value;
                    }

                    // Find the first data row
                    result.FirstDataRow = FindFirstDataRow(worksheet, headerRow, rowCount, colCount, result.ColumnMappings);
                    _logger.LogInfo($"Detected first data row at row {result.FirstDataRow}");

                    // For any fields not found in headers, try content pattern detection
                    var unmappedFields = _fieldMatches.Keys.Except(result.ColumnMappings.Keys).ToList();
                    if (unmappedFields.Any())
                    {
                        var contentMappings = DetectFromContent(worksheet, result.FirstDataRow, colCount, unmappedFields);
                        foreach (var mapping in contentMappings)
                        {
                            result.ColumnMappings[mapping.Key] = mapping.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogInfo($"Error during column auto-detection: {ex.Message}");
            }

            _logger.LogInfo($"Auto-detection complete. Found {result.ColumnMappings.Count} column mappings, first data row: {result.FirstDataRow}");
            return result;
        }

        /// <summary>
        /// Find the first row that contains actual BOM data after the header row
        /// </summary>
        private int FindFirstDataRow(ExcelWorksheet worksheet, int headerRow, int rowCount, int colCount,
            Dictionary<string, string> columnMappings)
        {
            // If no column mappings found, can't detect data rows reliably
            if (columnMappings.Count == 0)
                return headerRow + 1;

            // Check up to 20 rows after the header row
            int maxRowToCheck = Math.Min(headerRow + 20, rowCount);

            // Convert column letters to column numbers for faster checking
            var colNumbers = new Dictionary<string, int>();
            foreach (var mapping in columnMappings)
            {
                colNumbers[mapping.Key] = GetExcelColumnNumber(mapping.Value);
            }

            // Try to find the first row where at least one mapped column contains valid data
            for (int row = headerRow + 1; row <= maxRowToCheck; row++)
            {
                bool hasData = false;

                foreach (var field in colNumbers.Keys)
                {
                    int col = colNumbers[field];
                    var cellValue = worksheet.Cells[row, col].Text;

                    // Cell has non-empty value and matches expected pattern (if available)
                    if (!string.IsNullOrWhiteSpace(cellValue))
                    {
                        if (_contentPatterns.ContainsKey(field))
                        {
                            if (_contentPatterns[field].IsMatch(cellValue))
                            {
                                hasData = true;
                                break;
                            }
                        }
                        else
                        {
                            // If no pattern to check, any non-empty value counts
                            hasData = true;
                            break;
                        }
                    }
                }

                if (hasData)
                {
                    return row;
                }
            }

            // If no clear data row found, default to the row after header
            return headerRow + 1;
        }

        /// <summary>
        /// Converts an Excel column letter to column number (e.g., A = 1, Z = 26, AA = 27)
        /// </summary>
        private int GetExcelColumnNumber(string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return 0;

            int result = 0;
            foreach (char c in columnName)
            {
                result = result * 26 + (c - 'A' + 1);
            }
            return result;
        }

        /// <summary>
        /// Legacy method for backward compatibility
        /// </summary>
        public Dictionary<string, string> DetectColumnMappings(int headerRow = 1)
        {
            var result = DetectColumnMappingsAndFirstDataRow(headerRow);
            return result.ColumnMappings;
        }

        /// <summary>
        /// Detect columns based on header row text
        /// </summary>
        private Dictionary<string, string> DetectFromHeaders(
            ExcelWorksheet worksheet, int headerRow, int colCount)
        {
            var result = new Dictionary<string, string>();
            var headerValues = new Dictionary<int, string>();

            // Get all header values
            for (int col = 1; col <= colCount; col++)
            {
                var cellValue = worksheet.Cells[headerRow, col].Text;
                if (!string.IsNullOrWhiteSpace(cellValue))
                {
                    headerValues[col] = cellValue.Trim().ToLowerInvariant();
                }
            }

            // Match headers against known field patterns
            foreach (var field in _fieldMatches.Keys)
            {
                var possibleMatches = _fieldMatches[field];

                // Find the best matching column
                var bestMatch = headerValues
                    .Where(h => possibleMatches.Any(m =>
                        h.Value.Contains(m) ||
                        LevenshteinDistance(h.Value, m) <= 2)) // Allow for minor typos
                    .OrderBy(h =>
                        possibleMatches.Min(m =>
                            h.Value == m ? 0 : // Exact match is best
                            h.Value.Contains(m) ? 1 : // Containing the term is next best
                            LevenshteinDistance(h.Value, m))) // Otherwise, closest match
                    .FirstOrDefault();

                if (bestMatch.Key > 0)
                {
                    result[field] = GetExcelColumnName(bestMatch.Key);
                    _logger.LogInfo($"Detected '{field}' in column {GetExcelColumnName(bestMatch.Key)} " +
                              $"based on header '{bestMatch.Value}'");
                }
            }

            return result;
        }

        /// <summary>
        /// Detect columns based on content patterns
        /// </summary>
        private Dictionary<string, string> DetectFromContent(
            ExcelWorksheet worksheet, int startRow, int colCount, List<string> fieldsToDetect)
        {
            var result = new Dictionary<string, string>();
            var sampleSize = Math.Min(20, worksheet.Dimension.Rows - startRow + 1); // Analyze up to 20 rows

            // For each column, calculate how many rows match our patterns
            var columnScores = new Dictionary<string, Dictionary<int, int>>();

            foreach (var field in fieldsToDetect)
            {
                if (!_contentPatterns.ContainsKey(field))
                    continue;

                var pattern = _contentPatterns[field];
                columnScores[field] = new Dictionary<int, int>();

                for (int col = 1; col <= colCount; col++)
                {
                    int matchCount = 0;

                    for (int row = startRow; row < startRow + sampleSize; row++)
                    {
                        var cellValue = worksheet.Cells[row, col].Text;
                        if (!string.IsNullOrWhiteSpace(cellValue) && pattern.IsMatch(cellValue))
                        {
                            matchCount++;
                        }
                    }

                    // Only count columns with at least 50% matches
                    if (matchCount >= sampleSize * 0.5)
                    {
                        columnScores[field][col] = matchCount;
                    }
                }
            }

            // For each field, find the column with the highest score
            foreach (var field in fieldsToDetect)
            {
                if (!columnScores.ContainsKey(field) || !columnScores[field].Any())
                    continue;

                var bestColumn = columnScores[field]
                    .OrderByDescending(s => s.Value)
                    .First().Key;

                result[field] = GetExcelColumnName(bestColumn);
                _logger.LogInfo($"Detected '{field}' in column {GetExcelColumnName(bestColumn)} " +
                          $"based on content pattern with {columnScores[field][bestColumn]} matches");
            }

            return result;
        }

        /// <summary>
        /// Converts a column number to Excel column letter (e.g., 1 = A, 27 = AA)
        /// </summary>
        private string GetExcelColumnName(int columnNumber)
        {
            string columnName = string.Empty;
            while (columnNumber > 0)
            {
                int remainder = (columnNumber - 1) % 26;
                columnName = (char)('A' + remainder) + columnName;
                columnNumber = (columnNumber - 1) / 26;
            }
            return columnName;
        }

        /// <summary>
        /// Calculate Levenshtein distance between two strings
        /// </summary>
        private int LevenshteinDistance(string s, string t)
        {
            // Simple implementation of Levenshtein distance for fuzzy matching
            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            if (n == 0) return m;
            if (m == 0) return n;

            for (int i = 0; i <= n; i++)
                d[i, 0] = i;

            for (int j = 0; j <= m; j++)
                d[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[n, m];
        }
    }
}