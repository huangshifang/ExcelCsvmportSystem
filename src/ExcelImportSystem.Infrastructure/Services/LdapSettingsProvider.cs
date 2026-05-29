using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ExcelImportSystem.Core.Configurations;
using ExcelImportSystem.Core.Interfaces;

namespace ExcelImportSystem.Infrastructure.Services;

/// <summary>
/// Provides runtime-reloadable LDAP settings backed by database.
/// Falls back to appsettings.json defaults on first load.
/// </summary>
public class LdapSettingsProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<LdapSettings> _fileDefaults;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private LdapSettings _current = null!;
    private bool _loaded;

    public LdapSettingsProvider(IServiceScopeFactory scopeFactory, IOptionsMonitor<LdapSettings> fileDefaults)
    {
        _scopeFactory = scopeFactory;
        _fileDefaults = fileDefaults;
    }

    public async Task<LdapSettings> GetSettingsAsync()
    {
        await EnsureLoadedAsync();
        return Clone(_current);
    }

    public async Task UpdateSettingsAsync(LdapSettings settings)
    {
        await _lock.WaitAsync();
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<ISystemSettingsService>();

            var dict = new Dictionary<string, string>
            {
                ["Ldap:Enabled"] = settings.Enabled.ToString().ToLowerInvariant(),
                ["Ldap:Server"] = settings.Server,
                ["Ldap:Port"] = settings.Port.ToString(),
                ["Ldap:UseSsl"] = settings.UseSsl.ToString().ToLowerInvariant(),
                ["Ldap:Domain"] = settings.Domain,
                ["Ldap:BaseDn"] = settings.BaseDn,
                ["Ldap:UserFilterTemplate"] = settings.UserFilterTemplate,
                ["Ldap:BindUserDn"] = settings.BindUserDn,
            };

            if (!string.IsNullOrEmpty(settings.BindPassword))
            {
                dict["Ldap:BindPassword"] = settings.BindPassword;
            }

            await service.SetBatchAsync(dict);
            _current = settings;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        await _lock.WaitAsync();
        try
        {
            if (_loaded) return;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<ISystemSettingsService>();
            var dbSettings = await service.GetAllAsync();
            var fileSettings = _fileDefaults.CurrentValue;

            _current = new LdapSettings
            {
                Enabled = ParseBool(dbSettings, "Ldap:Enabled", fileSettings.Enabled),
                Server = ParseString(dbSettings, "Ldap:Server", fileSettings.Server),
                Port = ParseInt(dbSettings, "Ldap:Port", fileSettings.Port),
                UseSsl = ParseBool(dbSettings, "Ldap:UseSsl", fileSettings.UseSsl),
                Domain = ParseString(dbSettings, "Ldap:Domain", fileSettings.Domain),
                BaseDn = ParseString(dbSettings, "Ldap:BaseDn", fileSettings.BaseDn),
                UserFilterTemplate = ParseString(dbSettings, "Ldap:UserFilterTemplate", fileSettings.UserFilterTemplate),
                BindUserDn = ParseString(dbSettings, "Ldap:BindUserDn", fileSettings.BindUserDn),
                BindPassword = ParseString(dbSettings, "Ldap:BindPassword", fileSettings.BindPassword),
            };

            _loaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string ParseString(Dictionary<string, string> db, string key, string fallback)
        => db.TryGetValue(key, out var val) ? val : fallback;

    private static int ParseInt(Dictionary<string, string> db, string key, int fallback)
        => int.TryParse(db.GetValueOrDefault(key), out var val) ? val : fallback;

    private static bool ParseBool(Dictionary<string, string> db, string key, bool fallback)
        => bool.TryParse(db.GetValueOrDefault(key), out var val) ? val : fallback;

    private static LdapSettings Clone(LdapSettings src) => new()
    {
        Enabled = src.Enabled,
        Server = src.Server,
        Port = src.Port,
        UseSsl = src.UseSsl,
        Domain = src.Domain,
        BaseDn = src.BaseDn,
        UserFilterTemplate = src.UserFilterTemplate,
        BindUserDn = src.BindUserDn,
        BindPassword = src.BindPassword,
    };
}
