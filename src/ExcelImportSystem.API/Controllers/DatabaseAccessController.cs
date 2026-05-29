using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ExcelImportSystem.Core.DTOs;
using ExcelImportSystem.Core.Interfaces;

namespace ExcelImportSystem.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DatabaseAccessController : ControllerBase
{
    private readonly IDatabaseAccessService _dbAccessService;
    private readonly ITableService _tableService;

    public DatabaseAccessController(IDatabaseAccessService dbAccessService, ITableService tableService)
    {
        _dbAccessService = dbAccessService;
        _tableService = tableService;
    }

    private int GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return int.Parse(claim?.Value ?? "0");
    }

    private string GetUserName()
    {
        return User.FindFirst(ClaimTypes.GivenName)?.Value
            ?? User.FindFirst(ClaimTypes.Name)?.Value
            ?? "Unknown";
    }

    [HttpGet("available-databases")]
    [Authorize(Policy = "DatabaseManage")]
    public async Task<ActionResult<ApiResponse<List<DatabaseInfoDto>>>> GetAvailableDatabases()
    {
        try
        {
            var dbs = await _tableService.GetDatabasesAsync();
            return Ok(ApiResponse<List<DatabaseInfoDto>>.Ok(dbs));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<List<DatabaseInfoDto>>.Fail(ex.Message));
        }
    }

    [HttpGet("tables/{database}")]
    [Authorize(Policy = "DatabaseManage")]
    public async Task<ActionResult<ApiResponse<List<TableInfoDto>>>> GetDatabaseTables(
        string database, [FromQuery] int? serverId = null)
    {
        try
        {
            var tables = await _tableService.GetTablesAsync(database, serverId: serverId);
            return Ok(ApiResponse<List<TableInfoDto>>.Ok(tables));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<List<TableInfoDto>>.Fail(ex.Message));
        }
    }

    [HttpGet("user/{userId}")]
    [Authorize(Policy = "DatabaseManage")]
    public async Task<ActionResult<ApiResponse<List<UserTableAccessDto>>>> GetUserTableAccesses(int userId)
    {
        try
        {
            var accesses = await _dbAccessService.GetUserTableAccessesAsync(userId);
            return Ok(ApiResponse<List<UserTableAccessDto>>.Ok(accesses));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<List<UserTableAccessDto>>.Fail(ex.Message));
        }
    }

    [HttpPut("user/{userId}")]
    [Authorize(Policy = "DatabaseManage")]
    public async Task<ActionResult<ApiResponse>> SetUserTableAccesses(
        int userId, [FromBody] List<UserTableAccessDto> accesses)
    {
        try
        {
            var grantedBy = GetUserName();
            await _dbAccessService.SetUserTableAccessesAsync(userId, accesses, grantedBy);
            return Ok(ApiResponse.Ok());
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse.Fail(ex.Message));
        }
    }

    [HttpGet("my-databases")]
    public async Task<ActionResult<ApiResponse<List<string>>>> GetMyDatabases()
    {
        try
        {
            var userId = GetUserId();
            var dbs = await _dbAccessService.GetUserDatabasesAsync(userId);
            return Ok(ApiResponse<List<string>>.Ok(dbs));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<List<string>>.Fail(ex.Message));
        }
    }
}
