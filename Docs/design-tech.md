# 分科科技建筑与加成

- **状态：** 已定稿（第 11 步）
- **对应功能：** 军事 / 城防 / 资源三座科技建筑；升级即加成，无独立科研队列
- **实现顺序：** 第 11 步（见 [路线图](design-roadmap.md)）

建造队列、解锁看已生效等级、到点结算仍见 [城内建筑](design-inner-city.md)。战斗见 [行军战斗](design-march-battle.md)。田产出见 [城外资源](design-outer-resources.md)。城墙见 [城墙](design-city-wall.md)。

## 范围

| 做 | 不做 |
|----|------|
| 三座城内科技建筑：演武堂 / 城防署 / 司农院 | 科技树节点、研究指令、独立科研队列 |
| 书院仍为总学，并作为三座分科的前置 | 取消书院已有的攻方战力加成 |
| 加成只由 **已生效等级** 计算，配置放 Core | 加速、重置科技、联盟科技 |
| 列表 `effects` 写出已生效数值；军队 / 田 / 城墙接口用加成后的数 | 客户端自算战斗 |

升级建筑就是「点科技」。第一版不加第二套队列。

## 目录

| type | 名称 | 分类 | 最高级 | 前置 |
|------|------|------|--------|------|
| `academy` | 书院 | `tech` | 10 | 主殿 ≥ 2（已有） |
| `drillHall` | 演武堂 | `tech` | 10 | 主殿 ≥ 3 **且** 书院 ≥ 1 |
| `defenseHall` | 城防署 | `tech` | 10 | 主殿 ≥ 3 **且** 书院 ≥ 1 |
| `resourceHall` | 司农院 | `tech` | 10 | 主殿 ≥ 3 **且** 书院 ≥ 1 |

前置看已生效 `level`。书院在升 1 级途中，三座分科仍不可建。

升到 1 级的基数（再按城内同一套 `1.8` 时长、`1.5` 费用）：

| type | 秒 | 粮 | 木 | 铁 | 铜 |
|------|----|----|----|----|----|
| drillHall | 20 | 180 | 100 | 120 | 40 |
| defenseHall | 20 | 120 | 180 | 80 | 30 |
| resourceHall | 18 | 160 | 140 | 40 | 40 |

`GET /api/buildings` 的 `buildings` **固定 8 项**（原 5 项 + 本步 3 项）。`blockedReason` 仍为 `prerequisite` / `queue` / `resources` / `maxLevel`；POST 失败 `message` 写清缺主殿还是缺书院。

## 加成（`L` = 已生效等级，`L=0` 为 0）

| 建筑 | 效果 | 公式 |
|------|------|------|
| 书院 | 攻方战力百分比（已有） | `attackBonusPercent = 2 * L` |
| 演武堂 | 本城兵力战力百分比（攻守都吃） | `troopPowerBonusPercent = 3 * L` |
| 演武堂 | 征兵费用减免 | `recruitDiscountPercent = 2 * L`（最高按 50% 截） |
| 城防署 | 城防固定值 | `wallDefenseFlat = 2 * L`，加在箭塔/城门之和上 |
| 城防署 | 陷阱额外伤亡 | `trapBonus += 0.01 * L` |
| 司农院 | 田产出与田上容量 | `productionBonusPercent = 5 * L`，速率与田容都乘 `(100+P)/100` 后向下取整 |

`effects` 用整数百分比或固定值，例如演武堂 1 级：`{ "troopPowerBonusPercent": 3, "recruitDiscountPercent": 2 }`。

### 战斗

```
power = inf*10 + arc*12 + cav*14
power' = floor(power * (100 + troopPowerBonusPercent) / 100)

atk = power'_攻 * (100 + academy.level * 2) / 100
def = power'_守 + (wallDefense + wallDefenseFlat) * 10 + outpostBasePower
```

攻方用出发城演武堂；守方玩家城用守城演武堂。NPC 据点没有分科建筑。陷阱：原城墙陷阱 + 城防署 `0.01 * L`，再夹到 0～0.95。

随机波动、伤亡区间不变。

### 征兵

先按兵种目录算总价，再 `floor(总价 * (100 - recruitDiscountPercent) / 100)`。`GET /api/army` 的 `troopTypes[].unitCost` 为 **已减免后的单价**（`Discount(单价)`）；多名时扣费按总价再取整，因此 `单价 × 数量` 可能与扣费差 1～数点。接口同时给出 `recruitDiscountPercent`、`troopPowerBonusPercent`。

### 田

`ratePerHour`、`fieldCap`、收取、被掠田上存量都用加成后的速率 / 容量，避免列表和结算两套数。仓库上限仍只看仓库建筑。

## 验收

- 书院未建：三座分科 `blockedReason=prerequisite`，POST → `40909`
- 主殿 3 级且书院 1 级后可建演武堂；1 级 `effects.troopPowerBonusPercent = 3`
- 演武堂 1 级征 5 名步兵：扣粮 98（原 100）
- 司农院 1 级、良田 1 级：`ratePerHour = 630`；回拨 1 小时后 pending=630
- 箭塔 1 级 + 城防署 1 级：`wallDefense = 10`（8+2）
- `GET /api/buildings` 仍 8 项，原主殿 / 民居 / 兵营行为不变
