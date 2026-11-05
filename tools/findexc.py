import io, os, re

L = os.path.expandvars(r"%LOCALAPPDATA%\..\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log")
lines = io.open(L, encoding='utf-8', errors='replace').read().splitlines()

for i, l in enumerate(lines):
    if 'StockGenerator_Slaves' in l:
        # walk backwards to the nearest non-stack line
        j = i
        while j > 0 and (lines[j].startswith('    at ') or lines[j].strip() == ''):
            j -= 1
        start = j
        while start > 0 and not re.match(r'^\d{4}-\d{2}-\d{2}', lines[start]):
            start -= 1
        for k in range(start, min(len(lines), i + 3)):
            print(k, lines[k][:400])
        print('=====')
        break
