# Excel 导入系统 — 开发文档

## 项目概述

本系统是一个完整的企业级 Web 应用，支持通过 Web 界面将 Excel 数据导入到 SQL Server 数据库，具备基于角色的权限控制（RBAC）和 LDAP/Active Directory 集成认证。

---

## 技术栈

| 层级 | 技术 |
|------|------|
| **前端** | React 19 + TypeScript + Vite 8 + Ant Design 6 + React Router 7 + i18next |
| **后端** | .NET 10.0 (ASP.NET Core Web API) |
| **ORM** | Entity Framework Core 10 (SQL Server) |
| **认证** | JWT Bearer + BCrypt + LDAP (System.DirectoryServices.Protocols) |
| **Excel/CSV** | EPPlus 7 + CsvHelper 33 |
| **日志** | Serilog (Console + Rolling File) |
| **数据库** | SQL Server |
| **HTTP 客户端** | Axios (前端) |

---

## 架构设计

### 后端：Clean Architecture 三层分离

```
ExcelImportSystem.sln
├── ExcelImportSystem.Core         # 领域层：实体、DTO、接口
│   ├── Entities/                  # User, Role, UserRole, RolePermission, ImportLog, UserDatabaseAccess, SystemSetting, LoginAuditLog, SqlServerInstance
│   ├── DTOs/                      # 请求/响应 DTO
│   ├── Interfaces/                # 服务接口 (IAuthService, IImportService, ITableService, IDatabaseAccessService, ISystemSettingsService, IConnectionFactory 等)
│   └── Configurations/            # 配置模型 (LdapSettings)
│
├── ExcelImportSystem.Infrastructure  # 基础设施层：实现
│   ├── Data/                      # AppDbContext + EF Fluent API 配置
│   ├── Services/                  # 服务实现 (AuthService, LdapService, ImportService, TableService, ConnectionFactory, DatabaseAccessService, SystemSettingsService, LdapSettingsProvider 等)
│   └── Extensions/               # DI 注册扩展
│
└── ExcelImportSystem.API          # 表现层：ASP.NET Core Web API
    ├── Controllers/               # REST API 控制器
    ├── Program.cs                 # 启动配置、JWT、CORS、种子数据
    └── appsettings.json           # 配置文件
```

**设计原则**：
- Core 层零依赖（不引用任何外部包），保证领域纯净
- Infrastructure 层实现 Core 的接口，依赖 EF Core、BCrypt 等外部库
- API 层只处理 HTTP 请求/响应，业务逻辑全在 Service 中

### 前端：组件化 SPA

```
frontend/src/
├── api/               # API 调用封装 (Axios + JWT 拦截器)
├── components/        # 共享组件 (AppLayout 布局, ChangePasswordModal)
├── context/           # React Context (AuthContext, LocaleContext)
├── i18n/              # 国际化 (中文/英文)
├── pages/             # 页面组件 (Dashboard, Import, ImportLogs, Users, Servers, System, Login, LoginAudit)
└── types/             # TypeScript 类型定义
```

---

## 核心功能实现

### 1. 认证与授权

**登录流程（CAPTCHA + 双重认证 + 安全防护）**：

```
登录请求
  ├─ ① CAPTCHA 验证（必填，每次都验证）
  │   └─ 失败 → 返回 401 "Invalid captcha code"
  ├─ ② 频率限制（固定窗口 10 次/分钟）
  │   └─ 超限 → 返回 503（ASP.NET Core Rate Limiter）
  ├─ ③ 账号锁定检查（FailedLoginCount >= 5 → 锁定 15 分钟）
  │   └─ 锁定中 → 返回 401 "Account is locked. Try again in X minutes."
  ├─ ④ 查找本地用户
  │   ├─ AuthType = "Local" → BCrypt 密码验证
  │   │   ├─ 成功 → 重置计数器 → 生成 JWT
  │   │   └─ 失败 → FailedLoginCount++ → 达到 5 次则锁定 15 分钟 → 返回 401
  │   └─ 本地失败 → 尝试 LDAP 绑定
  │       ├─ LDAP 绑定成功 → 查找或创建本地用户记录 → 分配 Viewer 角色 → 生成 JWT
  │       ├─ LDAP 绑定失败 + 用户存在 → FailedLoginCount++ → 达到 5 次则锁定 → 返回 401
  │       └─ LDAP 绑定失败 + 用户不存在 → 返回 401（不泄露用户存在性）
  └─ ⑤ 所有失败均记录 LogWarning 日志，成功记录 LogInformation
```

**CAPTCHA 验证码**（`CaptchaService.cs`）：

- **纯 SVG 生成**：零原生依赖（无 SkiaSharp/System.Drawing），跨平台 Docker 兼容
- 4 位随机字符（排除易混淆字符：0/O/I/L/1 等），随机颜色、旋转角度、干扰线、噪点
- 内存存储，5 分钟过期，并发字典 + 自动清理
- 前端使用原生 `<img>` 标签渲染 `data:image/svg+xml;base64,...`（不是 Ant Design `Image`）
- 登录失败时自动刷新验证码
- 接口 `ICaptchaService`：`Generate()` 返回 (Token, Base64Image)，`Validate(token, code)` 返回 bool

**账号锁定机制**（`User` 实体新增字段）：

| 字段 | 类型 | 说明 |
|------|------|------|
| `FailedLoginCount` | int | 连续失败次数（默认 0） |
| `LockoutEnd` | DateTime? | 锁定到期时间（UTC） |

- 阈值：5 次连续失败 → 锁定 15 分钟
- 适用范围：仅针对已存在的用户（不存在的用户不计数，防用户名枚举）
- 触发路径：本地密码错误、LDAP 密码错误均计数
- 解锁：锁定到期自动解除，或成功登录后立即清零
- SQL 迁移：`Program.cs` 中 `IF NOT EXISTS` 检查列是否存在，不存在则 `ALTER TABLE ADD`

**速率限制**（ASP.NET Core Rate Limiter）：

- 固定窗口算法，`Login` 策略：10 次请求 / 1 分钟
- `POST /api/auth/login` 应用 `[EnableRateLimiting("Login")]`
- 超限返回 HTTP 503，由框架自动处理
- 配置在 `Program.cs` 中 `AddRateLimiter` 方法

**JWT Claims 设计**：
- `ClaimTypes.NameIdentifier` — 用户 ID
- `ClaimTypes.Name` — 用户名
- `ClaimTypes.GivenName` — 显示名称
- `ClaimTypes.Role` — 角色（可多个，如 Admin）
- `"Permission"` — 权限（去重后的权限列表，如 Import.Execute）

**授权策略**（在 `Program.cs` 中定义）：

| 策略名 | 所需 Claim | 用于 |
|---------|-----------|------|
| `ImportExecute` | `Permission: Import.Execute` | 导入数据 |
| `ImportView` | `Permission: Import.View` | 查看导入 |
| `UserManage` | `Permission: User.Manage` | 用户管理 |
| `RoleManage` | `Permission: Role.Manage` | 角色管理 |
| `LogView` | `Permission: Log.View` | 查看导入日志 |
| `AuditView` | `Permission: Audit.View` | 查看登录审计日志 |
| `DatabaseManage` | `Permission: Database.Manage` | 管理用户数据库权限 |
| `SystemManage` | `Permission: System.Manage` | 系统设置 |
| `ServerView` | `Permission: Server.View` | 查看服务器实例 |
| `ServerManage` | `Permission: Server.Manage` | 管理服务器实例 |

**三角色体系**：

| 角色 | 权限 |
|------|------|
| **Admin** | Import.Execute, Import.View, User.Manage, Role.Manage, Log.View, Audit.View, Database.Manage, System.Manage, Server.View, Server.Manage |
| **Operator** | Import.Execute, Import.View, Log.View |
| **Viewer** | Import.View |

### 2. 密码修改与重置

**用户自主修改密码**：

- 端点：`POST /api/auth/change-password`（需登录，`[Authorize]`）
- 前端：`ChangePasswordModal` 组件（仅非 LDAP 用户可见，菜单项由 `AppLayout` 中 `user?.authType !== 'LDAP'` 控制）
- 流程：输入旧密码 → 验证旧密码正确 → 输入新密码（最少 6 位 + 确认）→ BCrypt 哈希后保存
- LDAP 用户不可修改密码（`AuthService` 抛出 `InvalidOperationException`）

**管理员重置用户密码**：

- 端点：`POST /api/auth/users/{id}/reset-password`（需 `UserManage` 权限）
- 前端：用户管理页面操作栏的「Reset Password」按钮（仅非 LDAP 用户可见）
- 流程：输入新密码（最少 6 位 + 确认）→ 直接覆盖密码哈希
- LDAP 用户不可重置密码（`AuthService` 抛出 `InvalidOperationException`）

### 3. 登录审计日志

**概述**：

每次登录尝试（成功或失败）自动记录到 `LoginAuditLogs` 表，用于安全审计和异常检测。

**实体 `LoginAuditLog`**：

| 字段 | 类型 | 说明 |
|------|------|------|
| `Id` | int | 主键（自增） |
| `Username` | string | 尝试登录的用户名 |
| `IpAddress` | string? | 客户端 IP 地址 |
| `UserAgent` | string? | 浏览器 User-Agent |
| `Success` | bool | 是否登录成功 |
| `FailureReason` | string? | 失败原因（Invalid captcha / Invalid password / Account locked / AD collision / Invalid credentials） |
| `Timestamp` | DateTime | 登录时间（UTC） |

**服务 `LoginAuditService`**：

- 使用 `IServiceScopeFactory` 创建独立的 DbContext 作用域，确保审计写入不阻塞登录流程
- `LogAsync(username, success, failureReason)` — 从 `IHttpContextAccessor` 自动获取客户端 IP 和 UserAgent
- `GetLogsAsync(page, pageSize, username?, success?, from?, to?)` — 分页查询，支持按用户名、状态、日期范围过滤

**API 端点**：

- `GET /api/auth/login-logs?page=&pageSize=&username=&success=&from=&to=` — 需 `AuditView` 权限
- 权限控制：仅 Admin 拥有 `Audit.View` 权限，Operator/Viewer 不可访问

**前端页面** `LoginAuditPage`：

- 路由 `/login-logs`，侧边栏「登录审计」菜单项（仅 Admin 可见）
- 功能：按用户名搜索、按状态（成功/失败）筛选、按日期范围过滤、分页浏览
- 列表显示：ID、用户名、状态标签、失败原因、IP 地址、User-Agent、时间

**SQL 迁移**：

```sql
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE Name = 'LoginAuditLogs')
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
END
```

### 4. Excel / CSV 数据导入

**支持的格式**：

| 格式 | 扩展名 | 解析库 |
|------|--------|--------|
| Excel 2007+ | `.xlsx` | EPPlus 7 |
| Excel 97-2003 | `.xls` | EPPlus 7 |
| CSV / TSV / TXT | `.csv`, `.tsv`, `.txt` | CsvHelper 33 |

文件类型通过扩展名自动判断（`IsCsvFile()` / `IsExcelFile()` 辅助方法）。

**两步流程 + 异步轮询**：

1. **Preview（预览）** — `POST /api/import/preview`
   - 上传文件 + 选择目标服务器（可选，null=本地）→ 选择数据库 → 选择表
   - 根据扩展名选用 Excel 或 CSV 解析器 → 提取列名 + 最多 5 行示例数据
   - 查询 SQL Server 目标表的列元数据（类型、可空、主键、自增）— 通过 `IConnectionFactory` 连接到正确的服务器
   - 按名称（不区分大小写）自动映射列到数据库列
   - **权限验证**：调用 `ValidateAccess()` 检查用户是否有权访问目标表（含 ServerId）
   - 返回：Excel/CSV 列、示例数据、表列、自动映射、总行数
   - 超时：300 秒（大文件预览可能较慢）

2. **Execute（执行）** — `POST /api/import/execute`
   - **Fire-and-forget 模式**：请求线程立即将文件流拷贝到内存（`MemoryStream → byte[]`），然后通过 `Task.Run` 在新线程执行实际导入
   - 立即返回 `{ taskId }`（GUID），不等待导入完成
   - 进度通过 `static ConcurrentDictionary<string, ImportProgressDto>` 在内存中跟踪
   - `ImportRequestDto.ServerId` 指定目标服务器，通过 `IConnectionFactory.CreateConnectionAsync(serverId)` 创建连接

3. **Progress（轮询进度）** — `GET /api/import/progress/{taskId}`
   - 返回 `ImportProgressDto`：`{ taskId, status, percent, totalRows, processedRows, message, errorCount, result }`
   - 状态流转：`pending` → `reading` → `importing` → `completed` / `failed`
   - 前端每 500ms 轮询一次，展示圆形进度条

**CSV 解析**（CsvHelper）：
- 自动检测分隔符（`,` / `\t`）
- 支持带引号的字段、标题行、多种编码
- 配置 `CsvConfiguration`：`BadDataFound = null`（静默跳过坏行），`Mode = CsvMode.RFC4180`

**关键实现细节**（`ImportService.cs`）：
- `IServiceScopeFactory` 替代直接的 `AppDbContext` 注入（因为 fire-and-forget 任务超出请求作用域生命周期）
- 自增列自动跳过（让数据库生成值）
- 参数化查询防止 SQL 注入
- 事务支持可配置（`UseTransaction` 参数）
- 批次处理避免大文件内存溢出（`batchSize` 默认 1000，前端可配置 100-10000）
- 大文件支持：Kestrel `MaxRequestBodySize` = 210MB，`FormOptions.MultipartBodyLengthLimit` = 210MB
- 错误行收集到 `Errors` 列表，完整记录到 `ImportLog` 中
- 接口变更：`PreviewAsync(int userId, ...)`、`ExecuteAsync(int userId, ...)` 均需 userId 用于权限验证

### 5. 多 SQL Server 实例支持

**概述**：

系统支持将数据导入到多个不同的 SQL Server 实例，不限于本地数据库。通过 `SqlServerInstance` 实体注册远程服务器，`IConnectionFactory` 管理连接，所有现有功能（表发现、导入、权限控制）均支持跨服务器操作。

**核心组件**：

```
前端 ServerSelector → API ServerController → IConnectionFactory (Singleton)
                                                   ├─ 本地 (serverId=null): AppDbContext 连接字符串
                                                   └─ 远程 (serverId=N): 缓存/数据库中的连接字符串
                                                           ↓
                                              SqlConnection → sys.databases 发现
                                                           ↓
                                              TableService / ImportService 使用
```

**实体 `SqlServerInstance`**：

| 字段 | 类型 | 说明 |
|------|------|------|
| `Id` | int | 主键（自增） |
| `Name` | string | 服务器名称（唯一） |
| `ConnectionString` | string | SQL Server 连接字符串（加密存储，API 不返回） |
| `Description` | string? | 描述信息 |
| `IsActive` | bool | 是否启用（禁用后不参与数据库发现） |
| `CreatedAt` | DateTime | 创建时间（UTC） |

**`IConnectionFactory` 接口**：

- `CreateConnectionAsync(int? serverId)` — 创建到指定服务器的 `SqlConnection`（null = 本地）
- `GetServersAsync()` — 获取所有服务器列表（不含 ConnectionString）
- `GetServerAsync(int id)` — 获取单个服务器信息
- `ResolveServerIdAsync(string databaseName)` — 根据数据库名反查所属服务器
- `InvalidateCache()` — 清空所有缓存（服务器 CRUD 后调用）

**`ConnectionFactory` 实现**（Singleton）：

- 使用 `IServiceScopeFactory` 在 singleton 作用域中安全访问 scoped 的 `AppDbContext`
- `ConcurrentDictionary<int, string>` 缓存连接字符串（按 ServerId）
- `ConcurrentDictionary<string, int?>` 缓存数据库→服务器映射
- `EnsureCacheAsync()` 懒加载：首次调用时遍历所有活跃服务器，对每个执行 `SELECT name FROM sys.databases`
- 远程服务器不可达时记录 `LogWarning`，不阻塞本地数据库访问（优雅降级）

**ServerId 在各层的传播**：

| 层级 | 传播方式 |
|------|----------|
| `ImportRequestDto` | `int? ServerId` 字段 |
| `DatabaseInfoDto` | `int? ServerId` + `string? ServerName` |
| `UserTableAccessDto` | `int? ServerId` |
| `ITableService` | `GetTablesAsync(database, schema?, userId?, serverId?)` |
| `IDatabaseAccessService` | `HasAccessAsync(userId, db, schema?, table?, serverId?)` |
| 前端 FormData | `formData.append('serverId', String(selectedServerId))` |

**API 端点**（`ServerController`）：

| 方法 | 端点 | 权限 | 说明 |
|------|------|------|------|
| GET | `/api/server` | ServerView | 列出所有服务器（不含连接字符串） |
| GET | `/api/server/{id}` | ServerView | 获取单个服务器 |
| POST | `/api/server` | ServerManage | 创建服务器 |
| PUT | `/api/server/{id}` | ServerManage | 更新服务器 |
| DELETE | `/api/server/{id}` | ServerManage | 删除服务器（检查关联权限） |
| POST | `/api/server/test` | ServerManage | 测试连接字符串 |

**向后兼容性**：

- `ServerId = null` 在所有地方表示本地/默认服务器
- 未配置任何远程服务器时，系统行为与单服务器版本完全一致
- 已有的 `UserDatabaseAccess` 记录 `ServerId` 为 NULL，继续正常工作

**Docker 中的远程服务器连接**：

- 容器内使用 `host.docker.internal` 可连接到宿主机上的 SQL Server
- 无需额外端口映射，通过宿主机网络访问远程 SQL Server

### 6. LDAP/AD 集成

**技术选型**：`System.DirectoryServices.Protocols`（S.DS.P）
- 内置于 .NET，无需额外 NuGet 包（.NET 5+ 内置）
- 跨平台支持（Windows/Linux/macOS）
- 底层 LDAP 协议访问，灵活可控

**核心流程**：
1. `LdapConnection` 创建连接（支持 SSL）
2. 使用服务账号绑定 → `SearchRequest` 查找用户 DN
3. 使用用户 DN + 密码进行 `Bind` 验证
4. 验证成功 → 返回 `(DN, DisplayName, Email)`

**安全措施**：
- LDAP Filter 转义（防 LDAP 注入）
- 连接用完即释放（`using` 模式）
- 服务账号密码存储在 `appsettings.json`（生产环境应使用 Secret Manager 或环境变量）

**数据库迁移处理**：
- 使用 `EnsureCreated()` 的项目无法使用 EF Migration
- 采用 Raw SQL 条件迁移：检查列或表是否存在，不存在则创建
- 迁移脚本示例（`Program.cs`）：
  - `AuthType` / `LdapDn` 列（LDAP 集成）
  - `FailedLoginCount` / `LockoutEnd` 列（账号锁定）
  - `UserDatabaseAccesses` 表（数据库权限）
  - `SystemSettings` 表（运行时配置）
  - `LoginAuditLogs` 表（登录审计）
  - `Server.View` / `Server.Manage` / `Database.Manage` / `System.Manage` / `Audit.View` 权限数据迁移

**Admin 密码种子数据修复**：
- 原有 bug：每次重启都会将 admin 密码重置为 `admin123`
- 修复后：只在用户不存在时创建（`if (admin == null)` 块），已存在的 admin 密码不受影响

### 7. 仪表板统计

- `GET /api/dashboard/stats` — 聚合 `ImportLogs` 表
- **Admin** 角色 → 汇总所有用户的导入数据
- **普通用户** → 只汇总自己的导入数据
- Controller 层检查 `ClaimTypes.Role` 决定是否传递 `userId` 过滤条件

### 8. 表级数据库权限管理

**实体 `UserDatabaseAccess`（v1.1 升级为表级粒度）**：

```
UserDatabaseAccess (Id, UserId, DatabaseName, SchemaName?, TableName?, GrantedBy, GrantedAt)
  └─ FK → User (Cascade Delete)
  └─ Filtered Unique Index: (UserId, DatabaseName) WHERE SchemaName IS NULL AND TableName IS NULL  (wildcard)
  └─ Filtered Unique Index: (UserId, DatabaseName, SchemaName, TableName) WHERE TableName IS NOT NULL  (specific table)
```

**权限粒度**：
- `TableName IS NULL` → wildcard 授权：允许访问该数据库的所有表（向后兼容 v1.0 的数据）
- `TableName IS NOT NULL` → 精确授权：仅允许访问指定的表

**核心接口 `IDatabaseAccessService`**：
- `GetUserDatabasesAsync(userId)` — 获取用户有权限访问的数据库列表（去重）
- `GetUserTableAccessesAsync(userId)` — 获取用户所有表级访问记录
- `SetUserTableAccessesAsync(userId, accesses, grantedBy)` — 批量替换用户的表级权限（全量替换模式）
- `HasAccessAsync(userId, databaseName, schemaName?, tableName?)` — 检查用户是否有权访问指定表

**权限过滤流程**：

```
GET /api/table/databases (带 JWT userId)
  → TableService.GetDatabasesAsync(userId)
    → 查询用户角色 → 是 Admin 则返回全部数据库
    → 非 Admin → 查询 UserDatabaseAccesses 表（DISTINCT DatabaseName）
    → 返回用户有权访问的数据库列表

GET /api/table?database=X (带 JWT userId)
  → TableService.GetTablesAsync(database, userId)
    → 查询 INFORMATION_SCHEMA 获取该库全部表
    → 非 Admin 用户：查询 UserTableAccesses
      → 存在 wildcard 授权 → 返回全部表
      → 无 wildcard → 过滤到仅授权的表

POST /api/import/preview / execute
  → ImportService.ExecuteAsync() / PreviewAsync()
    → 检查用户角色 + 表级权限 (HasAccessAsync)
    → 无权限 → 抛出 UnauthorizedAccessException (403)
```

**前端管理员 UI**（`UsersPage`）：
- 两级树形选择器：展开数据库 → 显示该库所有表
- 支持两种授权模式切换：
  - 「选择所有表」→ wildcard 授权（整库访问）
  - 逐表勾选 → 精确授权（仅选中的表）
- 通过 `GET /api/databaseaccess/tables/{database}` 懒加载表列表

### 9. 系统设置（运行时 LDAP 配置）

**核心设计**：

```
appsettings.json (初始默认) → SystemSettings 表 (持久化) → LdapSettingsProvider (内存缓存) → LdapService
                                       ↑                          ↑
                                  通过 API 更新              配置即时生效
```

**`LdapSettingsProvider`**（Singleton）：
- 首次获取时从数据库加载（无则回退到 `appsettings.json`）
- `GetSettingsAsync()` — 获取当前设置副本
- `UpdateSettingsAsync()` — 更新数据库并刷新内存缓存
- 使用 `IServiceScopeFactory` 在 singleton 生命周期中安全访问 scoped 的 `DbContext`

**`SystemSettingsService`**（Scoped）：
- 基于 `SystemSettings` 键值对表（Key, Value）
- 支持单键读写和批量写入
- LDAP 配置以 `Ldap:*` 为前缀存储


### 10. 前端路由与权限控制

```
/login          → 公开（已登录则重定向到 /）
/               → 受保护（仪表板）
/import         → 受保护（导入页面）
/import-logs    → 受保护（导入日志）
/login-logs     → 受保护 + AuditView 权限（登录审计，仅 Admin）
/users          → 受保护 + UserManage 权限（用户管理，含数据库权限弹窗）
/servers        → 受保护 + ServerView 权限（服务器实例管理）
/system         → 受保护 + SystemManage 权限（系统设置）
```

- `AuthContext` 管理全局认证状态（token、用户信息、权限列表）
- `AppLayout` 根据 `hasPermission()` 条件渲染侧边栏菜单
- Axios 拦截器自动附加 `Authorization: Bearer <token>`，401 时重定向到登录页

---

## 配置说明

### appsettings.json 关键配置

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=ExcelImportDb;..."
  },
  "Jwt": {
    "Key": "至少32字符的密钥",
    "Issuer": "ExcelImportSystem",
    "Audience": "ExcelImportSystem",
    "ExpireHours": "24"
  },
  "Ldap": {
    "Enabled": false,
    "Server": "ldap.example.com",
    "Port": 389,
    "UseSsl": false,
    "Domain": "EXAMPLE",
    "BaseDn": "DC=example,DC=com",
    "UserFilterTemplate": "(sAMAccountName={0})",
    "BindUserDn": "",
    "BindPassword": ""
  }
}
```

### 前端环境变量 (`.env`)

```
VITE_API_URL=http://localhost:5000/api
```

---

## 开发约定

### 后端
- **异步优先**：所有数据库操作使用 `async/await`
- **DTO 模式**：API 层仅使用 DTO，不暴露 Entity
- **统一响应格式**：所有 API 返回 `ApiResponse<T>` (Success, Message, Data, Errors)
- **异常处理**：Controller 中 `try/catch` 将业务异常映射到 HTTP 状态码
- **BCrypt**：密码哈希使用 `BCrypt.Net-Next`

### 前端
- **国际化**：所有用户可见文本必须通过 `t('key')` 翻译
- **类型安全**：TypeScript 严格模式，所有 API 响应均有类型定义
- **Context 模式**：认证、语言切换使用 React Context
- **Ant Design**：统一使用 antd 组件，自定义样式最小化

### 安全
- JWT 密钥至少 32 字符
- 密码使用 BCrypt 哈希（不是 MD5/SHA）
- SQL 使用参数化查询（防 SQL 注入）
- LDAP Filter 转义（防 LDAP 注入）
- CORS 限制前端源
- **登录必填 CAPTCHA**（SVG 验证码，防机器人暴力破解）
- **速率限制**：登录接口 10 次/分钟（ASP.NET Core Rate Limiter）
- **账号锁定**：5 次失败 → 锁定 15 分钟（防暴力猜解）
- **失败日志**：所有登录失败均记录 `LogWarning`（含原因：验证码错误、密码错误、账号锁定、AD 冲突）
- **防用户枚举**：不存在的用户统一返回 "Invalid username or password"，不触发锁定计数
- **登录审计**：每次登录尝试记录到 `LoginAuditLogs` 表（用户名、IP、UserAgent、成功/失败、失败原因），仅 Admin 可查看
- **前端防自动填充**：登录表单 `autoComplete="off"` / `"new-password"` 防止浏览器密码管理器覆盖用户输入
- **生产环境 HTTPS**：通过自建内部 CA 签发证书实现局域网 HTTPS，详见「10. 离线部署与 HTTPS」

---

## 项目初始化步骤

1. **数据库**：确保 SQL Server 运行，修改 `appsettings.json` 连接字符串
2. **后端**：`cd src && dotnet run`（启动于 `http://localhost:5000`）
3. **前端**：`cd frontend && npm install && npm run dev`（启动于 `http://localhost:5173`）
4. **登录**：默认账号 `admin / admin123`（登录页面不显示提示，需输入验证码）
5. **LDAP**：配置 AD 服务器信息，设置 `Ldap:Enabled: true`

---

## 扩展指南

### 添加新角色
1. `Program.cs` 的 `SeedData` 中添加新 `Role` 实体
2. 为新角色添加 `RolePermission` 条目
3. 如需要新权限，在 `Program.cs` 的 `AddAuthorization` 中添加新 Policy

### 添加新的导入目标
- 无需代码修改 — 系统通过 `INFORMATION_SCHEMA` 动态发现数据库、表和列
- 只需在 SQL Server 中创建表，系统即可自动识别

### 添加新页面
1. 在 `frontend/src/pages/` 创建页面组件
2. 在 `App.tsx` 添加路由
3. 在 `AppLayout.tsx` 添加侧边栏菜单项（配合 `hasPermission` 检查）
4. 添加 i18n 翻译键（`en.json` + `zh.json`）

### 添加新权限
1. 在 `Program.cs` 的 `AddAuthorization` 中添加新 Policy
2. 在 `Program.cs` 的 `SeedData` 中为角色添加对应的 `RolePermission`
3. 在 `Program.cs` 的迁移代码中添加数据迁移 SQL（确保已有数据库也能获得新权限）
4. 前端通过 `hasPermission('Your.Permission')` 进行 UI 控制

### 11. 离线部署与 HTTPS

**离线部署包结构**（`offline-deploy/` 目录）：

```
offline-deploy/
├── excelimportsystem-api.tar.gz      # 后端镜像 (~135 MB)
├── excelimportsystem-frontend.tar.gz # 前端镜像 (~32 MB)
├── docker-compose.yml                # 编排文件 (image: 模式，非 build:)
├── nginx-https.conf                  # Nginx HTTPS 配置
├── generate-cert.sh / .bat           # 证书生成脚本
├── .env.template                     # 环境变量模板
├── deploy.sh / .bat                  # 一键部署脚本
└── README.md                         # 离线部署说明
```

**HTTPS 证书架构**：

使用自建内部 CA 签发服务器证书，支持 IP 地址访问，不依赖域名或外部 CA。

```
ca.crt (CA 根证书，有效期 10 年)
  └── server.crt (服务器证书，CN=<服务器IP>，由 CA 签发)
```

- `generate-cert.sh` / `generate-cert.bat`：使用 OpenSSL 生成 CA 根证书和服务器证书
- CA 私钥留存在服务器上不分发，CA 证书需安装到每台客户端
- 服务器证书 CN 填服务器 IP 地址，有效期 10 年

**Nginx HTTPS 配置**（`nginx-https.conf`）：

- HTTP (80) → HTTPS (443) 301 永久重定向
- TLS 1.2 / 1.3，禁用旧协议
- HSTS 头（`max-age=31536000`）
- 安全头：`X-Content-Type-Options`、`X-Frame-Options`
- API 代理设置 `X-Forwarded-Proto: https` 传递原始协议
- Docker Compose 中通过 volumes 挂载覆盖容器内 `/etc/nginx/conf.d/default.conf` 和 `/etc/nginx/certs/`

**Docker Compose 部署配置**：

与开发环境不同，生产 compose 移除了 SQL Server 容器（`db` 服务），改用外部数据库。容器通过 `host.docker.internal` 连接宿主机上的 SQL Server。

```yaml
# API 服务关键变更
environment:
  - ConnectionStrings__DefaultConnection=Server=host.docker.internal;Database=ExcelImportDb;...
ports:
  - "5001:5000"    # 避免与 Windows Docker Desktop 的 5000 端口冲突

# frontend 服务关键变更
ports:
  - "${FRONTEND_HTTP_PORT:-80}:80"
  - "${FRONTEND_HTTPS_PORT:-443}:443"
volumes:
  - ./nginx-https.conf:/etc/nginx/conf.d/default.conf:ro
  - ./certs:/etc/nginx/certs:ro
```

**客户端信任 CA**：

- Windows：`certutil -addstore Root ca.crt` 或通过域组策略自动分发
- Linux：`sudo cp ca.crt /usr/local/share/ca-certificates/ && sudo update-ca-certificates`
- macOS：`sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain ca.crt`

**仅 HTTP 部署（不推荐）**：将 `nginx-https.conf` 替换为纯 HTTP 配置，删除 docker-compose.yml 中 HTTPS 端口映射和 volumes。详见 `offline-deploy/README.md`。
