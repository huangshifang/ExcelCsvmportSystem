using Microsoft.EntityFrameworkCore;
using ExcelImportSystem.Core.DTOs;
using ExcelImportSystem.Core.Interfaces;
using ExcelImportSystem.Infrastructure.Data;

namespace ExcelImportSystem.Infrastructure.Services;

public class ImportLogService : IImportLogService
{
    private readonly AppDbContext _context;

    public ImportLogService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<ImportLogDto>> GetLogsAsync(
        int page, int pageSize, string? tableName = null,
        string? status = null, DateTime? from = null, DateTime? to = null)
    {
        var query = _context.ImportLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(tableName))
            query = query.Where(l => l.TargetTable.Contains(tableName));
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(l => l.Status == status);
        if (from.HasValue)
            query = query.Where(l => l.ImportedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(l => l.ImportedAt <= to.Value);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(l => l.ImportedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new ImportLogDto
            {
                Id = l.Id,
                UserId = l.UserId,
                UserName = l.UserName,
                TargetTable = l.TargetTable,
                FileName = l.FileName,
                TotalRows = l.TotalRows,
                SuccessRows = l.SuccessRows,
                FailedRows = l.FailedRows,
                Status = l.Status,
                ErrorMessage = l.ErrorMessage,
                ServerId = l.ServerId,
                ServerName = l.ServerName,
                ImportedAt = l.ImportedAt
            })
            .ToListAsync();

        return new PagedResult<ImportLogDto>
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = items
        };
    }

    public async Task<DashboardStatsDto> GetDashboardStatsAsync(int? userId)
    {
        var query = _context.ImportLogs.AsQueryable();

        if (userId.HasValue)
            query = query.Where(l => l.UserId == userId.Value);

        return new DashboardStatsDto
        {
            TotalImports = await query.CountAsync(),
            TotalRows = await query.SumAsync(l => (int?)l.TotalRows) ?? 0,
            SuccessRows = await query.SumAsync(l => (int?)l.SuccessRows) ?? 0,
            FailedRows = await query.SumAsync(l => (int?)l.FailedRows) ?? 0
        };
    }

    public async Task<ImportLogDto?> GetLogByIdAsync(int id)
    {
        return await _context.ImportLogs
            .Where(l => l.Id == id)
            .Select(l => new ImportLogDto
            {
                Id = l.Id,
                UserId = l.UserId,
                UserName = l.UserName,
                TargetTable = l.TargetTable,
                FileName = l.FileName,
                TotalRows = l.TotalRows,
                SuccessRows = l.SuccessRows,
                FailedRows = l.FailedRows,
                Status = l.Status,
                ErrorMessage = l.ErrorMessage,
                ServerId = l.ServerId,
                ServerName = l.ServerName,
                ImportedAt = l.ImportedAt
            })
            .FirstOrDefaultAsync();
    }
}
