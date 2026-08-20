# 总体架构与技术选型

- **状态：** 已定稿
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
├── SanguoGame.Server/             # API + SignalR + Hangfire + 进程启动
├── SanguoGame.Core/               # 错误码、业务异常、规则与配置表
├── SanguoGame.Infrastructure/     # FreeSql + PostgreSQL
└── web/                           # Vue 3 独立前端（npm）
```

- HTTP / SignalR JSON 信封见 [统一协议](design-api.md)。
- 网页端必须另建 Vue 工程（除非改用很简陋的 Razor）。
- Orleans、网关、多服务：人多以后再说。

## 技术栈

| 层 | 技术 | 说明 |
|----|------|------|
| 网页 UI | Vue 3 + TypeScript + Vite | 城内 / 城墙 / 出征 / 战报用普通组件；不用 Unity WebGL / Cocos |
| 大地图 | Canvas | 拖拽、缩放、城点、据点、行军线；不要用 DOM 画大量格子 |
| HTTP | ASP.NET Core Web API + Axios | 玩家主动指令 |
| 推送 | SignalR + `@microsoft/signalr` | 底层优先 WebSocket，可降级 |
| 持久化 | FreeSql + PostgreSQL | 玩家、城池、建筑、据点、行军、战报；Hangfire **不走** FreeSql |
| 锁 | 数据库行锁（`SELECT … FOR UPDATE`） | 按 `cityId` 串行；无 Redis 时不阻塞 |
| 定时任务 | Hangfire + `Hangfire.PostgreSql`（schema `hangfire`） | 建造完成、行军到达、AI tick |
| 认证 | JWT + Refresh Token | HTTP 与 Hub 同一套身份 |

人多以后再考虑 Orleans（每城一个 Grain）、分服、地图分片。

明确不上：微服务拆分、gRPC 对浏览器、Socket.IO、自建 `System.Net.WebSockets` 协议。

## 关键约束

- **服务端权威**：建造、出兵、战斗只信服务器；客户端只展示和发指令。
- **耗时即任务**：升级写 `FinishAt`，行军写 `ArriveAt`，到点由 Hangfire 结算再推送。
- **战斗**：到达后一次性结算（兵力、兵种、城防、科技、随机种子 → 战报），不做帧同步。
- **资源**：按上次收取时间现算，点收取才写库。
- **地图**：只持久化有内容的格子。
- **并发**：同一座城按 `cityId` 行锁串行；锁两城时先锁较小 Id。

## 为何用 .NET

规则复杂、定时任务多、还要网页推送时，ASP.NET Core + SignalR + Hangfire 是完整组合。没有现成「战国引擎」可装即玩，能省的是登录、推送、任务队列和存储，不是玩法公式。
