# 邮件

- **状态：** 已定稿（第 9 步）
- **对应功能：** 站内信：战报通知、联盟邀请 / 申请、系统通知
- **实现顺序：** 第 9 步（见 [路线图](design-roadmap.md)）

信封与分页见 [统一协议](design-api.md)。联盟请求见 [联盟](design-alliance.md)。战报列表仍走 `GET /api/reports`，邮件只是入口，不替代战报。

## 范围

| 做 | 不做 |
|----|------|
| 每封邮件属于一个角色；列表、已读、全部已读 | 玩家互发私聊、附件、资源附件 |
| 战斗结算后给攻守双方各写一封（据点只给攻方） | 推 SignalR（刷新邮件页即可） |
| 联盟邀请 / 申请 / 踢出 / 解散写邮件 | 邮件里直接点同意（同意走联盟 API） |

## 模型

```
Mail
 ├─ recipientCharacterId
 ├─ type: system | battle | alliance
 ├─ title / body
 ├─ relatedType / relatedId（可选，如 report / invite / alliance）
 ├─ isRead
 └─ createdAt
```

无发送人账号。系统与战斗由服务端写入；联盟相关由联盟服务写入。第一版不删信。

## HTTP

均需登录，且已有角色。

### `GET /api/mail`

查询：`page`、`pageSize`、可选 `unreadOnly=true`。

```json
{
  "unreadCount": 2,
  "items": [
    {
      "id": 1,
      "type": "battle",
      "title": "出征获胜",
      "body": "攻克村落·3,4，缴获粮80 …",
      "relatedType": "report",
      "relatedId": 9,
      "isRead": false,
      "createdAt": "2026-08-20T04:00:00.000Z"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "total": 1
}
```

### `POST /api/mail/{id}/read`

标记已读。非收件人 → `40400`。已读再调仍成功。

### `POST /api/mail/read-all`

将该角色全部未读标为已读。

## 写入约定

| 事件 | type | 收件人 | related |
|------|------|--------|---------|
| 行军结算 | `battle` | 攻方角色；目标为玩家城时再给守方 | `report` + 战报 Id |
| 联盟邀请 | `alliance` | 被邀请角色 | `invite` + 邀请 Id |
| 入盟申请 | `alliance` | 盟主 | `application` + 联盟 Id |
| 申请通过 / 被踢 / 解散 | `alliance` | 当事角色 / 全体成员 | `alliance` + 联盟 Id |

战斗邮件与战报同一事务写入，避免有战报无邮件。

## 错误码

| 码 | 场景 |
|----|------|
| `40100` | 未登录 |
| `40400` | 无角色，或邮件不存在 / 不是自己的 |
