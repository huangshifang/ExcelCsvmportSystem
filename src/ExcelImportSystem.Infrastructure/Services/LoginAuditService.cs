using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ExcelImportSystem.Core.DTOs;
using ExcelImportSystem.Core.Entities;
using ExcelImportSystem.Core.Interfaces;
using ExcelImportSystem.Infrastructure.Data;

namespace ExcelImportSystem.Infrastructure.Services;

public class LoginAuditService : ILoginAuditService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<LoginAuditService> _logger;

    public LoginAuditService(
        IServiceScopeFactory scopeFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<LoginAuditService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task LogAsync(string username, bool success, string? failureReason)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            var ip = httpContext?.Connection.RemoteIpAddress?.ToString();
            var userAgent = httpContext?.Request.Headers.UserAgent.ToString();

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.LoginAuditLogs.Add(new LoginAuditLog
            {
                Username = username,
                IpAddress = ip,
                UserAgent = userAgent,
                Success = success,
                FailureReason = failureReason,
                Timestamp = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write login audit log for {Username}", username);
        }
    }

    public async Task<PagedResult<LoginAuditLogDto>> GetLogsAsync(
        int page, int pageSize, string? username = null, bool? success = null,
        DateTime? from = null, DateTime? to = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var query = db.LoginAuditLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(username))
            query = query.Where(l => l.Username.Contains(username));

        if (success.HasValue)
            query = query.Where(l => l.Success == success.Value);

        if (from.HasValue)
            query = query.Where(l => l.Timestamp >= from.Value);

        if (to.HasValue)
            query = query.Where(l => l.Timestamp <= to.Value);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(l => l.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new LoginAuditLogDto
            {
                Id = l.Id,
                Username = l.Username,
                IpAddress = l.IpAddress,
                UserAgent = l.UserAgent,
                Success = l.Success,
                FailureReason = l.FailureReason,
                Timestamp = l.Timestamp
            })
            .ToListAsync();

        return new PagedResult<LoginAuditLogDto> { Total = total, Page = page, PageSize = pageSize, Items = items };
    }
}
