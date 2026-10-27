"""Sanity-check the generated workshop VDF before handing it to steamcmd.

Verifies that the description is a single physical line, that quotes and
backslashes are escaped exactly once, and that the escaped form round-trips
back to the original text.
"""
import pathlib

VDF = pathlib.Path(r"E:\AI\mods\TraderShips\workshop\workshopitem.vdf")
SRC = pathlib.Path(r"E:\AI\mods\TraderShips\workshop-description.txt")

lines = VDF.read_text(encoding="utf-8").splitlines()
print(f"total lines: {len(lines)}")

desc_line = None
for ln in lines:
    if ln.startswith('\t"description"'):
        desc_line = ln
        break

if desc_line is None:
    raise SystemExit("FAIL: description line not found")

# Strip the leading key and the single trailing quote that closes the value.
prefix = '\t"description"\t\t"'
assert desc_line.startswith(prefix), f"unexpected prefix: {desc_line[:40]!r}"
assert desc_line.endswith('"'), "description line does not end with a quote"
value = desc_line[len(prefix):-1]

print(f"description line length : {len(desc_line)}")
print(f"escaped value length    : {len(value)}  (Steam limit 8000)")
print(f"raw newlines in value   : {value.count(chr(10))}  (must be 0)")
print(f"literal backslash-n     : {value.count(chr(92) + 'n')}")

bare_quotes = 0
escaped_quotes = 0
i = 0
while i < len(value):
    ch = value[i]
    if ch == chr(92):
        nxt = value[i + 1] if i + 1 < len(value) else ""
        if nxt == '"':
            escaped_quotes += 1
        i += 2
        continue
    if ch == '"':
        bare_quotes += 1
    i += 1

print(f"escaped quotes          : {escaped_quotes}")
print(f"bare quotes             : {bare_quotes}  (must be 0)")

restored = value.replace(chr(92) + 'n', chr(10)).replace(chr(92) + '"', '"')
original = SRC.read_text(encoding="utf-8-sig").strip()
match = restored.strip() == original
print(f"round-trip match        : {match}")

if not match:
    # Show the first divergence to make fixes obvious.
    a, b = restored.strip(), original
    for idx in range(min(len(a), len(b))):
        if a[idx] != b[idx]:
            lo = max(0, idx - 40)
            print(f"first difference at {idx}:")
            print(f"  restored: ...{a[lo:idx + 40]!r}")
            print(f"  original: ...{b[lo:idx + 40]!r}")
            break
    else:
        print(f"  length differs: restored={len(a)} original={len(b)}")

print()
print("--- last 130 chars restored ---")
print(restored[-130:])
