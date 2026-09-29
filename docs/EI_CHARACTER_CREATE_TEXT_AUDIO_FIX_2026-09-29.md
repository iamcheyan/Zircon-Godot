# EI 选角 / 新建人物：文案、交互、音频修复记录（2026-09-29）

本文记录本轮针对用户反馈的三个问题的修复，以及顺带对齐的两处原版差异。
上一轮「新建人物界面 parity 复核」见
[`EI_CHARACTER_CREATE_PARITY_2026-09-28.md`](EI_CHARACTER_CREATE_PARITY_2026-09-28.md)，
选角列表见
[`EI_CHARACTER_SELECT_ANIMATION_PARITY_2026-09.md`](EI_CHARACTER_SELECT_ANIMATION_PARITY_2026-09.md)。

- 分支：`master`
- 起点：`61bb5c41`（本轮开始前的 HEAD）
- 终点：`fa295fc6`（已推送 `origin/master`，fork `iamcheyan/Zircon`）
- 证据来源：`Mir3-Research/reference/mir3-source/Source/Client/`（Delphi 原版客户端源码：
  `IntroScn.pas` / `FState.pas` / `ClMain.pas` / `CMsg.pas` / `SoundUtil.pas`）、
  `Mir3-Research/docs/research/ei-ui-layout/login-flow-evidence.json`（反汇编证据）、
  客户端加密消息表 `CMList.dat`（本轮用 `EDCode.pas::Decrypt` 算法离线解出）。

---

## 0. 用户反馈 → 修复对照

| # | 用户反馈 | 根因 | 修复 | 提交 |
| --- | --- | --- | --- | --- |
| 1 | 新建人物「原版是有文字的」，现在没有 | 原版 `DrawNewChr` 在 (95,15) 画人物说明框，移植版从未实现 | 补回说明框（`[ 男 战士 ]` + 职业说明） | `e188ddfc` |
| 2 | 新建人物时背景音乐重复播放 / 两个音频一起响 | legacy 仍播现代 `SelectScene` 循环（SelChr.wav，28s），同时相位 BGM 又播 SelChr_bgm.wav，同曲错位叠加 | legacy 不播现代循环；相位 BGM 单实例、切相位先停上一首 | `88a25633` |
| 3 | 人物的动画还保留 Zircon 的动画和闪现 | `_Process` 里保留了现代 Zircon 的 `帧+100`/`帧+130` 叠加层，EI 原版只有 `帧+20`（阴影）与 `帧+40`（3D 叠加） | legacy 直接跳过该叠加块 | `1fd99689` |
| 4 | 「细节的交互」 | 见 §2（说明框随职业/性别更新、未选中预览灰阶、名字框外框、选中详情框、删除确认、建角上限） | 见下 | `9f4ea494` `2a51de66` `d37d3568` `fa295fc6` |

---

## 1. 新建人物说明文案（用户反馈 #1）

### 原版行为

`IntroScn.pas::TSelectChrScene.DrawNewChr`（phase 2 每帧绘制）：

```pascal
with g_DXCanvas do begin
  rcShow.Left := 95;  rcShow.Top := 15;
  rcShow.Right := rcShow.Left + _CHR_EXPLAIN_WIDTH + 20;   // _CHR_EXPLAIN_WIDTH = 430
  rcShow.Bottom := rcShow.Top + m_nDividedExplain * 18 + 20;
  Draw2DRect(rcShow, $C89664, 80);        // 填充 RGB(100,150,200) alpha 80/255
  Draw2DRectLine(rcShow, $FF966432);      // 边框 RGB(50,100,150)
  // 首行：字号 11 粗体，按职业上色
  //   武士 RGB(250,200,150) / 法师 RGB(250,170,170) / 道士 RGB(150,220,150)
  // 正文：RGB(250,250,255)，从 Top+35 起、行距 18
end;
```

首行文字 = `SetCharExplain(gender, job)` 拼出的
`CMsg(211/212)` + `CMsg(213/214/215)` → `[ 男 战士 ]` / `[ 女 道士 ]` …；
正文 = `CMsg(216/217/218)`，用 `StringDivide(_CHR_EXPLAIN_WIDTH, …)` 折到 430 宽。

### CMList.dat 文案（本轮新增 `LegacyEiText.cs` 逐字转写）

| id | 文本 |
| --- | --- |
| 206 | `角色名` |
| 207 | `等级` |
| 208/209/210 | `职业   战士` / `职业   法师` / `职业   道士` |
| 211/212 | `[ 男` / `[ 女` |
| 213/214/215 | ` 战士 ]` / ` 法师 ]` / ` 道士 ]` |
| 216/217/218 | 战士 / 法师 / 道士 的职业说明长文 |
| 219 | `首先创建角色，才能开始游戏。` |
| 223 | `文字过多。(韩文最多6个字)` |
| 224 | `此角色名已经存在。` |
| 225 | `此角色名不正确。` |
| 226 | `不能创建2个以上的角色。` |
| 227 | `删除角色发生错误。` |
| 228 | `删除的角色无法还原，一定时间内不能创建同名角色，还要删除吗？` |

> `CMList.dat` 用 `EDCode.pas::Decrypt` 解密（种子 `$F0,$39,$AB,$8E`、
> `CrypLong=$9FDE1A93`、4 轮 `data[8+i] ^= (data[3-J]+i)`），本轮离线解出后转写，
> 避免运行时依赖该文件。

### 实现

- `GodotClient/Scripts/LegacyEiText.cs`（新文件）：上表 + `GenderJobTitle/JobDescription/JobDetail`。
- `SelectScene.cs`：`_legacyCreateExplainBox` / `_legacyCreateExplainTitle` /
  `_legacyCreateExplainLines`，`UpdateLegacyCreateExplain()` 按 `_legacyCreateClassIndex`
  与 `_legacyCreateSelected` 重建；`WrapLegacyEiText()` 用 `MirSkin.MeasureText` 按 430 折行
  （与 `UiScaler` 缩放无关）。

### 交互接线（对应原版 handler）

| 原版 | 触发 | 本轮接线 |
| --- | --- | --- |
| `SelChrNewJob`（0x459D1D/0x459E19/0x459EA5） | 点职业钮 | `SelectCreateClass` → `RebuildLegacyCreateSlots` → `UpdateLegacyCreateExplain` |
| `DCreateChrClick`（FState.pas 14534） | 点预览人物 | `SelectLegacyCreateSlot` → `UpdateLegacyCreateExplain`（性别取被点槽，即 `[+0x1488]`） |

---

## 2. 其他交互 / 外观对齐

| 项 | 原版 | 修复前 | 现状 | 提交 |
| --- | --- | --- | --- | --- |
| 未选中预览 | `DrawNewChr` 对未选中槽用 `Blend_GrayScale` 灰阶画 | 两只都彩色 | `DXImageControl.GrayScale`（同一 dot(0.299,0.587,0.114) shader） | `9f4ea494` |
| 名字输入框外框 | `Draw2DRectLine(EdChrName 矩形, $FF966432)` | 无 | 名字框底加 `Border` RGB(50,100,150) | `9f4ea494` |
| 选角屏选中详情 | `PlayScene` 在 (80,110) 画 40+文本宽 ×70 的框：`角色名 Xxx` / `等级   N` / `职业   战士`，色 `$96C8FF`=RGB(255,200,150) | 两个常驻 DXLabel「名字 Lv等级 职业」 | 原版详情框；删除逐槽标签 | `2a51de66` |
| 删除确认 | `SelChrEraseChrClick`：`DMessageDlg(CMsg 228, [mbYes,mbNo])` → mrYes 才发 `SendDelChr` | 旧 Zircon C# 行为：Yes 禁用 5 秒 + 倒计时后显示硬编码英文 | 原版中文文案 + Yes 立即可用 | `d37d3568` |
| 建角上限 | F51 handler 扫 2 槽，占用则弹 LoadString 802、不改阶段（每账号 2 角色） | Zircon 的 4 角色上限 | legacy 上限 2 + 原版文案 | `fa295fc6` |
| phase 2 左上角 | 只有说明框 | 移植版 phase-0 提示文字「选择角色后点进入游戏」与说明框重叠 | 进 phase 2 时清掉该提示（错误文案保留） | `e188ddfc` |

---

## 3. 背景音乐（用户反馈 #2）

### 根因

`SelectScene._Ready` 无条件播 `SoundIndex.SelectScene`（`sounds.json` → `SelChr.wav`，
44100Hz/28.0s，`loop:true`）；`TickPhaseBgm` 又按相位播 `LegacySelChrBgm`
（`SelChr_bgm.wav`，22050Hz/28.29s）与 `LegacyCreateChrBgm`（`CreateChr_bgm.wav`，
22050Hz/27.69s）。`SelChr.wav` 与 `SelChr_bgm.wav` 是**同一首曲子**的两份拷贝
（时长 28.0 / 28.29s），所以两轨同曲错位叠加 = 用户听到的「重复播放」。

### 原版

`IntroScn.pas::PlayScene`（phase 0/3）与 `DrawNewChr`（phase 2）各有一处：

```pascal
if m_bBGMPlay then begin
  m_bBGMPlayTime := m_bBGMPlayTime + GetTickCount;
  if (m_bBGMPlayTime > 1000) then begin
    PlayBGMEx('.\Sound\SelChr.mp3');   // phase 0/3；phase 2 换成 CreateChr.mp3
    m_bBGMPlay := FALSE;  m_bBGMPlayTime := 0;
  end;
end;
```

`SoundUtil.pas::PlayBGMEx` 先 `ClearBGM`（停上一首）再
`BASS_StreamCreateFile(..., BASS_SAMPLE_LOOP)`（循环）——**任意时刻只有一首 BGM**。

### 修复

- `_Ready`：`if (!AutoLoginArgs.LegacyUi) SoundPlayback.Play(this, SoundIndex.SelectScene);`
- `SoundPlayback` 新增 `PlayBgm/StopBgm`：单实例、先停上一首、循环播放；
  `TickPhaseBgm` 改用 `PlayBgm`；相位切换即换曲。
- 建角成功 0x209 的一次性音效由误用的 `LegacySelChrBgm` 改为 `LegacySelChr`（SelChr.wav）。
- `_ExitTree` 调 `StopBgm()`（原版 `CloseScene` 的 `ClearBGM`）。

### 验证（`--legacy-slot-preview` 日志）

```
[LegacySelect] 相位 BGM: phase=0 -> LegacySelChrBgm
[Sound] BGM 播放 LegacySelChrBgm (SelChr_bgm.wav, 单实例循环)     ← 只有这一首
（点「创建角色」）
[LegacySelect] phase=1 ...；[Sound] 播放 LegacyCreateChr (CreateChr.wav, loop=False)
[LegacySelect] 相位 BGM: phase=2 -> LegacyCreateChrBgm
[Sound] BGM 播放 LegacyCreateChrBgm (CreateChr_bgm.wav, 单实例循环) ← 上一首已停
```

---

## 4. Zircon 叠加层 / 人物闪现（用户反馈 #3）

`SelectScene._Process` 保留了现代 Zircon 的合成层：非循环动画期间给槽 0 叠
`帧+100` 与 `帧+130` 两张图，定位 `(450,200) + baseOffset - overlayOffset`。

- EI 原版（`PlayScene` / `DrawNewChr`）只有：阴影 = `帧+20`、身体 = 当前帧、
  叠加 = `帧+40`（且只有选中槽画 +40）。
- `帧+100`/`帧+130` 在 EI 里没有对应物。以道士男 intro 段 `1460-1476` 为例，
  `+130` = `1590-1606`，用 `wilsdk` 解 `Interface1c.wil` 得到的是**别的角色的技能帧**
  （1590=36×94、1600=52×44、1606=76×48，非空），叠在洞窟上即「闪现杂影」；
  且只作用于槽 0，槽 1 没有，不对称。

修复：legacy 模式下 `_Process` 在叠加块前早退。现代 Zircon UI 路径不变。

**A/B 实证**（同一 `--legacy-slot-preview` 序列，仅切换该 guard；截图
`screenshots/ei-create-text-2026-09-29/06-overlay-before-fix.png` 与
`07-overlay-after-fix.png`，为 (430,200)-(520,290) 区域 4× 放大）：

- **修复前**：在洞窟岩壁 (450,200) 处叠出一枚**发光球体 + 剑**图标
  （即 `帧+130` 解出的 1590-1606 段），与角色无关，随 intro 动画闪现/消失；
- **修复后**：同位置只有干净的岩壁。

两轮逐帧像素差分显示，唯一与角色动画无关的结构性差异就在
`(451,224)-(487,260)`（该叠加块），其余差异来自两轮动画帧不同步。

> `帧+40` 叠加层原版用 3D 贴图页绘制（女道 g5 段 1980-1994 在 WIL 里是 256×128 /
> 256×256 幂次页），无 2D 等价物，仍未移植（见 §6）。

---

## 5. 本轮提交

| 提交 | 内容 |
| --- | --- |
| `1fd99689` | fix(ei选角): 移除 legacy 选角屏 Zircon 叠加层，消除人物闪现杂影 |
| `88a25633` | fix(ei建角): 修正新建人物背景音乐双重播放 |
| `e188ddfc` | feat(ei建角): 补回原版新建人物界面的说明文案 |
| `9f4ea494` | fix(ei建角): 未选中预览按原版灰阶 + 名字框补 1px 外框 |
| `2a51de66` | fix(ei选角): 选中角色详情改用原版 (80,110) 文本框，去掉移植版逐槽标签 |
| `d37d3568` | fix(ei选角): 删除确认改用原版 CMsg 228 Yes/No 对话框 |
| `fa295fc6` | fix(ei选角): 建角上限对齐原版每账号 2 个角色 |

远端核对：

```
git ls-remote origin refs/heads/master
# fa295fc6eeb2fe07301eb5f0ff51abdb89c4b20f  refs/heads/master
```

---

## 6. 验证

### 6.1 构建

```
dotnet build GodotClient/ZirconClient.csproj --no-restore --no-incremental
# 0 错误 / 3 既有警告
```

### 6.2 完整流程实机（Xvfb :101 + openbox + godot-mono）

隔离服务端：`/tmp/ei-flow-srv`（端口 7001/3001，`Database/Users.db` 为独立副本，
仓库内 `Debug/ServerCore/Database/Users.db` md5 保持
`138ac3549426fae0682a93af0afbed2d` 未被触碰）。

命令：

```
godot-mono --path GodotClient -- \
  --server 127.0.0.1 --port 7001 --user test@test.com --pass test123 \
  --stay-select --window=800x600
```

| 阶段 | 操作 | 结果 |
| --- | --- | --- |
| 登录 | 自动登录 | 成功，角色数 2 |
| 选角 | 进入 phase 0 | 背景 F50，无逐槽标签；BGM 只有 SelChr_bgm |
| 选角 | 点角色 | 出现 (80,110) 详情框「角色名 TestHero / 等级   255 / 职业   道士」 |
| 新建 | 点「创建角色」 | phase 1 播 CreateChr.wav + CreateChr.ogv → phase 2 背景 F80 |
| 新建 | 默认 | 说明框「[ 男 战士 ]」+ 战士说明；男槽彩色、女槽灰阶 |
| 新建 | 点「道士」+ 点女槽 | 说明框「[ 女 道士 ]」+ 道士说明；女槽彩色、男槽灰阶 |
| 新建 | 输入 `EIFlow1` + ✔ | 服务器回 0x209 → SelChr.wav + CreateChr.ogv → 回列表，角色数 +1 |
| 删除 | 选角色 + 「删除角色」 | 弹出 CMsg 228 中文确认框，Yes 立即可用 |
| 删除 | 点 Yes | 角色数 2→1，列表刷新 |
| 建角上限 | 账号 2 角色时点「创建角色」 | 日志「F51 建角被拒」+ 顶部显示「您可以为每个单独的帐号建立两个角色。」 |
| 进游戏 | 选角色 + 「开始游戏」 | phase 4 + StartGame.wav + StartGame.ogv → F602 公告确认 → 进入 GameScene（地图 8） |

### 6.3 截图

`screenshots/ei-create-text-2026-09-29/`：

| 文件 | 内容 |
| --- | --- |
| `01-create-warrior-male-explain.png` | 默认「[ 男 战士 ]」说明框 + 女槽灰阶 |
| `02-create-taoist-female-explain.png` | 「[ 女 道士 ]」说明框 + 名字已输入 |
| `03-select-info-box.png` | 选角屏 (80,110) 选中详情框（无逐槽标签） |
| `04-delete-confirm-cmsg228.png` | CMsg 228 删除确认框 |
| `05-two-character-limit.png` | 建角上限提示 |

---

## 7. 未闭合 / 差异（如实记录）

| 项 | 状态 | 说明 |
| --- | --- | --- |
| 选中槽 `帧+40` 叠加层 | candidate | 原版是 3D 贴图页（女道 1980-1994 为 256² 幂次页），无 2D 等价物，未移植 |
| 按钮旁悬停标签原版渲染 | candidate | 文案已接；渲染用移植版 tooltip，非原版「缩放 sprite + 文字条」 |
| 名字 EDIT 的原版 Win32 外观 | candidate | 位置/尺寸/上限一致；外观为移植版透明底 + 白字 + 光标 |
| 0x4586F0「类名条」 | candidate | 反汇编证据中位置未做像素级反解，未移植 |
| phase 0 顶部提示文字 | 有意保留 | 移植版在 (8,8) 的状态/错误文字（断线、建角失败等反馈）原版没有；已避开说明框，未删除 |
| 原版运行画面（Windows/DX8） | blocked | 本机无 Windows/Wine 与原版服务端，无法做同版对照 |

---

## 8. 复现脚本

```bash
# 离线取景（不连服、不建/删角色）
godot-mono --path GodotClient -- --legacy-slot-preview --window=800x600
godot-mono --path GodotClient -- --legacy-slot-preview --legacy-create --window=800x600

# 全流程（隔离服务端 /tmp/ei-flow-srv，端口 7001）
godot-mono --path GodotClient -- --server 127.0.0.1 --port 7001 \
  --user test@test.com --pass test123 --stay-select --window=800x600
```

> 窗口点击坐标：`xdotool --window <wid> X Y` 的 (X,Y) 即 **640×480 画布坐标**
> （窗口内容左上 = 画布原点）。窗口在屏幕上的位置由 WM 决定，用屏幕坐标点击时
> 需另加窗口偏移。
