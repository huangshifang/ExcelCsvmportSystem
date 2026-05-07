namespace ExcelImportSystem.Core.Entities;

public class ImportLog
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string TargetTable { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public int SuccessRows { get; set; }
    public int FailedRows { get; set; }
    /// <summary>Status: Success, Partial, Failed</summary>
    public string Status { get; set; } = "Success";
    public string? ErrorMessage { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}
