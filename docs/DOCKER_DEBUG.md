# Docker 部署调试记录

> 日期：2026-05-07  
> 目的：首次测试 `docker compose up` 构建部署，记录所有遇到的问题和修复方案

---

## 环境

| 项目 | 值 |
|---|---|
| 操作系统 | Windows 11 Home China (Win32) |
| Docker | 29.2.1（Docker Desktop） |
| WSL | docker-desktop (Stopped → 手动启动) |
| 后端 | .NET 10.0.0-preview.3 / ASP.NET Core 10 |
| 前端 | React 19 + TypeScript + Vite |
| 数据库 | SQL Server 2022（宿主机） |

---

## 问题 1：Docker Desktop 未运行

**现象**：

```
failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine
```

**原因**：Docker Desktop 服务未启动。

**解决**：启动 Docker Desktop。

```bash
start "" "C:/Program Files/Docker/Docker/Docker Desktop.exe"
# 等待约 30 秒至 docker info 返回正常
```

---

## 问题 2：Docker Hub 不可访问，MCR 可用

**现象**：`docker pull` 从 Docker Hub 拉取镜像超时（`registry-1.docker.io` 不可达），但 MCR（`mcr.microsoft.com`）可访问。

**诊断**：

```bash
curl -sI --connect-timeout 10 https://registry-1.docker.io  # timeout
curl -sI --connect-timeout 10 https://mcr.microsoft.com      # 200
```

**原因**：国内网络环境，Docker Hub 被墙。

**解决**：Docker Desktop 已预配 3 个国内镜像加速器：

```json
// C:\Users\86158\.docker\daemon.json
{
  "registry-mirrors": [
    "https://docker.1ms.run",
    "https://docker.m.daocloud.io",
    "https://docker.xuanyuan.me"
  ]
}
```

- `node:22-alpine`、`nginx:alpine` 通过镜像加速器成功拉取
- .NET SDK / ASP.NET 运行时来自 MCR（`mcr.microsoft.com`），直接可访问

---

## 问题 3：前端 TypeScript 编译失败（4 个错误）

**现象**：

```
src/context/AuthContext.tsx(61,15): error TS2345: Property 'authType' is missing
src/context/AuthContext.tsx(3,29): error TS6196: 'LoginResponse' is declared but never used
src/pages/Import/ImportPage.tsx(9,3): error TS6133: 'UploadOutlined' declared but never used
src/pages/Import/ImportPage.tsx(96,29): error TS2352: Conversion of 'UploadFile<any>' to 'Blob'
```

**原因**：Docker 构建执行 `tsc -b` 全量类型检查，比 Vite 开发模式更严格。

**修复**：

| 文件 | 行 | 修改 |
|---|---|---|
| `AuthContext.tsx` | 61 | 回退 UserInfo 对象补充 `authType: 'Local'` |
| `AuthContext.tsx` | 3 | 移除未使用的 `LoginResponse` 导入 |
| `ImportPage.tsx` | 9 | 移除未使用的 `UploadOutlined` 图标导入 |
| `ImportPage.tsx` | 96, 139 | `fileList[0] as Blob` → `fileList[0].originFileObj as Blob` |

---

## 问题 4：API Dockerfile — `dotnet publish --no-restore` 失败

**现象**：

```
error MSB4018: NuGet.Packaging.Core.PackagingException:
Unable to find fallback package folder
'C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages'
```

**原因**：Dockerfile 分步执行 `dotnet restore`（生成 project.assets.json）然后 `dotnet publish --no-restore`。Windows 宿主机上生成的 assets.json 引用了 Visual Studio 的 NuGet 回退文件夹，Linux 容器中该路径不存在。

**修复**：移除 `--no-restore` 参数，让 `dotnet publish` 在容器内重新 restore，生成干净的 assets.json。

```dockerfile
# 修改前
RUN dotnet publish ... -c Release -o /app --no-restore

# 修改后
RUN dotnet publish ... -c Release -o /app
```

保留显式 `dotnet restore` 步骤以利用 Docker 层缓存加速后续构建。

---

## 问题 5：API 绑定 localhost 导致容器外无法访问

**现象**：

```
$ curl http://localhost:5000/api/diagnostics/health
curl: (52) Empty reply from server
```

但 `docker logs` 显示应用正常启动、数据库连接成功。

**原因**：`Program.cs` 第 19 行：

```csharp
builder.WebHost.UseUrls("http://localhost:5000");
```

`localhost` 在容器内部仅绑定 loopback 接口，Docker 端口映射无法将外部请求转发到 loopback 地址。

**修复**：

```csharp
// 修改为监听所有网络接口
builder.WebHost.UseUrls("http://+:5000");
```

---

## 问题 6：SQL Server 镜像无法拉取（已绕过）

**现象**：`mcr.microsoft.com/mssql/server:2022-latest`（约 1.5GB）下载至约 95MB 后中断：

```
short read: expected 490183101 bytes but got 95830016: unexpected EOF
```

**原因**：镜像太大，国内网络不稳定，下载中断且层完整性校验失败。

**决策**：舍弃 SQL Server 容器。数据库使用宿主机 SQL Server，连接串改为 `host.docker.internal`。

**修改 `docker-compose.yml`**：

```yaml
# 移除 db 服务及其 depends_on、volume

# API 连接串改为指向宿主机
environment:
  - ConnectionStrings__DefaultConnection=Server=host.docker.internal;Database=ExcelImportDb;User ID=excel_user;Password=sh.12345;TrustServerCertificate=True;MultipleActiveResultSets=true
```

---

## 最终验证

### 容器状态

```
NAME                           STATUS
excelimportsystem-api-1        Up, port 5000
excelimportsystem-frontend-1   Up, port 80
```

### 健康检查

```json
{
  "status": "healthy",
  "database": {"connected": true, "userCount": 2, "error": ""},
  "server": "a86db081d42e",
  "time": "2026-05-07T14:07:10Z"
}
```

### 登录测试

```bash
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"admin123"}'
# → 返回 JWT Token，角色: Admin，权限: 5个全部
```

### 前端

`http://localhost` → Nginx 正常提供 React SPA 页面

---

## 修改文件汇总

| 文件 | 操作 | 说明 |
|---|---|---|
| `src/ExcelImportSystem.API/Program.cs` | 修改 | `http://localhost:5000` → `http://+:5000` |
| `src/Dockerfile` | 修改 | 移除 `--no-restore` 参数 |
| `docker-compose.yml` | 修改 | 移除 `db` 服务，连接串指向宿主机 |
| `frontend/src/context/AuthContext.tsx` | 修改 | 补充 `authType` 字段 + 移除未用导入 |
| `frontend/src/pages/Import/ImportPage.tsx` | 修改 | 修复类型转换 + 移除未用导入 |

---

## 建议

1. **CI/CD**：将 `tsc -b` 加入 pre-commit hook 或 CI pipeline，避免 Docker 构建时才发现 TS 错误
2. **数据保护**：容器日志提示 `DataProtection-Keys` 未持久化，生产环境应挂载 volume 到 `/root/.aspnet/DataProtection-Keys`
3. **.NET 稳定版**：当前使用预览版 SDK 和 NuGet 包，待 .NET 10 正式发布后切换
4. **API 端口暴露**：当前 API 端口 5000 对外暴露，生产环境建议仅暴露前端 80 端口
