# 开发路线图

- **状态：** 已定稿（第一版施工顺序）
- **对应功能：** 先做什么、再做什么；每步的前置、范围和验收
- **实现顺序：** 全文即顺序

玩法细则、接口字段、数值仍以各 `design-*.md` 为准。本文只回答：**当前做到哪、下一步干什么、做到什么算完、不要提前做什么。**

步骤编号或范围若调整，先改本文，再改根 README 的短列表。

## 怎么用

1. **先设计，后代码。** 本步对应的 `design-*.md` 未定稿，不写该步业务代码（骨架与探活除外）。
2. **按步推进，不跳玩法。** 基础设施只在「本步第一次真正用到」时接入，不要为第 5 步提前把第 7 步做了。
3. **一步一验收。** 验收未过，不开始下一步的玩法代码。
4. **第一版单进程。** HTTP、SignalR、Hangfire 同站；不上微服务、Orleans、gRPC。

## 当前指针

| 项 | 值 |
|----|----|
| 已完成 | 第 0～13 步；第一版数值已按联调缩短时长、加大田产出 |
| 正在做 / 下一步 | 第一版玩法已齐。召回行军、占领据点、武将不在本路线图内 |
| 立刻要写的文档 | — |

## 总览

| 步 | 内容 | 对应设计 | 本步首次接入 | 状态 |
|----|------|----------|--------------|------|
| 0 | 服务端骨架、统一信封、探活 | [统一协议](design-api.md)、[架构](design-architecture.md) | ASP.NET Core 9、CORS、空 Hub | **已完成** |
| 1 | 账号、JWT、随机空地建主城 | [账号建城](design-account-city.md)、[大地图](design-world-map.md)（仅格子规则） | PostgreSQL、FreeSql、JWT | **已完成** |
| 2 | 城内建造 / 升级 + 到点完成 | [城内建筑](design-inner-city.md)、[实时推送](design-realtime.md) | Hangfire、行锁、SignalR `BuildComplete` | **已完成** |
| 3 | 城外矿 / 木 / 田：产出与收取 | [城外资源](design-outer-resources.md) | （复用 2 的任务与锁） | **已完成** |
| 4 | 城墙与基础城防 | [城墙](design-city-wall.md) | — | **已完成** |
| 5 | 出兵打 NPC 据点 + 战报 | [行军战斗](design-march-battle.md)、[大地图](design-world-map.md)（据点） | SignalR `MarchArrived` | **已完成** |
| 6 | 打玩家、掠夺、保护 CD | [PvP](design-pvp.md) | SignalR `CityAttacked` | **已完成** |
| 7 | AI 玩家 | [AI](design-ai.md) | Hangfire AI tick | **已完成** |
| 8 | 大地图表现 | [前后端通讯](design-frontend-comm.md)、[大地图](design-world-map.md) | Canvas 地图 | **已完成** |
| 9 | 邮件、排行、联盟 | [邮件](design-mail.md)、[排行](design-ranking.md)、[联盟](design-alliance.md) | 同联盟免战 | **已完成** |
| 10 | 市集兑换与同盟运输 | [市集](design-market.md) | NPC 市集点、`sg_transport`、`TransportArrived` | **已完成** |
| 11 | 分科科技建筑与加成 | [科技](design-tech.md) | 演武堂 / 城防署 / 司农院 | **已完成** |
| 12 | 每日军务与斥候侦察 | [每日军务与斥候](design-daily-scout.md) | `sg_daily_quest`、`sg_march.kind` | **已完成** |
| 13 | 商城、元宝与道具 | [商城](design-shop.md) | `sg_item` / `sg_buff`、征兵队列、迁城 | **已完成** |

网页端：第 1 步末搭最小 Vue（登录 + 我的城）；城内界面跟第 2 步；出征面板跟第 5 步；大地图画布放第 8 步。不要第 0 步先做空前端。

---

## 第 0 步：服务端骨架（已完成）

**目标：** 进程能跑，前后端契约形状已定，探活可用。

**已有：**

- `SanguoGame.Server` / `Core` / `Infrastructure` 分层
- HTTP 信封 `{ code, message, data, traceId }`，校验 / 业务失败 / 未处理异常过滤器
- CORS 放行 `http://localhost:5173`
- `GET /api/system/ping`
- `GET /hubs/game` 空壳（尚无推送、尚无鉴权）

**本步不包含：** 数据库、JWT、玩法表、Vue 工程。

---

## 第 1 步：账号 + 建主城（已完成）

**目标：** 能注册登录，能在地图空地落下唯一主城，能查出「我的城」。

### 先定稿

| 文档 | 本步必须写清 |
|------|----------------|
| [design-account-city.md](design-account-city.md) | 账号 / 角色 / 主城模型；JWT + Refresh；一角几城；建城 API 与失败码 |
| [design-world-map.md](design-world-map.md) | **仅格子规则**：地图规模、一城一格、只存有内容的格子、随机空地怎么选。NPC 据点、视野加载、画布放到第 5 / 8 步 |

### 本步接入

| 技术 | 用途 |
|------|------|
| PostgreSQL + FreeSql | 账号、角色、城池、已占用坐标 |
| JWT + Refresh Token | HTTP `[Authorize]`；为第 2 步 Hub 预留同一套身份 |

**本步不接：** Hangfire（建城瞬时完成）、Redis（坐标用唯一约束即可）、SignalR 推送。

### 做

- 注册、登录、刷新令牌
- 创建角色（若账号与角色分离）
- 服务端选随机空地建主城并落库
- 查询我的城（含坐标与城内 / 城墙 / 城外分区空壳或默认槽）
- 建城后可在仓库根目录创建 `web/`：Vue 3 + Vite，登录页 +「我的城」只读展示；HTTP 客户端与代理约定见 [前后端通讯](design-frontend-comm.md)

### 不做

- 建筑升级、资源产出、出兵
- NPC 据点、Pixi 大地图
- Hub 推送事件

### 验收

- 注册 → 登录拿到 token → 建城成功，返回坐标
- 同一角色再调建城 → `40900`（或文档规定的细分码）
- 坐标不可与已有城重叠（并发建城也不重复）
- 未登录调建城 → `40100`
- `GET` 我的城能读到刚建的主城
- 信封、camelCase、UTC 时间与第 0 步一致；`GET /api/system/ping` 仍可用

---

## 第 2 步：城内建筑（建造 / 升级 + 到点完成）

**目标：** 对主城下达建造或升级，立刻返回 `finishAt`，到点后等级生效并推送。

### 先定稿

- [design-inner-city.md](design-inner-city.md)：槽位、队列是否串行、配置表、取消规则、API
- [design-realtime.md](design-realtime.md)：Hangfire 存储（建议 PostgreSQL 独立 `hangfire` schema）、幂等、`BuildComplete` payload

### 本步接入

| 技术 | 用途 |
|------|------|
| Hangfire | 到 `FinishAt` 结算；失败重试；同一建筑只完成一次 |
| Redis | 按 `cityId` 串行，防连点 |
| SignalR | 连接后加入 `city:{cityId}`；推送 `BuildComplete` |
| Vue | 城内建筑列表、升级按钮、本地倒计时（仅展示） |

### 做

- 建筑列表、建造、升级（扣资源、写 `FinishAt`）
- 到点生效；客户端倒计时不得当作完成依据
- JWT 同时保护 API 与 Hub

### 不做

- 城外田、城墙战斗加成、行军
- 用 HTTP 轮询「升完没有」

### 验收

- 升级立刻返回 `finishAt`，未到点查建筑仍为旧等级
- 到点后等级 +1，并收到 `BuildComplete`
- 资源不足、槽位冲突返回约定业务码
- 同一座城连点升级不会出现双队列脏数据
- 进程重启后，未到期任务仍会结算（Hangfire 持久化）

---

## 第 3 步：城外资源田（已完成）

**目标：** 城外矿 / 木 / 田可建可升，产出按时间结算，玩家可收取。

### 先定稿

- [design-outer-resources.md](design-outer-resources.md)：种类、容量、收取公式（按上次收取时间，或每 N 分钟 tick）、被掠预留字段（公式本身第 6 步再定）

### 做

- 田地列表、建造 / 升级（复用第 2 步到点任务）
- 收取：服务端按时间戳结资源，禁止每秒写库
- 仓库或角色资源字段

### 不做

- 被其他玩家掠夺的完整规则（第 6 步）
- 每秒心跳刷资源

### 验收

- 等待一段时间后收取，数量与公式一致（允许文档写明的取整）
- 连续两次立刻收取，第二次接近 0
- 升级中的田按文档规定产出或不产出，行为稳定

---

## 第 4 步：城墙与基础城防

**目标：** 城墙分区有可升级的城防建筑；数值先能进后续战斗公式，本步可以还没有真实战斗。

### 先定稿

- [design-city-wall.md](design-city-wall.md)：建筑清单、与城内差异、对战斗的修正项、第一版是否做损坏修复

### 做

- 城墙建筑的建造 / 升级（复用第 2 步任务管线）
- 查询城防状态，供第 5 步结算读取

### 不做

- 城墙作为大地图独立地块
- 完整攻城战（第 5 / 6 步）

### 验收

- 能升级至少一种城防，到点生效
- 查城接口能带出城防等级，供战斗侧读取

---

## 第 5 步：出兵打 NPC + 战报

**目标：** 从主城出兵打地图 NPC 据点；到达后一次性结算并出战报。

### 先定稿

- [design-march-battle.md](design-march-battle.md)：行军字段、速度与 `ArriveAt`、战斗公式、战报、幂等
- [design-world-map.md](design-world-map.md)：NPC 据点类型、刷新、占领或掠夺（本步补完第 1 步未写的据点部分）

### 本步接入

- Hangfire 按 `ArriveAt` 结算行军
- SignalR `MarchArrived`
- Vue：出征面板、行军列表、战报（大地图画布仍可第 8 步）

### 做

- `POST` 出征：扣兵、写行军、立刻返回 `arriveAt`
- 到点结算（兵力、兵种、城防、科技、随机种子 → 战报）
- 查询行军中、查询战报
- NPC 与玩家走同一套 March / Battle，本步目标类型只有 NPC

### 不做

- 打其他玩家、保护罩、掠夺玩家仓库
- 帧同步、客户端预测、全图实时视野

### 验收

- 出征立刻返回到达时间；未到点据点状态不变
- 到点后有战报，攻守双方结果自洽（胜负、伤亡、资源变化按文档）
- 同一 `marchId` 重试结算只生效一次
- 兵力不足、目标不存在返回约定业务码

---

## 第 6 步：玩家对战与掠夺

**目标：** 对其他玩家城出兵；可掠资源；战败或被掠后有保护 CD。

### 先定稿

- [design-pvp.md](design-pvp.md)：可攻击条件、掠夺上限与优先顺序、免战、多部队打同一城时按 `cityId` 串行

### 做

- 复用第 5 步行军战斗管线，增加目标类型「玩家城」
- 掠夺入账 / 扣账；保护罩期间拒绝或无效攻击（按文档）
- 被打方 `CityAttacked`，攻方战报

### 不做

- 实时国战、联盟宣战（若第 8 步才做联盟）
- 另一套战斗公式

### 验收

- A 打 B：B 收到被打推送，双方战报一致
- 保护期内攻击按文档失败或无效
- 同时两支部队打同一城，结算串行、资源不会少扣或多扣
- 城外田被掠后，第 3 步的收取公式仍成立

---

## 第 7 步：AI 玩家

**目标：** 地图上有服务端脚本控制的城，使用与真人相同的建造 / 出兵 / 战斗接口。

### 先定稿

- [design-ai.md](design-ai.md)：是否占用账号表、生成数量与分布、tick 周期、行为模板、是否允许战斗作弊（默认不允许）

### 做

- Hangfire（或同类定时）按 tick 替 AI「点按钮」
- AI 城可被玩家攻打，也可打 NPC / 玩家（范围按模板）

### 不做

- 机器学习、独立战斗规则
- 为 AI 单独做一套客户端

### 验收

- 新档或刷怪后地图上有 AI 城
- AI 会升级或出兵（按模板），日志可追到与真人相同的应用服务
- 玩家攻打 AI 走第 6 步同一管线

---

## 第 8 步：地图表现与外围（第一版收尾）

**目标：** 浏览器里能拖地图看城点和行军线；其余外围系统按需单独立项。

### 本步可拆（每项先补设计再做）

| 项 | 说明 |
|----|------|
| 大地图 UI | Canvas / PixiJS：拖拽、缩放、城点、行军线；按视野拉有内容的格子。不要用 DOM 铺满格子 |
| 邮件 / 排行 / 联盟 | 第 9 步 |

第 5 步若已有出征面板，本步只补「看地图」，不改战斗结算。联盟 / 邮件 / 排行见第 9 步。

### 验收（地图 UI 最低标准）

- 能看到自己的城与附近 NPC / 其他城
- 行军中能看到从 A 到 B 的线或标记（位置按 `arriveAt` 插值仅展示）
- 缩放拖拽不卡死（不做全图实时视野）

---

## 第 9 步：邮件、排行、联盟

**目标：** 有站内信入口、三张排行榜、可组盟且同盟不能互打。

### 先定稿

- [design-mail.md](design-mail.md)
- [design-ranking.md](design-ranking.md)
- [design-alliance.md](design-alliance.md)

### 做

- 战斗与联盟事件写入邮件；列表 / 已读
- 国力、兵力、掠夺排行现算前 50
- 创建 / 申请 / 邀请 / 退出 / 解散；出征校验同盟

### 不做

- 私聊、联盟仓库与宣战、排行奖励

### 验收

- 打 NPC 后攻方未读邮件 +1，点已读后清零
- 两人组盟后互打返回 `40918`
- `GET /api/rankings?type=power` 含自己的名次

---

## 第 10 步：市集兑换与同盟运输

**目标：** 能在地图市集按比例换资源；同联盟能互相运资源；两者都按行军公式花时间。

### 先定稿

- [design-market.md](design-market.md)：市集占格、汇率、双程/单程计时、运输表、API 与错误码

### 本步接入

- 启动补齐 NPC 市集点
- 表 `sg_transport`；Hangfire 按 `ArriveAt` 结算
- SignalR `TransportArrived` / `ResourceReceived`

### 做

- `GET` 市集列表与汇率；`POST` 兑换立刻扣付出资源并返回 `arriveAt`
- 到点按锁定换得量入仓（仓库截断）
- 同联盟 `POST` 援助：立刻扣发送方，到点入接收方
- Vue：市集页、地图市集标记与运输线

### 不做

- 玩家挂单、押运兵力、取消/加速、途中被劫、城内市集建筑、AI 交易

### 验收

- 1000 粮换木：立刻粮 -1000，到点木 +900
- 同种兑换 / 数量过小 → `40928`
- 组盟后援助成功；非同盟 `40926`；运给自己 `40925`
- 途中退盟仍送达
- 地图能看到市集与运输线

---

## 第 11 步：分科科技建筑与加成（已完成）

**目标：** 书院之外再有军事 / 城防 / 资源科技建筑，升级即加成。

### 先定稿

- [design-tech.md](design-tech.md)

### 做

- 演武堂、城防署、司农院：城内建筑，共用一条队列
- 前置：主殿 3 级且书院 1 级
- 兵力战力%、征兵折扣、城防固定值、陷阱、田产出/田容按文档公式接入战斗、征兵、收取与被掠

### 不做

- 科技树节点、研究指令、第二队列

### 验收

- 书院未建不可建分科 → `40909`
- 演武堂 1 级征 5 步兵扣粮 98
- 司农院 1 级良田 1 级每小时 630
- 箭塔 1 + 城防署 1 → 城防 10

---

## 第 12 步：每日军务与斥候侦察（已完成）

**目标：** 当天有五条固定事可做并可领犒赏；出征前能派人看据点 / 敌城虚实。

### 先定稿

- [design-daily-scout.md](design-daily-scout.md)

### 做

- `GET /api/daily`、`POST /api/daily/claim`
- 收取入库、下达升级、征兵、战胜据点、市集兑换出发时计数
- `POST /api/army/scout`：扣 1 步兵、半程到达、侦察邮件、不战斗

### 不做

- 召回 / 加速、占领据点、侦察市集、随机任务

### 验收

- 见 [每日军务与斥候](design-daily-scout.md) 验收条

---

## 第 13 步：商城、元宝与道具（已完成）

**目标：** 出征能掉元宝；能用元宝买加速 / 丰收 / 迁城令；时效可叠加；征兵改为到点入帐。

### 先定稿

- [design-shop.md](design-shop.md)

### 做

- 城上元宝；战胜 70% 掉 20～40，战败 30% 掉 5～12
- `GET/POST /api/shop` 购买与使用；时效类时间累加、百分比不叠加
- 征兵队列；迁城令随机空地 / 指定坐标
- Vue 商城页、元宝 HUD、征兵倒计时

### 不做

- 充值、玩家交易、加速行军 / 运输

### 验收

- 见 [商城、元宝与道具](design-shop.md) 验收条

---

## 明确不做（第一版全程）

- 全图实时视野、帧同步、即时 RTS
- 微服务拆分、gRPC、自建原生 WebSocket 协议
- Unity WebGL / Cocos 做页游主端
- 每秒写库的资源心跳

人多以后的 Orleans、分服、地图分片不在本路线图内。

## 文档与代码的对应关系

| 阶段 | 设计 | 代码落点（约定） |
|------|------|------------------|
| 协议 / 过滤器 | `design-api.md` | `SanguoGame.Server` Contracts、Filters |
| 规则与错误码 | 各玩法 `design-*.md` | `SanguoGame.Core` |
| 库、Redis、Hangfire | 本文各步「首次接入」 | `SanguoGame.Infrastructure` |
| HTTP / Hub | `design-realtime.md` | `SanguoGame.Server` Controllers、Hubs |
| 网页 | `design-frontend-comm.md` | `web/`（第 1 步末创建） |
| 市集 / 运输 | `design-market.md` | `SanguoGame.Core` 汇率；`sg_market` / `sg_transport`；`MarketsController` |
| 科技建筑 | `design-tech.md` | `SanguoGame.Core` 加成公式；`InnerBuildingCatalog` 三座分科 |
| 每日军务 / 斥候 | `design-daily-scout.md` | `sg_daily_quest`；`MarchKind`；`DailyController` / `army/scout` |
| 商城 / 元宝 / 道具 | `design-shop.md` | `sg_item` / `sg_buff`；`ShopController`；征兵队列；迁城 |
