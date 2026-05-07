namespace ExcelImportSystem.Core.Entities;

public class RolePermission
{
    public int Id { get; set; }
    public int RoleId { get; set; }
    /// <summary>
    /// Permission identifier, e.g. "Import.Execute", "Import.View", "User.Manage"
    /// </summary>
    public string Permission { get; set; } = string.Empty;

    public Role Role { get; set; } = null!;
}
