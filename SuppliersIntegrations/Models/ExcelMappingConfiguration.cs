using System.Collections.Generic;
using System.Windows.Controls;
using System;

namespace BOMVIEW.Models
{
    public class ExcelMappingConfiguration
    {
        public bool UseQuantityBuffer { get; set; }
        public string SelectedSheet { get; set; }
        public int StartRow { get; set; }
        public decimal AssemblyQuantity { get; set; } = 1m;
        public Dictionary<string, string> ColumnMappings { get; set; }
        public HashSet<string> MandatoryFields { get; set; }

        public ExcelMappingConfiguration()
        {
            ColumnMappings = new Dictionary<string, string>();
            MandatoryFields = new HashSet<string> { "OrderingCode" };
            UseQuantityBuffer = false; // Default value
        }

        public string GetColumnForField(string field)
        {
            return ColumnMappings.TryGetValue(field, out var column) ? column : string.Empty;
        }

        public int CalculateBufferedQuantity(int baseQuantity)
        {
            if (!UseQuantityBuffer) return baseQuantity;

            decimal bufferedQty = baseQuantity * 1.1m; // Add 10%
            return (int)Math.Ceiling(bufferedQty); // Round up to nearest integer
        }

        public int CalculateQuantityWithAssembly(int quantityForOne)
        {
            // First multiply by assembly quantity
            decimal totalQty = quantityForOne * AssemblyQuantity;

            // Then apply buffer if enabled
            if (UseQuantityBuffer)
            {
                totalQty = totalQty * 1.1m; // Add 10%
            }

            return (int)Math.Ceiling(totalQty); // Round up to nearest integer
        }
    }
}

public class ExcelSheetPreview
{
    public List<string> SheetNames { get; set; } = new();
    public List<string> Headers { get; set; } = new();
    public List<string> ColumnLetters { get; set; } = new();
    public List<List<string>> PreviewRows { get; set; } = new();
}