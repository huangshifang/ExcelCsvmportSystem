using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExcelImportSystem.Core.DTOs;
using ExcelImportSystem.Core.Entities;
using ExcelImportSystem.Core.Interfaces;
using ExcelImportSystem.Infrastructure.Data;

namespace ExcelImportSystem.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ServerController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConnectionFactory _connectionFactory;
    private readonly ILogger<ServerController> _logger;

    public ServerController(AppDbContext context, IConnectionFactory connectionFactory, ILogger<ServerController> logger)
    {
        _context = context;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    [HttpGet]
    [Authorize(Policy = "ServerView")]
    public async Task<ActionResult<ApiResponse<List<SqlServerInstanceDto>>>> GetAll()
    {
        try
        {
            var servers = await _connectionFactory.GetServersAsync();
            return Ok(ApiResponse<List<SqlServerInstanceDto>>.Ok(servers));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list servers");
            return StatusCode(500, ApiResponse<List<SqlServerInstanceDto>>.Fail(ex.Message));
        }
    }

    [HttpGet("{id}")]
    [Authorize(Policy = "ServerView")]
    public async Task<ActionResult<ApiResponse<SqlServerInstanceDto>>> GetById(int id)
    {
        try
        {
            var server = await _connectionFactory.GetServerAsync(id);
            if (server == null)
                return NotFound(ApiResponse<SqlServerInstanceDto>.Fail("Server not found"));
            return Ok(ApiResponse<SqlServerInstanceDto>.Ok(server));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<SqlServerInstanceDto>.Fail(ex.Message));
        }
    }

    [HttpPost]
    [Authorize(Policy = "ServerManage")]
    public async Task<ActionResult<ApiResponse<SqlServerInstanceDto>>> Create([FromBody] CreateServerDto dto)
    {
        try
        {
            var exists = await _context.SqlServerInstances.AnyAsync(s => s.Name == dto.Name);
            if (exists)
                return BadRequest(ApiResponse<SqlServerInstanceDto>.Fail($"Server '{dto.Name}' already exists"));

            var entity = new SqlServerInstance
            {
                Name = dto.Name,
                ConnectionString = dto.ConnectionString,
                Description = dto.Description,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.SqlServerInstances.Add(entity);
            await _context.SaveChangesAsync();

            _connectionFactory.InvalidateCache();

            var result = new SqlServerInstanceDto
            {
                Id = entity.Id, Name = entity.Name,
                Description = entity.Description,
                IsActive = entity.IsActive, CreatedAt = entity.CreatedAt
            };
            return Ok(ApiResponse<SqlServerInstanceDto>.Ok(result, "Server created"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create server");
            return StatusCode(500, ApiResponse<SqlServerInstanceDto>.Fail(ex.Message));
        }
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "ServerManage")]
    public async Task<ActionResult<ApiResponse<SqlServerInstanceDto>>> Update(int id, [FromBody] UpdateServerDto dto)
    {
        try
        {
            var entity = await _context.SqlServerInstances.FindAsync(id);
            if (entity == null)
                return NotFound(ApiResponse<SqlServerInstanceDto>.Fail("Server not found"));

            if (dto.Name != null) entity.Name = dto.Name;
            if (dto.ConnectionString != null) entity.ConnectionString = dto.ConnectionString;
            if (dto.Description != null) entity.Description = dto.Description;
            if (dto.IsActive.HasValue) entity.IsActive = dto.IsActive.Value;

            await _context.SaveChangesAsync();
            _connectionFactory.InvalidateCache();

            var result = new SqlServerInstanceDto
            {
                Id = entity.Id, Name = entity.Name,
                Description = entity.Description,
                IsActive = entity.IsActive, CreatedAt = entity.CreatedAt
            };
            return Ok(ApiResponse<SqlServerInstanceDto>.Ok(result, "Server updated"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update server {Id}", id);
            return StatusCode(500, ApiResponse<SqlServerInstanceDto>.Fail(ex.Message));
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "ServerManage")]
    public async Task<ActionResult<ApiResponse>> Delete(int id)
    {
        try
        {
            var entity = await _context.SqlServerInstances.FindAsync(id);
            if (entity == null)
                return NotFound(ApiResponse.Fail("Server not found"));

            var accessCount = await _context.UserDatabaseAccesses
                .Where(a => a.ServerId == id).CountAsync();
            if (accessCount > 0)
                return BadRequest(ApiResponse.Fail(
                    $"Cannot delete: {accessCount} user access grants still reference this server. Revoke them first."));

            _context.SqlServerInstances.Remove(entity);
            await _context.SaveChangesAsync();
            _connectionFactory.InvalidateCache();
            return Ok(ApiResponse.Ok());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete server {Id}", id);
            return StatusCode(500, ApiResponse.Fail(ex.Message));
        }
    }

    [HttpPost("test")]
    [Authorize(Policy = "ServerManage")]
    public async Task<ActionResult<ApiResponse>> TestConnection([FromBody] TestServerConnectionDto dto)
    {
        try
        {
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(dto.ConnectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT @@VERSION";
            var version = (await cmd.ExecuteScalarAsync())?.ToString() ?? "Unknown";
            return Ok(ApiResponse.Ok(new { version }, "Connection successful"));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse.Fail($"Connection failed: {ex.Message}"));
        }
    }
}
