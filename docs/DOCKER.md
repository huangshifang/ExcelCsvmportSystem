# Excel 导入系统 — Docker 容器化部署指南

---

## 架构设计

```
docker-compose up
    │
    ├── db (SQL Server 2022)
    │   └── 端口 1433，数据持久化到 volume
    │
    ├── api (ASP.NET Core Backend)
    │   └── 端口 5000，连接 db 服务
    │
    └── frontend (Nginx + React)
        └── 端口 80，反向代理 /api → api:5000
```

**访问方式**：浏览器打开 `http://localhost` 即可使用完整系统，Nginx 自动将 `/api/*` 请求转发到后端。

```
浏览器 → localhost:80 (Nginx)
              ├─ /          → React 静态文件 (SPA)
              └─ /api/*     → proxy_pass → api:5000 (ASP.NET Core)
                                              └─ → db:1433 (SQL Server)
```

---

## 文件清单（8 个新增文件，0 个修改）

| 文件 | 位置 | 用途 |
|------|------|------|
| `nuget.config` | `src/` | .NET 10 Preview NuGet 包源配置 |
| `Dockerfile` | `src/` | 后端多阶段构建镜像 |
| `Dockerfile` | `frontend/` | 前端多阶段构建镜像 |
| `nginx.conf` | `frontend/` | Nginx 反向代理配置 |
| `.env.production` | `frontend/` | Vite 生产构建环境变量 |
| `docker-compose.yml` | 项目根 | 三服务编排定义 |
| `.env.docker` | 项目根 | 环境变量模板 |
| `.dockerignore` | 项目根 | Docker 构建排除规则 |

---

## 文件内容详解

### 1. `src/nuget.config` — NuGet 预览源

项目使用 .NET 10 Preview，需要额外的 NuGet 源来解析预览版包：

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="dotnet10" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet10/nuget/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
```

### 2. `src/Dockerfile` — 后端镜像

```dockerfile
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/nightly/sdk:10.0-preview AS build
WORKDIR /src
COPY nuget.config ./
COPY ExcelImportSystem.Core/*.csproj ExcelImportSystem.Core/
COPY ExcelImportSystem.Infrastructure/*.csproj ExcelImportSystem.Infrastructure/
COPY ExcelImportSystem.API/*.csproj ExcelImportSystem.API/
RUN dotnet restore ExcelImportSystem.API/ExcelImportSystem.API.csproj
COPY . .
RUN dotnet publish ExcelImportSystem.API/ExcelImportSystem.API.csproj \
    -c Release -o /app --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/nightly/aspnet:10.0-preview AS runtime
WORKDIR /app
RUN mkdir -p /app/logs
COPY --from=build /app .
EXPOSE 5000
ENTRYPOINT ["dotnet", "ExcelImportSystem.API.dll"]
```

**说明**：
- 多阶段构建，最终镜像只含运行时，体积小
- 先复制 `.csproj` 再 `restore`，利用 Docker 层缓存加速重复构建
- `EXPOSE 5000` 与 `Program.cs` 中 `UseUrls("http://localhost:5000")` 一致

### 3. `frontend/Dockerfile` — 前端镜像

```dockerfile
# Stage 1: Build
FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

# Stage 2: Serve
FROM nginx:alpine AS serve
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
```

**说明**：
- `npm ci` 而非 `npm install`，确保依赖版本锁定
- 构建时自动读取 `.env.production`（Vite 默认行为）
- 最终镜像只有 Nginx + 静态文件，体积约 20MB

### 4. `frontend/nginx.conf` — Nginx 配置

```nginx
server {
    listen 80;
    server_name _;

    root /usr/share/nginx/html;
    index index.html;

    # Gzip 压缩
    gzip on;
    gzip_types text/plain text/css application/json application/javascript
               text/xml application/xml text/javascript image/svg+xml;
    gzip_min_length 1024;
    gzip_proxied any;

    # SPA 路由 — 所有非文件请求回退到 index.html
    location / {
        try_files $uri $uri/ /index.html;
    }

    # API 反向代理到后端
    location /api {
        proxy_pass http://api:5000;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # 导入大文件超时设置
        proxy_read_timeout 300s;
        proxy_send_timeout 300s;
        proxy_connect_timeout 60s;

        # 上传文件大小限制
        client_max_body_size 100m;
    }

    # 静态资源强缓存
    location /assets {
        expires 1y;
        add_header Cache-Control "public, immutable";
    }
}
```

**关键配置说明**：
- `try_files $uri /index.html` — React SPA 路由必须的配置，所有非文件路径都返回 `index.html`
- `proxy_pass http://api:5000` — `api` 是 docker-compose 中定义的服务名，Docker DNS 自动解析
- `client_max_body_size 100m` — 允许上传大 Excel 文件
- `proxy_read_timeout 300s` — 大数据量导入可能耗时较长

### 5. `frontend/.env.production` — 前端生产变量

```env
VITE_API_URL=/api
```

**说明**：Vite 在 `npm run build` 时自动读取此文件。设为 `/api` 后，前端所有 API 请求都发到同源的 `/api/*`，由 Nginx 代理到后端。这避免了跨域问题和硬编码后端地址。

### 6. `docker-compose.yml` — 服务编排

```yaml
services:
  db:
    image: mcr.microsoft.com/mssql/server:2022-latest
    restart: unless-stopped
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: ${SA_PASSWORD}
    ports:
      - "1433:1433"
    volumes:
      - sqldata:/var/opt/mssql
    healthcheck:
      test: /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$${SA_PASSWORD}" -Q "SELECT 1" -C
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 30s
    networks:
      - app-network

  api:
    build:
      context: ./src
      dockerfile: Dockerfile
    restart: unless-stopped
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://+:5000
      - ConnectionStrings__DefaultConnection=Server=db;Database=ExcelImportDb;User ID=sa;Password=${SA_PASSWORD};TrustServerCertificate=True;MultipleActiveResultSets=true
      - Jwt__Key=${JWT_KEY}
      - Jwt__Issuer=ExcelImportSystem
      - Jwt__Audience=ExcelImportSystem
      - Jwt__ExpireHours=24
      - Ldap__Enabled=${LDAP_ENABLED:-false}
    depends_on:
      db:
        condition: service_healthy
    networks:
      - app-network

  frontend:
    build:
      context: ./frontend
      dockerfile: Dockerfile
    restart: unless-stopped
    ports:
      - "80:80"
    depends_on:
      - api
    networks:
      - app-network

volumes:
  sqldata:
    driver: local

networks:
  app-network:
    driver: bridge
```

**关键设计**：

| 设计 | 说明 |
|------|------|
| `depends_on: condition: service_healthy` | API 等待 SQL Server 健康检查通过后才启动 |
| `$${SA_PASSWORD}` | 双美元符转义，让 docker-compose 解析变量而不是直接传给 shell |
| `restart: unless-stopped` | 容器崩溃自动重启，手动停止不重启 |
| `sqldata` volume | 数据库文件持久化，`docker-compose down` 后数据不丢失 |
| `app-network` bridge | 容器间通过服务名互相访问（`db`、`api`） |

### 7. `.env.docker` — 环境变量模板

```bash
# SQL Server SA 密码 (至少 8 位，含大小写字母+数字+符号)
SA_PASSWORD=YourStrong!Passw0rd

# JWT 签名密钥 (至少 32 字符，生产环境请更换)
JWT_KEY=YourSuperSecretKeyAtLeast32CharactersLong!

# LDAP/AD 认证 (默认关闭)
LDAP_ENABLED=false
```

### 8. `.dockerignore` — 构建排除

```
**/bin/
**/obj/
**/logs/
**/node_modules/
**/dist/
.git/
.vs/
.vscode/
.env
docs/
```

排除这些目录可以显著减小 Docker 构建上下文，加速构建。

---

## 配置外部化机制

ASP.NET Core 支持通过环境变量覆盖 `appsettings.json` 中的配置。冒号 `:` 映射为双下划线 `__`：

| appsettings.json 路径 | 环境变量 |
|------------------------|----------|
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` |
| `Jwt:Key` | `Jwt__Key` |
| `Ldap:Enabled` | `Ldap__Enabled` |

docker-compose.yml 中已在 `api` 服务的 `environment` 部分设置了这些变量。

---

## 部署步骤

### 前置要求

- Docker Engine 20.10+
- Docker Compose v2
- 至少 4GB 可用内存（SQL Server 要求 2GB+）

### 一键部署

```bash
# 1. 进入项目目录
cd ExcelImportSystem

# 2. 复制并配置环境变量
cp .env.docker .env
# 可选：编辑 .env 修改 SA 密码和 JWT 密钥

# 3. 构建并启动所有服务
docker-compose up -d

# 4. 查看启动日志
docker-compose logs -f

# 5. 浏览器访问
# http://localhost
# 默认账号: admin / admin123
```

### 常用管理命令

```bash
# 查看运行状态
docker-compose ps

# 查看所有日志
docker-compose logs -f

# 查看单个服务日志
docker-compose logs -f api
docker-compose logs -f frontend

# 重启某个服务
docker-compose restart api

# 停止所有服务
docker-compose down

# 停止并删除数据卷（重置数据库）
docker-compose down -v

# 重新构建并启动（代码修改后）
docker-compose up -d --build
```

---

## 关键技术决策

### 1. 为什么用 Nginx 反向代理而不是直接暴露后端？

| 方案 | 优点 | 缺点 |
|------|------|------|
| **Nginx 代理（采用）** | 无跨域问题，单端口访问，生产级静态文件服务 | 多一层代理 |
| 直接暴露后端 + CORS | 简单 | 跨域配置复杂，暴露多端口 |

### 2. 为什么使用 .NET 10 Preview 镜像？

项目使用 .NET 10 preview NuGet 包，编译产物依赖 .NET 10 运行时，必须使用匹配的预览镜像。.NET 10 正式发布后，可切换为稳定镜像：
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
```

### 3. 为什么用 `depends_on` + `healthcheck` 而不是 `wait-for-it`？

Docker Compose v2 原生支持 `condition: service_healthy`，无需额外脚本。SQL Server 容器内置 `sqlcmd`，可直接用于健康检查。

---

## 潜在问题与解决方案

### 问题 1：.NET 10 Preview 镜像不可用

**现象**：`docker build` 报错 `manifest for mcr.microsoft.com/dotnet/nightly/sdk:10.0-preview not found`

**解决**：
- 检查 [Microsoft 夜间镜像状态](https://github.com/dotnet/dotnet-docker/blob/nightly/README.md)
- 尝试不同 tag（如 `10.0.0-preview.3`）
- 降级项目到 .NET 9（需修改所有 `.csproj` 的 `TargetFramework`）

### 问题 2：NuGet 预览源无法访问

**现象**：`dotnet restore` 失败，找不到预览版包

**解决**：
- 更新 `nuget.config` 中的 `dotnet10` 源 URL
- 检查是否有 VPN/代理阻止 Azure DevOps 访问
- 备选源：在本地 `dotnet restore` 后，将 `~/.nuget/packages` 复制到镜像

### 问题 3：SQL Server 启动慢

**现象**：`api` 服务启动时连不上数据库

**解决**：
- 已配置 `healthcheck` + `depends_on: condition: service_healthy`，确保等数据库就绪
- 首次启动约 30-60 秒（SA 密码设置 + 系统数据库初始化）
- 增加 `start_period` 和 `retries` 参数

### 问题 4：ARM 架构 Mac（M1/M2/M3）

**现象**：SQL Server 2022 官方镜像不支持 ARM

**解决**：
- 使用 Azure SQL Edge：`image: mcr.microsoft.com/azure-sql-edge`
- 或通过 Rosetta 模拟运行 x86 镜像（性能差）

---

## 生产环境检查清单

部署到生产环境前，请确认：

- [ ] 修改 `.env` 中所有默认密码和密钥
- [ ] `SA_PASSWORD` 使用强密码（16 位以上，大小写+数字+符号）
- [ ] `JWT_KEY` 使用随机生成的长密钥（64 字符以上）
- [ ] 配置 SSL/HTTPS（建议使用反向代理如 Caddy 或 Traefik）
- [ ] 限制 SQL Server 端口 1433 仅内部访问（移除 `ports` 映射）
- [ ] 配置日志收集（Serilog → Elasticsearch / Seq 等）
- [ ] 设置 `ASPNETCORE_ENVIRONMENT=Production`
- [ ] 配置 LDAP（如需 AD 认证）
- [ ] 备份 SQL Server 数据卷
