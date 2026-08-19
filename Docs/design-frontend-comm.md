# 网页端与前后端通讯

- **状态：** 待撰写
- **对应功能：** 独立 Vue 工程如何连接 API 与 SignalR
- **背景：** [历史技术讨论](cursor_web_based_sengoku_game_tech.md) 中 Vue 独立项目通讯一节

## 待覆盖内容

- 前端技术：Vue 3 + TypeScript + Vite；城内用组件库，大地图用 Canvas / PixiJS
- 环境变量：`VITE_API_BASE`；Axios 封装与 Token 拦截器
- SignalR：`withUrl(API + /hubs/game)`，`accessTokenFactory` 使用同一 JWT
- 开发：Vite 代理 `/api` 与 `/hubs`（`ws: true`），减少 CORS 问题
- 生产：Nginx 同域名反代（推荐）或分域名 + CORS
- Nginx 需为 Hub 打开 WebSocket Upgrade

## 草案要点

两个工程可以同仓库 `web/` + `SanguoGame.Server/`，通讯只依赖 URL，不依赖是否在同一个解决方案里。
