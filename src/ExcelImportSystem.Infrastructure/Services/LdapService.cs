using System.DirectoryServices.Protocols;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ExcelImportSystem.Core.Configurations;
using ExcelImportSystem.Core.DTOs;
using ExcelImportSystem.Core.Interfaces;

namespace ExcelImportSystem.Infrastructure.Services;

public class LdapService : ILdapService
{
    private readonly LdapSettings _settings;
    private readonly ILogger<LdapService> _logger;

    public LdapService(IOptions<LdapSettings> settings, ILogger<LdapService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<(bool Success, string? Dn, string? DisplayName, string? Email)> AuthenticateAsync(
        string username, string password)
    {
        if (!_settings.Enabled)
            return (false, null, null, null);

        try
        {
            var lookup = await LookupUserAsync(username);
            if (lookup.Dn == null)
                return (false, null, null, null);

            using var connection = CreateConnection();
            connection.Bind(new NetworkCredential(lookup.Dn, password));
            return (true, lookup.Dn, lookup.DisplayName, lookup.Email);
        }
        catch (LdapException ex) when (ex.ErrorCode == 49)
        {
            _logger.LogWarning("LDAP bind failed for {Username}: invalid credentials", username);
            return (false, null, null, null);
        }
        catch (DirectoryOperationException ex)
        {
            _logger.LogError(ex, "LDAP operation error for {Username}", username);
            return (false, null, null, null);
        }
    }

    public Task<(string? Dn, string? DisplayName, string? Email)> LookupUserAsync(string username)
    {
        return Task.Run(() =>
        {
            try
            {
                using var connection = CreateConnection();

                if (!string.IsNullOrWhiteSpace(_settings.BindUserDn))
                {
                    connection.Bind(new NetworkCredential(_settings.BindUserDn, _settings.BindPassword));
                }

                var filter = string.Format(_settings.UserFilterTemplate, EscapeLdapFilter(username));
                var request = new SearchRequest(
                    _settings.BaseDn,
                    filter,
                    SearchScope.Subtree,
                    "distinguishedName", "displayName", "mail", "sAMAccountName");

                var response = (SearchResponse)connection.SendRequest(request);
                if (response.Entries.Count == 0)
                    return ((string?)null, null, null);

                var entry = response.Entries[0];
                return (
                    (string?)entry.DistinguishedName,
                    GetAttribute(entry, "displayName"),
                    GetAttribute(entry, "mail")
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LDAP lookup failed for {Username}", username);
                return ((string?)null, null, null);
            }
        });
    }

    public Task<List<LdapSearchResultDto>> SearchUsersAsync(string? search)
    {
        return Task.Run(() =>
        {
            var results = new List<LdapSearchResultDto>();

            if (!_settings.Enabled)
                return results;

            try
            {
                using var connection = CreateConnection();

                if (!string.IsNullOrWhiteSpace(_settings.BindUserDn))
                {
                    connection.Bind(new NetworkCredential(_settings.BindUserDn, _settings.BindPassword));
                }

                var pattern = string.IsNullOrWhiteSpace(search) ? "*" : $"*{EscapeLdapFilter(search)}*";
                var filter = string.Format(_settings.UserFilterTemplate, pattern);

                var request = new SearchRequest(
                    _settings.BaseDn,
                    filter,
                    SearchScope.Subtree,
                    "distinguishedName", "displayName", "mail", "sAMAccountName");

                var response = (SearchResponse)connection.SendRequest(request);

                foreach (SearchResultEntry entry in response.Entries)
                {
                    results.Add(new LdapSearchResultDto
                    {
                        Dn = entry.DistinguishedName,
                        SamAccountName = GetAttribute(entry, "sAMAccountName"),
                        DisplayName = GetAttribute(entry, "displayName"),
                        Email = GetAttribute(entry, "mail")
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LDAP search failed for pattern '{Search}'", search);
            }

            return results;
        });
    }

    private LdapConnection CreateConnection()
    {
        var identifier = new LdapDirectoryIdentifier(_settings.Server, _settings.Port);
        var connection = new LdapConnection(identifier)
        {
            AuthType = AuthType.Basic,
            SessionOptions =
            {
                ProtocolVersion = 3,
                SecureSocketLayer = _settings.UseSsl
            }
        };

        return connection;
    }

    private static string GetAttribute(SearchResultEntry entry, string name)
    {
        if (entry.Attributes.Contains(name))
            return entry.Attributes[name][0] as string ?? string.Empty;
        return string.Empty;
    }

    private static string EscapeLdapFilter(string value)
    {
        return value
            .Replace("\\", "\\5c")
            .Replace("*", "\\2a")
            .Replace("(", "\\28")
            .Replace(")", "\\29")
            .Replace("\0", "\\00");
    }
}
