import io, re, sys, collections

log = sys.argv[1]
themes = collections.Counter()
per_faction = collections.defaultdict(set)
rows = []

with io.open(log, encoding='utf-8', errors='replace') as f:
    for line in f:
        m = re.search(r'\[TraderShips\] (\w+) / (\w+) \((\d+) defs\): (.*)', line)
        if not m:
            continue
        fac, theme, n, rest = m.group(1), m.group(2), int(m.group(3)), m.group(4)
        per_faction[fac].add(theme)
        themes[theme] += 1
        cats = collections.Counter()
        for tok in rest.split(', '):
            c = re.search(r'\[(\w*)\]$', tok)
            cats[c.group(1) if c else 'NONE'] += 1
        rows.append((fac, theme, n, cats))

print('=== theme totals ===')
for t, c in themes.most_common():
    print('%-12s %d' % (t, c))
print()
print('=== factions: %d ===' % len(per_faction))
sizes = collections.Counter(len(v) for v in per_faction.values())
for k in sorted(sizes):
    print('  %d themes: %d factions' % (k, sizes[k]))
print()
print('=== spot check: top-3 cat in each theme (first faction per theme) ===')
seen = set()
for fac, theme, n, cats in rows:
    if theme in seen:
        continue
    seen.add(theme)
    print('%-10s %-16s %s' % (theme, fac, cats.most_common(4)))
