"""Show each workshop VDF field with its measured length, so the "key name too long"
assertion can be traced to the offending branch.

steamcmd parses a workshop VDF into KeyValues; every parsed token (key *and* value)
goes into the shared string pool, whose entries are capped. When the parser loses
its position it swallows the rest of the file into one token, and the assert reports
that token's length. This tool measures the nested structure so we can see which
branch a stale/odd file is actually holding.

Usage:  python tools/vdfstats.py [path]
"""
import io
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
path = Path(sys.argv[1]) if len(sys.argv) > 1 else REPO / "workshop" / "workshopitem.vdf"

raw = io.open(path, "r", encoding="utf-8-sig").read()

print(f"file      : {path}")
print(f"bytes     : {len(raw.encode('utf-8'))}")
print(f"chars     : {len(raw)}")
print(f"BOM       : {raw.startswith(chr(0xFEFF))}")
print(f"CRLF      : {chr(13) + chr(10) in raw}")
print(f"real LF   : {chr(10) in raw}")
print()

# 顶层键值：tab 缩进 + "key" + 空白 + "value"
for m in re.finditer(r'^\t"([^"]+)"\s+"((?:[^"\\]|\\.)*)"', raw, re.M):
    key, val = m.group(1), m.group(2)
    print(f"  {key:<16} escaped={len(val):>5}  unescaped={len(val.replace(chr(92) + 'n', chr(10))):>5}")

print()
# 逐字符扫描，检查引号配对与转义是否符合 VDF 规则
depth = 0
i = 0
tokens = 0
longest = 0
while i < len(raw):
    c = raw[i]
    if c == '"':
        j = i + 1
        buf = 0
        while j < len(raw):
            if raw[j] == '\\':
                j += 2
                buf += 1
                continue
            if raw[j] == '"':
                break
            # VDF 里裸换行出现在字符串内是非法的
            if raw[j] == chr(10):
                print(f"  ! 未转义的换行出现在第 {j} 字节的字符串内")
            j += 1
            buf += 1
        tokens += 1
        longest = max(longest, buf)
        i = j + 1
        continue
    if c == '{':
        depth += 1
    elif c == '}':
        depth -= 1
        if depth < 0:
            print(f"  ! 第 {i} 字节处出现多余的 }}")
    i += 1

print(f"  字符串 token 数: {tokens}  最长: {longest}  嵌套深度收尾: {depth}")
if depth != 0:
    print(f"  ! 花括号不配对，深度收尾 = {depth}")
