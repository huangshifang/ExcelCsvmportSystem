using Microsoft.Data.SqlClient;
using ExcelImportSystem.Core.DTOs;

namespace ExcelImportSystem.Core.Interfaces;

public interface IConnectionFactory
{
    Task<SqlConnection> CreateConnectionAsync(int? serverId);
    Task<List<SqlServerInstanceDto>> GetServersAsync();
    Task<SqlServerInstanceDto?> GetServerAsync(int id);
    Task<int?> ResolveServerIdAsync(string databaseName);
    void InvalidateCache();
}
