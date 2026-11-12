// 复现 Waylith（RimWorld 1.7）加载 mod DLL 时的字段类型解析错误：
//
//   Could not load type of field 'TraderShips.Area_LandingZone+<LandingLocations>d__9:<indices>5__1' (4)
//   due to: Expected reference type but got type kind 17
//
// type kind 17 = ELEMENT_TYPE_VALUETYPE。用 System.Reflection.Metadata 全量解码可以证明
// 某个 DLL 的字段签名"本身"是否自洽 —— 全部解得出，就说明问题在加载器环境
// （程序集重定向、FieldInjection、宽松绑定），不在 mod 源码。
//
// 用法：
//   ldprobe <dll> [...]                 签名审计 + AssemblyRef
//   ldprobe --types <dll> [...]         命名空间 / 顶层类型清单
//   ldprobe --designator <gameDir>      打印 1.7 Designator 相关类型的成员（判断拖拽开关）
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help")
        {
            Console.WriteLine("usage: ldprobe <dll> [...]          签名审计");
            Console.WriteLine("       ldprobe --types <dll> [...]  命名空间/类型");
            Console.WriteLine("       ldprobe --designator <gameDir> 1.7 Designator 成员");
            return 2;
        }

        if (args[0] == "--designator")
        {
            DesignatorProbe.Run(args.Length > 1 ? args[1] : @"H:\steam\steamapps\common\RimWorld");
            return 0;
        }

        if (args[0] == "--types")
        {
            Lister.Run(args.Skip(1), true);
            return 0;
        }

        Lister.Run(args, false);
        Console.WriteLine();
        foreach (string path in args)
        {
            Console.WriteLine(new string('=', 78));
            Console.WriteLine(path);
            SignatureAudit(path);
        }
        return 0;
    }

    private static void SignatureAudit(string path)
    {
        using var fs = File.OpenRead(path);
        using var pe = new PEReader(fs);
        MetadataReader md = pe.GetMetadataReader();

        int fields = 0, badFields = 0, methods = 0, badMethods = 0;
        var firstBad = new List<string>();

        foreach (TypeDefinitionHandle th in md.TypeDefinitions)
        {
            TypeDefinition t = md.GetTypeDefinition(th);
            string tname = Qualified(md, t);

            foreach (FieldDefinitionHandle fh in t.GetFields())
            {
                fields++;
                try
                {
                    _ = md.GetFieldDefinition(fh).DecodeSignature(new EchoProvider(md), null);
                }
                catch (Exception e)
                {
                    badFields++;
                    if (firstBad.Count < 12)
                        firstBad.Add($"    字段 {tname}.{md.GetString(md.GetFieldDefinition(fh).Name)} -> {e.GetType().Name}: {e.Message}");
                }
            }

            foreach (MethodDefinitionHandle mh in t.GetMethods())
            {
                methods++;
                try
                {
                    _ = md.GetMethodDefinition(mh).DecodeSignature(new EchoProvider(md), null);
                }
                catch (Exception e)
                {
                    badMethods++;
                    if (firstBad.Count < 12)
                        firstBad.Add($"    方法 {tname}::{md.GetString(md.GetMethodDefinition(mh).Name)} -> {e.GetType().Name}: {e.Message}");
                }
            }
        }

        foreach (string line in firstBad)
            Console.WriteLine(line);

        Console.WriteLine("  字段签名解码失败 {0}/{1}，方法签名解码失败 {2}/{3}", badFields, fields, badMethods, methods);
        Console.WriteLine("  => " + (badFields == 0 && badMethods == 0
            ? "元数据自洽：问题不在签名写法，在加载器环境"
            : "元数据不自洽：签名本身读不出来，需要换编译目标"));
    }

    private static string Qualified(MetadataReader md, TypeDefinition t)
    {
        string ns = md.GetString(t.Namespace);
        string name = md.GetString(t.Name);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    private sealed class EchoProvider : ISignatureTypeProvider<string, object>
    {
        private readonly MetadataReader md;

        public EchoProvider(MetadataReader md)
        {
            this.md = md;
        }

        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> args)
            => genericType + "<" + string.Join(",", args) + ">";
        public string GetGenericMethodParameter(object ctx, int index) => "!!" + index;
        public string GetGenericTypeParameter(object ctx, int index) => "!" + index;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPinnedType(string elementType) => elementType;
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => reader.GetString(reader.GetTypeDefinition(handle).Name);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            TypeReference tr = reader.GetTypeReference(handle);
            string ns = reader.GetString(tr.Namespace);
            return (ns.Length == 0 ? "" : ns + ".") + reader.GetString(tr.Name);
        }

        public string GetTypeFromSpecification(MetadataReader reader, object ctx, TypeSpecificationHandle handle, byte rawTypeKind)
            => reader.GetTypeSpecification(handle).DecodeSignature(this, ctx);
    }
}
