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
                && a.DatabaseName == databaseName
                && a.SchemaName == schemaName
                && a.TableName == tableName
                && a.ServerId == serverId);

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

    public async Task RevokeAccessAsync(int userId, string databaseName, string? schemaName, string? tableName, int? serverId = null)
    {
        var access = await _context.UserDatabaseAccesses
            .FirstOrDefaultAsync(a => a.UserId == userId
                && a.DatabaseName == databaseName
                && a.SchemaName == schemaName
                && a.TableName == tableName
                && a.ServerId == serverId);

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
            // Database-level check: user has any access record for this database
            return await _context.UserDatabaseAccesses
                .AnyAsync(a => a.UserId == userId
                    && a.DatabaseName == databaseName
                    && a.ServerId == serverId);
        }

        // Table-level check: either a wildcard grant (TableName IS NULL) or exact match
        return await _context.UserDatabaseAccesses
            .AnyAsync(a => a.UserId == userId
                && a.DatabaseName == databaseName
                && a.ServerId == serverId
                && (a.TableName == null
                    || (a.SchemaName == schemaName && a.TableName == tableName)));
    }
}
