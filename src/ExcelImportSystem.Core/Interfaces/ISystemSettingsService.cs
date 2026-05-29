namespace ExcelImportSystem.Core.Interfaces;

public interface ISystemSettingsService
{
    Task<Dictionary<string, string>> GetAllAsync();
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    Task SetBatchAsync(Dictionary<string, string> settings);
}
