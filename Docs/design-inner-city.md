# 城内建筑（内政 / 科技 / 军事）

- **状态：** 已定稿（第 2 步）
- **对应功能：** 城内建造、升级、到点生效；科技与造兵只做建筑前置
- **实现顺序：** 第 2 步（见 [路线图](design-roadmap.md)）

通道、Hangfire 存储、Hub 鉴权见 [实时推送](design-realtime.md)。信封见 [统一协议](design-api.md)。城墙、城外田不在本文。

## 范围

| 做 | 不做 |
|----|------|
| 城内 5 种建筑：主殿 / 民居 / 仓库 / 书院 / 兵营 | 城墙、城外田 |
| 全城 **一条** 建造队列（串行） | 多队列、VIP 加队列 |
| 扣库存、写 `finishAt`、到点升等级 | 取消建造 / 加速（第一版） |
| 库存四种资源；建城送初始量 | 资源产出与收取（第 3 步） |
| 解锁只看主殿等级 | 独立「研究科技」队列 |
| SignalR `BuildComplete` | HTTP 轮询是否升完 |

客户端倒计时仅展示；**等级是否变化只信服务端**（推送或重新拉取）。

## 模型

每种建筑在一座城里 **最多一座**（按 `buildingType` 唯一）。未建造视为等级 0，列表仍返回目录项，便于前端画空槽。

```
City
 ├─ 资源库存：grain / wood / iron / copper（粮木铁铜）
 └─ 城内建筑
      ├─ palace   主殿   内政
      ├─ house    民居   内政
      ├─ warehouse 仓库  内政
      ├─ academy  书院   科技
      └─ barracks 兵营   军事
```

建城（第 1 步）不预建任何建筑。第 2 步上线后，已有城补资源列默认值即可，建筑仍从空槽开始。

第 1 步 `GET /api/city/me` 的 `zones.inner` 为空数组占位。本步起：

- `zones.inner` 改为建筑摘要数组（`type / level / status`），或前端改调下面的 `GET /api/buildings`
- 推荐前端以 `GET /api/buildings` 为准；`city/me` 可只保留摘要以免两套真相

### 建造队列

一座城同一时刻最多 **一个** 进行中的建造/升级：

- 进行中：该建筑 `status = upgrading`，`targetLevel = 当前等级 + 1`，`finishAt` 有值；`level` 仍为旧等级
- 空闲：全城没有任何 `upgrading`
- 数据库用部分唯一约束保证：同一 `city_id` 最多一行 `status = upgrading`
- HTTP 下达指令与 Hangfire 结算都按 `cityId` 加 Redis 锁（`lock:city:{cityId}`，建议 10 秒过期）

### 状态

| `status` | 含义 |
|----------|------|
| `idle` | 空闲（含未建造 level=0 的目录项，无行或行上 idle） |
| `upgrading` | 正在升到 `targetLevel`，未到点 |

到点后：`level = targetLevel`，`status = idle`，`targetLevel` / `finishAt` 清空。

## 资源

存在 **城** 上，供以后掠夺。四种：`grain`、`wood`、`iron`、`copper`。

| 项 | 约定 |
|----|------|
| 初始库存 | 建城或本步迁表时各 **2000** |
| 默认容量 | 无仓库时 **8000** / 每种；有仓库则按效果表 |
| 本步扣费 | 升级时四种分别校验，缺任一 → `40906`；扣完立刻落库 |
| 本步不产出 | 库存不会自己涨；涨产在第 3 步 |

容量本步只用于「当前库存不得超过 cap」（扣费后自然减少；若以后发奖再截断）。不在本步做收取。

## 目录与解锁

`buildingType` 用英文短横线小写，JSON 枚举按 camelCase 字符串。

| type | 名称 | 分类 `category` | 最高级 | 前置 |
|------|------|-----------------|--------|------|
| `palace` | 主殿 | `civil` | 10 | 无 |
| `house` | 民居 | `civil` | 10 | 主殿 ≥ 1 |
| `warehouse` | 仓库 | `civil` | 10 | 主殿 ≥ 1 |
| `academy` | 书院 | `tech` | 10 | 主殿 ≥ 2 |
| `barracks` | 兵营 | `military` | 10 | 主殿 ≥ 2 |

前置看的是 **已生效的 `level`**，不是进行中的 `targetLevel`。主殿在升 2 级途中，兵营仍不可建。

效果（只由当前 `level` 计算，配置可调）：

| type | 效果 |
|------|------|
| `palace` | 无数值；只做解锁门槛 |
| `house` | `populationCap = 50 + 100 * level`（本步只返回，不消耗人口） |
| `warehouse` | `resourceCap = 8000 + 4000 * level`（每种资源同一上限） |
| `academy` | 预留 `researchSpeedBonus`（本步恒 0，不做科研指令） |
| `barracks` | 预留造兵（第 5 步） |

## 时长与消耗

目标等级 `L`（从 `L-1` 升到 `L`，新建是 0→1）：

```
durationSeconds = ceil(baseDurationSeconds * 1.8^(L - 1))
cost[res]       = ceil(baseCost[res]       * 1.5^(L - 1))
```

`base*` 对应升到 **1 级** 的值：

| type | 秒 | 粮 | 木 | 铁 | 铜 |
|------|----|----|----|----|----|
| palace | 15 | 200 | 200 | 80 | 40 |
| house | 10 | 120 | 80 | 20 | 10 |
| warehouse | 12 | 100 | 160 | 40 | 20 |
| academy | 20 | 150 | 150 | 60 | 80 |
| barracks | 20 | 180 | 100 | 120 | 30 |

数值放 **Core 配置表**（代码常量或 JSON），不要写死在 Controller。改时长不必改协议。第一版联调已把 `baseDurationSeconds` 缩短约一半，方便一局内升到兵营。

已满级：`next` 为 `null`，再点升级 → `40908`。

## 下达与结算

```
POST /api/buildings/upgrade { buildingType }
  → Redis 锁城
  → 校验城、队列空闲、前置、未满级、资源够
  → 扣资源；写入 upgrading + finishAt + targetLevel
  → 调度 Hangfire 延迟到 finishAt
  → 立刻返回（旧 level + finishAt + 扣后库存）

Hangfire CompleteInnerBuilding(cityId, buildingType, targetLevel)
  → 同锁
  → 若当前 level ≥ targetLevel 或不是 upgrading：直接成功（幂等）
  → 否则 level = targetLevel，清队列字段
  → SignalR 组 city:{cityId} 推 BuildComplete
```

Hangfire 失败允许重试；业务必须幂等。Job 参数用 `cityId + buildingType + targetLevel`，不要只靠 Hangfire JobId。

进程重启后未到期任务仍要执行：Hangfire 用 PostgreSQL **独立 schema `hangfire`**，不经过 FreeSql。详见 [实时推送](design-realtime.md)。

## 错误码（本步）

| 码 | HTTP | 场景 |
|----|------|------|
| `40001` | 200 | `buildingType` 不在目录 |
| `40100` | 401 | 未登录 |
| `40400` | 200 | 尚未建城 |
| `40906` | 200 | 资源不足 |
| `40907` | 200 | 本城队列占用中（已有升级） |
| `40908` | 200 | 已满级 |
| `40909` | 200 | 前置未满足（主殿等级不够） |

缺资源时 `message` 写清缺哪一种即可，不必再拆错误码。

## HTTP API

均需登录；只能操作自己的主城。时间 UTC。`GET` 带 `serverTime` 供倒计时校准。

### `GET /api/buildings`

`data`：

```json
{
  "cityId": 1,
  "serverTime": "2026-08-19T12:00:00.000Z",
  "resources": { "grain": 2000, "wood": 2000, "iron": 2000, "copper": 2000 },
  "resourceCap": 8000,
  "populationCap": 50,
  "queue": null,
  "buildings": []
}
```

有队列时：

```json
"queue": {
  "buildingType": "palace",
  "targetLevel": 1,
  "finishAt": "2026-08-19T12:00:30.000Z"
}
```

`buildings` **固定 5 项**（目录全量）。单项：

```json
{
  "type": "palace",
  "name": "主殿",
  "category": "civil",
  "level": 0,
  "maxLevel": 10,
  "status": "idle",
  "targetLevel": null,
  "finishAt": null,
  "effects": {},
  "next": {
    "level": 1,
    "durationSeconds": 30,
    "cost": { "grain": 200, "wood": 200, "iron": 80, "copper": 40 }
  },
  "blockedReason": null
}
```

`blockedReason`（可升级则为 `null`）：`queue`（全城在建）、`maxLevel`、`prerequisite`、`resources`。仅展示用；真正拦截以 POST 错误码为准。

`effects` 按当前 `level` 填已生效字段，未建可 `{}` 或带默认 cap 说明；列表级已有 `resourceCap` / `populationCap`。

### `POST /api/buildings/upgrade`

```json
{ "buildingType": "palace" }
```

未建当作 0→1，已建当作 `level → level+1`。成功 `data` 与 `GET /api/buildings` 同一形状（含扣费后库存与 `queue`）。

无 body 或 type 非法 → `40001`。

### 不提供

- `POST` 取消
- `GET` 轮询专用接口（刷新用同一个 GET）

## SignalR `BuildComplete`

连接 `/hubs/game`，JWT 与 HTTP 相同。连上后服务端把连接加入 `city:{cityId}`（无城则不入组）。

事件名：`BuildComplete`。payload 仍是信封，`data`：

```json
{
  "cityId": 1,
  "buildingType": "palace",
  "level": 1,
  "serverTime": "2026-08-19T12:00:30.000Z",
  "resources": { "grain": 1800, "wood": 1800, "iron": 1920, "copper": 1960 },
  "resourceCap": 8000,
  "populationCap": 50
}
```

前端收到后应再 `GET /api/buildings` 或按 payload 把对应项 `level` 改掉并清 `queue`。倒计时归零 **不能** 本地自行加等级。

## 表（逻辑名）

| 表 / 列 | 说明 |
|---------|------|
| `sg_city.grain` 等四列 | `int`，默认 2000；非负 |
| `sg_building` | `id`，`city_id`，`type`，`level`，`status`，`target_level`，`finish_at`，`updated_at` |

约束：

- `uk_building_city_type (city_id, type)`
- 部分唯一：`uk_building_city_queue (city_id) WHERE status = upgrading`
- `level` 0～10；未建可以不插行（GET 用目录补 level=0）

升级开始时若无行则插入 `level=0, status=upgrading`。

## 网页

在现有 `web/` 增加城内页：列出 5 建筑、库存、升级按钮、按 `finishAt` 与 `serverTime` 倒计时。登录后 `HubConnection` 监听 `BuildComplete`。大地图、城墙、田地仍不做。

## 本步不做

城外产出、城防战斗加成、造兵、科研指令、取消/加速、多队列、客户端自改等级。
