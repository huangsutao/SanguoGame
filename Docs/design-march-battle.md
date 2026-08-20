# 行军与战斗结算

- **状态：** 已定稿（第 5 步）
- **对应功能：** 征兵、出兵打 NPC 据点、到达后一次性结算、战报
- **实现顺序：** 第 5 步（见 [路线图](design-roadmap.md)）；打玩家见第 6 步 [PvP](design-pvp.md)

通道、Hangfire、Hub 见 [实时推送](design-realtime.md)。NPC 据点与格子见 [大地图](design-world-map.md)。

## 范围

| 做 | 不做 |
|----|------|
| 兵营解锁后征兵（步 / 弓 / 骑），兵力存在城上 | 帧同步、客户端预测 |
| `POST` 出征立刻返回 `arriveAt` | 全图实时视野 |
| Hangfire 到点一次性结算并出战报 | 多回合回合制、攻城耐久战 |
| SignalR `MarchArrived` | 取消行军、加速行军 |
| NPC 与玩家走同一套 March / Battle | 本步打其他玩家（第 6 步） |

## 兵力

三种兵存在 **城** 上：`infantry` / `archer` / `cavalry`。出征时从城上扣除，结算后 **存活兵力返回出发城**。

| 项 | 约定 |
|----|------|
| 兵种上限 | `troopCap = 30 + 40 * barracks.level`（三种合计；含驻城与行军中） |
| 解锁 | 步兵：兵营 ≥ 1；弓兵：兵营 ≥ 2；骑兵：兵营 ≥ 3 |
| 征兵 | 即时扣资源加兵，不占建造队列 |
| 未建兵营 | 不可征兵、不可出征 |

每 1 名兵消耗（即时）：

| type | 粮 | 木 | 铁 | 铜 |
|------|----|----|----|----|
| `infantry` | 20 | 5 | 10 | 0 |
| `archer` | 10 | 20 | 8 | 5 |
| `cavalry` | 15 | 10 | 20 | 5 |

一次征兵 `count` 为 1～100。超出上限 → `40916`。兵营不够 → `40915`。资源不足 → `40906`。

## 行军

出发格 = 本城 `(x, y)`。目标格 = 据点或玩家城坐标。客户端不自算到达时间。

```
distance = |x1 - x2| + |y1 - y2|          # 曼哈顿
durationSeconds = max(MinMarchSeconds, distance * SecondsPerTile)
ArriveAt = now + durationSeconds
```

配置（`WorldMap`）：`SecondsPerTile` 默认 20，`MinMarchSeconds` 默认 30。同一城同时最多 `MaxMarchesPerCity` 支行军（默认 3），超出 → `40917`。

兵力必须至少 1，且不超过城上现有；不足 → `40910`。目标不存在 → `40400`。打自己的城 → `40914`。

出征与结算都要对出发城（及守方城 / 据点）串行。

## 战斗公式

到达后一次性结算。随机只影响 ±10% 战力波动与伤亡率区间，种子为 `marchId`（可复现）。

```
power(inf, arc, cav) = inf * 10 + arc * 12 + cav * 14

atk = power(攻方兵力) * (100 + academy.level * 2) / 100
def = power(守方兵力) + wallDefense * 10 + outpostBasePower

atk' = atk * (90 + rng(0..20)) / 100
def' = def * (90 + rng(0..20)) / 100

attackerWon = atk' >= def'          # 战力相等攻方胜
```

- NPC 据点没有城防建筑：`wallDefense = 0`，`outpostBasePower` 见 [大地图](design-world-map.md)；守方兵力为据点驻军（只记步兵当量）。
- 玩家城：`outpostBasePower = 0`，守方兵力为 **当时仍驻城的兵**（行军在外的不参战），城防见 [城墙](design-city-wall.md)。
- 书院未建按 0。

伤亡（对参战兵力）：

```
若攻方胜：攻方伤亡率 0.15～0.30；守方伤亡率 0.55～0.80
若攻方败：攻方伤亡率 0.55～0.80；守方伤亡率 0.15～0.30
攻方伤亡率 += trapBonus（仅打玩家城时；据点为 0）
剩余 = floor(原兵力 * (1 - 伤亡率))，每种兵分别取整
```

NPC 战败：驻军清零，`recoverAt = now + OutpostRecoverSeconds`（默认 7200）。到期后按目录恢复满编（读取时惰性恢复，不另开 tick）。NPC 战胜：驻军按守方剩余写回。

攻方无论胜负，存活兵力加回出发城（受 `troopCap` 截断，超出部分消失）。战胜 NPC 时把据点战利品加入攻方仓库（受 `resourceCap` 截断）。

## 战报

第一版只出 **结果战报**，不做回放。同一 `marchId` 只生成一行（结算幂等）。

字段：攻守双方战前 / 战后兵力、是否攻方胜、掠夺资源、随机种子、一句话 `summary`。

## HTTP API

均需登录。时间 UTC。

### `GET /api/army`

```json
{
  "cityId": 1,
  "serverTime": "2026-08-20T12:00:00.000Z",
  "resources": { "grain": 1800, "wood": 1800, "iron": 1920, "copper": 1960 },
  "resourceCap": 12000,
  "troops": { "infantry": 20, "archer": 0, "cavalry": 0 },
  "troopCap": 70,
  "barracksLevel": 1,
  "wallDefense": 8,
  "protectionUntil": null,
  "marches": []
}
```

`marches` 为本城尚未结算的行军。

### `POST /api/army/recruit`

```json
{ "troopType": "infantry", "count": 10 }
```

成功 `data` 与 `GET /api/army` 同一形状。

### `POST /api/army/march`

```json
{ "targetType": "outpost", "targetId": 3, "infantry": 20, "archer": 0, "cavalry": 0 }
```

`targetType`：`outpost`（本步）或 `city`（第 6 步）。成功立刻返回行军（含 `arriveAt`），兵力已从城上扣除。

### `GET /api/reports?page=&pageSize=`

与本城有关的战报（攻方或守方是本城），新的在前。分页约定见 [统一协议](design-api.md)。

单项：

```json
{
  "id": 1,
  "marchId": 9,
  "attackerCityId": 1,
  "defenderType": "outpost",
  "defenderId": 3,
  "attackerWon": true,
  "attackerBefore": { "infantry": 20, "archer": 0, "cavalry": 0 },
  "attackerAfter": { "infantry": 16, "archer": 0, "cavalry": 0 },
  "defenderBefore": { "infantry": 40, "archer": 0, "cavalry": 0 },
  "defenderAfter": { "infantry": 0, "archer": 0, "cavalry": 0 },
  "loot": { "grain": 80, "wood": 80, "iron": 40, "copper": 20 },
  "seed": 9,
  "summary": "攻克村落，缴获粮80 木80 铁40 铜20",
  "createdAt": "2026-08-20T12:10:00.000Z"
}
```

## SignalR `MarchArrived`

连接 `/hubs/game`。事件名：`MarchArrived`。payload 仍是信封，`data` 为战报对象（同上）。推到攻方组 `city:{attackerCityId}`。

## Hangfire

Job：`CompleteMarch(marchId)`，在 `arriveAt` 触发。已结算则直接成功。启动时扫描已到期仍为 `marching` 的行补结算。

## 表

| 表 | 说明 |
|----|------|
| `sg_city` | 增加 `infantry` / `archer` / `cavalry`（默认 0）；`protection_until` 第 6 步用 |
| `sg_outpost` | NPC 据点，见 [大地图](design-world-map.md) |
| `sg_march` | 行军：出发城、目标类型与 Id、三种兵、出发/到达时间、`status` |
| `sg_battle_report` | 战报；`march_id` 唯一 |

`status`：`marching` / `settled`。

## 网页

出征面板：显示兵力、征兵、选择附近据点出征、行军列表（倒计时仅展示）、战报列表。大地图画布仍可第 8 步。

## 本步不做

打玩家、保护罩、掠夺玩家仓库、取消/加速、客户端自改兵力。
