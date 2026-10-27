# Trader Ships — RimWorld 1.7 (Waylith Engine) 移植说明

## 背景结论（先纠正一个前提）

RimWorld 1.7 使用的引擎不是 Vulkan，而是 Ludeon 自研的 **Waylith Engine**。官方公告：
<https://ludeon.com/blog/2026/10/announcing-the-waylith-engine-and-update-1-7/>

不过你的直觉方向是对的——它确实是一次渲染/运行时的大换血：

| 项目 | 1.6 | 1.7 (Waylith) |
| --- | --- | --- |
| 引擎 | Unity | Waylith（自研，基于 Brutal 框架） |
| 图形后端 | D3D11 / OpenGL | **Vulkan**（`Brutal.Vulkan*.dll`、`slang`、SPIR-V 缓存） |
| 运行时 | Mono / .NET Framework 3.5–4.7.2 | **.NET 10 / CoreCLR**（`Microsoft.NETCore.App 10.0.13-waylithclr`） |
| UnityEngine | 真实 Unity | **`UnityStubs` 兼容层**（`WaylithClr.AssemblyNameRedirects` 把 `UnityEngine*` 重定向过去） |
| 核心资源 | `Mods/Core/Textures/*.png` | `Assets.zip` 内预处理为 `.dds.split` |

**关键含义：mod 的 C# 代码不再跑在 Mono 上，而是跑在 .NET 10 CoreCLR 上；UnityEngine API 由一层 stub 模拟。**

## 本机环境实测

- 游戏版本：`1.7.4990 rev1069`（`H:\steam\steamapps\common\RimWorld`）
- 程序集：`Assembly-CSharp v1.7.9795.32073`，目标框架 `.NETCoreApp,Version=v10.0`
- Harmony：`brrainz.harmony` 的 `1.7` 目录，版本 **v3.0.0.0**
- 你当前 mod 列表共 **772** 条

## 移植中的真实改动

只改了 4 个文件，全部是编译器或 1.7 API 变更强制要求的：

### 1. `Designator_AreaLandingZone.cs` — 硬编译错误

`Designator.DraggableDimensions` 在 1.7 已被删除（拖拽行为重构进 `DrawStyle` / `DrawStyleWorker`）。
改为与 vanilla 区域设计器一致的写法：

```csharp
public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.Areas;
```

### 2. `SkyfallerTradeShip.cs` — 消除运行时反射依赖

原代码用 `Traverse.Create(this).Property<float>("TimeInAnimation")` 反射一个**私有**属性，然后手抄了一遍官方的位移/旋转曲线计算。

1.7 把 `TimeInAnimation` 变成了 `protected`，并且 **新增了 `virtual GetDrawPositionAndRotation(ref Vector3, out float)`**，正是那段手抄逻辑的官方版本。

改动后：
- 不再依赖 Harmony 反射私有成员（这种依赖在换引擎时最容易静默失效）
- 直接复用官方曲线计算，跟随核心逻辑更新
- 基类已有 `ShadowMaterial`，删掉了重复实现（同时消掉 `CS0108` 警告）

### 3. `StepAddPart.cs` / `ShipSprite.cs` — 健壮性防御（非 1.7 强制）

原版就有的隐患：`RandomElement()` 在空集合上返回 `null`（只打 warning），随后 `.def.graphicData` 直接 NRE；`parts.Min(...)` 对空列表会抛 `InvalidOperationException`。
当前 XML 每个 category 都有 def，不会触发；但加了防御，避免被其他 mod 覆盖 def 后崩掉。

## 为什么编译目标是 net10.0

抽样统计了你本机 186 个 1.7 mod 程序集：

| 目标框架 | 数量 |
| --- | --- |
| `.NETCoreApp,Version=v10.0` | **130** |
| `.NETFramework,Version=v4.7.2` | 28 |
| `.NETFramework,Version=v4.8` | 17 |
| 无 TFM 属性 | 9 |
| `.NETCoreApp,Version=v6.0` | 2 |

主流生态（VEF、Rimatomics 等）已经重新编译到 net10.0。本 mod 也按 net10.0 编译。

## 静态审查做了什么

无法实机测试的前提下，改用「直接读真实程序集元数据」来验证，而不是靠反编译源码猜：

| 验证项 | 方法 | 结果 |
| --- | --- | --- |
| 依赖类型/成员是否存在 | `System.Reflection.Metadata` 直读 `Assembly-CSharp.dll` | 全部存在 |
| 访问级别与 virtual | 读 MethodAttributes | Harmony 两个补丁目标均为 `protected virtual`，可 patch |
| 覆写目标是否仍 virtual | 逐个校验 `DrawAt`/`Impact`/`CompTick`/`DrawStyleCategory` 等 | 14 项全部 virtual |
| XML 字段是否失效 | 提取 defs 中 114 个小写字段名，比对 1.7 类型成员 | 核心字段 103 个全命中；另 11 个是 mod 自定义字段（已证实存在于 `CompPropertiesShip`/`StepShipgen`） |
| 贴图/声音引用 | 解析所有 `texPath`/`uiPath`/`clipPath`/`shadow` | 全部可解析（含 Core 的 `Assets.zip` 内 `.dds.split`） |
| PNG 是否仍需转换 | 查 `ModContentLoader.LoadTexture` | **不需要**，非 DDS 走 `Texture2D.LoadFromFile`，PNG/JPG 原生支持 |
| LoadFolders 版本键匹配 | 读 `InitLoadFolders` / `FoldersForVersion` | `<v1.7>` 去前缀变 `1.7`，精确匹配失败后走 `<=` 回退分支命中，有效 |
| 存档兼容 | 读 `PassingShip.ExposeData` / `TradeShip.ExposeData` / `Scribe_*` 签名 | 序列化字段与 API 未变 |

## 测试时请重点关注

这些是静态分析**无法**排除、必须实机确认的点：

1. **飞船渲染**（最高风险）
   `ShipSprite.Draw` 用 `Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0)` 逐部件绘制，配合 `CutoutComplex` shader 着色。
   - `CutoutComplex` 在 1.7 仍存在（`Data/Core/Shaders/Map/CutoutComplex.frag.glsl` 已编译成 SPIR-V）
   - `Graphics.DrawMesh` 在 1.7 核心代码中仍被大量使用
   - 但 Waylith 的 Vulkan 管线对 `DrawMesh` 的**批次/深度/alpha 处理**可能有差异 → 留意：船是否显示、是否黑块、是否穿插/闪烁

2. **降落与起飞动画**
   `SkyfallerTradeShip.DrawAt` 已改用官方 `GetDrawPositionAndRotation`。确认动画曲线、旋转角度、阴影位置正常。

3. **Odyssey 兼容**
   Odyssey 引入了 gravship（`Building_GravEngine`、`CompGravshipThruster`、`CompShipLandingBeacon` 等），可能与「地表降落飞船」的占位/落点搜索冲突。
   本 mod 会优先用着陆区 → 再试 `DropCellFinder.TryFindShipLandingArea`（Royalty 的 `ShipLandingBeacon`）→ 最后随机落点。

4. **Harmony 补丁是否生效**
   `disableOrbital` 默认 `true`，会拦截 `IncidentWorker_OrbitalTraderArrival.CanFireNowSub` / `TryExecuteWorker`。
   验证方法：游戏内开发者模式 → 事件调试，确认轨道商船不再触发而是来飞船。

5. **存档往返**
   从 1.6 存档载入、再存再读，确认飞船及其货物不丢失。

## 构建与部署

```bash
# 编译（注意需要 .NET 10 SDK，系统自带的是 9.0，已装在 E:\AI\tools\dotnet10）
cd E:\AI\mods\rimworld\TraderShips\Source
DOTNET_CLI_HOME=/e/AI/tools/dotnet-home \
TMP=/e/AI/tools/tmp TEMP='E:\AI\tools\tmp' \
"/e/AI/tools/dotnet10/dotnet.exe" build TraderShips17.csproj -c Release
```

```powershell
# 部署到游戏 Mods 目录（每次会先清空目标再复制，避免目录嵌套）
powershell -NoProfile -ExecutionPolicy Bypass -File E:\AI\mods\rimworld\TraderShips\tools\deploy-1.7.ps1
```

部署位置：`H:\steam\steamapps\common\RimWorld\Mods\TraderShipsDev`

改名后的元数据：

| 字段 | 值 |
| --- | --- |
| name | `Trader ships (continued)` |
| author | `AUTOMATIC, IoIqq` |
| packageId | `IoIqq.traderships.continued` |

包名已与原版 `automatic.traderships` 区分，两者**可以并存**（但功能重复，建议只启用其一）。
注意：包名一旦改动，指向旧包的存档/预设会认作不同 mod。

## 目录结构

```
TraderShips/
├── About/
│   ├── About.xml          (1.5, supportedVersions 1.1–1.5)
│   └── About-1.7.xml      (1.7 用，部署时覆盖为 About.xml)
├── 1.7/
│   ├── Assemblies/TraderShips.dll
│   ├── Defs/*.xml
│   └── Patches/Designations.xml
├── Source/
│   ├── TraderShips17.csproj   (新增，net10.0)
│   └── *.cs
├── tools/                  (新增，验证与部署脚本)
├── Textures/ Sounds/ Languages/
└── LoadFolders.xml (部署时生成)
```

> `tools/deploy-1.7.ps1` 特意写成纯 ASCII：Windows PowerShell 5.1 会误读无 BOM 的 UTF-8 中文注释。

## 实机验证中修掉的两个致命问题

这两处都是静态分析看不出来、只有真正启动游戏才会暴露的，记录原因以免以后回退。

### 1. Harmony 硬依赖导致整个加载失败（最严重）

`csproj` 里直接引用了 workshop 的 `0Harmony.dll`，于是 `TraderShips` 构造函数里的
`new Harmony(...)` 变成硬依赖。玩家没启用 Harmony 时，CLR 解析 `HarmonyLib` 直接抛
`TypeLoadException`，连锁反应是：

```
TraderShips 构造失败 (TypeLoadException: HarmonyLib.Harmony)
  → 游戏判定 play-data 加载失败
  → 自动重置 ModsConfig（只剩 Core）
  → mod 已不在 RunningModsListForReading
  → ContentFinder 找不到贴图 (TraderShips/empty 等)
  → 82 条 GenStuff.DefaultStuffFor "Sequence contains no elements"
```

后面两类报错全是连带结果，不是独立缺陷。修法是让 Harmony 真正可选：

- `csproj` 移除 `0Harmony` 引用
- 补丁类去掉 `[HarmonyPatch]` 特性，改为普通静态方法
  （特性驱动会让 CLR 在枚举类型时就解析 `HarmonyLib`，照样崩）
- 构造函数用 `GenTypes.GetTypeInAnyAssembly("HarmonyLib.Harmony")` 反射查找，
  找不到只打一行 warning；补丁用 `Harmony.Patch(...)` 手工调用

验证手段：直接读 DLL 元数据，确认 `AssemblyRefs` 里无 `0Harmony`、无 `HarmonyLib.*` TypeRef、
无 Harmony 相关 CustomAttribute。见 `tools/verify-harmony-free.cs`。

### 2. `[DebugAction]` 在重度 MOD 环境下不注册

特性确实写进了 DLL（`tools/verify-debugattr.cs` 可证实参数正确），但
`DebugTabMenu_Actions.InitActions` 遍历的是 `GenTypes.AllTypes` —— **静态缓存，只算一次**，
且只包含 `AllActiveAssemblies` 里的程序集。772 个模组的环境下，该缓存在模组程序集进入
列表前就被计算了，所以方法永远扫不到。

修法是双保险：保留特性（干净环境仍有效），另加 `[StaticConstructorOnStartup]` 自注册 ——
主动调 `Dialog_Debug.TrySetupNodeGraph()` 建好节点图，再检查 `rootNode.children`
里有没有同名项，没有就 `AddChild` 追加。相关成员在 1.7 均为 public，不需要反射。

### 3. 调试菜单点击无反应的旧写法

`Dialog_DebugOptionListLister` + `DebugMenuOptionMode.Tool` 在 1.7 已失效：点击时先
`Close()` 再赋 `DebugTools.curTool`，但对话框在关闭那一帧会把 `curTool` 清成 null，
工具被丢弃，表现为「点了没反应」。1.7 自己的顶栏改用 `DebugActionNode`（挂在
`Dialog_Debug` 上所以能存活），本模组随之迁移。

## 测试环境备忘

- 秒建造：1.7 的 `DebugSettings` **已没有** `InstantBuild` 开关，只剩 `godMode`
  （开发者工具栏最右边的按钮）。开启后建造不消耗资源且瞬间完工。
- 快速铺地形：动作菜单 `Map` 列 → `T: Set terrain (rect)...`，测降落比真的盖房快。
- 部署脚本每次会清空目标目录再复制：`Copy-Item -Recurse` 到已存在目录会把源目录**套进去**，
  产生 `1.7/1.7/`、`Textures/Textures/` 这种错误结构。

## 待办 / 后续

- [x] 实机验证渲染与交易交互 —— 已通过
- [ ] 如需上工坊，把 `About/About-1.7.xml` 的内容提为主 `About.xml` 并加 `<li>1.6</li>`
- [ ] `Source/packages.config` 和旧 `TraderShips.csproj`（Framework 4.7.2）保留作 1.5 及更早版本的构建，不要删除
- [ ] `tools/` 下的验证程序仅用于开发期，发布时不必打包
