# 大地图与 NPC 据点

- **状态：** 已定稿（格子规则第 1 步；据点第 5 步；视野第 8 步）
- **对应功能：** 逻辑格子、NPC 据点、前端按有内容的格子展示
- **实现顺序：** 格子规则与 [建城](design-account-city.md) 同期；据点与战斗在第 5 步；画布在第 8 步

行军距离与结算见 [行军战斗](design-march-battle.md)。

## 已定稿：格子规则（第 1 步）

地图是 **坐标索引**，不是 RTS 地形。只持久化有内容的格子；空地不存行。

| 项 | 约定 |
|----|------|
| 规模 | 200 × 200（配置 `WorldMap:Width` / `Height`，默认 200） |
| 坐标 | 整数；`x ∈ [0, Width)`，`y ∈ [0, Height)`；含 `(0,0)` |
| 占格 | 一座主城占 **1 格**；一座 NPC 据点占 **1 格**；一座市集占 **1 格** |
| 占用存储 | `sg_city (x, y)`、`sg_outpost (x, y)`、`sg_market (x, y)` 各自唯一；选址时都算占用 |
| 选址 | 客户端不传坐标。在 `[0,W) × [0,H)` 均匀随机取格，占用则重抽，最多 `WorldMap:PlacementMaxAttempts` 次（默认 64） |
| 距离 | 第一版 **不** 做与邻城最小间距 |
| 行军距离 | 曼哈顿 `\|dx\| + \|dy\|`，见第 5 步 |

不做：全图实时视野、每帧同步、用 DOM 铺全部格子。

## NPC 据点（第 5 步）

两类据点共用 `sg_outpost`，用 `kind` 区分。**常驻点规则不变**；流寇是后来加的限时点。

### 常驻（`kind = permanent`）

启动时若常驻数量不足 `WorldMap:OutpostCount`（默认 24），按三种类型尽量均分补齐。只数 `kind = permanent`，不把流寇算进这个配额。

| type | 名称 | 驻军（步兵当量） | `outpostBasePower` | 战利品粮/木/铁/铜 |
|------|------|------------------|--------------------|-------------------|
| `village` | 村落 | 40 | 200 | 80 / 80 / 40 / 20 |
| `camp` | 营寨 | 80 | 500 | 150 / 150 / 80 / 40 |
| `fortress` | 关隘 | 150 | 1000 | 300 / 250 / 150 / 80 |

| 项 | 约定 |
|----|------|
| 占领 | 第一版 **不占领**；战胜后掠走战利品，据点仍在原格 |
| 刷新 | 战败后 `garrison = 0`，`recoverAt = now + OutpostRecoverSeconds`（默认 7200）。读取或作为目标时若已到期，按目录恢复满编与战利品 |
| 名称 | `{名称}·{x},{y}`，如 `村落·12,34` |
| `expiresAt` | 常驻为 `null` |

### 流寇（`kind = roaming`）

空地上随机出现的限时据点。到期从地图删掉并腾格；**攻方战胜后也删掉**（不进入 `recoverAt`）。攻方战败则留下残兵，直到到期。

| type | 名称 | 驻军 | `outpostBasePower` | 战利品粮/木/铁/铜 |
|------|------|------|--------------------|-------------------|
| `bandit` | 流寇 | 25 | 120 | 100 / 80 / 30 / 20 |
| `raider` | 马贼 | 50 | 280 | 180 / 120 / 80 / 40 |
| `warband` | 流寇大营 | 90 | 600 | 280 / 220 / 120 / 60 |

| 项 | 约定 |
|----|------|
| 数量 | 存活流寇不足 `WorldMap:RoamingOutpostCount`（默认 8）时，tick 在空地补到该数量；三种类型轮流出 |
| 寿命 | 生成时 `expiresAt = now + RoamingOutpostLifetimeSeconds`（默认 1800；开发环境 180） |
| tick | Hangfire 周期 `RoamingOutpostTickMinutes`（默认 1）：先删到期，再补生成。启动补种子之后也会跑一次 |
| 展示 | `GET /api/world` **不返回** 已到期流寇，并顺手删行，避免占格 |
| 出征 | 已到期或已删 → `40400`「据点不存在」。行军途中到期 / 被别人打掉：到达后兵力返回，战报「目标据点已消失」，无缴获 |
| 名称 | `{名称}·{x},{y}`，如 `流寇·18,7` |

表 `sg_outpost`：`id, type, name, x, y, garrison, recover_at, kind, expires_at`。`(x, y)` 仍唯一。`kind`：`permanent = 0`，`roaming = 1`。

`GET /api/world` 的据点多项增加 `kind`、`expiresAt`（常驻可省略 `expiresAt`）。

## 视野与画布（第 8 步）

地图实体很少（城 + 据点 + 行军），第一版 `GET /api/world` **一次返回全部有内容的格子**，不做分块分页。前端用 Canvas 画标记，不要用 DOM 铺 200×200 格。

### `GET /api/world`

需登录。无城仍可返回地图（`origin` 为 `(0,0)`）；有城则以本城为原点。

```json
{
  "width": 200,
  "height": 200,
  "serverTime": "2026-08-20T12:00:00.000Z",
  "origin": { "x": 42, "y": 87 },
  "cities": [
    { "id": 1, "name": "张三的城", "x": 42, "y": 87, "owner": "self", "protected": false }
  ],
  "outposts": [
    { "id": 3, "type": "village", "name": "村落·10,12", "x": 10, "y": 12, "garrison": 40, "kind": "permanent" },
    { "id": 8, "type": "bandit", "name": "流寇·18,7", "x": 18, "y": 7, "garrison": 25, "kind": "roaming", "expiresAt": "2026-08-20T12:30:00.000Z" }
  ],
  "marches": [
    {
      "id": 9,
      "fromX": 42,
      "fromY": 87,
      "toX": 10,
      "toY": 12,
      "departAt": "2026-08-20T12:00:00.000Z",
      "arriveAt": "2026-08-20T12:10:00.000Z",
      "status": "marching",
      "mine": true
    }
  ],
  "markets": [
    { "id": 1, "name": "市集·15,20", "x": 15, "y": 20 }
  ],
  "transports": []
}
```

`owner`：`self` / `ai` / `player`。`protected` 仅对他人城有意义。行军位置展示按 `departAt`～`arriveAt` 线性插值，**仅前端**，结算仍以服务端 `arriveAt` 为准。市集与运输见第 10 步 [市集](design-market.md)：市集运输线前半程去市集、后半程折返。

拖拽、滚轮缩放；点击据点 / 玩家城可作为出征目标；点击市集进入兑换，不出征。不做全图实时视野广播。
