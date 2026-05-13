# 优化变更日志 — 2026-05-13

## 多 SQL Server 实例支持（新增）

### 1. 远程服务器管理

| 项目 | 说明 |
|------|------|
| 实体 | `SqlServerInstance`（Id, Name, ConnectionString, Description, IsActive, CreatedAt） |
| 配置 | EF Fluent API：表名 `SqlServerInstances`，Name 唯一索引，ConnectionString 最大 1000 字符 |
| API | `ServerController` — 完整 CRUD + 连接测试端点 |
| 权限 | `Server.View`（查看服务器列表）、`Server.Manage`（创建/编辑/删除服务器） |
| 安全 | GET 响应从不返回 ConnectionString；删除时检查关联的 UserDatabaseAccess 引用 |
| 前端页面 | `/servers` — `ServersPage.tsx`，侧边栏「服务器实例」菜单项（CloudServerOutlined 图标） |

### 2. ConnectionFactory 连接工厂

| 项目 | 说明 |
|------|------|
| 接口 | `IConnectionFactory` — `CreateConnectionAsync(serverId?)`, `GetServersAsync()`, `ResolveServerIdAsync()`, `InvalidateCache()` |
| 实现 | `ConnectionFactory`（Singleton），使用 `IServiceScopeFactory` 安全访问 scoped DbContext |
| 缓存策略 | `ConcurrentDictionary` 缓存连接字符串（按 ServerId）和数据库→服务器映射 |
| 本地连接 | `serverId = null` → 使用 AppDbContext 连接字符串（向后兼容） |
| 远程连接 | `serverId = N` → 从缓存/数据库加载连接字符串，检查 IsActive 状态 |
| 数据库发现 | `EnsureCacheAsync()` 遍历所有活跃服务器执行 `sys.databases` 查询，失败时 LogWarning 不阻塞 |

### 3. 表级权限 ServerId 扩展

| 变更项 | 原有 | 现在 |
|--------|------|------|
| `UserDatabaseAccess` 表 | `(UserId, DatabaseName, SchemaName, TableName)` | 增加 `ServerId INT NULL` FK → `SqlServerInstances(Id)` |
| 过滤唯一索引 | `UQ_UserDatabaseAccess_Wildcard` / `UQ_UserDatabaseAccess_Table` | 重建为包含 `ServerId` 的索引 |
| `IDatabaseAccessService` | `HasAccessAsync(userId, db, schema?, table?)` | 增加 `int? serverId = null` 参数 |
| `UserTableAccessDto` | `(DatabaseName, SchemaName?, TableName?)` | 增加 `int? ServerId` |

### 4. TableService 多服务器发现

| 方法 | 变更 |
|------|------|
| `GetDatabasesAsync()` | 首先查询本地服务器 `sys.databases`（ServerId=null），然后遍历所有活跃远程服务器，每个返回的 `DatabaseInfoDto` 包含 `ServerId` 和 `ServerName` |
| `GetTablesAsync()` | 接受 `int? serverId`，通过 `IConnectionFactory.CreateConnectionAsync(serverId)` 创建连接，而不是使用 `context.Database.GetDbConnection()` |
| `GetTableAsync()` | 同上，接受 `serverId` 参数 |
| 非 Admin 过滤 | `(ServerId, DatabaseName)` 元组集合替代原来的纯 `DatabaseName` 集合 |

### 5. ImportService 多服务器支持

| 方法 | 变更 |
|------|------|
| `PreviewAsync` | 传递 `request.ServerId` 给 `tableService.GetTableAsync()` 和 `ValidateAccess()` |
| `ExecuteAsync` | 从 scope 解析 `IConnectionFactory`，传递给 `ExecuteInternalAsync` |
| `ExecuteInternalAsync` | 使用 `connectionFactory.CreateConnectionAsync(request.ServerId)` 替代 `context.Database.GetDbConnection()` |
| `ValidateAccess` | 传递 `serverId` 给 `HasAccessAsync` |

### 6. 前端适配

| 组件 | 变更 |
|------|------|
| `ImportPage` | 新增服务器选择器（数据库选择器上方），数据库选项显示 `[ServerName] dbName` 格式 |
| `UsersPage` 数据库权限弹窗 | 数据库按服务器分组显示，`handleToggleWildcard`/`handleToggleTable` 传播 `serverId` |
| `AppLayout` | 新增「服务器」菜单项（`Server.View` 权限控制） |
| `App.tsx` | 新增 `/servers` 路由 |
| `api/servers.ts` | 新增 API 模块（CRUD + 测试连接） |
| `api/tables.ts` | `getAll`/`getOne` 接受可选 `serverId` 参数 |
| `types/index.ts` | 新增 `SqlServerInstance`, `CreateServerRequest`, `UpdateServerRequest` 类型 |

### 7. i18n

| 键 | 英文 | 中文 |
|----|------|------|
| `nav.servers` | Servers | 服务器 |

### 8. 向后兼容性

- `ServerId = null` 在所有地方表示"本地/默认服务器"，与原有行为完全一致
- 未配置任何远程服务器时，系统行为与 v1.1 完全相同
- 已有的 `UserDatabaseAccess` 记录 `ServerId` 为 NULL，继续正常工作

### 9. Docker 注意事项

- 远程服务器地址使用 `host.docker.internal` 可连接到 Docker 宿主机上的 SQL Server
- 不可达的远程服务器不会阻塞本地数据库访问（`TableService` 捕获异常并记录 Warning）
- 新增容器无额外端口映射，通过宿主机网络访问远程 SQL Server

---

## 新增权限汇总

| 权限 Claim | 策略名 | 归属角色 | 用途 |
|------------|--------|----------|------|
| `Server.View` | `ServerView` | Admin | 查看服务器实例列表 |
| `Server.Manage` | `ServerManage` | Admin | 创建/编辑/删除服务器实例 |

10 个权限 → Admin：全部拥有；Operator：Import.Execute, Import.View, Log.View；Viewer：Import.View

---

## 新增文件清单

### 后端新增

| 文件 | 说明 |
|------|------|
| `Core/Entities/SqlServerInstance.cs` | 远程服务器实例实体 |
| `Core/Interfaces/IConnectionFactory.cs` | 多服务器连接工厂接口 |
| `Core/DTOs/ServerDtos.cs` | 服务器 CRUD DTO |
| `Infrastructure/Services/ConnectionFactory.cs` | 连接工厂实现（Singleton + 缓存） |
| `Infrastructure/Data/Configurations/SqlServerInstanceConfiguration.cs` | EF 配置 |
| `API/Controllers/ServerController.cs` | 服务器管理 API |

### 前端新增

| 文件 | 说明 |
|------|------|
| `api/servers.ts` | 服务器 API 调用模块 |
| `pages/Servers/ServersPage.tsx` | 服务器管理页面 |

### 修改的文件

| 文件 | 变更摘要 |
|------|----------|
| `Core/Entities/UserDatabaseAccess.cs` | 添加 ServerId + Server 导航属性 |
| `Core/DTOs/TableDtos.cs` | DatabaseInfoDto 添加 ServerId/ServerName |
| `Core/DTOs/ImportDtos.cs` | ImportRequestDto 添加 ServerId |
| `Core/DTOs/CommonDtos.cs` | UserTableAccessDto 添加 ServerId |
| `Core/Interfaces/ITableService.cs` | GetTablesAsync/GetTableAsync 添加 serverId 参数 |
| `Core/Interfaces/IDatabaseAccessService.cs` | HasAccessAsync/GrantAccessAsync 添加 serverId 参数 |
| `Infrastructure/Data/AppDbContext.cs` | 添加 SqlServerInstances DbSet |
| `Infrastructure/Data/Configurations/UserConfiguration.cs` | 添加 Server FK 关系，更新索引 |
| `Infrastructure/Services/TableService.cs` | 多服务器数据库/表发现 |
| `Infrastructure/Services/ImportService.cs` | 使用 IConnectionFactory 替代 AppDbContext 连接 |
| `Infrastructure/Services/DatabaseAccessService.cs` | ServerId 支持 |
| `Infrastructure/Extensions/ServiceCollectionExtensions.cs` | 注册 IConnectionFactory 为 Singleton |
| `API/Program.cs` | Server 策略 + 迁移 SQL + 权限种子 |
| `API/Controllers/TableController.cs` | 接受 serverId 查询参数 |
| `API/Controllers/ImportController.cs` | 传递 ServerId 到服务层 |
| 前端 10+ 文件 | 类型、API、页面、路由、i18n 适配 |
