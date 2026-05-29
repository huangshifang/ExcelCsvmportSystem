using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Microsoft.AspNetCore.Mvc;
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

builder.WebHost.UseUrls("http://+:5000");
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 210_000_000; // 200MB+ for large Excel uploads
});
builder.Host.UseSerilog();

// Increase multipart form size limit for large file uploads
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 210_000_000;
});

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
    options.AddPolicy("AuditView", policy =>
        policy.RequireClaim("Permission", "Audit.View"));
    options.AddPolicy("DatabaseManage", policy =>
        policy.RequireClaim("Permission", "Database.Manage"));
    options.AddPolicy("SystemManage", policy =>
        policy.RequireClaim("Permission", "System.Manage"));
    options.AddPolicy("ServerView", policy =>
        policy.RequireClaim("Permission", "Server.View"));
    options.AddPolicy("ServerManage", policy =>
        policy.RequireClaim("Permission", "Server.Manage"));
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            return new BadRequestObjectResult(
                ExcelImportSystem.Core.DTOs.ApiResponse<object>.Fail(
                    "Validation failed: " + string.Join("; ", errors), errors));
        };
    });
builder.Services.AddOpenApi();

// Rate limiter — protect login endpoint from brute force
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("Login", config =>
    {
        config.PermitLimit = 10;
        config.Window = TimeSpan.FromMinutes(1);
        config.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        config.QueueLimit = 0;
    });
});

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

        // Apply schema migrations for new columns/tables (safe to run multiple times)
        try
        {
            dbContext.Database.ExecuteSqlRaw(
                @"IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'AuthType' AND Object_ID = Object_ID('Users'))
                  BEGIN
                      ALTER TABLE Users ADD AuthType nvarchar(20) NOT NULL DEFAULT 'Local';
                      ALTER TABLE Users ADD LdapDn nvarchar(500) NULL;
                  END");

            dbContext.Database.ExecuteSqlRaw(
                @"IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'FailedLoginCount' AND Object_ID = Object_ID('Users'))
                  BEGIN
                      ALTER TABLE Users ADD FailedLoginCount int NOT NULL DEFAULT 0;
                      ALTER TABLE Users ADD LockoutEnd datetime2 NULL;
                  END");

            dbContext.Database.ExecuteSqlRaw(
                @"IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE Name = 'UserDatabaseAccesses')
                  BEGIN
                      CREATE TABLE UserDatabaseAccesses (
                          Id INT IDENTITY(1,1) PRIMARY KEY,
                          UserId INT NOT NULL,
                          DatabaseName NVARCHAR(200) NOT NULL,
                          GrantedBy NVARCHAR(200) NULL,
                          GrantedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                          CONSTRAINT FK_UserDatabaseAccess_Users FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE,
                          CONSTRAINT UQ_UserDatabase_UserId_DbName UNIQUE (UserId, DatabaseName)
                      );
                  END");

            // Add table-level access columns to UserDatabaseAccesses
            dbContext.Database.ExecuteSqlRaw(
                @"IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'SchemaName' AND Object_ID = Object_ID('UserDatabaseAccesses'))
                  BEGIN
                      ALTER TABLE UserDatabaseAccesses ADD SchemaName nvarchar(200) NULL;
                      ALTER TABLE UserDatabaseAccesses ADD TableName nvarchar(200) NULL;
                  END");

            // Replace old unique constraint with filtered unique indexes
            dbContext.Database.ExecuteSqlRaw(
                @"IF EXISTS (SELECT 1 FROM sys.indexes WHERE Name = 'UQ_UserDatabase_UserId_DbName' AND Object_ID = Object_ID('UserDatabaseAccesses'))
                  BEGIN
                      ALTER TABLE UserDatabaseAccesses DROP CONSTRAINT UQ_UserDatabase_UserId_DbName;
                  END;

                  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE Name = 'UQ_UserDatabaseAccess_Wildcard' AND Object_ID = Object_ID('UserDatabaseAccesses'))
                  BEGIN
                      CREATE UNIQUE NONCLUSTERED INDEX UQ_UserDatabaseAccess_Wildcard
                          ON UserDatabaseAccesses(UserId, DatabaseName)
                          WHERE SchemaName IS NULL AND TableName IS NULL;
                  END;

                  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE Name = 'UQ_UserDatabaseAccess_Table' AND Object_ID = Object_ID('UserDatabaseAccesses'))
                  BEGIN
                      CREATE UNIQUE NONCLUSTERED INDEX UQ_UserDatabaseAccess_Table
                          ON UserDatabaseAccesses(UserId, DatabaseName, SchemaName, TableName)
                          WHERE TableName IS NOT NULL;
                  END");

            // Ensure Database.Manage permission exists for Admin role (data migration)
            dbContext.Database.ExecuteSqlRaw(
                @"IF EXISTS (SELECT 1 FROM Roles WHERE Name = 'Admin')
                  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp
                      JOIN Roles r ON rp.RoleId = r.Id
                      WHERE r.Name = 'Admin' AND rp.Permission = 'Database.Manage')
                  BEGIN
                      DECLARE @adminId INT = (SELECT Id FROM Roles WHERE Name = 'Admin');
                      INSERT INTO RolePermissions (RoleId, Permission) VALUES (@adminId, 'Database.Manage');
                  END");

            // Ensure System.Manage permission exists for Admin role (data migration)
            dbContext.Database.ExecuteSqlRaw(
                @"IF EXISTS (SELECT 1 FROM Roles WHERE Name = 'Admin')
                  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp
                      JOIN Roles r ON rp.RoleId = r.Id
                      WHERE r.Name = 'Admin' AND rp.Permission = 'System.Manage')
                  BEGIN
                      DECLARE @adminId INT = (SELECT Id FROM Roles WHERE Name = 'Admin');
                      INSERT INTO RolePermissions (RoleId, Permission) VALUES (@adminId, 'System.Manage');
                  END");

            // Ensure Audit.View permission exists for Admin role (data migration)
            dbContext.Database.ExecuteSqlRaw(
                @"IF EXISTS (SELECT 1 FROM Roles WHERE Name = 'Admin')
                  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp
                      JOIN Roles r ON rp.RoleId = r.Id
                      WHERE r.Name = 'Admin' AND rp.Permission = 'Audit.View')
                  BEGIN
                      DECLARE @adminId INT = (SELECT Id FROM Roles WHERE Name = 'Admin');
                      INSERT INTO RolePermissions (RoleId, Permission) VALUES (@adminId, 'Audit.View');
                  END");

            // Ensure SystemSettings table exists
            dbContext.Database.ExecuteSqlRaw(
                @"IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE Name = 'SystemSettings')
                  BEGIN
                      CREATE TABLE SystemSettings (
                          Id INT IDENTITY(1,1) PRIMARY KEY,
                          [Key] NVARCHAR(200) NOT NULL,
                          [Value] NVARCHAR(MAX) NOT NULL,
                          CONSTRAINT UQ_SystemSettings_Key UNIQUE ([Key])
                      );
                  END");

            // Ensure LoginAuditLogs table exists
            dbContext.Database.ExecuteSqlRaw(
                @"IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE Name = 'LoginAuditLogs')
                  BEGIN
                      CREATE TABLE LoginAuditLogs (
                          Id INT IDENTITY(1,1) PRIMARY KEY,
                          Username NVARCHAR(200) NOT NULL,
                          IpAddress NVARCHAR(100) NULL,
                          UserAgent NVARCHAR(500) NULL,
                          Success BIT NOT NULL DEFAULT 0,
                          FailureReason NVARCHAR(500) NULL,
                          Timestamp DATETIME2 NOT NULL DEFAULT GETUTCDATE()
                      );
                      CREATE INDEX IX_LoginAuditLogs_Username ON LoginAuditLogs (Username);
                      CREATE INDEX IX_LoginAuditLogs_Timestamp ON LoginAuditLogs (Timestamp);
                  END");

            // Ensure SqlServerInstances table exists
            dbContext.Database.ExecuteSqlRaw(
                @"IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE Name = 'SqlServerInstances')
                  BEGIN
                      CREATE TABLE SqlServerInstances (
                          Id INT IDENTITY(1,1) PRIMARY KEY,
                          Name NVARCHAR(200) NOT NULL,
                          ConnectionString NVARCHAR(1000) NOT NULL,
                          Description NVARCHAR(500) NULL,
                          IsActive BIT NOT NULL DEFAULT 1,
                          CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                          CONSTRAINT UQ_SqlServerInstances_Name UNIQUE (Name)
                      );
                  END");

            // Add ServerId column to UserDatabaseAccesses
            dbContext.Database.ExecuteSqlRaw(
                @"IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'ServerId' AND Object_ID = Object_ID('UserDatabaseAccesses'))
                  BEGIN
                      ALTER TABLE UserDatabaseAccesses ADD ServerId INT NULL;
                  END");

            // Rebuild filtered unique indexes to include ServerId
            dbContext.Database.ExecuteSqlRaw(
                @"IF EXISTS (SELECT 1 FROM sys.indexes WHERE Name = 'UQ_UserDatabaseAccess_Wildcard' AND Object_ID = Object_ID('UserDatabaseAccesses'))
                  BEGIN DROP INDEX UQ_UserDatabaseAccess_Wildcard ON UserDatabaseAccesses; END;

                  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE Name = 'UQ_UserDatabaseAccess_Wildcard' AND Object_ID = Object_ID('UserDatabaseAccesses'))
                  BEGIN
                      CREATE UNIQUE NONCLUSTERED INDEX UQ_UserDatabaseAccess_Wildcard
                          ON UserDatabaseAccesses(UserId, ServerId, DatabaseName)
                          WHERE SchemaName IS NULL AND TableName IS NULL;
                  END;

                  IF EXISTS (SELECT 1 FROM sys.indexes WHERE Name = 'UQ_UserDatabaseAccess_Table' AND Object_ID = Object_ID('UserDatabaseAccesses'))
                  BEGIN DROP INDEX UQ_UserDatabaseAccess_Table ON UserDatabaseAccesses; END;

                  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE Name = 'UQ_UserDatabaseAccess_Table' AND Object_ID = Object_ID('UserDatabaseAccesses'))
                  BEGIN
                      CREATE UNIQUE NONCLUSTERED INDEX UQ_UserDatabaseAccess_Table
                          ON UserDatabaseAccesses(UserId, ServerId, DatabaseName, SchemaName, TableName)
                          WHERE TableName IS NOT NULL;
                  END");

            // Seed Server.View and Server.Manage for Admin
            dbContext.Database.ExecuteSqlRaw(
                @"IF EXISTS (SELECT 1 FROM Roles WHERE Name = 'Admin')
                  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp
                      JOIN Roles r ON rp.RoleId = r.Id
                      WHERE r.Name = 'Admin' AND rp.Permission = 'Server.View')
                  BEGIN
                      DECLARE @adminId INT = (SELECT Id FROM Roles WHERE Name = 'Admin');
                      INSERT INTO RolePermissions (RoleId, Permission) VALUES (@adminId, 'Server.View');
                      INSERT INTO RolePermissions (RoleId, Permission) VALUES (@adminId, 'Server.Manage');
                  END");

            // Add ServerId and ServerName columns to ImportLogs
            dbContext.Database.ExecuteSqlRaw(
                @"IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'ServerId' AND Object_ID = Object_ID('ImportLogs'))
                  BEGIN
                      ALTER TABLE ImportLogs ADD ServerId int NULL;
                      ALTER TABLE ImportLogs ADD ServerName nvarchar(200) NULL;
                  END");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Schema migration step failed (may be harmless if columns/tables already exist)");
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

app.UseRateLimiter();
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
            new RolePermission { RoleId = adminRole.Id, Permission = "Audit.View" },
            new RolePermission { RoleId = adminRole.Id, Permission = "Database.Manage" },
            new RolePermission { RoleId = adminRole.Id, Permission = "System.Manage" },
            new RolePermission { RoleId = adminRole!.Id, Permission = "Server.View" },
            new RolePermission { RoleId = adminRole.Id, Permission = "Server.Manage" },
            new RolePermission { RoleId = operatorRole!.Id, Permission = "Import.Execute" },
            new RolePermission { RoleId = operatorRole.Id, Permission = "Import.View" },
            new RolePermission { RoleId = operatorRole.Id, Permission = "Log.View" },
            new RolePermission { RoleId = viewerRole!.Id, Permission = "Import.View" }
        );
        db.SaveChanges();
    }

    // Seed admin user — create default admin on first run only
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

    // Ensure admin has Admin role
    if (!db.UserRoles.Any(ur => ur.UserId == admin.Id && ur.RoleId == adminRole!.Id))
    {
        db.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = adminRole!.Id });
        db.SaveChanges();
    }

    Log.Information("Seed data complete: admin(id={Id}) with {Role} role", admin.Id, adminRole?.Name ?? "?");
}
