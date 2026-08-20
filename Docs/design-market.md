# 市集兑换与同盟运输

- **状态：** 已定稿（第 10 步）
- **对应功能：** NPC 市集按比例兑换资源；同联盟互相运输资源。两者都按行军公式计时
- **实现顺序：** 第 10 步（见 [路线图](design-roadmap.md)）

行军距离公式见 [行军战斗](design-march-battle.md)。占格与视野见 [大地图](design-world-map.md)。同盟判定见 [联盟](design-alliance.md)。通道、Hangfire、Hub 见 [实时推送](design-realtime.md)。

## 范围

| 做 | 不做 |
|----|------|
| 地图上生成 NPC 市集点（不可攻打） | 玩家挂单自由市场、城内「市集」建筑 |
| 一种资源按固定比值换另一种；出发立刻扣付出量，到点入仓 | 取消 / 加速运输、途中被劫、押运兵力 |
| 同联盟单向运输资源，单程计时 | 联盟仓库、向非同盟 / 自己运输 |
| 运输占用独立队列，不占出征上限 | AI 自动交易 |

## 市集点

启动时若数量不足 `WorldMap:MarketCount`（默认 8），按空地随机补齐。一座市集占 **1 格**，与城、据点互斥。

| 项 | 约定 |
|----|------|
| 名称 | `市集·{x},{y}` |
| 占领 | 不可占领、不可作为出征目标 |
| 刷新 | 不消失、不恢复驻军（没有驻军） |

表 `sg_market`：`id, name, x, y`。`(x, y)` 唯一。

## 兑换公式

价值与税率放 Core 配置表，可调。

| 资源 | 价值 |
|------|------|
| 粮 `grain` | 10 |
| 木 `wood` | 10 |
| 铁 `iron` | 15 |
| 铜 `copper` | 20 |

```
toAmount = floor(fromAmount * value(from) * (1 - tax) / value(to))
tax = 0.10
```

例：付 1000 粮换木 → `floor(1000 * 10 * 0.9 / 10) = 900` 木。

| 项 | 约定 |
|----|------|
| 同种互兑 | 禁止 → `40928` |
| 最少付出 | 100 |
| 换得必须 | ≥ 1，否则 `40928` |
| 单次运量上限 | `cargoCap = 2000 + 1000 * warehouse.level`（按付出总量） |
| 下单时刻 | 立刻扣付出量，并 **锁定** 换得量；到点按锁定值入仓，不重新算汇率 |

同盟运输 **不抽税**，可一次带四种资源，总量仍受 `cargoCap`。

## 运输时间

复用 `MarchTiming`：曼哈顿 × `SecondsPerTile`，最短 `MinMarchSeconds`。开发环境仍用 `appsettings.Development.json` 的 5 秒/格、最短 10 秒。

| 类型 | 时长 | 出发 | 到点 |
|------|------|------|------|
| 市集兑换 | `2 * duration(城 → 市集)`（去再回） | 扣付出资源 | 换得入出发城仓库 |
| 同盟运输 | `duration(己城 → 盟友城)` | 扣发送方 | 入接收方仓库 |

运输 **不扣兵**、不进 `sg_march`、不占 `MaxMarchesPerCity`。独立上限 `WorldMap:MaxTransportsPerCity`（默认 3），只计本城 **发出** 且尚未结算的运输。超出 → `40927`。

地图展示：行军线插值仅前端。市集运输前半程城→市集、后半程市集→城；同盟运输单向。结算以服务端 `arriveAt` 为准。

## 模型

```
Transport
 ├─ kind: market | aid
 ├─ fromCityId
 ├─ toCityId（市集为 0）
 ├─ targetId（市集 Id 或盟友城 Id）
 ├─ fromX/Y、toX/Y（市集目标为市集格）
 ├─ pay*（付出，已从出发城扣除）
 ├─ credit*（将入账；市集下单时锁定）
 ├─ departAt / arriveAt
 └─ status: inTransit | settled
```

表 `sg_transport`。同一 `transportId` 只结算一次。

## 结算

Hangfire `CompleteTransport(transportId)`，在 `arriveAt` 触发。失败可重试；业务幂等。进程重启扫描已到期仍为 `inTransit` 的运输并补结算。

- 入仓按仓库上限 **每种资源分别截断**（与掠夺相同）；溢出丢弃，邮件写明
- 途中退盟：**货已上路，仍送达**
- 市集消失或盟友城没了：把 `pay*` **退回** 出发城（仍截断上限）
- 出发锁出发城；入账锁目标城；两城时先锁较小 `cityId`

SignalR：

| 事件 | 组 | 时机 |
|------|----|------|
| `TransportArrived` | `city:{fromCityId}` | 任意运输结算 |
| `ResourceReceived` | `city:{toCityId}` | 同盟运输送达接收方 |

payload 仍是统一信封。`data` 含结算摘要、实际入账、溢出、该城当前库存。

邮件 `type=system`，`relatedType=transport`：

| 事件 | 收件人 |
|------|--------|
| 兑换完成 / 退回 | 出发城角色 |
| 援助送达 | 发送方与接收方各一封 |

## HTTP

均需登录，且已有主城。时间 UTC。

### `GET /api/markets`

```json
{
  "cityId": 1,
  "serverTime": "2026-08-20T12:00:00.000Z",
  "resources": { "grain": 2000, "wood": 2000, "iron": 2000, "copper": 2000 },
  "resourceCap": 8000,
  "cargoCap": 2000,
  "taxRate": 0.1,
  "minAmount": 100,
  "values": { "grain": 10, "wood": 10, "iron": 15, "copper": 20 },
  "rates": [
    { "fromResource": "grain", "toResource": "wood", "fromAmount": 100, "toAmount": 90 }
  ],
  "markets": [
    {
      "id": 1,
      "name": "市集·10,12",
      "x": 10,
      "y": 12,
      "durationSeconds": 50,
      "roundTripSeconds": 100
    }
  ],
  "transports": []
}
```

`transports` 为本城发出且尚未结算的运输，以及运往本城尚未送达的援助。

### `POST /api/markets/trade`

```json
{ "marketId": 1, "fromResource": "grain", "toResource": "wood", "amount": 1000 }
```

立刻返回与 `GET` 相同的概览（含新运输的 `arriveAt` 与锁定换得量）。

### `POST /api/markets/aid`

```json
{ "targetCityId": 2, "grain": 200, "wood": 0, "iron": 0, "copper": 0 }
```

发送方未入盟 → `40922`。非同联盟 → `40926`。运给自己 → `40925`。

### `GET /api/world`

在原有 `cities` / `outposts` / `marches` 上增加 `markets[]` 与 `transports[]`。`transports[].mine` 为出发城或目的城是自己。

## 错误码

| 码 | 场景 |
|----|------|
| `40925` | 不能运给自己 |
| `40926` | 非同联盟不可运输 |
| `40927` | 运输数量已达上限 |
| `40928` | 运量超限、同种兑换、数量过小或换得为 0 |
| `40906` | 资源不足 |
| `40400` | 市集 / 目标城不存在 |
| `40922` | 发送方未加入联盟 |

## 验收

- 选市集用 1000 粮换木：立刻粮 -1000；未到点木不变；到点木 +900（仓库未满时）
- 同种兑换、付出 < 100 → `40928`
- 两人组盟后援助成功；非同盟 `40926`；运给自己 `40925`
- 途中退盟，援助仍送达接收方
- 大地图能看到市集标记与运输线；市集运输线去程再折返
