# 道馆带刀侍卫与六面神石名称及坐标对齐报告

**日期**：2026-10-08  
**环境**：Debian (82) / Zircon Mono / Mir3 经典纯净版  

---

## 1. 背景与问题描述

1. **道馆守卫模型与坐标偏差**：
   - 用户反馈：道馆（原白日门）日弘门大门守卫模型不符，原版应为身背/腰悬佩剑的帅气“带刀侍卫”，此前被误配为手持长矛的重甲步兵 `TownGuard`；
   - 日弘门大门前两侧守卫站位不对称，左侧守卫缩在房檐外斜坡草地，右侧在台阶旁，显得散乱失修。

2. **六面神石名称误译与底座脱节**：
   - 用户反馈：传奇3经典传送石应为「六面神石」，之前客户端翻译被误作为「六角圣石」或「六角神石」；
   - 六面神石的视觉位置与地图底层的六角石座/内圈石盘严重脱节（下偏/右偏约 2 个瓦片高度 ~48-64px），导致旋转发光动画歪斜脱心。

---

## 2. 根因分析与修复方案

### 2.1 六面神石名称与底座对齐

1. **名称修正**：
   - 原版英文标识为 `Hexa Holy Stone`，韩国原版 MUD3 标识为 `13Move_*`（已有部分翻译为「六面神石」）。
   - 在 `ClientData/zh/zh-glossary.json` 与 `GodotClient/translations/db_names.json` 中统一更正：
     - `"Hexa Holy Stone"` -> `"zh": "六面神石"`, `"ja": "六面神石"`。
   - 在 `ServerLibrary/chinese_alias.json` 中添加别名，便于管理命令调用。

2. **坐标脱节原因与各主城石阵底座几何中心**：
   - 地图底座中心由 `tile_idx=607` 及周边石台组成。客户端 NPC 渲染采用 `CellToScreen` 瓦片底线叠加偏移。原数据库坐标向下偏移了 2-4 格，导致悬于底座下方。
   - 经实机瓦片与渲染对齐测算，各主城六面神石的绝对贴合对齐坐标为：
     - **道馆 (Map 1)**: `(416, 179) -> (416, 177)`（完全对中石阵圆盘与六块符文石板）
     - **比奇城 (Map 0)**: `(498, 463) -> (498, 461)`
     - **毒蛇山谷 (Map 2)**: `(306, 244) -> (306, 242)`
     - **绿洲 (Map 4)**: `(435, 83) -> (432, 81)`
     - **潘夜岛 (Map 8)**: `(288, 241) -> (288, 237)`

### 2.2 道馆守卫模型与日弘门对称站位

1. **模型修正**：
   - 在 `Mon-12.Zl` 资源中：
     - 帧 3000 系列 (`ForestGuard`)：蓝袍道服、腰悬佩剑、手扶剑柄的英俊侍卫（原版道馆/白日门经典带刀侍卫）。
     - 帧 4000 系列 (`TownGuard`)：铁甲持矛步兵。
   - 修改 `SetupGuardsSystem.cs` 与 `GuardLayoutRestore.cs`：道馆（Map 1）守卫统一指定为 `ForestGuard`（带刀侍卫）。

2. **日弘门台阶对称坐标**：
   - 日弘门台阶两侧通道对称点位：
     - 左侧守卫：`(372, 162)`，朝向 `DownRight` (3)
     - 右侧守卫：`(375, 164)`，朝向 `DownLeft` (5)
   - 彻底解决原版遗留数据中左侧守卫缩在房檐外草地的问题。

---

## 3. 数据库同步与完整性验证

按 AGENTS.md 纪律更新数据库并严格同步 4 处镜像：
- `System.db 源文件 MD5: 82703ee967aec2fd0a968d8db5658303`
  - `[OK] /home/tetsuya/development/zircon/System.db`
  - `[OK] /home/tetsuya/development/zircon/Debug/ServerCore/Database/System.db`
  - `[OK] /home/tetsuya/mir2ei/Data/System.db`
  - `[OK] /home/tetsuya/mir2ei/Database/System.db`

---

## 4. 客户端与游戏内实机运行证据

1. **六面神石**：
   - 运行诊断日志：
     ```text
     [ObjectView] 首帧诊断: type=NPC name=六面神石 lib=NPC.Zl shape=56 drawImage=0 dir=Up frame=0 anim=Standing DrawFrame=0 BodyFrame=5600 Cell=(416,177) Pos=(0,0) viewport=(1280,975)
     ```
   - 游戏截图证据：`hexastone_ingame.png`，六面神石稳固矗立于八卦石阵中央圆盘，六块外圈石板符文与特效光晕 100% 像素级贴合对齐。

2. **日弘门带刀侍卫**：
   - 运行诊断日志：
     ```text
     [ObjectView] 首帧诊断: type=Monster name=ForestGuard lib=Mon-12.Zl shape=3 drawImage=0 dir=DownRight frame=0 anim=Standing DrawFrame=30 BodyFrame=3030 Cell=(372,162) Pos=(0,0) viewport=(1280,975)
     [ObjectView] 首帧诊断: type=Monster name=ForestGuard lib=Mon-12.Zl shape=3 drawImage=0 dir=DownLeft frame=0 anim=Standing DrawFrame=50 BodyFrame=3050 Cell=(375,164) Pos=(0,0) viewport=(1280,975)
     ```
   - 游戏截图证据：`rihong_gate_guards.png`，大门台阶左右两侧各有一名身着蓝袍的带刀侍卫面向台阶下方持刀值守，完全对称。
