using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ExcelImportSystem.Core.Configurations;
using ExcelImportSystem.Core.DTOs;
using ExcelImportSystem.Core.Interfaces;
using ExcelImportSystem.Infrastructure.Services;

namespace ExcelImportSystem.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "SystemManage")]
public class SystemSettingsController : ControllerBase
{
    private readonly LdapSettingsProvider _ldapProvider;
    private readonly ILdapService _ldapService;
    private readonly ILogger<SystemSettingsController> _logger;

    public SystemSettingsController(
        LdapSettingsProvider ldapProvider,
        ILdapService ldapService,
        ILogger<SystemSettingsController> logger)
    {
        _ldapProvider = ldapProvider;
        _ldapService = ldapService;
        _logger = logger;
    }

    [HttpGet("ldap")]
    public async Task<ActionResult<ApiResponse<LdapSettingsDto>>> GetLdapSettings()
    {
        try
        {
            var settings = await _ldapProvider.GetSettingsAsync();
            return Ok(ApiResponse<LdapSettingsDto>.Ok(MapToDto(settings)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load LDAP settings");
            return StatusCode(500, ApiResponse<LdapSettingsDto>.Fail(ex.Message));
        }
    }

    [HttpPut("ldap")]
    public async Task<ActionResult<ApiResponse>> UpdateLdapSettings([FromBody] LdapSettingsDto dto)
    {
        try
        {
            var settings = MapFromDto(dto);
            await _ldapProvider.UpdateSettingsAsync(settings);
            return Ok(ApiResponse.Ok());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save LDAP settings");
            return StatusCode(500, ApiResponse.Fail(ex.Message));
        }
    }

    [HttpPost("ldap/test")]
    public async Task<ActionResult<ApiResponse>> TestLdapConnection([FromBody] LdapSettingsDto dto)
    {
        try
        {
            var settings = MapFromDto(dto);

            // Test only — do not persist settings
            var result = await _ldapService.AuthenticateAsync(dto.TestUsername ?? "", dto.TestPassword ?? "");
            return Ok(ApiResponse<object>.Ok(new
            {
                success = result.Success,
                dn = result.Dn,
                displayName = result.DisplayName,
                email = result.Email,
            }, result.Success ? "Connection successful" : "Connection failed: invalid credentials"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<object>.Fail($"Connection test failed: {ex.Message}"));
        }
    }

    private static LdapSettingsDto MapToDto(LdapSettings s) => new()
    {
        Enabled = s.Enabled,
        Server = s.Server,
        Port = s.Port,
        UseSsl = s.UseSsl,
        Domain = s.Domain,
        BaseDn = s.BaseDn,
        UserFilterTemplate = s.UserFilterTemplate,
        BindUserDn = s.BindUserDn,
        BindPasswordSet = !string.IsNullOrEmpty(s.BindPassword),
    };

    private static LdapSettings MapFromDto(LdapSettingsDto d) => new()
    {
        Enabled = d.Enabled,
        Server = d.Server ?? "",
        Port = d.Port,
        UseSsl = d.UseSsl,
        Domain = d.Domain ?? "",
        BaseDn = d.BaseDn ?? "",
        UserFilterTemplate = d.UserFilterTemplate ?? "(sAMAccountName={0})",
        BindUserDn = d.BindUserDn ?? "",
        BindPassword = d.BindPassword ?? "",
    };
}

public class LdapSettingsDto
{
    public bool Enabled { get; set; }
    public string Server { get; set; } = "";
    public int Port { get; set; } = 389;
    public bool UseSsl { get; set; }
    public string Domain { get; set; } = "";
    public string BaseDn { get; set; } = "";
    public string UserFilterTemplate { get; set; } = "(sAMAccountName={0})";
    public string BindUserDn { get; set; } = "";
    public string? BindPassword { get; set; }
    public bool BindPasswordSet { get; set; }
    public string? TestUsername { get; set; }
    public string? TestPassword { get; set; }
}
