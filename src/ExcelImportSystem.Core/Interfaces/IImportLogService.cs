using ExcelImportSystem.Core.DTOs;

namespace ExcelImportSystem.Core.Interfaces;

public interface IImportLogService
{
    Task<PagedResult<ImportLogDto>> GetLogsAsync(int page, int pageSize, string? tableName = null, string? status = null, DateTime? from = null, DateTime? to = null);
    Task<ImportLogDto?> GetLogByIdAsync(int id);
    Task<DashboardStatsDto> GetDashboardStatsAsync(int? userId);
}
