namespace ExcelImportSystem.Core.DTOs;

public class DashboardStatsDto
{
    public int TotalImports { get; set; }
    public int TotalRows { get; set; }
    public int SuccessRows { get; set; }
    public int FailedRows { get; set; }
}

public class ImportLogDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string TargetTable { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public int SuccessRows { get; set; }
    public int FailedRows { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public int? ServerId { get; set; }
    public string? ServerName { get; set; }
    public DateTime ImportedAt { get; set; }
}

public class PagedResult<T>
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<T> Items { get; set; } = new();
}

public class LoginAuditLogDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool Success { get; set; }
    public string? FailureReason { get; set; }
    public DateTime Timestamp { get; set; }
}

public class ApiResponse<T>
{
    public bool Success { get; set; } = true;
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public List<string>? Errors { get; set; }

    public static ApiResponse<T> Ok(T data) => new() { Success = true, Data = data };
    public static ApiResponse<T> Ok(T data, string message) => new() { Success = true, Message = message, Data = data };
    public static ApiResponse<T> Fail(string message) => new() { Success = false, Message = message };
    public static ApiResponse<T> Fail(string message, List<string> errors) => new() { Success = false, Message = message, Errors = errors };
}

public class ApiResponse : ApiResponse<object>
{
    public static ApiResponse Ok() => new() { Success = true };
    public new static ApiResponse Fail(string message) => new() { Success = false, Message = message };
}

public class UserTableAccessDto
{
    public int? ServerId { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string? SchemaName { get; set; }
    public string? TableName { get; set; }
}
