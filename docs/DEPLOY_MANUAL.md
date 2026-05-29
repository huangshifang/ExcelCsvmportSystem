# Excel 导入系统 — Docker 部署操作手册

> 适用对象：初次接触 Docker 的实施人员  
> 预计阅读时间：15 分钟  
> 部署耗时：首次约 10-20 分钟（视网络速度），后续更新约 2-3 分钟

---

## 目录

1. [准备工作](#一准备工作)
2. [安装 Docker](#二安装-docker)
3. [配置 SQL Server 数据库](#三配置-sql-server-数据库)
4. [获取项目代码](#四获取项目代码)
5. [修改配置文件](#五修改配置文件)
6. [构建并启动](#六构建并启动)
7. [验证部署](#七验证部署)
8. [日常运维](#八日常运维)
9. [常见问题](#九常见问题)

---

## 一、准备工作

在开始部署之前，请确认以下信息（向系统管理员或开发人员索取）：

| 确认项 | 说明 | 示例 |
|---|---|---|
| SQL Server 服务器地址 | 数据库所在的机器 IP 或主机名 | `192.168.1.100` 或 `localhost` |
| SQL Server 端口 | 默认 1433 | `1433` |
| 数据库登录用户名 | SQL 身份验证的账号 | `excel_user` |
| 数据库登录密码 | 对应的密码 | `sh.12345` |
| 目标数据库名 | 已创建的数据库（应用会自动建表） | `ExcelImportDb` |
| 服务器操作系统 | Windows / Linux | Windows Server 2019+ 或 Linux |

> 如果 SQL Server 和本应用部署在同一台服务器上，地址填 `host.docker.internal`（Windows）或 `172.17.0.1`（Linux）。

---

## 二、安装 Docker

### 2.1 Windows 环境

**系统要求**：Windows 10 专业版/企业版 2004+ 或 Windows 11，启用虚拟化。

**步骤**：

1. 打开浏览器，访问 https://www.docker.com/products/docker-desktop/
2. 点击 **Download for Windows**，下载安装包（约 600 MB）
3. 双击运行安装包，全部默认选项，点击 **OK** 完成安装
4. 安装完成后**重启电脑**
5. 重启后 Docker Desktop 会自动启动，屏幕右下角系统托盘出现鲸鱼图标
6. 等待鲸鱼图标稳定（不再转圈），表示 Docker 已就绪

**配置国内镜像加速**（重要，否则拉取镜像极慢）：

1. 打开 Docker Desktop，点击右上角齿轮图标（设置）
2. 左侧选择 **Docker Engine**
3. 在 `{}` 中添加 `registry-mirrors`，结果如下：

```json
{
  "registry-mirrors": [
    "https://docker.1ms.run",
    "https://docker.m.daocloud.io",
    "https://docker.xuanyuan.me"
  ]
}
```

4. 点击 **Apply & restart**，等待重启完成

**验证安装**：打开 PowerShell 或 CMD，执行：

```bash
docker --version
# 应输出类似：Docker version 29.2.1
```

### 2.2 Linux 环境（CentOS / Ubuntu）

```bash
# Ubuntu / Debian
curl -fsSL https://get.docker.com | bash
sudo usermod -aG docker $USER
# 退出重新登录使权限生效

# 验证
docker --version
```

> Linux 下 Docker Hub 同样需要镜像加速。编辑 `/etc/docker/daemon.json`，添加上述 `registry-mirrors`。

---

## 三、配置 SQL Server 数据库

### 3.1 创建登录账号（如尚未创建）

用 SQL Server Management Studio (SSMS) 或 sqlcmd 连接数据库服务器，**使用 sa 管理员账号**执行：

```sql
-- 创建登录账号
CREATE LOGIN excel_user WITH PASSWORD = 'sh.12345';
GO

-- 创建数据库
CREATE DATABASE ExcelImportDb;
GO

-- 将 excel_user 设为目标数据库的 owner
USE ExcelImportDb;
CREATE USER excel_user FOR LOGIN excel_user;
ALTER ROLE db_owner ADD MEMBER excel_user;
GO
```

> 如果数据库已存在，只需执行 `USE ExcelImportDb; CREATE USER...` 部分。

### 3.2 启用 SQL Server 身份验证

确认 SQL Server 允许 SQL 身份验证（混合模式）：

1. 打开 SSMS，连接服务器
2. 右键服务器 → **属性** → **安全性**
3. 确认选择 **SQL Server 和 Windows 身份验证模式**

### 3.3 确认端口和防火墙

- SQL Server 默认监听 **1433** 端口
- 如果是远程服务器，确保防火墙开放 1433 端口
- 如果是同一台机器部署，无需额外配置

### 3.4 验证数据库连接

在部署前先用 SSMS 测试连接是否正常：

- 服务器：`192.168.1.100`（或实际 IP）
- 身份验证：SQL Server 身份验证
- 用户名：`excel_user`
- 密码：`sh.12345`

---

## 四、获取项目代码

### 方式一：从 Git 仓库克隆

```bash
git clone <你的仓库地址>
cd ExcelImportSystem
```

### 方式二：拷贝压缩包

1. 将项目文件夹 `ExcelImportSystem` 完整拷贝到部署服务器
2. 打开终端，进入该文件夹

```
cd D:\Deploy\ExcelImportSystem
```

> 终端（PowerShell / CMD）中的路径因实际存放位置而异，请自行调整。

---

## 五、修改配置文件

进入项目目录后，按以下步骤修改配置。

### 5.1 创建环境变量文件

项目根目录下有 `.env.docker` 模板文件，复制为 `.env`：

```bash
# Windows (PowerShell / CMD)
copy .env.docker .env

# Linux / macOS
cp .env.docker .env
```

### 5.2 编辑 `.env` 文件

用记事本打开 `.env` 文件，修改以下内容：

```ini
# JWT 密钥 — 务必改为随机字符串！越长越安全
# 可用 https://www.random.org/strings/ 在线生成
JWT_KEY=请替换为至少32位的随机字符串

# LDAP 认证 — 默认关闭，如有 AD 域控改为 true
LDAP_ENABLED=false
```

> `SA_PASSWORD` 变量已不需要（SQL Server 不在容器中），可忽略。

### 5.3 修改数据库连接（如需）

如果你的 SQL Server 与以下默认值不同，需要修改 `docker-compose.yml`：

```yaml
# 找到 api 服务的这行配置：
- ConnectionStrings__DefaultConnection=Server=host.docker.internal;Database=ExcelImportDb;User ID=excel_user;Password=sh.12345;TrustServerCertificate=True;MultipleActiveResultSets=true
```

**按实际情况修改以下参数**：

| 参数 | 说明 | 示例 |
|---|---|---|
| `Server=` | SQL Server 地址 | 同机：`host.docker.internal`（Win）/ `172.17.0.1`（Linux）；远程：`192.168.1.100` |
| `Database=` | 数据库名 | `ExcelImportDb` |
| `User ID=` | SQL 登录用户名 | `excel_user` |
| `Password=` | SQL 登录密码 | `sh.12345` |

> `TrustServerCertificate=True` 表示跳过 SSL 证书验证，内网部署无需修改。

### 5.4 关键注意事项

- **绝对不要用记事本保存为 UTF-8 BOM 格式**。如出现奇怪报错，用 VS Code 或 Notepad++ 另存为 UTF-8（无 BOM）
- `.env` 文件包含密码，不要上传到 Git 仓库
- 连接串中如果密码包含 `;` `{` `}` 等特殊字符，需用单引号包裹整个连接串

---

## 六、构建并启动

### 6.1 首次构建

在项目根目录下执行：

```bash
docker compose up -d --build
```

**首次构建耗时说明**：
- 下载 .NET SDK 镜像（约 800 MB）：3-5 分钟
- 下载 Node.js 镜像（约 130 MB）：1-2 分钟
- 下载 Nginx 镜像（约 50 MB）：30 秒
- 下载 NuGet 包：2-3 分钟
- 下载 npm 包：1-2 分钟
- 编译后端 + 前端：1-2 分钟

> 总计约 **10-20 分钟**，具体取决于网络速度。后续更新只需 2-3 分钟。

看到以下输出表示成功：

```
✔ Image excelimportsystem-api Built
✔ Image excelimportsystem-frontend Built
✔ Container excelimportsystem-api-1 Started
✔ Container excelimportsystem-frontend-1 Started
```

### 6.2 检查运行状态

```bash
docker compose ps
```

应显示两个容器，Status 均为 **Up**：

```
NAME                           STATUS          PORTS
excelimportsystem-api-1        Up 10 seconds   0.0.0.0:5001->5000/tcp
excelimportsystem-frontend-1   Up 10 seconds   0.0.0.0:80->80/tcp
```

### 6.3 查看日志

```bash
# 查看所有日志（Ctrl+C 退出）
docker compose logs -f

# 只看后端日志
docker compose logs -f api

# 只看最近 50 行
docker compose logs --tail 50 api
```

---

## 七、验证部署

### 7.1 检查 API 健康状态

浏览器访问：`http://localhost:5001/api/diagnostics/health`

应返回类似：
```json
{"status":"healthy","database":{"connected":true,"userCount":2},"...":"..."}
```

> 如果 `database.connected` 为 `false`，说明数据库连接失败，请检查第六节中的连接串配置。

### 7.2 访问前端

浏览器访问：`http://localhost`

应看到系统登录页面。使用默认账号登录：

- **用户名**：`admin`
- **密码**：`admin123`

### 7.3 测试数据导入功能

1. 登录后，点击左侧菜单「数据导入」
2. 点击「数据库」下拉框，应列出可访问的 SQL Server 数据库
3. 选择一个数据库，再选择一张目标表
4. 上传一个 Excel 文件（.xlsx / .xls）或 CSV 文件（.csv / .tsv / .txt）
5. 文件的第一行建议为列标题，与数据库表列名匹配
6. 点击「下一步」预览映射
7. 确认列映射正确后，点击「执行导入」

> **提示**：如果 Excel 文件有多个工作表，系统会自动跳过空工作表并读取第一个有数据的工作表。CSV 文件如无表头行，请关闭「包含表头」开关。

---

## 八、日常运维

### 8.1 启动/停止

```bash
# 启动
docker compose up -d

# 停止（保留容器和数据）
docker compose stop

# 停止并删除容器（不影响数据，下次 up 重新创建）
docker compose down
```

### 8.2 代码更新后重新部署

```bash
# 拉取最新代码
git pull

# 重新构建并启动
docker compose up -d --build
```

### 8.3 查看日志排查问题

```bash
# 实时查看 API 日志
docker compose logs -f api

# 查看最近 100 行
docker compose logs --tail 100

# 只看错误
docker compose logs 2>&1 | grep -i "err\|fail\|exception"
```

### 8.4 进入容器内部

```bash
# 进入 API 容器
docker compose exec api sh

# 进入前端容器
docker compose exec frontend sh
```

### 8.5 备份应用数据

应用本身的数据（用户、角色、导入日志）存储在连接的 SQL Server 数据库中。备份该 SQL Server 数据库即可。

```sql
-- 在 SQL Server 上执行完整备份
BACKUP DATABASE ExcelImportDb TO DISK = 'D:\Backup\ExcelImportDb.bak';
```

### 8.6 查看资源占用

```bash
docker stats
```

### 8.7 更换默认管理员密码

首次登录后，建议立即修改默认密码：

1. 登录系统
2. 左侧菜单 →「用户管理」
3. 找到 admin 用户，修改密码

---

## 九、常见问题

### 9.1 端口冲突

**现象**：启动报错 `port is already allocated` 或 `bind: address already in use`

**解决**：80 或 5001 端口被其他程序占用。修改 `docker-compose.yml` 中的端口映射：

```yaml
# 示例：改为 8081 和 5002
ports:
  - "8081:80"      # 浏览器访问 http://localhost:8081
  - "5002:5000"    # API 端口
```

### 9.2 数据库连接失败

**现象**：API 健康检查返回 `database.connected: false`

**排查步骤**：

1. **确认 SQL Server 在运行**：打开 SSMS，尝试连接
2. **确认连接串参数正确**：用户名、密码、数据库名是否拼写正确
3. **确认防火墙**：服务器的 1433 端口是否开放
4. **确认 SQL 身份验证**：SQL Server 是否允许 SQL 账号登录（见第三节）
5. **确认账号权限**：`excel_user` 是否有 `ExcelImportDb` 数据库的访问权限

### 9.3 首次构建失败

**现象**：`docker compose up -d --build` 中途报错退出

**常见原因及解决**：

| 错误现象 | 解决 |
|---|---|
| `error pulling image` / `connection refused` | 检查镜像加速器是否配置正确（见 2.1） |
| `npm ERR! network` | npm 安装超时，重试 `docker compose up -d --build` |
| `error MSB4018`（NuGet 相关） | 项目使用的 .NET 10 Preview，需确保 Dockerfile 中 restore 后可正常 publish（已修复） |

### 9.4 如何切换到生产模式

将 `docker-compose.yml` 中 `api` 服务的环境变量改为：

```yaml
- ASPNETCORE_ENVIRONMENT=Production
```

并确保：
- 移除 `ports: "5001:5000"`（不对外暴露 API 端口，仅前端 80/443 访问）
- 使用强密码和随机 JWT 密钥
- 如用外部反向代理（Nginx / Caddy），配置 HTTPS

### 9.5 如何在多台机器部署同一套系统

1. 在每台机器上按本手册步骤 1-6 执行
2. 所有机器指向**同一台 SQL Server 数据库**
3. 所有机器使用**相同的 JWT_KEY**（在 `.env` 中设置）

### 9.6 如何更新 JWT 密钥

1. 编辑 `.env` 文件，修改 `JWT_KEY`
2. 重新部署：`docker compose up -d`
3. 注意：更换密钥后所有已登录用户需要重新登录

---

## 附录 A：项目文件结构

```
ExcelImportSystem/
├── docker-compose.yml        # Docker 服务编排（部署时可能需要修改连接串）
├── .env.docker               # 环境变量模板
├── .env                      # 实际环境变量（从模板复制，需修改 JWT_KEY）
├── src/                      # 后端 .NET 源码
│   └── Dockerfile            # 后端构建配置
├── frontend/                 # 前端 React 源码
│   └── Dockerfile            # 前端构建配置
└── docs/                     # 文档
```

## 附录 B：环境变量说明

| 变量 | 默认值 | 说明 |
|---|---|---|
| `JWT_KEY` | (需自行设置) | JWT Token 签名密钥，至少 32 字符 |
| `LDAP_ENABLED` | `false` | 是否启用 AD/LDAP 域认证 |

---

> 如遇到本手册未涵盖的问题，请联系开发团队并提供 `docker compose logs` 的输出日志。
