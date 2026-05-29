namespace ExcelImportSystem.Core.DTOs;

public class DatabaseInfoDto
{
    public string Name { get; set; } = string.Empty;
    public int? ServerId { get; set; }
    public string? ServerName { get; set; }
}

public class TableInfoDto
{
    public string Database { get; set; } = string.Empty;
    public string Schema { get; set; } = "dbo";
    public string TableName { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public List<ColumnInfoDto> Columns { get; set; } = new();
}

public class ColumnInfoDto
{
    public string ColumnName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public int? MaxLength { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsIdentity { get; set; }
}
