# 总体架构与技术选型

- **状态：** 撰写中
- **对应功能：** 整站进程划分、技术栈、扩展路径

根目录 [README.md](../README.md) 是结构与技术栈入口；本文补充选型理由和约束。玩法细则见各 `design-*.md`。

## 玩法节奏（决定技术）

| 类型 | 代表 | 通信 | 第一版 |
|------|------|------|--------|
| **定时结算型** | 热血三国、七雄争霸、早期战国页游 | HTTP 为主，少量推送 | **采用** |
| 行军可视化型 | 率土之滨、三国志战略版 | HTTP + 全图 WebSocket 视野 | 不上 |
| 即时 RTS | 星际、红警 | 高频率同步 | 不上 |

建造、行军是「下达指令 → 到点结算」。地图只做坐标和据点，不做全图实时视野，不做帧同步。

## 进程与项目

第一版 **一个 ASP.NET Core 进程**：Web API、SignalR、Hangfire 同站。不要拆成地图服务 / 战斗服务 / 聊天服务。

类库已按分层拆开，**仍是同一个网站、同一个进程**：

```
SanguoGame/
├── SanguoGame.sln
├── SanguoGame.Server/             # API + SignalR + 进程启动
├── SanguoGame.Core/               # 错误码、业务异常；后续纯规则
├── SanguoGame.Infrastructure/     # FreeSql + PostgreSQL；后续 Redis、Hangfire
└── web/                           # Vue 3 独立前端（尚未创建，npm，不是 .csproj）
```

- HTTP / SignalR JSON 信封见 [统一协议](design-api.md)。
- 网页端必须另建 Vue 工程（除非改用很简陋的 Razor）。
- 可选 `Shared` 放前后端共用 DTO，不必须一上来就建。
- Orleans、网关、多服务：人多以后再说。

## 技术栈

与根 README「规划（第一版）」一致。补充说明：

| 层 | 技术 | 说明 |
|----|------|------|
| 网页 UI | Vue 3 + TypeScript + Vite | 城内用组件库（Element Plus / Naive UI 等）；不用 Unity WebGL / Cocos 做页游 SLG |
| 大地图 | Canvas / PixiJS | 不要用 DOM 画大量格子；Phaser 仅当地图交互明显游戏化时再考虑 |
| HTTP | ASP.NET Core Web API + Axios/`fetch` | 玩家主动指令 |
| 推送 | SignalR + `@microsoft/signalr` | 底层优先 WebSocket，可降级；不要自建原生 WebSocket 协议 |
| 持久化 | FreeSql + PostgreSQL | 玩家、城池、建筑、行军；Hangfire **不走** FreeSql，也不需要 EF Core |
| 缓存 / 锁 | Redis | 在线、按 `cityId` 串行、防连点 |
| 定时任务 | Hangfire | 建造完成、行军到达；存储用 `Hangfire.PostgreSql`（独立 `hangfire` schema）或 Redis |
| 认证 | JWT + Refresh Token | HTTP 与 Hub 同一套身份 |

人多以后再考虑 Orleans（每城一个 Grain）、分服、地图分片。

明确不上：微服务拆分、gRPC 对浏览器、Socket.IO（Node 生态）、自建 `System.Net.WebSockets` 协议。

## 关键约束

- **服务端权威**：建造、出兵、战斗只信服务器；客户端只展示和发指令。
- **耗时即任务**：升级写 `FinishAt`，行军写 `ArriveAt`，到点由 Hangfire 结算再推送。
- **战斗**：到达后一次性结算（兵力、兵种、城防、科技、随机种子 → 战报），不做帧同步。
- **资源**：按上次收取时间结算，或每 N 分钟 tick；不要每秒写库。
- **地图**：只持久化有内容的格子；前端按视野加载。
- **并发**：同一座城按 `cityId` 用 Redis 锁或行锁串行。

## 为何用 .NET

规则复杂、定时任务多、还要网页推送时，ASP.NET Core + SignalR + Hangfire 是完整组合。Node 适合原型，规则一复杂容易散；Java 也能做，但网页实时推送在 .NET 里更省事。没有现成「战国引擎」可装即玩，能省的是登录、推送、任务队列和存储，不是玩法公式。

可参考架构、不要指望开箱即玩：Lisergy-RTS（偏 RTS）、CoreGameIO/SharedMeta（Unity + Orleans）。本项目按浏览器 + REST + 定时结算做，不套 Unity 联网框架。
