using Microsoft.EntityFrameworkCore;
using ExcelImportSystem.Core.Entities;

namespace ExcelImportSystem.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<ImportLog> ImportLogs => Set<ImportLog>();
    public DbSet<UserDatabaseAccess> UserDatabaseAccesses => Set<UserDatabaseAccess>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<LoginAuditLog> LoginAuditLogs => Set<LoginAuditLog>();
    public DbSet<SqlServerInstance> SqlServerInstances => Set<SqlServerInstance>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
