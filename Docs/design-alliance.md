# 联盟

- **状态：** 已定稿（第 9 步）
- **对应功能：** 创建 / 加入 / 退出联盟；同联盟不可交战
- **实现顺序：** 第 9 步（见 [路线图](design-roadmap.md)）

邮件通知见 [邮件](design-mail.md)。PvP 管线见 [玩家对战](design-pvp.md)。

## 范围

| 做 | 不做 |
|----|------|
| 创建、列表、详情、申请、邀请、踢人、退出、解散、改公告 | 联盟仓库、科技、宣战、联盟战、官职树 |
| 角色至多加入 **一个** 联盟；满员 **20** | 多盟籍、联盟合并 |
| 同联盟出征在下达与结算时均无效 | 新手保护、白名单宣战 |

## 模型

```
Alliance
 ├─ name（展示）/ nameNormalized（唯一，大小写不敏感）
 ├─ leaderCharacterId
 ├─ notice
 └─ members[]
      ├─ characterId（全局唯一：一人一盟）
      ├─ role: leader | officer | member
      └─ joinedAt
Invite / Application：pending / accepted / declined
```

创建者即为盟主。第一版 **不设官员任命接口**（`officer` 预留给以后）；权限按角色判断，现网只有盟主与成员。

需要已建主城才能创建、申请、接受邀请。

## 权限

| 操作 | 盟主 | 官员 | 成员 | 未入盟 |
|------|------|------|------|--------|
| 改公告、邀请、审申请、踢成员 | ✓ | ✓ | | |
| 踢官员 / 解散 | ✓ | | | |
| 退出 | ✓（有他人则传位给最早官员否则最早成员；仅自己则解散） | ✓ | ✓ | |
| 申请 / 接受邀请 | | | | ✓ |

不能踢自己（走退出）。不能邀请自己。

## 同联盟免战

出征 `targetType=city`：

- 下达时若攻守角色已在同一联盟 → `40918`
- 行军途中入了同一盟：结算不战斗、不掠夺，兵力原数返回，战报 `summary` 为「同联盟不可交战」，`attackerWon = false`

## HTTP

均需登录。

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/alliances` | `{ "name" }` 创建 |
| GET | `/api/alliances` | 分页列表 |
| GET | `/api/alliances/me` | 我的联盟；未加入 → `40922` |
| GET | `/api/alliances/pending` | 我收到的邀请；若是官员/盟主还带本盟申请 |
| GET | `/api/alliances/{id}` | 详情（含成员） |
| POST | `/api/alliances/{id}/apply` | 申请 |
| POST | `/api/alliances/invite` | `{ "characterName" }` |
| POST | `/api/alliances/invites/{id}/accept` | 接受邀请 |
| POST | `/api/alliances/invites/{id}/decline` | 拒绝邀请 |
| POST | `/api/alliances/applications/{id}/accept` | 通过申请 |
| POST | `/api/alliances/applications/{id}/reject` | 拒绝申请 |
| POST | `/api/alliances/leave` | 退出 |
| POST | `/api/alliances/kick` | `{ "characterId" }` |
| POST | `/api/alliances/notice` | `{ "notice" }` |
| POST | `/api/alliances/dissolve` | 盟主解散 |

联盟名 2～12 位，去首尾空白后非空。公告最长 200。

## 错误码

| 码 | 场景 |
|----|------|
| `40918` | 同联盟不可交战 |
| `40919` | 已加入联盟 |
| `40920` | 联盟名占用 |
| `40921` | 满员 |
| `40922` | 未加入联盟 |
| `40923` | 权限不足 |
| `40924` | 邀请或申请已失效 |
| `40400` | 联盟 / 角色 / 邀请不存在；尚未建城 |
| `40900` | 重复邀请或重复申请 |

解散后删除成员、未决邀请与申请，并给当时在盟成员发邮件。
