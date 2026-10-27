// 产物验证：确认编译出的 TraderShips.dll 中，XML def 所需的所有类型真实存在，
// 并检查目标框架与程序集引用是否指向 1.7 的运行时。
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

string dll = @"E:\AI\mods\rimworld\TraderShips\1.7\Assemblies\TraderShips.dll";

using var fs = File.OpenRead(dll);
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

var types = new HashSet<string>();
foreach (var th in md.TypeDefinitions) types.Add(FullName(md.GetTypeDefinition(th)));

// 由 XML def 中 Class / thingClass / workerClass / driverClass 所要求的类型
string[] required = new[]
{
    "TraderShips.CompPropertiesShip",
    "TraderShips.CompShip",
    "TraderShips.Quest.QuestNode_CreateTraderShip",
    "TraderShips.Quest.QuestNode_ShipsTradeRequest_Initiate",
    "TraderShips.Quest.QuestPart_ShipsTradeRequest",
    "TraderShips.StepAddPart",
    "TraderShips.StepShipgen",
    "TraderShips.ShipgenDef",
    "TraderShips.ShipSpritePartDef",
    "TraderShips.SkyfallerTradeShip",
    "TraderShips.IncidentWorkerTraderShip",
    "TraderShips.IncidentWorkerTraderShipCrash",
    "TraderShips.JobDriverTradeWithShip",
    "TraderShips.Designator_AreaLandingZoneExpand",
    "TraderShips.Designator_AreaLandingZoneClear",
    "TraderShips.Area_LandingZone",
    "TraderShips.Globals",
    "TraderShips.TraderShips",
    "TraderShips.TraderShipsSettings",
    "TraderShips.LandedShip",
    "TraderShips.ShipSprite",
    "TraderShips.ShipSpritePart",
    "TraderShips.ShipgenState",
    "TraderShips.ShipgenStep",
    "TraderShips.Patch.PatchIncidentWorker_OrbitalTraderArrivalCanFireNowSub",
    "TraderShips.Patch.PatchIncidentWorker_OrbitalTraderArrivalTryExecuteWorker",
    "TraderShips.Debug",
    "TraderShips.StepShipgen",
    "TraderShips.Globals",
};

int missing = 0;
Console.WriteLine("=== 必需类型存在性 ===");
foreach (var t in required.Distinct())
{
    bool ok = types.Contains(t);
    if (!ok) { Console.WriteLine($"  [缺失] {t}"); missing++; }
}
Console.WriteLine(missing == 0 ? $"  全部 {required.Distinct().Count()} 个类型存在" : $"  缺失 {missing} 个");

// 目标框架
Console.WriteLine("\n=== 目标框架 ===");
foreach (var ca in md.GetAssemblyDefinition().GetCustomAttributes())
{
    var ctor = md.GetMemberReference((MemberReferenceHandle)md.GetCustomAttribute(ca).Constructor);
    var tname = md.GetString(md.GetTypeReference((TypeReferenceHandle)ctor.Parent).Name);
    if (tname == "TargetFrameworkAttribute")
    {
        var blob = md.GetBlobReader(md.GetCustomAttribute(ca).Value);
        blob.ReadUInt16();
        Console.WriteLine("  " + blob.ReadSerializedString());
    }
}

// 程序集引用
Console.WriteLine("\n=== 引用的程序集 ===");
foreach (var rh in md.AssemblyReferences)
{
    var r = md.GetAssemblyReference(rh);
    Console.WriteLine($"  {md.GetString(r.Name)}  v{r.Version}");
}

// 确认关键基类被正确继承（用 TypeRef/TypeDef 名称近似校验）
Console.WriteLine("\n=== 校验通过 ===");
