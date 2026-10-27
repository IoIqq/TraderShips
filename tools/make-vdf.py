"""Generate the steamcmd workshop VDF for Trader Ships (Continued).

Modelled on the working CrematoriumValueFilter upload, which keeps the VDF
description as a single escaped line (literal \\n sequences, not real newlines).

Usage:
    python make-vdf.py                  # create a new workshop item
    python make-vdf.py <publishedfileid>  # update an existing item
"""
import sys
import pathlib

MOD_DIR = pathlib.Path(r"H:\steam\steamapps\common\RimWorld\Mods\TraderShipsDev")
REPO = pathlib.Path(r"E:\AI\mods\rimworld\TraderShips")
OUT = REPO / "workshop" / "workshopitem.vdf"
DESC_SRC = REPO / "workshop-description.txt"

APPID = "294100"            # RimWorld
TITLE = "Trader Ships (Continued)"
VISIBILITY = "0"            # 0 public / 1 friends / 2 private
CHANGENOTE = "1.7.0 - Initial release for RimWorld 1.7 (Waylith engine)."

published_id = sys.argv[1] if len(sys.argv) > 1 else "0"

if not MOD_DIR.is_dir():
    raise SystemExit(f"mod folder missing: {MOD_DIR}")

preview = MOD_DIR / "About" / "preview.png"
if not preview.is_file():
    raise SystemExit(f"preview missing: {preview}")

raw = DESC_SRC.read_text(encoding="utf-8-sig")

# steamcmd VDF strings must be one physical line: newlines become the two
# characters backslash + n, and embedded double quotes get escaped.
flat = raw.replace("\r\n", "\n").replace('"', '\\"').replace("\n", "\\n").strip()


def q(value: str) -> str:
    """Escape a value for use inside a VDF quoted string."""
    return value.replace("\\", "\\\\").replace('"', '\\"')


vdf = f'''"workshopitem"
{{
\t"appid"\t\t\t"{APPID}"
\t"publishedfileid"\t"{published_id}"
\t"contentfolder"\t\t"{MOD_DIR}"
\t"previewfile"\t\t"{preview}"
\t"visibility"\t\t"{VISIBILITY}"
\t"title"\t\t\t"{q(TITLE)}"
\t"description"\t\t"{flat}"
\t"changenote"\t\t"{q(CHANGENOTE)}"
}}
'''

OUT.parent.mkdir(parents=True, exist_ok=True)
# No BOM: Steam's VDF parser rejects one at the start of the file.
OUT.write_text(vdf, encoding="utf-8", newline="\n")

print(f"wrote {OUT}")
print(f"  publishedfileid : {published_id}")
print(f"  contentfolder   : {MOD_DIR}")
print(f"  previewfile     : {preview}")
print(f"  description     : {len(raw)} chars source -> {len(flat)} chars escaped")
print()
print("Upload with:")
print(f'  H:\\steamcmd\\steamcmd.exe +login <user> +workshop_build_item "{OUT}" +quit')
