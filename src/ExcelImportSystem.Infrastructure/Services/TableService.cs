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
    private readonly ILogger<TableService> _logger;

    public TableService(AppDbContext context, ILogger<TableService> logger)
    {
        _context = context;
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

    public async Task<List<DatabaseInfoDto>> GetDatabasesAsync()
    {
        var connection = _context.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT name FROM sys.databases WHERE state = 0 ORDER BY name";

            var list = new List<DatabaseInfoDto>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add(new DatabaseInfoDto { Name = reader.GetString(0) });
            return list;
        }
        finally
        {
            connection.Close();
        }
    }

    public async Task<List<TableInfoDto>> GetTablesAsync(string database, string? schema = null)
    {
        var connection = _context.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            // Build dynamic SQL with 3-part naming — names are validated by QualifiedName
            var whereSchema = string.IsNullOrEmpty(schema)
                ? ""
                : $"AND TABLE_SCHEMA = @schema";

            var sql = $@"
                SELECT TABLE_SCHEMA, TABLE_NAME
                FROM {Quote(database)}.INFORMATION_SCHEMA.TABLES
                WHERE TABLE_TYPE = 'BASE TABLE'
                {whereSchema}
                ORDER BY TABLE_SCHEMA, TABLE_NAME";

            if (!SafeSqlName().IsMatch(database))
                throw new ArgumentException($"Invalid database name: {database}");

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

    public async Task<TableInfoDto?> GetTableAsync(string database, string tableName, string schema = "dbo")
    {
        var connection = _context.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            if (!SafeSqlName().IsMatch(database))
                throw new ArgumentException($"Invalid database name: {database}");
            if (!SafeSqlName().IsMatch(schema))
                throw new ArgumentException($"Invalid schema name: {schema}");
            AssertValidTableName(tableName);

            // 1. Columns
            var columns = new List<ColumnInfoDto>();
            var colSql = $@"
                SELECT COLUMN_NAME, DATA_TYPE,
                       CASE WHEN IS_NULLABLE = 'YES' THEN 1 ELSE 0 END,
                       CHARACTER_MAXIMUM_LENGTH
                FROM {Quote(database)}.INFORMATION_SCHEMA.COLUMNS
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

            // 2. Primary keys — use sys schema directly for cross-db compatibility
            var pkColumns = new List<string>();
            var pkSql = $@"
                SELECT c.name
                FROM {Quote(database)}.sys.indexes i
                JOIN {Quote(database)}.sys.index_columns ic
                    ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                JOIN {Quote(database)}.sys.columns c
                    ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                JOIN {Quote(database)}.sys.tables t
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
            var idSql = $@"
                SELECT c.name
                FROM {Quote(database)}.sys.identity_columns ic
                JOIN {Quote(database)}.sys.columns c
                    ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                JOIN {Quote(database)}.sys.tables t
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
