# 缺陷修复日志 — 2026-05-18

## 远程数据库实例表查询 3-part 命名错误（TableService.cs / ImportService.cs）

**问题**：配置远程（非默认）SQL Server 实例后，选择该实例上的数据库时，查询表列表报错：
```
对象名 'GUIFACTORY.INFORMATION_SCHEMA.TABLES' 无效。
```

**根因**：`TableService` 和 `ImportService` 使用 3-part 命名（如 `[Database].INFORMATION_SCHEMA.TABLES`、`[Database].[Schema].[Table]`）查询远程数据库。当 `SqlConnection` 通过 `ConnectionFactory` 连接到远程服务器时，3-part 命名在某些连接字符串配置下无法正确解析。

**修复**：采用 `connection.ChangeDatabase(database)` 切换当前数据库上下文，然后使用不带数据库前缀的查询：

| 文件 | 方法 | 变更 |
|------|------|------|
| `Infrastructure/Services/TableService.cs` | `GetTablesAsync()` | `connection.ChangeDatabase(database)` + 移除 `{Quote(database)}.` 前缀 |
| `Infrastructure/Services/TableService.cs` | `GetTableAsync()` | 同上，所有 `sys.*` 和 `INFORMATION_SCHEMA.*` 查询均移除 3-part 前缀 |
| `Infrastructure/Services/ImportService.cs` | `ExecuteInternalAsync()` | `connection.ChangeDatabase(request.Database)` + `DestinationTableName` 改为 `[schema].[table]` |
| `Infrastructure/Services/ImportService.cs` | `FallbackRowImport()` | INSERT 语句从 `[db].[schema].[table]` 改为 `[schema].[table]` |

**文档更新**：
| 文件 | 变更 |
|------|------|
| `docs/DEVELOPMENT.md` | 新增「跨数据库查询模式 — ChangeDatabase」章节 |

---

## 本次修复文件汇总

| 文件 | 变更类型 |
|------|----------|
| `src/ExcelImportSystem.Infrastructure/Services/TableService.cs` | 3-part → ChangeDatabase |
| `src/ExcelImportSystem.Infrastructure/Services/ImportService.cs` | 3-part → ChangeDatabase |
| `docs/DEVELOPMENT.md` | 新增 ChangeDatabase 模式文档 |
