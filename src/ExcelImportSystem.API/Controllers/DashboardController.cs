using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ExcelImportSystem.Core.DTOs;
using ExcelImportSystem.Core.Interfaces;

namespace ExcelImportSystem.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IImportLogService _logService;

    public DashboardController(IImportLogService logService)
    {
        _logService = logService;
    }

    [HttpGet("stats")]
    public async Task<ActionResult<ApiResponse<DashboardStatsDto>>> GetStats()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        var isAdmin = roles.Contains("Admin");

        var stats = await _logService.GetDashboardStatsAsync(isAdmin ? null : userId);
        return Ok(ApiResponse<DashboardStatsDto>.Ok(stats));
    }
}
