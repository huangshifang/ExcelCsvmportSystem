using Microsoft.EntityFrameworkCore;
using ExcelImportSystem.Core.Entities;
using ExcelImportSystem.Core.Interfaces;
using ExcelImportSystem.Infrastructure.Data;

namespace ExcelImportSystem.Infrastructure.Services;

public class SystemSettingsService : ISystemSettingsService
{
    private readonly AppDbContext _context;

    public SystemSettingsService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Dictionary<string, string>> GetAllAsync()
    {
        return await _context.SystemSettings
            .ToDictionaryAsync(s => s.Key, s => s.Value);
    }

    public async Task<string?> GetAsync(string key)
    {
        var setting = await _context.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == key);
        return setting?.Value;
    }

    public async Task SetAsync(string key, string value)
    {
        var setting = await _context.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == key);
        if (setting is null)
        {
            _context.SystemSettings.Add(new SystemSetting { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
        }
        await _context.SaveChangesAsync();
    }

    public async Task SetBatchAsync(Dictionary<string, string> settings)
    {
        var existingKeys = settings.Keys.ToHashSet();
        var existing = await _context.SystemSettings
            .Where(s => existingKeys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key);

        foreach (var (key, value) in settings)
        {
            if (existing.TryGetValue(key, out var setting))
            {
                setting.Value = value;
            }
            else
            {
                _context.SystemSettings.Add(new SystemSetting { Key = key, Value = value });
            }
        }

        await _context.SaveChangesAsync();
    }
}
