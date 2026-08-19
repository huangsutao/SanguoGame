# 实时推送与定时任务

- **状态：** 撰写中
- **对应功能：** HTTP 指令通道、SignalR、建造 / 行军到点

同一 ASP.NET Core 项目同时宿主 API 与 Hub。下指令走 HTTP，等结果 / 被打走推送。

## 通道分工

| 通道 | 服务端 | 网页端 | 用途 |
|------|--------|--------|------|
| HTTP | ASP.NET Core Web API | `fetch` / Axios | 玩家主动指令：登录、建城、升级、出兵 |
| WebSocket | SignalR（底层 WebSocket，可降级长轮询） | `@microsoft/signalr` | 服务器主动推：建造完成、行军到达、被打 |

不要用 WebSocket 发「升级建筑」这类指令（鉴权、重试、日志都更差）。不要用 HTTP 轮询「打完没有」。

不必手写原生 WebSocket：SignalR 自带重连、降级、与 ASP.NET 身份集成。

## HTTP

- Controller 或 Minimal API，JSON。信封与错误码见 [统一协议](design-api.md)。
- 例：`POST /api/city/build`、`POST /api/army/march`、`POST /api/buildings/upgrade`。
- 身份：`Authorization: Bearer <JWT>`，接口 `[Authorize]`。

适合「谁发起、结果立刻能定」的操作（点升级、派出部队、查看城池）。耗时操作也先走 HTTP：立刻返回 `finishAt` / `arriveAt`，完成后再推送。

## SignalR

Hub 路径：`/hubs/game`。客户端连上后加入自己的城分组（如 `city:{cityId}`）。

推送事件（第一版）：

| 事件 | 时机 |
|------|------|
| `BuildComplete` | 建造 / 升级到点生效；payload 见 [城内建筑](design-inner-city.md) |
| `MarchArrived` | 行军到达并出战报 |
| `CityAttacked` | 本城被打 |
| 资源变化 | 收取或被掠后（可并入上列事件） |

概念：

```csharp
public class GameHub : Hub
{
    // 连接后加入 city:{cityId}
    // 结算后：Clients.Group($"city:{cityId}").SendAsync("MarchArrived", report);
}
```

网页端用同一 JWT：

```ts
import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/game", { accessTokenFactory: () => token })
  .withAutomaticReconnect()
  .build();

connection.on("MarchArrived", (report) => { /* 战报 */ });
connection.on("BuildComplete", (building) => { /* 刷新建筑 */ });
await connection.start();
```

独立 Vue 工程的地址、代理、CORS 见 [前后端通讯](design-frontend-comm.md)。

## Hangfire

建造、行军都是延迟任务：到 `FinishAt` / `ArriveAt` 结算，失败重试，业务侧要幂等（同一 `marchId` 只结算一次）。

存储与游戏 ORM 分开：用 `Hangfire.PostgreSql`（建议独立 schema `hangfire`）或 Redis。不需要 EF Core，也不经过 FreeSql。

结算过程读写玩家 / 城池 / 部队仍用 FreeSql。结完再 SignalR 推送。

## 典型时序

```
玩家点「出兵」
    → HTTP POST /api/army/march   扣兵、写入 ArriveAt，立刻返回 { marchId, arriveAt }
    → 前端按 arriveAt 自己倒计时（仅展示）
    → Hangfire 到点结算战斗
    → SignalR 推 MarchArrived
```

升级建筑同理：HTTP 返回 `finishAt`，到点推 `BuildComplete`。被打则玩家未操作也会收到 `CityAttacked`。
