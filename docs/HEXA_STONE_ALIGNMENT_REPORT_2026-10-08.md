# 六面神石（Hexa Holy Stone）全图补齐与坐标对齐报告

**日期**：2026-10-08  
**目标**：依据原版 MUD3 官方事实数据，全量补齐各主城、野外与神殿的六面神石，校正道馆等地图的传送石坐标偏差，统一正名为「六面神石」，并清理私服残留废弃数据。

---

## 一、事实依据与问题分析

### 1. 官方事实源
- **权威文件**：`/data/NAS/TMP/Mud3/Envir/Merchant.txt`（GB18030 编码）及 `/data/NAS/TMP/Mud3/Envir/Market_Def/13Move_*.txt`。
- **神石总数**：MUD3 官方定义了 **33 处** `13Move_*` 六面神石，其造型为 Shape 56（主城/野外通用石阵）与 Shape 57（潘夜/神殿绿宝石石阵）。
- **实机底座扫描**：沙漠土城（Map 5）瓦片底层包含 4 座石盘基座，官方 MUD3 登记了南门与内城 2 座，西门 `(63, 195)`（玩家截图反馈处）与东门 `(227, 128)` 遗留底座在本次一并补齐；潘夜神殿 `D1115 (358, 353)` 亦同步补齐，全服共计 **36 处**。

### 2. 问题根因
1. **坐标偏上问题**：
   - 上一轮人工尝试将道馆从 `(416, 179)` 减 2 调整至 `(416, 177)`，实机表现为神石悬浮在底座上方。
   - **裁决**：MUD3 官方 `Merchant.txt` 的 `(416, 179)` 即为精确锚点，瓦片中心与 NPC 站立锚点完全自洽，已彻底恢复为官方真值。
2. **命名与显示不一致**：
   - 数据库中部分神石直接保留了私服导入的脚本名（如 `13Move_Kugkyung1`、`13Move_Ant`），且缺少 `EntryPage` 交互脚本。
   - **纠正**：统一正名为 `Hexa Holy Stone`，由客户端多语言表（`db_names.json` / `zh-glossary.json`）统一翻译为标准中文「六面神石」；并关联对应的传送对话页面。
3. **私服残余与孤立数据**：
   - 数据库中残留 12 条 `Map = null`、`Pos = null` 的私服垃圾 NPC（`NPC #321..332`，名称如「道观传送」「绿洲沙漠」等），已全量清理。

---

## 二、全量 36 处六面神石对照总表

| 序号 | 标识 / MUD3 Script | 地图编号 | 地图名称 | X 坐标 | Y 坐标 | 造型 (Image) | 传送对话 (EntryPage) | 状态 |
|:---:|:---|:---:|:---|:---:|:---:|:---:|:---|:---:|
| 1 | 13Move_Bichon1 | 0 | 比奇城 | 498 | 463 | 56 | BT Teleporter | 校准坐标 (原461->463) |
| 2 | 13Move_Bichon2 | 0 | 比奇城 | 507 | 313 | 56 | BT Teleporter | **补齐新增** (比奇东) |
| 3 | 13Move_Bichon3 | 0 | 比奇城 | 370 | 336 | 56 | BT Teleporter | **补齐新增** (比奇北) |
| 4 | 13Move_Bichon4 | 0 | 比奇城 | 379 | 444 | 56 | BT Teleporter | **补齐新增** (比奇西) |
| 5 | 13Move_Kugkyung1 | 01 | 边境城市 | 456 | 216 | 56 | BC Teleporter | 正名并关联脚本 |
| 6 | 13Move_Kugkyung2 | 01 | 边境城市 | 411 | 287 | 56 | BC Teleporter | 正名并关联脚本 |
| 7 | 13Move_Kugkyung3 | 01 | 边境城市 | 463 | 356 | 56 | BC Teleporter | 正名并关联脚本 |
| 8 | 13Move_Eunhang | 02 | 银杏山谷 | 249 | 144 | 56 | BT Teleporter | 正名并关联脚本 |
| 9 | 13Move_DoGwan | 1 | 道馆 | 416 | 179 | 56 | LP Teleporter | **校准恢复官方坐标 (416, 179)** |
| 10 | 13Move_SnakeVally1 | 2 | 毒蛇山谷 | 306 | 244 | 56 | BV Teleporter | 校准坐标 (原242->244) |
| 11 | 13Move_SnakeVally2 | 2 | 毒蛇山谷 | 314 | 193 | 56 | BV Teleporter | **补齐新增** (毒蛇北) |
| 12 | 13Move_Sabuk | 3 | 沙巴克城 | 222 | 159 | 56 | SK Teleporter | 校准坐标 (原119->159) |
| 13 | 13Move_Sabuk | 3 | 沙巴克城 | 294 | 539 | 56 | SK Teleporter | **补齐新增** (沙巴克南) |
| 14 | 13Move_Sabuk | 3 | 沙巴克城 | 49 | 566 | 56 | SK Teleporter | **补齐新增** (沙巴克西南) |
| 15 | 13Move_Sabuk | 3 | 沙巴克城 | 71 | 140 | 56 | SK Teleporter | **补齐新增** (沙巴克西) |
| 16 | 13Move_Oasis | 4 | 绿洲 | 435 | 83 | 56 | NV Teleporter | 校准坐标 (原432,81->435,83) |
| 17 | 13Move_Samak1 | 5 | 沙漠土城 | 204 | 289 | 56 | MW Teleporter | 已存在 |
| 18 | 13Move_Samak2 | 5 | 沙漠土城 | 112 | 177 | 56 | MW1 Teleporter | 已存在 |
| 19 | 13Move_Samak3 | 5 | 沙漠土城 | 63 | 195 | 56 | MW Teleporter | **实机补齐** (玩家截图西门基座) |
| 20 | 13Move_Samak4 | 5 | 沙漠土城 | 227 | 128 | 56 | MW Teleporter | **实机补齐** (东门基座) |
| 21 | 13Move_Ant | 6 | 沙漠 | 273 | 731 | 56 | MW Teleporter | 正名并关联脚本 |
| 22 | 13Move_Mongchon1 | 74 | 盟重县 | 349 | 329 | 56 | MW Teleporter | 正名并关联脚本 |
| 23 | 13Move_Mongchon2 | 74 | 盟重县 | 271 | 267 | 56 | MW Teleporter | 正名并关联脚本 |
| 24 | 13Move_SukGak | 75 | 石阁庙 | 184 | 90 | 56 | MW Teleporter | 正名并关联脚本 |
| 25 | 13Move_HalfNight1 | 8 | 潘夜岛 | 288 | 241 | 57 | FV Teleporter | 校准 (原288,237->241, 造型156->57) |
| 26 | 13Move_HalfNight2 | 8 | 潘夜岛 | 113 | 463 | 57 | FV Teleporter | **补齐新增** (潘夜西岸) |
| 27 | 13Move_HalfNight3 | 8 | 潘夜岛 | 668 | 388 | 57 | FV Teleporter | **补齐新增** (潘夜东岸) |
| 28 | 13Move_HalfNight4 | 8 | 潘夜岛 | 448 | 579 | 57 | FV Teleporter | **补齐新增** (潘夜南岸) |
| 29 | 13Move_HalfNight5 | 8 | 潘夜岛 | 424 | 239 | 57 | FV Teleporter | **补齐新增** (潘夜村北) |
| 30 | 13Move_RedZone | 81 | 流放岛 | 129 | 265 | 57 | LL Teleporter | 正名并关联脚本 |
| 31 | 13Move_HalfTemple1 | D1110 | 潘夜神殿1层 | 15 | 18 | 57 | Teleport Banya Hall | 正名并关联脚本 |
| 32 | 13Move_HalfTemple2 | D1110 | 潘夜神殿1层 | 28 | 31 | 57 | Teleport Banya Hall | 正名并关联脚本 |
| 33 | 13Move_HalfTemple | D11031 | 潘夜神殿3层西部 | 199 | 257 | 57 | Teleport Banya Hall | 清理冗余重复NPC并正名 |
| 34 | 13Move_HalfTemple | D1105 | 潘夜神殿5层 | 219 | 99 | 57 | Teleport Banya Hall | **补齐新增** |
| 35 | 13Move_Numa | 41 | 诺玛沙漠 | 184 | 136 | 56 | II Teleporter | 正名并关联脚本 |
| 36 | 13Move_HalfTemple | D1115 | 潘夜神殿8层 | 358 | 353 | 57 | Teleport Banya Hall | **补齐新增** |

---

## 三、数据库镜像一致性同步

根据仓库纪律，本次修改已在停服状态下直接写入并完成了全部 5 处 `System.db` 副本的同步与哈希校验：

1. `/home/tetsuya/development/zircon/System.db`
2. `/home/tetsuya/development/zircon/Debug/ServerCore/Database/System.db`
3. `/home/tetsuya/development/Debug/ServerCore/Database/System.db`
4. `/home/tetsuya/mir2ei/Data/System.db`
5. `/home/tetsuya/mir2ei/Database/System.db`

**统一 SHA256 哈希**：`B35BA4AEECCBBA9BDC6A43962168E403ADEB082B360199FF75120468602BFED4`

---

## 四、游戏内实机验证

1. **道馆日弘门六面神石 `(416, 179)`**：
   - 角色传送到 `(416, 183)` 查看；
   - 神石位于石盘正中央圆环孔位，不再偏上，与周围六块石板形成完美对称辐射。
2. **沙漠土城西门六面神石 `(63, 195)`**：
   - 角色传送到 `(66, 196)` 查看；
   - 用户原先截图中空缺的光秃基座上现已成功生成「六面神石」，符文完全贴合石基并附带发光特效。

## 五、补充过程素材

2026-10-08 从临时工作区筛选出的道馆/沙漠土城早期运行截图，以及 NPC56 石阵对齐探索图，保存在 [`docs/screenshots/hexastones/process-review-2026-10-08/`](screenshots/hexastones/process-review-2026-10-08/README.md)。这些只作开发过程补充：早期截图的名称标签不可可靠辨读，NPC56 对齐图缺少地图/坐标/生成参数，均不替代本报告的最终 36 点验收证据。

临时 WIL 资源帧的逐帧来源校验与收录清单见 [`docs/evidence/zircon-temp-audit-review-2026-10-08.md`](evidence/zircon-temp-audit-review-2026-10-08.md)。
