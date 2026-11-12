# 项目结构速查

> 最后一次整理：2026-11-13，版本 1.8.2

## 顶层

| 路径 | 作用 |
| --- | --- |
| `About/` | `About.xml`（版本、依赖、简介）、`PublishedFileId.txt`（= 3828162134）、预览图 |
| `1.1` ~ `1.5` | 旧版本分支的原样副本，含各自的 `0Harmony.dll`，**不参与 1.7 构建** |
| `1.7/` | 当前生效内容：`Assemblies/TraderShips.dll`、`Defs/`、`Patches/` |
| `Languages/` | 英 / 简中 / 繁中 / 西 四套，各 136 个键 |
| `Sounds/`、`Textures/` | 资源，按 `LoadFolders.xml` 始终加载 |
| `LoadFolders.xml` | 版本目录映射；1.6 复用 `1.5`，1.7 用 `1.7` |
| `Source/` | 唯一源码树，`namespace TraderShips` |
| `tools/` | 构建、部署、预检、上传、反编译辅助 |
| `workshop/` | 生成物：`workshopitem.vdf` |
| `docs/` | 排查记录（见下） |

## Source/ 分节

```
Source/
├── CompShip.cs / Building_Ship.cs / LandedShip.cs      飞船实体
├── ShipSprite*.cs / ShipgenDef.cs / Step*.cs           程序化船体
├── FactionShipGenerator.cs / StockGenerator_*.cs        船型与货单生成
├── CommsShipRequest.cs / ShipPurchase.cs / ShipOrder.cs 呼叫、买船、预订
├── Building_TradeDispatch.cs / Dialog_TradeDispatch.cs  贸易台
├── Dialog_OrderGoods.cs / GameComponent_ShipOrders.cs   预订货物
├── IncidentWorker*.cs / Skyfaller*.cs                   到来、坠毁、降落动画
├── Area/                                                着陆区与两个设计器
├── Quest/                                               任务节点
└── Debug.cs / DispatchTowerResearch.cs                  启动期注入与研究同步
```

## tools/

| 脚本 | 用途 |
| --- | --- |
| `make-rsp.py` → `build17.rsp` | 生成 Roslyn 响应文件（`build17.rsp` 是生成物，已 ignore） |
| `build.ps1` | 走 `sdk/9.0.306/Roslyn/bincore/csc.dll` 编译，输出 `1.7/Assemblies/TraderShips.dll` |
| `deploy-1.7.ps1` | 从零重建 `H:\steam\steamapps\common\RimWorld\Mods\TraderShipsDev`，171 个文件 |
| `preflight.py` | 上传前 24~28 项检查（键数、贴图、VDF、缓存 DLL 等） |
| `make-vdf.py` | 生成 `workshop/workshopitem.vdf`（**不含 description**） |
| `make-vdf.py` 的兄弟脚本 | `make-vdf.py` 是唯一入口；`check-vdf.py` 只做校验 |
| `il.py` / `ildll.py` / `findexc.py` / `range.py` / `sig.py` | 反编译与签名辅助 |
| `workshop.py` | 创意工坊检索 |
| `autotest.ps1` / `collect-autotest.ps1` / `logslice.py` | 游戏内自检结果的收集与切片 |
| `verify-*.cs` / `verifyproj/` | 光靠编译过不去的约定（Harmony 免依赖、XML 字段、元数据） |
| `screenshot.ps1` / `ui-probe.ps1` / `ui-actions.ps1` / `dpi-check.ps1` | UI 验证 |

**不要**在 `tools/` 放与标准库同名的 `.py`（曾有 `types.py` 遮蔽标准库导致全部脚本 import 崩溃）。

## docs/

| 文件 | 内容 |
| --- | --- |
| `PORTING-1.7.md` | 1.6 → 1.7 移植的完整记录 |
| `2026-11-12-deploy-mismatch.md` | 玩家反馈「贸易台不见 + type kind 17」的排查结论与复现方法 |
| `2026-11-12-designator-drag.md` | 1.7 拖拽设计器的真实开关（读元数据得出） |
| `steam-workshop-search.md` | 创意工坊检索方法 |

排查期间写过的一次性脚本（PE 元数据读取、签名审计、VDF 自检）已全部删除，仓库不留。

## 本地不入库的东西

| 路径 | 原因 |
| --- | --- |
| `1.7/Assemblies/*.dll` | 构建产物，进部署目录就够；工坊读的是部署目录 |
| `tools/build17.rsp` | `make-rsp.py` 每次生成 |
| `tools/ldprobe/`（已删） | 曾用于排查，现已移除 |
| `_test-results/`、`_autotest/` | 本地测试输出 |
| `Source/obj/`、`Source/bin/` | MSBuild 中间产物 |
| `*.legacy-backup`、`*.bak` | 手工备份 |

## 发布前三件事

1. `powershell -File tools/build.ps1` → `0 errors`
2. `powershell -File tools/deploy-1.7.ps1` → `File count: 171`
3. `python tools/preflight.py` → `0 failures`

再跑 `upload.bat`，确认日志里出现**本次运行新增的** `Upload finished for workshop item 3828162134 : OK`。
