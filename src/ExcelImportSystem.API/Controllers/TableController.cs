using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ExcelImportSystem.Core.DTOs;
using ExcelImportSystem.Core.Interfaces;

namespace ExcelImportSystem.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TableController : ControllerBase
{
    private readonly ITableService _tableService;
    private readonly ILogger<TableController> _logger;

    public TableController(ITableService tableService, ILogger<TableController> logger)
    {
        _tableService = tableService;
        _logger = logger;
    }

    private int GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return int.Parse(claim?.Value ?? "0");
    }

    [HttpGet("databases")]
    public async Task<ActionResult<ApiResponse<List<DatabaseInfoDto>>>> GetDatabases()
    {
        try
        {
            var userId = GetUserId();
            var dbs = await _tableService.GetDatabasesAsync(userId);
            return Ok(ApiResponse<List<DatabaseInfoDto>>.Ok(dbs));
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Failed to list databases");
            return StatusCode(500, ApiResponse<List<DatabaseInfoDto>>.Fail("Failed to connect to database server"));
        }
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<TableInfoDto>>>> GetTables(
        [FromQuery] string? database = null, [FromQuery] string? schema = null, [FromQuery] int? serverId = null)
    {
        if (string.IsNullOrWhiteSpace(database))
            return BadRequest(ApiResponse<List<TableInfoDto>>.Fail("Database name is required"));

        try
        {
            var userId = GetUserId();
            var tables = await _tableService.GetTablesAsync(database, schema, userId, serverId);
            return Ok(ApiResponse<List<TableInfoDto>>.Ok(tables));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<List<TableInfoDto>>.Fail(ex.Message));
        }
        catch (SqlException ex) when (ex.Number == 916)
        {
            return BadRequest(ApiResponse<List<TableInfoDto>>.Fail(
                $"Access denied to database '{database}'. Your login does not have permission to access this database."));
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Failed to query tables in database '{Database}'", database);
            return StatusCode(500, ApiResponse<List<TableInfoDto>>.Fail(
                $"Failed to query tables in database '{database}': {ex.Message}"));
        }
    }

    [HttpGet("{tableName}")]
    public async Task<ActionResult<ApiResponse<TableInfoDto>>> GetTable(
        string tableName, [FromQuery] string? database = null, [FromQuery] string schema = "dbo", [FromQuery] int? serverId = null)
    {
        if (string.IsNullOrWhiteSpace(database))
            return BadRequest(ApiResponse<TableInfoDto>.Fail("Database name is required"));

        try
        {
            var userId = GetUserId();
            var table = await _tableService.GetTableAsync(database, tableName, schema, userId, serverId);
            if (table == null)
                return NotFound(ApiResponse<TableInfoDto>.Fail(
                    $"Table '{database}.{schema}.{tableName}' not found"));

            return Ok(ApiResponse<TableInfoDto>.Ok(table));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<TableInfoDto>.Fail(ex.Message));
        }
        catch (SqlException ex) when (ex.Number == 916)
        {
            return BadRequest(ApiResponse<TableInfoDto>.Fail(
                $"Access denied to database '{database}'. Your login does not have permission to access this database."));
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Failed to get table info for {Db}.{Schema}.{Table}",
                database, schema, tableName);
            return StatusCode(500, ApiResponse<TableInfoDto>.Fail(
                $"Failed to get table info: {ex.Message}"));
        }
    }
}
