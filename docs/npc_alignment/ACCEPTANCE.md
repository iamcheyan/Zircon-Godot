# NPC 体系对齐执行验收报告

> **执行时间**: 2026-10-04
> **执行依据**: `docs/NPC_SYSTEM_ALIGNMENT_AND_WEBSITE_INTEGRATION_PLAN.md`
> **执行者**: 执行智能体（按 SOP 6 步流水线执行）

---

## 0. 与计划文档的偏差说明（重要）

执行中发现计划文档的两处事实性错误，经用户确认后按权威数据执行：

| 项 | 计划文档说法 | 权威事实（已核验） | 处理 |
|---|---|---|---|
| 权威数据源 | `Mud3-Config/Envir/Npcs.txt` | **该文件为 0 字节空文件**。真实原版权威数据在 `Mir3-Research/local-reference-data/yxs-mud3-2026-09-25/mud3/Envir/Merchant.txt`（2012 Mud3，263 条活跃记录，与既往审计 `02Weapon_Bichon1` 等 ID 吻合） | 用户确认改用 Merchant.txt |
| 地图代号 | 比奇=`01`、道馆=`02`、银杏=`0` | Mud3 `Mapinfo.txt`: **比奇=`0`、道馆=`1`、银杏=`02`、沙巴克=`3`**。与现役 DB `MapInfo.Description`（Bichon Town/银杏山谷/道馆）一致 | 用户确认按真实代号 |

---

## 1. 写库合规性 ✅

- **4 处 System.db MD5 完全一致**:
  - `Debug/ServerCore/Database/System.db`（服务端实际读取，`Session` 默认根 `.\Database\`）
  - `/home/tetsuya/mir2ei/Data/System.db`
  - `/home/tetsuya/mir2ei/Database/System.db`
  - `./System.db`（仓库根）
  - 终态 MD5: `29223469aec911a934d440bbaaf996f1`（4/4 相同）
- **服务端重启后日志无报错**: `grep -cE "\[NPC\] Bad Map|Failed to spawn NPC"` = **0**
- 服务端运行时生成计数（临时诊断，已移除）:
  - `Bichon Town [0] npcs=16` / `Banya Village [2] npcs=13` / `Lost Paradise [1] npcs=12`
  - 与 System.db 中各图 NPC 数**完全一致**，证明写库被服务端正确加载。

## 2. 零物理破坏 ✅

迁移前后行数对比（round-trip 导出比对）：

| 表 | 前 | 后 | Δ |
|---|---|---|---|
| NPCInfo | 294 | 294 | 0 |
| NPCPage | 304 | 304 | 0 |
| NPCAction | 288 | 288 | 0 |
| NPCCheck | 430 | 430 | 0 |
| MapRegion | 5010 | 5010 | 0 |

- **76 个新版多余 NPC 通过 `Region=null` 软停用**，服务端 `SEnvir.cs:927 if (info.Region == null) continue;` 静默跳过生成，脚本树完整可逆。

## 3. 经典城镇 NPC 就位 ✅

8 个比奇核心商人从 70×70 测试图 `0_000 (Town Hall)` 迁至原版 Mud3 比奇城（800×800）真实点位：

| NPC Index | NPCName | 原位置 | 新位置 (Mud3 权威) | Mud3 ID | 原版中文名 |
|---|---|---|---|---|---|
| 13 | Mr. Kang | 0_000(22,43) | **0 (402,356)** | 02Weapon_Bichon1 | 啊康 (武器) |
| 14 | Isaac | 0_000(28,43) | **0 (470,424)** | 05Book_Bichon | 店员 (书店) |
| 15 | David | 0_000(22,45) | **0 (397,363)** | 04Potion_Bichon1 | 药店老板 |
| 16 | Lennard | 0_000(28,45) | **0 (450,413)** | 07Grocery_Bichon | 杂货商 |
| 17 | Amy | 0_000(22,49) | **0 (414,349)** | 08Accessory_Bichon | 恩实 (首饰) |
| 18 | Loy | 0_000(25,42) | **0 (441,374)** | 10ChestnutMarket_Bichon | 栗子商人 |
| 19 | Linda | 0_000(22,47) | **0 (480,407)** | 03Armor_Bichon | 怡美 (防具) |
| 20 | Murphy | 0_000(28,47) | **0 (446,405)** | 01Meet_Bichon1 | 金氏 (肉店) |

- 其余 50 个交集 NPC 经 round-trip 比对**已在原版点位**（既往 NpcMover 迁移已生效），本期 Retain。
- 柜台站位: 17/58 个目标点位于原版柜台阻挡格内（`MapReader` Flag 语义核验），按计划要求保留原版真实站位，未"修正"到柜台外。
- 目标点无重叠、无越界（58/58 in-bounds，0 collision）。

## 4. 交互正常 ✅

- 服务端 `[NPC] Bad Map` / `Failed to spawn NPC` = 0，NPC 正常进入 `Map.NPCs` 注册表。
- 实机客户端日志确认 NPC 进视野: `[Game] 添加物体: NPC '洛伊' ObjectID=32 Cell=(441,374)` —— 即迁移后的栗子商人在原版点位被客户端接收渲染。
- NPCPage/NPCCheck 等对话数据零改动，`EntryPage` 引用链原样保留。

## 5. 网站百科文件存在 ✅

- `/home/tetsuya/development/mir3-website/data/npcs.json` 已生成：
  - **218 条**在役 NPC（= 游戏内实际生成的全部 NPC，与 DB `Region != null` 计数一致）
  - 76 个软停用 NPC **未进入**百科（游戏内不存在的不进百科，保证 100% 一一对应）
  - 结构含 `id / name_zh / name_en / map_code / map_name_zh / x / y / category / services / image / mud3_id`
  - 8 个比奇商人坐标与游戏内点位逐条核验 ✅

## 6. 实机截图证据 ⚠️ 部分

- `docs/screenshots/npc_alignment/00_entry.png` — 实机进入游戏画面（Xvfb 无头环境截取）
- `docs/npc_alignment/server_npc_spawn_evidence.txt` + `RESULTS.md` — 服务端生成计数实机证据
- 受限说明: 测试账号 `test@test.com` 与其他并行智能体共享，会话被反复抢占，
  且 `@move` GM 命令输入在无头 Xvfb 下受输入法/修饰键干扰，多城镇逐点截图
  未能在本会话内全部完成。替代证据：
  1. 服务端运行时 `npcs=16/13/12` 与 DB 逐图一致（写库生效的直接证明）；
  2. 客户端实机收到 `NPC '洛伊' Cell=(441,374)`（迁移 NPC 进世界渲染）；
  3. webclient 世界观测试台按修正坐标渲染 NPC（独立数据源交叉印证）。

## 7. 提交纪律 ✅

- 见本次提交：仅包含本任务相关文件（工具/文档/截图/网站数据），不含他人 WIP。
- 备份: `/home/tetsuya/mir2ei/Backup/npc_alignment_20261004122723/`（4 处原始库，MD5 `5713cb2c...`）

---

## 产出清单

| 文件 | 说明 |
|---|---|
| `docs/npc_alignment/build_alignment_matrix.py` | Step2 三方数据提取 + 对齐矩阵生成 |
| `docs/npc_alignment/build_website_npcs.py` | Step5 网站百科 npcs.json 生成 |
| `docs/npc_alignment/alignment_matrix.{json,csv}` | 294 NPC 对齐矩阵（Migrate 58/Disable 76/Retain 130/Pending 30，按 10-04 live 数据） |
| `docs/npc_alignment/missing_npcs_pending_intro.{json,csv}` | 差集B 原版有/新版缺失 待引入清单（205 条） |
| `docs/npc_alignment/RESULTS.md` / `server_npc_spawn_evidence.txt` | 服务端实机证据 |
| `Tools/NpcAligner/` | Step3 迁移控制台（Dry-run + commit 双模式，端口 7000 守卫，引用计数原位改/新建 region） |
| `docs/screenshots/npc_alignment/` | 实机截图 |
| `mir3-website/data/npcs.json` | 网站百科数据源 |

## 软停用名单（76 个，可逆）

内测NPC、泡点系统、VIP、名望系统、功能NPC×3、钓鱼系统、钻石抽奖、天降宝箱、
仓库系统×3、买卖商人×3、天下第一战/法/道、道观公告、合成大师、活动管理、沙城管家、
化废为宝、经验化废、升级加点、结婚司仪、买马商人、美发专家、比奇官吏、行会旗帜、
比赛评判×3、My00DefaultNpc、道观传送、传送银杏、传送比奇、传送土城、传送流放、
潘业传送3/4、绿洲沙漠、进进出出、神舰接待、诺玛接待×4、西沙接待、冰城接待、
绝情接待×2、魔界接待×2、群魔接待×4、异界接待×4、邪恶之地、邪恶接待×2、
龙穴接待×2、黑暗巢穴、青青草原、幽暗森林、钓鱼岛、血案接待、骑兵接待、尊者接待、杀人掠夺。

恢复方法: `NpcAligner` 逆向或直接给对应 NPCInfo 重新指派 Region 即可，脚本树未动。
