using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ExcelImportSystem.Core.DTOs;
using ExcelImportSystem.Core.Interfaces;
using ExcelImportSystem.Infrastructure.Data;

namespace ExcelImportSystem.Infrastructure.Services;

public class ConnectionFactory : IConnectionFactory, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConnectionFactory> _logger;
    private readonly ConcurrentDictionary<int, string> _connectionStringCache = new();
    private readonly ConcurrentDictionary<string, int?> _dbToServerCache = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _cacheBuilt;

    public ConnectionFactory(IServiceScopeFactory scopeFactory, ILogger<ConnectionFactory> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<SqlConnection> CreateConnectionAsync(int? serverId)
    {
        string connStr;
        if (serverId == null)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            connStr = context.Database.GetConnectionString()!;
        }
        else
        {
            if (!_connectionStringCache.TryGetValue(serverId.Value, out connStr!))
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var server = await context.SqlServerInstances
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == serverId.Value && s.IsActive);
                if (server == null)
                    throw new KeyNotFoundException($"Server #{serverId} not found or inactive");
                connStr = server.ConnectionString;
                _connectionStringCache[serverId.Value] = connStr;
            }
        }

        return new SqlConnection(connStr);
    }

    public async Task<List<SqlServerInstanceDto>> GetServersAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.SqlServerInstances
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new SqlServerInstanceDto
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description,
                IsActive = s.IsActive,
                CreatedAt = s.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<SqlServerInstanceDto?> GetServerAsync(int id)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var s = await context.SqlServerInstances.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return null;
        return new SqlServerInstanceDto
        {
            Id = s.Id,
            Name = s.Name,
            Description = s.Description,
            IsActive = s.IsActive,
            CreatedAt = s.CreatedAt
        };
    }

    public async Task<int?> ResolveServerIdAsync(string databaseName)
    {
        await EnsureCacheAsync();
        return _dbToServerCache.TryGetValue(databaseName, out var id) ? id : null;
    }

    private async Task EnsureCacheAsync()
    {
        if (_cacheBuilt) return;
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var servers = await context.SqlServerInstances
            .AsNoTracking()
            .Where(s => s.IsActive)
            .ToListAsync();

        foreach (var s in servers)
            _connectionStringCache[s.Id] = s.ConnectionString;

        // Local server
        var localConnStr = context.Database.GetConnectionString()!;
        await DiscoverDatabasesAsync(null, localConnStr);

        // Remote servers
        foreach (var s in servers)
        {
            try { await DiscoverDatabasesAsync(s.Id, s.ConnectionString); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to discover databases on server '{Server}'", s.Name); }
        }

        _cacheBuilt = true;
    }

    private async Task DiscoverDatabasesAsync(int? serverId, string connectionString)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sys.databases WHERE state = 0 AND HAS_DBACCESS(name) = 1 ORDER BY name";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            _dbToServerCache[reader.GetString(0)] = serverId;
    }

    public void InvalidateCache()
    {
        _connectionStringCache.Clear();
        _dbToServerCache.Clear();
        _cacheBuilt = false;
    }

    public void Dispose()
    {
        _connectionStringCache.Clear();
        _dbToServerCache.Clear();
    }
}
