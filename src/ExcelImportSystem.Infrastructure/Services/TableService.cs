using System.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ExcelImportSystem.Core.DTOs;
using ExcelImportSystem.Core.Interfaces;
using ExcelImportSystem.Infrastructure.Data;

namespace ExcelImportSystem.Infrastructure.Services;

public partial class TableService : ITableService
{
    private readonly AppDbContext _context;
    private readonly IDatabaseAccessService _dbAccessService;
    private readonly IConnectionFactory _connectionFactory;
    private readonly ILogger<TableService> _logger;

    public TableService(AppDbContext context, IDatabaseAccessService dbAccessService,
        IConnectionFactory connectionFactory, ILogger<TableService> logger)
    {
        _context = context;
        _dbAccessService = dbAccessService;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <summary>Validates a SQL identifier used as a raw object name (database/schema).</summary>
    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_@$#]{0,127}$")]
    private static partial Regex SafeSqlName();

    /// <summary>Validates a table name used in parameterized queries. Allows Unicode (e.g. Chinese).</summary>
    private static void AssertValidTableName(string tableName)
    {
        if (string.IsNullOrEmpty(tableName) || tableName.Contains(']') || tableName.Length > 128)
            throw new ArgumentException($"Invalid table name: {tableName}");
    }

    private static string Quote(string name) => $"[{name}]";

    /// <summary>
    /// Safely builds a 3-part name.
    /// Throws if any component contains unsafe characters (SQL injection guard).
    /// </summary>
    private static string QualifiedName(string database, string schema, string table)
    {
        if (!SafeSqlName().IsMatch(database))
            throw new ArgumentException($"Invalid database name: {database}");
        if (!SafeSqlName().IsMatch(schema))
            throw new ArgumentException($"Invalid schema name: {schema}");
        AssertValidTableName(table);
        return $"{Quote(database)}.{Quote(schema)}.{Quote(table)}";
    }

    private static (string db, string schema, string table) ParseTableRef(string database, string tableName, string schema)
    {
        return (database, schema, tableName);
    }

    public async Task<List<DatabaseInfoDto>> GetDatabasesAsync(int? userId = null)
    {
        var allDbs = new List<DatabaseInfoDto>();

        // Local server
        var localConn = _context.Database.GetDbConnection();
        await localConn.OpenAsync();
        try
        {
            using var cmd = localConn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sys.databases WHERE state = 0 AND HAS_DBACCESS(name) = 1 ORDER BY name";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                allDbs.Add(new DatabaseInfoDto { Name = reader.GetString(0), ServerId = null, ServerName = null });
        }
        finally { localConn.Close(); }

        // Remote servers
        var servers = await _connectionFactory.GetServersAsync();
        foreach (var server in servers.Where(s => s.IsActive))
        {
            try
            {
                using var conn = await _connectionFactory.CreateConnectionAsync(server.Id);
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT name FROM sys.databases WHERE state = 0 AND HAS_DBACCESS(name) = 1 ORDER BY name";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    allDbs.Add(new DatabaseInfoDto { Name = reader.GetString(0), ServerId = server.Id, ServerName = server.Name });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to list databases on server '{Server}'", server.Name);
            }
        }

        // Non-admin filtering
        if (userId.HasValue)
        {
            var userRoles = await _context.UserRoles
                .Where(ur => ur.UserId == userId.Value).Select(ur => ur.Role.Name).ToListAsync();
            if (!userRoles.Contains("Admin"))
            {
                var accesses = await _dbAccessService.GetUserTableAccessesAsync(userId.Value);
                var grantedSet = new HashSet<(int? Sid, string Db)>(
                    accesses.Select(a => ((int?)a.ServerId, a.DatabaseName)));
                allDbs = allDbs.Where(d => grantedSet.Contains((d.ServerId, d.Name))).ToList();
            }
        }

        return allDbs.OrderBy(d => d.ServerName ?? "").ThenBy(d => d.Name).ToList();
    }

    public async Task<List<TableInfoDto>> GetTablesAsync(string database, string? schema = null, int? userId = null, int? serverId = null)
    {
        if (!SafeSqlName().IsMatch(database))
            throw new ArgumentException($"Invalid database name: {database}");

        var connection = await _connectionFactory.CreateConnectionAsync(serverId);
        await connection.OpenAsync();
        try
        {
            connection.ChangeDatabase(database);

            var whereSchema = string.IsNullOrEmpty(schema)
                ? ""
                : $"AND TABLE_SCHEMA = @schema";

            var sql = $@"
                SELECT TABLE_SCHEMA, TABLE_NAME
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_TYPE = 'BASE TABLE'
                {whereSchema}
                ORDER BY TABLE_SCHEMA, TABLE_NAME";

            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;

            if (!string.IsNullOrEmpty(schema))
            {
                var sp = cmd.CreateParameter();
                sp.ParameterName = "@schema";
                sp.Value = schema;
                cmd.Parameters.Add(sp);
            }

            var tables = new List<TableInfoDto>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(new TableInfoDto
                {
                    Database = database,
                    Schema = reader.GetString(0),
                    TableName = reader.GetString(1)
                });
            }

            // Filter tables by access for non-admin users
            if (userId.HasValue)
            {
                var userRoles = await _context.UserRoles
                    .Where(ur => ur.UserId == userId.Value)
                    .Select(ur => ur.Role.Name)
                    .ToListAsync();

                if (!userRoles.Contains("Admin"))
                {
                    var accesses = await _dbAccessService.GetUserTableAccessesAsync(userId.Value);
                    var wildcardDb = accesses.Any(a =>
                        a.DatabaseName.Equals(database, StringComparison.OrdinalIgnoreCase)
                        && a.TableName == null);

                    if (!wildcardDb)
                    {
                        var grantedTables = accesses
                            .Where(a => a.DatabaseName.Equals(database, StringComparison.OrdinalIgnoreCase)
                                && a.TableName != null)
                            .Select(a => (Schema: a.SchemaName ?? "dbo", TableName: a.TableName!))
                            .ToHashSet();

                        tables = tables.Where(t =>
                            grantedTables.Contains((t.Schema, t.TableName))
                        ).ToList();
                    }
                }
            }

            return tables;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query tables in database '{Db}'", database);
            throw;
        }
        finally
        {
            connection.Close();
        }
    }

    public async Task<TableInfoDto?> GetTableAsync(string database, string tableName, string schema = "dbo", int? userId = null, int? serverId = null)
    {
        if (!SafeSqlName().IsMatch(database))
            throw new ArgumentException($"Invalid database name: {database}");
        if (!SafeSqlName().IsMatch(schema))
            throw new ArgumentException($"Invalid schema name: {schema}");
        AssertValidTableName(tableName);

        var connection = await _connectionFactory.CreateConnectionAsync(serverId);
        await connection.OpenAsync();
        try
        {
            connection.ChangeDatabase(database);

            // 1. Columns
            var columns = new List<ColumnInfoDto>();
            var colSql = @"
                SELECT COLUMN_NAME, DATA_TYPE,
                       CASE WHEN IS_NULLABLE = 'YES' THEN 1 ELSE 0 END,
                       CHARACTER_MAXIMUM_LENGTH
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = @tableName AND TABLE_SCHEMA = @schema
                ORDER BY ORDINAL_POSITION";

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = colSql;
                AddParam(cmd, "@tableName", tableName);
                AddParam(cmd, "@schema", schema);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    columns.Add(new ColumnInfoDto
                    {
                        ColumnName = reader.GetString(0),
                        DataType = reader.GetString(1),
                        IsNullable = reader.GetInt32(2) == 1,
                        MaxLength = reader.IsDBNull(3) ? null : (int?)reader.GetInt32(3)
                    });
                }
            }

            if (columns.Count == 0) return null;

            // 2. Primary keys
            var pkColumns = new List<string>();
            var pkSql = @"
                SELECT c.name
                FROM sys.indexes i
                JOIN sys.index_columns ic
                    ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                JOIN sys.columns c
                    ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                JOIN sys.tables t
                    ON i.object_id = t.object_id
                WHERE i.is_primary_key = 1 AND t.name = @tableName";

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = pkSql;
                AddParam(cmd, "@tableName", tableName);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    pkColumns.Add(reader.GetString(0));
            }

            // 3. Identity columns
            var identityColumns = new List<string>();
            var idSql = @"
                SELECT c.name
                FROM sys.identity_columns ic
                JOIN sys.columns c
                    ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                JOIN sys.tables t
                    ON c.object_id = t.object_id
                WHERE t.name = @tableName";

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = idSql;
                AddParam(cmd, "@tableName", tableName);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    identityColumns.Add(reader.GetString(0));
            }

            foreach (var col in columns)
            {
                col.IsPrimaryKey = pkColumns.Contains(col.ColumnName, StringComparer.OrdinalIgnoreCase);
                col.IsIdentity = identityColumns.Contains(col.ColumnName, StringComparer.OrdinalIgnoreCase);
            }

            return new TableInfoDto
            {
                Database = database,
                Schema = schema,
                TableName = tableName,
                Columns = columns
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get table info for {Db}.{Schema}.{Table}",
                database, schema, tableName);
            throw;
        }
        finally
        {
            connection.Close();
        }
    }

    private static void AddParam(IDbCommand cmd, string name, string value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
