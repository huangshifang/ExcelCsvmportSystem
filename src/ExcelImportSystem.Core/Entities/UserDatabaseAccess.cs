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
