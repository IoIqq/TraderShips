import io, os, sys

L = os.path.expandvars(r"%LOCALAPPDATA%\..\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log")
a, b = int(sys.argv[1]), int(sys.argv[2])
lines = io.open(L, encoding='utf-8', errors='replace').read().splitlines()
for j in range(max(0, a), min(len(lines), b)):
    print(j, lines[j][:340])
