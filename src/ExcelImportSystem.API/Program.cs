using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using ExcelImportSystem.Core.Entities;
using ExcelImportSystem.Infrastructure.Data;
using ExcelImportSystem.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/excelimport-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.WebHost.UseUrls("http://localhost:5000");
builder.Host.UseSerilog();

// Add services
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ImportExecute", policy =>
        policy.RequireClaim("Permission", "Import.Execute"));
    options.AddPolicy("ImportView", policy =>
        policy.RequireClaim("Permission", "Import.View"));
    options.AddPolicy("UserManage", policy =>
        policy.RequireClaim("Permission", "User.Manage"));
    options.AddPolicy("RoleManage", policy =>
        policy.RequireClaim("Permission", "Role.Manage"));
    options.AddPolicy("LogView", policy =>
        policy.RequireClaim("Permission", "Log.View"));
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// CORS for frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Initialize database and seed data
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var dbName = dbContext.Database.GetDbConnection().Database;

    try
    {
        // First attempt: create tables if database doesn't exist
        var created = dbContext.Database.EnsureCreated();

        if (created)
        {
            Log.Information("Database '{Db}' created with all tables.", dbName);
        }
        else
        {
            // Database already existed — verify it has our application tables
            // (previous runs with Migrate() may have created an empty DB shell)
            var tablesExist = true;
            try
            {
                // Touch a known table — if it doesn't exist this throws
                dbContext.Users.Count();
            }
            catch
            {
                tablesExist = false;
            }

            if (!tablesExist)
            {
                Log.Warning("Database '{Db}' exists but is missing application tables. Recreating...", dbName);
                dbContext.Database.EnsureDeleted();
                dbContext.Database.EnsureCreated();
                Log.Information("Database '{Db}' recreated successfully.", dbName);
            }
            else
            {
                Log.Information("Database '{Db}' already exists and schema is complete.", dbName);
            }
        }

        // Apply schema migrations for new columns (safe to run multiple times)
        try
        {
            dbContext.Database.ExecuteSqlRaw(
                @"IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'AuthType' AND Object_ID = Object_ID('Users'))
                  BEGIN
                      ALTER TABLE Users ADD AuthType nvarchar(20) NOT NULL DEFAULT 'Local';
                      ALTER TABLE Users ADD LdapDn nvarchar(500) NULL;
                  END");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Schema migration step failed (may be harmless if columns already exist)");
        }

        SeedData(dbContext);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Database initialization failed. Login will not work until this is resolved.");
        Log.Information("Tip: Make sure SQL Server is running and the connection string in appsettings.json is correct.");
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

static void SeedData(AppDbContext db)
{
    // Seed roles — no explicit Id (Identity column auto-generates)
    var adminRole = db.Roles.FirstOrDefault(r => r.Name == "Admin");
    var operatorRole = db.Roles.FirstOrDefault(r => r.Name == "Operator");
    var viewerRole = db.Roles.FirstOrDefault(r => r.Name == "Viewer");

    if (adminRole == null)
    {
        Log.Information("Seeding roles...");
        db.Roles.Add(new Role { Name = "Admin", Description = "System administrator with full access" });
        db.Roles.Add(new Role { Name = "Operator", Description = "Can import data and view logs" });
        db.Roles.Add(new Role { Name = "Viewer", Description = "Can only view import logs" });
        db.SaveChanges();

        adminRole = db.Roles.First(r => r.Name == "Admin");
        operatorRole = db.Roles.First(r => r.Name == "Operator");
        viewerRole = db.Roles.First(r => r.Name == "Viewer");
    }

    // Seed permissions
    if (!db.RolePermissions.Any())
    {
        Log.Information("Seeding permissions...");
        db.RolePermissions.AddRange(
            new RolePermission { RoleId = adminRole!.Id, Permission = "Import.Execute" },
            new RolePermission { RoleId = adminRole.Id, Permission = "Import.View" },
            new RolePermission { RoleId = adminRole.Id, Permission = "User.Manage" },
            new RolePermission { RoleId = adminRole.Id, Permission = "Role.Manage" },
            new RolePermission { RoleId = adminRole.Id, Permission = "Log.View" },
            new RolePermission { RoleId = operatorRole!.Id, Permission = "Import.Execute" },
            new RolePermission { RoleId = operatorRole.Id, Permission = "Import.View" },
            new RolePermission { RoleId = operatorRole.Id, Permission = "Log.View" },
            new RolePermission { RoleId = viewerRole!.Id, Permission = "Import.View" }
        );
        db.SaveChanges();
    }

    // Seed admin user — ALWAYS reset the password hash to match "admin123"
    var admin = db.Users.FirstOrDefault(u => u.Username == "admin");
    if (admin == null)
    {
        Log.Information("Creating admin user...");
        admin = new User
        {
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
            DisplayName = "System Admin",
            Email = "admin@system.local",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(admin);
        db.SaveChanges();
    }
    else
    {
        // Always re-hash the password to fix stale hashes from earlier versions
        admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123");
        db.SaveChanges();
    }

    // Ensure admin has Admin role
    if (!db.UserRoles.Any(ur => ur.UserId == admin.Id && ur.RoleId == adminRole!.Id))
    {
        db.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = adminRole!.Id });
        db.SaveChanges();
    }

    Log.Information("Seed data complete: admin(id={Id}) with {Role} role", admin.Id, adminRole?.Name ?? "?");
}
