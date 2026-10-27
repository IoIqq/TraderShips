// 确认编译产物不再引用 0Harmony，且不含 [HarmonyPatch] 特性——
// 只要还有引用，Harmony 缺失时 CLR 仍会在枚举类型时抛 TypeLoadException。
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

string dll = @"E:\AI\mods\rimworld\TraderShips\1.7\Assemblies\TraderShips.dll";
using var fs = File.OpenRead(dll);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();

Console.WriteLine("=== AssemblyRefs ===");
bool refsHarmony = false;
foreach (var rh in md.AssemblyReferences)
{
    var r = md.GetAssemblyReference(rh);
    string n = md.GetString(r.Name);
    Console.WriteLine($"  {n} v{r.Version}");
    if (n == "0Harmony") refsHarmony = true;
}
Console.WriteLine(refsHarmony ? "  >>> 仍引用 0Harmony（不合格）" : "  >>> 无 0Harmony 引用（合格）");

// 检查 TypeRef: 不能出现 HarmonyLib.* 类型
bool hasHarmonyLibTypeRef = false;
foreach (var h in md.TypeReferences)
{
    var t = md.GetTypeReference(h);
    string ns = md.GetString(t.Namespace);
    if (ns.StartsWith("HarmonyLib"))
    {
        Console.WriteLine($"  TypeRef: {ns}.{md.GetString(t.Name)}");
        hasHarmonyLibTypeRef = true;
    }
}

// 检查自定义特性: 不应再有 HarmonyPatch / HarmonyPatchAll 等
Console.WriteLine("\n=== CustomAttributes 中的 Harmony 特性 ===");
bool hasHarmonyAttr = false;
foreach (var ch in md.CustomAttributes)
{
    var ca = md.GetCustomAttribute(ch);
    EntityHandle ctor = ca.Constructor;
    StringHandle nameHandle = default;
    if (ctor.Kind == HandleKind.MemberReference)
    {
        var mr = md.GetMemberReference((MemberReferenceHandle)ctor);
        if (mr.Parent.Kind == HandleKind.TypeReference)
        {
            var tr = md.GetTypeReference((TypeReferenceHandle)mr.Parent);
            if (md.GetString(tr.Namespace).StartsWith("HarmonyLib"))
            {
                Console.WriteLine($"  attr from {md.GetString(tr.Namespace)}.{md.GetString(tr.Name)}");
                hasHarmonyAttr = true;
            }
        }
    }
}
if (!hasHarmonyAttr) Console.WriteLine("  (none)");

Console.WriteLine("\n=== Result ===");
Console.WriteLine((!refsHarmony && !hasHarmonyLibTypeRef && !hasHarmonyAttr)
    ? "  PASS: Harmony 完全解耦，缺失时不会崩溃"
    : "  FAIL: 仍有 Harmony 硬依赖");
