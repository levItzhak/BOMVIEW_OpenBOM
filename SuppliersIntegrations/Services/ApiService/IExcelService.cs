using BOMVIEW.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BOMVIEW.Services
{
    public interface IExcelService
    {
        ExcelSheetPreview GetExcelPreview(string filePath);
        Task<List<BomEntry>> ReadBomFileAsync(string filePath, ExcelMappingConfiguration config);
        Task SaveBomFileAsync(string filePath, List<BomEntry> entries);
        bool ValidateExcelFile(string filePath);
        Task SaveExternalSuppliersSheetAsync(string filePath, List<ExternalSupplierEntry> entries);
    }
}