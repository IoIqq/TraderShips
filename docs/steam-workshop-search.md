# Steam 创意工坊搜索方法（RimWorld）

> 记录时间：2026-11-07
> 适用：本机（Windows，装有 Steam++ / Watt Toolkit 加速器）

---

## 一、为什么不能直接用浏览器/爬虫

本机的网络环境有三层干扰，直接请求 Steam 会全部失败：

### 1. hosts 文件被改写

`C:\Windows\System32\drivers\etc\hosts` 里有 31 条 Steam 域名，全部指向本地：

```
# Steam++ Start
127.0.0.1 steamcommunity.com
127.0.0.1 www.steamcommunity.com
127.0.0.1 api.steampowered.com
127.0.0.1 store.steampowered.com
127.0.0.1 store.akamai.steamstatic.com
127.0.0.1 cdn.akamai.steamstatic.com
... 共 31 条
# Steam++ End
```

### 2. 公共 DNS 被污染

改用公共 DNS 查询也会拿到错误结果：

```
$ nslookup steamcommunity.com 8.8.8.8
Addresses:  2001::9df0:d08
            173.252.105.21        ← Facebook 的 IP，不是 Steam
```

### 3. 加速器在本地做中间人

```
$ netstat -ano | grep ":443 "
TCP    0.0.0.0:443    0.0.0.0:0    LISTENING    39084

$ tasklist | grep -i steam++
Steam++.exe                  38516
Steam++.Accelerator.exe      39084    ← 就是它占着 443
```

Steam++ 把 `steamcommunity.com:443` 的流量接到本机 443 端口，再转发到真实服务器。

---

## 二、正确做法：直连本地 443

既然域名已经解析到 `127.0.0.1`，而加速器在那里监听着，那么**直接请求就行**，只是要跳过证书校验（加速器用的是自签证书）。

```bash
curl -sk --max-time 25 "https://steamcommunity.com/workshop/browse/?appid=294100&searchtext=关键词"
```

- `-s` 静默
- `-k` 跳过证书校验（**必须**，否则 SSL 错误）
- 快速验证：`curl -sk https://steamcommunity.com/ | head -c 200` 应该返回 `<title>Steam Community</title>`

---

## 三、搜索 URL 参数

```
https://steamcommunity.com/workshop/browse/?appid=294100
    &searchtext=<关键词，需 URL 编码>
    &browsesort=textsearch
    &section=readytouseitems
```

| 参数 | 值 | 说明 |
|---|---|---|
| `appid` | `294100` | RimWorld |
| `searchtext` | 关键词 | 中英文都可以，需 `quote()` 编码 |
| `browsesort` | `textsearch` | 按相关度排序；其他值见下 |
| `section` | `readytouseitems` | 只要可用的物品 |

`browsesort` 其他可选值：

| 值 | 含义 |
|---|---|
| `textsearch` | 相关度 |
| `trend` | 趋势 |
| `toprated` | 最高评分 |
| `mostrecent` | 最新 |
| `mostsubscribed` | 订阅最多 |

分页：加 `&p=2`。

---

## 四、解析结果

新版工坊页面每个结果是一条 `<a>`，格式：

```html
<a href="https://steamcommunity.com/sharedfiles/filedetails/?id=2478833213"
   class="SxMdVrGe9aY- ..." style="--cursor:pointer">RPG Style Inventory Revamped</a>
```

所以用这条正则就能同时拿到 **ID 和标题**：

```python
import re, html

pattern = re.compile(
    r'filedetails/\?id=(\d+)"[^>]*class="[^"]*"[^>]*>([^<]{3,120})</a>')
for iid, title in pattern.findall(page):
    print(iid, html.unescape(title).strip())
```

**注意**：类名是 Steam 每次构建随机生成的（`SxMdVrGe9aY-` 这种），**不要硬编码类名**，用 `class="[^"]*"` 匹配即可。

---

## 五、坑：详情页 HTML 会被改坏

`sharedfiles/filedetails/?id=XXX` 这个页面经过加速器后，**字母 `s` 会被吞掉**：

| 原文 | 实际拿到 |
|---|---|
| `class="detailsStats"` | `cla ="detail Stat "` |
| `Posted` | `Po ted` |
| `sidebar` | ` idebar` |
| `sharedfiles` | ` haredfile ` |

后果：搜索页解析正常，但详情页的**订阅数 / 收藏数 / 标签 / 文件大小全都抓不到**。

试过但不可行的替代方案：

| 方案 | 结果 |
|---|---|
| `ISteamRemoteGetPublishedFileDetails` GET | 404 |
| `ISteamRemoteGetPublishedFileDetails` POST | 404（加速器不转发该路径） |
| `store.steampowered.com/api/appdetails` | 可用，但只覆盖游戏，不覆盖工坊条目 |

**结论**：详情页只能宽松解析标题和作者，其余字段不要依赖。

---

## 六、现成脚本

仓库里的 `tools/workshop.py` 已经封装好：

```bash
python tools/workshop.py "贸易台"
python tools/workshop.py "trade ship" "奴隶船"
python tools/workshop.py --id 3828162134
```

核心逻辑（可独立复制使用）：

```python
import re, subprocess, html as htmlmod
from urllib.parse import quote

APPID = "294100"
BASE = "https://steamcommunity.com"


def fetch(url, timeout=30):
    """走本地 Steam++ 加速器取页面。"""
    out = subprocess.run(
        ["curl", "-sk", "--max-time", str(timeout), url],
        capture_output=True,
    )
    return out.stdout.decode("utf-8", "ignore")


def search(term):
    url = (f"{BASE}/workshop/browse/?appid={APPID}"
           f"&searchtext={quote(term)}&browsesort=textsearch"
           f"&section=readytouseitems")
    page = fetch(url)

    pattern = re.compile(
        r'filedetails/\?id=(\d+)"[^>]*class="[^"]*"[^>]*>([^<]{3,120})</a>')

    seen = set()
    for iid, title in pattern.findall(page):
        if iid in seen:
            continue
        seen.add(iid)
        print(htmlmod.unescape(title).strip(),
              f"https://steamcommunity.com/sharedfiles/filedetails/?id={iid}")
```

---

## 七、换一台机器的注意事项

上面所有做法都依赖 **Steam++ 加速器**。如果交给别人在**没有装 Steam++** 的机器上跑：

1. hosts 里没有劫持 → 域名正常解析
2. 但 `steamcommunity.com` 在部分地区无法直连

此时的改法：

- **能直连**：去掉 `-k`，直接 `curl "https://steamcommunity.com/..."`
- **需要代理**：`curl -x http://127.0.0.1:PORT "https://steamcommunity.com/..."`
- **不需要改代码**：脚本里的 `fetch()` 是唯一需要动的地方，把 `-sk` 换成对应参数即可

---

## 八、快速自检

交给别人之前，让他先跑这三条确认环境：

```bash
# 1. 能不能拿到页面
curl -sk --max-time 20 https://steamcommunity.com/ | head -c 100

# 2. 加速器是否在跑
netstat -ano | grep ":443 "

# 3. 搜索结果是否可解析
curl -sk "https://steamcommunity.com/workshop/browse/?appid=294100&searchtext=trade" | grep -o 'filedetails/?id=[0-9]*' | head -3
```

三条都正常，脚本就能用。

---

## 九、常见报错对照

| 现象 | 原因 | 处理 |
|---|---|---|
| `curl: (60) SSL certificate problem` | 没加 `-k` | 加 `-k` |
| `curl: (7) Failed to connect` | 加速器没启动 | 打开 Steam++ 并开启加速 |
| 返回 `404 Not Found` | 走的是 API 路径，加速器不转发 | 改用 `workshop/browse` 页面 |
| 返回 `<title>Steam Community</title>` 但搜索无结果 | `searchtext` 没 URL 编码 | 用 `quote()` |
| 中文显示成乱码 | Windows 控制台 GBK | `sys.stdout.reconfigure(encoding="utf-8")` |
| 详情页字段全是空 | HTML 被改坏（字母 s 被吞） | 已知限制，只取标题和作者 |
