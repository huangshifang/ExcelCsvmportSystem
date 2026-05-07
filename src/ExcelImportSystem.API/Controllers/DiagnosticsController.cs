using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExcelImportSystem.Infrastructure.Data;

namespace ExcelImportSystem.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DiagnosticsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;

    public DiagnosticsController(AppDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    [HttpGet("health")]
    public async Task<ActionResult> Health()
    {
        var dbOk = false;
        var userCount = 0;
        var dbError = "";

        try
        {
            userCount = await _context.Users.CountAsync();
            dbOk = true;
        }
        catch (Exception ex)
        {
            dbError = ex.Message;
        }

        return Ok(new
        {
            status = dbOk ? "healthy" : "degraded",
            database = new
            {
                connected = dbOk,
                userCount,
                error = dbError
            },
            server = Environment.MachineName,
            time = DateTime.UtcNow
        });
    }
}
