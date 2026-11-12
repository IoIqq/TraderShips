# 1.7 (Waylith) 拖拽类设计器的正确写法 —— 实测元数据

本文记录用 `tools/ldprobe --designator` 从 `Assembly-CSharp.dll` **直接读出**的事实，
不是从文档或直觉推出来的。凡是和直觉冲突的地方，以这里为准。

## 事实一：`Designator_Cells` 是空的

```text
== Verse.Designator_Cells  attrs=Public, Abstract, BeforeFieldInit
（没有任何成员）
```

它只是一个"我按格操作"的标记基类。**拖拽能力不在这一层**。

## 事实二：拖拽的开关是 `DrawStyleCategory` → `DrawStyleDef` 的字段

```text
== Verse.Designator
   prop DragDrawMeasurements   virtual protected
   prop DrawStyleCategory      virtual protected
   field useMouseIcon
   field soundDragSustain
   field soundDragChanged

== Verse.DrawStyleDef
   field drawOutline
   field drawArea
   field drawShortSideMeasurement
   field canSnap
   field drawStyleType
```

也就是说：**能拖出多大的矩形，由你选的 DrawStyle（原版那排"直线/矩形/填充矩形/散点"按钮）
决定，而不是由设计器自己声明**。原版区域设计器（`Designator_AreaHomeExpand` 等）
在元数据里一个成员都没有 —— 全是继承的。

## 事实三：`Designator_Zone` 只覆写了两个

```text
== RimWorld.Designator_Zone
   prop DragDrawMeasurements   virtual protected
   prop DrawStyleCategory      virtual protected
```

`Designator_ZoneAdd` / `Designator_ZoneDelete` 都继承它。所以自定义区域设计器**最少**
只需要这两个 override，其余走基类默认。

## 对本 mod 的含义

`Source/Area/Designator_AreaLandingZone.cs` 目前的写法：

```csharp
public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.Areas;
public override bool DragDrawMeasurements => true;
```

方向是对的（和原版区域设计器一致），但用户报告"只能一格一格点"，
说明**拖拽在运行时没生效**。静态元数据无法证明运行时行为，需要按下面顺序实机排查：

1. 打开开发者模式 → `DrawStyle` 相关的调试信息（或直接看设计器面板上有没有那排
   直线/矩形/填充矩形小按钮）。**没有按钮 = `DrawStyleCategory` 没接上**，
   此时无论怎么写都只能单格点击。
2. 若按钮在、但拖动没反应 → 问题是 `SelectedUpdate()` 或 `CanDesignateCell()` 在
   拖拽过程中拒绝格子。注意 `CanDesignateCell` 现在对"已经是目标状态"的格子返回
   `false`（`Map.areaManager.LandingZone()[c] != (mode == DesignateMode.Add)`），
   拖拽擦过区域边缘时会被这个判据打断。
3. 区域设计器还依赖 `DesignationCategoryDef.specialDesignatorClasses` 注入
   （`1.7/Patches/Designations.xml` 注入到 `Zone` 分类），注入失败就是"面板里根本没有这两个按钮"，
   和"只能一格一格点"是两回事，要分开确认。

## 附带确认

`Designator_AreaHomeExpand` / `Designator_AreaHomeClear` 在元数据里也是零成员，
说明原版的"扩展/清除"这对设计器本身就没有比本 mod 多写任何东西。
所以"能拖一片"这件事**不可能**靠给设计器加成员解决，只能靠 DrawStyle + 拖拽运行时路径。
