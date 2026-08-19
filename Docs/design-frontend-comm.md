# 网页端与前后端通讯

- **状态：** 撰写中
- **对应功能：** 独立 Vue 工程如何连接 API 与 SignalR

通讯靠 URL + JSON + Token，不依赖前后端是否在同一个解决方案。两个工程可以同仓库（`web/` + `SanguoGame.Server/`），也可以分仓库。

## 前端技术

- Vue 3 + TypeScript + Vite。
- 城内、科技、出征面板：普通组件 + 组件库（Element Plus / Naive UI 等）。
- 大地图：Canvas / PixiJS，拖拽、缩放、城点、行军线；不要用 DOM 画几千个格子。

## 整体关系

```
浏览器
  ├─ 页面        →  Vue（开发期如 localhost:5173）
  ├─ HTTP        →  ASP.NET Core /api/...     下达指令
  └─ WebSocket   →  ASP.NET Core /hubs/game   接收结算结果
```

通道约定见 [实时推送与定时任务](design-realtime.md)。

## HTTP：Axios

`.env.development`

```env
VITE_API_BASE=http://localhost:5124
```

`.env.production` 按实际上线域名填写，同域名反代时可留空。

```ts
import axios from "axios";

export const http = axios.create({
  baseURL: import.meta.env.VITE_API_BASE,
  timeout: 15000,
});

http.interceptors.request.use((config) => {
  const token = localStorage.getItem("token");
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});
```

```ts
await http.post("/api/army/march", { targetX: 12, targetY: 34, troops: [] });
await http.get("/api/city/me");
```

请求 body 直接是业务字段；响应一律为 `{ code, message, data, traceId }`，以 `code === 0` 为成功。完整契约见 [统一协议](design-api.md)。

当前服务端开发地址是 `http://localhost:5124`（见根 README），不要写死在每个页面里。

## SignalR

HTTP 与 Hub **共用同一个 JWT**。

```ts
import * as signalR from "@microsoft/signalr";

export function createGameHub(token: string) {
  return new signalR.HubConnectionBuilder()
    .withUrl(`${import.meta.env.VITE_API_BASE}/hubs/game`, {
      accessTokenFactory: () => token,
    })
    .withAutomaticReconnect()
    .build();
}
```

登录成功后 `connection.start()`，监听 `BuildComplete`、`MarchArrived`、`CityAttacked`。

## 登录串起来

1. `POST /api/auth/login` 拿到 JWT，存 `localStorage`（或内存）。
2. Axios 每次带 `Authorization`。
3. SignalR 用 `accessTokenFactory` 带同一 Token。
4. 后端 API 与 Hub 都校验该 Token。

## 开发期连法（二选一）

### A. 前端直连后端

Vue 请求 `http://localhost:5124`。必须配 CORS。

```csharp
builder.Services.AddCors(o => o.AddPolicy("web", p =>
    p.WithOrigins("http://localhost:5173")
     .AllowAnyHeader()
     .AllowAnyMethod()
     .AllowCredentials()
));
app.UseCors("web");
```

SignalR 若带 Cookie/凭证，必须写明前端源，不能 `AllowAnyOrigin()` 再配 `AllowCredentials()`。JWT 放 Header 时 `AllowCredentials` 不是必须，但 `WithOrigins` 必须包含 Vue 地址。

### B. Vite 代理（开发更推荐）

```ts
export default {
  server: {
    proxy: {
      "/api": { target: "http://localhost:5124", changeOrigin: true },
      "/hubs": { target: "http://localhost:5124", ws: true, changeOrigin: true },
    },
  },
};
```

`VITE_API_BASE` 留空，请求 `/api/...`、`/hubs/game` 走页面同源，由 Vite 转给 ASP.NET。开发可以不配 CORS；上线仍按真实域名配。`/hubs` 必须 `ws: true`。

## 上线

**同域名反代（推荐）**

```
https://game.xxx.com          → Vue 静态文件（Nginx）
https://game.xxx.com/api      → ASP.NET
https://game.xxx.com/hubs     → ASP.NET（WebSocket）
```

前端 `VITE_API_BASE` 留空或写成当前站点，CORS 最省心。

**前后端不同域名** 时，`VITE_API_BASE=https://api.xxx.com`，后端 CORS 放行前端源。

Nginx 给 `/hubs` 打开 WebSocket：

```nginx
proxy_http_version 1.1;
proxy_set_header Upgrade $http_upgrade;
proxy_set_header Connection "upgrade";
```

## 页面加载后的时序

```
Vue 加载 → HTTP 登录 → HTTP 拉城池/建筑 → SignalR start
玩家点「升级」→ HTTP 立刻返回 finishAt → 界面倒计时（仅展示）
到点 → 服务端结算 → SignalR BuildComplete → Vue 刷新等级
```

被打时玩家未点按钮，SignalR 直接推 `CityAttacked`。
