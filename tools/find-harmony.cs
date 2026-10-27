// 找出哪个 0Harmony.dll 真正含有 HarmonyLib.Harmony / HarmonyPatch 等类型，
// 以及它的程序集版本号（编译引用必须指向有这些类型的那个）。
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

var candidates = new[]
{
    @"H:\steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll",
    @"H:\steam\steamapps\workshop\content\294100\2009463077\1.6\Assemblies\0Harmony.dll",
    @"H:\steam\steamapps\workshop\content\294100\2013476665\1.7\Assemblies\0Harmony.dll",
    @"H:\steam\steamapps\workshop\content\294100\2013476665\1.6\Assemblies\0Harmony.dll",
};

string FullName(MetadataReader md, TypeDefinition td)
{
    string ns = md.GetString(td.Namespace);
    string name = md.GetString(td.Name);
    return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
}

foreach (var path in candidates)
{
    if (!File.Exists(path)) { Console.WriteLine($"(missing) {path}"); continue; }

    using var fs = File.OpenRead(path);
    using var pe = new PEReader(fs);
    var md = pe.GetMetadataReader();
    var asm = md.GetAssemblyDefinition();

    var names = new HashSet<string>();
    foreach (var th in md.TypeDefinitions) names.Add(FullName(md, md.GetTypeDefinition(th)));

    bool hasHarmony = names.Contains("HarmonyLib.Harmony");
    bool hasPatch = names.Contains("HarmonyLib.HarmonyPatch");
    bool hasPatchAll = names.Contains("HarmonyLib.Harmony");
    bool hasTraverse = names.Contains("HarmonyLib.Traverse");

    Console.WriteLine($"--- {path}");
    Console.WriteLine($"    assembly: {md.GetString(asm.Name)} v{asm.Version}");
    Console.WriteLine($"    HarmonyLib.Harmony={hasHarmony}  HarmonyPatch={hasPatch}  Traverse={hasTraverse}");
    Console.WriteLine($"    type count: {names.Count}");
}
