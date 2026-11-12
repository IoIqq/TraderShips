"""CI 使用的 VDF 生成：**同时**产出 UTF-8 与纯 ASCII 两版，便于 A/B 排查。

steamcmd 的 KeyValues 解析器报了：
    src\\vstdlib\\keyvaluessystem.cpp (395) : Assertion Failed:
    CKeyValuesSystem::AddStringToPool: key name too long (1954 chars)

两种解释：
  A) steamcmd 把整段文件（含中文描述）当成单个未结束的字符串 token；
  B) 它把中文按单字节读，字符串池容量被撑爆。

两版都生成，上传时先试 ascii 那版；确认问题来源后再决定长期方案。

用法：python tools/make-vdf-ascii.py <publishedfileid>
"""
import io
import pathlib
import re
import sys

REPO = pathlib.Path(__file__).resolve().parent.parent
DESC = REPO / "workshop-description.txt"
OUT_DIR = REPO / "workshop"
TITLE = "Trader Ships (Continued)"

pub = sys.argv[1] if len(sys.argv) > 1 else "0"


def strip_tags(text: str) -> str:
    """去掉 BBCode，只留纯文本 —— 用于 ASCII 兜底版。"""
    text = re.sub(r"\[/?[a-zA-Z0-9=*][^\]]*\]", "", text)
    return text


def ascii_fold(text: str) -> str:
    """把非 ASCII 字符压成 ASCII（中文整体丢弃，保留英文段落）。"""
    out = []
    for ch in text:
        if ord(ch) < 128:
            out.append(ch)
        else:
            out.append("")
    text = "".join(out)
    text = re.sub(r"[ \t]{2,}", " ", text)
    text = re.sub(r"\n{3,}", "\n\n", text)
    return text.strip()


def esc(value: str) -> str:
    return value.replace("\\", "\\\\").replace('"', '\\"')


def flat(text: str) -> str:
    return esc(text.replace("\r\n", "\n").replace("\n", "\n"))


def build(description: str, changenote: str, out: pathlib.Path, one_line: bool):
    desc = description.replace("\r\n", "\n")
    if one_line:
        desc = desc.replace('"', '\\"').replace("\n", "\\n")
    else:
        desc = esc(desc)
    note = esc(changenote.replace("\n", " "))

    text = (
        '"workshopitem"\n'
        '{\n'
        '\t"appid"\t\t\t"294100"\n'
        f'\t"publishedfileid"\t"{pub}"\n'
        '\t"contentfolder"\t\t"H:\\steam\\steamapps\\common\\RimWorld\\Mods\\TraderShipsDev"\n'
        '\t"previewfile"\t\t"H:\\steam\\steamapps\\common\\RimWorld\\Mods\\TraderShipsDev\\About\\preview.png"\n'
        '\t"visibility"\t\t"0"\n'
        f'\t"title"\t\t\t"{esc(TITLE)}"\n'
        f'\t"description"\t\t"{desc}"\n'
        f'\t"changenote"\t\t"{note}"\n'
        '}\n'
    )
    out.write_text(text, encoding="utf-8", newline="\n")
    return out


def main():
    src = io.open(DESC, encoding="utf-8-sig").read()
    note = (
        "1.8.2 - Rebuilt the mod against the current RimWorld 1.7 with no gameplay changes. "
        "Fixes the trade desk not showing up in the architect menu and a load-time error in "
        "developer mode. Should work on existing saves as before."
    )

    full = build(src, note, OUT_DIR / "workshopitem.vdf", one_line=True)
    ascii_desc = ascii_fold(strip_tags(src))
    ascii_v = build(ascii_desc, note, OUT_DIR / "workshopitem-ascii.vdf", one_line=True)

    for p in (full, ascii_v):
        b = pathlib.Path(p).read_bytes()
        print(f"{p.name:<26} {len(b):>6} bytes  non-ascii={sum(1 for x in b if x > 127)}")


if __name__ == "__main__":
    main()
