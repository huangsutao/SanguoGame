# 总体架构与技术选型

- **状态：** 待撰写
- **对应功能：** 整站进程划分、技术栈、扩展路径
- **背景：** [历史技术讨论](cursor_web_based_sengoku_game_tech.md)

## 待覆盖内容

- 定时结算型 SLG 的约束：指令走 HTTP，结果走推送，不做帧同步
- 第一版单进程：ASP.NET Core 同时承载 Web API、SignalR、Hangfire
- 建议拆分：`Server`（宿主）/ `Core`（规则）/ `Infrastructure`（EF、Redis、任务）/ `web`（Vue）
- 数据：PostgreSQL + EF Core；缓存与按城锁：Redis
- 人多以后：Orleans（每城 Grain）、分服、地图分片
- 明确不上：微服务拆分、gRPC 对浏览器、自建 WebSocket 协议

## 草案要点（来自历史讨论，定稿前可改）

服务端权威；一切耗时操作为 `FinishAt` / `ArriveAt` 任务；战斗到达后一次性结算。
