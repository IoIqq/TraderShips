#!/usr/bin/env python
"""在 Steam 创意工坊搜索 RimWorld 模组。

本机 hosts 被 Steam++ 改写，所有 steam* 域名指向 127.0.0.1，
由 Steam++ 的加速器（监听 443）转发到真实服务器 —— 所以这里用 curl -k
直连本地 443，绕过 DNS 污染与证书链问题。

用法:
    python tools/workshop.py 关键词 [关键词2 ...]     搜索
    python tools/workshop.py --id 2478833213          查看条目详情
"""
import re
import subprocess
import sys
import html as htmlmod

# Windows 控制台默认 GBK，中文标题会变成乱码。
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

APPID = "294100"
BASE = "https://steamcommunity.com"


def fetch(url, timeout=30):
    """走本地 Steam++ 加速器取页面。"""
    out = subprocess.run(
        ["curl", "-sk", "--max-time", str(timeout), url],
        capture_output=True,
    )
    return out.stdout.decode("utf-8", "ignore")


def search(term, limit=15):
    from urllib.parse import quote
    url = (f"{BASE}/workshop/browse/?appid={APPID}"
           f"&searchtext={quote(term)}&browsesort=textsearch&section=readytouseitems")
    page = fetch(url)
    if not page:
        print("取不到页面（Steam++ 加速器是否在运行？）")
        return

    # 结果条目：<a href="...filedetails/?id=NNN" class="...">标题</a>
    pattern = re.compile(
        r'filedetails/\?id=(\d+)"[^>]*class="[^"]*"[^>]*>([^<]{3,120})</a>')
    seen, rows = set(), []
    for iid, title in pattern.findall(page):
        if iid in seen:
            continue
        seen.add(iid)
        rows.append((iid, htmlmod.unescape(title).strip()))

    if not rows:
        print(f"没有结果：{term}")
        return

    print(f"=== {term} （{len(rows)} 条）")
    for i, (iid, title) in enumerate(rows[:limit], 1):
        print(f"{i:2}. {title}")
        print(f"    https://steamcommunity.com/sharedfiles/filedetails/?id={iid}")


def detail(iid):
    """查看单个条目。

    注意：本机的 Steam++ 加速器会把详情页 HTML 里的字母 s 吞掉
    （class= 会变成 cla =），所以这里只做宽松匹配，取不到就留空，
    不做保证 —— 搜索功能不受影响。
    """
    page = fetch(f"{BASE}/sharedfiles/filedetails/?id={iid}")
    if not page:
        print("取不到页面（Steam++ 加速器是否在运行？）")
        return

    def grab(pat):
        m = re.search(pat, page, re.S | re.I)
        if not m:
            return ""
        return htmlmod.unescape(re.sub("<[^>]+>", "", m.group(1))).strip()

    # class 可能被改成 cla、cla =、cla= 等形态，用 [^>]* 兜住
    title = grab(r'workshopItemTitle[^>]*>(.*?)</div>')
    author = grab(r'friendBlockContent[^>]*>(.*?)<br')
    size = grab(r'File Size</div>\s*<div[^>]*>(.*?)</div>')

    print("标题:", title or "(未解析到)")
    print("作者:", author or "(未解析到)")
    print("大小:", size or "(未解析到)")
    print("链接: https://steamcommunity.com/sharedfiles/filedetails/?id=" + iid)


def main():
    args = sys.argv[1:]
    if not args:
        print(__doc__)
        return
    if args[0] == "--id":
        detail(args[1])
        return
    for term in args:
        search(term)
        print()


if __name__ == "__main__":
    main()