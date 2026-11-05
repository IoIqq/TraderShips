# 项目规范 / AGENTS.md

> 适用于本仓库的**每一次代码修改**，包括人类贡献者和 AI 助手。
> 建档：2026-11-05　对应版本 1.8.0

---

## 0. 一句话版本

**改完 → 子代理审核 → 修完审核意见 → 编译部署 → 更新 README 和 TODO → 提交并推送 GitHub。**

没有走完这条链的改动，一律视为**未完成**，不得上传创意工坊。

---

## 1. 强制流程

```text
 ① 改代码
    ↓
 ② 子代理审核（必须，见第 2 节）
    ↓
 ③ 处理审核意见（修 / 或记为误报并写进 TODO 第七节）
    ↓
 ④ 编译 + 部署 + 预检（见第 3 节）
    ↓
 ⑤ 更新 README 和 TODO（见第 4 节）
    ↓
 ⑥ git commit + push（见第 5 节）
    ↓
 ⑦ 需要发版时：上传创意工坊（见第 6 节）
```

**②和⑤不可跳过。** 哪怕只改了一行注释，也要走一遍审核和文档同步——因为"只改一行"恰恰是最容易翻车的地方。

---

## 2. 子代理审核

### 2.1 什么时候必须审

| 改动类型 | 审核要求 |
| --- | --- |
| 修改 `Source/` 下任何 `.cs` | **必须** |
| 修改 `1.7/Defs/` 下任何 XML | **必须** |
| 修改 `Languages/` 下任何键 | 自行核对键数与占位符即可（预检会查） |
| 只改 README / TODO / 注释 | 不需要 |

### 2.2 必须开**两个**子代理

单开一个会漏——实测证明，功能组会漏性能问题，性能组会漏资金漏洞。

**A 组：功能正确性**

```
你是 RimWorld 1.7 模组代码审查员。仓库：E:\AI\mods\rimworld\TraderShips。

任务：只审查功能正确性 / 边界情况 / 存档安全，不要改代码，输出可执行的问题清单。

本次改动：<在这里写清楚改了什么、为什么改>

重点核对：
1. 空引用 / 边界（null 检查、Map 为 null、对象已销毁）
2. 玩家花钱的原子性（扣款与发货之间的失败路径、回滚、实扣校验）
3. 存档安全（该序列化的字段、跨存档引用、列表是否无限增长）
4. 派系状态副作用（重复设关系、已消灭派系、好感下限截断）
5. 生成器正确性（HandlesThingDef 与实际库存是否一致、重复 Add）
6. 设置项生效时机（立即生效 vs 必须重启）

输出格式：P0（报错/数据丢失/存档损坏）/ P1（不符合预期）/ P2（体验）三段。
每条不超过 4 行，必须带 文件:行号 和具体修法。直接给清单，不要总结。
```

**B 组：UI 与性能**

```
你是 RimWorld 1.7 模组代码审查员。仓库：E:\AI\mods\rimworld\TraderShips。

任务：只审查 UI 与性能，不要改代码，输出可执行的问题清单。

本次改动：<同上>

重点核对：
1. IMGUI 重绘路径（DoWindowContents / Draw* 里有没有每帧做重活：LINQ、new 容器、
   Translate、字符串拼接、遍历 def）
2. 每帧 vs 每 tick（PostDraw / TransformLabel / CompInspectStringExtra）
3. 启动期（[StaticConstructorOnStartup] 里的全表扫描、对象数量、内存）
4. 贴图（RenderTexture 有没有 Release、临时 Texture2D 有没有 Destroy、是否重复创建）
5. 存档体积

输出格式同上：P0/P1/P2，每条不超过 4 行，带 文件:行号 和具体修法。直接给清单。
```

### 2.3 审核结果怎么处理

1. **逐条核实**，不要照单全收。子代理会编造不存在的 API——例如它曾断言
   `TraderKindDef.canTrade` 默认 `true` 会抛异常，而这个字段**根本不存在**。
2. 核实方法：`grep` 源码、用 `tools/il.py` 反编译 `Assembly-CSharp.dll` 确认 API。
3. 确认为误报的，写进 **TODO 第七节**，并注明"不要再修"，避免下次又被翻出来。
4. P0 必须修；P1 修或明确记录决策；P2 可记入待办。

---

## 3. 编译 / 部署 / 预检

```powershell
# 0. 先确认游戏没在跑，否则 DLL 被锁 → CS2012
Get-Process RimWorld -ErrorAction SilentlyContinue

# 1. 编译（不需要 .NET 10 SDK，走 Roslyn）
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build.ps1

# 2. 部署
powershell -NoProfile -ExecutionPolicy Bypass -File tools/deploy-1.7.ps1

# 3. 预检（28 项，任一失败就不要继续）
python tools/preflight.py
```

验收标准：

- 编译输出 `0 warnings 0 errors`（唯一的 `CS2023 /noconfig` 是脚本自带，可忽略）
- 部署 `File count: 171`
- 预检 `0 failures`

**不要用 MD5 判断代码是否一致**——`build17.rsp` 没开 `/deterministic`，每次编译 MVID 都不同，体积恒为 121856。要比对就比源码。

---

## 4. 更新 README 和 TODO

### README

改了什么功能，README 对应章节就要改。至少检查：

- [ ] 功能表（船型、货位、玩法）
- [ ] 设置表（新增/改名/改默认值，以及"生效时机"列）
- [ ] 代码结构树（新增/删除文件）
- [ ] 已知限制（新增限制，或已修的限制要删掉）
- [ ] 版本号

### TODO

- [ ] 修好的条目从"待验证"移到"已验证"——**但只有用户实机测过的才能移**
- [ ] 新增条目写清：问题、文件:行号、修法
- [ ] 误报写进第七节
- [ ] 涉及取舍的写进"待决策"

> 关键原则：**编译通过 ≠ 已验证**。AI 不能启动游戏测试，所有运行时行为必须由用户确认。
> TODO 里"已验证"和"已修复但未复测"是两回事，不要混。

---

## 5. 提交并推送 GitHub

```powershell
git add -A
git status                       # 确认没有误加文件
git commit -m "<type>: <一句话>"
git push origin master
```

### commit message 规范

```text
<type>: <改了什么>

<可选：为什么改，1-3 行>
```

| type | 用途 |
| --- | --- |
| `feat` | 新功能 |
| `fix` | 修 bug |
| `perf` | 性能优化 |
| `refactor` | 重构，行为不变 |
| `docs` | 只改文档 |
| `chore` | 构建脚本、工具 |

### 提交前检查

- [ ] `git status` 里没有 `obj/`、`bin/`、`_test-results/`、`_autotest/`
- [ ] 没有把 `GUID_*.dll` 或 `*.deps.json` 提交进去
- [ ] `1.7/Assemblies/*.dll` 已被 `.gitignore` 排除，**不要强行加入**——创意工坊读的是部署目录 `H:\\steam\\steamapps\\common\\RimWorld\\Mods\\TraderShipsDev`，仓库里不需要构建产物

---

## 6. 上传创意工坊（仅在发版时）

```cmd
upload.bat
```

- 账号 `rb3481141298`，密码由 steamcmd 自己询问，**脚本和仓库里绝不留密码**
- 脚本会自动从 `PublishedFileId.txt` 读 ID，避免新建重复条目
- 成功判据是日志里**本次运行新增的** `Upload finished for workshop item 3828162134 : OK`
- 传完记得更新 `About.xml` 的 `<modVersion>` 和 VDF 的 `changenote`

---

## 7. 禁止事项

1. **不要索取或保存 Steam 密码**，脚本只传账号名。
2. **不要说"我测试过了"**——AI 不能启动游戏。所有运行时验证由用户完成。
3. **不要凭印象断言 API 存在**。用 `grep` 或 `tools/il.py` 反编译确认。
4. **不要照抄子代理的结论**。它们会编造字段名、行号和行为。
5. **不要用字符串替换改 .cs**。中文字符和缩进极易失配，产生重复类或括号错位；
   用行号定位 + 小范围替换。
6. **不要在 `tools/` 放与标准库同名的 .py**。曾经有个 `types.py` 遮蔽了
   标准库 `types`，导致 `make-vdf.py` 和 `preflight.py` 全部 import 崩溃。
7. **不要跳过预检**。28 项里任何一项失败都意味着工坊页面会出问题。
8. **不要提交 `_test-results/` 和 `_autotest/`**，那是本地测试产物。

---

## 8. 快捷清单（复制粘贴用）

```text
[ ] 改代码
[ ] 子代理 A（功能）跑完，逐条核实
[ ] 子代理 B（性能）跑完，逐条核实
[ ] 审核意见已修 / 已记为误报
[ ] tools/build.ps1 → 0 error
[ ] tools/deploy-1.7.ps1 → 171 files
[ ] tools/preflight.py → 0 failures
[ ] README 已更新（功能/设置/结构/限制/版本）
[ ] TODO 已更新（已验证 / 待复测 / 误报 / 待决策）
[ ] git add -A && git status 无脏文件
[ ] git commit -m "<type>: ..."
[ ] git push origin master
[ ] （发版时）upload.bat + 确认日志 OK + 更新版本号
```