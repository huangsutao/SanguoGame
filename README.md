# SanguoGame

网络版战国类网页 SLG：玩家在大地图坐标建城，城池分为**城内 / 城墙 / 城外**；可建造或升级内政、科技、军事、城防与资源田，出兵掠夺地图据点或其他玩家，并包含 AI 玩家。

玩法按 **定时结算型 SLG** 落地：建造、行军是「下达指令 → 到点结算」，服务端权威，客户端只负责展示与发指令。第一版不做全图实时视野，也不做成即时 RTS。

## 当前进度

| 部分 | 状态 |
|------|------|
| 服务端 `SanguoGame.Server` | 已有 ASP.NET Core 9 Web API 空骨架（模板接口） |
| 网页端 | 尚未创建，规划为独立 Vue 3 工程 |
| 玩法系统（建城、建筑、行军等） | 未实现，设计见 [Docs](Docs/README.md) |

## 仓库结构

```
SanguoGame/
├── README.md                      # 本文件：结构、技术栈、设计文档入口
├── Docs/                          # 详细设计文档（统一放这里）
│   ├── README.md                  # 文档索引与撰写约定
│   └── design-*.md                # 各功能详细设计（含待撰写占位）
└── SanguoGame.Server/             # 服务端：ASP.NET Core Web API
    ├── Controllers/               # HTTP 接口（当前为模板 WeatherForecast）
    ├── Program.cs                 # 启动、中间件、服务注册
    ├── appsettings*.json          # 配置
    └── SanguoGame.Server.sln      # 解决方案
```

规划中的目录（尚未落地，实现时再拆）：

```
SanguoGame/
├── SanguoGame.Server/             # API + SignalR + 进程启动
├── SanguoGame.Core/               # 纯规则：建造、战斗、行军、资源结算
├── SanguoGame.Infrastructure/     # FreeSql、Redis、Hangfire
└── web/                           # Vue 3 独立前端（npm 工程，不是 .csproj）
```

第一版服务端保持 **单个 ASP.NET Core 进程**：HTTP、SignalR、定时任务都放在同一站点里，不要拆微服务。

## 技术栈

### 已落地

| 层 | 技术 | 说明 |
|----|------|------|
| 服务端 | ASP.NET Core 9 Web API | `SanguoGame.Server`，当前仅模板工程 |
| 运行时 | .NET 9 | `TargetFramework: net9.0` |

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

建议实现顺序（详细设计链接见下一节）：

1. 账号 + 在地图随机空地建主城
2. 城内建筑：建造 / 升级 + 到点完成
3. 城外资源田：产出与收取
4. 城墙与基础城防
5. 出兵打 NPC 据点 + 战报
6. 打其他玩家（掠夺、保护 CD）
7. AI 玩家
8. 地图表现、联盟、邮件、排行等扩展

## 详细设计文档

**以后新增的详细设计一律放在 [`Docs/`](Docs/README.md) 目录**，本表作为入口。尚未写完的文档已预留文件，正文标为「待撰写」。

| 主题 | 文档 | 状态 |
|------|------|------|
| 文档索引与约定 | [Docs/README.md](Docs/README.md) | 已有 |
| 总体架构与技术选型 | [Docs/design-architecture.md](Docs/design-architecture.md) | 撰写中 |
| 账号、角色与建城 | [Docs/design-account-city.md](Docs/design-account-city.md) | 待撰写 |
| 大地图与 NPC 据点 | [Docs/design-world-map.md](Docs/design-world-map.md) | 待撰写 |
| 城内建筑（内政 / 科技 / 军事） | [Docs/design-inner-city.md](Docs/design-inner-city.md) | 待撰写 |
| 城墙与城防 | [Docs/design-city-wall.md](Docs/design-city-wall.md) | 待撰写 |
| 城外资源田 | [Docs/design-outer-resources.md](Docs/design-outer-resources.md) | 待撰写 |
| 行军与战斗结算 | [Docs/design-march-battle.md](Docs/design-march-battle.md) | 待撰写 |
| 玩家对战与掠夺 | [Docs/design-pvp.md](Docs/design-pvp.md) | 待撰写 |
| AI 玩家 | [Docs/design-ai.md](Docs/design-ai.md) | 待撰写 |
| 实时推送与定时任务 | [Docs/design-realtime.md](Docs/design-realtime.md) | 撰写中 |
| 网页端与前后端通讯 | [Docs/design-frontend-comm.md](Docs/design-frontend-comm.md) | 撰写中 |

## 本地运行服务端

需要安装 [.NET 9 SDK](https://dotnet.microsoft.com/download)。

```bash
cd SanguoGame.Server
dotnet run --launch-profile http
```

默认 HTTP 地址：`http://localhost:5124`。开发环境会映射 OpenAPI。当前仅有模板接口 `GET /WeatherForecast`。

## 相关约定

- 服务端权威：建造、出兵、战斗只信服务器。
- 耗时操作一律写成到点任务（`FinishAt` / `ArriveAt`），由后台任务结算后再推送。
- 战斗为到达后一次性结算并出战报，不做帧同步。
- 同一座城的并发操作按 `cityId` 串行（Redis 锁或数据库行锁）。
- 详细设计、接口契约、数值表等文档统一维护在 `Docs/`，不要散落在仓库其他位置。
