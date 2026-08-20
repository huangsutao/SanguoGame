# 网页端与前后端通讯

- **状态：** 已定稿
- **对应功能：** 独立 Vue 工程如何连接 API 与 SignalR

通讯靠 URL + JSON + Token，不依赖前后端是否在同一个解决方案。两个工程可以同仓库（`web/` + `SanguoGame.Server/`），也可以分仓库。

## 前端技术

- Vue 3 + TypeScript + Vite。
- 城内、城墙、出征、战报：普通组件。
- 大地图：Canvas 拖拽、缩放、城点、据点、行军线；不要用 DOM 画几千个格子。

## 整体关系

```
浏览器
  ├─ 页面        →  Vue（开发期如 localhost:5173）
  ├─ HTTP        →  ASP.NET Core /api/...     下达指令
  └─ WebSocket   →  ASP.NET Core /hubs/game   接收结算结果
```

通道约定见 [实时推送与定时任务](design-realtime.md)。

## HTTP

开发期推荐 Vite 把 `/api`、`/hubs` 代理到 `http://localhost:5124`，`VITE_API_BASE` 留空。请求 body 直接是业务字段；响应一律为 `{ code, message, data, traceId }`，以 `code === 0` 为成功。完整契约见 [统一协议](design-api.md)。

Axios 每次带 `Authorization: Bearer <accessToken>`。

## SignalR

HTTP 与 Hub **共用同一个 JWT**。`accessTokenFactory` 取同一 Token。登录成功且已建城后 `connection.start()`，监听：

| 事件 | 用途 |
|------|------|
| `BuildComplete` | 刷新城内 / 城墙 / 城外等级 |
| `MarchArrived` | 刷新兵力、行军、战报 |
| `CityAttacked` | 被打：刷新兵力、资源、保护 |

开发期 Vite 代理 `/hubs` 必须 `ws: true`。

## 开发期连法

### Vite 代理（推荐）

```ts
server: {
  proxy: {
    "/api": { target: "http://localhost:5124", changeOrigin: true },
    "/hubs": { target: "http://localhost:5124", ws: true, changeOrigin: true },
  },
}
```

### 直连后端

Vue 请求 `http://localhost:5124`。后端 CORS 放行 `http://localhost:5173`（已配置策略名 `web`）。

## 上线

同域名反代：静态站点 + `/api` + `/hubs`（WebSocket 需 `Upgrade`）。前后端不同域名时配 CORS 与 `VITE_API_BASE`。

## 页面加载后的时序

```
Vue 加载 → HTTP 登录 → HTTP 拉城池/建筑 → SignalR start
玩家点「升级」→ HTTP 立刻返回 finishAt → 界面倒计时（仅展示）
到点 → 服务端结算 → SignalR BuildComplete → Vue 刷新等级
玩家点「出征」→ HTTP 立刻返回 arriveAt → 到点 MarchArrived
被打 → CityAttacked（玩家未点按钮）
```
