# Multi-SQL Server Remote Database Support - Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the system to support importing into databases on multiple remote SQL Server instances, while keeping all existing functionality intact.

**Architecture:** New `SqlServerInstance` entity stores per-server connection strings. `IConnectionFactory` creates connections to the correct server. `TableService` discovers databases across all servers. `ImportService` uses `IConnectionFactory` instead of `AppDbContext` connection for target operations. `UserDatabaseAccess` gains optional `ServerId` FK for per-server access control.

**Tech Stack:** .NET 10, EF Core, Microsoft.Data.SqlClient, React 19 + TypeScript + Ant Design

---

## File Map

### Create:
- `src/ExcelImportSystem.Core/Entities/SqlServerInstance.cs`
- `src/ExcelImportSystem.Core/Interfaces/IConnectionFactory.cs`
- `src/ExcelImportSystem.Core/DTOs/ServerDtos.cs`
- `src/ExcelImportSystem.Infrastructure/Services/ConnectionFactory.cs`
- `src/ExcelImportSystem.Infrastructure/Data/Configurations/SqlServerInstanceConfiguration.cs`
- `src/ExcelImportSystem.API/Controllers/ServerController.cs`
- `frontend/src/api/servers.ts`
- `frontend/src/pages/Servers/ServersPage.tsx`

### Modify:
- `src/ExcelImportSystem.Core/Entities/UserDatabaseAccess.cs:6-14` — add ServerId + nav prop
- `src/ExcelImportSystem.Core/DTOs/TableDtos.cs:3-6` — add ServerId/ServerName to DatabaseInfoDto
- `src/ExcelImportSystem.Core/DTOs/ImportDtos.cs:5-14` — add ServerId to ImportRequestDto
- `src/ExcelImportSystem.Core/DTOs/CommonDtos.cs:64-69` — add ServerId to UserTableAccessDto
- `src/ExcelImportSystem.Infrastructure/Data/AppDbContext.cs:10-17` — add SqlServerInstances DbSet
- `src/ExcelImportSystem.Infrastructure/Data/Configurations/UserConfiguration.cs:59-72` — add Server relationship
- `src/ExcelImportSystem.Infrastructure/Services/TableService.cs:56-98` — multi-server discover
- `src/ExcelImportSystem.Infrastructure/Services/ImportService.cs:147-213` — use ConnectionFactory
- `src/ExcelImportSystem.Infrastructure/Services/DatabaseAccessService.cs` — ServerId support
- `src/ExcelImportSystem.Infrastructure/Extensions/ServiceCollectionExtensions.cs:15-33` — register new services
- `src/ExcelImportSystem.API/Program.cs:54-72,160-218` — add Server policies + migration SQL
- `frontend/src/types/index.ts` — add server types, update existing
- `frontend/src/api/tables.ts` — add serverId param
- `frontend/src/api/databaseAccess.ts` — add serverId param
- `frontend/src/App.tsx` — add /servers route
- `frontend/src/components/AppLayout.tsx` — add Servers menu item
- `frontend/src/pages/Import/ImportPage.tsx` — add server selector
- `frontend/src/pages/Users/UsersPage.tsx` — server context in DB access modal
- `frontend/src/i18n/locales/en.json` — new i18n keys
- `frontend/src/i18n/locales/zh.json` — new i18n keys

---

## Phase 1: Core Layer

### Task 1: Create SqlServerInstance entity

**Files:**
- Create: `src/ExcelImportSystem.Core/Entities/SqlServerInstance.cs`

- [ ] **Step 1: Write the entity**

```csharp
namespace ExcelImportSystem.Core.Entities;

public class SqlServerInstance
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 2: Build to verify compilation**

```bash
cd src && dotnet build
```

- [ ] **Step 3: Commit**

```bash
git add src/ExcelImportSystem.Core/Entities/SqlServerInstance.cs
git commit -m "feat: add SqlServerInstance entity for multi-server support"
```

---

### Task 2: Update UserDatabaseAccess entity to support ServerId

**Files:**
- Modify: `src/ExcelImportSystem.Core/Entities/UserDatabaseAccess.cs`

- [ ] **Step 1: Add ServerId and navigation property**

```csharp
namespace ExcelImportSystem.Core.Entities;

public class UserDatabaseAccess
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? ServerId { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string? SchemaName { get; set; }
    public string? TableName { get; set; }
    public string? GrantedBy { get; set; }
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public SqlServerInstance? Server { get; set; }
}
```

- [ ] **Step 2: Build to verify**

```bash
cd src && dotnet build
```

- [ ] **Step 3: Commit**

```bash
git add src/ExcelImportSystem.Core/Entities/UserDatabaseAccess.cs
git commit -m "feat: add ServerId FK to UserDatabaseAccess for multi-server"
```

---

### Task 3: Create ServerDtos

**Files:**
- Create: `src/ExcelImportSystem.Core/DTOs/ServerDtos.cs`

- [ ] **Step 1: Write DTOs**

```csharp
using System.ComponentModel.DataAnnotations;

namespace ExcelImportSystem.Core.DTOs;

public class SqlServerInstanceDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateServerDto
{
    [Required]
    public string Name { get; set; } = string.Empty;
    [Required]
    public string ConnectionString { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdateServerDto
{
    public string? Name { get; set; }
    public string? ConnectionString { get; set; }
    public string? Description { get; set; }
    public bool? IsActive { get; set; }
}

public class TestServerConnectionDto
{
    [Required]
    public string ConnectionString { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Build to verify**

```bash
cd src && dotnet build
```

- [ ] **Step 3: Commit**

```bash
git add src/ExcelImportSystem.Core/DTOs/ServerDtos.cs
git commit -m "feat: add server DTOs for CRUD operations"
```

---

### Task 4: Update existing DTOs with ServerId

**Files:**
- Modify: `src/ExcelImportSystem.Core/DTOs/TableDtos.cs`
- Modify: `src/ExcelImportSystem.Core/DTOs/ImportDtos.cs`
- Modify: `src/ExcelImportSystem.Core/DTOs/CommonDtos.cs`

- [ ] **Step 1: Update DatabaseInfoDto**

In `TableDtos.cs`, replace:
```csharp
public class DatabaseInfoDto
{
    public string Name { get; set; } = string.Empty;
}
```
With:
```csharp
public class DatabaseInfoDto
{
    public string Name { get; set; } = string.Empty;
    public int? ServerId { get; set; }
    public string? ServerName { get; set; }
}
```

- [ ] **Step 2: Update ImportRequestDto**

In `ImportDtos.cs`, replace:
```csharp
public class ImportRequestDto
{
    public string Database { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string Schema { get; set; } = "dbo";
    public IFormFile File { get; set; } = null!;
    public bool UseTransaction { get; set; } = true;
    public bool HasHeaderRow { get; set; } = true;
    public int BatchSize { get; set; } = 1000;
}
```
With:
```csharp
public class ImportRequestDto
{
    public int? ServerId { get; set; }
    public string Database { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string Schema { get; set; } = "dbo";
    public IFormFile File { get; set; } = null!;
    public bool UseTransaction { get; set; } = true;
    public bool HasHeaderRow { get; set; } = true;
    public int BatchSize { get; set; } = 1000;
}
```

- [ ] **Step 3: Update UserTableAccessDto**

In `CommonDtos.cs`, replace:
```csharp
public class UserTableAccessDto
{
    public string DatabaseName { get; set; } = string.Empty;
    public string? SchemaName { get; set; }
    public string? TableName { get; set; }
}
```
With:
```csharp
public class UserTableAccessDto
{
    public int? ServerId { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string? SchemaName { get; set; }
    public string? TableName { get; set; }
}
```

- [ ] **Step 4: Build to verify**

```bash
cd src && dotnet build
```

- [ ] **Step 5: Commit**

```bash
git add src/ExcelImportSystem.Core/DTOs/TableDtos.cs src/ExcelImportSystem.Core/DTOs/ImportDtos.cs src/ExcelImportSystem.Core/DTOs/CommonDtos.cs
git commit -m "feat: add ServerId to DatabaseInfoDto, ImportRequestDto, UserTableAccessDto"
```

---

### Task 5: Create IConnectionFactory interface

**Files:**
- Create: `src/ExcelImportSystem.Core/Interfaces/IConnectionFactory.cs`

- [ ] **Step 1: Write the interface**

```csharp
using Microsoft.Data.SqlClient;
using ExcelImportSystem.Core.DTOs;

namespace ExcelImportSystem.Core.Interfaces;

public interface IConnectionFactory
{
    Task<SqlConnection> CreateConnectionAsync(int? serverId);
    Task<List<SqlServerInstanceDto>> GetServersAsync();
    Task<SqlServerInstanceDto?> GetServerAsync(int id);
    Task<int?> ResolveServerIdAsync(string databaseName);
}
```

- [ ] **Step 2: Build to verify**

```bash
cd src && dotnet build
```

- [ ] **Step 3: Commit**

```bash
git add src/ExcelImportSystem.Core/Interfaces/IConnectionFactory.cs
git commit -m "feat: add IConnectionFactory interface for multi-server connections"
```

---

## Phase 2: Infrastructure Layer

### Task 6: Create SqlServerInstance EF configuration

**Files:**
- Create: `src/ExcelImportSystem.Infrastructure/Data/Configurations/SqlServerInstanceConfiguration.cs`
- Modify: `src/ExcelImportSystem.Infrastructure/Data/Configurations/UserConfiguration.cs`

- [ ] **Step 1: Write SqlServerInstanceConfiguration**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ExcelImportSystem.Core.Entities;

namespace ExcelImportSystem.Infrastructure.Data.Configurations;

public class SqlServerInstanceConfiguration : IEntityTypeConfiguration<SqlServerInstance>
{
    public void Configure(EntityTypeBuilder<SqlServerInstance> builder)
    {
        builder.ToTable("SqlServerInstances");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.Name).IsUnique();
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.ConnectionString).HasMaxLength(1000).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(500);
    }
}
```

- [ ] **Step 2: Update UserDatabaseAccessConfiguration**

In `UserConfiguration.cs`, in the `UserDatabaseAccessConfiguration` class, after the `HasOne(uda => uda.User)` line, add:
```csharp
        builder.HasOne(uda => uda.Server)
            .WithMany()
            .HasForeignKey(uda => uda.ServerId)
            .OnDelete(DeleteBehavior.SetNull);
```

The full Configure method becomes:
```csharp
public void Configure(EntityTypeBuilder<UserDatabaseAccess> builder)
{
    builder.ToTable("UserDatabaseAccesses");
    builder.HasKey(uda => uda.Id);
    builder.Property(uda => uda.DatabaseName).HasMaxLength(200).IsRequired();
    builder.Property(uda => uda.SchemaName).HasMaxLength(200);
    builder.Property(uda => uda.TableName).HasMaxLength(200);
    builder.Property(uda => uda.GrantedBy).HasMaxLength(200);
    builder.HasOne(uda => uda.User).WithMany(u => u.DatabaseAccesses).HasForeignKey(uda => uda.UserId).OnDelete(DeleteBehavior.Cascade);
    builder.HasOne(uda => uda.Server).WithMany().HasForeignKey(uda => uda.ServerId).OnDelete(DeleteBehavior.SetNull);
    builder.HasIndex(uda => new { uda.UserId, uda.ServerId, uda.DatabaseName, uda.SchemaName, uda.TableName });
}
```

Also update the index from `{ UserId, DatabaseName, SchemaName, TableName }` to `{ UserId, ServerId, DatabaseName, SchemaName, TableName }` because the uniqueness key changed.

- [ ] **Step 3: Build to verify**

```bash
cd src && dotnet build
```

- [ ] **Step 4: Commit**

```bash
git add src/ExcelImportSystem.Infrastructure/Data/Configurations/SqlServerInstanceConfiguration.cs src/ExcelImportSystem.Infrastructure/Data/Configurations/UserConfiguration.cs
git commit -m "feat: add SqlServerInstance EF config, update UserDatabaseAccess FK"
```

---

### Task 7: Update AppDbContext

**Files:**
- Modify: `src/ExcelImportSystem.Infrastructure/Data/AppDbContext.cs`

- [ ] **Step 1: Add SqlServerInstances DbSet**

Add after `public DbSet<LoginAuditLog> LoginAuditLogs => Set<LoginAuditLog>();`:
```csharp
    public DbSet<SqlServerInstance> SqlServerInstances => Set<SqlServerInstance>();
```

- [ ] **Step 2: Build to verify**

```bash
cd src && dotnet build
```

- [ ] **Step 3: Commit**

```bash
git add src/ExcelImportSystem.Infrastructure/Data/AppDbContext.cs
git commit -m "feat: add SqlServerInstances DbSet to AppDbContext"
```

---

### Task 8: Create ConnectionFactory service

**Files:**
- Create: `src/ExcelImportSystem.Infrastructure/Services/ConnectionFactory.cs`

- [ ] **Step 1: Write ConnectionFactory**

```csharp
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
            // Local/default server — use AppDbContext connection string
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
                    throw new KeyNotFoundException($"Server instance #{serverId} not found or inactive");
                connStr = server.ConnectionString;
                _connectionStringCache[serverId.Value] = connStr;
            }
        }

        var connection = new SqlConnection(connStr);
        return connection;
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
        {
            _connectionStringCache[s.Id] = s.ConnectionString;
        }

        // Discover databases on each server
        var localConnStr = context.Database.GetConnectionString()!;
        // Local server (null serverId)
        await DiscoverDatabasesAsync(null, localConnStr);
        foreach (var s in servers)
        {
            try
            {
                await DiscoverDatabasesAsync(s.Id, s.ConnectionString);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to discover databases on server '{Server}'", s.Name);
            }
        }

        _cacheBuilt = true;
    }

    private async Task DiscoverDatabasesAsync(int? serverId, string connectionString)
    {
        try
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sys.databases WHERE state = 0 AND HAS_DBACCESS(name) = 1 ORDER BY name";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var dbName = reader.GetString(0);
                _dbToServerCache[dbName] = serverId;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to discover databases for serverId={ServerId}", serverId);
        }
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
```

- [ ] **Step 2: Build to verify**

```bash
cd src && dotnet build
```

- [ ] **Step 3: Commit**

```bash
git add src/ExcelImportSystem.Infrastructure/Services/ConnectionFactory.cs
git commit -m "feat: add ConnectionFactory for multi-server connection management"
```

---

### Task 9: Inject IConnectionFactory into TableService, update discovery

**Files:**
- Modify: `src/ExcelImportSystem.Infrastructure/Services/TableService.cs`

- [ ] **Step 1: Add IConnectionFactory dependency**

Replace the constructor:
```csharp
public TableService(AppDbContext context, IDatabaseAccessService dbAccessService, ILogger<TableService> logger)
{
    _context = context;
    _dbAccessService = dbAccessService;
    _logger = logger;
}
```
With:
```csharp
private readonly AppDbContext _context;
private readonly IDatabaseAccessService _dbAccessService;
private readonly IConnectionFactory _connectionFactory;
private readonly ILogger<TableService> _logger;

public TableService(AppDbContext context, IDatabaseAccessService dbAccessService, IConnectionFactory connectionFactory, ILogger<TableService> logger)
{
    _context = context;
    _dbAccessService = dbAccessService;
    _connectionFactory = connectionFactory;
    _logger = logger;
}
```

NOTE: Remove the existing field declarations from lines 13-16 since we're declaring them inline above.

Actually, to minimize diff, just add `_connectionFactory` to the existing pattern. Remove the old field declarations (lines 13-16) and the old constructor (lines 17-22), replace with:

```csharp
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
```

- [ ] **Step 2: Rewrite GetDatabasesAsync for multi-server**

Replace the entire `GetDatabasesAsync` method (lines 56-98) with:

```csharp
    public async Task<List<DatabaseInfoDto>> GetDatabasesAsync(int? userId = null)
    {
        var allDbs = new List<DatabaseInfoDto>();

        // 1. Discover databases on local/default server (ServerId = null)
        var localConn = _context.Database.GetDbConnection();
        await localConn.OpenAsync();
        try
        {
            using var cmd = localConn.CreateCommand();
            cmd.CommandText = @"
                SELECT d.name
                FROM sys.databases d
                WHERE d.state = 0 AND HAS_DBACCESS(d.name) = 1
                ORDER BY d.name";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                allDbs.Add(new DatabaseInfoDto { Name = reader.GetString(0), ServerId = null, ServerName = null });
        }
        finally { localConn.Close(); }

        // 2. Discover databases on each remote server
        var servers = await _connectionFactory.GetServersAsync();
        foreach (var server in servers.Where(s => s.IsActive))
        {
            try
            {
                using var conn = await _connectionFactory.CreateConnectionAsync(server.Id);
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT name FROM sys.databases WHERE state = 0 AND HAS_DBACCESS(name) = 1 ORDER BY name";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    allDbs.Add(new DatabaseInfoDto { Name = reader.GetString(0), ServerId = server.Id, ServerName = server.Name });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to list databases on server '{Server}'", server.Name);
            }
        }

        // 3. Non-admin: filter to UserDatabaseAccess
        if (userId.HasValue)
        {
            var userRoles = await _context.UserRoles
                .Where(ur => ur.UserId == userId.Value)
                .Select(ur => ur.Role.Name)
                .ToListAsync();
            if (!userRoles.Contains("Admin"))
            {
                var accesses = await _dbAccessService.GetUserTableAccessesAsync(userId.Value);
                var grantedSet = new HashSet<(int? ServerId, string Db)>(
                    accesses.Select(a => ((int?)a.ServerId, a.DatabaseName)),
                    EqualityComparer<(int? ServerId, string Db)>.Create(
                        (a, b) => StringComparer.OrdinalIgnoreCase.Equals(a.Db, b.Db) && a.ServerId == b.ServerId,
                        o => (o.ServerId ?? 0) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(o.Db)));
                allDbs = allDbs.Where(d => grantedSet.Contains((d.ServerId, d.Name))).ToList();
            }
        }

        return allDbs.OrderBy(d => d.ServerName ?? "").ThenBy(d => d.Name).ToList();
    }
```

- [ ] **Step 3: Update GetTablesAsync for multi-server**

The `GetTablesAsync` method needs to accept an optional `serverId` parameter. Update the interface first:

In `src/ExcelImportSystem.Core/Interfaces/ITableService.cs`, update:
```csharp
Task<List<TableInfoDto>> GetTablesAsync(string database, string? schema = null, int? userId = null, int? serverId = null);
```

Then in `TableService.cs`, update the `GetTablesAsync` method to use `IConnectionFactory`:

Replace the connection opening in GetTablesAsync (lines 102-103):
```csharp
var connection = _context.Database.GetDbConnection();
await connection.OpenAsync();
```
With:
```csharp
var connection = await _connectionFactory.CreateConnectionAsync(serverId);
await connection.OpenAsync();
```

Also add `int? serverId = null` to the method signature.

NOTE: The 3-part naming `[Database].[Schema].[Table]` still works because we're connected directly to the correct SQL Server instance.

- [ ] **Step 4: Update GetTableAsync similarly**

Add `int? serverId = null` parameter. Update interface:
```csharp
Task<TableInfoDto?> GetTableAsync(string database, string tableName, string schema = "dbo", int? userId = null, int? serverId = null);
```

In the method, replace:
```csharp
var connection = _context.Database.GetDbConnection();
await connection.OpenAsync();
```
With:
```csharp
var connection = await _connectionFactory.CreateConnectionAsync(serverId);
await connection.OpenAsync();
```

- [ ] **Step 5: Build to verify**

```bash
cd src && dotnet build
```

Expected: may need to fix callers of these methods that need the new parameter. We'll handle that in Phase 3.

- [ ] **Step 6: Commit**

```bash
git add src/ExcelImportSystem.Infrastructure/Services/TableService.cs src/ExcelImportSystem.Core/Interfaces/ITableService.cs
git commit -m "feat: multi-server database/table discovery in TableService"
```

---

### Task 10: Update ImportService to use IConnectionFactory

**Files:**
- Modify: `src/ExcelImportSystem.Infrastructure/Services/ImportService.cs`

- [ ] **Step 1: Update ExecuteInternalAsync connection creation**

In `ExecuteInternalAsync` (around line 213), replace:
```csharp
var connection = context.Database.GetDbConnection();
await connection.OpenAsync();
```
With:
```csharp
// Resolve connection factory from a new scope (since we're in a background task)
var connectionFactory = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<IConnectionFactory>();
var connection = await connectionFactory.CreateConnectionAsync(request.ServerId);
await connection.OpenAsync();
```

Wait — we're already inside a scope. We need `IConnectionFactory` injected into the method. Actually, looking at the method signature more carefully, `context`, `tableService`, `dbAccessService` are passed in. Let's add `IConnectionFactory` to the parameters.

Update the `ExecuteInternalAsync` signature to include `IConnectionFactory connectionFactory`:
```csharp
private static async Task ExecuteInternalAsync(
    string taskId, int userId, ImportRequestDto request, List<ColumnMappingDto> mappings,
    byte[] fileBytes, string fileName,
    AppDbContext context, ITableService tableService, IDatabaseAccessService dbAccessService,
    IConnectionFactory connectionFactory,
    ILogger<ImportService> logger, ConcurrentDictionary<string, ImportProgressDto> store)
```

And in the `ExecuteAsync` method where it's called (around line 115), resolve and pass `connectionFactory`:
```csharp
var connectionFactory = scope.ServiceProvider.GetRequiredService<IConnectionFactory>();
// ...
await ExecuteInternalAsync(taskId, userId, request, mappings, fileBytes, fileName,
    context, tableService, dbAccessService, connectionFactory, logger, store);
```

Then in `ExecuteInternalAsync`, replace the connection open (line 213):
```csharp
var connection = await connectionFactory.CreateConnectionAsync(request.ServerId);
await connection.OpenAsync();
```

- [ ] **Step 2: Update PreviewAsync** — pass serverId to tableService

In `PreviewAsync`, when calling `tableService.GetTableAsync`, pass `request.ServerId`:
```csharp
var table = await tableService.GetTableAsync(request.Database, request.TableName, request.Schema, serverId: request.ServerId)
    ?? throw new KeyNotFoundException($"Table '{request.Database}.{request.Schema}.{request.TableName}' not found");
```

- [ ] **Step 3: Update access validation** — include ServerId

In `ValidateAccess`, the `HasAccessAsync` call now needs to consider ServerId. Update the `HasAccessAsync` call in both `PreviewAsync` and `ExecuteInternalAsync` to pass ServerId. We'll handle `HasAccessAsync` update in the next task.

- [ ] **Step 4: Build to verify**

```bash
cd src && dotnet build
```

- [ ] **Step 5: Commit**

```bash
git add src/ExcelImportSystem.Infrastructure/Services/ImportService.cs
git commit -m "feat: use IConnectionFactory in ImportService for multi-server import"
```

---

### Task 11: Update DatabaseAccessService for ServerId

**Files:**
- Modify: `src/ExcelImportSystem.Infrastructure/Services/DatabaseAccessService.cs`

- [ ] **Step 1: Update GetAll / HasAccess to include ServerId**

Replace the entire file with updated version that propagates ServerId through all operations:

```csharp
using Microsoft.EntityFrameworkCore;
using ExcelImportSystem.Core.DTOs;
using ExcelImportSystem.Core.Entities;
using ExcelImportSystem.Core.Interfaces;
using ExcelImportSystem.Infrastructure.Data;

namespace ExcelImportSystem.Infrastructure.Services;

public class DatabaseAccessService : IDatabaseAccessService
{
    private readonly AppDbContext _context;

    public DatabaseAccessService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<string>> GetUserDatabasesAsync(int userId)
    {
        return await _context.UserDatabaseAccesses
            .Where(a => a.UserId == userId)
            .Select(a => a.DatabaseName)
            .Distinct()
            .ToListAsync();
    }

    public async Task<List<UserTableAccessDto>> GetUserTableAccessesAsync(int userId)
    {
        return await _context.UserDatabaseAccesses
            .Where(a => a.UserId == userId)
            .Select(a => new UserTableAccessDto
            {
                ServerId = a.ServerId,
                DatabaseName = a.DatabaseName,
                SchemaName = a.SchemaName,
                TableName = a.TableName
            })
            .ToListAsync();
    }

    public async Task GrantAccessAsync(int userId, string databaseName, string? schemaName, string? tableName, string grantedBy, int? serverId = null)
    {
        var exists = await _context.UserDatabaseAccesses
            .AnyAsync(a => a.UserId == userId
                && a.ServerId == serverId
                && a.DatabaseName == databaseName
                && a.SchemaName == schemaName
                && a.TableName == tableName);

        if (!exists)
        {
            _context.UserDatabaseAccesses.Add(new UserDatabaseAccess
            {
                UserId = userId,
                ServerId = serverId,
                DatabaseName = databaseName,
                SchemaName = schemaName,
                TableName = tableName,
                GrantedBy = grantedBy,
                GrantedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }
    }

    public async Task RevokeAccessAsync(int userId, string databaseName, string? schemaName, string? tableName)
    {
        var access = await _context.UserDatabaseAccesses
            .FirstOrDefaultAsync(a => a.UserId == userId
                && a.DatabaseName == databaseName
                && a.SchemaName == schemaName
                && a.TableName == tableName);

        if (access is not null)
        {
            _context.UserDatabaseAccesses.Remove(access);
            await _context.SaveChangesAsync();
        }
    }

    public async Task SetUserTableAccessesAsync(int userId, List<UserTableAccessDto> accesses, string grantedBy)
    {
        var existing = await _context.UserDatabaseAccesses
            .Where(a => a.UserId == userId)
            .ToListAsync();

        _context.UserDatabaseAccesses.RemoveRange(existing);

        if (accesses.Count > 0)
        {
            var entities = accesses.Select(a => new UserDatabaseAccess
            {
                UserId = userId,
                ServerId = a.ServerId,
                DatabaseName = a.DatabaseName,
                SchemaName = a.SchemaName,
                TableName = a.TableName,
                GrantedBy = grantedBy,
                GrantedAt = DateTime.UtcNow
            }).ToList();

            _context.UserDatabaseAccesses.AddRange(entities);
        }

        await _context.SaveChangesAsync();
    }

    public async Task<bool> HasAccessAsync(int userId, string databaseName, string? schemaName = null, string? tableName = null, int? serverId = null)
    {
        if (string.IsNullOrEmpty(tableName))
        {
            return await _context.UserDatabaseAccesses
                .AnyAsync(a => a.UserId == userId && a.DatabaseName == databaseName && a.ServerId == serverId);
        }

        return await _context.UserDatabaseAccesses
            .AnyAsync(a => a.UserId == userId
                && a.ServerId == serverId
                && a.DatabaseName == databaseName
                && (a.TableName == null
                    || (a.SchemaName == schemaName && a.TableName == tableName)));
    }
}
```

- [ ] **Step 2: Update the interface** — `IDatabaseAccessService`

```csharp
using ExcelImportSystem.Core.DTOs;

namespace ExcelImportSystem.Core.Interfaces;

public interface IDatabaseAccessService
{
    Task<List<string>> GetUserDatabasesAsync(int userId);
    Task<List<UserTableAccessDto>> GetUserTableAccessesAsync(int userId);
    Task GrantAccessAsync(int userId, string databaseName, string? schemaName, string? tableName, string grantedBy, int? serverId = null);
    Task RevokeAccessAsync(int userId, string databaseName, string? schemaName, string? tableName);
    Task SetUserTableAccessesAsync(int userId, List<UserTableAccessDto> accesses, string grantedBy);
    Task<bool> HasAccessAsync(int userId, string databaseName, string? schemaName = null, string? tableName = null, int? serverId = null);
}
```

- [ ] **Step 3: Build to verify**

```bash
cd src && dotnet build
```

- [ ] **Step 4: Commit**

```bash
git add src/ExcelImportSystem.Infrastructure/Services/DatabaseAccessService.cs src/ExcelImportSystem.Core/Interfaces/IDatabaseAccessService.cs
git commit -m "feat: add ServerId support to DatabaseAccessService"
```

---

### Task 12: Register new services in DI

**Files:**
- Modify: `src/ExcelImportSystem.Infrastructure/Extensions/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Register IConnectionFactory**

Add after the `AddSingleton<ICaptchaService, CaptchaService>();` line:
```csharp
        services.AddSingleton<IConnectionFactory, ConnectionFactory>();
```

- [ ] **Step 2: Build to verify**

```bash
cd src && dotnet build
```

- [ ] **Step 3: Commit**

```bash
git add src/ExcelImportSystem.Infrastructure/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat: register IConnectionFactory as singleton"
```

---

## Phase 3: API Layer

### Task 13: Add Server permissions, seed, and migration SQL to Program.cs

**Files:**
- Modify: `src/ExcelImportSystem.API/Program.cs`

- [ ] **Step 1: Add Server policies in authorization**

After `options.AddPolicy("SystemManage", ...)` add:
```csharp
    options.AddPolicy("ServerView", policy =>
        policy.RequireClaim("Permission", "Server.View"));
    options.AddPolicy("ServerManage", policy =>
        policy.RequireClaim("Permission", "Server.Manage"));
```

- [ ] **Step 2: Add migration SQL for SqlServerInstances table**

After the LoginAuditLogs migration SQL block (around line 280), add:
```csharp
            // Ensure SqlServerInstances table exists
            dbContext.Database.ExecuteSqlRaw(
                @"IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE Name = 'SqlServerInstances')
                  BEGIN
                      CREATE TABLE SqlServerInstances (
                          Id INT IDENTITY(1,1) PRIMARY KEY,
                          Name NVARCHAR(200) NOT NULL,
                          ConnectionString NVARCHAR(1000) NOT NULL,
                          Description NVARCHAR(500) NULL,
                          IsActive BIT NOT NULL DEFAULT 1,
                          CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                          CONSTRAINT UQ_SqlServerInstances_Name UNIQUE (Name)
                      );
                  END");

            // Add ServerId column to UserDatabaseAccesses if not exists
            dbContext.Database.ExecuteSqlRaw(
                @"IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'ServerId' AND Object_ID = Object_ID('UserDatabaseAccesses'))
                  BEGIN
                      ALTER TABLE UserDatabaseAccesses ADD ServerId INT NULL;
                  END");

            // Rebuild indexes on UserDatabaseAccesses to include ServerId
            dbContext.Database.ExecuteSqlRaw(
                @"IF EXISTS (SELECT 1 FROM sys.indexes WHERE Name = 'UQ_UserDatabaseAccess_Wildcard' AND Object_ID = Object_ID('UserDatabaseAccesses'))
                  BEGIN
                      DROP INDEX UQ_UserDatabaseAccess_Wildcard ON UserDatabaseAccesses;
                  END;

                  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE Name = 'UQ_UserDatabaseAccess_Wildcard' AND Object_ID = Object_ID('UserDatabaseAccesses'))
                  BEGIN
                      CREATE UNIQUE NONCLUSTERED INDEX UQ_UserDatabaseAccess_Wildcard
                          ON UserDatabaseAccesses(UserId, ServerId, DatabaseName)
                          WHERE SchemaName IS NULL AND TableName IS NULL;
                  END;

                  IF EXISTS (SELECT 1 FROM sys.indexes WHERE Name = 'UQ_UserDatabaseAccess_Table' AND Object_ID = Object_ID('UserDatabaseAccesses'))
                  BEGIN
                      DROP INDEX UQ_UserDatabaseAccess_Table ON UserDatabaseAccesses;
                  END;

                  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE Name = 'UQ_UserDatabaseAccess_Table' AND Object_ID = Object_ID('UserDatabaseAccesses'))
                  BEGIN
                      CREATE UNIQUE NONCLUSTERED INDEX UQ_UserDatabaseAccess_Table
                          ON UserDatabaseAccesses(UserId, ServerId, DatabaseName, SchemaName, TableName)
                          WHERE TableName IS NOT NULL;
                  END");
```

- [ ] **Step 3: Seed Server permissions for Admin**

Add to seed permissions block (after `"System.Manage"` for admin):
```csharp
            new RolePermission { RoleId = adminRole.Id, Permission = "Server.View" },
            new RolePermission { RoleId = adminRole.Id, Permission = "Server.Manage" },
```

And add data migration SQL for existing Admin roles:
```csharp
            // Ensure Server.View/Server.Manage permissions exist for Admin
            dbContext.Database.ExecuteSqlRaw(
                @"IF EXISTS (SELECT 1 FROM Roles WHERE Name = 'Admin')
                  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp
                      JOIN Roles r ON rp.RoleId = r.Id
                      WHERE r.Name = 'Admin' AND rp.Permission = 'Server.View')
                  BEGIN
                      DECLARE @adminId INT = (SELECT Id FROM Roles WHERE Name = 'Admin');
                      INSERT INTO RolePermissions (RoleId, Permission) VALUES (@adminId, 'Server.View');
                      INSERT INTO RolePermissions (RoleId, Permission) VALUES (@adminId, 'Server.Manage');
                  END");
```

- [ ] **Step 4: Build to verify**

```bash
cd src && dotnet build
```

- [ ] **Step 5: Commit**

```bash
git add src/ExcelImportSystem.API/Program.cs
git commit -m "feat: add Server policies, migration SQL, and permission seed"
```

---

### Task 14: Create ServerController

**Files:**
- Create: `src/ExcelImportSystem.API/Controllers/ServerController.cs`

- [ ] **Step 1: Write the controller**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExcelImportSystem.Core.DTOs;
using ExcelImportSystem.Core.Entities;
using ExcelImportSystem.Core.Interfaces;
using ExcelImportSystem.Infrastructure.Data;

namespace ExcelImportSystem.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ServerController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConnectionFactory _connectionFactory;
    private readonly ILogger<ServerController> _logger;

    public ServerController(AppDbContext context, IConnectionFactory connectionFactory, ILogger<ServerController> logger)
    {
        _context = context;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    [HttpGet]
    [Authorize(Policy = "ServerView")]
    public async Task<ActionResult<ApiResponse<List<SqlServerInstanceDto>>>> GetAll()
    {
        try
        {
            var servers = await _connectionFactory.GetServersAsync();
            return Ok(ApiResponse<List<SqlServerInstanceDto>>.Ok(servers));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list servers");
            return StatusCode(500, ApiResponse<List<SqlServerInstanceDto>>.Fail(ex.Message));
        }
    }

    [HttpGet("{id}")]
    [Authorize(Policy = "ServerView")]
    public async Task<ActionResult<ApiResponse<SqlServerInstanceDto>>> GetById(int id)
    {
        try
        {
            var server = await _connectionFactory.GetServerAsync(id);
            if (server == null)
                return NotFound(ApiResponse<SqlServerInstanceDto>.Fail("Server not found"));
            return Ok(ApiResponse<SqlServerInstanceDto>.Ok(server));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<SqlServerInstanceDto>.Fail(ex.Message));
        }
    }

    [HttpPost]
    [Authorize(Policy = "ServerManage")]
    public async Task<ActionResult<ApiResponse<SqlServerInstanceDto>>> Create([FromBody] CreateServerDto dto)
    {
        try
        {
            var exists = await _context.SqlServerInstances.AnyAsync(s => s.Name == dto.Name);
            if (exists)
                return BadRequest(ApiResponse<SqlServerInstanceDto>.Fail($"Server '{dto.Name}' already exists"));

            var entity = new SqlServerInstance
            {
                Name = dto.Name,
                ConnectionString = dto.ConnectionString,
                Description = dto.Description,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.SqlServerInstances.Add(entity);
            await _context.SaveChangesAsync();

            _connectionFactory.InvalidateCache();

            var result = new SqlServerInstanceDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Description = entity.Description,
                IsActive = entity.IsActive,
                CreatedAt = entity.CreatedAt
            };
            return Ok(ApiResponse<SqlServerInstanceDto>.Ok(result, "Server created"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create server");
            return StatusCode(500, ApiResponse<SqlServerInstanceDto>.Fail(ex.Message));
        }
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "ServerManage")]
    public async Task<ActionResult<ApiResponse<SqlServerInstanceDto>>> Update(int id, [FromBody] UpdateServerDto dto)
    {
        try
        {
            var entity = await _context.SqlServerInstances.FindAsync(id);
            if (entity == null)
                return NotFound(ApiResponse<SqlServerInstanceDto>.Fail("Server not found"));

            if (dto.Name != null) entity.Name = dto.Name;
            if (dto.ConnectionString != null) entity.ConnectionString = dto.ConnectionString;
            if (dto.Description != null) entity.Description = dto.Description;
            if (dto.IsActive.HasValue) entity.IsActive = dto.IsActive.Value;

            await _context.SaveChangesAsync();
            _connectionFactory.InvalidateCache();

            var result = new SqlServerInstanceDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Description = entity.Description,
                IsActive = entity.IsActive,
                CreatedAt = entity.CreatedAt
            };
            return Ok(ApiResponse<SqlServerInstanceDto>.Ok(result, "Server updated"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update server {Id}", id);
            return StatusCode(500, ApiResponse<SqlServerInstanceDto>.Fail(ex.Message));
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "ServerManage")]
    public async Task<ActionResult<ApiResponse>> Delete(int id)
    {
        try
        {
            var entity = await _context.SqlServerInstances.FindAsync(id);
            if (entity == null)
                return NotFound(ApiResponse.Fail("Server not found"));

            // Remove associated UserDatabaseAccesses
            var accessCount = await _context.UserDatabaseAccesses
                .Where(a => a.ServerId == id)
                .CountAsync();
            if (accessCount > 0)
                return BadRequest(ApiResponse.Fail(
                    $"Cannot delete server: {accessCount} user access grant(s) still reference it. Revoke those first."));

            _context.SqlServerInstances.Remove(entity);
            await _context.SaveChangesAsync();
            _connectionFactory.InvalidateCache();

            return Ok(ApiResponse.Ok());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete server {Id}", id);
            return StatusCode(500, ApiResponse.Fail(ex.Message));
        }
    }

    [HttpPost("test")]
    [Authorize(Policy = "ServerManage")]
    public async Task<ActionResult<ApiResponse>> TestConnection([FromBody] TestServerConnectionDto dto)
    {
        try
        {
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(dto.ConnectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT @@VERSION";
            var version = (await cmd.ExecuteScalarAsync())?.ToString() ?? "Unknown";
            return Ok(ApiResponse.Ok(new { version }, "Connection successful"));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse.Fail($"Connection failed: {ex.Message}"));
        }
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
cd src && dotnet build
```

- [ ] **Step 3: Commit**

```bash
git add src/ExcelImportSystem.API/Controllers/ServerController.cs
git commit -m "feat: add ServerController CRUD API"
```

---

### Task 15: Update TableController and ImportController for ServerId

**Files:**
- Modify: `src/ExcelImportSystem.API\Controllers\TableController.cs`
- Modify: `src/ExcelImportSystem.API\Controllers\ImportController.cs`

- [ ] **Step 1: Update TableController** — add `serverId` query params

In `GetTables`, add `int? serverId = null` parameter and pass to service.

In `GetTable`, add `int? serverId = null` parameter and pass to service.

The `GetDatabases` method already returns `ServerId`/`ServerName` from the DTO, no code change needed.

- [ ] **Step 2: Build to verify**

```bash
cd src && dotnet build
```

- [ ] **Step 3: Commit**

```bash
git add src/ExcelImportSystem.API/Controllers/TableController.cs src/ExcelImportSystem.API/Controllers/ImportController.cs
git commit -m "feat: support serverId param in TableController and ImportController"
```

---

## Phase 4: Frontend

### Task 16: Update TypeScript types

**Files:**
- Modify: `frontend/src/types/index.ts`

- [ ] **Step 1: Add server types, update existing**

Add new interfaces:
```typescript
export interface SqlServerInstance {
  id: number;
  name: string;
  description?: string;
  isActive: boolean;
  createdAt: string;
}

export interface CreateServerRequest {
  name: string;
  connectionString: string;
  description?: string;
}

export interface UpdateServerRequest {
  name?: string;
  connectionString?: string;
  description?: string;
  isActive?: boolean;
}

export interface TestServerConnectionRequest {
  connectionString: string;
}
```

Update existing:
```typescript
export interface DatabaseInfo {
  name: string;
  serverId?: number;
  serverName?: string;
}

export interface UserTableAccess {
  serverId?: number;
  databaseName: string;
  schemaName?: string;
  tableName?: string;
}
```

The `ImportRequest` interface in the frontend doesn't need updating since it's built from FormData; we just need to append `serverId`.

- [ ] **Step 2: Commit**

```bash
git add frontend/src/types/index.ts
git commit -m "feat: add server types and update DatabaseInfo/UserTableAccess"
```

---

### Task 17: Create serversApi

**Files:**
- Create: `frontend/src/api/servers.ts`

- [ ] **Step 1: Write the API module**

```typescript
import apiClient from './client';
import type { ApiResponse, SqlServerInstance } from '../types';

export const serversApi = {
  getAll: () =>
    apiClient.get<ApiResponse<SqlServerInstance[]>>('/server'),

  getById: (id: number) =>
    apiClient.get<ApiResponse<SqlServerInstance>>(`/server/${id}`),

  create: (data: { name: string; connectionString: string; description?: string }) =>
    apiClient.post<ApiResponse<SqlServerInstance>>('/server', data),

  update: (id: number, data: { name?: string; connectionString?: string; description?: string; isActive?: boolean }) =>
    apiClient.put<ApiResponse<SqlServerInstance>>(`/server/${id}`, data),

  delete: (id: number) =>
    apiClient.delete<ApiResponse<null>>(`/server/${id}`),

  test: (connectionString: string) =>
    apiClient.post<ApiResponse<{ version: string }>>('/server/test', { connectionString }),
};
```

- [ ] **Step 2: Commit**

```bash
git add frontend/src/api/servers.ts
git commit -m "feat: add servers API module"
```

---

### Task 18: Create ServersPage

**Files:**
- Create: `frontend/src/pages/Servers/ServersPage.tsx`

- [ ] **Step 1: Write the page**

```typescript
import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { Card, Table, Typography, Space, Button, Modal, Form, Input, Switch, message, Popconfirm, Tag } from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined, CloudServerOutlined, LinkOutlined } from '@ant-design/icons';
import { serversApi } from '../../api/servers';
import { useAuth } from '../../context/AuthContext';
import type { SqlServerInstance } from '../../types';

const { Title } = Typography;

export default function ServersPage() {
  const { t } = useTranslation();
  const { hasPermission } = useAuth();
  const [loading, setLoading] = useState(false);
  const [servers, setServers] = useState<SqlServerInstance[]>([]);
  const [modalVisible, setModalVisible] = useState(false);
  const [editingServer, setEditingServer] = useState<SqlServerInstance | null>(null);
  const [testModalVisible, setTestModalVisible] = useState(false);
  const [testing, setTesting] = useState(false);
  const [form] = Form.useForm();
  const [testForm] = Form.useForm();
  const [submitting, setSubmitting] = useState(false);

  const fetchServers = async () => {
    setLoading(true);
    try {
      const res = await serversApi.getAll();
      setServers(res.data.data ?? []);
    } catch {
      message.error('Failed to load servers');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { fetchServers(); }, []);

  const handleCreate = () => {
    setEditingServer(null);
    form.resetFields();
    setModalVisible(true);
  };

  const handleEdit = (server: SqlServerInstance) => {
    setEditingServer(server);
    form.setFieldsValue({ name: server.name, description: server.description, isActive: server.isActive });
    setModalVisible(true);
  };

  const handleSubmit = async () => {
    try {
      const values = await form.validateFields();
      setSubmitting(true);
      if (editingServer) {
        await serversApi.update(editingServer.id, values);
        message.success('Server updated');
      } else {
        await serversApi.create(values);
        message.success('Server created');
      }
      setModalVisible(false);
      fetchServers();
    } catch (err: unknown) {
      if ((err as { errorFields?: unknown })?.errorFields) return;
      message.error((err as { response?: { data?: { message?: string } } })?.response?.data?.message || 'Failed');
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (id: number) => {
    try {
      await serversApi.delete(id);
      message.success('Server deleted');
      fetchServers();
    } catch (err: unknown) {
      message.error((err as { response?: { data?: { message?: string } } })?.response?.data?.message || 'Failed to delete');
    }
  };

  const handleTestConnection = async () => {
    try {
      const values = await testForm.validateFields();
      setTesting(true);
      const res = await serversApi.test(values.connectionString);
      message.success(res.data.message || 'Connection successful');
      setTestModalVisible(false);
    } catch (err: unknown) {
      if ((err as { errorFields?: unknown })?.errorFields) return;
      message.error((err as { response?: { data?: { message?: string } } })?.response?.data?.message || 'Connection failed');
    } finally {
      setTesting(false);
    }
  };

  const columns = [
    { title: 'ID', dataIndex: 'id', key: 'id', width: 60 },
    { title: 'Name', dataIndex: 'name', key: 'name' },
    { title: 'Description', dataIndex: 'description', key: 'description', render: (v?: string) => v || '-' },
    {
      title: 'Status',
      dataIndex: 'isActive',
      key: 'isActive',
      width: 80,
      render: (v: boolean) => <Tag color={v ? 'green' : 'red'}>{v ? 'Active' : 'Inactive'}</Tag>,
    },
    { title: 'Created', dataIndex: 'createdAt', key: 'createdAt', render: (v: string) => new Date(v).toLocaleDateString() },
    ...(hasPermission('Server.Manage') ? [{
      title: 'Actions',
      key: 'actions',
      render: (_: unknown, record: SqlServerInstance) => (
        <Space>
          <Button type="link" icon={<EditOutlined />} onClick={() => handleEdit(record)}>Edit</Button>
          <Popconfirm title="Delete this server?" onConfirm={() => handleDelete(record.id)} okText="Delete" cancelText="Cancel" okButtonProps={{ danger: true }}>
            <Button type="link" danger icon={<DeleteOutlined />}>Delete</Button>
          </Popconfirm>
        </Space>
      ),
    }] : []),
  ];

  return (
    <div>
      <Title level={4}><CloudServerOutlined style={{ marginRight: 8 }} />Server Instances</Title>
      <Card>
        <div style={{ marginBottom: 16, display: 'flex', gap: 8 }}>
          {hasPermission('Server.Manage') && (
            <>
              <Button type="primary" icon={<PlusOutlined />} onClick={handleCreate}>Add Server</Button>
              <Button icon={<LinkOutlined />} onClick={() => { testForm.resetFields(); setTestModalVisible(true); }}>Test Connection</Button>
            </>
          )}
        </div>
        <Table dataSource={servers} columns={columns} rowKey="id" loading={loading} pagination={false} size="middle" />
      </Card>

      <Modal title={editingServer ? 'Edit Server' : 'Add Server'} open={modalVisible} onOk={handleSubmit} onCancel={() => setModalVisible(false)} confirmLoading={submitting} width={560}>
        <Form form={form} layout="vertical">
          <Form.Item name="name" label="Name" rules={[{ required: true }]}><Input /></Form.Item>
          {!editingServer && (
            <Form.Item name="connectionString" label="Connection String" rules={[{ required: true }]}>
              <Input.Password placeholder="Server=host;Database=master;User Id=sa;Password=...;TrustServerCertificate=True" />
            </Form.Item>
          )}
          {editingServer && (
            <Form.Item name="connectionString" label="Connection String (leave blank to keep current)">
              <Input.Password placeholder="Leave blank to keep current" />
            </Form.Item>
          )}
          <Form.Item name="description" label="Description"><Input /></Form.Item>
          {editingServer && (
            <Form.Item name="isActive" label="Active" valuePropName="checked"><Switch /></Form.Item>
          )}
        </Form>
      </Modal>

      <Modal title="Test Connection" open={testModalVisible} onOk={handleTestConnection} onCancel={() => setTestModalVisible(false)} confirmLoading={testing} okText="Test">
        <Form form={testForm} layout="vertical">
          <Form.Item name="connectionString" label="Connection String" rules={[{ required: true }]}>
            <Input.Password placeholder="Server=host;Database=master;User Id=sa;Password=...;TrustServerCertificate=True" />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add frontend/src/pages/Servers/ServersPage.tsx
git commit -m "feat: add ServersPage for managing SQL Server instances"
```

---

### Task 19: Update App.tsx and AppLayout for /servers route

**Files:**
- Modify: `frontend/src/App.tsx`
- Modify: `frontend/src/components/AppLayout.tsx`

- [ ] **Step 1: Add route in App.tsx**

Add import:
```typescript
import ServersPage from './pages/Servers/ServersPage';
```

Add route inside `<ProtectedRoute>` (after the `/login-logs` route):
```typescript
        <Route path="servers" element={<ServersPage />} />
```

- [ ] **Step 2: Add menu item in AppLayout.tsx**

Add import:
```typescript
import { ..., CloudServerOutlined } from '@ant-design/icons';
```

Add menu item (visible only to Server.View permission):
```typescript
    ...(hasPermission('Server.View')
      ? [{
            key: '/servers',
            icon: <CloudServerOutlined />,
            label: t('nav.servers'),
          }]
      : []),
```

- [ ] **Step 3: Commit**

```bash
git add frontend/src/App.tsx frontend/src/components/AppLayout.tsx
git commit -m "feat: add /servers route and nav menu item"
```

---

### Task 20: Update ImportPage for server selector

**Files:**
- Modify: `frontend/src/pages/Import/ImportPage.tsx`

- [ ] **Step 1: Add server selector state and dropdown**

Add state:
```typescript
  const [selectedServerId, setSelectedServerId] = useState<number | undefined>(undefined);
```

Add a server selector `<Form.Item>` before the database selector. When server changes, reset database and load databases.

The database `Select` options should be filtered/mapped to include server context:
```typescript
options={databases.map((d) => ({
  label: d.serverName ? `[${d.serverName}] ${d.name}` : d.name,
  value: d.name,
}))}
```

When sending preview/execute FormData, also append `serverId`:
```typescript
if (selectedServerId) formData.append('serverId', String(selectedServerId));
```

- [ ] **Step 2: Commit**

```bash
git add frontend/src/pages/Import/ImportPage.tsx
git commit -m "feat: add server selector to ImportPage"
```

---

### Task 21: Update UsersPage database access modal for ServerId

**Files:**
- Modify: `frontend/src/pages/Users/UsersPage.tsx`

- [ ] **Step 1: Group databases by server in the access modal**

The `allDatabases` list now includes `serverId` and `serverName`. Group by server in the render and propagate `serverId` through toggle/save operations.

Update `UserTableAccess` objects to include `serverId`:
```typescript
const handleToggleWildcard = (serverId: number | undefined, database: string, checked: boolean) => {
  if (checked) {
    setUserAccesses(prev => [
      ...prev.filter(a => !(a.databaseName === database && a.serverId === serverId)),
      { serverId, databaseName: database },
    ]);
  } else {
    setUserAccesses(prev => prev.filter(a => !(a.databaseName === database && a.serverId === serverId)));
  }
};
```

Similarly update `handleToggleTable` to include `serverId`.

- [ ] **Step 2: Commit**

```bash
git add frontend/src/pages/Users/UsersPage.tsx
git commit -m "feat: add ServerId support to UsersPage database access modal"
```

---

### Task 22: Update i18n

**Files:**
- Modify: `frontend/src/i18n/locales/en.json`
- Modify: `frontend/src/i18n/locales/zh.json`

- [ ] **Step 1: Add English keys**

Add to `nav`:
```json
"servers": "Servers"
```

Add new section:
```json
"servers": {
  "pageTitle": "Server Instances",
  "addServer": "Add Server",
  "editServer": "Edit Server",
  "name": "Name",
  "connectionString": "Connection String",
  "description": "Description",
  "testConnection": "Test Connection",
  "active": "Active",
  "inactive": "Inactive"
}
```

- [ ] **Step 2: Add Chinese keys**

Same structure in zh.json with Chinese translations.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/i18n/locales/en.json frontend/src/i18n/locales/zh.json
git commit -m "feat: add i18n keys for server management"
```

---

### Task 23: Update API modules for serverId params

**Files:**
- Modify: `frontend/src/api/tables.ts`

- [ ] **Step 1: Add serverId param support**

In `tablesApi.getAll` and `tablesApi.getOne`, add optional `serverId` parameter passed as query param.

- [ ] **Step 2: Commit**

```bash
git add frontend/src/api/tables.ts
git commit -m "feat: add serverId query param to tables API"
```

---

### Task 24: Final verification and smoke test

- [ ] **Step 1: Build backend**

```bash
cd src && dotnet build
```
Expected: 0 errors

- [ ] **Step 2: Build frontend**

```bash
cd frontend && npm run build
```
Expected: no TS errors

- [ ] **Step 3: Run backend and verify API starts**

```bash
cd src && dotnet run
```
Expected: application starts, database migration scripts run

- [ ] **Step 4: Run frontend dev server**

```bash
cd frontend && npm run dev
```
Expected: loads without errors

- [ ] **Step 5: Manual test checklist**
  - Login as admin → see "Servers" in sidebar
  - Add a test server instance
  - Test connection
  - Navigate to Import → see databases from both local and remote
  - Import flow still works for local databases
  - User DB access modal shows server-grouped databases
