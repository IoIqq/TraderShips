// 校验访问级别：Harmony 补丁目标必须可被外部程序集 patch，
// 且 TraderShips 覆写的成员在 1.7 中必须仍是 virtual/protected-or-public。
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

string asmPath = @"H:\steam\steamapps\common\RimWorld\Assembly-CSharp.dll";

using var fs = File.OpenRead(asmPath);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();

string FullName(TypeDefinition td)
{
    string ns = md.GetString(td.Namespace);
    string name = md.GetString(td.Name);
    if (!td.GetDeclaringType().IsNil)
        return FullName(md.GetTypeDefinition(td.GetDeclaringType())) + "+" + name;
    return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
}

var typeMap = new Dictionary<string, TypeDefinition>();
foreach (var th in md.TypeDefinitions)
{
    var td = md.GetTypeDefinition(th);
    var fn = FullName(td);
    if (!typeMap.ContainsKey(fn)) typeMap[fn] = td;
}

// Harmony 补丁目标：(类型, 方法名)
var patchTargets = new (string Type, string Method)[]
{
    ("RimWorld.IncidentWorker_OrbitalTraderArrival", "CanFireNowSub"),
    ("RimWorld.IncidentWorker_OrbitalTraderArrival", "TryExecuteWorker"),
};

Console.WriteLine("=== Harmony 补丁目标可及性 ===");
int bad = 0;
foreach (var (type, method) in patchTargets)
{
    if (!typeMap.TryGetValue(type, out var td))
    {
        Console.WriteLine($"  [缺失] {type}");
        bad++;
        continue;
    }
    bool found = false;
    foreach (var mh in td.GetMethods())
    {
        var m = md.GetMethodDefinition(mh);
        if (md.GetString(m.Name) != method) continue;
        found = true;
        var attrs = m.Attributes;
        bool isVirtual = (attrs & MethodAttributes.Virtual) != 0;
        bool isPrivate = (attrs & MethodAttributes.MemberAccessMask) == MethodAttributes.Private;
        bool isFamily = (attrs & MethodAttributes.MemberAccessMask) == MethodAttributes.Family;
        Console.WriteLine($"  {type}.{method}: virtual={isVirtual} private={isPrivate} protected={isFamily} -> " +
            (isVirtual && !isPrivate ? "可 patch" : "风险"));
        if (isPrivate) bad++;
    }
    if (!found) { Console.WriteLine($"  [缺失方法] {type}.{method}"); bad++; }
}

// 被 TraderShips 覆写的基类成员必须是 virtual
Console.WriteLine("\n=== 被覆写成员是否为 virtual ===");
var overrides = new (string Type, string Member)[]
{
    ("RimWorld.Skyfaller", "DrawAt"),
    ("RimWorld.Skyfaller", "Impact"),
    ("RimWorld.Skyfaller", "GetDrawPositionAndRotation"),
    ("Verse.Designator", "DrawStyleCategory"),
    ("Verse.Designator", "DragDrawMeasurements"),
    ("Verse.ThingComp", "CompTick"),
    ("Verse.ThingComp", "PostExposeData"),
    ("Verse.ThingComp", "PostSpawnSetup"),
    ("Verse.ThingComp", "PostDraw"),
    ("Verse.ThingComp", "CompGetGizmosExtra"),
    ("RimWorld.PassingShip", "ExposeData"),
    ("RimWorld.TradeShip", "PassingShipTick"),
    ("RimWorld.TradeShip", "Depart"),
    ("Verse.Area", "GetUniqueLoadID"),
};

foreach (var (type, member) in overrides)
{
    if (!typeMap.TryGetValue(type, out var td)) { Console.WriteLine($"  [缺失类型] {type}"); bad++; continue; }
    bool found = false, v = false;
    foreach (var mh in td.GetMethods())
    {
        var m = md.GetMethodDefinition(mh);
        if (md.GetString(m.Name) != member) continue;
        found = true;
        if ((m.Attributes & MethodAttributes.Virtual) != 0) v = true;
    }
    foreach (var ph in td.GetProperties())
    {
        var p = md.GetPropertyDefinition(ph);
        if (md.GetString(p.Name) != member) continue;
        found = true;
        var acc = p.GetAccessors();
        if (!acc.Getter.IsNil)
        {
            var g = md.GetMethodDefinition(acc.Getter);
            if ((g.Attributes & MethodAttributes.Virtual) != 0) v = true;
        }
    }
    Console.WriteLine($"  {type}.{member}: found={found} virtual={v}");
    if (!found || !v) bad++;
}

Console.WriteLine(bad == 0 ? "\n访问级别校验通过" : $"\n发现 {bad} 处风险");
