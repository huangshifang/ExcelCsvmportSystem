using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ExcelImportSystem.Core.DTOs;
using ExcelImportSystem.Core.Interfaces;

namespace ExcelImportSystem.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<LoginResponseDto>>> Login([FromBody] LoginDto dto)
    {
        try
        {
            var result = await _authService.LoginAsync(dto);
            return Ok(ApiResponse<LoginResponseDto>.Ok(result));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(ApiResponse<LoginResponseDto>.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<LoginResponseDto>.Fail(
                $"Server error: {ex.Message}"));
        }
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<UserInfoDto>>> GetCurrentUser()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await _authService.GetUserInfoAsync(userId);
        return Ok(ApiResponse<UserInfoDto>.Ok(user));
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<ActionResult<ApiResponse>> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        try
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _authService.ChangePasswordAsync(userId, dto);
            return Ok(ApiResponse.Ok());
        }
        catch (UnauthorizedAccessException ex)
        {
            return BadRequest(ApiResponse.Fail(ex.Message));
        }
    }

    [HttpGet("users")]
    [Authorize(Policy = "UserManage")]
    public async Task<ActionResult<ApiResponse<PagedResult<UserInfoDto>>>> GetUsers(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null)
    {
        var result = await _authService.GetUsersAsync(page, pageSize, search);
        return Ok(ApiResponse<PagedResult<UserInfoDto>>.Ok(result));
    }

    [HttpPost("users")]
    [Authorize(Policy = "UserManage")]
    public async Task<ActionResult<ApiResponse<UserInfoDto>>> CreateUser([FromBody] CreateUserDto dto)
    {
        try
        {
            var user = await _authService.CreateUserAsync(dto);
            return Ok(ApiResponse<UserInfoDto>.Ok(user, "User created successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<UserInfoDto>.Fail(ex.Message));
        }
    }

    [HttpPut("users/{id}")]
    [Authorize(Policy = "UserManage")]
    public async Task<ActionResult<ApiResponse<UserInfoDto>>> UpdateUser(int id, [FromBody] UpdateUserDto dto)
    {
        try
        {
            var user = await _authService.UpdateUserAsync(id, dto);
            return Ok(ApiResponse<UserInfoDto>.Ok(user, "User updated successfully"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<UserInfoDto>.Fail(ex.Message));
        }
    }

    [HttpDelete("users/{id}")]
    [Authorize(Policy = "UserManage")]
    public async Task<ActionResult<ApiResponse>> DeleteUser(int id)
    {
        try
        {
            await _authService.DeleteUserAsync(id);
            return Ok(ApiResponse.Ok());
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return BadRequest(ApiResponse.Fail(ex.Message));
        }
    }

    [HttpPost("users/{id}/link-ldap")]
    [Authorize(Policy = "UserManage")]
    public async Task<ActionResult<ApiResponse<UserInfoDto>>> LinkLdap(int id, [FromBody] LinkLdapDto dto)
    {
        try
        {
            var user = await _authService.LinkLdapAsync(id, dto);
            return Ok(ApiResponse<UserInfoDto>.Ok(user, "AD account linked successfully"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<UserInfoDto>.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<UserInfoDto>.Fail(ex.Message));
        }
    }

    [HttpDelete("users/{id}/unlink-ldap")]
    [Authorize(Policy = "UserManage")]
    public async Task<ActionResult<ApiResponse>> UnlinkLdap(int id)
    {
        try
        {
            await _authService.UnlinkLdapAsync(id);
            return Ok(ApiResponse.Ok());
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail(ex.Message));
        }
    }

    [HttpGet("ldap-users")]
    [Authorize(Policy = "UserManage")]
    public async Task<ActionResult<ApiResponse<List<LdapSearchResultDto>>>> SearchLdapUsers(
        [FromQuery] string? search = null)
    {
        try
        {
            var results = await _authService.SearchLdapUsersAsync(search);
            return Ok(ApiResponse<List<LdapSearchResultDto>>.Ok(results));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<List<LdapSearchResultDto>>.Fail(
                $"LDAP search failed: {ex.Message}"));
        }
    }
}
