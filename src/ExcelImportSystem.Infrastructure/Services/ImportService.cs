using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using ExcelImportSystem.Core.DTOs;
using ExcelImportSystem.Core.Entities;
using ExcelImportSystem.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using ExcelImportSystem.Infrastructure.Data;

namespace ExcelImportSystem.Infrastructure.Services;

public class ImportService : IImportService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImportService> _logger;
    private static readonly ConcurrentDictionary<string, ImportProgressDto> _progressStore = new();

    public ImportService(IServiceScopeFactory scopeFactory, ILogger<ImportService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    private static bool IsCsvFile(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext == ".csv" || ext == ".tsv" || ext == ".txt";
    }

    private static bool IsExcelFile(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext == ".xlsx" || ext == ".xls";
    }

    // ============================================================
    // Preview
    // ============================================================
    public async Task<ImportPreviewDto> PreviewAsync(int userId, ImportRequestDto request)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tableService = scope.ServiceProvider.GetRequiredService<ITableService>();
        var dbAccessService = scope.ServiceProvider.GetRequiredService<IDatabaseAccessService>();

        await ValidateAccess(context, dbAccessService, userId, request.Database, request.Schema, request.TableName, request.ServerId);

        var table = await tableService.GetTableAsync(request.Database, request.TableName, request.Schema, serverId: request.ServerId)
            ?? throw new KeyNotFoundException($"Table '{request.Database}.{request.Schema}.{request.TableName}' not found");

        var (excelColumns, sampleData, totalRows) = IsCsvFile(request.File.FileName)
            ? ReadCsvPreview(request)
            : ReadExcelPreview(request);

        var autoMappings = new List<ColumnMappingDto>();
        foreach (var excelCol in excelColumns)
        {
            var match = table.Columns.Find(c =>
                c.ColumnName.Equals(excelCol, StringComparison.OrdinalIgnoreCase) && !c.IsIdentity);
            if (match != null)
                autoMappings.Add(new ColumnMappingDto { ExcelColumn = excelCol, TableColumn = match.ColumnName });
        }

        return new ImportPreviewDto
        {
            ExcelColumns = excelColumns,
            SampleData = sampleData,
            TableColumns = table.Columns,
            AutoMappings = autoMappings,
            TotalRows = totalRows
        };
    }

    // ============================================================
    // Execute (fire-and-forget, returns taskId immediately)
    // ============================================================
    public ImportExecuteResponseDto ExecuteAsync(int userId, ImportRequestDto request, List<ColumnMappingDto> mappings)
    {
        var taskId = Guid.NewGuid().ToString("N");

        _progressStore[taskId] = new ImportProgressDto
        {
            TaskId = taskId,
            Status = "pending",
            Message = "Queued..."
        };

        // Capture what we need for the background task
        var scopeFactory = _scopeFactory;
        var logger = _logger;
        var store = _progressStore;
        // Copy stream to memory so the request-scoped IFormFile stream doesn't get disposed
        using var ms = new MemoryStream();
        request.File.CopyTo(ms);
        var fileBytes = ms.ToArray();
        var fileName = request.File.FileName;

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tableService = scope.ServiceProvider.GetRequiredService<ITableService>();
            var dbAccessService = scope.ServiceProvider.GetRequiredService<IDatabaseAccessService>();
            var connectionFactory = scope.ServiceProvider.GetRequiredService<IConnectionFactory>();

            try
            {
                await ExecuteInternalAsync(taskId, userId, request, mappings, fileBytes, fileName,
                    context, tableService, dbAccessService, connectionFactory, logger, store);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Import task {TaskId} failed unexpectedly", taskId);
                store[taskId] = new ImportProgressDto
                {
                    TaskId = taskId,
                    Status = "failed",
                    Percent = 100,
                    Message = $"Unexpected error: {ex.Message}",
                    Result = new ImportResultDto { Success = false, Message = ex.Message }
                };
            }
        });

        return new ImportExecuteResponseDto { TaskId = taskId };
    }

    // ============================================================
    // Progress
    // ============================================================
    public ImportProgressDto? GetProgress(string taskId)
    {
        _progressStore.TryGetValue(taskId, out var progress);
        return progress;
    }

    // ============================================================
    // Internal execution
    // ============================================================
    private static async Task ExecuteInternalAsync(
        string taskId, int userId, ImportRequestDto request, List<ColumnMappingDto> mappings,
        byte[] fileBytes, string fileName,
        AppDbContext context, ITableService tableService, IDatabaseAccessService dbAccessService,
        IConnectionFactory connectionFactory,
        ILogger<ImportService> logger, ConcurrentDictionary<string, ImportProgressDto> store)
    {
        void UpdateProgress(string status, int percent, string message, int totalRows = 0, int processed = 0, int errors = 0, ImportResultDto? result = null)
        {
            store[taskId] = new ImportProgressDto
            {
                TaskId = taskId,
                Status = status,
                Percent = percent,
                TotalRows = totalRows,
                ProcessedRows = processed,
                Message = message,
                ErrorCount = errors,
                Result = result
            };
        }

        // Validate access
        var userRoles = await context.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.Role.Name)
            .ToListAsync();
        if (!userRoles.Contains("Admin"))
        {
            var hasAccess = await dbAccessService.HasAccessAsync(userId, request.Database, request.Schema, request.TableName, serverId: request.ServerId);
            if (!hasAccess)
                throw new UnauthorizedAccessException($"Access denied to table '{request.Database}.{request.Schema}.{request.TableName}'.");
        }

        var table = await tableService.GetTableAsync(request.Database, request.TableName, request.Schema, serverId: request.ServerId)
            ?? throw new KeyNotFoundException($"Table not found");

        var user = await context.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        UpdateProgress("reading", 5, "Reading file...");

        // Parse file into DataTable
        DataTable dataTable;
        var totalRows = 0;
        if (IsCsvFile(fileName))
        {
            (dataTable, totalRows) = ReadCsvData(fileBytes, request, mappings);
        }
        else if (IsExcelFile(fileName))
        {
            (dataTable, totalRows) = ReadExcelData(fileBytes, request, mappings, percent =>
            {
                UpdateProgress("reading", 5 + (int)(percent * 0.20), $"Reading Excel... {percent}%", totalRows, 0);
            });
        }
        else
        {
            throw new InvalidDataException($"Unsupported file format: {Path.GetExtension(fileName)}");
        }

        if (dataTable.Rows.Count == 0)
            throw new InvalidDataException("No data rows found in file");

        UpdateProgress("importing", 30, $"Writing {dataTable.Rows.Count} rows to database...", dataTable.Rows.Count, 0);

        var result = new ImportResultDto { Success = true, TotalRows = dataTable.Rows.Count };
        var connection = await connectionFactory.CreateConnectionAsync(request.ServerId);
        await connection.OpenAsync();
        connection.ChangeDatabase(request.Database);

        DbTransaction? transaction = null;
        try
        {
            if (request.UseTransaction)
                transaction = await connection.BeginTransactionAsync();

            using var bulkCopy = new SqlBulkCopy(
                (SqlConnection)connection,
                request.UseTransaction ? SqlBulkCopyOptions.Default : SqlBulkCopyOptions.TableLock,
                (SqlTransaction?)transaction)
            {
                DestinationTableName = $"[{request.Schema}].[{request.TableName}]",
                BatchSize = Math.Min(request.BatchSize, dataTable.Rows.Count),
                NotifyAfter = Math.Max(1, dataTable.Rows.Count / 20),
                BulkCopyTimeout = 600
            };

            // Wire up progress notification
            var processed = 0;
            bulkCopy.SqlRowsCopied += (_, e) =>
            {
                processed = (int)e.RowsCopied;
                var pct = 30 + (int)(processed / (double)dataTable.Rows.Count * 65);
                UpdateProgress("importing", Math.Min(pct, 95), $"Imported {processed:N0} / {dataTable.Rows.Count:N0} rows...",
                    dataTable.Rows.Count, processed);
            };

            // Map columns
            foreach (DataColumn col in dataTable.Columns)
            {
                bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);
            }

            await bulkCopy.WriteToServerAsync(dataTable);

            if (transaction is not null)
                await transaction.CommitAsync();

            result.ImportedRows = dataTable.Rows.Count;
            result.FailedRows = 0;
            result.Message = $"Successfully imported {dataTable.Rows.Count:N0} rows";

            UpdateProgress("completed", 100, result.Message, dataTable.Rows.Count, dataTable.Rows.Count, 0, result);
        }
        catch (Exception ex)
        {
            if (transaction is not null) await transaction.RollbackAsync();
            logger.LogError(ex, "Bulk copy failed for task {TaskId}", taskId);

            if (!request.UseTransaction)
            {
                // Fall back to row-by-row for error isolation
                logger.LogInformation("Falling back to row-by-row import for task {TaskId}", taskId);
                await FallbackRowImport(dataTable, request, mappings, connection, result, percent =>
                {
                    UpdateProgress("importing", 30 + (int)(percent * 60), $"Row-by-row import: {percent}%",
                        dataTable.Rows.Count, result.ImportedRows, result.FailedRows);
                });
                UpdateProgress(result.Success ? "completed" : "failed", 100, result.Message,
                    result.TotalRows, result.ImportedRows, result.FailedRows, result);
            }
            else
            {
                result.Success = false;
                result.Message = $"Bulk import failed: {ex.Message}";
                result.FailedRows = dataTable.Rows.Count;
                UpdateProgress("failed", 100, result.Message, dataTable.Rows.Count, 0, (int)result.FailedRows, result);
            }
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
            if (connection.State == ConnectionState.Open)
                connection.Close();
        }

        // Write import log
        string? serverName = null;
        if (request.ServerId.HasValue)
        {
            serverName = (await context.SqlServerInstances
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == request.ServerId.Value))?.Name;
        }
        var log = new ImportLog
        {
            UserId = userId,
            UserName = user.DisplayName,
            TargetTable = $"{request.Database}.{request.Schema}.{request.TableName}",
            FileName = fileName,
            TotalRows = result.TotalRows,
            SuccessRows = result.ImportedRows,
            FailedRows = result.FailedRows,
            Status = result.Success ? "Success" : (result.ImportedRows > 0 ? "Partial" : "Failed"),
            ErrorMessage = result.Errors.Count > 0 ? string.Join("; ", result.Errors.Take(5)) : null,
            ServerId = request.ServerId,
            ServerName = serverName,
            ImportedAt = DateTime.UtcNow
        };
        context.ImportLogs.Add(log);
        await context.SaveChangesAsync();
        result.ImportLogId = log.Id;

        logger.LogInformation("Import task {TaskId} completed: {Rows} rows, {Failed} failed", taskId, result.ImportedRows, result.FailedRows);
    }

    // ============================================================
    // CSV reading
    // ============================================================
    private static (List<string> columns, List<Dictionary<string, string>> sample, int totalRows) ReadCsvPreview(ImportRequestDto request)
    {
        using var reader = new StreamReader(request.File.OpenReadStream());
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = request.HasHeaderRow,
            MissingFieldFound = null,
            BadDataFound = null,
        });

        csv.Read();

        List<string> columns;
        if (request.HasHeaderRow)
        {
            csv.ReadHeader();
            columns = csv.HeaderRecord?.ToList() ?? new List<string>();
        }
        else
        {
            // No header row — generate column names and re-read the first record as data
            var fieldCount = csv.ColumnCount;
            columns = Enumerable.Range(1, fieldCount).Select(i => $"Column{i}").ToList();
        }

        if (columns.Count == 0)
            return (columns, new List<Dictionary<string, string>>(), 0);

        var sample = new List<Dictionary<string, string>>();
        var totalRows = 0;

        // If no header, the first record was already read above and is data
        if (!request.HasHeaderRow && csv.Parser.RawRecord != null)
        {
            totalRows++;
            if (sample.Count < 5)
            {
                var row = new Dictionary<string, string>();
                for (int i = 0; i < columns.Count; i++)
                    row[columns[i]] = csv.GetField(i) ?? "";
                sample.Add(row);
            }
        }

        while (csv.Read())
        {
            totalRows++;
            if (sample.Count < 5)
            {
                var row = new Dictionary<string, string>();
                for (int i = 0; i < columns.Count; i++)
                    row[columns[i]] = csv.GetField(i) ?? "";
                sample.Add(row);
            }
        }

        return (columns, sample, totalRows);
    }

    private static (DataTable table, int totalRows) ReadCsvData(byte[] fileBytes, ImportRequestDto request, List<ColumnMappingDto> mappings)
    {
        var table = new DataTable();
        var mappingDict = mappings.Where(m => !string.IsNullOrEmpty(m.TableColumn)).ToDictionary(
            m => m.ExcelColumn, m => m.TableColumn, StringComparer.OrdinalIgnoreCase);
        foreach (var dbCol in mappingDict.Values.Distinct(StringComparer.OrdinalIgnoreCase))
            table.Columns.Add(dbCol);

        using var reader = new StreamReader(new MemoryStream(fileBytes));
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = request.HasHeaderRow,
            MissingFieldFound = null,
            BadDataFound = null,
        });

        csv.Read();

        string[] csvColumns;
        if (request.HasHeaderRow)
        {
            csv.ReadHeader();
            csvColumns = csv.HeaderRecord ?? Array.Empty<string>();
        }
        else
        {
            csvColumns = Enumerable.Range(1, csv.ColumnCount).Select(i => $"Column{i}").ToArray();
        }

        if (csvColumns.Length == 0)
            return (table, 0);

        var colIndexes = new List<(int CsvIdx, string DbCol)>();
        for (int i = 0; i < csvColumns.Length; i++)
        {
            if (mappingDict.TryGetValue(csvColumns[i], out var dbCol))
                colIndexes.Add((i, dbCol));
        }

        var totalRows = 0;

        // If no header, the first record was already read and is data
        if (!request.HasHeaderRow && csv.Parser.RawRecord != null)
        {
            var row = table.NewRow();
            foreach (var (csvIdx, dbCol) in colIndexes)
                row[dbCol] = (object?)csv.GetField(csvIdx) ?? DBNull.Value;
            table.Rows.Add(row);
            totalRows++;
        }

        while (csv.Read())
        {
            var row = table.NewRow();
            foreach (var (csvIdx, dbCol) in colIndexes)
            {
                row[dbCol] = (object?)csv.GetField(csvIdx) ?? DBNull.Value;
            }
            table.Rows.Add(row);
            totalRows++;
        }

        return (table, totalRows);
    }

    // ============================================================
    // Excel reading
    // ============================================================

    /// <summary>
    /// Finds the first worksheet with actual data, skipping sheets that only
    /// have an empty A1 cell (common when Excel files contain multiple sheets
    /// and the data lives on sheet 2+).
    /// </summary>
    private static ExcelWorksheet GetFirstDataWorksheet(ExcelPackage package)
    {
        foreach (var ws in package.Workbook.Worksheets)
        {
            if (ws.Dimension == null)
                continue;
            if (ws.Dimension.Rows > 1 || ws.Dimension.Columns > 1)
                return ws;
            if (!string.IsNullOrEmpty(ws.Cells[1, 1].Text))
                return ws;
        }
        return package.Workbook.Worksheets[0]
            ?? throw new InvalidDataException("Excel file is empty");
    }

    private static (List<string> columns, List<Dictionary<string, string>> sample, int totalRows) ReadExcelPreview(ImportRequestDto request)
    {
        using var package = new ExcelPackage(request.File.OpenReadStream());
        var worksheet = GetFirstDataWorksheet(package);
        if (worksheet.Dimension == null)
            return (new List<string>(), new List<Dictionary<string, string>>(), 0);

        var totalRows = worksheet.Dimension.Rows;
        var startRow = request.HasHeaderRow ? 2 : 1;
        var colCount = worksheet.Dimension.Columns;

        var columns = new List<string>();
        for (int col = 1; col <= colCount; col++)
        {
            columns.Add(request.HasHeaderRow
                ? worksheet.Cells[1, col].Text?.Trim() ?? $"Column{col}"
                : $"Column{col}");
        }

        var sample = new List<Dictionary<string, string>>();
        var maxSample = Math.Min(startRow + 4, totalRows);
        for (int row = startRow; row <= maxSample; row++)
        {
            var rowData = new Dictionary<string, string>();
            for (int col = 1; col <= colCount; col++)
                rowData[columns[col - 1]] = worksheet.Cells[row, col].Text?.Trim() ?? "";
            sample.Add(rowData);
        }

        var dataRowCount = request.HasHeaderRow ? totalRows - 1 : totalRows;
        return (columns, sample, Math.Max(0, dataRowCount));
    }

    private static (DataTable table, int totalRows) ReadExcelData(byte[] fileBytes, ImportRequestDto request, List<ColumnMappingDto> mappings, Action<int>? progressCallback = null)
    {
        var table = new DataTable();
        var mappingDict = mappings.Where(m => !string.IsNullOrEmpty(m.TableColumn)).ToDictionary(
            m => m.ExcelColumn, m => m.TableColumn, StringComparer.OrdinalIgnoreCase);
        foreach (var dbCol in mappingDict.Values.Distinct(StringComparer.OrdinalIgnoreCase))
            table.Columns.Add(dbCol);

        using var package = new ExcelPackage(new MemoryStream(fileBytes));
        var worksheet = GetFirstDataWorksheet(package);
        if (worksheet.Dimension == null)
            return (table, 0);

        var totalRows = worksheet.Dimension.Rows;
        var startRow = request.HasHeaderRow ? 2 : 1;
        var colCount = worksheet.Dimension.Columns;

        // Read headers
        var headerCols = new List<string>();
        for (int col = 1; col <= colCount; col++)
        {
            headerCols.Add(request.HasHeaderRow
                ? worksheet.Cells[1, col].Text?.Trim() ?? $"Column{col}"
                : $"Column{col}");
        }

        // Build Excel col index → DataTable col name mapping
        var colMap = new List<(int ExcelCol, string DbCol)>();
        for (int i = 0; i < headerCols.Count; i++)
        {
            if (mappingDict.TryGetValue(headerCols[i], out var dbCol))
                colMap.Add((i + 1, dbCol));
        }

        int lastReportedPercent = -1;
        for (int row = startRow; row <= totalRows; row++)
        {
            var dataRow = table.NewRow();
            bool hasData = false;
            foreach (var (excelCol, dbCol) in colMap)
            {
                var val = worksheet.Cells[row, excelCol].Value;
                dataRow[dbCol] = val ?? DBNull.Value;
                if (val != null && val.ToString() != "")
                    hasData = true;
            }
            if (hasData)
                table.Rows.Add(dataRow);

            // Report progress
            var pct = (int)((row - startRow + 1) / (double)(totalRows - startRow + 1) * 100);
            if (pct != lastReportedPercent)
            {
                lastReportedPercent = pct;
                progressCallback?.Invoke(pct);
            }
        }

        return (table, table.Rows.Count);
    }

    // ============================================================
    // Fallback: row-by-row import (error isolation, non-transaction)
    // ============================================================
    private static async Task FallbackRowImport(
        DataTable dataTable, ImportRequestDto request, List<ColumnMappingDto> mappings,
        DbConnection connection, ImportResultDto result, Action<int> progressCallback)
    {
        var cols = dataTable.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
        var colNames = string.Join(", ", cols.Select(c => $"[{c}]"));
        var paramNames = string.Join(", ", cols.Select((_, i) => $"@p{i}"));
        var insertSql = $"INSERT INTO [{request.Schema}].[{request.TableName}] ({colNames}) VALUES ({paramNames})";

        var imported = 0;
        var failed = 0;
        var errors = new List<string>();
        var lastPct = -1;

        for (int r = 0; r < dataTable.Rows.Count; r++)
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = insertSql;
                cmd.CommandTimeout = 120;
                for (int c = 0; c < cols.Count; c++)
                {
                    cmd.Parameters.Add(new SqlParameter($"@p{c}", dataTable.Rows[r][c] ?? DBNull.Value));
                }
                await cmd.ExecuteNonQueryAsync();
                imported++;
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"Row {r + 1}: {ex.Message}");
            }

            var pct = (int)((r + 1) / (double)dataTable.Rows.Count * 100);
            if (pct != lastPct)
            {
                lastPct = pct;
                progressCallback(pct);
            }
        }

        result.ImportedRows = imported;
        result.FailedRows = failed;
        result.TotalRows = imported + failed;
        result.Errors = errors.Take(100).ToList();
        result.Success = failed == 0;
        result.Message = failed == 0
            ? $"Imported {imported:N0} rows"
            : $"Partially imported: {imported:N0} succeeded, {failed:N0} failed";
    }

    // ============================================================
    // Access check
    // ============================================================
    private static async Task ValidateAccess(AppDbContext context, IDatabaseAccessService dbAccessService, int userId, string database, string schema, string tableName, int? serverId = null)
    {
        var userRoles = await context.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.Role.Name)
            .ToListAsync();

        if (!userRoles.Contains("Admin"))
        {
            var hasAccess = await dbAccessService.HasAccessAsync(userId, database, schema, tableName, serverId: serverId);
            if (!hasAccess)
                throw new UnauthorizedAccessException($"Access denied to table '{database}.{schema}.{tableName}'.");
        }
    }
}
