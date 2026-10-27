# Steam 创意工坊上传步骤

## 前置状态

- steamcmd：`H:\steamcmd\steamcmd.exe`（已存在，**尚未登录**）
- 部署目录：`H:\steam\steamapps\common\RimWorld\Mods\TraderShipsDev`（已就绪，93 个文件）
- VDF：`E:\AI\mods\TraderShips\workshop\workshopitem.vdf`（已生成并校验）

## 一、登录 steamcmd

```bat
H:\steamcmd\steamcmd.exe +login <你的Steam账号>
```

进去后按提示输入密码，有 Steam Guard 会要求验证码。登录成功后再执行 `quit`。

登录态保存在 `H:\steamcmd\config\config.vdf`，之后不用重复登录。

## 二、上传

**首次上传**（创建新条目，VDF 里 `publishedfileid` 必须是 `0`）：

```bat
H:\steamcmd\steamcmd.exe +login <账号> +workshop_build_item "E:\AI\mods\TraderShips\workshop\workshopitem.vdf" +quit
```

成功后会打印新条目的 `PublishedFileId`。

**后续更新**（改已有条目）：

1. 把 ID 写进 VDF：

```bat
python E:\AI\mods\TraderShips\tools\make-vdf.py 3820000000
```

2. 重跑同一条上传命令

## 三、上传后要做的事

拿到 PublishedFileId 后，把它告诉我，或者自己执行：

```bat
echo 3820000000 > "H:\steam\steamapps\common\RimWorld\Mods\TraderShipsDev\About\PublishedFileId.txt"
```

同时把源仓库里也建一份（下次部署会带上）：

```bat
echo 3820000000 > "E:\AI\mods\TraderShips\About\PublishedFileId.txt"
```

> 注意：`deploy-1.7.ps1` 已改成「源仓库没有 PublishedFileId.txt 就删掉目标里的」。
> 所以源仓库那份必须存在，否则每次部署都会把游戏目录里的清掉。

## 四、检查清单

上传前已确认：

- [x] `About/About.xml`：名称、作者、packageId、版本、依赖、loadAfter
- [x] `About/preview.png`：你提供的封面图（533×300）
- [x] `1.7/Assemblies/TraderShips.dll`：只有一个 DLL，无 `.deps.json` 残留
- [x] `Languages/`：英 / 简中 / 繁中 / 西
- [x] `LoadFolders.xml`：`<v1.7>` 正确
- [x] `PublishedFileId.txt` **已删除**（否则会覆盖原作者 AUTOMATIC1111 的页面）
- [x] VDF 转义校验通过（裸引号 0，往返一致）

## 五、常见失败原因

| 现象 | 原因 |
| --- | --- |
| `Invalid workshop item` | `publishedfileid` 写了别人的 ID |
| `Fail to build workshop item` | `contentfolder` 路径不存在或含特殊字符 |
| 描述显示成一行乱码 | VDF 里用了真实换行，必须用字面 `\n` |
| 上传后页面没有图 | `previewfile` 路径错误，或图不是 PNG |
| 提示需要先登录 | `+login` 放在 `+workshop_build_item` 之前 |

## 六、更新流程备忘

改代码后：

```bash
cd E:\AI\mods\TraderShips\Source
dotnet build TraderShips17.csproj -c Release
```

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File E:\AI\mods\TraderShips\tools\deploy-1.7.ps1
```

然后重新上传（必须带 publishedfileid，否则会再建一个新条目）。

记得同步改 `About/About-1.7.xml` 里的 `<modVersion>` 和 VDF 里的 `changenote`。
