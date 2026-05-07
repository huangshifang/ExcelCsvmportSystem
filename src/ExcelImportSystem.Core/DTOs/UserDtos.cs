using System.ComponentModel.DataAnnotations;

namespace ExcelImportSystem.Core.DTOs;

public class LoginDto
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class LoginResponseDto
{
    public string Token { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
}

public class UserInfoDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public List<string> Roles { get; set; } = new();
    public string AuthType { get; set; } = "Local";
    public string? LdapDn { get; set; }
}

public class ChangePasswordDto
{
    [Required]
    public string OldPassword { get; set; } = string.Empty;
    [Required]
    [MinLength(6)]
    public string NewPassword { get; set; } = string.Empty;
}

public class CreateUserDto
{
    [Required]
    public string Username { get; set; } = string.Empty;
    [MinLength(6)]
    public string? Password { get; set; }
    [Required]
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<int> RoleIds { get; set; } = new();
    public string? AuthType { get; set; }
    public string? LdapDn { get; set; }
}

public class UpdateUserDto
{
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public bool? IsActive { get; set; }
    public List<int>? RoleIds { get; set; }
    public string? AuthType { get; set; }
    public string? LdapDn { get; set; }
}

public class LinkLdapDto
{
    [Required]
    public string Username { get; set; } = string.Empty;
}

public class LdapSearchResultDto
{
    public string Dn { get; set; } = string.Empty;
    public string SamAccountName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
