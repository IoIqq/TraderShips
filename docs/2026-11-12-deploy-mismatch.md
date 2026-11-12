# 一个 1.7 mod 的静态排查工具集（2026-11-12）

排查"创意工坊版 Trader ships 报 type kind 17 + 贸易台不见 + 着陆区只能一格一格点"时
新增的两个小工具。都不需要启动游戏。

## `tools/pemeta.py` — 只读元数据

```bash
python tools/pemeta.py 1.7/Assemblies/TraderShips.dll      # TypeDef/TypeRef/AssemblyRef 计数
python tools/pemeta.py 1.7/Assemblies/TraderShips.dll -v   # 附全部 BCL TypeRef
```

用途：确认 DLL 编译时链的是哪套 BCL、CLI 元数据版本、表规模。
`Expected reference type but got type kind 17` 这类报错，第一步就该看这里。

## `tools/ldprobe` — 用 System.Reflection.Metadata 全量审计

```bash
cd tools/ldprobe
dotnet build -c Release
dotnet run -c Release --no-build -- <dll>              # 签名审计 + AssemblyRef
dotnet run -c Release --no-build -- --types <dll>      # 命名空间 / 顶层类型清单
```

- **签名审计**：把 348 个字段、523 个方法签名全部解码一遍。全过 → 元数据干净，
  故障在加载器环境（重定向、WCL 注入、宽松绑定），不在源码。
- **`--types`**：核对 XML 里写的类名（`TraderShips.Designator_AreaLandingZoneExpand` 等）
  是否真的存在于 DLL。类名对不上时 XML 补丁不会报错，会在实例化设计器/建筑时才抛异常。

## `tools/bcl-versions.ps1` — 游戏自带 BCL 版本

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/bcl-versions.ps1
```

打印 `H:\steam\steamapps\common\RimWorld` 里 `System.Private.CoreLib` / `System.Runtime`
等程序集的版本，用来和 mod 的 AssemblyRef 对照。

## XML 里引用的 20 个类名全部存在

```text
Building_Ship  Building_TradeDispatch  CompPropertiesShip  CompUseEffect_TradeDispatch
Designator_AreaLandingZoneClear  Designator_AreaLandingZoneExpand
FactionShipExtension  IncidentWorkerTraderShip  IncidentWorkerTraderShipArrival
IncidentWorkerTraderShipCrash  JobDriverTradeWithShip  QuestNode_CreateTraderShip
QuestNode_ShipsTradeRequest_Initiate  ShipgenDef  ShipSpritePartDef
SkyfallerTradeShip  SkyfallerTradeShipShuttleIncoming  SkyfallerTradeShipShuttleLeaving
StepAddPart  StepShipgen
```

所以"贸易台找不到"不是 Def 写错名字 —— Def 在、类在、分类是 `Misc`。
只剩 Def 加载期被丢弃这一种解释，而 Def 加载期唯一的变量就是
**DLL 加载本身**（`StaticConstructorOnStartup` 抛异常 / 类型解析失败）。
这又把问题推回 `AggregateException` 那一条。

## 本机实测基线（2026-11-12）

| 项 | 值 |
| --- | --- |
| 游戏 | 1.7.5005 rev1082，`Assembly-CSharp 1.7.9810.32479` |
| 运行时 | `Microsoft.NETCore.App 10.0.15-waylithclr.dev` |
| 仓库 DLL 编译时引用 | `Assembly-CSharp 1.7.9810.32479`（本次重编译后） |
| 旧 DLL（10-27/11-07 那份） | `Assembly-CSharp 1.7.9805.38275` ← 与当前游戏差 5 个小版本 |
| BCL | 均为 `System.Private.CoreLib 10.0.0.0` |
| CLI 元数据头 | `1.1 / v4.0.30319`（与工坊 246 个 1.7 模组一致） |
| 签名完整性 | 348 字段 / 523 方法，0 失败 |
