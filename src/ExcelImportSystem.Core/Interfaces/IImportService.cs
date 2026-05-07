using ExcelImportSystem.Core.DTOs;

namespace ExcelImportSystem.Core.Interfaces;

public interface IImportService
{
    Task<ImportPreviewDto> PreviewAsync(ImportRequestDto request);
    Task<ImportResultDto> ExecuteAsync(int userId, ImportRequestDto request, List<ColumnMappingDto> mappings);
}
