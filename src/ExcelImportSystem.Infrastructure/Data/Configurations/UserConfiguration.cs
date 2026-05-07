using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ExcelImportSystem.Core.Entities;

namespace ExcelImportSystem.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Username).HasMaxLength(100).IsRequired();
        builder.HasIndex(u => u.Username).IsUnique();
        builder.Property(u => u.PasswordHash).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(200);
        builder.Property(u => u.AuthType).HasMaxLength(20).IsRequired().HasDefaultValue("Local");
        builder.Property(u => u.LdapDn).HasMaxLength(500);
    }
}

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(r => r.Name).IsUnique();
        builder.Property(r => r.Description).HasMaxLength(500);
    }
}

public class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles");
        builder.HasKey(ur => ur.Id);
        builder.HasOne(ur => ur.User).WithMany(u => u.UserRoles).HasForeignKey(ur => ur.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(ur => ur.Role).WithMany(r => r.UserRoles).HasForeignKey(ur => ur.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(ur => new { ur.UserId, ur.RoleId }).IsUnique();
    }
}

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions");
        builder.HasKey(rp => rp.Id);
        builder.Property(rp => rp.Permission).HasMaxLength(200).IsRequired();
        builder.HasOne(rp => rp.Role).WithMany(r => r.RolePermissions).HasForeignKey(rp => rp.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(rp => new { rp.RoleId, rp.Permission }).IsUnique();
    }
}

public class ImportLogConfiguration : IEntityTypeConfiguration<ImportLog>
{
    public void Configure(EntityTypeBuilder<ImportLog> builder)
    {
        builder.ToTable("ImportLogs");
        builder.HasKey(il => il.Id);
        builder.Property(il => il.UserName).HasMaxLength(200).IsRequired();
        builder.Property(il => il.TargetTable).HasMaxLength(200).IsRequired();
        builder.Property(il => il.FileName).HasMaxLength(500).IsRequired();
        builder.Property(il => il.Status).HasMaxLength(50).IsRequired();
        builder.HasOne(il => il.User).WithMany().HasForeignKey(il => il.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(il => il.ImportedAt);
        builder.HasIndex(il => il.TargetTable);
    }
}
