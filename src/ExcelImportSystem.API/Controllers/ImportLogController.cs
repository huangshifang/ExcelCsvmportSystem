using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ExcelImportSystem.Core.DTOs;
using ExcelImportSystem.Core.Interfaces;

namespace ExcelImportSystem.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "LogView")]
public class ImportLogController : ControllerBase
{
    private readonly IImportLogService _logService;

    public ImportLogController(IImportLogService logService)
    {
        _logService = logService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<ImportLogDto>>>> GetLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? tableName = null,
        [FromQuery] string? status = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var result = await _logService.GetLogsAsync(page, pageSize, tableName, status, from, to);
        return Ok(ApiResponse<PagedResult<ImportLogDto>>.Ok(result));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ImportLogDto>>> GetLog(int id)
    {
        var log = await _logService.GetLogByIdAsync(id);
        if (log == null)
            return NotFound(ApiResponse<ImportLogDto>.Fail("Log not found"));

        return Ok(ApiResponse<ImportLogDto>.Ok(log));
    }
}
