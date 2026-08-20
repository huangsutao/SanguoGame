# SanguoGame

网络版战国类网页 SLG：玩家在大地图坐标建城，城池分为**城内 / 城墙 / 城外**；可建造或升级内政、科技、军事、城防与资源田，出兵掠夺地图据点或其他玩家，并包含 AI 玩家。

玩法按 **定时结算型 SLG** 落地：建造、行军是「下达指令 → 到点结算」，服务端权威，客户端只负责展示与发指令。第一版不做全图实时视野，也不做成即时 RTS。

## 当前进度

| 部分 | 状态 |
|------|------|
| 服务端骨架 | Server / Core / Infrastructure 已拆；统一 JSON 信封、CORS、`/hubs/game` 空壳 |
| 探活接口 | `GET /api/system/ping` |
| 账号 / 建城 | 注册登录、JWT、创角、随机空地建主城已落地（需 PostgreSQL） |
| 城内建筑 | 建造/升级、到点完成、Hangfire、SignalR `BuildComplete` |
| 城外资源田 | 四种田建造/升级、按时间现算出产、点收取入库 |
| 城墙 | 箭塔 / 城门 / 陷阱，与全城队列共用 |
| 军队 / 行军 | 征兵、出征 NPC / 玩家、到点结算、战报 |
| AI 玩家 | 启动补齐 AI 城；Hangfire tick 升级 / 征兵 / 出征 |
| 网页端 | `web/`：城池、城墙、出征、战报、Canvas 大地图、邮件、排行、联盟；升级与征兵展示消耗 |
| 其余 | 第一版数值已缩短建造 / 加大田产出；开发环境加快行军。实机联调见 [路线图](Docs/design-roadmap.md) |

## 仓库结构

```
SanguoGame/
├── README.md                      # 本文件：结构、技术栈、设计文档入口
├── Docs/                          # 详细设计文档（统一放这里）
├── SanguoGame.sln                 # 服务端解决方案
├── SanguoGame.Server/             # API + SignalR + 进程启动
├── SanguoGame.Core/               # 错误码、业务异常；后续纯规则
├── SanguoGame.Infrastructure/     # FreeSql + PostgreSQL；Hangfire 存储走同一库
└── web/                           # Vue 3 独立前端（npm）
```

第一版服务端保持 **单个 ASP.NET Core 进程**：HTTP、SignalR、定时任务都放在同一站点里，不要拆微服务。HTTP 与 SignalR 的 JSON 信封见 [统一协议](Docs/design-api.md)。

## 技术栈

### 已落地

| 层 | 技术 | 说明 |
|----|------|------|
| 服务端 | ASP.NET Core 9 Web API | `SanguoGame.Server` 宿主；Core / Infrastructure 类库 |
| 运行时 | .NET 9 | `TargetFramework: net9.0` |
| 网页端 | Vue 3 + TypeScript + Vite | `web/`：登录、创角、建城最小页 |

### 规划（第一版）

| 层 | 技术 | 用途 |
|----|------|------|
| 网页 UI | Vue 3 + TypeScript + Vite | 城内界面、建筑、科技、出征面板 |
| 大地图 | Canvas / PixiJS | 拖拽、缩放、城点、行军线（不用 DOM 画大量格子） |
| HTTP 客户端 | Axios 或 `fetch` | 登录、建城、升级、出兵等指令 |
| 实时推送客户端 | `@microsoft/signalr` | 建造完成、行军到达、被攻打 |
| API | ASP.NET Core Web API | 玩家主动指令，JSON + JWT |
| WebSocket | SignalR | 服务端主动推送；底层优先 WebSocket，可降级 |
| 数据 | FreeSql + PostgreSQL | 玩家、城池、建筑、行军 |
| 缓存 / 锁 | Redis | 在线状态、按城串行、防连点 |
| 定时任务 | Hangfire（或 `IHostedService`） | 建造完成、行军到达、资源结算、AI tick |
| 认证 | JWT + Refresh Token | HTTP 与 SignalR 共用同一套身份 |

人多以后再考虑 Orleans（每城一个 Grain）、分服与地图分片。第一版不上 gRPC、不上自建原生 WebSocket 协议。

选型说明见 [总体架构与技术选型](Docs/design-architecture.md)。

## 通讯方式

独立 Vue 工程与后端约定 URL，用 HTTP 发指令、用 SignalR 收推送：

```
浏览器
  ├─ 页面        →  Vue（开发期如 localhost:5173）
  ├─ HTTP        →  ASP.NET Core /api/...     下达指令
  └─ WebSocket   →  ASP.NET Core /hubs/game   接收结算结果
```

典型流程：玩家点「出兵」→ `POST /api/army/march` 立刻返回到达时间 → Hangfire 到点结算 → SignalR 推送 `MarchArrived`。

开发期推荐 Vite 把 `/api`、`/hubs` 代理到后端；上线推荐 Nginx 同域名反代。详细约定见 [前后端通讯设计](Docs/design-frontend-comm.md)。

## 玩法与开发顺序

城池模型：

```
玩家
 ├─ 账号 / 角色
 └─ 主城（地图坐标 x, y）
     ├─ 城内：内政 / 科技 / 军事建筑
     ├─ 城墙：箭塔、城门、陷阱
     └─ 城外：矿、木、田（可被掠夺）

世界
 ├─ 地图格子 / NPC 据点
 ├─ 其他玩家城
 ├─ 行军队列（A → B，到达时间）
 └─ AI 玩家（同一套结算，决策为服务端脚本）
```

建议实现顺序（施工清单、每步验收与基础设施接入见 [开发路线图](Docs/design-roadmap.md)）：

1. 账号 + 在地图随机空地建主城
2. 城内建筑：建造 / 升级 + 到点完成
3. 城外资源田：产出与收取
4. 城墙与基础城防
5. 出兵打 NPC 据点 + 战报
6. 打其他玩家（掠夺、保护 CD）
7. AI 玩家
8. 地图表现
9. 邮件、排行、联盟

**当前第一版玩法已按第 0～9 步落地，建造时长与田产出已按联调改过。** 开发环境会再缩短行军、保护罩和 AI tick。实机打一局后可按手感再调 Core 配置表。

## 详细设计文档

**以后新增的详细设计一律放在 [`Docs/`](Docs/README.md) 目录**，本表作为入口。尚未写完的文档已预留文件，正文标为「待撰写」。

| 主题 | 文档 | 状态 |
|------|------|------|
| 文档索引与约定 | [Docs/README.md](Docs/README.md) | 已有 |
| 开发路线图（先做什么） | [Docs/design-roadmap.md](Docs/design-roadmap.md) | 已定稿 |
| 总体架构与技术选型 | [Docs/design-architecture.md](Docs/design-architecture.md) | 已定稿 |
| HTTP / SignalR 统一协议 | [Docs/design-api.md](Docs/design-api.md) | 已定稿 |
| 账号、角色与建城 | [Docs/design-account-city.md](Docs/design-account-city.md) | 已定稿 |
| 大地图与 NPC 据点 | [Docs/design-world-map.md](Docs/design-world-map.md) | 已定稿 |
| 城内建筑（内政 / 科技 / 军事） | [Docs/design-inner-city.md](Docs/design-inner-city.md) | 已定稿 |
| 城墙与城防 | [Docs/design-city-wall.md](Docs/design-city-wall.md) | 已定稿 |
| 城外资源田 | [Docs/design-outer-resources.md](Docs/design-outer-resources.md) | 已定稿 |
| 行军与战斗结算 | [Docs/design-march-battle.md](Docs/design-march-battle.md) | 已定稿 |
| 玩家对战与掠夺 | [Docs/design-pvp.md](Docs/design-pvp.md) | 已定稿 |
| AI 玩家 | [Docs/design-ai.md](Docs/design-ai.md) | 已定稿 |
| 实时推送与定时任务 | [Docs/design-realtime.md](Docs/design-realtime.md) | 已定稿 |
| 网页端与前后端通讯 | [Docs/design-frontend-comm.md](Docs/design-frontend-comm.md) | 已定稿 |
| 邮件 | [Docs/design-mail.md](Docs/design-mail.md) | 已定稿 |
| 排行 | [Docs/design-ranking.md](Docs/design-ranking.md) | 已定稿 |
| 联盟 | [Docs/design-alliance.md](Docs/design-alliance.md) | 已定稿 |

## 本地运行服务端

需要安装 [.NET 9 SDK](https://dotnet.microsoft.com/download) 和 PostgreSQL（可用 Docker）：

```bash
docker compose up -d
dotnet run --project SanguoGame.Server --launch-profile http
```

默认 HTTP 地址：`http://localhost:5124`。本地默认连接 Docker Compose 的 PostgreSQL（库名 `sanguogame`，用户 `sanguo`）。开发环境会映射 OpenAPI，并自动同步表结构。

生产环境不要把密钥写进仓库，用环境变量覆盖：

```bash
ConnectionStrings__Default="Host=...;Port=5432;Database=postgres;Username=...;Password=...;SSL Mode=Require"
Jwt__SigningKey="至少 32 字符的独立密钥"
Cors__Origins__0="https://your.frontend.origin"
```

非 Development 环境会拒绝开发用 JWT 密钥，并禁止 `FreeSql:AutoSyncStructure`。

探活：`GET /api/system/ping`。账号与建城接口见 [账号、角色与建城](Docs/design-account-city.md)。

## 本地运行网页端

需要 [Node.js](https://nodejs.org/)（已含 npm）。先在 Visual Studio 里启动服务端（`http://localhost:5124`），再：

```bash
cd web
npm install
npm run dev
```

浏览器打开 `http://localhost:5173`。开发期 Vite 把 `/api`、`/hubs` 代理到后端，不必改 `VITE_API_BASE`。

`ASPNETCORE_ENVIRONMENT=Development`（`dotnet run` 默认如此）会读取 `appsettings.Development.json`：行军每格 5 秒、最短 10 秒，保护罩 / 据点恢复 3 分钟，AI 每分钟 tick 一次。生产默认仍是 `appsettings.json` 里的 20 秒/格、2 小时保护。

## 测试与 CI

需要 Docker（Testcontainers 拉起 PostgreSQL）或环境变量 `TEST_POSTGRES`（CI 用 service 容器）：

```bash
dotnet test SanguoGame.sln
```

GitHub Actions 在推送与 PR 时跑后端测试和 `web/` 的 `npm run build`。

## 相关约定

- 服务端权威：建造、出兵、战斗只信服务器。
- 耗时操作一律写成到点任务（`FinishAt` / `ArriveAt`），由后台任务结算后再推送。
- 战斗为到达后一次性结算并出战报，不做帧同步。
- 同一座城的并发操作按 `cityId` 串行（Redis 锁或数据库行锁）。
- 详细设计、接口契约、数值表等文档统一维护在 `Docs/`，不要散落在仓库其他位置。
