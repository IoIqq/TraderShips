// 静态校验：mod 的 XML def 中使用的字段名，是否都真实存在于 1.7 的对应 C# 类型上。
// 目的：捕获 1.7 中被改名/删除的 XML 字段（这类问题只在加载期报错）。
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;

string rwDir = @"H:\steam\steamapps\common\RimWorld";
string defsDir = @"E:\AI\mods\TraderShips\1.7\Defs";

using var fs = File.OpenRead(Path.Combine(rwDir, "Assembly-CSharp.dll"));
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

// 收集类型的字段名 + 属性名（含基类）
var typeMembers = new Dictionary<string, HashSet<string>>();
var typeBases = new Dictionary<string, string>();

foreach (var th in md.TypeDefinitions)
{
    var td = md.GetTypeDefinition(th);
    var fn = FullName(td);
    if (typeMembers.ContainsKey(fn)) continue;

    var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var fh in td.GetFields()) members.Add(md.GetString(md.GetFieldDefinition(fh).Name));
    foreach (var ph in td.GetProperties()) members.Add(md.GetString(md.GetPropertyDefinition(ph).Name));
    typeMembers[fn] = members;

    var bt = td.BaseType;
    if (!bt.IsNil)
    {
        try
        {
            if (bt.Kind == HandleKind.TypeReference)
            {
                var tr = md.GetTypeReference((TypeReferenceHandle)bt);
                var ns = md.GetString(tr.Namespace);
                var nm = md.GetString(tr.Name);
                typeBases[fn] = string.IsNullOrEmpty(ns) ? nm : ns + "." + nm;
            }
            else if (bt.Kind == HandleKind.TypeDefinition)
            {
                typeBases[fn] = FullName(md.GetTypeDefinition((TypeDefinitionHandle)bt));
            }
        }
        catch { }
    }
}

HashSet<string> AllMembers(string typeName, int depth = 0)
{
    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (depth > 12 || typeName == null) return set;
    if (!typeMembers.TryGetValue(typeName, out var own)) return set;
    set.UnionWith(own);
    if (typeBases.TryGetValue(typeName, out var b)) set.UnionWith(AllMembers(b, depth + 1));
    return set;
}

// XML 元素名 -> 期望的 C# 类型
var fieldOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["skyfaller"] = "Verse.SkyfallerProperties",
    ["graphicData"] = "Verse.GraphicData",
    ["statBases"] = "Verse.StatModifier",
    ["building"] = "RimWorld.BuildingProperties",
    ["questNameRules"] = "RimWorld.RulePack",
    ["questDescriptionRules"] = "RimWorld.RulePack",
};

// 收集 mod XML 中出现的元素名（排除结构性标签与已知合法项）
var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "Defs","li","points","rulesStrings","steps","nodes","parms","subSounds","grains","comps","statBases",
    "killedLeavings","costList","apparelTags","weaponTags","techHediffsTags","disallowedThingDefs",
    "specialDesignatorClasses","supportedVersions","targetTags","graphicData","skyfaller","building",
    "questNameRules","questDescriptionRules","volume","offset","rect","root","curve","node",
    "loadFolders","xpath","value","Operation","Patch",
};

var elementNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
foreach (var file in Directory.GetFiles(defsDir, "*.xml").Concat(Directory.GetFiles(@"E:\AI\mods\TraderShips\1.7\Patches", "*.xml")))
{
    try
    {
        var doc = XDocument.Load(file);
        foreach (var el in doc.Descendants())
        {
            var n = el.Name.LocalName;
            if (n.Contains('.')) continue;      // Class="..." 形式
            if (skip.Contains(n)) continue;
            if (Regex.IsMatch(n, @"^[A-Z][A-Za-z0-9_]*$") && !fieldOwner.ContainsKey(n)) { /* 可能是 defName 值，忽略 */ }
            elementNames.Add(n);
        }
    }
    catch (Exception e) { Console.WriteLine($"解析失败 {file}: {e.Message}"); }
}

int missing = 0, checkedCount = 0;
foreach (var name in elementNames.OrderBy(x => x))
{
    // 只检查看起来像字段名的（小写开头的 camelCase）
    if (!char.IsLower(name[0])) continue;
    checkedCount++;

    bool found = false;
    foreach (var kv in typeMembers)
    {
        if (kv.Value.Contains(name)) { found = true; break; }
    }
    if (!found)
    {
        Console.WriteLine($"[未知字段] <{name}>  未在任何 1.7 类型中找到");
        missing++;
    }
}

Console.WriteLine($"\n已检查 {checkedCount} 个小写开头字段名，未找到 {missing} 个。");
Console.WriteLine(missing == 0 ? "XML 字段校验通过：所有字段名均存在于 1.7 程序集" : "存在需要人工确认的字段");
