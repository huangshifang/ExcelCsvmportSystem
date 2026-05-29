# 变更日志 — 2026-05-20

## 修复: 远程实例用户授权时报错 "Failed to load tables for GUI"

**问题**：管理员在新增远程 SQL Server 实例后，为用户授权该实例上的数据库表时，展开数据库节点时报错 `Failed to load tables for GUI`。

**根因**：前端 `UsersPage.tsx` 调用 `databaseAccessApi.getDatabaseTables(database, serverId)` 时正确传递了 `serverId`，但后端 `DatabaseAccessController.GetDatabaseTables()` 方法签名缺失 `[FromQuery] int? serverId` 参数，导致 `serverId` 被丢弃。`TableService.GetTablesAsync(database, serverId: null)` 总是连接本地 SQL Server，远程数据库在本地不存在，抛出 "数据库不存在" 异常。

**修复**：
| 文件 | 变更 |
|------|------|
| `API/Controllers/DatabaseAccessController.cs` | `GetDatabaseTables()` 新增 `[FromQuery] int? serverId = null` 参数，透传给 `GetTablesAsync()` |

---

## 修复: 普通用户选择远程实例数据库时报错 "Database 'XXX' does not exist"

**问题**：管理员为用户授权远程实例后，该用户登录进入导入页面，选择数据库时报错 `Failed to load tables: Failed to query tables in database 'GUI': 数据库 'GUI' 不存在`。

**根因**：前端 `ImportPage.tsx` 数据库下拉框的 `value` 只是数据库名称（如 `"GUI"`），不包含 `serverId` 信息。当多个 SQL Server 实例存在同名数据库时（尤其是远程实例），选择后 `handleDatabaseSelect(database)` 无法确定目标服务器，`selectedServerId` 可能为 `undefined`，导致请求本地服务器上不存在的数据库。

**修复**：
| 文件 | 变更 |
|------|------|
| `frontend/src/pages/Import/ImportPage.tsx` | 数据库下拉框 `value` 改为复合键 `${d.serverId ?? 0}::${d.name}` |
| 同上 | 新增 `parseDbKey()` 辅助函数，解析复合键提取 `serverId` 和数据库名 |
| 同上 | `handleDatabaseSelect` 调用 `parseDbKey` 并设置 `selectedServerId` |
| 同上 | Select 组件 `value` 属性同步使用复合键格式 |

**注意**：`serverId === 0` 表示本地服务器，`parseDbKey` 将其转为 `undefined` 以确保 API 请求不携带 `serverId` 参数（避免后端尝试查找不存在的服务器 #0）。

---

## 新增: 导入日志显示数据库实例信息

**功能**：在导入日志页面增加「数据库实例」列，区分本地和远程 SQL Server 的导入记录。

**改动**：
| 层 | 文件 | 变更 |
|------|------|------|
| Entity | `Core/Entities/ImportLog.cs` | 新增 `ServerId` (int?) 和 `ServerName` (string?) 字段 |
| DTO | `Core/DTOs/CommonDtos.cs` | `ImportLogDto` 新增 `ServerId`、`ServerName` |
| 迁移 | `API/Program.cs` | 新增 ALTER TABLE 迁移 SQL，添加 `ServerId`、`ServerName` 列 |
| 服务 | `Infrastructure/Services/ImportService.cs` | 创建导入日志时查询 `SqlServerInstances` 表获取实例名称并保存 |
| 服务 | `Infrastructure/Services/ImportLogService.cs` | 查询投影追加 `ServerId`、`ServerName` |
| 前端类型 | `frontend/src/types/index.ts` | `ImportLog` 接口新增 `serverId?`、`serverName?` |
| 前端页面 | `frontend/src/pages/ImportLogs/ImportLogsPage.tsx` | 表格和详情模态框增加「数据库实例」列 |
| i18n | `src/i18n/locales/en.json`、`zh.json` | 新增 `logs.server`（Server Instance / 数据库实例）、`logs.local`（Local / 本地） |

**显示规则**：`serverName` 有值时显示实例名称，为 null 时显示「本地」。

---

## 本次修复文件汇总

| 文件 | 变更类型 |
|------|----------|
| `API/Controllers/DatabaseAccessController.cs` | Bug 修复：新增 serverId 参数 |
| `frontend/src/pages/Import/ImportPage.tsx` | Bug 修复：数据库复合键编码 + parseDbKey |
| `Core/Entities/ImportLog.cs` | 新增字段 ServerId、ServerName |
| `Core/DTOs/CommonDtos.cs` | ImportLogDto 新增字段 |
| `API/Program.cs` | 新增迁移 SQL |
| `Infrastructure/Services/ImportService.cs` | 日志写入时查询并保存服务器名称 |
| `Infrastructure/Services/ImportLogService.cs` | 查询投影追加字段 |
| `frontend/src/types/index.ts` | ImportLog 接口新增字段 |
| `frontend/src/pages/ImportLogs/ImportLogsPage.tsx` | 新增加「数据库实例」列 |
| `frontend/src/i18n/locales/en.json` | 新增翻译键 |
| `frontend/src/i18n/locales/zh.json` | 新增翻译键 |
