using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ExcelImportSystem.Core.DTOs;
using ExcelImportSystem.Core.Interfaces;

namespace ExcelImportSystem.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ImportController : ControllerBase
{
    private readonly IImportService _importService;

    public ImportController(IImportService importService)
    {
        _importService = importService;
    }

    [HttpPost("preview")]
    [Authorize(Policy = "ImportExecute")]
    public async Task<ActionResult<ApiResponse<ImportPreviewDto>>> Preview([FromForm] ImportRequestDto request)
    {
        try
        {
            if (request.File == null || request.File.Length == 0)
                return BadRequest(ApiResponse<ImportPreviewDto>.Fail("No file uploaded"));

            var preview = await _importService.PreviewAsync(request);
            return Ok(ApiResponse<ImportPreviewDto>.Ok(preview));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<ImportPreviewDto>.Fail(ex.Message));
        }
        catch (InvalidDataException ex)
        {
            return BadRequest(ApiResponse<ImportPreviewDto>.Fail(ex.Message));
        }
    }

    [HttpPost("execute")]
    [Authorize(Policy = "ImportExecute")]
    public async Task<ActionResult<ApiResponse<ImportResultDto>>> Execute(
        [FromForm] ImportRequestDto request, [FromForm] string mappings)
    {
        try
        {
            if (request.File == null || request.File.Length == 0)
                return BadRequest(ApiResponse<ImportResultDto>.Fail("No file uploaded"));

            var columnMappings = System.Text.Json.JsonSerializer.Deserialize<List<ColumnMappingDto>>(mappings,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Invalid column mappings");

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await _importService.ExecuteAsync(userId, request, columnMappings);

            if (result.Success)
                return Ok(ApiResponse<ImportResultDto>.Ok(result, result.Message));
            else
                return Ok(ApiResponse<ImportResultDto>.Ok(result, result.Message));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or InvalidDataException)
        {
            return BadRequest(ApiResponse<ImportResultDto>.Fail(ex.Message));
        }
    }
}
