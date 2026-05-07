namespace ExcelImportSystem.Core.Configurations;

public class LdapSettings
{
    public const string Section = "Ldap";
    public bool Enabled { get; set; }
    public string Server { get; set; } = string.Empty;
    public int Port { get; set; } = 389;
    public bool UseSsl { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string BaseDn { get; set; } = string.Empty;
    public string UserFilterTemplate { get; set; } = "(sAMAccountName={0})";
    public string BindUserDn { get; set; } = string.Empty;
    public string BindPassword { get; set; } = string.Empty;
}
