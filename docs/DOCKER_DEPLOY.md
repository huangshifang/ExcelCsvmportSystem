# Excel 导入系统 — Docker 部署实战指南

> 基于 2026-05-07 实际部署调试过程编写，涵盖从零到成功运行的全过程。

---

## 一、环境说明

| 项目 | 值 |
|---|---|
| OS | Windows 11 Home China |
| Docker | 29.2.1 (Docker Desktop + WSL2) |
| 后端 | .NET 10.0.0-preview.3 / ASP.NET Core |
| 前端 | React 19 + TypeScript + Vite + Ant Design |
| 数据库 | SQL Server 2022（宿主机，非容器） |
| NuGet 私源 | Azure DevOps dotnet10 preview feed |

---

## 二、最终架构

```
浏览器 → localhost:80 (Nginx)
              ├─ /          → React SPA 静态文件
              └─ /api/*     → proxy_pass → api:5000
                                            └─ → host.docker.internal:1433 (宿主机 SQL Server)
```

- **API 暴露端口**：5000（调试用，生产建议隐藏）
- **前端端口**：80
- **数据库连接串**：`Server=host.docker.internal;Database=ExcelImportDb;User ID=excel_user;Password=sh.12345;...`

---

## 三、快速部署

```bash
# 1. 复制环境变量
cp .env.docker .env

# 2. 构建并启动（仅 API + 前端，不含数据库容器）
docker compose up -d --build

# 3. 验证
curl http://localhost:5000/api/diagnostics/health
# → {"status":"healthy","database":{"connected":true}}

# 4. 浏览器访问 http://localhost
# 默认登录：admin / admin123
```

---

## 四、遇到的问题及解决方案

### 问题 1：Docker Desktop 未启动

**现象**：
```
Cannot connect to the Docker daemon at npipe:////./pipe/dockerDesktopLinuxEngine
```

**解决**：启动 Docker Desktop
```bash
start "" "C:/Program Files/Docker/Docker/Docker Desktop.exe"
# 等待约 30 秒
```

---

### 问题 2：Docker Hub 不可访问

**现象**：`docker pull` 超时，`registry-1.docker.io` 无法连接。

**原因**：国内网络限制，Docker Hub 被墙。

**解决**：在 `C:\Users\<用户名>\.docker\daemon.json` 中配置镜像加速器：
```json
{
  "registry-mirrors": [
    "https://docker.1ms.run",
    "https://docker.m.daocloud.io",
    "https://docker.xuanyuan.me"
  ]
}
```

> `mcr.microsoft.com`（Microsoft Container Registry）无需加速器，国内可直接访问。

---

### 问题 3：.NET 10 Preview 镜像标签确认

**现象**：担心 `mcr.microsoft.com/dotnet/nightly/sdk:10.0-preview` 标签不存在。

**排查**：
```bash
curl -s "https://mcr.microsoft.com/v2/dotnet/nightly/sdk/tags/list" | python3 -c "import sys,json; [print(t) for t in json.load(sys.stdin)['tags'] if '10.0' in t]" | head
```

**结论**：`10.0-preview` 标签存在，可直接使用。若标签失效，可选择具体版本号标签如 `10.0.100-preview.7-azurelinux3.0-amd64`。

---

### 问题 4：前端 TypeScript 编译失败（4 个错误）

**现象**：`npm run build` 执行 `tsc -b` 时报类型错误。

**原因**：Docker 构建使用 `tsc -b` 全量类型检查，比 Vite 开发模式（esbuild 转译，不做类型检查）更严格。

**具体错误及修复**：

| 文件 | 错误 | 修复 |
|---|---|---|
| `AuthContext.tsx:61` | `UserInfo` 缺失 `authType` 字段 | 回退对象补充 `authType: 'Local'` |
| `AuthContext.tsx:3` | `LoginResponse` 导入未使用 | 移除未使用的导入 |
| `ImportPage.tsx:9` | `UploadOutlined` 导入未使用 | 移除未使用的图标导入 |
| `ImportPage.tsx:96,139` | `UploadFile` 强转 `Blob` 类型不兼容 | `fileList[0] as Blob` → `fileList[0].originFileObj as Blob` |

**教训**：应将 `tsc --noEmit` 加入 CI pipeline 或 pre-commit hook。

---

### 问题 5：`dotnet publish --no-restore` 失败

**现象**：
```
NuGet.Packaging.Core.PackagingException: Unable to find fallback package folder
'C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages'
```

**原因**：Dockerfile 分步执行 `dotnet restore`（生成 assets.json）再 `dotnet publish --no-restore`。assets.json 在 Windows 宿主机上生成时引用了 VS NuGet 回退文件夹路径，Linux 容器中该路径不存在。

**修复**：移除 `--no-restore`，让 publish 在容器内重新 restore：

```dockerfile
# 修改前
RUN dotnet publish ... -c Release -o /app --no-restore

# 修改后
RUN dotnet publish ... -c Release -o /app
```

> 保留显式 `dotnet restore` 步骤以利用 Docker 层缓存加速后续构建。

---

### 问题 6：API 绑定 `localhost` 导致容器外无法访问

**现象**：
```bash
$ curl http://localhost:5000/api/diagnostics/health
curl: (52) Empty reply from server
```
但 `docker logs` 显示应用已启动且数据库连接成功。

**原因**：`Program.cs` 第 19 行：
```csharp
builder.WebHost.UseUrls("http://localhost:5000");
```
`localhost` 在容器内部仅绑定 loopback 接口（`127.0.0.1`）。Docker 端口映射将外部流量转发到容器内部 IP，无法到达 loopback 地址。

**修复**：改为监听所有接口：
```csharp
builder.WebHost.UseUrls("http://+:5000");
```

---

### 问题 7：SQL Server 容器镜像无法拉取

**现象**：`mcr.microsoft.com/mssql/server:2022-latest`（约 1.5 GB）下载中断：
```
short read: expected 490183101 bytes but got 95830016: unexpected EOF
```

**原因**：镜像巨大，国内网络不稳定导致下载中断且层完整性校验失败。

**决策**：**放弃 SQL Server 容器**，改用宿主机数据库。

**docker-compose.yml 改动**：
- 移除 `db` 服务及其 `depends_on`、`healthcheck`、`volume`
- API 连接串改为 `Server=host.docker.internal;...`
- `host.docker.internal` 是 Docker Desktop 提供的特殊 DNS，指向宿主机

---

### 问题 8：数据库权限导致表查询失败（Error 916）

**现象**：选择数据库后报 "One or more validation errors occurred"。

**根因**：`GetDatabases()` 的 SQL 列出所有在线数据库，但部分库 `excel_user` 无权访问。用户选择无权库后，`GetTablesAsync` 抛出 `SqlException(916)`。

**修复**（`TableService.cs`）：
```sql
-- 原 SQL：列出所有在线库
SELECT name FROM sys.databases WHERE state = 0

-- 修复后：只列出当前登录用户有权访问的库
SELECT d.name FROM sys.databases d
WHERE d.state = 0 AND HAS_DBACCESS(d.name) = 1
```

同时修复 `TableController.cs`：参数改为 `string?` 可空类型 + 显式空值校验 + SQL 异常捕获返回友好错误。

---

### 问题 9：前端文件上传 `originFileObj` 为 `undefined`

**现象**：选择数据库、表、文件后点击"下一步"，报 "One or more validation errors occurred."。

**根因**：`ImportPage.tsx` 的 `beforeUpload` 回调：
```typescript
// 错误写法：file 是浏览器 File 对象，不是 antd UploadFile 结构
beforeUpload={(file) => {
    setFileList([file as unknown as UploadFile]);
    return false;
}}
```

`file` 是原生 `File` 对象，没有 `originFileObj` 属性。后续 `handlePreview` 中：
```typescript
formData.append('file', fileList[0].originFileObj as Blob);
// originFileObj 是 undefined → FormData file 字段变为字符串 "undefined"
```

后端 `[FromForm] ImportRequestDto` 无法将 `"undefined"` 绑定到 `IFormFile File`，触发 `[ApiController]` 自动模型校验 → 400 "One or more validation errors occurred"。

**修复**：
```typescript
// 正确写法：显式构造 UploadFile 结构
beforeUpload={(file) => {
    setFileList([{ uid: file.name, name: file.name, originFileObj: file } as UploadFile]);
    return false;
}}
```

---

## 五、完整修改文件清单

| 文件 | 修改内容 |
|---|---|
| `src/ExcelImportSystem.API/Program.cs` | `http://localhost:5000` → `http://+:5000` |
| `src/ExcelImportSystem.API/Controllers/TableController.cs` | 参数可空 + 显式校验 + SQL 异常处理 |
| `src/ExcelImportSystem.Infrastructure/Services/TableService.cs` | 数据库列表加入 `HAS_DBACCESS` 过滤 |
| `src/Dockerfile` | 移除 `--no-restore` |
| `docker-compose.yml` | 移除 `db` 服务，连接串指向宿主机 |
| `frontend/src/pages/Import/ImportPage.tsx` | `beforeUpload` 正确设置 `originFileObj`；移除未用导入；`as Blob` → `.originFileObj as Blob` |
| `frontend/src/context/AuthContext.tsx` | 补充 `authType` 字段；移除未用导入 |

---

## 六、常用命令速查

```bash
# 查看状态
docker compose ps

# 查看日志
docker compose logs -f api
docker compose logs -f frontend

# 代码修改后重新部署
docker compose up -d --build

# 停止
docker compose down

# 彻底清理（含构建缓存）
docker compose down --rmi all
```

---

## 七、检查清单

部署前后确认以下各项：

- [ ] Docker Desktop 已启动
- [ ] 宿主 SQL Server 允许 SQL 身份验证（excel_user / sh.12345）
- [ ] 宿主 SQL Server 已创建 `ExcelImportDb` 数据库
- [ ] `.env` 已从 `.env.docker` 复制（或环境变量已设置）
- [ ] `docker compose ps` 显示两个服务均为 Up
- [ ] `curl localhost:5000/api/diagnostics/health` 返回 `healthy`
- [ ] 浏览器访问 `http://localhost` 可打开登录页
- [ ] `admin / admin123` 可成功登录
- [ ] 导入页面可列出数据库和表
- [ ] 上传 Excel 文件可预览并导入
