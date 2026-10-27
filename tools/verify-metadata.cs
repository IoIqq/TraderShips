// 用 System.Reflection.Metadata 直接读取 1.7 真实 DLL 的元数据，
// 校验 TraderShips 依赖的成员签名，避免反编译产物与真实 IL 不一致。
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

string rwDir = @"H:\steam\steamapps\common\RimWorld";
string asmPath = Path.Combine(rwDir, "Assembly-CSharp.dll");

using var fs = File.OpenRead(asmPath);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();

// 目标：类型全名 -> 需要存在的成员名列表
var expectations = new Dictionary<string, string[]>
{
    ["RimWorld.Skyfaller"] = new[] { "TimeInAnimation", "ShadowMaterial", "DrawAt", "GetDrawPositionAndRotation", "DrawDropSpotShadow", "Impact" },
    ["Verse.Designator"] = new[] { "DragDrawMeasurements", "DrawStyleCategory", "CanDesignateCell", "DesignateSingleCell", "SelectedUpdate" },
    ["RimWorld.IncidentWorker"] = new[] { "CanFireNowSub", "TryExecuteWorker" },
    ["RimWorld.IncidentWorker_OrbitalTraderArrival"] = new[] { "CanFireNowSub", "TryExecuteWorker" },
    ["Verse.Area"] = new[] { "Color", "Label", "ListPriority", "GetUniqueLoadID", "ActiveCells" },
    ["Verse.AreaManager"] = new[] { "Get" },
    ["RimWorld.TradeShip"] = new[] { "PassingShipTick", "Depart", "CanTradeNow", "WasAnnounced", "Goods", "GiveSoldThingToTrader" },
    ["RimWorld.PassingShip"] = new[] { "ticksUntilDeparture", "Departed", "name" },
    ["RimWorld.DropCellFinder"] = new[] { "FindSafeLandingSpot", "TryFindShipLandingArea", "RandomDropSpot", "TryFindDropSpotNear" },
    ["RimWorld.TradeUtility"] = new[] { "AllSellableColonyPawns" },
    ["RimWorld.Pawn_TraderTracker"] = new[] { "ColonyThingsWillingToBuy" },
    ["Verse.Graphic"] = new[] { "GetColoredVersion", "MatSingle", "drawSize" },
    ["Verse.GraphicData"] = new[] { "Graphic" },
    ["RimWorld.ShaderTypeDefOf"] = new[] { "CutoutComplex" },
    ["Verse.MeshPool"] = new[] { "plane10" },
    ["Verse.GenPlace"] = new[] { "TryPlaceThing" },
};

bool TypeHasMember(TypeDefinition td, string memberName)
{
    foreach (var mh in td.GetMethods()) { if (md.GetString(md.GetMethodDefinition(mh).Name) == memberName) return true; }
    foreach (var fh in td.GetFields()) { if (md.GetString(md.GetFieldDefinition(fh).Name) == memberName) return true; }
    foreach (var ph in td.GetProperties()) { if (md.GetString(md.GetPropertyDefinition(ph).Name) == memberName) return true; }
    return false;
}

string FullName(TypeDefinition td)
{
    string ns = md.GetString(td.Namespace);
    string name = md.GetString(td.Name);
    if (!td.GetDeclaringType().IsNil)
    {
        // nested
        var parent = md.GetTypeDefinition(td.GetDeclaringType());
        return FullName(parent) + "+" + name;
    }
    return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
}

var typeMap = new Dictionary<string, TypeDefinition>();
foreach (var th in md.TypeDefinitions)
{
    var td = md.GetTypeDefinition(th);
    var fn = FullName(td);
    if (!typeMap.ContainsKey(fn)) typeMap[fn] = td;
}

int problems = 0;
foreach (var kv in expectations)
{
    if (!typeMap.TryGetValue(kv.Key, out var td))
    {
        Console.WriteLine($"[缺失类型] {kv.Key}");
        problems++;
        continue;
    }
    foreach (var member in kv.Value)
    {
        if (!TypeHasMember(td, member))
        {
            Console.WriteLine($"[缺失成员] {kv.Key}.{member}");
            problems++;
        }
    }
}

Console.WriteLine(problems == 0
    ? "\n元数据校验通过：所有被 TraderShips 引用的类型与成员均真实存在于 1.7 Assembly-CSharp.dll"
    : $"\n共 {problems} 处问题");
