using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using ExcelImportSystem.Core.DTOs;
using ExcelImportSystem.Core.Entities;
using ExcelImportSystem.Core.Interfaces;
using ExcelImportSystem.Infrastructure.Data;

namespace ExcelImportSystem.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILdapService _ldapService;
    private readonly ICaptchaService _captchaService;
    private readonly ILoginAuditService _auditService;
    private readonly ILogger<AuthService> _logger;
    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes = 15;

    public AuthService(
        AppDbContext context,
        IConfiguration configuration,
        ILdapService ldapService,
        ICaptchaService captchaService,
        ILoginAuditService auditService,
        ILogger<AuthService> logger)
    {
        _context = context;
        _configuration = configuration;
        _ldapService = ldapService;
        _captchaService = captchaService;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<LoginResponseDto> LoginAsync(LoginDto dto)
    {
        // Validate CAPTCHA (required for every login)
        if (!_captchaService.Validate(dto.CaptchaToken ?? "", dto.CaptchaCode ?? ""))
        {
            _logger.LogWarning("Login failed for '{Username}': invalid captcha", dto.Username);
            await _auditService.LogAsync(dto.Username, false, "Invalid captcha");
            throw new UnauthorizedAccessException("Invalid captcha code");
        }

        var user = await _context.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role).ThenInclude(r => r.RolePermissions)
            .FirstOrDefaultAsync(u => u.Username == dto.Username && u.IsActive);

        // Check account lockout
        if (user != null && user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
        {
            var remaining = (int)(user.LockoutEnd.Value - DateTime.UtcNow).TotalMinutes;
            _logger.LogWarning("Login blocked for locked account '{Username}': {Minutes} min remaining", dto.Username, remaining);
            await _auditService.LogAsync(dto.Username, false, "Account locked");
            throw new UnauthorizedAccessException($"Account is locked. Try again in {remaining + 1} minutes.");
        }

        // Reset failed counter when lockout has expired, so user gets a fresh set of attempts
        if (user != null && user.LockoutEnd.HasValue && user.LockoutEnd.Value <= DateTime.UtcNow
            && user.FailedLoginCount >= MaxFailedAttempts)
        {
            user.FailedLoginCount = 0;
            user.LockoutEnd = null;
        }

        // Phase 1: Try local auth for Local users
        if (user != null && user.AuthType == "Local")
        {
            if (BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            {
                user.LastLoginAt = DateTime.UtcNow;
                user.FailedLoginCount = 0;
                user.LockoutEnd = null;
                await _context.SaveChangesAsync();
                _logger.LogInformation("User '{Username}' logged in successfully", dto.Username);
                await _auditService.LogAsync(dto.Username, true, null);
                return BuildLoginResponse(user);
            }

            // Failed attempt
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockoutEnd = DateTime.UtcNow.AddMinutes(LockoutMinutes);
                _logger.LogWarning("Account '{Username}' locked after {Count} failed attempts", dto.Username, user.FailedLoginCount);
            }
            await _context.SaveChangesAsync();
            _logger.LogWarning("Login failed for '{Username}': invalid password (attempt {Count}/{Max})", dto.Username, user.FailedLoginCount, MaxFailedAttempts);
            await _auditService.LogAsync(dto.Username, false, "Invalid password");
            throw new UnauthorizedAccessException("Invalid username or password");
        }

        // Phase 2: Try LDAP authentication
        var ldapResult = await _ldapService.AuthenticateAsync(dto.Username, dto.Password);

        if (ldapResult.Success)
        {
            if (user == null)
            {
                // Auto-create user on first LDAP login
                user = new User
                {
                    Username = dto.Username,
                    AuthType = "LDAP",
                    LdapDn = ldapResult.Dn,
                    DisplayName = ldapResult.DisplayName ?? dto.Username,
                    Email = ldapResult.Email ?? "",
                    PasswordHash = "",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                // Assign default Viewer role
                var viewerRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "Viewer");
                if (viewerRole != null)
                {
                    _context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = viewerRole.Id });
                    await _context.SaveChangesAsync();
                }
            }
            else if (user.AuthType == "LDAP")
            {
                // Update info from AD
                user.LdapDn = ldapResult.Dn;
                user.DisplayName = ldapResult.DisplayName ?? user.DisplayName;
                user.Email = ldapResult.Email ?? user.Email;
            }
            else
            {
                _logger.LogWarning("Login failed for '{Username}': AD account collides with local account", dto.Username);
                await _auditService.LogAsync(dto.Username, false, "AD collision with local account");
                throw new UnauthorizedAccessException(
                    "This account uses local authentication. Please use your local password.");
            }

            user.LastLoginAt = DateTime.UtcNow;
            user.FailedLoginCount = 0;
            user.LockoutEnd = null;
            await _context.SaveChangesAsync();

            // Reload with permissions
            user = await _context.Users
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role).ThenInclude(r => r.RolePermissions)
                .FirstAsync(u => u.Id == user.Id);

            _logger.LogInformation("User '{Username}' logged in via LDAP", dto.Username);
            await _auditService.LogAsync(dto.Username, true, null);
            return BuildLoginResponse(user);
        }

        // Failed LDAP auth
        if (user != null)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockoutEnd = DateTime.UtcNow.AddMinutes(LockoutMinutes);
                _logger.LogWarning("Account '{Username}' locked after {Count} failed LDAP attempts", dto.Username, user.FailedLoginCount);
            }
            await _context.SaveChangesAsync();
        }
        _logger.LogWarning("Login failed for '{Username}': invalid credentials", dto.Username);
        await _auditService.LogAsync(dto.Username, false, "Invalid credentials");
        throw new UnauthorizedAccessException("Invalid username or password");
    }

    public async Task<UserInfoDto> GetUserInfoAsync(int userId)
    {
        var user = await _context.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null) throw new KeyNotFoundException("User not found");

        return MapToDto(user);
    }

    public async Task<bool> ChangePasswordAsync(int userId, ChangePasswordDto dto)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) throw new KeyNotFoundException("User not found");

        if (user.AuthType == "LDAP")
            throw new InvalidOperationException("LDAP users cannot change password through this system.");

        if (!BCrypt.Net.BCrypt.Verify(dto.OldPassword, user.PasswordHash))
            throw new UnauthorizedAccessException("Current password is incorrect");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ResetPasswordAsync(int userId, string newPassword)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) throw new KeyNotFoundException("User not found");

        if (user.AuthType == "LDAP")
            throw new InvalidOperationException("Cannot reset password for LDAP users.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<PagedResult<UserInfoDto>> GetUsersAsync(int page, int pageSize, string? search = null)
    {
        var query = _context.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u => u.Username.Contains(search) || u.DisplayName.Contains(search));

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserInfoDto
            {
                Id = u.Id,
                Username = u.Username,
                DisplayName = u.DisplayName,
                Email = u.Email,
                IsActive = u.IsActive,
                Roles = u.UserRoles.Select(ur => ur.Role.Name).ToList(),
                AuthType = u.AuthType,
                LdapDn = u.LdapDn
            })
            .ToListAsync();

        return new PagedResult<UserInfoDto> { Total = total, Page = page, PageSize = pageSize, Items = items };
    }

    public async Task<UserInfoDto> CreateUserAsync(CreateUserDto dto)
    {
        if (await _context.Users.AnyAsync(u => u.Username == dto.Username))
            throw new InvalidOperationException("Username already exists");

        var authType = dto.AuthType ?? "Local";
        if (authType == "Local" && string.IsNullOrEmpty(dto.Password))
            throw new InvalidOperationException("Password is required for Local authentication users");

        var user = new User
        {
            Username = dto.Username,
            AuthType = authType,
            LdapDn = dto.LdapDn,
            PasswordHash = authType == "Local"
                ? BCrypt.Net.BCrypt.HashPassword(dto.Password!)
                : "",
            DisplayName = dto.DisplayName,
            Email = dto.Email ?? "",
            IsActive = true
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        if (dto.RoleIds.Count != 0)
        {
            foreach (var roleId in dto.RoleIds)
                _context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
            await _context.SaveChangesAsync();
        }

        return await GetUserInfoAsync(user.Id);
    }

    public async Task<UserInfoDto> UpdateUserAsync(int userId, UpdateUserDto dto)
    {
        var user = await _context.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null) throw new KeyNotFoundException("User not found");

        if (dto.DisplayName != null) user.DisplayName = dto.DisplayName;
        if (dto.Email != null) user.Email = dto.Email;
        if (dto.IsActive.HasValue) user.IsActive = dto.IsActive.Value;
        if (dto.AuthType != null) user.AuthType = dto.AuthType;
        if (dto.LdapDn != null) user.LdapDn = dto.LdapDn;

        if (dto.RoleIds != null)
        {
            _context.UserRoles.RemoveRange(user.UserRoles);
            foreach (var roleId in dto.RoleIds)
                _context.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
        }

        await _context.SaveChangesAsync();
        return await GetUserInfoAsync(userId);
    }

    public async Task<bool> DeleteUserAsync(int userId)
    {
        if (userId == 1) throw new InvalidOperationException("Cannot delete the default admin user");

        var user = await _context.Users.FindAsync(userId);
        if (user == null) throw new KeyNotFoundException("User not found");

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<UserInfoDto> LinkLdapAsync(int userId, LinkLdapDto dto)
    {
        var user = await _context.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null) throw new KeyNotFoundException("User not found");

        var (dn, displayName, email) = await _ldapService.LookupUserAsync(dto.Username);
        if (dn == null)
            throw new KeyNotFoundException($"AD user '{dto.Username}' not found");

        user.AuthType = "LDAP";
        user.LdapDn = dn;
        user.PasswordHash = "";
        user.DisplayName = displayName ?? user.DisplayName;
        user.Email = email ?? user.Email;

        await _context.SaveChangesAsync();
        return MapToDto(user);
    }

    public async Task<bool> UnlinkLdapAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) throw new KeyNotFoundException("User not found");

        user.AuthType = "Local";
        user.LdapDn = null;

        await _context.SaveChangesAsync();
        return true;
    }

    public Task<List<LdapSearchResultDto>> SearchLdapUsersAsync(string? search)
    {
        return _ldapService.SearchUsersAsync(search);
    }

    private LoginResponseDto BuildLoginResponse(User user)
    {
        var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission)
            .Distinct()
            .ToList();

        return new LoginResponseDto
        {
            Token = GenerateJwtToken(user, roles, permissions),
            DisplayName = user.DisplayName,
            Roles = roles,
            Permissions = permissions
        };
    }

    private static UserInfoDto MapToDto(User user)
    {
        return new UserInfoDto
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Email = user.Email,
            IsActive = user.IsActive,
            Roles = user.UserRoles.Select(ur => ur.Role.Name).ToList(),
            AuthType = user.AuthType,
            LdapDn = user.LdapDn
        };
    }

    private string GenerateJwtToken(User user, List<string> roles, List<string> permissions)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_configuration["Jwt:Key"] ?? "DefaultSuperSecretKeyForDevelopment123!"));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.GivenName, user.DisplayName),
        };

        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(permissions.Select(p => new Claim("Permission", p)));

        var expires = DateTime.UtcNow.AddHours(
            double.Parse(_configuration["Jwt:ExpireHours"] ?? "24"));

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"] ?? "ExcelImportSystem",
            audience: _configuration["Jwt:Audience"] ?? "ExcelImportSystem",
            claims: claims,
            expires: expires,
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
