using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using ExcelImportSystem.Core.DTOs;
using ExcelImportSystem.Core.Entities;
using ExcelImportSystem.Core.Interfaces;
using ExcelImportSystem.Infrastructure.Data;

namespace ExcelImportSystem.Infrastructure.Services;

public class ImportService : IImportService
{
    private readonly AppDbContext _context;
    private readonly ITableService _tableService;
    private readonly ILogger<ImportService> _logger;

    public ImportService(AppDbContext context, ITableService tableService, ILogger<ImportService> logger)
    {
        _context = context;
        _tableService = tableService;
        _logger = logger;
        // EPPlus license context
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    public async Task<ImportPreviewDto> PreviewAsync(ImportRequestDto request)
    {
        var table = await _tableService.GetTableAsync(request.Database, request.TableName, request.Schema);
        if (table == null)
            throw new KeyNotFoundException($"Table '{request.Database}.{request.Schema}.{request.TableName}' not found");

        using var package = new ExcelPackage(request.File.OpenReadStream());
        var worksheet = package.Workbook.Worksheets[0];
        if (worksheet == null || worksheet.Dimension == null)
            throw new InvalidDataException("Excel file is empty or invalid");

        var totalRows = worksheet.Dimension.Rows;
        var startRow = request.HasHeaderRow ? 2 : 1;
        var colCount = worksheet.Dimension.Columns;

        // Read header / excel columns
        var excelColumns = new List<string>();
        for (int col = 1; col <= colCount; col++)
        {
            var header = request.HasHeaderRow
                ? worksheet.Cells[1, col].Text?.Trim() ?? $"Column{col}"
                : $"Column{col}";
            excelColumns.Add(header);
        }

        // Read sample data (first 5 rows)
        var sampleData = new List<Dictionary<string, string>>();
        var maxSample = Math.Min(startRow + 4, totalRows);
        for (int row = startRow; row <= maxSample; row++)
        {
            var rowData = new Dictionary<string, string>();
            for (int col = 1; col <= colCount; col++)
            {
                rowData[excelColumns[col - 1]] = worksheet.Cells[row, col].Text?.Trim() ?? "";
            }
            sampleData.Add(rowData);
        }

        // Auto-map columns by name matching (case-insensitive)
        var autoMappings = new List<ColumnMappingDto>();
        foreach (var excelCol in excelColumns)
        {
            var match = table.Columns.Find(c =>
                c.ColumnName.Equals(excelCol, StringComparison.OrdinalIgnoreCase) &&
                !c.IsIdentity);
            if (match != null)
            {
                autoMappings.Add(new ColumnMappingDto
                {
                    ExcelColumn = excelCol,
                    TableColumn = match.ColumnName
                });
            }
        }

        return new ImportPreviewDto
        {
            ExcelColumns = excelColumns,
            SampleData = sampleData,
            TableColumns = table.Columns,
            AutoMappings = autoMappings,
            TotalRows = request.HasHeaderRow ? totalRows - 1 : totalRows
        };
    }

    public async Task<ImportResultDto> ExecuteAsync(int userId, ImportRequestDto request, List<ColumnMappingDto> mappings)
    {
        var user = await _context.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        var table = await _tableService.GetTableAsync(request.Database, request.TableName, request.Schema);
        if (table == null)
            throw new KeyNotFoundException($"Table '{request.Database}.{request.Schema}.{request.TableName}' not found");

        var result = new ImportResultDto { Success = true };

        using var package = new ExcelPackage(request.File.OpenReadStream());
        var worksheet = package.Workbook.Worksheets[0];
        if (worksheet == null || worksheet.Dimension == null)
            throw new InvalidDataException("Excel file is empty or invalid");

        var totalRows = worksheet.Dimension.Rows;
        var startRow = request.HasHeaderRow ? 2 : 1;
        var colCount = worksheet.Dimension.Columns;

        // Read headers
        var excelColumns = new List<string>();
        for (int col = 1; col <= colCount; col++)
        {
            var header = request.HasHeaderRow
                ? worksheet.Cells[1, col].Text?.Trim() ?? $"Column{col}"
                : $"Column{col}";
            excelColumns.Add(header);
        }

        // Build column index mapping from Excel -> DB
        var columnMap = new List<(int ExcelColIndex, ColumnInfoDto DbColumn)>();
        var identityColumns = table.Columns.Where(c => c.IsIdentity).Select(c => c.ColumnName).ToHashSet();
        var allTableCols = table.Columns.Select(c => c.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var map in mappings)
        {
            var excelIdx = excelColumns.FindIndex(c =>
                c.Equals(map.ExcelColumn, StringComparison.OrdinalIgnoreCase));
            var dbCol = table.Columns.Find(c =>
                c.ColumnName.Equals(map.TableColumn, StringComparison.OrdinalIgnoreCase));

            if (excelIdx >= 0 && dbCol != null && !dbCol.IsIdentity)
            {
                columnMap.Add((excelIdx + 1, dbCol));
            }
        }

        if (columnMap.Count == 0)
            throw new InvalidOperationException("No valid column mappings found");

        // Build table column list (exclude identity columns)
        var insertColumns = columnMap.Select(m => m.DbColumn.ColumnName).ToList();
        var insertParams = columnMap.Select((m, i) => $"@p{i}").ToList();

        var insertSql = $"INSERT INTO [{request.Database}].[{request.Schema}].[{request.TableName}] ({string.Join(", ", insertColumns.Select(c => $"[{c}]"))}) VALUES ({string.Join(", ", insertParams)})";

        // Get connection
        var connection = _context.Database.GetDbConnection();
        await connection.OpenAsync();

        DbTransaction? transaction = null;
        try
        {
            if (request.UseTransaction)
                transaction = await connection.BeginTransactionAsync();

            var importedRows = 0;
            var failedRows = 0;
            var errors = new List<string>();

            // Process in batches
            var dataRows = new List<object[]>();
            for (int row = startRow; row <= totalRows; row++)
            {
                var rowValues = new object[columnMap.Count];
                bool hasData = false;

                for (int c = 0; c < columnMap.Count; c++)
                {
                    var (excelColIdx, dbCol) = columnMap[c];
                    var rawValue = worksheet.Cells[row, excelColIdx].Value;
                    rowValues[c] = rawValue ?? DBNull.Value;
                    if (rawValue != null && rawValue != DBNull.Value && rawValue.ToString() != "")
                        hasData = true;
                }

                if (!hasData) continue; // skip empty rows
                dataRows.Add(rowValues);

                if (dataRows.Count >= request.BatchSize || row == totalRows)
                {
                    // Process batch
                    foreach (var values in dataRows)
                    {
                        try
                        {
                            using var cmd = connection.CreateCommand();
                            cmd.CommandText = insertSql;
                            cmd.CommandTimeout = 120;
                            if (transaction is not null)
                                cmd.Transaction = transaction;

                            for (int i = 0; i < values.Length; i++)
                            {
                                var param = new SqlParameter($"@p{i}", values[i] ?? DBNull.Value);
                                cmd.Parameters.Add(param);
                            }

                            await cmd.ExecuteNonQueryAsync();
                            importedRows++;
                        }
                        catch (Exception ex)
                        {
                            failedRows++;
                            errors.Add($"Row {row - dataRows.Count + dataRows.IndexOf(values) + startRow}: {ex.Message}");
                        }
                    }
                    dataRows.Clear();
                }
            }

            if (failedRows > 0 && importedRows == 0)
            {
                if (transaction is not null) await transaction.RollbackAsync();
                result.Success = false;
                result.Message = "All rows failed to import";
            }
            else if (failedRows > 0)
            {
                if (request.UseTransaction)
                {
                    // Rollback entire transaction if any rows failed (strict mode)
                    if (transaction is not null) await transaction.RollbackAsync();
                    result.Success = false;
                    result.Message = $"Transaction rolled back: {failedRows} row(s) failed";
                    importedRows = 0;
                }
                else
                {
                    if (transaction is not null) await transaction.CommitAsync();
                    result.Message = $"Partially imported: {importedRows} rows succeeded, {failedRows} rows failed";
                }
            }
            else
            {
                if (transaction is not null) await transaction.CommitAsync();
                result.Message = $"Successfully imported {importedRows} rows";
            }

            result.TotalRows = importedRows + failedRows;
            result.ImportedRows = importedRows;
            result.FailedRows = failedRows;
            result.Errors = errors.Take(100).ToList(); // limit error count
            result.Success = failedRows == 0;

            // Log the import
            var log = new ImportLog
            {
                UserId = userId,
                UserName = user.DisplayName,
                TargetTable = $"{request.Database}.{request.Schema}.{request.TableName}",
                FileName = request.File.FileName,
                TotalRows = result.TotalRows,
                SuccessRows = importedRows,
                FailedRows = failedRows,
                Status = failedRows == 0 ? "Success" : (importedRows > 0 ? "Partial" : "Failed"),
                ErrorMessage = failedRows > 0 ? string.Join("; ", errors.Take(5)) : null,
                ImportedAt = DateTime.UtcNow
            };

            _context.ImportLogs.Add(log);
            await _context.SaveChangesAsync();
            result.ImportLogId = log.Id;

            _logger.LogInformation(
                "Import completed: User={User}, Table={Table}, File={File}, Total={Total}, Success={Success}, Failed={Failed}",
                user.Username, log.TargetTable, request.File.FileName, result.TotalRows, importedRows, failedRows);
        }
        catch (Exception ex)
        {
            if (transaction is not null) await transaction.RollbackAsync();
            _logger.LogError(ex, "Import failed: User={User}, Table={Table}, File={File}",
                user.Username, $"{request.Database}.{request.Schema}.{request.TableName}", request.File.FileName);

            result.Success = false;
            result.Message = $"Import failed: {ex.Message}";
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
            if (connection.State == ConnectionState.Open)
                connection.Close();
        }

        return result;
    }
}
