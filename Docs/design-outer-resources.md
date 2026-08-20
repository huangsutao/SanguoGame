# 城外资源田

- **状态：** 已定稿（第 3 步）
- **对应功能：** 城外矿、木场、良田、铜矿的建造、升级、产出与收取
- **实现顺序：** 第 3 步（见 [路线图](design-roadmap.md)）

建造队列、到点、Hangfire、`BuildComplete` 复用 [城内建筑](design-inner-city.md) 与 [实时推送](design-realtime.md)。库存仍在城上：`grain / wood / iron / copper`。掠夺公式见第 6 步 [玩家对战](design-pvp.md)，本文只预留可被收走的「田上存量」。

## 范围

| 做 | 不做 |
|----|------|
| 四种田：良田 / 木场 / 铁矿 / 铜矿，各最多一座 | 多块同类型田、随机空地再占格 |
| 建造 / 升级走 **全城同一条队列**（与城内互斥） | 单独的城外队列 |
| 按「上次结算时间」算产出；点收取才写库 | 每秒 / 每分钟 tick 刷库存 |
| 田上有独立容量；入库受仓库 `resourceCap` 限制 | 被掠完整规则（第 6 步） |
| `GET` 列表时当场算出可收取量，不写库 | HTTP 轮询刷资源 |

## 模型

城外田与城内建筑一样，每种 `type` 一座，未建为等级 0。实现上 **复用 `sg_building`**（或等价表），用 `type` 区分，避免再拆一套队列。

```
City 库存 grain/wood/iron/copper（上限看仓库）
 └─ 城外
      ├─ farm        良田 → grain
      ├─ lumber      木场 → wood
      ├─ ironMine    铁矿 → iron
      └─ copperMine  铜矿 → copper
```

| 项 | 约定 |
|----|------|
| 解锁 | 四种均需 **主殿已生效等级 ≥ 1**（看 `palace.level`，不看升级中的目标级） |
| 最高级 | 10 |
| 队列 | 与城内共用：全城同时只能有一个 `upgrading`（部分唯一索引已按 `city_id`） |
| 升级中产出 | **仍按当前已生效 `level` 产**；未到点不按 `targetLevel` |
| 未建（level=0） | 不产出；收取为 0 |

建城、第 2 步不预建田。主殿升到 1 级后即可建田。

## 产出（按上次收取时间）

每座 **已建成**（`level ≥ 1`）的田存 `lastCollectedAt`（UTC）。

```
elapsedSeconds = max(0, now - lastCollectedAt)
pending = floor(ratePerHour * elapsedSeconds / 3600)
pending = min(pending, fieldCap)
```

- `ratePerHour`、`fieldCap` 只由 **当前 `level`** 决定。
- **禁止** 用后台每秒改库存。`GET /api/fields` 用上面公式现算 `pending`，不 UPDATE。
- 只有 **收取成功**（以及第 6 步被掠）才改 `lastCollectedAt`。
- 0→1 建造 **到点完成时** 把 `lastCollectedAt` 设为完成时刻，从此开始积攒。

### 速率与田上容量

`L` 为已生效等级（`L ≥ 1`）：

```
ratePerHour = baseRatePerHour * L
fieldCap    = baseFieldCap * L
```

司农院加成见 [科技](design-tech.md)：速率与田容再乘 `(100 + 5 * resourceHall.level) / 100` 后取整。列表、收取、被掠用同一套数。

| type | 名称 | 产出 | 1 级每小时 | 1 级田上容量 |
|------|------|------|------------|--------------|
| `farm` | 良田 | `grain` | 600 | 1500 |
| `lumber` | 木场 | `wood` | 500 | 1500 |
| `ironMine` | 铁矿 | `iron` | 400 | 1500 |
| `copperMine` | 铜矿 | `copper` | 300 | 1500 |

数值放 Core 配置表，可调。第一版联调已把 `baseRatePerHour` 调到约 10 倍、田上容量约 5 倍，避免开局后长时间空等。

取整：`pending` 向下取整为 int；不足 1 的部分留在时间里（不改 `lastCollectedAt` 则下次还能攒够）。

## 收取

`POST /api/fields/collect`，可收一座或全部。

对每一座要收的田：

1. 按公式算 `pending`。
2. `space = resourceCap - 城上该资源库存`（`resourceCap` 与城内仓库相同，四种共用一个上限）。
3. `take = min(pending, max(0, space))`。
4. 城库存 `+= take`。
5. 剩余 `left = pending - take`：
   - `left = 0`：`lastCollectedAt = now`
   - `left > 0`：倒推时间，让田上仍相当于 `left`  
     `lastCollectedAt = now - floor(left / ratePerHour * 3600)` 秒  
     （`ratePerHour = 0` 时直接 `lastCollectedAt = now`）

连续点两次收取：第一次把 `pending` 入库（或受仓库卡住），第二次 `elapsed ≈ 0`，`pending` 为 0 或接近 0。符合验收。

仓库已满且 `take = 0`：仍 HTTP 200，该田 `collected` 为 0，`message` 可提示「仓库已满」。不新开错误码。

未建或 `pending = 0`：该田 `collected` 为 0，不报错。

收取与升级都要对城串行（行锁 / 同一套城锁），防止连点双收。

## 建造 / 升级

与城内同一套：扣库存、`status = upgrading`、`finishAt`、Hangfire 到点、`BuildComplete`（`buildingType` 为田的 type）。

时长与消耗公式同城内（目标等级 `L`）：

```
durationSeconds = ceil(baseDurationSeconds * 1.8^(L - 1))
cost[res]       = ceil(baseCost[res]       * 1.5^(L - 1))
```

升到 1 级的 `base*`：

| type | 秒 | 粮 | 木 | 铁 | 铜 |
|------|----|----|----|----|----|
| farm | 12 | 150 | 80 | 20 | 10 |
| lumber | 12 | 80 | 150 | 20 | 10 |
| ironMine | 15 | 100 | 100 | 80 | 20 |
| copperMine | 15 | 100 | 80 | 40 | 80 |

错误码复用城内：`40001` 未知 type、`40400` 无城、`40906` 缺资源、`40907` 全城队列忙（含正在升城内建筑）、`40908` 满级、`40909` 主殿不到 1 级。

第一版同样 **不可取消、不加速**。

升级开始时不重置 `lastCollectedAt`，以免把已攒未收清掉。

## 被掠（预留）

田上 `pending`（由 `lastCollectedAt` + 等级算出）是可被掠的「田上资源」；城库存是仓库。第 6 步再定抢田还是抢仓、比例和保护 CD。本步表结构不要做成无法表示田上存量（保留 `lastCollectedAt` 即可）。

## HTTP API

均需登录，只操作自己的主城。时间 UTC。`GET` 带 `serverTime`。

### `GET /api/fields`

```json
{
  "cityId": 1,
  "serverTime": "2026-08-19T12:00:00.000Z",
  "resources": { "grain": 1800, "wood": 1800, "iron": 1920, "copper": 1960 },
  "resourceCap": 12000,
  "queue": null,
  "fields": []
}
```

`queue` 形状与城内相同（全城那一条，可能是城内建筑或某块田）。

`fields` **固定 4 项**。单项：

```json
{
  "type": "farm",
  "name": "良田",
  "resource": "grain",
  "level": 1,
  "maxLevel": 10,
  "status": "idle",
  "targetLevel": null,
  "finishAt": null,
  "ratePerHour": 60,
  "fieldCap": 300,
  "pending": 12,
  "lastCollectedAt": "2026-08-19T11:48:00.000Z",
  "next": {
    "level": 2,
    "durationSeconds": 45,
    "cost": { "grain": 225, "wood": 120, "iron": 30, "copper": 15 }
  },
  "blockedReason": null
}
```

`pending` 按请求时的 `serverTime` 现算。`blockedReason` 同城内：`queue` / `maxLevel` / `prerequisite` / `resources`。

### `POST /api/fields/upgrade`

```json
{ "fieldType": "farm" }
```

未建当 0→1。成功 `data` 与 `GET /api/fields` 同一形状。到点推已有 `BuildComplete`。

### `POST /api/fields/collect`

```json
{ "fieldType": "farm" }
```

`fieldType` 省略或 `null`：四座都收。

`data`：

```json
{
  "cityId": 1,
  "serverTime": "2026-08-19T12:00:00.000Z",
  "resources": { "grain": 1812, "wood": 1800, "iron": 1920, "copper": 1960 },
  "resourceCap": 12000,
  "collected": { "grain": 12, "wood": 0, "iron": 0, "copper": 0 },
  "fields": []
}
```

`collected` 为本次实际入库（受仓库截断后）。不推 SignalR（玩家自己点的，看 HTTP 即可）。

不提供取消、不提供单独的轮询接口。

## 表

在 `sg_building` 上：

| 列 | 说明 |
|----|------|
| 已有 `type / level / status / target_level / finish_at` | 田与城内共用队列 |
| `last_collected_at` | 城内建筑保持 `null`；田在首次建成后必有值 |

`GET /api/buildings` 仍只返回城内目录（第 11 步起 8 种），不要把田混进城内页。

## 网页

在现有城内页下增加「城外」：四座田、`pending`、建造/升级、一键收取。倒计时规则与城内相同。大地图、城墙不做。

## 本步不做

城墙、行军、造兵、科研、多块同类型田、产出进大地图格子、掠夺结算、每秒写库。
