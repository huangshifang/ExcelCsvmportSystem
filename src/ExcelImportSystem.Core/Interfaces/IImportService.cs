using ExcelImportSystem.Core.DTOs;

namespace ExcelImportSystem.Core.Interfaces;

public interface IImportService
{
    Task<ImportPreviewDto> PreviewAsync(int userId, ImportRequestDto request);
    ImportExecuteResponseDto ExecuteAsync(int userId, ImportRequestDto request, List<ColumnMappingDto> mappings);
    ImportProgressDto? GetProgress(string taskId);
}
