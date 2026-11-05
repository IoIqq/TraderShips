import io, re, sys

log = sys.argv[1]
pat = sys.argv[2] if len(sys.argv) > 2 else r'TraderShips'
maxlen = int(sys.argv[3]) if len(sys.argv) > 3 else 900

count = 0
with io.open(log, encoding='utf-8', errors='replace') as f:
    for line in f:
        if re.search(pat, line):
            count += 1
            print(line.rstrip()[:maxlen])
print('--- matched', count)
