# 排行

- **状态：** 已定稿（第 9 步）
- **对应功能：** 国力 / 兵力 / 掠夺排行
- **实现顺序：** 第 9 步（见 [路线图](design-roadmap.md)）

第一版 **现算、不落排行榜表**。地图规模小（玩家 + 少量 AI），读时扫城即可。以后人多再改定时刷新。

## 范围

| 做 | 不做 |
|----|------|
| 三种榜：国力、驻城兵力、累计掠夺 | 每日结算奖励、隐榜、按联盟排行 |
| 返回前 50，并带自己的名次与分数 | 分页翻完全服 |
| 含 AI 城，条目标 `isAi` | 排除 AI 的隐藏开关 |

## 分数

| `type` | 公式 |
|--------|------|
| `power` | `sum(建筑等级) * 100 + 驻城兵力总数`（城内 / 城墙 / 田都算；行军中兵力不算） |
| `troops` | 驻城步 + 弓 + 骑 |
| `loot` | 该城作为攻方且战胜的战报中，四种掠夺资源之和 |

同分按 `cityId` 升序。未建城的账号 `myRank` 为 `null`，`myScore` 为 0。

## HTTP

### `GET /api/rankings?type=power`

`type`：`power` / `troops` / `loot`，默认 `power`。未知值 → `40001`。需登录。

```json
{
  "type": "power",
  "serverTime": "2026-08-20T04:00:00.000Z",
  "myRank": 3,
  "myScore": 420,
  "items": [
    {
      "rank": 1,
      "cityId": 2,
      "characterName": "黄巾甲",
      "cityName": "黄巾甲的城",
      "score": 800,
      "isAi": true,
      "allianceName": null
    }
  ]
}
```

`allianceName` 无盟时为 `null`（序列化省略）。
