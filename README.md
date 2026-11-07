# Trader ships (continued)

[![Steam Workshop](https://img.shields.io/badge/Steam-Workshop-1b2838?logo=steam)](https://steamcommunity.com/sharedfiles/filedetails/?id=3828162134)
[![GitHub](https://img.shields.io/badge/GitHub-Repository-181717?logo=github)](https://github.com/IoIqq/TraderShips)

交易飞船会降落到你的地图上，和你面对面交易——不需要轨道商船，也不需要贸易信标。

这是 [AUTOMATIC1111 原版 Trader ships](https://github.com/AUTOMATIC1111/TraderShips) 的 **1.7（Waylith 引擎）** 续作。原始设计、美术与代码均为原作者成果；本分支在移植之外还扩展了大量玩法。

当前版本：**1.8.0**　｜　支持 1.1 ~ 1.7

---

## 功能

### 飞船到来

- 飞船降落并开启交易，行为类似来访商队
- 优先降落在你划定的**着陆区**（区域分类里两个工具：扩展 / 清除）；着陆区被占满时飞船会自己在附近或整张地图上另找空地，不会因为"没地方停"就取消来访
- 有概率**坠毁**而非正常降落，散落残骸与幸存者
- 飞船有**停留时长**，到期自行离开
- 同时停靠数量有上限，避免地图被船塞满

### 贸易台

独立建筑「**贸易台**」，不再让玩家在通讯台菜单里翻找。

| 项 | 值 |
| --- | --- |
| 尺寸 | 3×2 |
| 造价 | 钢 80 + 零部件 2 |
| 耗电 | 150W |
| 科技 | 与通讯台相同（微电子学基础） |
| 使用 | 殖民者走到建筑旁右键，选 `Trade` |

主从式界面：左栏派系列表（带现金 / 价格 / 呼叫三列），右栏该派系的船型详情与购买入口。

界面是本模组自绘的，**不继承 `Building_CommsConsole`**——否则会把其他模组挂在通讯台上的选项一起吸进来（曾经出现过 `EES 拦截敌方据点通信` 混进列表）。

通讯台入口保留，但收进「贸易台……」子菜单。

### 派系专属船队

`[StaticConstructorOnStartup]` 时为每个派系按它的物品库生成船型，defName 形如 `TraderShipsAuto_&lt;派系&gt;_&lt;主题&gt;`，确定性可复现。

| 主题 | 主货 |
| --- | --- |
| 医疗 | 药品、医疗物资 |
| 武器 | 武器、弹药 |
| 服装 | 衣物、护甲、头盔 |
| 食品 | 成品餐与饮品（生食归物资） |
| 物资 | 金属、皮革、布料、矿产 |
| 工业 | 零件、燃料、化工 |
| 家具 | 可搬运的家具与建材 |
| 珍宝 | 神器、遗物、超科技小件 |
| 杂货 | 其余一切 |
| 人才 | 4~8 人，技能 14~20 且带热情，**买回来是殖民者** |
| 奴隶 | 10~18 人，便宜量大，买回来是奴隶 |

**货位**：普通主题 40 种；食品与杂货 80 种。主题按比例预留（医疗/武器/服装 60%，物资/家具/珍宝 70%，食品 50%，杂货 100%），其余从该派系物品库随机补——既有辨识度，又不会整船清一色。

**材质与品质**：`StockGenerator_ThingDefs` 用 `GenStuff.DefaultStuffFor` 固定材质、不生成品质，否则一件扎甲会裂成「钢铁 / 陶瓷 / 哥布林合金扎甲」几十条同名变体。

**收购范围**：每种船额外挂一个 `StockGenerator_BuyCategories`，用**原版 ThingCategoryDef 与 tradeTag** 声明它收什么。这不是锦上添花——`TraderKindDef.WillTrade` 的实现就是遍历生成器问 `HandlesThingDef`，只挂具体 def 的生成器会让「会购买的物品」只剩白银一项。

### 兼容其它模组的独立商船

有些模组会定义自己的轨道商船（例如 **Misc.Robots** 的 `robots trader`），它们不属于任何派系，按派系查船型永远查不到。

贸易台左栏顶部因此有一条固定的「**独立商船**」：自动收集所有「`orbital` 且没有 `faction`」的第三方船型，点进去就能呼叫。官方 Core / DLC 的 `Orbital_*` 会被排除——它们只是因为没装 Odyssey 而暂时没有派系。

随机来船也做了公平化：本模组会为每个派系生成约 10 条船型，40 多个派系就是 1500 条，如果直接按 def 数量加权，别的模组那 1 条独立商船会被稀释到 1/1500，等于被我们悄悄下架。现在改为按来源分三组（本模组 / 独立商船 / 其它）、组间按 `sqrt(数量)` 加权，小组不会被碾死。

### 商业玩法

- **包船**：付银呼叫指定派系的指定船型，好感越高越便宜（+100 时 6 折）
- **购买飞船**：向派系买一艘自己的穿梭机，价格 8000 银起，随好感浮动
- **贸易伙伴**：同一派系连续交易累计折扣，每艘 +1%，上限 +15%

### 预订货物

贸易台里可以下订单：选该派系的货、填数量、付 **10% 定金**（最低 100 银），**3 天后**该派系派一艘对应主题的船，货已经在船上。

几点设计：

- 目录来自该派系各船型的 `StockGenerator_ThingDefs`，也就是"这些船实际会带什么"——不从全表猜，避免订了货船来了却没有
- 同派系同船型的多个订单**合成一艘船**，不会为五样货来五艘船
- 停靠位满、造不出船、坠落物放不下：**不丢单**，顺延一天重试；退还定金失败时也保留订单
- 定金是前期费用，不抵扣货款
- 不可堆叠物品（家具等）单次上限 5 件，否则会生成上千个独立 `Thing`

### 飞船作为事件实体

- 血量降到 **60%** 时船长要求赔偿：付钱了事 / 拒绝（好感 −40，并来信告知实际扣了多少）/ 拒绝并承担后果（转敌对）
- **只有你自己的殖民者或炮塔**打伤飞船才会触发索赔。火灾、雷击、无主爆炸、别的派系交火不算在你头上——这条是防刷物品的
- 直接**击毁**：整船货物全部散落（先撒货后爆炸，不会被自家爆炸烧掉），该派系立刻敌对
- **偷渡客**：飞船有概率带下一个**无阵营**的人，在殖民地附近徘徊并来信请求收留。收留即入队，拒绝则在停留期到点后自行离场

---

## 安装

1. 在 [Steam 创意工坊（ID: 3828162134）](https://steamcommunity.com/sharedfiles/filedetails/?id=3828162134) 订阅，或克隆本仓库
2. 启用 *Trader ships (continued)* 并重启游戏

手动安装时，把 `About`、`1.7`、`Languages`、`Sounds`、`Textures` 和 `LoadFolders.xml` 复制到 `RimWorld/Mods/` 下的一个文件夹里。

**不需要 Harmony**。它只用于通讯台那个可选入口，缺少时自动降级为「只有独立建筑可用」。

**不需要任何 DLC**。有 Royalty 时飞船会用降落信标作为备选落点。

---

## 设置

全部在 **选项 → 模组设置 → Trader ships**。

| 选项 | 默认 | 生效时机 | 说明 |
| --- | --- | --- | --- |
| 禁用轨道商船 | 开 | 立即 | 用飞船替换原版轨道商船 |
| 需要通讯台 | 开 | 立即 | 没通讯台就不来飞船 |
| 启用任务 | 开 | 立即 | 与飞船相关的任务 |
| 允许呼叫贸易飞船 | 开 | 立即 | 启用付费呼叫 |
| 呼叫飞船花费 | 1000 银 | 立即 | 基础价，按好感浮动 |
| 呼叫飞船需要盟友 | 开 | 立即 | 关掉后中立派系也能叫 |
| 呼叫飞船冷却 | 1 天 | 立即 | 同派系两次呼叫间隔 |
| 飞船停留时长 | 1 天 | 仅影响新船 | 降落后等多久离开 |
| 同时停靠上限 | 2 艘 | 立即 | 超过不再触发新飞船 |
| 飞船最小间隔 | 0 天 | 立即 | 随机飞船之间的最小间隔 |
| 偷渡客概率 | 25% | 立即 | 降落后带下偷渡客的几率 |
| 偷渡客停留 | 3 天 | 立即 | 偷渡客待多久 |
| 派系专属贸易飞船 | 开 | **需重启** | 按物品库生成主题船型 |
| 烘焙飞船贴图 | 开 | **需重启** | 合并部件贴图以减少绘制调用 |
| 飞船配色 | 开 | **需重启**（已生成的船） | 随机涂装 |

---

## 派系专属飞船的美术

默认使用程序化随机拼装的飞船外观。想给某个派系单独画一艘，放到：

```
Textures/TraderShips/Factions/<派系defName>.png
```

规格：**700×300 像素**（7×3 格，100 像素 = 1 格），透明背景，朝东。放进去重启即生效，没有对应文件的派系继续用随机外观。

派系船型也可以手动指定，用 XML 补丁：

```xml
<Operation Class="PatchOperationAdd">
  <xpath>Defs/FactionDef[defName="Empire" and not(modExtensions)]</xpath>
  <value><modExtensions /></value>
</Operation>

<Operation Class="PatchOperationAddModExtension">
  <xpath>Defs/FactionDef[defName="Empire"]</xpath>
  <value>
    <li Class="TraderShips.FactionShipExtension">
      <shipKinds>
        <li>Orbital_Empire</li>
      </shipKinds>
    </li>
  </value>
</Operation>
```

注意 `PatchOperationAddModExtension` 的 `value` 是 `XmlContainer`，必须写成 `<value><li Class="..."/></value>`，不能写成 `<value Class="...">`——后者会抛 `InvalidCastException`。Empire 原本没有 `<modExtensions>` 节点，所以前面那步 `PatchOperationAdd` 是必需的。

写 `<noTradeShips>true</noTradeShips>` 可彻底禁止某派系有飞船。

---

## 代码结构

```
Source/
├── CompShip.cs                         飞船组件：生成、索赔、击毁、偷渡、离开
├── Building_Ship.cs                    玩家自有飞船的船体
├── ShipSprite.cs / ShipSpriteBaker.cs  程序化拼装 + 烘焙成单张贴图
├── FactionShipGenerator.cs             启动期按派系生成船型（9 主题 + 人才 + 奴隶）
├── StockGenerator_ThingDefs.cs         按具体 ThingDef 供货（固定材质、不出品质）
├── StockGenerator_Pawns.cs             按 PawnKindDef 供货（人才 / 奴隶）
├── StockGenerator_BuyCategories.cs     按原版分类 / tradeTag 声明收购范围
├── CommsShipRequest.cs                 呼叫、定价、银两结算
├── ShipPurchase.cs                     买船
├── Dialog_TradeDispatch.cs             贸易台主界面（主从式，带缓存）
├── Building_TradeDispatch.cs           贸易台建筑
├── CompUseEffect_TradeDispatch.cs      右键使用 → 开窗
├── GameComponent_TalentPawns.cs        人船成交后转殖民者 / 奴隶（带超时清理）
├── ChoiceLetter_Stowaway.cs            偷渡客「请求收留」信件
└── Debug.cs                            开发者菜单 + 自检探针
```

### 几个不显然的实现细节

**为什么不用原版 `StockGenerator_Slaves`**：它在构造 `PawnGenerationRequest` 时读 `this.trader.orbital`，而我们这些在 `[StaticConstructorOnStartup]` 里凭空创建、直接塞进 `DefDatabase` 的 `TraderKindDef` 没有经过 `Trader` 初始化，`gen.trader` 是 null，原版生成器直接抛 `NullReferenceException`——表现是「整船一件货都生成不出来」。

**为什么成交后才改 GuestStatus**：`Pawn_GuestTracker.SetGuestStatus` 内部访问 `HostFaction`，生成阶段 host 为 null 会空引用。所以生成时只登记 `thingIDNumber`，等 pawn 真的加入玩家后再用 `Faction.OfPlayer` 作为 host 设置。

**为什么扣款要返回实扣数**：`AvailableSilver` 只统计「真正付得出」的银（递归容器、排除玩家禁用的）。如果只做事前判断、事后不校验，两次读取之间银两被搬走就会变成「判定通过 → 只扣到一部分 → 照样放行」。

---

## 构建

**不需要 .NET 10 SDK**（本机只有 9.0.306，且 .NET 10 SDK 在国内镜像拿不到）。改走 Roslyn：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build.ps1
```

脚本做三件事：用 `tools/make-rsp.py` 生成响应文件（引用 `Microsoft.NETCore.App/10.0.7` 的托管程序集 + 游戏的 `Assembly-CSharp.dll` / `UnityStubs.dll` / `Waylith.dll`），调 `sdk/9.0.306/Roslyn/bincore/csc.dll`，最后打印 MD5。

关键点：必须带 `-nostdlib+ -noconfig`，否则会引入 SDK 9 的 `System.Runtime 9.0.0.0` 并报 `CS1705`。`build17.rsp` 没开 `/deterministic`，所以 **MD5 每次编译都不同**，体积恒为 121856——不要用 MD5 判断是否同一份代码。

编译前必须**完全退出游戏**，否则 DLL 被锁会报 `CS2012`。

部署：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/deploy-1.7.ps1
```

脚本每次从零重建目标目录——`Copy-Item -Recurse` 到已存在的目录会把源嵌套进去，产生坏掉的 `1.7/1.7/` 结构。当前部署 171 个文件。

---

## 上传创意工坊

```cmd
upload.bat
```

账号填 `rb3481141298`（走 steamcmd 缓存凭据），密码由 steamcmd 自己询问，脚本不保存。

脚本依次：从 `workshop-description.txt` 重新生成 VDF（自动带上 `PublishedFileId.txt` 里的 ID，避免新建重复条目）→ 跑 28 项预检 → 检查 steamcmd / VDF / DLL 唯一性 → 上传 → 到日志里找**本次运行新增的** `Upload finished for workshop item ... : OK`。

> 早期版本的校验会取全日志最后一条成功记录，于是把上一次会话的旧记录当成这次的成功——现在只认本次运行之后的行。

---

## 测试

游戏内的自检探针：开发者菜单 → **Spawn trade ship → === Run all self-test probes ===**，结果写到 `_autotest/probe.log`，包含所有轨道船型的现金区间、派系可派船型、建筑状态。

配套脚本：

```bash
python tools/logslice.py <Player.log> "TraderShips"   # 只打我们相关的行
python tools/ctx.py "Exception" 30 6 3                # 异常前后文
python tools/themestats.py <Player.log>               # 主题分布统计
```

用的是 upload.bat 里那个 Python（dsh-primary-runtime），**注意不要放名为 types.py 的脚本进 tools/**——它会遮蔽标准库 types，让所有 import 崩掉。

---

## 已知限制

| 现象 | 原因 |
| --- | --- |
| 落地动画看不到船体，只有影子 | `ShipJob_FlyAway` 只在离场时 `Contents.SetShuttle`，进场时为空 |
| 旧存档的船按旧时间离场 | 存档里存的是旧 `ticksUntilDeparture`，新设置不回溯 |
| 中途移除模组会报错 | `OrbitalTraderArrival.workerClass` 指向本模组 |
| 存档里已存在的船点「拒绝」不扣好感 | 那些船没有 `ownerFaction` 字段（1.8 新增） |
| 会自己 Tick 的建筑不再出现在任何船上 | 1.8.1 起过滤「自己声明 Tick」或「挂着会 Tick 的 ThingComp」的建筑；代价是个别模组的功能性建筑买不到 |
| 与 Fantasy Biotech 的「替换商船来信」冲突 | 两个模组都改写 `IncidentDef[OrbitalTraderArrival]/workerClass`，后加载的生效。把 Trader ships 排在它之后可恢复落地飞船 |

---

## 1.7 移植要点

RimWorld 1.7 用 Ludeon 自研的 **Waylith 引擎**替换了 Unity 与 Mono，运行在 **.NET 10 / CoreCLR** 与 Vulkan 渲染器上。

| 问题 | 处理 |
| --- | --- |
| `Designator.DraggableDimensions` 被移除 | 改用 `DrawStyleCategory => DrawStyleCategoryDefOf.Areas` |
| `Skyfaller` 的动画计时器只剩私有字段 | 改用新的 `GetDrawPositionAndRotation` 钩子 |
| Harmony 曾是硬依赖 | 改为可选，通过 `GenTypes.GetTypeInAnyAssembly` 反射解析 |
| `[DebugAction]` 在模组多的环境下不注册 | 改为自注册进 `Dialog_Debug.rootNode` |
| 每个部件一次 `Graphics.DrawMesh` 开销大 | 改为烘焙成单张贴图，每船一次绘制调用 |

完整记录见 [PORTING-1.7.md](PORTING-1.7.md)。

---

## 语言

英文、西班牙语、简体中文、繁体中文。当前各 **136 个键**，预检会核对键数与占位符一致性。

---

## 贡献

本仓库对**每一次代码修改**都有固定流程，见 [AGENTS.md](AGENTS.md)：

> 改完 → 子代理审核（功能 + 性能两组）→ 处理审核意见 → 编译部署预检 → 更新 README 和 TODO → 提交推送 GitHub

三个要点：

- **子代理审核不可跳过**，且必须开两个：功能组会漏性能问题，性能组会漏资金漏洞
- **审核结论要逐条核实**，子代理会编造不存在的 API（例如声称 `TraderKindDef.canTrade` 存在）
- **编译通过 ≠ 已验证**，运行时行为必须由用户在游戏内确认

---

## 致谢

- **AUTOMATIC1111** — 原作：设计、代码、飞船美术
- **IoIqq** — 1.7 / Waylith 引擎移植、中文翻译、贸易台与派系船队系统

如果你是原作者且不希望这个续作存在，请联系我，我会下架。

MIT 协议，与原作一致。见 [LICENSE](LICENSE)。