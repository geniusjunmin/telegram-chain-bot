# Telegram Chain Bot (.NET 8)

一个可直接运行的 Telegram 接龙 Bot 项目，包含：
- ASP.NET Minimal API
- Telegram Bot Webhook + Inline Keyboard + WebApp
- SQLite 数据存储（结构可扩展为 Redis）
- Docker / Docker Compose 一键启动

## 1. 项目结构

```text
telegram-chain-bot
├─ src
│  ├─ Api
│  │  ├─ Program.cs
│  │  └─ ChainController.cs
│  ├─ Bot
│  │  ├─ BotService.cs
│  │  └─ UpdateHandler.cs
│  ├─ Database
│  │  ├─ Models
│  │  │  ├─ Chain.cs
│  │  │  └─ ChainMember.cs
│  │  └─ AppDbContext.cs
│  ├─ Options
│  │  └─ BotOptions.cs
│  ├─ Services
│  │  ├─ BotSecurityService.cs
│  │  ├─ ChainService.cs
│  │  └─ TelegramService.cs
│  └─ TelegramChainBot.csproj
├─ webapp
│  ├─ index.html
│  ├─ app.js
│  └─ style.css
├─ docker
│  ├─ Dockerfile
│  └─ docker-compose.yml
├─ data
└─ README.md
```

## 2. 创建 Telegram Bot

1. 在 Telegram 搜索 `@BotFather`
2. 发送 `/newbot` 并按提示创建
3. 获取 `BOT_TOKEN`

## 3. 环境变量

在 `telegram-chain-bot` 根目录创建 `.env`：

```env
BOT_TOKEN=123456:ABCDEF...
WEBHOOK_BASE_URL=https://your-domain-or-ngrok
```

`WEBHOOK_BASE_URL` 必须是 Telegram 可访问的公网 HTTPS 地址。

## 4. Webhook 配置

程序启动后会自动调用 `setWebhook`：
- Webhook URL: `{WEBHOOK_BASE_URL}/telegram/webhook/{BOT_TOKEN}`

你也可以手动验证：

```bash
curl https://api.telegram.org/bot<YOUR_BOT_TOKEN>/getWebhookInfo
```

## 5. Docker 运行

### 5.1 使用 Docker Compose (推荐)

在项目根目录下运行：

```bash
docker compose -f docker/docker-compose.yml up -d
```

### 5.2 使用 Docker Run 快速部署

如果你想直接从 Docker Hub 拉取镜像运行，可以使用以下命令：

```bash
docker run -d \
  --name telegram-chain-bot \
  --restart unless-stopped \
  -p 8080:8080 \
  -v $(pwd)/data:/app/data \
  -e BOT_TOKEN="你的_BOT_TOKEN" \
  -e WEBHOOK_BASE_URL="https://your-domain.com" \
  -e WEBHOOK_PATH="/telegram/webhook" \
  -e SQLITE_PATH="/app/data/chain.db" \
  <你的DOCKERHUB用户名>/telegram-chain-bot:latest
```

**参数说明：**
- `-v $(pwd)/data:/app/data`: 将本地 `data` 目录挂载到容器内，用于持久化存储 SQLite 数据库。
- `-e BOT_TOKEN`: 你的 Telegram Bot Token。
- `-e WEBHOOK_BASE_URL`: 你的公网 HTTPS 域名。
- `-p 8080:8080`: 映射端口。

## 6. 本地开发运行

```bash
cd src
dotnet restore
dotnet run
```

## 7. 使用方式

1. 在聊天中发送：
   - `/start_chain 聚餐接龙`
2. Bot 会发送接龙消息和按钮。
3. 用户点击 `参加接龙` 后，消息会实时刷新名单。
4. 也可点击 `打开 WebApp` 在 WebApp 页面中加入。

## 8. 安全说明

`/api/join` 通过请求头 `X-Telegram-Init-Data` 验证 Telegram WebApp 签名，防止伪造用户。
