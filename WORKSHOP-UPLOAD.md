# Steam 创意工坊上传步骤

## 前置状态

- steamcmd：`H:\steamcmd\steamcmd.exe`（已存在，**尚未登录**）
- 部署目录：`H:\steam\steamapps\common\RimWorld\Mods\TraderShipsDev`（已就绪，93 个文件）
- VDF：`E:\AI\mods\rimworld\TraderShips\workshop\workshopitem.vdf`（已生成并校验）

## 一、一条命令搞定（推荐）

**双击 `E:\AI\mods\rimworld\TraderShips\upload.bat`** —— 会提示输入 Steam 账号名，密码由 steamcmd 自己询问。

或在命令行里带参数：

```cmd
E:\AI\mods\rimworld\TraderShips\upload.bat IoIqq
```

脚本依次做：

```
[1/7] 从 workshop-description.txt 重新生成 VDF
[2/7] 跑 28 项预检，不通过就停
[3/7] 检查 steamcmd
[4/7] 检查 VDF
[5/7] 检查 DLL 唯一性 + 拦截 GUID 缓存
[6/7] 显示新建还是更新
[7/7] 上传
```

**上传后不只看退出码**——`steamcmd` 的退出码不可靠（指南 4.7 节）。脚本会去日志里找
`Upload finished for workshop item <ID> : OK`，找到才算成功，并把 ID **自动写入两个位置**，不用手动拷贝。

> 为什么是 `.bat` 但放在仓库根目录：`cmd.exe` 解析含中文的路径会崩（把 `setlocal` 拆成
> `bledelayedexpansion`）。放在全英文路径的根目录才稳。同理，脚本里 `cd /d "%~dp0"` 保证
> 双击时工作目录正确——否则会在 `C:\WINDOWS\system32` 下运行。

## 二、手动方式

**登录**（登录态缓存在 `H:\steamcmd\config\config.vdf`；密码不会被保存）

```bat
H:\steamcmd\steamcmd.exe +login 你的账号
```

**上传**

```bat
H:\steamcmd\steamcmd.exe +login 你的账号 "+workshop_build_item E:\AI\mods\rimworld\TraderShips\workshop\workshopitem.vdf" +quit
```

> 按 steamcmd 官方约定，带参数的命令**连同 `+` 一起加引号**，路径含空格时才不会断。

## 三、后续更新

**首次上传**：VDF 里 `publishedfileid` 必须是 `0`（现在就是）。上传成功后 Steam 分配新 ID。

**更新已有条目**：把 ID 写进 VDF，再重跑上传。

```bat
python E:\AI\mods\rimworld\TraderShips\tools\make-vdf.py 3820000000
```

> ⚠️ `steamcmd` **不会自动生成 `PublishedFileId.txt`**，只有该文件已存在时才复用其中的 ID。
> 首次上传后必须手动创建，否则下次上传会**再新建一个条目**，工坊出现重复模组。

## 四、上传后必做

`upload.bat` 会**自动完成这一步**——成功时从日志提取 ID 并写入两个位置。

如果用了手动命令，或脚本提示 ID 无法自动读取，从日志里取：

```powershell
Select-String -Path 'H:\steamcmd\logs\workshop_log.txt' -Pattern 'Upload finished for workshop item (\d+)' | Select-Object -Last 1
```

然后**两个位置都要写**：

```powershell
'3820000000' | Out-File -Encoding ascii -NoNewline "H:\steam\steamapps\common\RimWorld\Mods\TraderShipsDev\About\PublishedFileId.txt"
'3820000000' | Out-File -Encoding ascii -NoNewline "E:\AI\mods\rimworld\TraderShips\About\PublishedFileId.txt"
```

> `deploy-1.7.ps1` 的行为是「源仓库没有 `PublishedFileId.txt` 就删掉目标里的」。
> 所以**源仓库那份必须存在**，否则每次部署都会把游戏目录里的清掉。

拿到 ID 后告诉我，我会补上 GitHub README 的工坊徽章和 About.xml 里的工坊链接。

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

改代码后（**先完全退出游戏**，否则 `CS2012` 写不进 DLL）：

```bash
cd E:\AI\mods\rimworld\TraderShips\Source
DOTNET_CLI_HOME=/e/AI/tools/dotnet-home TMP=/e/AI/tools/tmp TEMP='E:\AI\tools\tmp' \
  "/e/AI/tools/dotnet10/dotnet.exe" build TraderShips17.csproj -c Release
```

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File E:\AI\mods\rimworld\TraderShips\tools\deploy-1.7.ps1
```

然后重新上传（**必须带 publishedfileid**，否则会再建一个条目）。

记得同步改 `About/About-1.7.xml` 的 `<modVersion>`、VDF 的 `changenote`，以及 `tools/make-vdf.py` 里的 `CHANGENOTE` 常量。

## 七、与原发布指南的差异

`Mods\README_RimWorld模组发布指南.md` 里部分环境信息已过时，以本节为准：

| 项目 | 指南写的 | 实际 |
| --- | --- | --- |
| 游戏版本 | 1.7.4985 rev1269 | **1.7.4990 rev1069** |
| .NET 运行时 | 10.0.7 | **10.0.13-waylithclr.dev** |
| .NET SDK | 仅 9.0.306，无 net10 | **已装 10.0.403**（`E:\AI\tools\dotnet10`） |
| steamcmd | `H:\RimCrow\tools\steamcmd\` | **`H:\steamcmd\`**（两处都有副本，内容相同） |
| 编译方式 | 绕过 MSBuild 直接调 csc.dll | **直接用 net10 SDK**，`TraderShips17.csproj` 正常构建 |

指南第 4.4 节「GUID 缓存 DLL」和第 4.5 节「PublishedFileId.txt 不会自动生成」这两条**仍然有效且非常关键**，已纳入脚本检查。
