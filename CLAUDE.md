# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

### Backend (.NET 10)
```bash
# Run API (http://localhost:5000)
cd src && dotnet run

# Build only
cd src && dotnet build
```

### Frontend (React 19 + Vite 8)
```bash
cd frontend && npm install && npm run dev   # http://localhost:5173
npm run build                                 # production build
npm run lint                                  # eslint
```

### Docker
```bash
cp .env.docker .env              # first time only
docker compose up -d             # API :5001, Frontend :8080 (HTTP) + :8443 (HTTPS)
```
Port 5000 often collides with Docker Desktop on Windows; the compose file maps host 5001 → container 5000. SQL Server runs externally (connection via `host.docker.internal`). Nginx serves HTTPS via self-signed/internal CA certificates mounted from `offline-deploy/certs/`.

No test project exists yet.

## Architecture

**Clean Architecture** with 3 .NET projects + React SPA:

- **ExcelImportSystem.Core** — Zero-dependency domain layer: entities (`User`, `Role`, `UserRole`, `RolePermission`, `ImportLog`, `UserDatabaseAccess`, `SystemSetting`, `LoginAuditLog`), DTOs, service interfaces (`IAuthService`, `IImportService`, `ITableService`, `IDatabaseAccessService`, `ISystemSettingsService`, `ILdapService`, `ICaptchaService`, `ILoginAuditService`), and config models (`LdapSettings`).
- **ExcelImportSystem.Infrastructure** — EF Core DbContext (`AppDbContext`), service implementations (`AuthService`, `ImportService`, `TableService`, `LdapService`, `CaptchaService`, `LoginAuditService`, `DatabaseAccessService`, `SystemSettingsService`, `LdapSettingsProvider`, `ImportLogService`), DI extension. Uses `EnsureCreated()` (NOT EF Migrations). Schema changes are applied via raw SQL in `Program.cs` with `IF NOT EXISTS` guards. New dependency: `CsvHelper` (CSV parsing).
- **ExcelImportSystem.API** — ASP.NET Core Web API, JWT auth, CORS, seed data, and raw SQL schema migrations all in `Program.cs`. Controllers handle HTTP concerns only; business logic lives in services.

DI registration is centralized in `ServiceCollectionExtensions.AddInfrastructure()`.

### Auth: JWT + LDAP Hybrid

Login flow: try local BCrypt → if fail, try LDAP bind → auto-create local user on first LDAP success → issue JWT.

JWT claims include `Permission` (multiple, e.g. `Import.Execute`), `ClaimTypes.Role`, `ClaimTypes.NameIdentifier`, `ClaimTypes.Name`. Authorization policies defined in `Program.cs` check specific `Permission` claims.

Three seed roles: **Admin** (all 8 permissions: Import.Execute, Import.View, User.Manage, Role.Manage, Log.View, Audit.View, Database.Manage, System.Manage), **Operator** (Import.Execute, Import.View, Log.View), **Viewer** (Import.View only).

Default login: `admin / admin123` (only set on first creation, never overwritten by seed data).

### Security

- **CAPTCHA**: SVG-based `CaptchaService` (zero native dependencies, Docker-compatible) generates 4-character CAPTCHA on `GET /api/auth/captcha`. **Mandatory** on every login — the `LoginDto` requires `CaptchaToken` + `CaptchaCode`, validated before any credential check. Frontend auto-refreshes CAPTCHA on login failure.
- **Rate limiting**: ASP.NET Core fixed-window rate limiter on `POST /api/auth/login` — 10 requests per minute per IP. Configured in `Program.cs` with `AddFixedWindowLimiter("Login", ...)` and applied via `[EnableRateLimiting("Login")]`.
- **Account lockout**: After 5 consecutive failed attempts, account locks for 15 minutes (`FailedLoginCount` + `LockoutEnd` fields on `User`). Tracks failures for both local and LDAP auth paths. Locked account returns remaining-minutes message. Successful login resets counters. Only applies to existing users (prevents username enumeration).
- **Brute force logging**: `AuthService` logs `LogWarning` on all failure types (invalid CAPTCHA, locked account, wrong password, AD collision) and `LogInformation` on success.
- **Password change**: Logged-in non-LDAP users can change password via `POST /api/auth/change-password` (old password required). Admin can reset any non-LDAP user's password via `POST /api/auth/users/{id}/reset-password`.
- **Admin password fix**: Seed data creates admin only when absent; `else` branch that overwrote password on every restart was removed.
- **Login audit**: Every login attempt (success or failure) is recorded to `LoginAuditLogs` table via `ILoginAuditService.LogAsync()`. Captures username, IP address, UserAgent, success/failure, and failure reason. Queryable via `GET /api/auth/login-logs` (paginated, filterable by username/status/date). UI page at `/login-logs` accessible only to Admin (`Audit.View` permission). Audit writes use `IServiceScopeFactory` to avoid blocking the login flow.
- **Audit.View permission**: New permission assigned only to Admin, gates the login audit page. Operator/Viewer cannot access login audit logs. Follows the standard permission pattern (policy + seed + migration SQL + frontend `hasPermission()`).

### Excel/CSV Import Flow

Two-step + async polling:

1. `POST /api/import/preview` — Reads Excel (.xlsx/.xls via EPPlus) or CSV (.csv/.tsv/.txt via CsvHelper). Auto-maps columns by name (case-insensitive) → returns sample data + table metadata. Validates user's table-level access permission before reading.

2. `POST /api/import/execute` — **Fire-and-forget**: copies file stream to memory, returns `taskId` immediately, then processes in background via `Task.Run`. Foreground request returns `{ taskId }` immediately (no timeout).

3. `GET /api/import/progress/{taskId}` — Poll for progress (`ImportProgressDto`: status, percent, totalRows, processedRows, message, errorCount, result). Status transitions: `pending` → `reading` → `importing` → `completed` / `failed`. Frontend polls every 500ms with `<Progress>` circle.

Execute uses raw `DbCommand` with `SqlParameter` for INSERTs — NOT EF Core — to support dynamic table targets. Batch processing (configurable `batchSize`, default 1000). Optional `DbTransaction` for atomicity.

`ImportService` uses `IServiceScopeFactory` (instead of direct DbContext/ITableService injection) so background tasks can create their own scopes after the request scope ends. Progress stored in `ConcurrentDictionary` (memory) keyed by taskId — no database persistence for progress state.

File size limit: 200MB (Kestrel `MaxRequestBodySize` + `FormOptions.MultipartBodyLengthLimit`). `TableService` queries `sys.databases`, `INFORMATION_SCHEMA`, and `sys.indexes/identity_columns` to discover tables/columns at runtime.

**Multi-worksheet Excel**: `GetFirstDataWorksheet()` skips empty placeholder sheets (only empty A1 cell) and auto-selects the first sheet with data. This handles files where data resides on sheet2+ (common in enterprise reporting).

**CSV parsing**: When `HasHeaderRow=false`, generates `Column1`..`ColumnN` column names and treats the first record as data. When `true`, reads the first row as headers via CsvHelper's `ReadHeader()`.

**Frontend defensive checks**: `excelColumns` is safely extracted (`preview?.excelColumns ?? []`) to prevent null-reference crashes. Empty columns show a red error alert rather than a misleading green "all mapped" message.

### Key Patterns

- **Safe SQL identifiers**: `SafeSqlName()` regex validates database/schema names before interpolation into dynamic SQL. Table names validated separately. Values always parameterized.
- **Role-based DB filtering**: `TableService.GetDatabasesAsync(userId)` returns all databases for Admins, but filters to `UserDatabaseAccesses` for non-admins. `TableService.GetTablesAsync(database, schema, userId)` applies table-level filtering: wildcard access grants all tables; otherwise only explicitly granted tables are visible.
- **Table-level access validation in ImportService**: `ValidateAccess()` checks `IDatabaseAccessService.HasAccessAsync()` before Preview and Execute — non-admin users cannot import into unapproved tables even if they guess the table name.
- **LDAP settings**: `LdapSettingsProvider` (singleton) loads from DB → falls back to `appsettings.json`. Uses `IServiceScopeFactory` to safely access scoped DbContext from singleton scope.
- **SystemSettings**: Key-value table (`SystemSetting`) used for runtime LDAP config (`Ldap:*` prefixed keys). `LdapSettingsProvider` caches in-memory; updates invalidate cache.
- **Fire-and-forget pattern**: Import execute copies file bytes, stores progress in `ConcurrentDictionary`, runs actual import on `Task.Run`. Controller returns taskId immediately. Progress polled via `GET /api/import/progress/{taskId}`.

### Frontend

- **Auth**: `AuthContext` stores token/permissions in localStorage, provides `hasPermission(perm)` and `hasRole(role)` helpers. Login requires CAPTCHA (fetched from `/api/auth/captcha`, rendered as SVG `<img>` — not Ant Design `Image`).
- **Components**: `ChangePasswordModal` (old/new/confirm password, logs out after change for non-LDAP users); `LoginAuditPage` (login attempt history with username/status/date filters); `AppLayout` dropdown shows "Change Password" only for non-LDAP users.
- **Login form**: Username field uses `autoComplete="off"` and password uses `autoComplete="new-password"` to prevent browser password managers from overriding user-typed credentials (which caused accidental admin lockout).
- **Routing**: React Router 7 with `ProtectedRoute` wrapper (checks token). `AppLayout` sidebar renders menu items conditionally based on `hasPermission()`.
- **i18n**: All user-visible text must use `t('key')` from `react-i18next`. Locales in `src/i18n/locales/{en,zh}.json`. Login page has no default credentials hint.
- **HTTP**: Axios interceptor auto-attaches `Authorization: Bearer <token>` and redirects to `/login` on 401.
- **API response format**: All endpoints return `ApiResponse<T>` with `{ success, message, data, errors }`.

### Adding a New Permission

1. `Program.cs` — add policy in `AddAuthorization` + add `RolePermission` seed to the role(s) that should have it + add data migration SQL
2. Frontend — use `hasPermission('Your.Permission')` for UI gating
