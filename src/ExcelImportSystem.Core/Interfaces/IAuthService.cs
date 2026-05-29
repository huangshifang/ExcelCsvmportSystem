using ExcelImportSystem.Core.DTOs;

namespace ExcelImportSystem.Core.Interfaces;

public interface IAuthService
{
    Task<LoginResponseDto> LoginAsync(LoginDto dto);
    Task<UserInfoDto> GetUserInfoAsync(int userId);
    Task<bool> ChangePasswordAsync(int userId, ChangePasswordDto dto);
    Task<bool> ResetPasswordAsync(int userId, string newPassword);
    Task<PagedResult<UserInfoDto>> GetUsersAsync(int page, int pageSize, string? search = null);
    Task<UserInfoDto> CreateUserAsync(CreateUserDto dto);
    Task<UserInfoDto> UpdateUserAsync(int userId, UpdateUserDto dto);
    Task<bool> DeleteUserAsync(int userId);
    Task<UserInfoDto> LinkLdapAsync(int userId, LinkLdapDto dto);
    Task<bool> UnlinkLdapAsync(int userId);
    Task<List<LdapSearchResultDto>> SearchLdapUsersAsync(string? search);
}
