"""Full pre-flight check for the Trader Ships workshop upload.

Runs the checklist from the local RimWorld publishing guide (section 9) so we
do not discover a problem after the item is already public.

Exit code 0 means everything passed.
"""
import pathlib
import re
import struct
import sys
import xml.etree.ElementTree as ET

MOD = pathlib.Path(r"H:\steam\steamapps\common\RimWorld\Mods\TraderShipsDev")
REPO = pathlib.Path(r"E:\AI\mods\TraderShips")

ok = []
warn = []
fail = []


def check(condition, good, bad, hard=True):
    if condition:
        ok.append(good)
    else:
        (fail if hard else warn).append(bad)


# ---------------------------------------------------------------- About.xml
about = MOD / "About" / "About.xml"
if not about.is_file():
    fail.append(f"About.xml missing: {about}")
    root = None
else:
    try:
        root = ET.parse(about).getroot()
        ok.append(f"About.xml parses ({about})")
    except ET.ParseError as e:
        fail.append(f"About.xml invalid XML: {e}")
        root = None

if root is not None:
    for field in ("name", "author", "packageId", "modVersion", "description"):
        node = root.find(field)
        val = (node.text or "").strip() if node is not None else ""
        check(bool(val), f"About.xml <{field}> = {val[:60]!r}", f"About.xml <{field}> empty or missing")

    versions = [(li.text or "").strip() for li in root.findall("supportedVersions/li")]
    check("1.7" in versions, f"supportedVersions includes 1.7 ({versions})", f"supportedVersions lacks 1.7: {versions}")

    deps = [(li.findtext("packageId") or "").strip() for li in root.findall("modDependencies/li")]
    check("brrainz.harmony" in deps, "modDependencies includes brrainz.harmony", f"modDependencies missing harmony: {deps}")

    after = [(li.text or "").strip() for li in root.findall("loadAfter/li")]
    check("brrainz.harmony" in after, "loadAfter includes brrainz.harmony", f"loadAfter missing harmony: {after}")

    desc = (root.findtext("description") or "")
    # Steam workshop copy must not leak into the in-game description field.
    check("[b]" not in desc and "[h1]" not in desc,
          "About.xml description is plain text (no BBCode)",
          "About.xml description contains BBCode, which the mod list shows literally",
          hard=False)

# ---------------------------------------------------------------- assemblies
asm_dir = MOD / "1.7" / "Assemblies"
dlls = sorted(asm_dir.glob("*.dll")) if asm_dir.is_dir() else []
check(len(dlls) == 1, f"exactly one DLL: {[d.name for d in dlls]}", f"expected 1 DLL, found {len(dlls)}: {[d.name for d in dlls]}")

guid = [d.name for d in dlls if d.name.startswith("GUID_")]
check(not guid, "no GUID_*.dll runtime cache copies", f"GUID cache DLLs present: {guid}")

deps_json = list(asm_dir.glob("*.deps.json")) if asm_dir.is_dir() else []
check(not deps_json, "no .deps.json leftovers", f".deps.json files present: {[d.name for d in deps_json]}")

# ---------------------------------------------------------------- LoadFolders
lf = MOD / "LoadFolders.xml"
if lf.is_file():
    txt = lf.read_text(encoding="utf-8-sig")
    check("<v1.7>" in txt, "LoadFolders.xml declares <v1.7>", "LoadFolders.xml lacks <v1.7>")
else:
    fail.append("LoadFolders.xml missing (mod uses 1.7/ subfolder layout)")

# ---------------------------------------------------------------- languages
lang_root = MOD / "Languages"
en_file = lang_root / "English" / "Keyed" / "TraderShips.xml"


def load_keys(path):
    return {c.tag: (c.text or "") for c in ET.parse(path).getroot()}


if en_file.is_file():
    en = load_keys(en_file)
    ok.append(f"English keys: {len(en)}")
    for lang in ("ChineseSimplified", "ChineseTraditional"):
        f = lang_root / lang / "Keyed" / "TraderShips.xml"
        if not f.is_file():
            fail.append(f"{lang} translation missing")
            continue
        tr = load_keys(f)
        missing = [k for k in en if k not in tr]
        extra = [k for k in tr if k not in en]
        check(not missing and not extra,
              f"{lang}: {len(tr)} keys, matches English",
              f"{lang}: missing={missing[:4]} extra={extra[:4]}")
        # Placeholders must match or Translate() throws at runtime.
        bad_ph = []
        for k in en:
            if k in tr:
                a = sorted(re.findall(r"\{[^}]*\}", en[k]))
                b = sorted(re.findall(r"\{[^}]*\}", tr[k]))
                if a != b:
                    bad_ph.append(k)
        check(not bad_ph, f"{lang}: placeholders match", f"{lang}: placeholder mismatch in {bad_ph[:4]}")
else:
    fail.append(f"English language file missing: {en_file}")

# ---------------------------------------------------------------- images
preview = MOD / "About" / "preview.png"
if preview.is_file():
    head = preview.read_bytes()[:24]
    is_png = head[:8] == b"\x89PNG\r\n\x1a\n"
    check(is_png, "preview.png is a real PNG", "preview.png is not actually a PNG (Steam rejects it)")
    if is_png:
        w, h = struct.unpack(">II", head[16:24])
        pct = w / h
        check(abs(pct - 16 / 9) < 0.05,
              f"preview.png {w}x{h}, 16:9 (Steam showcase ratio)",
              f"preview.png {w}x{h} is not 16:9; Steam will crop it",
              hard=False)
else:
    fail.append("About/preview.png missing - workshop page would have no image")

# ---------------------------------------------------------------- PublishedFileId
pfid = MOD / "About" / "PublishedFileId.txt"
if pfid.is_file():
    val = pfid.read_text(encoding="utf-8-sig").strip()
    if val == "2046222331":
        fail.append("PublishedFileId.txt contains AUTOMATIC1111's original ID - this would OVERWRITE his page")
    else:
        ok.append(f"PublishedFileId.txt present: {val} (will UPDATE this item)")
else:
    warn.append("PublishedFileId.txt absent - upload will CREATE A NEW item (intended for first release)")

# ---------------------------------------------------------------- VDF
vdf = REPO / "workshop" / "workshopitem.vdf"
if vdf.is_file():
    lines = vdf.read_text(encoding="utf-8").splitlines()
    desc_line = next((l for l in lines if l.startswith('\t"description"')), None)
    if desc_line is None:
        fail.append("VDF has no description line")
    else:
        prefix = '\t"description"\t\t"'
        value = desc_line[len(prefix):-1]
        check(desc_line.endswith('"'), "VDF description line is terminated", "VDF description line not terminated")
        check("\n" not in value, "VDF description is a single line", "VDF description contains raw newlines")
        check(len(value) < 8000, f"VDF description {len(value)} chars < 8000", f"VDF description too long: {len(value)}")
        restored = value.replace("\\n", "\n").replace('\\"', '"')
        original = (REPO / "workshop-description.txt").read_text(encoding="utf-8-sig").strip()
        check(restored.strip() == original, "VDF description round-trips to the source file",
              "VDF description does not match workshop-description.txt - regenerate it")

    content = next((l for l in lines if l.startswith('\t"contentfolder"')), "")
    check(str(MOD) in content, f"VDF contentfolder points at {MOD}", f"VDF contentfolder wrong: {content.strip()}")

    pv = next((l for l in lines if l.startswith('\t"previewfile"')), "")
    check(str(preview) in pv, "VDF previewfile points at preview.png", f"VDF previewfile wrong: {pv.strip()}")

    pub = next((l for l in lines if l.startswith('\t"publishedfileid"')), "")
    check('"0"' in pub, 'VDF publishedfileid is "0" (creates a new item)',
          f"VDF publishedfileid is not 0: {pub.strip()}", hard=False)
else:
    raise SystemExit(f"VDF missing: {vdf}\nRun: python {REPO / 'tools' / 'make-vdf.py'}")

# ---------------------------------------------------------------- report
print()
for line in ok:
    print(f"  [OK]   {line}")
for line in warn:
    print(f"  [WARN] {line}")
for line in fail:
    print(f"  [FAIL] {line}")

print()
print(f"{len(ok)} passed, {len(warn)} warnings, {len(fail)} failures")
sys.exit(1 if fail else 0)
