using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ExcelImportSystem.Core.DTOs;
using ExcelImportSystem.Core.Interfaces;

namespace ExcelImportSystem.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TableController : ControllerBase
{
    private readonly ITableService _tableService;

    public TableController(ITableService tableService)
    {
        _tableService = tableService;
    }

    [HttpGet("databases")]
    public async Task<ActionResult<ApiResponse<List<DatabaseInfoDto>>>> GetDatabases()
    {
        var dbs = await _tableService.GetDatabasesAsync();
        return Ok(ApiResponse<List<DatabaseInfoDto>>.Ok(dbs));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<TableInfoDto>>>> GetTables(
        [FromQuery] string database, [FromQuery] string? schema = null)
    {
        var tables = await _tableService.GetTablesAsync(database, schema);
        return Ok(ApiResponse<List<TableInfoDto>>.Ok(tables));
    }

    [HttpGet("{tableName}")]
    public async Task<ActionResult<ApiResponse<TableInfoDto>>> GetTable(
        string tableName, [FromQuery] string database, [FromQuery] string schema = "dbo")
    {
        var table = await _tableService.GetTableAsync(database, tableName, schema);
        if (table == null)
            return NotFound(ApiResponse<TableInfoDto>.Fail($"Table '{database}.{schema}.{tableName}' not found"));

        return Ok(ApiResponse<TableInfoDto>.Ok(table));
    }
}
