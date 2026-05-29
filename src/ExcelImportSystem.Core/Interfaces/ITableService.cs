using ExcelImportSystem.Core.DTOs;

namespace ExcelImportSystem.Core.Interfaces;

public interface ITableService
{
    Task<List<DatabaseInfoDto>> GetDatabasesAsync(int? userId = null);
    Task<List<TableInfoDto>> GetTablesAsync(string database, string? schema = null, int? userId = null, int? serverId = null);
    Task<TableInfoDto?> GetTableAsync(string database, string tableName, string schema = "dbo", int? userId = null, int? serverId = null);
}
