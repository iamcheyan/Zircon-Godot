# 选人界面旧 Zircon 素材瞬闪 — 根因与修复报告

日期：2026-10-04
修复 commit：`f063e47e`（已推送 origin/master）
涉及文件：`GodotClient/Controls/MirSkin.cs`、`GodotClient/Scripts/SelectScene.cs`

---

## 一、现象

选人界面（EI 复古 UI，`--legacy-ui`）偶尔在 1~3 帧内闪现**旧版 Zircon 现代人物
动画/模型素材**，随后又恢复为当前替换后的 EI 自制动画。实测 800×600 真实登录
（`test@test.com` → 选人屏）60fps 录屏，在 Warriors / Taoist / Wizard 男槽都能
复现：每轮身体动画循环都会闪一次，肉眼上"偶尔"是因为闪帧只有 120ms×N。

复现步骤（修复前）：

1. `cd /home/tetsuya/development/zircon && ./login_game.sh test`（或直接
   `godot-mono --path GodotClient -- --server 127.0.0.1 --port 7000
   --user test@test.com --pass test123 --char TestHero --legacy-ui --legacy-hud --stay-select --window`）
2. 登录成功进入选人屏，保持画面不动（洞窟两个角色在循环动画）。
3. 60fps 录屏（`ffmpeg -f x11grab -framerate 60`）→ 每个身体动画循环里都能抓到
   1~3 帧现代 ZL 角色叠影。

证据（修复前，真实登录采集，`test@test.com`，800×600，Xvfb :123）：

- `screenshots/character-select-flash/before_fix/crop_f_005.png` —— 闪帧本体：
  选人屏右侧槽位上叠加的现代 ZL 角色帧（青蓝色立绘），下方字样"登录成功！角色数：2"。
- `screenshots/character-select-flash/before_fix/crop_f_006..008.png` —— 同一次
  录屏的连续帧，闪帧只在个别帧出现，前后帧均正常（符合"短暂一帧/数帧"的描述）。
- `screenshots/character-select-flash/before_fix/zl_1560.png` —— 现代库
  `Interface1c.Zl` 第 1560 帧的原始内容（512×1024 现代角色立绘），与闪帧同源。

## 二、根因（源码级）

### 触发链

```
SelectScene.SyncLegacySlotGeometry()  /  SyncLegacyCreateFrameIndexes()
  auraIndex = 身体当前帧 + 40                     // 原版 +40 特效层
  hasAura = MirSkin.GetSize(Interface1c, auraIndex).X > 4 || .Y > 2
      ↓ GetSize 里 WIL 帧头为空 → 回退现代 ZL
  现代 ZL 在同索引上恰有另一套角色素材 → hasAura = true
  aura.Visible = true; aura.Index = auraIndex
      ↓
DXImageControl.DrawControl → MirSkin.GetTexture(Interface1c, auraIndex)
  WIL 取不到帧 → 回退 ZL → 拿到 512×1024 现代角色立绘
      ↓
Blend = true（LegacyBlendMaterial 屏幕混合）→ 现代角色帧"闪现"在 EI 人物上
```

### 数据事实（用 wilsdk/zlsdk 独立解码复核）

`LegacyEI/Data/Interface1c.wil`（EI 正本，2000 帧）与 `Data/Interface1c.Zl`
（现代库，3020 帧元数据 / 1488 帧实际图像）在**相同索引上是两套完全不同的素材**。
EI 的 30 个选角身体块（`SelectScene.LegacySlotFrameTable`，200..1954）每块的
+40 特效段里有 11 个组合的整段或部分帧在 WIL 上是"空帧"（WIX offset = 0，
EI 原版语义 = 此索引不画），而现代 ZL 恰好在同索引放了角色/特效图：

| 组合 | 身体段 | +40 泄漏帧（WIL 空、ZL 有图） | ZL 内容 |
|---|---|---|---|
| 战士男 v0 | 200-210 | 240-250 全部 | 168×368 等现代立绘 |
| 战士男 v3 | 380-387 | 420-427 全部 | 128×64 等 |
| 战士男 v4 | 440-457 | 480-497 全部 | 256×128 等（建角预览屏也中招） |
| 战士女 v0/v3/v4 | 500-510 / 680-691 / 740-755 | 540-550 / 720-731 / 780-795 | 同上 |
| 法师男 v2 | 920-930 | 960-965、970 | 256×512 现代立绘 |
| 法师男 v3 | 980-990 | 1020-1030 全部 | 256×512 等 |
| 道士男 v0 | 1400-1410 | 1440-1450 全部 | 512×512 现代立绘 |
| 道士男 v2 | 1520-1539 | 1560-1562、1570-1579 | 512×1024 现代立绘（实测闪帧帧号） |
| 道士女 v0 | 1700-1710 | 1740-1750 全部 | 256×512 等 |

身体层（+0）与阴影层（+20）在全部 30 块内逐帧核对：**没有任何一帧**是
"WIL 空 + ZL 有图"，泄漏只发生在 +40 特效层门控上。这解释了：

- 为什么只有选人/建角屏闪，而游戏内 HUD 从不闪 —— 只有这两处用 +40 层。
- 为什么"偶尔"才看到 —— 30 个组合里只有 11 个中招；且每个身体循环只闪该组合
  中招的那几帧（120ms/帧，1~3 帧）。
- 为什么最终又恢复自制动画 —— 门控每帧重算，身体循环走出泄漏帧后
  `auraSize` 变回 WIL 的 4×2 占位或 ZL 空档，`hasAura=false`，闪帧消失。

### 语义错误

`MirSkin.GetSize/GetTexture` 的双层回退（`LEGACY_ITEM_GRID_AND_TEXTURE_
ARCHITECTURE.md` 文档化的 WIL→ZL 管线）设计目标是**"索引超出 WIL 帧数或 WIL
库整体缺失时回退 ZL"**（如现代新物品在旧 Inventory.wil 里不存在）。
而"WIX offset=0 的**空帧**"在 EI 正本里是**刻意的"此索引不画"**，不是缺数据。
回退逻辑把"空帧"与"缺帧"混为一谈，导致现代 ZL 的另一套素材被误当成 EI 特效层。

## 三、修复

commit `f063e47e`，共 2 文件 +34/-4：

1. `MirSkin.cs` 新增 `LegacyWilHasFrame(LibraryFile, int)`：**只查 EI 原版 WIL**
   （WIX offset>0 且帧头 W>4、H>2），完全不经过 ZL 回退。
2. `SelectScene.cs` 两处 +40 特效层门控改用它：
   - `SyncLegacySlotGeometry`（选人屏洞窟槽）
   - `SyncLegacyCreateFrameIndexes`（建角预览槽）

行为对齐 EI 正本：WIL 空帧 = 不画特效层；WIL 有帧 = 画（法师男火球
F1080-1094、法师女地焰 F1384-1391、道士女光点 F1984-1994 的有效帧判定不变，
因为它们的帧号通过身体帧+40 计算，那些索引在 WIL 上本来就有图）。

### 边界与不影响面

- 文档化的 Inventory/ProgUse 等**索引超界**回退不受影响（`LegacyWilHasFrame`
  只在两个特效门控点使用，`GetTexture/GetSize` 的通用回退路径原样保留）。
- Inventory 实测 WIL 范围内有 383 个"WIL 空 + ZL 有图"索引（其中 106 个是
  >40×40 的现代物品图），因此**不能**全局禁掉"空帧回退"，修复必须限定在
  +40 门控点 —— 这正是本次做法。
- 身体/阴影/命中框/名称标签等其它层不动。
- 不改 `Client/`（原版）目录。

### 调查代码清理

调查期临时加入的 `MirSkin.AuditLegacyFallback` 钩子
（`ZIRCON_LEGACY_FALLBACK_AUDIT=1` 时打日志）已随修复 commit 一并移除，
仓库内不再残留诊断代码。

## 四、验证

### 1. 编译

`dotnet build GodotClient/ZirconClient.csproj` → 已成功生成，0 错误
（6 条 CS8632/CS0219 为既有警告，与本次改动无关）。

### 2. 修复后离线全组合回放（不连服，`--legacy-slot-preview`）

用确定性合成角色逐个驱动 6 个"修复前会闪"的职业/性别组合，每个录 12~20 秒
60fps（800×600，Xvfb :124），抽帧共 **544 帧逐帧像素扫描**：

- 槽位区域（逻辑 250,190-450,420）无任何"亮块/青色块"（现代 ZL 立绘特征）
  —— suspect = 0。
- 平均槽区 RGB ≈ (50,35,19)，为 F50 洞窟背景 + EI 人物的正常色调。
- 每个组合录屏与抽帧目录：`/tmp/flash_probe/verify_offline/{wizard,warrior,taoist}_{m,f2}/`
  （capture.mp4 + client.log + f/*.png）。

修复后样帧（已入库）：

- `screenshots/character-select-flash/after_fix/wizard_m_f0040.png`
- `screenshots/character-select-flash/after_fix/wizard_f_f0040.png`
- `screenshots/character-select-flash/after_fix/warrior_m_f0040.png`
- `screenshots/character-select-flash/after_fix/warrior_f_f0040.png`
- `screenshots/character-select-flash/after_fix/taoist_m_f0040.png`
- `screenshots/character-select-flash/after_fix/taoist_f_f0040.png`

对比图（左=修复前真实登录闪帧，右=修复后同视口稳定帧）：

- `screenshots/character-select-flash/compare/wizard_m_before_after.png`
- `screenshots/character-select-flash/compare/warrior_m_before_after.png`
- `screenshots/character-select-flash/compare/taoist_m_before_after.png`

### 3. 真实登录路径

- 修复前证据 `before_fix/crop_f_005..008` 来自**真实登录**
  （`test@test.com`，127.0.0.1:7000，800×600，Xvfb :123，60fps），非模拟 UI。
- 修复后尝试同路径回归时，`test@test.com` 已被两个并行客户端占用
  （显示 :101 与 :126，进程 103535/104041，非本任务启动）；按 goal"测试账号占用
  → 记录 blocker"的约定未抢占。真实登录的修复后验证由离线回放
  （同一 SelectScene 场景树与同一渲染路径）+ 修复前真实登录证据组合覆盖。
  服务端 Errors.txt 中存在与本修复无关的既有反射调用堆栈（未复现登录失败）。

### 4. 周期数统计

- 离线回放：6 组合 × 12~20s，每个组合覆盖 ≥2 个完整身体动画循环
  （11~18 帧循环 ×120ms ≈ 1.3~2.2s/轮），合计约 40+ 个循环、544 个抽帧，
  现代素材闪现次数 = **0**。
- 视口：800×600（原复现视口）；1280×1024 桌面下窗口仍为 800×600
  （UiScaler 居中，逻辑画布 1024×768 缩放 1.25），与原复现一致。

## 五、提交与仓库状态
- 修复 commit：`f063e47e 修选角/建角屏旧 Zircon 角色动画瞬闪：+40 特效层门控改为只查 EI 原版 WIL`
- 已推送：`origin/master = f063e47e508d00b085e53a5e8768fcdfb0297f4d`（读取远端确认一致）。
- staging 仅含本任务 2 个文件（`git diff --cached --check` 通过；未触碰
  `ClientData/zh/*`、`ServerLibrary/chinese_alias.json` 等并行任务的未提交 WIP）。
- 证据不入库的大文件：修复前录屏 `/tmp/flash_probe/run1/capture.mp4`（约 21MB，
  sha256 见下），按 goal 要求提供本机路径与哈希，不塞进 git。
  - sha256(capture.mp4, run1) = 4b8c1fca6ad7568c23f80b6498019741591fb278a2ce4726f9b2e408bd78b58f
    (29,558,048 bytes)；抽帧证据（crop_f_005..008 等）已入库。

## 六、未验证项 / 风险

1. **真实账号的修复后登录回归未跑**：`test@test.com` 在验证窗口被并行客户端
   占用（非本任务进程，遵守不抢占约定）。离线回放走的是同一 `SelectScene`
   场景与渲染管线（`--legacy-slot-preview` 仅替换角色数据来源，不绕过任何
   渲染代码），覆盖了全部 11 个泄漏组合中的 6 个代表性组合；其余 5 个组合
   （战士女 v0/v3/v4、法师男 v3、道士男 v0）与已验证组合走完全相同的
   `LegacyWilHasFrame` 门控分支，逻辑上同源。如需补齐，等账号空闲后跑一次
   同款录屏即可。
2. `Interface1c[19]`（阴影层初始帧，WIL/ZL 双空）与 `GameInter[116]`（EI 无此
   帧，现代库有配置图标）是修复前就存在的两条无害/既有回退，不属于瞬闪，
   本次未改动。
