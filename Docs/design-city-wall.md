# 城墙与城防

- **状态：** 已定稿（第 4 步）
- **对应功能：** 城墙分区的箭塔、城门、陷阱：建造 / 升级 / 查询；数值供第 5 / 6 步战斗读取
- **实现顺序：** 第 4 步（见 [路线图](design-roadmap.md)）

建造队列、到点、Hangfire、`BuildComplete` 复用 [城内建筑](design-inner-city.md) 与 [实时推送](design-realtime.md)。库存仍在城上。完整攻城战见 [行军战斗](design-march-battle.md)。

## 范围

| 做 | 不做 |
|----|------|
| 三种城防：箭塔 / 城门 / 陷阱，各最多一座 | 城墙作为大地图独立地块 |
| 建造 / 升级走 **全城同一条队列**（与城内、城外互斥） | 单独的城墙队列 |
| 查询城防等级与 `wallDefense`，供战斗读取 | 损坏、修复、攻城战动画 |
| 解锁看主殿已生效等级 | 第一版城墙耐久扣血 |

## 模型

实现上 **复用 `sg_building`**，用 `type` 区分，避免再拆一套队列。

```
City
 └─ 城墙
      ├─ arrowTower  箭塔 → 守城战力
      ├─ gate        城门 → 守城战力
      └─ trap        陷阱 → 攻方额外伤亡
```

| 项 | 约定 |
|----|------|
| 解锁 | 箭塔 / 城门需 **主殿 ≥ 2**；陷阱需 **主殿 ≥ 3**（看已生效 `palace.level`） |
| 最高级 | 10 |
| 队列 | 与城内、城外共用：全城同时只能有一个 `upgrading` |
| 未建（level=0） | 不提供加成 |

建城、第 2 / 3 步不预建城防。

## 效果（只由当前 `level` 计算）

`L` 为已生效等级（`L ≥ 1`）：

| type | 名称 | 效果 |
|------|------|------|
| `arrowTower` | 箭塔 | `wallDefense += 8 * L` |
| `gate` | 城门 | `wallDefense += 6 * L` |
| `trap` | 陷阱 | `trapBonus = 0.02 * L`（攻方伤亡率额外加上该值） |

```
wallDefense = 8 * arrowTower.level + 6 * gate.level
```

未建按 0。战斗公式见 [行军战斗](design-march-battle.md)：守方战力加 `wallDefense * 10`；攻方伤亡率加 `trapBonus`。

## 时长与消耗

公式同城内（目标等级 `L`）：

```
durationSeconds = ceil(baseDurationSeconds * 1.8^(L - 1))
cost[res]       = ceil(baseCost[res]       * 1.5^(L - 1))
```

升到 1 级的 `base*`：

| type | 秒 | 粮 | 木 | 铁 | 铜 |
|------|----|----|----|----|----|
| arrowTower | 35 | 120 | 160 | 80 | 20 |
| gate | 30 | 150 | 200 | 40 | 20 |
| trap | 40 | 80 | 80 | 120 | 40 |

错误码复用城内：`40001` / `40400` / `40906` / `40907` / `40908` / `40909`。

第一版 **不可取消、不加速**。

## HTTP API

均需登录，只操作自己的主城。时间 UTC。`GET` 带 `serverTime`。

### `GET /api/walls`

```json
{
  "cityId": 1,
  "serverTime": "2026-08-20T12:00:00.000Z",
  "resources": { "grain": 1800, "wood": 1800, "iron": 1920, "copper": 1960 },
  "resourceCap": 12000,
  "wallDefense": 0,
  "trapBonus": 0,
  "queue": null,
  "walls": []
}
```

`queue` 形状与城内相同（全城那一条）。`walls` **固定 3 项**，单项形状同城内建筑（`category` 为 `wall`），`effects` 带 `wallDefense` 或 `trapBonus`（百分数 ×100 的整数，如陷阱 1 级为 `2` 表示 2%）。

`blockedReason` 同城内：`queue` / `maxLevel` / `prerequisite` / `resources`。

### `POST /api/walls/upgrade`

```json
{ "wallType": "arrowTower" }
```

未建当 0→1。成功 `data` 与 `GET /api/walls` 同一形状。到点推已有 `BuildComplete`（`buildingType` 为城防 type）。

`GET /api/buildings` 仍只返回城内 5 种，不要把城防混进城内页。

## 网页

在城内页增加「城墙」区：三座城防、升级按钮、倒计时规则与城内相同。大地图、战斗不做。

## 本步不做

行军、造兵、攻城战、损坏修复、城墙占大地图一格。
