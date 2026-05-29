using ExcelImportSystem.Core.DTOs;

namespace ExcelImportSystem.Core.Interfaces;

public interface ILoginAuditService
{
    Task LogAsync(string username, bool success, string? failureReason);
    Task<PagedResult<LoginAuditLogDto>> GetLogsAsync(int page, int pageSize, string? username = null, bool? success = null, DateTime? from = null, DateTime? to = null);
}
