# -*- coding: utf-8 -*-
"""把 mod DLL 的元数据读出来，重点输出 AssemblyRef（即编译时引用的 BCL）。

排查目标报错（Waylith / RimWorld 1.7）：
    Could not load type of field 'X+<Foo>d__9:<indices>5__1' (4) due to:
    Expected reference type but got type kind 17

ECMA-335 element type 0x11(17) = ELEMENT_TYPE_VALUETYPE。字段本来装 HashSet<int> 引用，
运行时却按值类型解析 —— 这是 DLL 的元数据形态和运行时不一致的症状（源码改多少遍都不会变）。

用法：python tools/pemeta.py <dll> [...] [-v]
"""
import io
import re
import struct
import sys


def u16(b, o):
    return struct.unpack_from('<H', b, o)[0]


def u32(b, o):
    return struct.unpack_from('<I', b, o)[0]


def meta_heads(b):
    out = []
    i = 0
    while True:
        i = b.find(b'BSJB', i)
        if i < 0:
            return out
        try:
            major, minor = struct.unpack_from('<HH', b, i + 4)
            vlen = u32(b, i + 12)
            if not 0 < vlen < 64:
                i += 4
                continue
            ver = b[i + 16:i + 16 + vlen].rstrip(b'\0').decode('ascii', 'replace')
            p = i + 16 + vlen + 2
            n = u16(b, p)
            p += 2
            st = {}
            ok = True
            for _ in range(n):
                off, sz = u32(b, p), u32(b, p + 4)
                p += 8
                e = b.index(b'\0', p)
                name = b[p:e].decode('ascii', 'replace')
                p = (e + 1 + 3) & ~3
                st[name] = (off, sz)
                if off > len(b):
                    ok = False
            if ok and '#Strings' in st:
                out.append((i, ver, major, minor, st))
        except Exception:                       # noqa: BLE001
            pass
        i += 4


def dump(paths, verbose=False):
    for path in paths:
        b = io.open(path, 'rb').read()
        print('=' * 78)
        print(path)
        heads = meta_heads(b)
        if not heads:
            print('  不是托管程序集')
            continue
        for (pos, ver, major, minor, st) in heads[-1:]:
            print('  CLI 头 %d.%d  运行时串 %r' % (major, minor, ver))
            # 用 <Module> 反推 #Strings 基址（头里声明的偏移未必可靠）
            m = b.find(b'\x00<Module>\x00')
            sbase = m + 1 if m > 0 else pos + st['#Strings'][0]
            print('  #Strings 基址 %d（声明 %d，差 %+d）'
                  % (sbase, pos + st['#Strings'][0], sbase - pos - st['#Strings'][0]))

            # 找 AssemblyRef 表：Module/MVID 之后第一处形如
            # (u16 major<=30)(u16 minor)(u16 0..0xff)(u16 0)(u32 0)(blob idx)(str idx)(str idx) 的连续行
            refs = find_assemblyrefs(b, sbase)
            print('  AssemblyRef:')
            if refs:
                for r in refs:
                    print('    ' + r)
            else:
                # 表结构不是标准 24 字节对齐时退化为"出现次数"提示
                print('    （表格布局无法定位，改用字符串出现次数）')
                for pat in (b'System.Private.CoreLib', b'System.Runtime', b'System.Linq',
                            b'netstandard', b'mscorlib', b'Assembly-CSharp', b'UnityStubs'):
                    print('    %-26s x%d' % (pat.decode(), b.count(pat)))
            if verbose:
                for pat in (b'System.Runtime', b'System.Private.CoreLib', b'netstandard',
                            b'mscorlib', b'Assembly-CSharp', b'UnityStubs'):
                    print('  出现 %-24s %d 次' % (pat.decode(), b.count(pat)))


def _s(b, base, idx):
    e = b.index(b'\0', base + idx)
    return b[base + idx:e].decode('utf-8', 'replace')


def find_assemblyrefs(b, sbase):
    """暴力扫描：AssemblyRef 行 = 2+2+2+2+4 版本/标志 + blob + name + culture + hash。"""
    hits = []
    # 已知的 BCL 名字串索引
    names = {}
    for pat in (b'System.Runtime', b'System.Private.CoreLib', b'netstandard',
                b'mscorlib', b'Assembly-CSharp', b'UnityStubs'):
        i = b.find(pat + b'\x00', sbase)
        if i > 0 and not b[i - 1:i].isalnum():
            names[pat.decode()] = i - sbase

    for name, idx in sorted(names.items(), key=lambda kv: kv[1]):
        # 在 idx 之前找引用它的行：row 中 name 字段是 2/4 字节索引
        for width in (2, 4):
            pat = struct.pack('<H' if width == 2 else '<I', idx)
            start = 0
            while True:
                j = b.find(pat, start, sbase)
                if j < 0:
                    break
                start = j + 1
                row = j - 16 - 2           # 16 字节版本+flags，blob 索引 2 字节
                if row < 0:
                    continue
                maj, mi, bu, rev = struct.unpack_from('<HHHH', b, row)
                flags = u32(b, row + 8)
                if not (0 < maj <= 30 and mi <= 100 and bu <= 99999 and rev <= 99999):
                    continue
                hits.append('%s, Version=%d.%d.%d.%d  (row@%d, idx宽度%d, flags=0x%x)'
                            % (name, maj, mi, bu, rev, row, width, flags))
    # 去重
    seen, out = set(), []
    for h in hits:
        k = h.split('  (')[0]
        if k in seen:
            continue
        seen.add(k)
        out.append(h)
    return out


if __name__ == '__main__':
    dump([a for a in sys.argv[1:] if a != '-v'], verbose='-v' in sys.argv)
