// 1.7 (Waylith) 把拖拽行为从 Designator.DraggableDimensions 挪走了。
// 本探针把相关类型的成员全打出来，直接看拖拽由什么驱动，不再靠猜。
// 注意：只反射元数据，不触发静态构造，所以不需要一整套依赖解析。
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

internal static class DesignatorProbe
{
    public static void Run(string gameDir)
    {
        string path = System.IO.Path.Combine(gameDir, "Assembly-CSharp.dll");
        using var fs = System.IO.File.OpenRead(path);
        using var pe = new PEReader(fs);
        MetadataReader md = pe.GetMetadataReader();

        string[] wanted =
        {
            "Designator", "Designator_Cells", "Designator_AreaHomeExpand",
            "Designator_AreaHomeClear", "Designator_Zone",
            "DrawStyleCategoryDef", "DrawStyleDef",
        };

        foreach (TypeDefinitionHandle th in md.TypeDefinitions)
        {
            TypeDefinition t = md.GetTypeDefinition(th);
            string name = md.GetString(t.Name);
            if (!wanted.Contains(name)) continue;

            Console.WriteLine("== " + md.GetString(t.Namespace) + "." + name
                              + "  attrs=" + t.Attributes);

            foreach (PropertyDefinitionHandle ph in t.GetProperties())
            {
                PropertyDefinition p = md.GetPropertyDefinition(ph);
                Console.WriteLine("   prop " + md.GetString(p.Name));
            }

            foreach (FieldDefinitionHandle fh in t.GetFields())
                Console.WriteLine("   field " + md.GetString(md.GetFieldDefinition(fh).Name));

            foreach (MethodDefinitionHandle mh in t.GetMethods())
            {
                MethodDefinition m = md.GetMethodDefinition(mh);
                string n = md.GetString(m.Name);
                if (n.StartsWith("get_") || n.StartsWith("set_") ||
                    n.IndexOf("Drag", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Mouse", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Console.WriteLine("   meth " + n + "  " +
                        (m.Attributes.HasFlag(MethodAttributes.Virtual) ? "virtual " : "") +
                        (m.Attributes.HasFlag(MethodAttributes.FamORAssem) ? "proto " : "") +
                        (m.Attributes.HasFlag(MethodAttributes.FamANDAssem) ? "private-proto " : "") +
                        (m.Attributes.HasFlag(MethodAttributes.Family) ? "protected " : ""));
                }
            }
            Console.WriteLine();
        }
    }
}
