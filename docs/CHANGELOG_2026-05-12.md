# 优化变更日志 — 2026-05-12

## 一、安全防护全面升级

### 1. CAPTCHA 验证码（新增）

| 项目 | 说明 |
|------|------|
| 实现文件 | `CaptchaService.cs` |
| 技术方案 | 纯 SVG 生成，零原生依赖（无 SkiaSharp/System.Drawing），Docker 兼容 |
| 验证规则 | 4 位随机字符（排除 0/O/I/L/1 等易混淆字符），随机颜色、旋转角度、干扰线、噪点 |
| 存储策略 | 内存 `ConcurrentDictionary`，5 分钟过期，自动清理 |
| 登录要求 | **每次登录必填**，`LoginDto` 增加 `CaptchaToken` + `CaptchaCode` 字段 |
| 前端实现 | 原生 `<img>` 渲染 `data:image/svg+xml;base64,...`，登录失败自动刷新 |
| 接口 | `ICaptchaService`：`Generate()` → (Token, Base64Image)，`Validate(token, code)` → bool |
| API | `GET /api/auth/captcha`（无需认证） |

### 2. 速率限制（新增）

| 项目 | 说明 |
|------|------|
| 实现位置 | `Program.cs` — `AddRateLimiter` |
| 算法 | 固定窗口（Fixed Window） |
| 策略 | `Login`：10 次请求 / 1 分钟 / IP |
| 应用端点 | `POST /api/auth/login` — `[EnableRateLimiting("Login")]` |
| 超限响应 | HTTP 503，由 ASP.NET Core 框架自动处理 |

### 3. 账号锁定机制（新增）

| 项目 | 说明 |
|------|------|
| 实体字段 | `User.FailedLoginCount`（int，默认 0）、`User.LockoutEnd`（DateTime?） |
| 阈值 | 连续 **5 次** 失败 → 锁定 **15 分钟** |
| 计数范围 | 仅针对已存在的用户（不存在的用户统一返回 "Invalid username or password"，防用户名枚举） |
| 触发路径 | 本地密码错误、LDAP 密码错误均计数 |
| 解锁方式 | 锁定到期自动解除 / 成功登录后立即清零 |
| 返回消息 | `"Account is locked. Try again in X minutes."` |
| SQL 迁移 | `Program.cs` 中 `IF NOT EXISTS` 条件添加列 |

### 4. 登录审计日志（新增）

| 项目 | 说明 |
|------|------|
| 实体 | `LoginAuditLog`（Id, Username, IpAddress, UserAgent, Success, FailureReason, Timestamp） |
| 服务 | `LoginAuditService` — 使用 `IServiceScopeFactory` 独立作用域，审计写入不阻塞登录流程 |
| 记录时机 | 每次登录尝试（成功/失败），自动获取客户端 IP 和 UserAgent |
| 失败原因 | Invalid captcha / Invalid password / Account locked / AD collision / Invalid credentials |
| API | `GET /api/auth/login-logs` — 分页查询，支持按用户名/状态/日期范围过滤 |
| 权限 | `Audit.View` — 仅 Admin 拥有 |
| 前端页面 | `/login-logs`（`LoginAuditPage.tsx`），侧边栏「登录审计」菜单项 |

### 5. 密码功能增强（新增）

| 功能 | 端点 | 权限 | 说明 |
|------|------|------|------|
| 用户自主改密 | `POST /api/auth/change-password` | 需登录 | 旧密码验证 + 新密码（最少 6 位），LDAP 用户不可使用 |
| 管理员重置密码 | `POST /api/auth/users/{id}/reset-password` | `UserManage` | 无需旧密码，直接覆盖哈希，LDAP 用户不可重置 |

前端 `ChangePasswordModal` 组件，仅非 LDAP 用户的菜单中显示。

### 6. Admin 密码种子数据修复（Bug 修复）

- **原有 bug**：每次服务重启都将 admin 密码重置为 `admin123`
- **修复后**：只在 `admin == null` 时创建新用户，移除 `else` 分支中 `BCrypt.HashPassword("admin123")` 的覆盖逻辑

---

## 二、导入系统重构

### 1. CSV 文件支持（新增）

| 项目 | 说明 |
|------|------|
| 解析库 | CsvHelper 33.0.1 |
| 支持格式 | `.csv`（逗号分隔）、`.tsv` / `.txt`（制表符分隔） |
| 检测方式 | 扩展名自动判断（`IsCsvFile()` / `IsExcelFile()`） |
| 配置 | RFC 4180 模式，`BadDataFound = null`（静默跳过坏行），自动检测分隔符 |
| 实现 | `ImportService.ReadCsvPreview()` 和后台执行中的 CSV 读取逻辑 |

### 2. 异步 Fire-and-Forget 执行（重构）

| 项目 | 说明 |
|------|------|
| 原有模式 | 同步等待 → 前端阻塞直到导入完成（大文件时请求超时） |
| 新模式 | 请求线程立即将文件流拷贝到内存 → 返回 `{ taskId }` → `Task.Run` 后台执行 |
| 优势 | 前端无需等待，支持超大文件导入，用户体验提升 |
| 进度存储 | `static ConcurrentDictionary<string, ImportProgressDto>` — 内存中，key 为 taskId |
| API | `POST /api/import/execute` → 返回 `ImportExecuteResponseDto(taskId)` |
| 进度查询 | `GET /api/import/progress/{taskId}` → 返回 `ImportProgressDto` |
| 前端轮询 | 每 **500ms** 轮询一次，展示圆形进度条（Ant Design `<Progress>`） |

### 3. 进度模型 `ImportProgressDto`

```json
{
  "taskId": "guid",
  "status": "reading",
  "percent": 42.5,
  "totalRows": 50000,
  "processedRows": 21250,
  "message": "Importing row 21250/50000...",
  "errorCount": 3,
  "result": null
}
```

状态流转：`pending` → `reading` → `importing` → `completed` / `failed`

### 4. 大文件上传支持

| 配置项 | 值 | 位置 |
|--------|-----|------|
| `Kestrel.MaxRequestBodySize` | 210,000,000 (200MB+) | `Program.cs` |
| `FormOptions.MultipartBodyLengthLimit` | 210,000,000 | `Program.cs` |
| 前端超时 | 300,000ms (5 分钟) | `importsApi.preview` / `importsApi.execute` |

### 5. Batch Size 可配置

前端 `ImportPage.tsx` 中 `InputNumber` 控件，范围 100-10000，默认 1000，值随请求传递到后端。

---

## 三、表级数据库权限管理（新增）

### 1. 实体升级

`UserDatabaseAccess` 从 v1.0 的数据库级升级到 v1.1 的表级：

| 新增字段 | 类型 | 说明 |
|----------|------|------|
| `SchemaName` | nvarchar(200)? | 架构名 |
| `TableName` | nvarchar(200)? | 表名（NULL = 通配符，表示整库授权） |

唯一约束改为过滤索引：
- `UQ_UserDatabaseAccess_Wildcard`：`(UserId, DatabaseName) WHERE SchemaName IS NULL AND TableName IS NULL`
- `UQ_UserDatabaseAccess_Table`：`(UserId, DatabaseName, SchemaName, TableName) WHERE TableName IS NOT NULL`

### 2. 服务 `DatabaseAccessService`

- `GetUserDatabasesAsync(userId)` — 获取有权限的数据库（去重）
- `GetUserTableAccessesAsync(userId)` — 获取所有表级访问记录
- `SetUserTableAccessesAsync(userId, accesses, grantedBy)` — 全量替换模式
- `HasAccessAsync(userId, database, schema?, table?)` — 单表权限检查

### 3. 导入权限验证

`ImportService.PreviewAsync()` 和 `ExecuteAsync()` 在执行前调用 `ValidateAccess()`：
- 查询用户角色 → Admin 跳过验证
- 非 Admin → 调用 `IDatabaseAccessService.HasAccessAsync()`
- 无权限 → 抛出 `UnauthorizedAccessException` → 返回 HTTP 403

### 4. 前端管理员 UI

`UsersPage` 中「数据库权限」按钮 → 弹出 `Modal`：
- 两级树形选择器：数据库 → 展开显示表列表（懒加载）
- 「选择所有表」复选框 = 通配符授权
- 逐表勾选 = 精确授权

---

## 四、系统设置模块（新增）

### 1. 运行时 LDAP 配置

| 组件 | 生命周期 | 职责 |
|------|----------|------|
| `SystemSetting` 表 | 持久化 | Key-Value 存储（`Ldap:*` 前缀） |
| `SystemSettingsService` | Scoped | 单键读写 + 批量写入 |
| `LdapSettingsProvider` | Singleton | 内存缓存，`IServiceScopeFactory` 访问 scoped DbContext |

### 2. 配置优先级

```
appsettings.json（默认） → SystemSettings 表（持久化） → LdapSettingsProvider（内存缓存） → LdapService
```

通过 API 更新后立即刷新缓存，无需重启。

### 3. 前端页面

- 路由 `/system`，权限 `System.Manage`（仅 Admin）
- LDAP 启用开关、服务器/端口/SSL/Domain/BaseDN/Filter/BindUser/BindPassword
- 「测试连接」功能：输入域账号密码验证配置

---

## 五、新增权限汇总

| 权限 Claim | 策略名 | 归属角色 | 用途 |
|------------|--------|----------|------|
| `Audit.View` | `AuditView` | Admin | 查看登录审计日志 |
| `Database.Manage` | `DatabaseManage` | Admin | 管理用户数据库/表权限 |
| `System.Manage` | `SystemManage` | Admin | 系统设置（LDAP 等） |

8 个权限 → Admin：全部拥有；Operator：Import.Execute, Import.View, Log.View；Viewer：Import.View

---

## 六、Docker & 部署变更

### 1. Docker Compose

| 变更项 | 原有 | 现在 |
|--------|------|------|
| SQL Server 容器 | 内嵌（`db` 服务） | **移除**，改用外部数据库 |
| 数据库连接 | `Server=db;...` | `Server=host.docker.internal;...` |
| API 端口 | 无端口映射 | `5001:5000` |
| 前端 HTTP | `80:80` | `8080:80` |
| 前端 HTTPS | 无 | `8443:443`（新增） |
| HTTPS 证书 | 无 | `offline-deploy/certs/` 挂载到 `/etc/nginx/certs/` |
| 时区 | 未设置 | `TZ=Asia/Shanghai` |

### 2. 离线部署包

`offline-deploy/` 目录新增：
- Docker 镜像 tar.gz（API + 前端）
- `nginx-https.conf`（HTTPS + HTTP→HTTPS 重定向 + HSTS + 安全头）
- `generate-cert.sh` / `.bat`（OpenSSL 自建内部 CA 签发服务器证书）
- `deploy.sh` / `.bat`（一键部署脚本）
- `.env.template`（环境变量模板）

---

## 七、代码质量与架构改进

| 改进项 | 说明 |
|--------|------|
| `IServiceScopeFactory` 模式 | `ImportService`、`LoginAuditService`、`LdapSettingsProvider` 均使用此模式解决生命周期不匹配问题 |
| API 响应格式统一 | `ConfigureApiBehaviorOptions` 覆盖默认 `InvalidModelStateResponseFactory`，返回 `ApiResponse` 而非默认 `ProblemDetails` |
| SQL 异常处理 | `TableController` 区分 `SqlException.Number == 916`（权限拒绝）和其他异常，返回更友好的错误信息 |
| 日志完善 | `AuthService` 对所有失败类型记录 `LogWarning`，成功记录 `LogInformation`，便于安全审计和故障排查 |
| 前端防自动填充 | 登录表单 `autoComplete="off"` + `autoComplete="new-password"` 防止浏览器密码管理器导致意外锁定 |

---

## 八、新增文件清单

### 后端新增

| 文件 | 说明 |
|------|------|
| `Infrastructure/Services/CaptchaService.cs` | SVG 验证码生成与验证 |
| `Infrastructure/Services/LoginAuditService.cs` | 登录审计记录与查询 |
| `Infrastructure/Services/DatabaseAccessService.cs` | 表级数据库权限管理 |
| `Infrastructure/Services/SystemSettingsService.cs` | 系统设置键值对读写 |
| `Infrastructure/Services/LdapSettingsProvider.cs` | LDAP 配置缓存与热更新 |
| `Core/Entities/LoginAuditLog.cs` | 登录审计实体 |
| `Core/Entities/SystemSetting.cs` | 系统设置实体 |
| `Core/Entities/UserDatabaseAccess.cs` | 用户数据库访问权限实体 |
| `Core/Interfaces/ICaptchaService.cs` | CAPTCHA 服务接口 |
| `Core/Interfaces/ILoginAuditService.cs` | 登录审计服务接口 |
| `Core/Interfaces/IDatabaseAccessService.cs` | 数据库权限服务接口 |
| `Core/Interfaces/ISystemSettingsService.cs` | 系统设置服务接口 |
| `API/Controllers/DatabaseAccessController.cs` | 数据库权限管理 API |
| `API/Controllers/SystemSettingsController.cs` | 系统设置 API |

### 前端新增

| 文件 | 说明 |
|------|------|
| `components/ChangePasswordModal.tsx` | 修改密码弹窗 |
| `pages/LoginAudit/LoginAuditPage.tsx` | 登录审计页面 |
| `pages/System/SystemSettingsPage.tsx` | 系统设置页面 |
| `api/databaseAccess.ts` | 数据库权限 API 调用 |
| `api/systemSettings.ts` | 系统设置 API 调用 |

---

## 九、文档更新

| 文档 | 更新内容 |
|------|----------|
| `CLAUDE.md` | 导入流程（CSV + 异步 + 轮询）、Docker 端口、新服务列表、`IServiceScopeFactory` 模式、表级权限验证、fire-and-forget 模式 |
| `docs/DEVELOPMENT.md` | 技术栈增加 CsvHelper、第 4 节重写（Excel/CSV + fire-and-forget + 进度跟踪 + 大文件支持）、安全措施完善、Docker 部署配置说明 |
| `docs/USER_MANUAL.md` | 系统描述增加 CSV、导入步骤增加实时进度 UI 和批量配置、支持格式表完善、角色权限对照表更新 |
