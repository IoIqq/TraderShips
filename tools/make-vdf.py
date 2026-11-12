"""Generate the steamcmd workshop VDF for Trader Ships (Continued).

No description field: the VDF only carries the item metadata steamcmd needs to
update the existing workshop item. A mod description can be edited on the
workshop page itself, and leaving it out keeps the file small and pure ASCII
apart from the changenote.

Usage:
    python make-vdf.py                      # create a new workshop item
    python make-vdf.py <publishedfileid>    # update an existing item
"""
import sys
import pathlib

MOD_DIR = pathlib.Path(r"H:\steam\steamapps\common\RimWorld\Mods\TraderShipsDev")
REPO = pathlib.Path(r"E:\AI\mods\rimworld\TraderShips")
OUT = REPO / "workshop" / "workshopitem.vdf"

APPID = "294100"            # RimWorld
TITLE = "Trader Ships (Continued)"
VISIBILITY = "0"            # 0 public / 1 friends / 2 private
CHANGENOTE = ("1.8.2 - Rebuilt the mod against the current RimWorld 1.7 with no gameplay changes. "
              "Fixes the trade desk not showing up in the architect menu and a load-time error in "
              "developer mode. Should work on existing saves as before. "
              "| 1.8.2 - Recompiled against the current 1.7 build, no gameplay changes. "
              "Fixes the trade desk missing from the architect menu and a load-time error in "
              "developer mode. Save compatible as before.")

published_id = sys.argv[1] if len(sys.argv) > 1 else "0"

if not MOD_DIR.is_dir():
    raise SystemExit(f"mod folder missing: {MOD_DIR}")

preview = MOD_DIR / "About" / "preview.png"
if not preview.is_file():
    raise SystemExit(f"preview missing: {preview}")


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
print(f"  changenote      : {len(CHANGENOTE)} chars")
print(f"  non-ascii bytes : {sum(1 for b in OUT.read_bytes() if b > 127)}")
print()
print("Upload with:")
print(f'  H:\\steamcmd\\steamcmd.exe +login <user> +workshop_build_item "{OUT}" +quit')
