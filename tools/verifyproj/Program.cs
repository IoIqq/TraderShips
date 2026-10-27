// 确认 Debug.cs 中的 [DebugAction] 特性是否真的被写进了 DLL，以及参数顺序是否正确。
// 如果这里有，但游戏里看不到，说明是运行时扫描问题；如果这里就没有，说明编译/源码问题。
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

string dll = @"E:\AI\mods\TraderShips\1.7\Assemblies\TraderShips.dll";
using var fs = File.OpenRead(dll);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();

Console.WriteLine("=== 查找 DebugAction 特性 ===");

int found = 0;
foreach (var th in md.TypeDefinitions)
{
    var td = md.GetTypeDefinition(th);
    string typeName = md.GetString(td.Namespace) + "." + md.GetString(td.Name);

    foreach (var mh in td.GetMethods())
    {
        var m = md.GetMethodDefinition(mh);
        string methodName = md.GetString(m.Name);

        foreach (var cah in m.GetCustomAttributes())
        {
            var ca = md.GetCustomAttribute(cah);
            string attrName = "";

            if (ca.Constructor.Kind == HandleKind.MemberReference)
            {
                var mr = md.GetMemberReference((MemberReferenceHandle)ca.Constructor);
                if (mr.Parent.Kind == HandleKind.TypeReference)
                {
                    var tr = md.GetTypeReference((TypeReferenceHandle)mr.Parent);
                    attrName = md.GetString(tr.Namespace) + "." + md.GetString(tr.Name);
                }
            }

            if (attrName.Contains("DebugAction"))
            {
                found++;
                Console.WriteLine($"  找到: {typeName}.{methodName}  <- {attrName}");

                // 解出固定参数（category, name, ...）
                try
                {
                    var blob = md.GetBlobReader(ca.Value);
                    ushort prolog = blob.ReadUInt16();
                    Console.WriteLine($"    prolog=0x{prolog:X4}");

                    // 两个字符串固定参数
                    if (blob.RemainingBytes > 0 && blob.ReadByte() == 0xFF)
                    {
                        Console.WriteLine("    category = null");
                    }
                    else
                    {
                        blob.Offset -= 1;
                        Console.WriteLine($"    category = \"{blob.ReadSerializedString()}\"");
                    }

                    if (blob.RemainingBytes > 0 && blob.ReadByte() == 0xFF)
                    {
                        Console.WriteLine("    name = null");
                    }
                    else
                    {
                        blob.Offset -= 1;
                        Console.WriteLine($"    name = \"{blob.ReadSerializedString()}\"");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"    (无法解析参数: {e.Message})");
                }
            }
        }
    }
}

Console.WriteLine(found > 0 ? $"\n共 {found} 个 DebugAction 特性存在" : "\n>>> 没有找到任何 DebugAction 特性！");
