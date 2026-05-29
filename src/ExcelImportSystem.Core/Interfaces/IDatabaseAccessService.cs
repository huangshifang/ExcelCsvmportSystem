using ExcelImportSystem.Core.DTOs;

namespace ExcelImportSystem.Core.Interfaces;

public interface IDatabaseAccessService
{
    Task<List<string>> GetUserDatabasesAsync(int userId);
    Task<List<UserTableAccessDto>> GetUserTableAccessesAsync(int userId);
    Task GrantAccessAsync(int userId, string databaseName, string? schemaName, string? tableName, string grantedBy, int? serverId = null);
    Task RevokeAccessAsync(int userId, string databaseName, string? schemaName, string? tableName, int? serverId = null);
    Task SetUserTableAccessesAsync(int userId, List<UserTableAccessDto> accesses, string grantedBy);
    Task<bool> HasAccessAsync(int userId, string databaseName, string? schemaName = null, string? tableName = null, int? serverId = null);
}
