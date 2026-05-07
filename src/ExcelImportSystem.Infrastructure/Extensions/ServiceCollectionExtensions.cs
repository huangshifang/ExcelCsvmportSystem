using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ExcelImportSystem.Core.Configurations;
using ExcelImportSystem.Core.Interfaces;
using ExcelImportSystem.Infrastructure.Data;
using ExcelImportSystem.Infrastructure.Services;

namespace ExcelImportSystem.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sqlOptions => sqlOptions.EnableRetryOnFailure(3)));

        services.Configure<LdapSettings>(configuration.GetSection(LdapSettings.Section));
        services.AddSingleton<ILdapService, LdapService>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ITableService, TableService>();
        services.AddScoped<IImportService, ImportService>();
        services.AddScoped<IImportLogService, ImportLogService>();

        return services;
    }
}
