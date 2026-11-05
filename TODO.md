# 待办 / 验证清单

> 2026-11-05　版本 **1.8.0**　已上传创意工坊 3828162134　部署 171 文件

---

## 一、已验证（用户游戏内实测）

- [x] 模组能被游戏识别（About.xml 多版本 + LoadFolders.xml）
- [x] 派系专属船型自动生成（每派系 9 主题 + 人才 + 奴隶）
- [x] 贸易台可建造、与通讯台同科技、造价正确
- [x] 好感定价、包船扣银
- [x] 反射读取原版 `StockGenerator_SingleDef` 现金区间
- [x] 每船 40 种货（食品 / 杂货 80），无同名变体
- [x] 「会购买的物品」按原版分类 / tradeTag 列出完整分类
- [x] 人才船买回来是殖民者、4 项技能 14~20 带热情
- [x] 奴隶船 10~18 人，买回来是奴隶
- [x] 拒绝赔偿扣好感 + 发信件通知
- [x] 只有殖民地小人 / 玩家炮塔打船才触发索赔
- [x] 击毁飞船掉落整船货物
- [x] 偷渡客无阵营、信件文案单行

## 二、已修复但**尚未实机复测**

这批是 2026-11-05 审查后集中修掉的，编译通过、部署完成，等一次游戏内回归。

### 资金安全

- [ ] **扣款校验实扣数**：`PaySilver` 改返回 `int`，包船 / 买船 / 赔偿三处都判 `paid < cost`
- [ ] **买船失败全额退款**：`MakeThing` 或 `GenSpawn` 抛异常时走 `RefundSilverTo`
- [ ] **`AvailableSilver` 口径统一**：不再取 HUD 计数的最大值，只算真正付得出的银

### UI 性能

- [ ] **贸易台窗口级缓存**：`silverCache` / `kindsCache` / `cashCache`，`PreClose` 清空
  - 改前：每帧每个可见船型各一次全图递归扫描 + 40~80 个生成器
  - 改后：整个窗口一次

### 行为修正

- [ ] **击毁改为先撒货后爆炸**：避免 30 点爆炸伤害烧掉刚掉落的货物与掉出的飞行员
- [ ] **偷渡客超时离场**：`pawn.mindState.exitMapAfterTick` 与信件超时同步，信件过期不再永久滞留
- [ ] **船名只拼一次**：`nameDecorated` 标记并序列化，不再出现「A · 杂货船 · 杂货船」
- [ ] **索赔框只关自己**：保存 `Dialog_NodeTree` 实例，通讯台对话同时开着时不会关错
- [ ] **已敌对 / 已消灭不再重复设关系**：避免每次击毁都刷一条状态变更

### 资源与存档

- [ ] **临时可读贴图销毁**：`MakeReadable` 的副本用完 `Object.Destroy`，不再跨读档累积
- [ ] **人船 pawn 队列超时清理**：5 天未成交就丢弃，`firstSeenTick` 随存档保存

### 启动期（P2）

- [ ] **`CollectItems` 全局池只算一次**：`EnsureTradeableGoods()` 懒加载缓存
- [ ] **`ThemeOf` 记忆化**：按 `shortHash`，同一件物品被多个派系重复分类时只算一次
- [ ] **分类关键字常量表**：七个 `Is*` 改传 `static readonly string[]`，`CatMatch` 去掉 `params`
- [ ] **半成品 def 自愈**：`BuildThemedKinds` 两阶段（先全构建再统一 Add），`SkipReason` 改为检查每个应有主题
- [ ] **重复 Add 防护**：Add 前 `GetNamedSilentFail` 判重

> 等价性由子代理在仓库外合成工程验证过（三组规模各 3000/2500/800 件物品）：逐派系「defName + 有序货单」签名 diff = 0，Add 顺序一致，二次 Generate 幂等，半套自愈后签名逐字相同。启动耗时 654→171 / 248→62 / 37→11 ms。

---

## 三、回归测试清单（按风险排序）

### P0

| # | 操作 | 判据 |
| --- | --- | --- |
| 1 | 启动游戏，搜日志 `Generated .* themed trade ships` | 派系数与之前一致（54），没有异常 |
| 2 | 打开贸易台 | 船型数、货单、现金与之前一致，无变多变少 |
| 3 | 击毁一艘飞船 | 地面货物一件不少（日志 `dropped N/N`），且**没有被爆炸烧掉** |
| 4 | 把银放进运输舱后买船 | 不该白送：要么扣足，要么提示银不够并退款 |

### P1

| # | 操作 | 判据 |
| --- | --- | --- |
| 5 | 打船到 60% → 付钱，中途搬走银两再点付钱 | 提示银不够，船不走 |
| 6 | 叫船后放着不管，等信件超时 | 偷渡客会自己走出地图 |
| 7 | 通讯台对话开着时触发索赔，点「拒绝」 | 关的是索赔框，不是通讯台 |
| 8 | 反复击毁同一敌对派系的船 | 不再重复刷「转为敌对」消息 |

### P2

| # | 操作 | 判据 |
| --- | --- | --- |
| 9 | 设置项逐个测（见第四节） | 按下表「生效时机」表现 |
| 10 | 多语言切换（英 / 繁 / 西） | 无 `TraderShipsXXX` 原始 key 漏出 |
| 11 | 旧存档加载 | 不报错 |

---

## 四、设置项生效时机

**立即生效**（每次调用时读 settings）：

- `disableOrbital` / `requireCommsConsole` / `enableQuests`
- `enableShipRequest` / `shipRequestSilverCost` / `shipRequestRequireAlly`
- `shipRequestCooldownDays` / `maxLandedShips` / `shipArrivalIntervalDays`
- `stowawayChancePercent` / `stowawayStayDays`
- `shipStayDays`（**仅影响之后生成的船**）

**必须重启**：

- `autoFactionShips` —— 只在 `[StaticConstructorOnStartup]` 里跑一次
- `bakeShipGraphics` —— 已生成的船有 `bakeAttempted` 缓存
- `colors` / 饱和度 / 明度 —— 只在 `CreateSprite` 时读，已生成的船不重抽

---

## 五、已知限制（不修）

| 现象 | 原因 |
| --- | --- |
| 落地动画看不到船体，只有影子 | `ShipJob_FlyAway` 只在离场时 `Contents.SetShuttle`，进场时为空；要改得 Harmony 拦 `ShipJob_Arrive` |
| 旧存档的船按旧时间离场 | 存档里存的是旧 `ticksUntilDeparture`，新设置不回溯 |
| 中途移除模组会报错 | `OrbitalTraderArrival.workerClass` 指向本模组，属不可逆引用 |
| 存档里已存在的船点「拒绝」不扣好感 | 那些船没有 `ownerFaction` 字段（1.8 才加） |
| 家具船可能带出会 Tick 报错的模组建筑 | 部分模组的 Building 从贸易库存落地后空引用（实测 VFEMedieval 蜂房）；人船已过滤建筑，家具船未过滤 |

---

## 六、待决策

- [ ] **家具船是否也过滤可搬运建筑** —— 人船已经过滤了（避免模组建筑 Tick 报错），家具船还留着，因为它的主题就是卖家具
- [ ] **好感被下限截断时的行为** —— 好感已 −100 时选「拒绝」，扣除 0，现在只发「已无法更差」信件然后船飞走。是否改成直接转敌对？（当前：保持现状）
- [ ] **贸易台加主题筛选下拉框** —— 现在 40~80 条列表较长，可加「只看武器船」这类过滤
- [ ] **preview.png 尺寸** —— 当前 533×300，创意工坊建议 ≥640×360

---

## 七、审查中已剔除的误报

子代理报告里经核实不成立、**不要再修**的：

| 结论 | 实际 |
| --- | --- |
| `TraderKindDef.canTrade` 默认 true 会抛异常 | 全仓库 `canTrade` 零命中，字段不存在；交易实测正常 |
| 坠落物无 `isEdifice` 导致船消失 | 用 `SkyfallerMaker.MakeSkyfaller` 显式持有，落船实测正常 |
| 卖 pawn 会复制 | 买人实测正常；`LandedShip` 未覆盖 `GiveSoldThingToTrader`，走的是 `TradeShip` 基类实现 |
| Sprite setter 参与序列化 | 读档路径不走 setter |
| 存档体积膨胀 | Thing 走 Scribe 引用表；2 艘 ≈ 8KB |
