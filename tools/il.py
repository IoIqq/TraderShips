import sys, dnfile
from dncil.cil.body import CilMethodBody
from dncil.cil.body.reader import CilMethodBodyReaderBase

PATH = r"H:\steam\steamapps\common\RimWorld\Assembly-CSharp.dll"
TYPE = sys.argv[1]
METHOD = sys.argv[2] if len(sys.argv) > 2 else None
NESTED = len(sys.argv) > 3


class Reader(CilMethodBodyReaderBase):
    def __init__(self, pe, rva):
        self.pe = pe
        self.offset = self.pe.get_offset_from_rva(rva)

    def read(self, n):
        data = self.pe.get_data(self.pe.get_rva_from_offset(self.offset), n)
        self.offset += n
        return data

    def tell(self):
        return self.offset

    def seek(self, offset):
        self.offset = offset


pe = dnfile.dnPE(PATH)
mdt = pe.net.mdtables

def find_type(name, nested):
    for t in mdt.TypeDef.rows:
        if str(t.TypeName) == name:
            ns = str(t.TypeNamespace)
            if nested and '+' not in ns:
                continue
            return t
    return None

def find_type_any(name):
    hits = []
    for t in mdt.TypeDef.rows:
        if str(t.TypeName) == name:
            hits.append(t)
    return hits

def methods(t):
    return [m.row for m in t.MethodList]

def tokval(op):
    if hasattr(op, 'value'):
        return op.value
    if isinstance(op, int):
        return op
    if isinstance(op, tuple) and len(op) == 3:
        return op[2]
    return None

def resolve(tok):
    if tok is None:
        return ''
    table = (tok >> 24) & 0xFF
    rid = tok & 0xFFFFFF
    try:
        if table == 0x0A:
            r = mdt.MemberRef.rows[rid - 1]
            c = r.Class.row
            cn = str(c.TypeName) if hasattr(c, 'TypeName') else str(c)
            return cn + "::" + str(r.Name)
        if table == 0x06:
            return "MethodDef " + str(mdt.MethodDef.rows[rid - 1].Name)
        if table == 0x04:
            return "Field " + str(mdt.Field.rows[rid - 1].Name)
        if table == 0x01:
            return "TypeRef " + str(mdt.TypeRef.rows[rid - 1].TypeName)
        if table == 0x02:
            return "TypeDef " + str(mdt.TypeDef.rows[rid - 1].TypeName)
        if table == 0x2B:
            return repr(mdt.MethodSpec.rows[rid - 1])
    except Exception as e:
        return "tok %08x (%s)" % (tok, e)
    return "tok %08x" % tok

def dump_type(t, indent=""):
    print(indent + "TYPE " + str(t.TypeNamespace) + " " + str(t.TypeName))
    for f in t.FieldList:
        print(indent + "  FIELD " + str(f.row.Name))
    for m in methods(t):
        print(indent + "  METHOD " + str(m.Name))
    for t2 in mdt.NestedClass.rows:
        if t2.EnclosingClass.row is t:
            dump_type(t2.NestedClass.row, indent + "    ")

BASE = TYPE.split('+')[0]
cands = find_type_any(BASE)
if not cands:
    print("not found")
    sys.exit(1)

target = None
for t in cands:
    if '+' not in str(t.TypeNamespace):
        target = t
        break
if target is None:
    target = cands[0]

# allow Type+<Nested> selection: "StockGenerator_Slaves+<GenerateThings>d__2"
if '+' in TYPE:
    outer, inner = TYPE.split('+', 1)
    for t in mdt.TypeDef.rows:
        if str(t.TypeName) == inner:
            target = t
            break

if NESTED:
    dump_type(target)
    sys.exit(0)

def dump_body(t, m):
    print("\n=== " + str(t.TypeName) + "::" + str(m.Name) + " RVA " + str(m.Rva) + " ===")
    body = CilMethodBody(Reader(pe, m.Rva))
    for insn in body.instructions:
        op = insn.opcode
        operand = insn.operand
        tv = tokval(operand)
        shown = operand
        if tv is not None and isinstance(tv, int):
            shown = resolve(tv)
        print("  %04x %-14s %s" % (insn.offset, op.name, shown))

found = False
for m in methods(target):
    if METHOD is None or str(m.Name) == METHOD:
        dump_body(target, m)
        found = True
if not found and not NESTED:
    print("method not found; use 3rd arg to list")