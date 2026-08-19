# HTTP / SignalR 统一协议

- **状态：** 已定稿（骨架阶段）
- **对应功能：** 所有 HTTP 接口与 SignalR 推送的请求、响应信封

本文是前后端共用的 JSON 契约。通道、代理与 CORS 见 [前后端通讯](design-frontend-comm.md)；Hub 路径与事件名见 [实时推送](design-realtime.md)。

## 原则

- **请求不包一层**：body 直接是业务 DTO，不要 `{ "data": { ... } }`。
- **响应一律信封**：HTTP 与 SignalR 都用同一套字段。
- JSON 属性 **camelCase**，属性名大小写不敏感。
- 时间一律 **UTC ISO-8601**（带 `Z`），例如 `2026-08-19T12:00:00.000Z`。

## HTTP 请求

- `POST` / `PUT` / `PATCH`：JSON body 即为 DTO。
- `GET`：查询字符串。列表分页统一使用：

| 参数 | 含义 | 约定 |
|------|------|------|
| `page` | 页码 | 从 **1** 开始，默认 1 |
| `pageSize` | 每页条数 | 默认 20，最大 100 |

对应服务端类型：`PagedQuery` / `PagedResult<T>`。

## HTTP 响应信封

```json
{
  "code": 0,
  "message": "ok",
  "data": {},
  "traceId": "00-..."
}
```

| 字段 | 成功 | 失败 |
|------|------|------|
| `code` | `0` | 非 0，见错误码 |
| `message` | `"ok"` 或提示文案 | 可展示给玩家的原因 |
| `data` | 业务对象 | `null`（序列化时省略） |
| `traceId` | 当前请求跟踪号 | 对日志用 |

分页成功时 `data` 形如：

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "total": 0
}
```

### HTTP 状态码

| 场景 | HTTP | `code` |
|------|------|--------|
| 业务成功 | 200 | `0` |
| 参数校验失败、可预期业务失败（如坐标占用） | 200 | 非 0 |
| 未登录 / 令牌无效（鉴权接入后） | 401 | `40100` |
| 未处理异常 | 500 | `50000` |

前端拦截器以 **`code === 0`** 判断成功，不要只看 HTTP 状态。生产环境 `50000` 的 `message` 为固定文案，细节只打服务端日志。

## 错误码

定义在 `SanguoGame.Core.ErrorCodes`。业务失败抛 `BizException(code, message)`，由过滤器写入信封。

| 码 | 含义 |
|----|------|
| `0` | 成功 |
| `40001` | 参数校验失败 |
| `40100` | 未登录、令牌无效，或登录失败 |
| `40300` | 无权限 |
| `40400` | 资源不存在 |
| `40900` | 业务冲突（未再细分时的兜底） |
| `40901` | 用户名已注册 |
| `40902` | 该账号已有角色 |
| `40903` | 角色名已被占用 |
| `40904` | 该角色已有主城 |
| `40905` | 无空地可建城 |
| `40906` | 资源不足 |
| `40907` | 本城建造队列占用中 |
| `40908` | 建筑已满级 |
| `40909` | 建筑前置未满足 |
| `50000` | 未处理异常 |

账号 / 建城见 [账号、角色与建城](design-account-city.md)；城内建筑见 [城内建筑](design-inner-city.md)。后续玩法继续用 `409xx` 细分。

## SignalR

Hub：`/hubs/game`。推送 payload 使用同一信封字段（`code` / `message` / `data` / `traceId`）。具体事件名仍按 [实时推送](design-realtime.md)（`BuildComplete`、`MarchArrived`、`CityAttacked`）。当前 Hub 为空壳，尚未推送。

## 示例

`GET /api/system/ping`（骨架探活，非玩法接口）：

```json
{
  "code": 0,
  "message": "ok",
  "data": {
    "serverTime": "2026-08-19T12:00:00.000Z"
  },
  "traceId": "0HNG..."
}
```

业务失败示例（账号篇接入后）：

```json
{
  "code": 40900,
  "message": "坐标已被占用",
  "traceId": "0HNG..."
}
```
