# 全图六面神石 36 处点位实机巡检与底座对齐终验报告

- **日期**：2026-10-08
- **环境**：Linux Debian 82 本地（Godot-Mono 4.3 + ServerCore 7000）
- **真值依据**：原版 MUD3 官方 `Merchant.txt` 权威配置（33处）+ EI 3.0 / 地图底座几何中心（3处）
- **数据库镜像 SHA256**：`e3a6db7e46ecb8b5f7f7a11f401c7c6138aa9d15d98c4b972271571a9e9a3dd2`（5 处强一致）

---

## 1. 概述与核心修复总结

针对用户提出的：
1. **沙巴克皇宫内墙底座漏配六面神石**（原图坐标 `[51, 222]`）；
2. **六面神石命名原版对齐**（“六角神石”统一更正为官方译名“六面神石”，内部 `Hexa Holy Stone`）；
3. **神石与地表石阵底座圆盘像素级严丝合缝对齐**；
4. **沙巴克城超界无效点位剔除**（清除攻城大地图 600x600 遗留的超界坐标 `(294, 539)` 与 `(49, 566)`）；
5. **实机自动化全量 36 处巡检取证**（在真实运行游戏视口内逐一传送，确认底座与神石完全居中，拍照留档）。

### 关键架构缺陷修复：`ShowMapChanged` 异步滞后导致的 NPC 吞包
在实机巡检测试过程中，发现了长期隐藏的客户端渲染时序缺陷：
- **表现**：切图时部分点位（如道馆、盟重东等）虽然服务端已派发 `ObjectNPC` 包、日志显示 `添加物体: NPC '六面神石'`，但在首帧视口截图中神石完全不见。
- **根本原因**：`OnMapChanged` 历史遗留了 `CallDeferred(nameof(ShowMapChanged))`。当客户端收到 `MapChanged` 并在同批次 TCP 数据流中收到紧随其后的 `ObjectNPC` 时，`OnObjectNPC` 在主线程立即创建了节点加入 `_objects`；随后进入 Deferred 调用阶段，`ShowMapChanged()` 执行 `LoadPlayerMap()`，其内部的 `_objects.Clear()` 将刚刚收到的新地图 NPC 全部销毁，导致新地图首屏 NPC 丢失。
- **修复措施**：将 `OnMapChanged` 的 `ShowMapChanged()` 改为同步立即执行，确保切图清空旧对象发生在接收新对象之前；同时巡检工具增加了实机状态双重就绪校验（地图匹配 + 角色到达 + NPC 实例在场后触发渲染刷新）。

---

## 2. 全图 36 处六面神石配置与实机验证清单

| 编号 | 地图代码 | 地图中文名 | 神石坐标 (X, Y) | 玩家视口坐标 (X, Y) | 造型 | 脚本入口 | 验证状态 | 实机截图路径 |
|:---:|:---|:---|:---:|:---:|:---:|:---|:---:|:---|
| 01 | 0 | 比奇城 | (498, 463) | (499, 464) | 56 | BT Teleporter | 完美对齐 | `docs/screenshots/hexastones/01_0_498_463_Bichon_South.png` |
| 02 | 0 | 比奇城 | (507, 313) | (508, 314) | 56 | BT Teleporter | 完美对齐 | `docs/screenshots/hexastones/02_0_507_313_Bichon_East.png` |
| 03 | 0 | 比奇城 | (370, 336) | (371, 337) | 56 | BT Teleporter | 完美对齐 | `docs/screenshots/hexastones/03_0_370_336_Bichon_North.png` |
| 04 | 0 | 比奇城 | (379, 444) | (380, 445) | 56 | BT Teleporter | 完美对齐 | `docs/screenshots/hexastones/04_0_379_444_Bichon_West.png` |
| 05 | 01 | 边境城市 | (456, 216) | (457, 217) | 56 | BC Teleporter | 完美对齐 | `docs/screenshots/hexastones/05_01_456_216_BorderTown_North.png` |
| 06 | 01 | 边境城市 | (411, 287) | (412, 288) | 56 | BC Teleporter | 完美对齐 | `docs/screenshots/hexastones/06_01_411_287_BorderTown_West.png` |
| 07 | 01 | 边境城市 | (463, 356) | (464, 357) | 56 | BC Teleporter | 完美对齐 | `docs/screenshots/hexastones/07_01_463_356_BorderTown_East.png` |
| 08 | 02 | 银杏山谷 | (249, 144) | (250, 145) | 56 | BT Teleporter | 完美对齐 | `docs/screenshots/hexastones/08_02_249_144_GinkgoValley.png` |
| 09 | 1 | 道馆 | (416, 179) | (417, 180) | 56 | LP Teleporter | 完美对齐 | `docs/screenshots/hexastones/09_1_416_179_DaoGwan_RiHong.png` |
| 10 | 2 | 毒蛇山谷 | (306, 244) | (307, 245) | 56 | BV Teleporter | 完美对齐 | `docs/screenshots/hexastones/10_2_306_244_SnakeValley_South.png` |
| 11 | 2 | 毒蛇山谷 | (314, 193) | (315, 194) | 56 | BV Teleporter | 完美对齐 | `docs/screenshots/hexastones/11_2_314_193_SnakeValley_North.png` |
| 12 | 3 | 沙巴克城 (正门城墙) | (222, 159) | (221, 158) | 56 | SK Teleporter | 完美对齐 | `docs/screenshots/hexastones/12_3_222_159_Sabuk_MainGate.png` |
| 13 | 3 | 沙巴克城 (西门) | (71, 140) | (72, 141) | 56 | SK Teleporter | 完美对齐 | `docs/screenshots/hexastones/13_3_71_140_Sabuk_West.png` |
| 14 | 3 | 沙巴克城 (皇宫内墙) | (51, 222) | (52, 223) | 56 | SK1 Teleporter | 完美对齐 | `docs/screenshots/hexastones/14_3_51_222_Sabuk_Palace_Inner.png` |
| 15 | 4 | 绿洲 | (435, 83) | (436, 84) | 56 | NV Teleporter | 完美对齐 | `docs/screenshots/hexastones/15_4_435_83_Oasis.png` |
| 16 | 41 | 诺玛沙漠 | (184, 136) | (185, 137) | 56 | II Teleporter | 完美对齐 | `docs/screenshots/hexastones/16_41_184_136_NumaDesert.png` |
| 17 | 5 | 沙漠土城 (南) | (204, 289) | (205, 290) | 56 | MW Teleporter | 完美对齐 | `docs/screenshots/hexastones/17_5_204_289_MudFortress_South.png` |
| 18 | 5 | 沙漠土城 (内城) | (112, 177) | (113, 178) | 56 | MW1 Teleporter | 完美对齐 | `docs/screenshots/hexastones/18_5_112_177_MudFortress_Inner.png` |
| 19 | 5 | 沙漠土城 (西门) | (63, 195) | (64, 196) | 56 | MW Teleporter | 完美对齐 | `docs/screenshots/hexastones/19_5_63_195_MudFortress_WestGate.png` |
| 20 | 5 | 沙漠土城 (东门) | (227, 128) | (228, 129) | 56 | MW Teleporter | 完美对齐 | `docs/screenshots/hexastones/20_5_227_128_MudFortress_EastGate.png` |
| 21 | 6 | 沙漠 (蚂蚁洞) | (273, 731) | (274, 732) | 56 | MW Teleporter | 完美对齐 | `docs/screenshots/hexastones/21_6_273_731_Desert_AntCave.png` |
| 22 | 74 | 盟重县 (东) | (349, 329) | (350, 330) | 56 | MW Teleporter | 完美对齐 | `docs/screenshots/hexastones/22_74_349_329_Mongchon_East.png` |
| 23 | 74 | 盟重县 (西) | (271, 267) | (272, 268) | 56 | MW Teleporter | 完美对齐 | `docs/screenshots/hexastones/23_74_271_267_Mongchon_West.png` |
| 24 | 75 | 石阁庙 | (184, 90) | (185, 91) | 56 | MW Teleporter | 完美对齐 | `docs/screenshots/hexastones/24_75_184_90_SukGak_Temple.png` |
| 25 | 8 | 潘夜岛 (村庄) | (288, 241) | (289, 242) | 57 | FV Teleporter | 完美对齐 | `docs/screenshots/hexastones/25_8_288_241_Banya_Village.png` |
| 26 | 8 | 潘夜岛 (西岸) | (113, 463) | (114, 464) | 57 | FV Teleporter | 完美对齐 | `docs/screenshots/hexastones/26_8_113_463_Banya_WestCoast.png` |
| 27 | 8 | 潘夜岛 (东岸) | (668, 388) | (669, 389) | 57 | FV Teleporter | 完美对齐 | `docs/screenshots/hexastones/27_8_668_388_Banya_EastCoast.png` |
| 28 | 8 | 潘夜岛 (南岸) | (448, 579) | (449, 580) | 57 | FV Teleporter | 完美对齐 | `docs/screenshots/hexastones/28_8_448_579_Banya_SouthCoast.png` |
| 29 | 8 | 潘夜岛 (村北) | (424, 239) | (425, 240) | 57 | FV Teleporter | 完美对齐 | `docs/screenshots/hexastones/29_8_424_239_Banya_North.png` |
| 30 | 81 | 流放岛 | (129, 265) | (130, 266) | 57 | LL Teleporter | 完美对齐 | `docs/screenshots/hexastones/30_81_129_265_RedZone_ExileIsland.png` |
| 31 | 12 | 旧潘夜岛 | (190, 265) | (191, 266) | 57 | BI Teleporter | 完美对齐 | `docs/screenshots/hexastones/31_12_190_265_OldBanyaIsland.png` |
| 32 | D1110 | 潘夜神殿1层 (厅1) | (15, 18) | (16, 19) | 57 | Teleport Banya Hall | 完美对齐 | `docs/screenshots/hexastones/32_D1110_15_18_BanyaTemple_Hall1.png` |
| 33 | D1110 | 潘夜神殿1层 (厅2) | (28, 31) | (29, 32) | 57 | Teleport Banya Hall | 完美对齐 | `docs/screenshots/hexastones/33_D1110_28_31_BanyaTemple_Hall2.png` |
| 34 | D11031 | 潘夜神殿3层西部 | (199, 257) | (200, 258) | 57 | Teleport Banya Hall | 完美对齐 | `docs/screenshots/hexastones/34_D11031_199_257_BanyaTemple_3West.png` |
| 35 | D1105 | 潘夜神殿5层 | (219, 99) | (220, 100) | 57 | Teleport Banya Hall | 完美对齐 | `docs/screenshots/hexastones/35_D1105_219_99_BanyaTemple_5.png` |
| 36 | D1115 | 潘夜神殿8层 | (358, 353) | (359, 354) | 57 | Teleport Banya Hall | 完美对齐 | `docs/screenshots/hexastones/36_D1115_358_353_BanyaTemple_8.png` |

---

## 3. 重点点位实机取证分析

### 3.1 沙巴克皇宫内墙底座（点位 14，`[51, 222]`）
- **背景**：用户截图中沙巴克皇宫内墙（坐标 `[51, 222]`）有一处明显的六角形石阵地基底座，但原版孤立 NPC #279 坐标缺失。
- **实测表现**：NPC #279 精确绑定至该底座几何中心 `(51, 222)`，实机截图 `14_3_51_222_Sabuk_Palace_Inner.png` 显示：六面神石矗立在底座圆心，底部的金色符文环流与地盘环状花纹严丝合缝重合。

### 3.2 沙巴克城正门城墙（点位 12，`[222, 159]`）
- **背景**：MUD3 原版记录的正门传送石位于沙巴克城门台阶与外城交接处 `(222, 159)`。因其东南方相邻格 `(223, 160)` 属于城墙垛口碰撞块（无法落脚），传统传送算法在此处会被服务端拒绝。
- **调整**：实机巡检将测试角色站位定位在西北侧走道 `(221, 158)`，截图 `12_3_222_159_Sabuk_MainGate.png` 完美展示了城墙走道尽头台阶上的神石。

### 3.3 道馆日弘门（点位 09，`[416, 179]`）
- **实机表现**：截图 `09_1_416_179_DaoGwan_RiHong.png` 显示，神石位于道馆东北日弘门外的树荫石阵圆环正中，神石闪动时金色符文环流与地盘无任何偏移或倾斜。

### 3.4 潘夜岛与地牢系列（点位 25..36）
- **造型区分**：潘夜岛及潘夜神殿地牢采用原版 Image 57 特殊图腾石柱与神殿阵列造型。
- **实测表现**：所有神殿大厅（D1110、D11031、D1105、D1115）的法阵石台中心坐标与神石几何中心完全一致，光柱直冲地牢顶部，无缝还原原版传奇3意境。

---

## 4. 数据库一致性校验

写入完成后已严格执行跨目录同步校验，五处数据库文件的 SHA256 校验和完全一致：
```
e3a6db7e46ecb8b5f7f7a11f401c7c6138aa9d15d98c4b972271571a9e9a3dd2  /home/tetsuya/development/zircon/System.db
e3a6db7e46ecb8b5f7f7a11f401c7c6138aa9d15d98c4b972271571a9e9a3dd2  /home/tetsuya/development/zircon/Debug/ServerCore/Database/System.db
e3a6db7e46ecb8b5f7f7a11f401c7c6138aa9d15d98c4b972271571a9e9a3dd2  /home/tetsuya/development/Debug/ServerCore/Database/System.db
e3a6db7e46ecb8b5f7f7a11f401c7c6138aa9d15d98c4b972271571a9e9a3dd2  /home/tetsuya/mir2ei/Data/System.db
e3a6db7e46ecb8b5f7f7a11f401c7c6138aa9d15d98c4b972271571a9e9a3dd2  /home/tetsuya/mir2ei/Database/System.db
```
经 `ClassicMagicFixer hexastone --dry-run` 回读校验，更新数、新建数、清理孤立数均为 0，全部 36 处神石状态均为 `[一致]`。
