# EI 新建人物界面运行取证 — 2026-09-28

## 范围与环境

- **离线取景**（`--legacy-slot-preview [--legacy-create]`）：不登录、不连服务器、不发包。
- **真实往返**（07–11）：连**隔离的服务端副本**——把 `Debug/ServerCore` 的
  可执行文件 + `Server.ini` 拷到 `/tmp/ei-create-srv`（改 Port=7001 / UserCountPort=3001），
  `Database/Users.db` 与 `System.db` 用**副本**、`Map/` 只读软链、`cwd=/tmp/ei-create-srv`
  （MirDB 的 `.\Database\` 相对 cwd）。隔离证明：副本 `Users.db` 初始 md5 与仓库内
  `Debug/ServerCore/Database/Users.db` 相同，全部读写只发生在前者；验收后仓库内
  `Users.db` 的 md5 仍为 `138ac354…`、mtime 仍为 2026-09-27 10:15（未被动过）。
  测试账号 `test@test.com`、新建角色名 `EITest02`/`EITest03`（用完即与临时库一起丢弃）。
- 测试机 Xvfb `:100`（1024×768×24）+ openbox，Godot 4.6.3 mono（llvmpipe 软件 Vulkan）。
- 客户端窗口被 `ClientSettings.ApplyLegacyPregameWindow()` 固定为 **800x600 logical**，
  复古画布为左上角 640×480（画布原点实测 (1,24)，用 F50/F80 逐像素对齐得到，
  11 张截图全部 **MAE = 0.000**）。下列截图均为 **800×600 客户端完整 viewport**。

## 截图

### 离线取景（不连服务器）

| 文件 | 阶段 | 内容 |
| --- | --- | --- |
| `00-offline-wil-prediction-warrior.png` | — | **离线正向预测**（只用 `wilsdk` 解码 Interface1c + 原版绘制约定合成：F80 + 2 预览槽 + F82 + F81 + 名字框底 + 5 按钮），**不是**原版运行画面、也不是本客户端截图；用于与 `03` 对照 |
| `01-phase0-list-before-create.png` | stage 0 | 角色列表：背景 F50、洞窟两个槽位、4 个文字钮 |
| `02-phase1-createchr-video.png` | stage 1 | 点「创建角色」后的 `CreateChr.ogv` 过场 |
| `03-phase2-create-default-warrior-male-sel.png` | stage 2 | 创建界面初始态：F80 背景、男武（选中、动画中）在 (110,110)、女武（定格首帧）在 (400,160)、F81 石台、名字框、5 个图形钮 |
| `04-phase2-create-taoist-female-sel.png` | stage 2 | 点女槽 + 点道士钮：锚点变为 (110,120)/(425,118)，帧段 1640-1656 / 1940-1954（女道样本） |
| `05-phase2-create-wizard-female-sel.png` | stage 2 | 点法师钮：锚点 (110,120)/(420,115)，帧段 1040-1054 / 1340-1356 |
| `06-phase2-exit-back-to-phase0-list.png` | stage 2 → 0 | 点 ✘（退出人物创建）后回到列表：背景切回 F50、洞窟槽位恢复 |

### 真实往返（隔离服务端副本，端口 7001）

| 文件 | 阶段 | 内容 |
| --- | --- | --- |
| `07-create-screen-name-entered.png` | stage 2 | 真实流程进创建界面并在 (288,405) 输入框内键入 `EITest02` |
| `08-confirm-success-transition-video.png` | stage 3 | 点 ✔ 后服务器回 0x209 → 播 `SelChr.wav` + 重播 `CreateChr.ogv`（原版 0x459216 链） |
| `09-back-to-list-new-warrior.png` | stage 0 | 过场播完回列表，新角色 **`EITest02` Lv8 战士** 出现在第 2 槽（默认职业武士、性别男） |
| `10-create-female-taoist.png` | stage 2 | 点女槽（性别=女）+ 点道士钮 → 锚点 (110,120)/(425,118)，帧段 1640-1656 / 1940-1954 |
| `11-back-to-list-new-taoist.png` | stage 0 | 提交后回列表，新角色 **`EITest03` Lv8 道士**（性别取自被选中的**女**预览槽） |

## 量化结果（与独立预测逐像素比较）

- 画布原点 (1,24)，**背景 MAE = 0.000**（屏上背景与 `Interface1c.wil` F50/F80 逐像素相同）。
- 全画布 MAE：`03` = 0.128、`04` = 0.073、`05` = 0.195（单位 /255）；
  残差集中在名字框 1px 边与空输入框的闪烁光标、以及鼠标 tooltip 位置，slot 区无结构性残差。
- 帧号识别：连续截图槽 0 的帧 = 442 → 448 → 454 → **442（回绕）** → 447 → 453，
  即 18 帧段（440-457）按 120 ms/帧推进并正确回绕。
- 真实往返日志（节选）：`phase=1` → `CreateChr.ogv 开始/完毕` → `phase=2 背景F=80` →
  `槽重建 class=Warrior … 选中槽=0` → `选中预览槽 1（性别 Female）` →
  `槽重建 class=Taoist 锚点 slot0=(110,120) slot1=(425,118)` →
  `建角色成功: EITest03` → `phase=3 保留上一相位画面` + `相位 BGM: phase=3 -> LegacySelChrBgm`
  → `过场 CreateChr.ogv 播放完毕` → `phase=0 背景F=50` + `洞窟槽位: 角色数=2`。

## 结果与限制

- **完成**：stage 2 的背景/控件/坐标/帧段/动画时序与交互（职业钮、点人物选性别、✔ 确认、
  ✘ 退出、名字输入）均按原版字节证据实现，并有离线量化对照 + 真实服务端往返。
- **完成**：创建成功链路（提交 → 服务器 → 过场 → 回列表并出现新角色）在隔离副本上实测通过，
  两次分别验证了「默认男武」与「点选女槽 + 道士」两条路径。
- **blocked**：原版运行画面（需同版 Windows/DX8 环境）本机不可得，故
  「原版 vs Godot 同 viewport 视觉对照」仍未完成；上述预测对照不等于原版运行对照。
- **未取第二分辨率**：客户端强制 800×600，无法取得 1024×768 对照。
- **未测试**：创建**失败**提示的可见性（原版弹 Mirmg 文案框；移植版 legacy 下错误信息落在
  隐藏的 status 标签上）；删除角色；第三个角色（原版上限 2）。
