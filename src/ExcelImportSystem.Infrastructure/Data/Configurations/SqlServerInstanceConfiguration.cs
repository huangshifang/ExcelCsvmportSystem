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
