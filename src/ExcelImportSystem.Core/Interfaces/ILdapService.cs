using ExcelImportSystem.Core.DTOs;

namespace ExcelImportSystem.Core.Interfaces;

public interface ILdapService
{
    Task<(bool Success, string? Dn, string? DisplayName, string? Email)> AuthenticateAsync(string username, string password);
    Task<(string? Dn, string? DisplayName, string? Email)> LookupUserAsync(string username);
    Task<List<LdapSearchResultDto>> SearchUsersAsync(string? search);
}
