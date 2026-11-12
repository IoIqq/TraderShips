// DLL 里的命名空间/类型清单 —— 核对 1.7/Patches 与 1.7/Defs 里写的类名是否真的存在。
// 这些类名一旦对不上，XML 不会在补丁阶段报错，而是在实例化设计器/建筑时抛
// TypeLoadException，外层再包成 AggregateException（"post-long-event action"）。
//
// 用法： dotnet run -c Release -- <assembly.dll> [--types]
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

internal static class Lister
{
    public static void Run(IEnumerable<string> paths, bool types)
    {
        foreach (string path in paths)
        {
            using var fs = File.OpenRead(path);
            using var pe = new PEReader(fs);
            MetadataReader md = pe.GetMetadataReader();

            Console.WriteLine("=== " + path);
            foreach (AssemblyReferenceHandle h in md.AssemblyReferences)
            {
                AssemblyReference ar = md.GetAssemblyReference(h);
                Console.WriteLine("  ref " + md.GetString(ar.Name) + " " + ar.Version);
            }

            List<TypeDefinition> tops = md.TypeDefinitions
                .Select(md.GetTypeDefinition)
                .Where(t => t.GetDeclaringType().IsNil)
                .ToList();

            var spaces = tops.Select(t => md.GetString(t.Namespace))
                             .Where(s => s.Length > 0)
                             .Distinct()
                             .OrderBy(s => s);
            Console.WriteLine("  命名空间: " + string.Join(" | ", spaces));

            if (!types) continue;
            foreach (TypeDefinition t in tops)
            {
                string n = md.GetString(t.Namespace);
                Console.WriteLine("  " + (n.Length == 0 ? "" : n + ".") + md.GetString(t.Name));
            }
        }
    }
}
