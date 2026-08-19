# 实时推送与定时任务

- **状态：** 待撰写
- **对应功能：** HTTP 指令通道、SignalR、建造 / 行军到点
- **背景：** [历史技术讨论](cursor_web_based_sengoku_game_tech.md) 中 HTTP + WebSocket 一节

## 待覆盖内容

- HTTP：Controller / Minimal API、JSON、JWT；用于登录、建城、升级、出兵
- SignalR Hub：`/hubs/game`；按用户或 `city:{id}` 分组
- 推送事件清单：`BuildComplete`、`MarchArrived`、`CityAttacked`、资源变化等
- Hangfire（或等价物）任务：延迟到 `FinishAt` / `ArriveAt`，失败重试与幂等
- 不要用 WebSocket 下指令，不要用 HTTP 轮询战斗结果
- 认证：Hub 与 API 共用 JWT

## 草案要点

同一 ASP.NET Core 项目同时宿主 API 与 Hub。断线重连由 SignalR 客户端 `withAutomaticReconnect` 处理。
