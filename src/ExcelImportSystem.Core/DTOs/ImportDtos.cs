using Microsoft.AspNetCore.Http;

namespace ExcelImportSystem.Core.DTOs;

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

public class ColumnMappingDto
{
    public string ExcelColumn { get; set; } = string.Empty;
    public string TableColumn { get; set; } = string.Empty;
}

public class ImportPreviewDto
{
    public List<string> ExcelColumns { get; set; } = new();
    public List<Dictionary<string, string>> SampleData { get; set; } = new();
    public List<ColumnInfoDto> TableColumns { get; set; } = new();
    public List<ColumnMappingDto> AutoMappings { get; set; } = new();
    public int TotalRows { get; set; }
}

public class ImportResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public int ImportedRows { get; set; }
    public int FailedRows { get; set; }
    public List<string> Errors { get; set; } = new();
    public int? ImportLogId { get; set; }
}

public class ImportExecuteResponseDto
{
    public string TaskId { get; set; } = string.Empty;
}

public class ImportProgressDto
{
    public string TaskId { get; set; } = string.Empty;
    public string Status { get; set; } = "pending"; // pending | reading | importing | completed | failed
    public int Percent { get; set; }
    public int TotalRows { get; set; }
    public int ProcessedRows { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ErrorCount { get; set; }
    public ImportResultDto? Result { get; set; }
}
