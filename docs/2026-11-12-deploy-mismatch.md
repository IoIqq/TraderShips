# 2026-11-12 玩家反馈排查记录

> 排查期间写过的临时脚本（PE 元数据读取、`System.Reflection.Metadata` 签名审计、
> VDF 结构自检）已删除，仓库里不保留。本文只留结论和可复现的方法。

## 反馈原文（Steam，11 月 11 日 18:06）

玩家环境：原版 + Royalty / Biotech / Ideology / Anomaly，只前置 Harmony，无其他模组。

1. 建筑规划里找不到贸易台
2. 区域规划里的「着陆区扩展 / 清除」只能一格一格点，不能拉一片
3. 开发者模式报错：
   `Could not execute post-long-event action. Exception: System.AggregateException: ... (Could not load type of field 'TraderShips.Area_LandingZone+<LandingLocations>d__9:<indices>5__1' (4) due to: Expected reference type but got type kind 17)`

## 查实的部分

| 项 | 结果 | 怎么查的 |
| --- | --- | --- |
| DLL 与源码是否同一份 | **是**，98 个 TypeDef 全是 `TraderShips.*` | 读 DLL 的 TypeDef 表 |
| XML 引用的 20 个类名 | **全部存在** | 同上，对照 `1.7/Defs`、`1.7/Patches` |
| 字段/方法签名 | **348 字段 + 523 方法全部可解码，0 失败** | `System.Reflection.Metadata` 的 `DecodeSignature` |
| CLI 元数据头 | `1.1 / v4.0.30319` | 与创意工坊里 246 个 1.7 模组同分布 |
| 旧 DLL 引用的游戏程序集 | `Assembly-CSharp 1.7.9805.38275` | DLL 的 AssemblyRef 表 |
| 本机游戏程序集 | `Assembly-CSharp 1.7.9810.32479` | `[Reflection.AssemblyName]::GetAssemblyName()` |
| BCL | 两边都是 `System.Private.CoreLib 10.0.0.0` | 同上 |

### 关于 `type kind 17`

ECMA-335 element type `0x11 = ELEMENT_TYPE_VALUETYPE`。一个放 `HashSet<int>` 引用的字段
被读成值类型，而签名本身解码完全正常 —— 说明故障发生在**加载器把签名解析到运行时类型**
那一步，不在源码。可控变量只剩「编译时用的 `Assembly-CSharp` 比游戏旧」这一条，
已用当前游戏程序集重新编译并部署。

### 关于「贸易台找不到」

Def 在（`TraderShipsDispatchTower`，`designationCategory=Misc`）、类在、分类对。
所以不是写错了，只剩「Def 加载期被丢弃」，而加载期唯一的变量就是 DLL 加载本身 ——
这条和上面的 `AggregateException` 指向同一个原因。

### 关于「着陆区只能一格一格点」

从 `Assembly-CSharp` 的元数据直读出来：

- `Verse.Designator`：`DragDrawMeasurements`、`DrawStyleCategory` 两个 virtual 属性
- `Verse.DrawStyleDef`：`drawStyleType`、`drawArea`、`canSnap` 等字段
- `Verse.Designator_Cells`：**零成员**
- `RimWorld.Designator_AreaHomeExpand` / `...Clear`：**零成员**

结论：「能拖出一片」由 `DrawStyleCategory` → `DrawStyleDef.drawStyleType` 决定，
不可能靠给设计器加成员实现。本 mod 的写法方向正确，但运行时没生效。

实机确认只看一件事：**设计器面板上有没有那排直线/矩形/填充矩形按钮**。
没有按钮 = `DrawStyleCategory` 没接上；有按钮但拖不动 = `CanDesignateCell` 的
「已是目标状态就返回 false」打断了擦边拖拽。

## 复现方法

```powershell
# 游戏侧程序集版本
[Reflection.AssemblyName]::GetAssemblyName('H:\steam\steamapps\common\RimWorld\Assembly-CSharp.dll').FullName

# mod 编译时引用的游戏程序集版本 —— 看 DLL 的 AssemblyRef 表
# 任意 IL 工具均可，例如：dotnet tool install -g dotnet-ildasm; dotnet-ildasm -i 1.7/Assemblies/TraderShips.dll
```
