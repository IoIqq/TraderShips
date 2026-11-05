import sys, re, dnfile
PATH = r"H:\steam\steamapps\common\RimWorld\Assembly-CSharp.dll"
pat = re.compile(sys.argv[1], re.I)
pe = dnfile.dnPE(PATH)
for t in pe.net.mdtables.TypeDef.rows:
    n = str(t.TypeName)
    ns = str(t.TypeNamespace)
    if pat.search(n) or pat.search(ns):
        print(ns + "." + n)
