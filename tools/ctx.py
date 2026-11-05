import io, os, sys, re

L = os.path.expandvars(r"%LOCALAPPDATA%\..\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log")
pat = sys.argv[1]
before = int(sys.argv[2]) if len(sys.argv) > 2 else 25
after = int(sys.argv[3]) if len(sys.argv) > 3 else 8
limit = int(sys.argv[4]) if len(sys.argv) > 4 else 2

lines = io.open(L, encoding='utf-8', errors='replace').read().splitlines()
idx = [i for i, l in enumerate(lines) if re.search(pat, l)]
print('hits:', idx[:10], 'total', len(idx))
for i in idx[:limit]:
    for j in range(max(0, i - before), min(len(lines), i + after)):
        print(j, lines[j][:320])
    print('-----')
