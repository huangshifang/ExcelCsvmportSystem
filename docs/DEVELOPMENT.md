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
| **Excel** | EPPlus 7 |
| **日志** | Serilog (Console + Rolling File) |
| **数据库** | SQL Server |
| **HTTP 客户端** | Axios (前端) |

---

## 架构设计

### 后端：Clean Architecture 三层分离

```
ExcelImportSystem.sln
├── ExcelImportSystem.Core         # 领域层：实体、DTO、接口
│   ├── Entities/                  # User, Role, UserRole, RolePermission, ImportLog
│   ├── DTOs/                      # 请求/响应 DTO
│   ├── Interfaces/                # 服务接口 (IAuthService, IImportService 等)
│   └── Configurations/            # 配置模型 (LdapSettings)
│
├── ExcelImportSystem.Infrastructure  # 基础设施层：实现
│   ├── Data/                      # AppDbContext + EF Fluent API 配置
│   ├── Services/                  # 服务实现 (AuthService, LdapService 等)
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
├── components/        # 共享组件 (AppLayout 布局)
├── context/           # React Context (AuthContext, LocaleContext)
├── i18n/              # 国际化 (中文/英文)
├── pages/             # 页面组件 (Dashboard, Import, Users, Login)
└── types/             # TypeScript 类型定义
```

---

## 核心功能实现

### 1. 认证与授权

**双重认证策略（Hybrid Auth）**：

```
登录请求 → 查找本地用户
  ├─ AuthType = "Local" → BCrypt 密码验证
  │   ├─ 成功 → 生成 JWT
  │   └─ 失败 → 返回 401
  └─ 本地失败 → 尝试 LDAP 绑定
      ├─ LDAP 绑定成功 → 查找或创建本地用户记录 → 分配 Viewer 角色 → 生成 JWT
      └─ LDAP 绑定失败 → 返回 401
```

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
| `LogView` | `Permission: Log.View` | 查看日志 |

**三角色体系**：

| 角色 | 权限 |
|------|------|
| **Admin** | Import.Execute, Import.View, User.Manage, Role.Manage, Log.View |
| **Operator** | Import.Execute, Import.View, Log.View |
| **Viewer** | Import.View |

### 2. Excel 数据导入

**两步流程**：

1. **Preview（预览）** — `POST /api/import/preview`
   - 上传 Excel 文件 → EPPlus 读取 → 提取列名 + 最多 5 行示例数据
   - 查询 SQL Server 目标表的列元数据（类型、可空、主键、自增）
   - 按名称（不区分大小写）自动映射 Excel 列到数据库列
   - 返回：Excel 列、示例数据、表列、自动映射、总行数

2. **Execute（执行）** — `POST /api/import/execute`
   - 接收文件 + 用户确认的列映射关系
   - 构建参数化 SQL INSERT 语句（防 SQL 注入）
   - 开启数据库事务，按批次（默认 1000 行）插入
   - 使用 `System.Data.Common.DbTransaction` 确保跨批次原子性
   - 记录 ImportLog（成功/部分成功/失败）

**关键实现细节**（`ImportService.cs`）：
- 自增列自动跳过（让数据库生成值）
- 参数化查询防止 SQL 注入
- 事务支持可配置（`UseTransaction` 参数）
- 批次处理避免大文件内存溢出

### 3. LDAP/AD 集成

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
- 采用 Raw SQL 条件迁移：检查列是否存在，不存在则 `ALTER TABLE ADD`

### 4. 仪表板统计

- `GET /api/dashboard/stats` — 聚合 `ImportLogs` 表
- **Admin** 角色 → 汇总所有用户的导入数据
- **普通用户** → 只汇总自己的导入数据
- Controller 层检查 `ClaimTypes.Role` 决定是否传递 `userId` 过滤条件

### 5. 前端路由与权限控制

```
/login          → 公开（已登录则重定向到 /）
/               → 受保护（仪表板）
/import         → 受保护（导入页面）
/import-logs    → 受保护（导入日志）
/users          → 受保护 + UserManage 权限（用户管理）
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
- 生产环境建议使用 HTTPS

---

## 项目初始化步骤

1. **数据库**：确保 SQL Server 运行，修改 `appsettings.json` 连接字符串
2. **后端**：`cd src && dotnet run`（启动于 `http://localhost:5000`）
3. **前端**：`cd frontend && npm install && npm run dev`（启动于 `http://localhost:5173`）
4. **登录**：默认账号 `admin / admin123`
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
3. 在 `AppLayout.tsx` 添加侧边栏菜单项（配合权限检查）
4. 添加 i18n 翻译键
