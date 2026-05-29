using System.ComponentModel.DataAnnotations;

namespace ExcelImportSystem.Core.DTOs;

public class SqlServerInstanceDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateServerDto
{
    [Required]
    public string Name { get; set; } = string.Empty;
    [Required]
    public string ConnectionString { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdateServerDto
{
    public string? Name { get; set; }
    public string? ConnectionString { get; set; }
    public string? Description { get; set; }
    public bool? IsActive { get; set; }
}

public class TestServerConnectionDto
{
    [Required]
    public string ConnectionString { get; set; } = string.Empty;
}
