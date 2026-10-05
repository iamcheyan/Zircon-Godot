# 全套城防守卫与地域特色侍卫系统实施方案 (2026-10-04)

## 1. 背景与历史考证

在传奇 3（Mir3）经典世界观与原版客户端设定中，各大主城与地域的守卫体系绝非单一粗糙的大汉大刀守卫，而是由**多兵种、多地域特色**的防御卫队组成：

1. **道馆带刀侍卫（`TownGuard` / 锦衣卫护卫）**：
   - **历史考据**：道馆（Lost Paradise / 道观）作为道家与江湖重地，守卫身着深蓝色绫罗战袍、腰佩长刀（双手横刀于腰际）、头戴双翎战盔，形如大内锦衣卫，英姿飒爽；
   - **美术素材**：`Mon_12.wil` Shape 4（帧 4000+，`MonsterImage.TownGuard`）。
2. **白日门森林护卫（`ForestGuard` / 白日门道家侍卫）**：
   - **历史考据**：白日门作为天尊隐修之所与道术源泉，城门与天尊堂前由身披道家战铠、内着劲装的森林卫士值守；
   - **美术素材**：`Mon_12.wil` Shape 3（帧 3000+，`MonsterImage.ForestGuard`）。
3. **沙漠卫士（`SandGuard` / 绿洲绿洲风情卫士）**：
   - **历史考据**：盟重荒漠与绿洲（Oasis）炎热干燥，卫士采用沙漠特制斗篷、沙地护甲与专属战戈；
   - **美术素材**：`Mon_12.wil` Shape 5（帧 5000+，`MonsterImage.SandGuard`）。
4. **城防弓箭手守卫（`ArcherGuard` / `TownArcher`）**：
   - **历史考据**：比奇大城与盟重土城的城墙、箭楼与高台哨卡上，由百步穿杨的神射手（弓箭守卫）驻守，对引怪入城的野怪及红名玩家发动致命超远程箭矢射击（原版 `RC_ARCHERPOLICE = 20`）；
   - **美术素材**：`Mon_9.wil` Shape 6（帧 6000+，`MonsterImage.ArcherGuard`）。
5. **经典城门大刀守卫（`Guard`）**：
   - **美术素材**：`Mon_3.wil` Shape 6（帧 6000+，`MonsterImage.Guard`），重盔重甲、手执巨型长柄大关刀，近战瞬移跳劈秒杀红名。

---

## 2. 数据库实体规划 (MonsterInfo 体系)

将全套守卫纳入**「系统机制伴生实体（System Companion Entities）」双轨保护白名单**，连续编排在核心号段（Index 1 ~ 5）：

| Index | 英文名称 (MonsterName) | 中文名称 | 模型 (Image) | 素材库与Shape | AI类型 | 职责与机制 |
|:---:|:---|:---|:---|:---|:---:|:---|
| **1** | `Guard` | 大刀守卫 | `Guard` | `Mon_3.wil`, Shape 6 | `-1` | 比奇/盟重/毒蛇城门大砍刀守卫，近战瞬移跳劈秒杀红名 |
| **2** | `TownGuard` | 道馆带刀护卫 | `TownGuard` | `Mon_12.wil`, Shape 4 | `-1` | 道馆（Lost Paradise）专属锦衣卫风采蓝袍侍卫，横刀于腰，秒杀红名 |
| **3** | `ForestGuard` | 白日门森林护卫 | `ForestGuard` | `Mon_12.wil`, Shape 3 | `-1` | 白日门天尊堂与城门专属森林道家侍卫，秒杀红名 |
| **4** | `SandGuard` | 沙漠卫士 | `SandGuard` | `Mon_12.wil`, Shape 5 | `-1` | 绿洲（Oasis）专属沙漠斗篷护卫，秒杀红名 |
| **5** | `ArcherGuard` | 弓箭手守卫 | `ArcherGuard` | `Mon_9.wil`, Shape 6 | `-3` | 比奇城墙/盟重土城箭楼远程神射手，15格超远视野射击红名与怪 |

---

## 3. 地图守卫点位编排 (GuardInfo 体系)

### 3.1 地图归属与模型切换
1. **道馆（Map 1 / Lost Paradise）**：
   - 将原 Map 1 下现存的全部 8 个守卫记录，由 `Guard` 全部切换为 **`TownGuard` (Index 2)**。
2. **白日门（Map 74 / 011）**：
   - 将原 Map 74 下现存的全部 4 个守卫记录，由 `Guard` 全部切换为 **`ForestGuard` (Index 3)**。
3. **绿洲（Map 4 / Oasis）**：
   - 将原 Map 4 下现存的全部 7 个守卫记录，由 `Guard` 全部切换为 **`SandGuard` (Index 4)**。
4. **比奇城（Map 0）与盟重土城（MUD3 MapInfo 文件名 `5`）**：
   - 保持城门大刀守卫（`Guard`，Index 1）；
   - 在各城门箭垛与四角箭楼高处新增部署 **`ArcherGuard` (Index 5)**。`02` 是银杏山谷，不能用作盟重地图；地图资源替换后必须按当前地图重新选点，不能照搬旧坐标。

---

## 4. 服务端弓箭手守卫 AI (`ArcherGuard.cs`) 架构

### 4.1 核心属性与规则
- **站桩防守**：`CanMove => false`，站在城墙哨位不动；
- **无敌守卫**：`Attacked(...) => 0`，不被常规攻击消灭；
- **目标识别**：仅针对红名玩家（`PKPoint >= 200`）和非宠物野外怪物；
- **超远射程**：`ViewRange = 15`，`AttackRange = 15`；
- **远程射击协议**：向视野内客户端广播 `S.ObjectRangeAttack`，客户端自动触发发射 `1070` 号抛物线箭矢投射物（`MirProjectile`），并在命中延迟后结算致死伤害。
