# 1.7 (Waylith) 拖拽类设计器的正确写法 —— 实测元数据

> 结论来自直接读 `Assembly-CSharp.dll` 的元数据，不是从文档或直觉推的。
> 排查用的临时探针已删除；复现见文末。

## 事实一：`Designator_Cells` 是空的

```text
Verse.Designator_Cells   Public, Abstract
  （没有任何成员）
```

它只是「我按格操作」的标记基类，**拖拽能力不在这一层**。

## 事实二：拖拽的开关是 `DrawStyleCategory` → `DrawStyleDef`

```text
Verse.Designator
  prop DragDrawMeasurements    virtual protected
  prop DrawStyleCategory       virtual protected
  field useMouseIcon
  field soundDragSustain
  field soundDragChanged

Verse.DrawStyleDef
  field drawStyleType
  field drawOutline
  field drawArea
  field drawShortSideMeasurement
  field canSnap
```

**能拖出多大的矩形，由你选的 DrawStyle（原版那排「直线 / 矩形 / 填充矩形 / 散点」按钮）决定，
不由设计器自己声明。** 原版 `Designator_AreaHomeExpand` 在元数据里一个成员都没有。

## 事实三：`Designator_Zone` 只覆写两个

```text
RimWorld.Designator_Zone
  prop DragDrawMeasurements    virtual protected
  prop DrawStyleCategory       virtual protected
```

`Designator_ZoneAdd` / `Designator_ZoneDelete` 都继承它。自定义区域设计器**最少**
只需要这两个 override。

## 对本 mod 的含义

`Source/Area/Designator_AreaLandingZone.cs` 目前：

```csharp
public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.Areas;
public override bool DragDrawMeasurements => true;
```

方向和原版区域设计器一致，但玩家报告「只能一格一格点」，说明拖拽在运行时没生效。
按下面顺序实机确认：

1. 打开设计器面板，看**有没有那排直线 / 矩形 / 填充矩形小按钮**。
   没有 = `DrawStyleCategory` 没接上，此时怎么改都只能单格点击。
2. 按钮在、但拖动没反应 → 问题在 `SelectedUpdate()` 或 `CanDesignateCell()`。
   注意后者对「已经是目标状态」的格子返回 `false`
   （`Map.areaManager.LandingZone()[c] != (mode == DesignateMode.Add)`），
   拖拽擦过区域边缘时会被它打断。
3. 还要区分另一种情况：`1.7/Patches/Designations.xml` 把两个设计器注入 `Zone` 分类，
   注入失败就是「面板里根本没有这两个按钮」，和「只能一格一格点」不是一回事。

## 复现

```powershell
# 用任意 IL / 元数据工具列出 Verse.Designator、Verse.DrawStyleDef 的成员
dotnet tool install -g dotnet-ildasm
dotnet-ildasm -i "H:\steam\steamapps\common\RimWorld\Assembly-CSharp.dll" -f Verse.Designator
```

或用 `System.Reflection.Metadata` 十行代码遍历 `TypeDefinition` 的 `GetProperties/GetFields`。
