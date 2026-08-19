# 文档目录

本目录存放 **SanguoGame 的详细设计**。根目录 [README.md](../README.md) 只保留项目结构、技术栈和文档入口；功能怎么做、接口怎么定、数值怎么配，都写在这里。

## 约定

- 新增详细设计、接口说明、数值草案，一律放本目录，不要写到源码旁或仓库根目录。
- 文件名使用 `design-<主题>.md`（英文短横线），文内标题用中文。
- 文档开头标明 **状态**（待撰写 / 撰写中 / 已定稿）和 **对应功能**。
- 根 README 的「详细设计文档」表要同步增加链接。
- 历史聊天导出可保留作背景，定稿设计以 `design-*.md` 为准。

## 索引

| 文档 | 说明 | 状态 |
|------|------|------|
| [cursor_web_based_sengoku_game_tech.md](cursor_web_based_sengoku_game_tech.md) | 网页战国 SLG 技术选型、通讯、落地顺序（聊天导出） | 已有 |
| [design-architecture.md](design-architecture.md) | 总体架构、进程划分、技术选型 | 待撰写 |
| [design-account-city.md](design-account-city.md) | 账号、角色、主城创建 | 待撰写 |
| [design-world-map.md](design-world-map.md) | 大地图格子、据点、视野加载 | 待撰写 |
| [design-inner-city.md](design-inner-city.md) | 城内内政 / 科技 / 军事建筑 | 待撰写 |
| [design-city-wall.md](design-city-wall.md) | 城墙与城防建筑 | 待撰写 |
| [design-outer-resources.md](design-outer-resources.md) | 城外矿、木、田与产出 | 待撰写 |
| [design-march-battle.md](design-march-battle.md) | 行军、到达结算、战报 | 待撰写 |
| [design-pvp.md](design-pvp.md) | 打玩家、掠夺、保护 CD | 待撰写 |
| [design-ai.md](design-ai.md) | AI 玩家决策与 tick | 待撰写 |
| [design-realtime.md](design-realtime.md) | HTTP 指令、SignalR 推送、Hangfire 到点任务 | 待撰写 |
| [design-frontend-comm.md](design-frontend-comm.md) | Vue 独立工程、CORS / 代理、上线反代 | 待撰写 |
