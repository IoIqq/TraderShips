import io, os, re, sys

repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
os.chdir(repo)

csproj = io.open('Source/TraderShips17.csproj', encoding='utf-8').read()
refs = re.findall(r'<HintPath>(.*?)</HintPath>', csproj)

srcs = []
for root, dirs, files in os.walk('Source'):
    dirs[:] = [d for d in dirs if d not in ('obj', 'bin')]
    for f in files:
        if f.endswith('.cs'):
            srcs.append(os.path.join(root, f).replace(os.sep, '/'))
srcs.sort()

lines = [
    '/nologo',
    '/target:library',
    '/out:' + os.path.join('1.7', 'Assemblies', 'TraderShips.dll').replace(os.sep, '/'),
    '/langversion:latest',
    '/optimize+',
    '/unsafe+',
    '/nostdlib+',
    '/noconfig',
]

core = 'C:/Program Files/dotnet/shared/Microsoft.NETCore.App/10.0.7/'
for f in ['System.Runtime.dll', 'System.Private.CoreLib.dll', 'System.Collections.dll',
          'System.Linq.dll', 'System.IO.dll', 'netstandard.dll', 'System.Console.dll',
          'System.Runtime.Extensions.dll', 'System.Threading.dll', 'System.Text.Encoding.dll',
          'System.Reflection.dll', 'System.Memory.dll']:
    p = core + f
    if os.path.exists(p):
        lines.append('/reference:"' + p + '"')

for r in refs:
    p = r.replace('$(RimWorldDir)', 'H:\\steam\\steamapps\\common\\RimWorld')
    lines.append('/reference:"' + p + '"')

for s in srcs:
    lines.append('"' + s + '"')

io.open('tools/build17.rsp', 'w', encoding='utf-8').write('\n'.join(lines))
print('sources:', len(srcs))
print('refs:', len(refs))
print('rsp: tools/build17.rsp')
