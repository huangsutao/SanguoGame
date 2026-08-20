# 实时推送与定时任务

- **状态：** 已定稿（Hub 鉴权、Hangfire、建造 / 行军到点、AI tick）
- **对应功能：** HTTP 指令通道、SignalR、建造 / 行军到点

同一 ASP.NET Core 项目同时宿主 API 与 Hub。下指令走 HTTP，等结果 / 被打走推送。城内建造细则见 [城内建筑](design-inner-city.md)。

## 通道分工

| 通道 | 服务端 | 网页端 | 用途 |
|------|--------|--------|------|
| HTTP | ASP.NET Core Web API | `fetch` / Axios | 玩家主动指令：登录、建城、升级、出兵 |
| WebSocket | SignalR（底层 WebSocket，可降级长轮询） | `@microsoft/signalr` | 服务器主动推：建造完成、行军到达、被打 |

不要用 WebSocket 发「升级建筑」这类指令。不要用 HTTP 轮询「升完没有」。

## HTTP

JSON 信封见 [统一协议](design-api.md)。身份：`Authorization: Bearer <JWT>`，`[Authorize]`。

耗时操作立刻返回 `finishAt` / `arriveAt`，完成后再推送。城内 / 城墙 / 城外升级：`POST /api/buildings|walls|fields/upgrade`。出征：`POST /api/army/march`。市集兑换 / 同盟运输：`POST /api/markets/trade|aid`。

## SignalR

Hub 路径：`/hubs/game`。`[Authorize]`，与 HTTP **同一套 JWT**。

浏览器 WebSocket 不能自定义 Header，客户端用 `accessTokenFactory`；服务端从查询串 `access_token` 取令牌（仅 `/hubs` 路径）。

连接成功后按账号查主城，加入组 `city:{cityId}`。无城不入组。结算后：

```csharp
Clients.Group($"city:{cityId}").SendAsync("BuildComplete", envelope);
```

payload 仍是 `{ code, message, data, traceId }`。`BuildComplete` 的 `data` 见 [城内建筑](design-inner-city.md)。

| 事件 | 时机 |
|------|------|
| `BuildComplete` | 建造 / 升级到点生效 |
| `MarchArrived` | 行军到达并出战报（第 5 步） |
| `CityAttacked` | 本城被打（第 6 步） |
| `TransportArrived` | 市集兑换或运输到达出发城（第 10 步） |
| `ResourceReceived` | 同盟运输送达接收城（第 10 步） |

网页：

```ts
const connection = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/game", { accessTokenFactory: () => token })
  .withAutomaticReconnect()
  .build();
```

开发期 Vite 代理 `/hubs` 必须 `ws: true`。见 [前后端通讯](design-frontend-comm.md)。

## Hangfire

延迟任务存 PostgreSQL **独立 schema `hangfire`**，不经过 FreeSql、不用 EF。连接串与游戏库相同（`ConnectionStrings:Default`）。`PrepareSchemaIfNecessary = true`。

建造 Job：`CompleteInnerBuilding(cityId, buildingType, targetLevel)`，在 `finishAt` 触发。行军 Job：`CompleteMarch(marchId)`，在 `arriveAt` 触发。AI：周期任务 `AiTick`。失败可重试；业务幂等。

进程重启后未到期任务仍由 Hangfire 执行。启动时扫描已到期仍为 `upgrading` 的建筑、已到期仍为 `marching` 的行军、已到期仍为 `inTransit` 的运输并补结算；并补齐常驻 NPC 据点、市集与 AI 城，再补一轮流寇刷新。流寇周期任务 `RoamingOutpostTick` 见 [大地图](design-world-map.md)。

## 按城串行

指令与结算都要串行。第 2 步：对 `sg_city` 行 `SELECT … FOR UPDATE`（同一事务），并靠部分唯一索引 `uk_building_city_queue`。Redis `lock:city:{cityId}` 有连接串再启用；**无 Redis 时行锁即可**，不阻塞本步。

## 典型时序

```
玩家点「升级」
    → HTTP POST /api/buildings/upgrade  扣资源、写 FinishAt，立刻返回
    → 前端按 finishAt 倒计时（仅展示）
    → Hangfire 到点结算
    → SignalR 推 BuildComplete
```
