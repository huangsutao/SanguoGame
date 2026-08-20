# 商城、元宝与道具

- **状态：** 已定稿（第 13 步）
- **对应功能：** 元宝货币、NPC 商城买道具、限时加速/资源加成、迁城令
- **实现顺序：** 第 13 步（见 [路线图](design-roadmap.md)）

元宝只从出征掠夺按概率掉落，不能用粮木铁铜兑换，也不能充值。市集兑换仍只换四种资源，见 [市集](design-market.md)。建造队列见 [城内建筑](design-inner-city.md)。出征结算见 [行军战斗](design-march-battle.md) 与 [PvP](design-pvp.md)。迁城占格规则见 [大地图](design-world-map.md)。

## 范围

| 做 | 不做 |
|----|------|
| 城上存元宝；出征战斗按胜负概率掉落 | 充值、人民币、活动礼包、VIP |
| NPC 商城用元宝买固定目录道具 | 玩家交易、拍卖、赠送 |
| 加速类、资源加成类道具有时效，多次使用时间累加 | 永久加成、把时效加成叠成更高百分比 |
| 加速覆盖建造、造兵、田/墙升级、科技研发 | 加速行军、加速运输、取消队列 |
| 低级迁城令随机空地；高级迁城令指定坐标 | 免费迁城、跨服迁城 |
| 征兵改为到点入帐，与加速令配套 | 多条征兵队列 |

## 元宝

存在 `sg_city.yuanbao`，整数、非负、无仓库上限。建城为 **0**。只由服务端发放与扣减。

### 掉落

仅 **进攻行军** 真正打完一场战斗后，给 **攻方** 掷一次。斥候、目标消失、同联盟/保护期未交战，不掷。

掷骰用战报同一 `seed` 的独立派生，保证可复现：

```
chance = 胜 70% / 负 30%
amount = 胜 20～40 / 负 5～12（含端点，均匀）
未命中则 0
```

入账 `yuanbao = min(int.MaxValue, yuanbao + amount)`。战报与邮件摘要带「获元宝 N」；`yuanbao` 字段始终返回（未掉为 0）。

守方不因被打获得元宝。NPC 据点与玩家城用同一套概率。

## 道具目录

数值放 Core。`itemType` 英文 camelCase。

| type | 名称 | 价 | 时效 | 效果 |
|------|------|----|------|------|
| `speedBuild` | 建造加速令 | 80 | 5 小时 | 主殿 / 民居 / 仓库 / 兵营建造与升级时长 ÷ 1.5 |
| `speedUpgrade` | 升级加速令 | 80 | 5 小时 | 城外田、城墙建造与升级时长 ÷ 1.5 |
| `speedTech` | 研发加速令 | 100 | 5 小时 | 书院 / 演武堂 / 城防署 / 司农院升级时长 ÷ 1.5 |
| `speedRecruit` | 征兵加速令 | 80 | 5 小时 | 征兵时长 ÷ 1.5 |
| `resourceBoost` | 丰收令 | 120 | 5 小时 | 田产出速率 +50%（与司农院加算） |
| `relocateRandom` | 迁城令 | 150 | 无 | 随机空地迁城 |
| `relocateTarget` | 高级迁城令 | 400 | 无 | 指定空地迁城 |

加速百分比固定 **50**，多次使用 **只加时长、不加百分比**。

```
actualSeconds = max(1, ceil(baseSeconds * 100 / (100 + speedPercent)))
expireAt = max(now, 当前 expireAt) + durationSeconds * count
expireAt 上限 = now + 30 天
```

下达建造 / 征兵时按 **当时** 生效的加速写 `finishAt`。使用加速令时若已有匹配队列，按新旧百分比把剩余时间缩短，并另调度 Hangfire（原任务到点幂等）。时效结束后，已缩短的队列不再拉长。

丰收令：首次从「未生效」变为「生效」时，按旧速率把田上 pending 快照进 `lastCollectedAt`，之后按新速率走。收取与被掠的 pending 按时间切分：`lastCollectedAt → min(now, expireAt)` 用加速后速率，其余用原速率。展示的 `ratePerHour` 在令仍有效时为加速后值。

## 征兵队列

全城一条。下达立刻扣资源，到 `recruitFinishAt` 才加兵。

```
durationSeconds = ceil(secondsPerUnit * count) 再套加速
secondsPerUnit：步兵 2、弓兵 3、骑兵 4
```

带兵上限把 **驻军 + 行军中 + 征兵队列** 算进去。队列占用时再征 → `40935`。每日军务「征兵」在下达时计数。进程重启补结算到期队列。SignalR `RecruitComplete`。

## 迁城

| 项 | 约定 |
|----|------|
| 低级 | 服务端按建城同一套随机空地，排除本城当前格 |
| 高级 | body 必带 `x`、`y`；越界、占用、与当前格相同 → `40934` |
| 占用 | 与其他城、据点、市集互斥；`(x,y)` 唯一约束兜底并发 |
| 拦截 | 有未结算的本城出发行军、指向本城的行军、本城发出运输 → `40933` |
| 成功 | 改坐标、扣 1 张令、写入与 PvP 相同的保护罩 |
| 建造 / 征兵 | 不拦截；队列跟城走 |

客户端不传目标城 Id。坐标只信服务端。

## 模型

```
City
 ├─ yuanbao
 ├─ recruitType / recruitCount / recruitFinishAt
 ├─ items[itemType] → count
 └─ buffs[buffType] → expireAt
```

| 表 | 说明 |
|----|------|
| `sg_city.yuanbao` | `int` 默认 0 |
| `sg_city.recruit_*` | 征兵队列；空闲时 type/finish 为空 |
| `sg_item` | `id, city_id, item_type, count`；`(city_id, item_type)` 唯一；`count ≥ 0` |
| `sg_buff` | `id, city_id, buff_type, expire_at`；`(city_id, buff_type)` 唯一 |
| `sg_battle_report.yuanbao` | 本次攻方获得的元宝 |

`buff_type` 与时效道具 `item_type` 相同。过期行可留着，读取时 `expireAt > now` 才生效。

## HTTP

均需登录且已有主城。时间 UTC。`GET` 带 `serverTime`。

### `GET /api/shop`

```json
{
  "cityId": 1,
  "serverTime": "2026-08-20T12:00:00.000Z",
  "yuanbao": 40,
  "x": 10,
  "y": 12,
  "catalog": [
    {
      "type": "speedBuild",
      "name": "建造加速令",
      "kind": "buff",
      "price": 80,
      "durationHours": 5,
      "speedPercent": 50,
      "owned": 2,
      "description": "主殿、民居、仓库、兵营建造与升级加速 50%，持续 5 小时，重复使用时间累加。"
    }
  ],
  "buffs": [
    { "type": "speedBuild", "name": "建造加速令", "expireAt": "2026-08-20T17:00:00.000Z", "speedPercent": 50 }
  ]
}
```

迁城令 `kind=consumable`，`durationHours` / `speedPercent` 为 `null`。`buffs` 只含未过期项。

### `POST /api/shop/buy`

```json
{ "itemType": "speedBuild", "count": 1 }
```

`count` 1～99。成功返回与 `GET` 相同形状。元宝不足 → `40931`。未知类型 → `40001`。

### `POST /api/shop/use`

时效道具：

```json
{ "itemType": "speedBuild", "count": 1 }
```

迁城令：

```json
{ "itemType": "relocateRandom" }
```

```json
{ "itemType": "relocateTarget", "x": 3, "y": 8 }
```

`count` 默认 1，时效道具可 >1 一次加多段时间。迁城令 `count` 只能为 1。库存不足 → `40932`。

成功仍返回商城概览（含新坐标与保护至）。前端应再拉 `GET /api/city/me` 与大地图。

## 错误码

| 码 | 场景 |
|----|------|
| `40001` | 未知道具、数量非法、高级迁城缺坐标 |
| `40906` | 资源不足（征兵仍用） |
| `40931` | 元宝不足 |
| `40932` | 道具数量不足 |
| `40933` | 行军或运输未结束，不能迁城 |
| `40934` | 迁城目标非法（越界、占用、原地） |
| `40935` | 征兵队列占用中 |
| `40905` | 随机迁城时地图无空地 |

## SignalR

| 事件 | 组 | 时机 |
|------|----|------|
| `RecruitComplete` | `city:{cityId}` | 征兵到点入帐 |
| `BuildComplete` | 同第 2 步 | 使用加速令导致提前完成时仍走原事件 |

不另推「买到了 / 用了道具」；HTTP 响应即真相。

## 网页

HUD 增加元宝。新页「商城」：目录、拥有量、生效中的时效、购买与使用。高级迁城令提供坐标输入。军队页展示征兵队列倒计时。战报展示获得的元宝。迁城后刷新坐标与地图。

## 安全

- 目录、价格、掉落、坐标合法性只信服务端
- 买 / 用 / 迁城都走 `cityId` 行锁；元宝与库存不出现负数
- `price * count` 用 64 位再与余额比，防溢出
- 不能指定别人的城或改别人库存
- 迁城目标必须空地；唯一索引防两城同格

## 验收

- 无元宝购买 → `40931`；发元宝后能买，余额与库存正确
- 使用建造加速令后升主殿，`finishAt` 比无令更短；再用一张，`expireAt` 大约 +5 小时
- 丰收令后天产出 pending 约为无令的 1.5 倍（司农院 0 级时）
- 征兵立刻扣资源、未到点兵力不变；到点后兵力增加；队列中再征 → `40935`
- 战胜战报 `yuanbao` 符合 `YuanbaoLoot.Roll(seed, true)`；战败符合 `Roll(seed, false)`；斥候战报无此收益
- 低级迁城坐标改变且原格可被占用；高级迁到指定空格；指定已占格 → `40934`
- 出征途中迁城 → `40933`
- 未登录买道具 → `40100`
