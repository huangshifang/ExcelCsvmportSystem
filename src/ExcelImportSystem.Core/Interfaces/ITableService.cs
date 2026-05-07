using ExcelImportSystem.Core.DTOs;

namespace ExcelImportSystem.Core.Interfaces;

public interface ITableService
{
    Task<List<DatabaseInfoDto>> GetDatabasesAsync();
    Task<List<TableInfoDto>> GetTablesAsync(string database, string? schema = null);
    Task<TableInfoDto?> GetTableAsync(string database, string tableName, string schema = "dbo");
}
