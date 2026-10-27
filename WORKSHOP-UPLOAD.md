# Steam 创意工坊上传清单

## 一、上传方式

用 **RimWorld 自带的 Mod Uploader**（最省事，不会漏文件）：

```
H:\steam\steamapps\common\RimWorld\ModUpdater.exe
```

启动后它会列出 `Mods` 目录下的模组，选中 `TraderShipsDev` 即可。

或者用第三方工具 **RimSort / RimPy**，打包成 `.zip` 再手动上传。

> 注意：`ModUpdater.exe` 上传前会读取 `About/PublishedFileId.txt`。
> 这个文件我已经删掉了，所以会**新建一个工坊条目**并自动写入新 ID。
> 如果文件还在，它会尝试更新那个旧条目（原版 2046222331）。

## 二、上传前必须确认的三件事

### 1. `About` 目录内容

```
About/
├── About.xml          ← 部署时由 About-1.7.xml 复制而来
├── preview.png        ← 工坊缩略图（当前是原版美术，建议换成自己的）
└── (无 PublishedFileId.txt  ← 正确，首次上传后自动生成)
```

### 2. 检查部署目录确实是干净的最新版

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File E:\AI\mods\TraderShips\tools\deploy-1.7.ps1
```

脚本每次会先清空目标目录。**不要**手工复制，`Copy-Item -Recurse` 到已存在目录会产生
`1.7/1.7/` 这种嵌套结构（这个坑已经踩过一次）。

### 3. 确认 `tools/` 不会被上传

`ModUpdater.exe` 只上传 `Mods\TraderShipsDev`，而 `tools/` 在源仓库 `E:\AI\mods\TraderShips\tools`，
不在部署目录里。不需要额外处理。

## 三、About.xml 关键字段

| 字段 | 值 | 说明 |
| --- | --- | --- |
| `name` | `Trader ships (continued)` | 工坊显示名 |
| `packageId` | `IoIqq.traderships.continued` | 与原版 `automatic.traderships` 区分开，可并存 |
| `author` | `AUTOMATIC1111, IoIqq` | 保留原作者 |
| `supportedVersions` | `1.7` | |
| `modVersion` | `1.7.0` | |
| `modDependencies` | `brrainz.harmony` | 游戏内模组列表会显示为缺失依赖 |
| `loadAfter` | `brrainz.harmony`, Royalty, Odyssey | 确保补丁在 Harmony 之后应用 |

## 四、工坊简介

直接用 `workshop-description.txt` 的内容，Steam 的 BBCode 格式：

- `[h1]` `[h2]` 标题
- `[b]` 加粗
- `[list][*]` 列表
- `[url]` 链接
- `[hr][/hr]` 分隔线
- `[i]` 斜体

中英双语，英文在前。

## 五、上传时填写的元数据

**标题**（Steam 标题栏）：

```
Trader Ships (Continued)
```

**标签 / Tags**：

```
Gameplay
Animals, Plants & Environment  ← 不要，没有
```

实际建议勾选：

- `Gameplay`
- `1.7`
- `QoL`（可选，因为它替代轨道商船流程）

**缩略图**：`About/preview.png`，建议 512×512 以上，1:1 或 16:9。

## 六、上传后

1. 工坊页面生成后，把新的 `PublishedFileId.txt` 提回源仓库的 `About/`
   （部署脚本会一并复制，注意它现在不复制这个文件——需要的话我改脚本）
2. 工坊页面的 `Change Notes` 首次写：

```
Initial release for RimWorld 1.7.

Continuation of AUTOMATIC1111's Trader ships, ported to the Waylith engine.
- Rebuilt for .NET 10 / Waylith
- Harmony is now an optional dependency
- Rendering and designator APIs updated for 1.7
```

## 七、许可与授权注意

原版 Trader ships 的许可情况需要你自己确认。如果原仓库是 MIT/GPL，在 About 或工坊简介中
保留原作者署名即可；如果是**未声明许可**（默认保留所有权利），严格来说需要原作者同意才能
重新发布——这一点建议你先去原仓库确认，或者直接联系 AUTOMATIC1111。

工坊简介里我已经写了：

> If you are the original author and would prefer this not exist, contact me and I will take it down.
