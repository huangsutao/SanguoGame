# 账号、角色与建城

- **状态：** 已定稿（第 1 步）
- **对应功能：** 注册登录、角色、在地图空地创建主城
- **实现顺序：** 第 1 步（见 [路线图](design-roadmap.md)）

格子规模、坐标范围、空地选取见 [大地图格子规则](design-world-map.md)。HTTP 信封见 [统一协议](design-api.md)。

## 模型

第一版 **一个账号 → 至多一个角色 → 至多一座主城**。表结构按这个基数做唯一约束；以后若要多角色，再放开账号—角色约束。

```
Account（登录身份）
  └─ Character（游戏内名称，一角）
        └─ City（主城，地图上一格）
```

| 实体 | 要点 |
|------|------|
| 账号 | `username` 登录用；密码只存哈希；用户名规范化后唯一（大小写不敏感） |
| 角色 | 展示名，全局唯一；未建角色不能建城 |
| 主城 | 服务端随机空地落点；`(x, y)` 唯一；城名默认「{角色名}的城」 |
| 刷新令牌 | 明文只发给客户端一次；库内存 SHA-256；刷新时轮换 |

城内 / 城墙 / 城外建筑表第 2～4 步再加。第 1 步查询我的城时 `zones` 固定返回空数组，占位给前端。

## 鉴权

- Access Token：JWT（HS256），声明 `sub` = 账号 Id。有效期默认 **120 分钟**。
- Refresh Token：随机字节的 Base64，有效期默认 **14 天**。`POST /api/auth/refresh` 颁发新的一对，旧刷新令牌作废。
- 受保护 HTTP 接口：`Authorization: Bearer <accessToken>`。
- 未带令牌或令牌无效：**HTTP 401**，信封 `code = 40100`。
- 登录密码错误等可预期失败：**HTTP 200**，信封 `code = 40100`（避免前端把登录接口当「掉线」）。
- SignalR 第 2 步再用同一套 JWT；第 1 步 Hub 仍为空壳。

配置节 `Jwt`：`Issuer`、`Audience`、`SigningKey`（≥32 字符）、`AccessTokenMinutes`、`RefreshTokenDays`。

## 建城规则

- 客户端 **不传坐标**。`POST /api/city/found` 由服务端按 [格子规则](design-world-map.md) 选空地。
- 已有主城再调 → `40904`。
- 尚无角色 → `40400`（尚未创建角色）。
- 随机选格与插入之间若撞上唯一约束（并发建城），换格重试；仍失败 → `40905`。
- 本步不用 Redis；占用以 `sg_city (x, y)` 唯一索引为准。

## 校验

| 字段 | 规则 |
|------|------|
| `username` | 3～16 位，`[a-zA-Z0-9_]` |
| `password` | 8～64 位 |
| 角色 `name` | 2～12 位，去首尾空白后非空；允许中文 |

参数不合规走统一校验：HTTP 200、`code = 40001`。

## 错误码（本步）

| 码 | HTTP | 场景 |
|----|------|------|
| `40001` | 200 | 参数校验失败 |
| `40100` | 401 | 未登录 / Access Token 无效 |
| `40100` | 200 | 登录失败（用户名或密码错误）；刷新令牌无效或过期 |
| `40400` | 200 | 没有角色、没有城 |
| `40901` | 200 | 用户名已注册 |
| `40902` | 200 | 该账号已有角色 |
| `40903` | 200 | 角色名已被占用 |
| `40904` | 200 | 该角色已有主城 |
| `40905` | 200 | 无空地可建（重试耗尽或地图已满） |

通用 `40900` 仍保留，本步业务优先用上表细分码。

## HTTP API

请求 body 直接为 DTO。成功 `code = 0`。时间均为 UTC ISO-8601。

### `POST /api/auth/register`

注册成功即登录，返回令牌。

```json
{ "username": "player1", "password": "password1" }
```

`data`：

```json
{
  "accessToken": "...",
  "refreshToken": "...",
  "tokenType": "Bearer",
  "expiresAt": "2026-08-19T14:00:00.000Z"
}
```

### `POST /api/auth/login`

body 同上。`data` 同注册。

### `POST /api/auth/refresh`

```json
{ "refreshToken": "..." }
```

`data` 同注册（新的一对令牌）。

### `POST /api/auth/logout`

```json
{ "refreshToken": "..." }
```

将刷新令牌作废。不要求 Access Token。`data` 可为 `null`。

### `GET /api/auth/me`

需登录。一次拉齐开局状态。

```json
{
  "accountId": 1,
  "username": "player1",
  "character": { "id": 1, "name": "张三" },
  "city": { "id": 1, "name": "张三的城", "x": 42, "y": 87 }
}
```

尚无角色 / 城时对应字段为 `null`（序列化省略）。

### `POST /api/characters`

需登录。

```json
{ "name": "张三" }
```

`data`：`{ "id", "name", "createdAt" }`。

### `GET /api/characters/me`

需登录。无角色 → `40400`。

### `POST /api/city/found`

需登录。无 body。`data` 见下节「城」。

### `GET /api/city/me`

需登录。无城 → `40400`。

## 城 `data` 形状

```json
{
  "id": 1,
  "characterId": 1,
  "name": "张三的城",
  "x": 42,
  "y": 87,
  "createdAt": "2026-08-19T12:00:00.000Z",
  "zones": {
    "inner": [],
    "wall": [],
    "outer": []
  }
}
```

`GET /api/auth/me` 里的 `city` 只含 `id / name / x / y`，不含 `zones`。

## 表（逻辑名）

| 表 | 唯一约束 |
|----|----------|
| `sg_account` | `username_normalized` |
| `sg_character` | `account_id`；`name` |
| `sg_city` | `character_id`；`(x, y)` |
| `sg_refresh_token` | `token_hash` |

密码哈希使用 ASP.NET Identity `PasswordHasher`（PBKDF2）。开发环境可用 FreeSql `AutoSyncStructure` 建表；生产再改为显式迁移。

## 本步不做

Hangfire、Redis、SignalR 推送、客户端自选坐标、多角色、建筑槽位落库、Vue 大地图。最小网页端（登录 / 创角 / 建城）可在本步 API 完成后再建 `web/`，不阻塞本设计。
