# 经典纯净物品清单与全谱素材映射规范 (Classic Items & Asset Mapping Specification)

本文档由 Zircon 客户端与服务端双向对齐数据库（`System.db` 版本 `2026.10.04.1`）自动衍生生成。
详细列出经 17173 官方资料站和 `canonical_identity.json` 权威基准清洗后的**全部经典物品**及其在客户端底层资源（背包图标、地面掉落、纸娃娃换装库）的完整映射关系，供数值策划、UI 呈现、装备渲染及后续扩展查阅。

## 目录
1. [数据清洗背景与权威对齐原则](#一数据清洗背景与权威对齐原则)
2. [素材映射体系与换装公式规范](#二素材映射体系与换装公式规范)
3. [分门别类完整物品全谱映射清单](#三分门别类完整物品全谱映射清单)
   - [武器 (Weapon) (47件)](#weapon)
   - [衣服/盔甲 (Armour) (104件)](#armour)
   - [头盔 (Helmet) (13件)](#helmet)
   - [项链 (Necklace) (55件)](#necklace)
   - [手镯 (Bracelet) (49件)](#bracelet)
   - [戒指 (Ring) (61件)](#ring)
   - [鞋靴 (Shoes) (11件)](#shoes)
   - [消耗品/药水 (Consumable) (8件)](#consumable)
   - [矿石 (Ore) (9件)](#ore)
   - [暗石/强化石 (DarkStone) (2件)](#darkstone)
   - [照明道具 (Torch) (2件)](#torch)
   - [食材肉品 (Meat) (1件)](#meat)
   - [货币 (Currency) (3件)](#currency)
   - [杂物与任务信物 (Nothing) (65件)](#nothing)
4. [客户端资源文件物理分布表](#四客户端资源文件物理分布表)

---

## 一、数据清洗背景与权威对齐原则

为解决早期 Zircon 数据库混入私服怪物、刺客职业、变态武器以及非经典雪原/诺玛后期散件的问题，项目组基于 **17173 传奇3官方资料站** 建立了权威经典白名单：
- **剔除非经典装备**：剔除刺客双手刃、雪原高阶神圣装备、私服改版强化装等 752 条冗余记录。
- **保留经典骨干**：收敛至三职业（战士、法师、道士）最核心的武器、盔甲、首饰套系（沃玛套、祖玛套、赤月套、六大重装等），并保留基础货币、经典矿石、药水与新手任务信物。
- **数据一致性保证**：当前数据库保持服务端（`Debug/ServerCore/Database/System.db`）与客户端（`/home/tetsuya/mir2ei/Data/System.db`）等 4 处镜像 MD5 严格一致，彻底消除因索引错位导致的虚空物品或背包卡死。

### 物品分类数量统计表

| 类别分类 | 英文标识 | 物品数量 | 占格特性 | 纸娃娃呈现 |
| :--- | :--- | :---: | :---: | :---: |
| 武器 | Weapon | 47 | 1x3 / 2x3 | 是 (M_Weapon / WM_Weapon) |
| 衣服/盔甲 | Armour | 104 | 2x2 | 是 (M_Hum / WM_Hum) |
| 头盔 | Helmet | 13 | 1x1 / 2x2 | 是 (M_Helmet / WM_Helmet) |
| 项链 | Necklace | 55 | 1x1 | 否 (仅背包图标与地面) |
| 手镯 | Bracelet | 49 | 1x1 | 否 (仅背包图标与地面) |
| 戒指 | Ring | 61 | 1x1 | 否 (仅背包图标与地面) |
| 鞋靴 | Shoes | 11 | 1x1 | 否 (仅背包图标与地面) |
| 消耗品/药水 | Consumable | 8 | 1x1 | 否 (仅背包图标与地面) |
| 矿石 | Ore | 9 | 1x1 | 否 (仅背包图标与地面) |
| 暗石/强化石 | DarkStone | 2 | 1x1 | 否 (仅背包图标与地面) |
| 照明道具 | Torch | 2 | 1x1 | 否 (仅背包图标与地面) |
| 食材肉品 | Meat | 1 | 1x1 | 否 (仅背包图标与地面) |
| 货币 | Currency | 3 | 1x1 | 否 (数值形式存储) |
| 杂物与任务信物 | Nothing | 65 | 1x1 | 否 (仅背包图标与地面) |
| **总计** | - | **430** | - | - |

---

## 二、素材映射体系与换装公式规范

在传奇3客户端中，一件装备在不同场景下由不同的图形库和帧号进行呈现：

### 1. 背包与商店图标 (`StoreItem.Zl`)
- **呈现位置**：玩家背包、快捷腰带栏、NPC 商店买卖列表、仓库。
- **读取字段**：`ItemInfo.Image`。
- **资源库文件**：`Data/StoreItem.Zl`（包含 8590 帧图样）。
- **尺寸与占格计算**：根据 `StoreItem.Zl` 实际贴图宽高除以 36 向上取整。严禁使用 1440 帧的旧 `inventory.wil`，否则高级装备索引落空退化为 1x1 单格引发背包重叠重绘灾难。

### 2. 地面掉落外观 (`Ground.Zl`)
- **呈现位置**：怪物被击杀爆落、玩家主动丢弃（Drop）在地图地面上。
- **读取字段**：`ItemInfo.Shape`。
- **资源库文件**：`Data/Ground.Zl`。
- **表现形式**：地面静态掉落物，包含金币堆、剑、衣服包袱、瓶罐等各种物理外观。

### 3. 纸娃娃全身换装系统 (Paperdoll System)
当玩家在角色装备栏（Character Dialog）穿上装备时，游戏画面中的角色模型、攻击施法动作、行走跑动帧将实时改变：

#### (1) 衣服/盔甲 (Armour) 换装映射规则：
$$LibraryKey = \lfloor\text{ArmourShape} / 11\rfloor + (\text{Female} ? 5000 : 0)$$
$$\text{ArmourFrame} = \text{DrawFrame} + (\text{ArmourShape} \bmod 11) \times \text{ArmourShapeOffSet} + \text{ArmourShift}$$
- **库分段**：
  - `Shape 0..10`：男角色使用 `M_Hum.Zl`，女角色使用 `WM_Hum.Zl`
  - `Shape 11..21`：男角色使用 `M_HumEx1.Zl`，女角色使用 `WM_HumEx1.Zl`
  - `Shape 22..32`：男角色使用 `M_HumEx2.Zl`，女角色使用 `WM_HumEx2.Zl`
  - `Shape 33..43`：男角色使用 `M_HumEx3.Zl`，女角色使用 `WM_HumEx3.Zl`
  - `Shape 44..54`：男角色使用 `M_HumEx4.Zl`，女角色使用 `WM_HumEx4.Zl`
  - 每个库可容纳 11 套服装动作动画（每套包含站立、跑步、攻击、施法、死亡等全套动作）。

#### (2) 武器 (Weapon) 换装映射规则：
$$LibraryKey = \lfloor\text{WeaponShape} / 10\rfloor + (\text{Female} ? 5000 : 0)$$
$$\text{WeaponFrame} = \text{DrawFrame} + (\text{WeaponShape} \bmod 10) \times \text{WeaponShapeOffSet}$$
- **库分段**：
  - `Shape 0..9`：男角色使用 `M_Weapon1.Zl`，女角色使用 `WM_Weapon1.Zl`
  - `Shape 10..19`：男角色使用 `M_Weapon2.Zl`，女角色使用 `WM_Weapon2.Zl`
  - `Shape 20..29`：男角色使用 `M_Weapon3.Zl`，女角色使用 `WM_Weapon3.Zl`
  - 每个库可容纳 10 把独立武器动作动画。

#### (3) 头盔 (Helmet) 换装映射规则：
$$LibraryKey = \lfloor(\text{HelmetShape} - 1) / 10\rfloor + (\text{Female} ? 5000 : 0)$$
$$\text{HelmetFrame} = \text{DrawFrame} + ((\text{HelmetShape} - 1) \bmod 10) \times \text{ArmourShapeOffSet} + \text{ArmourShift}$$
- **库分段**：
  - `Shape 1..10`：男角色使用 `M_Helmet1.Zl`，女角色使用 `WM_Helmet1.Zl`
  - `Shape 11..20`：男角色使用 `M_Helmet2.Zl`，女角色使用 `WM_Helmet2.Zl`

---

## 三、分门别类完整物品全谱映射清单

### <a id="weapon"></a>3.1 武器 (Weapon) (47件)

| 序号 | 中文名 | 英文名 | 需求等级 | 需求职业 | 地面(Ground) | 图标(StoreItem) | 纸娃娃武器库及子序号 | 重量 | 持久 | 价格 |
| :---: | :--- | :--- | :---: | :---: | :---: | :---: | :--- | :---: | :---: | :---: |
| 126 | **木剑** | Wood Sword | 1 | WarWizTao | 101 | 1042 | `M_Weapon11.Zl / WM_Weapon11.Zl (Sub #1)` | 5 | 4000 | 50 |
| 174 | **匕首** | Dagger | 3 | WarWizTao | 103 | 1045 | `M_Weapon11.Zl / WM_Weapon11.Zl (Sub #3)` | 7 | 6000 | 100 |
| 176 | **短剑** | Iron Sword | 7 | WarWizTao | 105 | 1043 | `M_Weapon11.Zl / WM_Weapon11.Zl (Sub #5)` | 10 | 10000 | 1000 |
| 178 | **乌木剑** | Arisu Wood Sword | 7 | WarWizTao | 8 | 1042 | `M_Weapon1.Zl / WM_Weapon1.Zl (Sub #8)` | 5 | 7000 | 1000 |
| 184 | **青铜斧** | Bronze Axe | 14 | WarWizTao | 102 | 1060 | `M_Weapon11.Zl / WM_Weapon11.Zl (Sub #2)` | 20 | 20000 | 3500 |
| 186 | **海魂** | Trident | 14 | WarWizTao | 100 | 1080 | `M_Weapon11.Zl / WM_Weapon11.Zl (Sub #0)` | 10 | 9000 | 3500 |
| 187 | **半月** | Scimitar | 14 | WarWizTao | 104 | 1100 | `M_Weapon11.Zl / WM_Weapon11.Zl (Sub #4)` | 13 | 12000 | 3500 |
| 201 | **偃月** | War Spear | 18 | WarWizTao | 5 | 1081 | `M_Weapon1.Zl / WM_Weapon1.Zl (Sub #5)` | 11 | 10000 | 5000 |
| 202 | **降魔** | War Blade | 18 | WarWizTao | 29 | 1101 | `M_Weapon3.Zl / WM_Weapon3.Zl (Sub #9)` | 15 | 14000 | 5000 |
| 306 | **修罗** | Power Axe | 20 | WarWizTao | 11 | 1065 | `M_Weapon2.Zl / WM_Weapon2.Zl (Sub #1)` | 30 | 22000 | 6000 |
| 328 | **双刃剑** | Bone Claymore | 26 | WarWizTao | 26 | 1044 | `M_Weapon3.Zl / WM_Weapon3.Zl (Sub #6)` | 33 | 22000 | 8500 |
| 329 | **魔杖** | Mage Staff | 26 | WarWizTao | 15 | 1082 | `M_Weapon2.Zl / WM_Weapon2.Zl (Sub #5)` | 10 | 13000 | 10000 |
| 330 | **银蛇** | Serpent Sword | 26 | WarWizTao | 17 | 1102 | `M_Weapon2.Zl / WM_Weapon2.Zl (Sub #7)` | 21 | 22000 | 10000 |
| 357 | **凝霜** | Sword Of Purification | 25 | WarWizTao | 26 | 1210 | `M_Weapon3.Zl / WM_Weapon3.Zl (Sub #6)` | 33 | 20000 | 8000 |
| 363 | **命运之刃** | Enchanted Blade | 29 | WarWizTao | 12 | 1067 | `M_Weapon2.Zl / WM_Weapon2.Zl (Sub #2)` | 47 | 24000 | 15000 |
| 402 | **祖玛裁决之杖** | Zuma Judgement Mace | 33 | WarWizTao | 14 | 1302 | `M_Weapon2.Zl / WM_Weapon2.Zl (Sub #4)` | 80 | 32000 | 23000 |
| 403 | **祖玛无极棍** | Zuma Runed Staff | 33 | WarWizTao | 18 | 1260 | `M_Weapon2.Zl / WM_Weapon2.Zl (Sub #8)` | 29 | 24000 | 23000 |
| 404 | **祖玛骨玉权杖** | Zuma Dragon Bone Staff | 33 | WarWizTao | 16 | 1193 | `M_Weapon2.Zl / WM_Weapon2.Zl (Sub #6)` | 14 | 15000 | 23000 |
| 428 | **炼狱** | Great Axe | 33 | WarWizTao | 4 | 1066 | `M_Weapon1.Zl / WM_Weapon1.Zl (Sub #4)` | 70 | 30000 | 23000 |
| 429 | **血饮** | Mage Sword | 33 | WarWizTao | 27 | 1083 | `M_Weapon3.Zl / WM_Weapon3.Zl (Sub #7)` | 13 | 20000 | 23000 |
| 430 | **无名刀** | Valor Blade | 33 | WarWizTao | 108 | 1062 | `M_Weapon11.Zl / WM_Weapon11.Zl (Sub #8)` | 25 | 24000 | 23000 |
| 439 | **青铜剑** | Bronze Sword | 5 | WarWizTao | 26 | 1043 | `M_Weapon3.Zl / WM_Weapon3.Zl (Sub #6)` | 9 | 8000 | 500 |
| 494 | **诺玛族修罗** | Numa Power Axe | 27 | WarWizTao | 11 | 1250 | `M_Weapon2.Zl / WM_Weapon2.Zl (Sub #1)` | 50 | 26000 | 9000 |
| 495 | **诺玛族魔杖** | Numa Mage Staff | 27 | WarWizTao | 15 | 1280 | `M_Weapon2.Zl / WM_Weapon2.Zl (Sub #5)` | 12 | 13000 | 9000 |
| 497 | **诅咒银蛇** | Cursed Serpent Sword | 27 | WarWizTao | 17 | 1231 | `M_Weapon2.Zl / WM_Weapon2.Zl (Sub #7)` | 23 | 22000 | 9000 |
| 507 | **潘夜命运之刃** | Enchanted Blade Of Banya | 31 | WarWizTao | 12 | 1170 | `M_Weapon2.Zl / WM_Weapon2.Zl (Sub #2)` | 61 | 28000 | 23000 |
| 509 | **潘夜血饮** | Mage Sword Of Banya | 34 | WarWizTao | 27 | 1083 | `M_Weapon3.Zl / WM_Weapon3.Zl (Sub #7)` | 15 | 16000 | 25000 |
| 510 | **潘夜无极棍** | Runed Staff Of Banya | 34 | WarWizTao | 18 | 1263 | `M_Weapon2.Zl / WM_Weapon2.Zl (Sub #8)` | 33 | 24000 | 25000 |
| 523 | **潘夜银蛇** | Serpent Sword Of Banya | 31 | WarWizTao | 17 | 1233 | `M_Weapon2.Zl / WM_Weapon2.Zl (Sub #7)` | 27 | 22000 | 20000 |
| 524 | **潘夜魔杖** | Banya Mage Staff | 31 | WarWizTao | 15 | 1281 | `M_Weapon2.Zl / WM_Weapon2.Zl (Sub #5)` | 14 | 14000 | 20000 |
| 547 | **井中月** | Forged Scimitar | 35 | WarWizTao | 13 | 1068 | `M_Weapon2.Zl / WM_Weapon2.Zl (Sub #3)` | 54 | 26000 | 28000 |
| 548 | **无极棍** | Runed Staff | 38 | WarWizTao | 18 | 1103 | `M_Weapon2.Zl / WM_Weapon2.Zl (Sub #8)` | 33 | 24000 | 40000 |
| 549 | **裁决之杖** | Judgement Mace | 38 | WarWizTao | 14 | 1069 | `M_Weapon2.Zl / WM_Weapon2.Zl (Sub #4)` | 90 | 34000 | 40000 |
| 550 | **骨玉权杖** | Dragon Bone Staff | 38 | WarWizTao | 16 | 1084 | `M_Weapon2.Zl / WM_Weapon2.Zl (Sub #6)` | 15 | 17000 | 40000 |
| 621 | **旋风流星刀** | Fury Blade | 45 | WarWizTao | 21 | 1049 | `M_Weapon3.Zl / WM_Weapon3.Zl (Sub #1)` | 87 | 35000 | 60000 |
| 622 | **飞魂魔刃** | Twisted Blade Of Souls | 45 | WarWizTao | 24 | 1061 | `M_Weapon3.Zl / WM_Weapon3.Zl (Sub #4)` | 18 | 19000 | 60000 |
| 632 | **封魔剑** | Celestial Blade | 45 | WarWizTao | 0 | 1046 | `M_Weapon1.Zl / WM_Weapon1.Zl (Sub #0)` | 36 | 26000 | 60000 |
| 635 | **霹雷** | Sword Of Abyss | 0 | WarWizTao | 37 | 1074 | `M_Weapon4.Zl / WM_Weapon4.Zl (Sub #7)` | 0 | 35000 | 80000 |
| 636 | **铁轮** | Glaive Of Doom | 0 | WarWizTao | 39 | 1086 | `M_Weapon4.Zl / WM_Weapon4.Zl (Sub #9)` | 0 | 18000 | 80000 |
| 637 | **逍遥扇** | Warden's Fan Of Obedience | 0 | WarWizTao | 38 | 1105 | `M_Weapon4.Zl / WM_Weapon4.Zl (Sub #8)` | 0 | 25000 | 80000 |
| 683 | **破山剑** | Apocalypse | 0 | WarWizTao | 7 | 1041 | `M_Weapon1.Zl / WM_Weapon1.Zl (Sub #7)` | 0 | 35000 | 100000 |
| 684 | **天神法杖** | Heavenly Staff Of Immortality | 0 | WarWizTao | 23 | 1047 | `M_Weapon3.Zl / WM_Weapon3.Zl (Sub #3)` | 0 | 19000 | 100000 |
| 816 | **鹤嘴锄** | Pick Axe | 20 | All | 6 | 1040 | `M_Weapon1.Zl / WM_Weapon1.Zl (Sub #6)` | 10 | 10000 | 700 |
| 817 | **风之鹤嘴锄** | Pick Axe Of Wind | 30 | All | 6 | 1048 | `M_Weapon1.Zl / WM_Weapon1.Zl (Sub #6)` | 21 | 30000 | 30000 |
| 819 | **屠龙** | Obsidian Giant Blade | 0 | WarWizTao | 3 | 1070 | `M_Weapon1.Zl / WM_Weapon1.Zl (Sub #3)` | 0 | 36000 | 80000 |
| 820 | **龙纹剑** | Divine Blade Of Judgement | 0 | WarWizTao | 19 | 1104 | `M_Weapon2.Zl / WM_Weapon2.Zl (Sub #9)` | 0 | 26000 | 80000 |
| 825 | **嗜魂法杖** | Muk's Staff Of Retribution | 36 | WarWizTao | 2 | 1085 | `M_Weapon1.Zl / WM_Weapon1.Zl (Sub #2)` | 16 | 19000 | 30000 |


### <a id="armour"></a>3.2 衣服/盔甲 (Armour) (104件)

| 序号 | 中文名 | 英文名 | 需求等级 | 职业 | 性别 | 地面(Ground) | 图标(StoreItem) | 纸娃娃衣服库及子序号 | 重量 | 持久 |
| :---: | :--- | :--- | :---: | :---: | :---: | :---: | :---: | :--- | :---: | :---: |
| 1168 | **布衣(男)** | Commoner Outfit (M) | 1 | WarWizTao | Male | 2 | 941 | `M_Hum.Zl (Sub #2)` | 5 | 5000 |
| 1169 | **布衣(女)** | Commoner Outfit (F) | 1 | WarWizTao | Female | 2 | 951 | `WM_Hum.Zl (Sub #2)` | 5 | 5000 |
| 1170 | **初学盔甲(男)** | Trainee's Armour (M) | 1 | Assassin | Male | 1 | 2000 | `M_Hum.Zl (Sub #1)` | 11 | 5000 |
| 1171 | **初学盔甲(女)** | Trainee's Armour (F) | 1 | Assassin | Female | 1 | 2010 | `WM_Hum.Zl (Sub #1)` | 11 | 5000 |
| 1172 | **轻型盔甲(男)** | Light Armour (M) | 11 | WarWizTao | Male | 2 | 942 | `M_Hum.Zl (Sub #2)` | 8 | 8000 |
| 1173 | **轻型盔甲(女)** | Light Armour (F) | 11 | WarWizTao | Female | 2 | 952 | `WM_Hum.Zl (Sub #2)` | 8 | 8000 |
| 1174 | **潜行披风(男)** | Shroud Of Stealth (M) | 11 | Assassin | Male | 1 | 2000 | `M_Hum.Zl (Sub #1)` | 13 | 8000 |
| 1175 | **潜行披风(女)** | Shroud Of Stealth (F) | 11 | Assassin | Female | 1 | 2010 | `WM_Hum.Zl (Sub #1)` | 13 | 8000 |
| 1176 | **中型盔甲(男)** | Medium Armour (M) | 22 | WarWizTao | Male | 3 | 980 | `M_Hum.Zl (Sub #3)` | 23 | 25000 |
| 1177 | **中型盔甲(女)** | Medium Armour (F) | 22 | WarWizTao | Female | 3 | 990 | `WM_Hum.Zl (Sub #3)` | 23 | 25000 |
| 1178 | **魔法长袍(男)** | Flame Robe (M) | 22 | WarWizTao | Male | 4 | 1020 | `M_Hum.Zl (Sub #4)` | 12 | 20000 |
| 1179 | **魔法长袍(女)** | Flame Robe (F) | 22 | WarWizTao | Female | 4 | 1030 | `WM_Hum.Zl (Sub #4)` | 12 | 20000 |
| 1180 | **灵魂战衣(男)** | Faith Robe (M) | 22 | WarWizTao | Male | 5 | 1000 | `M_Hum.Zl (Sub #5)` | 15 | 20000 |
| 1181 | **灵魂战衣(女)** | Faith Robe (F) | 22 | WarWizTao | Female | 5 | 1010 | `WM_Hum.Zl (Sub #5)` | 15 | 20000 |
| 1182 | **疾风轻甲(男)** | Tunic Of Velocity (M) | 22 | Assassin | Male | 2 | 2001 | `M_Hum.Zl (Sub #2)` | 20 | 25000 |
| 1183 | **疾风轻甲(女)** | Tunic Of Velocity (F) | 22 | Assassin | Female | 2 | 2011 | `WM_Hum.Zl (Sub #2)` | 20 | 25000 |
| 1184 | **重盔甲(男)** | Iron Plate Armour (M) | 33 | WarWizTao | Male | 6 | 981 | `M_Hum.Zl (Sub #6)` | 51 | 30000 |
| 1185 | **重盔甲(女)** | Iron Plate Armour (F) | 33 | WarWizTao | Female | 6 | 991 | `WM_Hum.Zl (Sub #6)` | 51 | 30000 |
| 1186 | **恶魔长袍(男)** | Robe Of Dark Flame (M) | 33 | WarWizTao | Male | 7 | 1021 | `M_Hum.Zl (Sub #7)` | 17 | 22000 |
| 1187 | **恶魔长袍(女)** | Robe Of Dark Flame (F) | 33 | WarWizTao | Female | 7 | 1031 | `WM_Hum.Zl (Sub #7)` | 17 | 22000 |
| 1188 | **幽灵战衣(男)** | Robe Of Balance (M) | 33 | WarWizTao | Male | 8 | 1001 | `M_Hum.Zl (Sub #8)` | 28 | 24000 |
| 1189 | **幽灵战衣(女)** | Robe Of Balance (F) | 33 | WarWizTao | Female | 8 | 1011 | `WM_Hum.Zl (Sub #8)` | 28 | 24000 |
| 1190 | **残影盔甲(男)** | Shadow Armour Of Deviation (M) | 33 | Assassin | Male | 3 | 2002 | `M_Hum.Zl (Sub #3)` | 32 | 30000 |
| 1191 | **残影盔甲(女)** | Shadow Armour Of Deviation (F) | 33 | Assassin | Female | 3 | 2012 | `WM_Hum.Zl (Sub #3)` | 32 | 30000 |
| 1192 | **战神盔甲(男)** | Wyvern Armour (M) | 40 | WarWizTao | Male | 9 | 960 | `M_Hum.Zl (Sub #9)` | 35 | 30000 |
| 1193 | **战神盔甲(女)** | Wyvern Armour (F) | 40 | WarWizTao | Female | 9 | 970 | `WM_Hum.Zl (Sub #9)` | 35 | 30000 |
| 1194 | **碧玉战甲(男)** | Nephrite Armour (M) | 40 | Assassin | Male | 4 | 2003 | `M_Hum.Zl (Sub #4)` | 43 | 30000 |
| 1195 | **碧玉战甲(女)** | Nephrite Armour (F) | 40 | Assassin | Female | 4 | 2013 | `WM_Hum.Zl (Sub #4)` | 43 | 30000 |
| 1196 | **圣战宝甲(男)** | Wyvern Armour Of Protection (M) | 51 | WarWizTao | Male | 9 | 961 | `M_Hum.Zl (Sub #9)` | 44 | 36000 |
| 1197 | **圣战宝甲(女)** | Wyvern Armour Of Protection (F) | 51 | WarWizTao | Female | 9 | 971 | `WM_Hum.Zl (Sub #9)` | 44 | 36000 |
| 1198 | **猎魔长袍(男)** | Demon Hunter's Shroud (M) | 51 | Assassin | Male | 5 | 2004 | `M_Hum.Zl (Sub #5)` | 63 | 36000 |
| 1199 | **猎魔长袍(女)** | Demon Hunter's Shroud (F) | 51 | Assassin | Female | 5 | 2014 | `WM_Hum.Zl (Sub #5)` | 63 | 36000 |
| 1200 | **死刑行装(男)** | Raiment Of Executioner(M) | 38 | Assassin | Male | 3 | 2002 | `M_Hum.Zl (Sub #3)` | 39 | 35000 |
| 1201 | **死刑行装(女)** | Raiment Of Executioner(F) | 38 | Assassin | Female | 3 | 2012 | `WM_Hum.Zl (Sub #3)` | 39 | 35000 |
| 1202 | **特制重盔甲(男)** | Enchanted Iron Plate Armour (M) | 38 | WarWizTao | Male | 6 | 982 | `M_Hum.Zl (Sub #6)` | 61 | 35000 |
| 1203 | **特制重盔甲(女)** | Enchanted Iron Plate Armour (F) | 38 | WarWizTao | Female | 6 | 992 | `WM_Hum.Zl (Sub #6)` | 61 | 35000 |
| 1204 | **法神披风(男)** | Robe Of Conjurer (M) | 38 | WarWizTao | Male | 7 | 1022 | `M_Hum.Zl (Sub #7)` | 20 | 23000 |
| 1205 | **法神披风(女)** | Robe Of Conjurer (F) | 38 | WarWizTao | Female | 7 | 1032 | `WM_Hum.Zl (Sub #7)` | 20 | 23000 |
| 1206 | **Robe Of Preserver (M)** | Robe Of Preserver (M) | 38 | WarWizTao | Male | 8 | 1002 | `M_Hum.Zl (Sub #8)` | 30 | 26000 |
| 1207 | **Robe Of Preserver (F)** | Robe Of Preserver (F) | 38 | WarWizTao | Female | 8 | 1012 | `WM_Hum.Zl (Sub #8)` | 30 | 26000 |
| 1208 | **Armour Of Condemned (M)** | Armour Of Condemned (M) | 44 | WarWizTao | Male | 6 | 983 | `M_Hum.Zl (Sub #6)` | 81 | 39000 |
| 1209 | **Armour Of Condemned (F)** | Armour Of Condemned (F) | 44 | WarWizTao | Female | 6 | 993 | `WM_Hum.Zl (Sub #6)` | 81 | 39000 |
| 1210 | **Raiment Of Arch Mage (M)** | Raiment Of Arch Mage (M) | 44 | WarWizTao | Male | 7 | 1023 | `M_Hum.Zl (Sub #7)` | 25 | 25000 |
| 1211 | **Raiment Of Arch Mage (F)** | Raiment Of Arch Mage (F) | 44 | WarWizTao | Female | 7 | 1033 | `WM_Hum.Zl (Sub #7)` | 25 | 25000 |
| 1212 | **Raiment Of High Priest (M)** | Raiment Of High Priest (M) | 44 | WarWizTao | Male | 8 | 1003 | `M_Hum.Zl (Sub #8)` | 40 | 29000 |
| 1213 | **Raiment Of High Priest (F)** | Raiment Of High Priest (F) | 44 | WarWizTao | Female | 8 | 1013 | `WM_Hum.Zl (Sub #8)` | 40 | 29000 |
| 1214 | **Tunic Of Annihilation (M)** | Tunic Of Annihilation (M) | 44 | Assassin | Male | 4 | 2003 | `M_Hum.Zl (Sub #4)` | 49 | 39000 |
| 1215 | **Tunic Of Annihilation (F)** | Tunic Of Annihilation (F) | 44 | Assassin | Female | 4 | 2013 | `WM_Hum.Zl (Sub #4)` | 49 | 39000 |
| 1216 | **Ghost Armour (M)** | Ghost Armour (M) | 48 | WarWizTao | Male | 1 | 984 | `M_Hum.Zl (Sub #1)` | 25 | 25000 |
| 1217 | **Ghost Armour (F)** | Ghost Armour (F) | 48 | WarWizTao | Female | 1 | 994 | `WM_Hum.Zl (Sub #1)` | 25 | 25000 |
| 1218 | **Raiment Of Spectral Energy (M)** | Raiment Of Spectral Energy (M) | 48 | Assassin | Male | 5 | 2004 | `M_Hum.Zl (Sub #5)` | 57 | 25000 |
| 1219 | **Raiment Of Spectral Energy (F)** | Raiment Of Spectral Energy (F) | 48 | Assassin | Female | 5 | 2014 | `WM_Hum.Zl (Sub #5)` | 57 | 25000 |
| 1220 | **Argent Battleplate Of Might (M)** | Argent Battleplate Of Might (M) | 60 | Warrior | Male | 10 | 962 | `M_Hum.Zl (Sub #10)` | 85 | 40000 |
| 1221 | **Argent Battleplate Of Might (F)** | Argent Battleplate Of Might (F) | 60 | Warrior | Female | 10 | 972 | `WM_Hum.Zl (Sub #10)` | 85 | 40000 |
| 1222 | **Sacred Warplate Of Mastery (M)** | Sacred Warplate Of Mastery (M) | 64 | WarWizTao | Male | 10 | 963 | `M_Hum.Zl (Sub #10)` | 105 | 45000 |
| 1223 | **Sacred Warplate Of Mastery (F)** | Sacred Warplate Of Mastery (F) | 64 | WarWizTao | Female | 10 | 973 | `WM_Hum.Zl (Sub #10)` | 105 | 45000 |
| 1224 | **Blazing Raiment Of Phoenix (M)** | Blazing Raiment Of Phoenix (M) | 64 | WarWizTao | Male | 10 | 964 | `M_Hum.Zl (Sub #10)` | 35 | 22000 |
| 1225 | **Blazing Raiment Of Phoenix (F)** | Blazing Raiment Of Phoenix (F) | 64 | WarWizTao | Female | 10 | 974 | `WM_Hum.Zl (Sub #10)` | 35 | 22000 |
| 1226 | **Azure Raiment Of Illusion (M)** | Azure Raiment Of Illusion (M) | 64 | WarWizTao | Male | 10 | 965 | `M_Hum.Zl (Sub #10)` | 60 | 35000 |
| 1227 | **Azure Raiment Of Illusion (F)** | Azure Raiment Of Illusion (F) | 64 | WarWizTao | Female | 10 | 975 | `WM_Hum.Zl (Sub #10)` | 60 | 35000 |
| 1228 | **Wyvern Scale Armour (M)** | Wyvern Scale Armour (M) | 60 | Assassin | Male | 6 | 2005 | `M_Hum.Zl (Sub #6)` | 83 | 42000 |
| 1229 | **Wyvern Scale Armour (F)** | Wyvern Scale Armour (F) | 60 | Assassin | Female | 6 | 2015 | `WM_Hum.Zl (Sub #6)` | 83 | 42000 |
| 1230 | **Armour Of Demonic Wrath (M)** | Armour Of Demonic Wrath (M) | 64 | Assassin | Male | 8 | 2007 | `M_Hum.Zl (Sub #8)` | 92 | 45000 |
| 1231 | **Armour Of Demonic Wrath (F)** | Armour Of Demonic Wrath (F) | 64 | Assassin | Female | 8 | 2017 | `WM_Hum.Zl (Sub #8)` | 92 | 45000 |
| 1232 | **Blazing Raiment Of Embers (M)** | Blazing Raiment Of Embers (M) | 60 | Wizard | Male | 10 | 962 | `M_Hum.Zl (Sub #10)` | 30 | 20000 |
| 1233 | **Blazing Raiment Of Embers (F)** | Blazing Raiment Of Embers (F) | 60 | Wizard | Female | 10 | 972 | `WM_Hum.Zl (Sub #10)` | 30 | 20000 |
| 1234 | **Demonic Raiment Of Illusion (M)** | Demonic Raiment Of Illusion (M) | 60 | Taoist | Male | 10 | 962 | `M_Hum.Zl (Sub #10)` | 35 | 25000 |
| 1235 | **Demonic Raiment Of Illusion (F)** | Demonic Raiment Of Illusion (F) | 60 | Taoist | Female | 10 | 972 | `WM_Hum.Zl (Sub #10)` | 35 | 25000 |
| 1236 | **Armour Of Abyss (M)** | Armour Of Abyss (M) | 44 | Assassin | Male | 7 | 2006 | `M_Hum.Zl (Sub #7)` | 70 | 45000 |
| 1237 | **Armour Of Abyss (F)** | Armour Of Abyss (F) | 44 | Assassin | Female | 7 | 2016 | `WM_Hum.Zl (Sub #7)` | 70 | 45000 |
| 1238 | **Overlord's Battleplate (M)** | Overlord's Battleplate (M) | 44 | Warrior | Male | 12 | 944 | `M_HumEx1.Zl (Sub #1)` | 90 | 45000 |
| 1239 | **Overlord's Battleplate (F)** | Overlord's Battleplate (F) | 44 | Warrior | Female | 12 | 954 | `WM_HumEx1.Zl (Sub #1)` | 90 | 45000 |
| 1240 | **Elementalist's Battleplate (M)** | Elementalist's Battleplate (M) | 44 | Wizard | Male | 12 | 944 | `M_HumEx1.Zl (Sub #1)` | 23 | 22000 |
| 1241 | **Elementalist's Battleplate (F)** | Elementalist's Battleplate (F) | 44 | Wizard | Female | 12 | 954 | `WM_HumEx1.Zl (Sub #1)` | 23 | 22000 |
| 1242 | **Archon's Battleplate (M)** | Archon's Battleplate (M) | 44 | Taoist | Male | 12 | 944 | `M_HumEx1.Zl (Sub #1)` | 25 | 35000 |
| 1243 | **Archon's Battleplate (F)** | Archon's Battleplate (F) | 44 | Taoist | Female | 12 | 954 | `WM_HumEx1.Zl (Sub #1)` | 25 | 35000 |
| 1244 | **FootBall Kit (M)** | FootBall Kit (M) | 0 | All | Male | 17 | 2870 | `M_HumEx1.Zl (Sub #6)` | 0 | 5000000 |
| 1245 | **FootBall Kit (F)** | FootBall Kit (F) | 0 | All | Female | 17 | 2880 | `WM_HumEx1.Zl (Sub #6)` | 0 | 5000000 |
| 1246 | **Tunic of the Blood God (M)** | Tunic of the Blood God (M) | 70 | Assassin | Male | 9 | 2008 | `M_Hum.Zl (Sub #9)` | 100 | 45000 |
| 1247 | **Tunic of the Blood God (F)** | Tunic of the Blood God (F) | 70 | Assassin | Female | 9 | 2018 | `WM_Hum.Zl (Sub #9)` | 100 | 45000 |
| 1248 | **Armour of the Blessed King** | Armour of the Blessed King | 70 | Warrior | Male | 25 | 3321 | `M_HumEx2.Zl (Sub #3)` | 100 | 45000 |
| 1249 | **Armour of the Blessed Queen** | Armour of the Blessed Queen | 70 | Warrior | Female | 25 | 3331 | `WM_HumEx2.Zl (Sub #3)` | 100 | 45000 |
| 1250 | **Vestment of Shattered Souls (M)** | Vestment of Shattered Souls (M) | 70 | Wizard | Male | 36 | 3326 | `M_HumEx3.Zl (Sub #3)` | 25 | 30000 |
| 1251 | **Vestment of Shattered Souls (F)** | Vestment of Shattered Souls (F) | 70 | Wizard | Female | 36 | 3336 | `WM_HumEx3.Zl (Sub #3)` | 25 | 30000 |
| 1252 | **Robes of the Blessed One (M)** | Robes of the Blessed One (M) | 70 | Taoist | Male | 33 | 3323 | `M_HumEx3.Zl (Sub #0)` | 28 | 40000 |
| 1253 | **Robes of the Blessed One (F)** | Robes of the Blessed One (F) | 70 | Taoist | Female | 33 | 3333 | `WM_HumEx3.Zl (Sub #0)` | 28 | 40000 |
| 1254 | **Armour of the Sun Keeper (M)** | Armour of the Sun Keeper (M) | 75 | Warrior | Male | 22 | 3320 | `M_HumEx2.Zl (Sub #0)` | 40 | 50000 |
| 1255 | **Armour of the Sun Keeper (F)** | Armour of the Sun Keeper (F) | 75 | Warrior | Female | 22 | 3330 | `WM_HumEx2.Zl (Sub #0)` | 40 | 50000 |
| 1256 | **Armour of Arcane Fury (M)** | Armour of Arcane Fury (M) | 75 | Wizard | Male | 37 | 3327 | `M_HumEx3.Zl (Sub #4)` | 20 | 30000 |
| 1257 | **Armour of Arcane Fury (F)** | Armour of Arcane Fury (F) | 75 | Wizard | Female | 37 | 3337 | `WM_HumEx3.Zl (Sub #4)` | 20 | 30000 |
| 1258 | **Armour of Eternal Rest (M)** | Armour of Eternal Rest (M) | 75 | Taoist | Male | 44 | 6000 | `M_HumEx4.Zl (Sub #0)` | 30 | 35000 |
| 1259 | **Armour of Eternal Rest (F)** | Armour of Eternal Rest (F) | 75 | Taoist | Female | 44 | 6010 | `WM_HumEx4.Zl (Sub #0)` | 30 | 35000 |
| 1260 | **Tunic of Whispers (M)** | Tunic of Whispers (M) | 75 | Assassin | Male | 22 | 3380 | `M_HumEx2.Zl (Sub #0)` | 35 | 40000 |
| 1261 | **Tunic of Whispers (F)** | Tunic of Whispers (F) | 75 | Assassin | Female | 22 | 3390 | `WM_HumEx2.Zl (Sub #0)` | 35 | 40000 |
| 1262 | **Tunic of Vengence (F)** | Tunic of Vengence (F) | 83 | Assassin | Female | 22 | 3391 | `WM_HumEx2.Zl (Sub #0)` | 35 | 40000 |
| 1263 | **Tunic of Vengence (M)** | Tunic of Vengence (M) | 83 | Assassin | Male | 22 | 3381 | `M_HumEx2.Zl (Sub #0)` | 35 | 40000 |
| 1264 | **Armour of the Titan Slayer (M)** | Armour of the Titan Slayer (M) | 83 | Warrior | Male | 22 | 3325 | `M_HumEx2.Zl (Sub #0)` | 40 | 50000 |
| 1265 | **Armour of the Titan Slayer (F)** | Armour of the Titan Slayer (F) | 83 | Warrior | Female | 22 | 3335 | `WM_HumEx2.Zl (Sub #0)` | 40 | 50000 |
| 1266 | **Rainments of the Blazing Comet (M)** | Rainments of the Blazing Comet (M) | 83 | Wizard | Male | 37 | 3328 | `M_HumEx3.Zl (Sub #4)` | 20 | 30000 |
| 1267 | **Rainments of the Blazing Comet (F)** | Rainments of the Blazing Comet (F) | 83 | Wizard | Female | 37 | 3338 | `WM_HumEx3.Zl (Sub #4)` | 20 | 30000 |
| 1268 | **Robes of the Titan Slayer (M)** | Robes of the Titan Slayer (M) | 83 | Taoist | Male | 44 | 3351 | `M_HumEx4.Zl (Sub #0)` | 30 | 35000 |
| 1269 | **Robes of the Titan Slayer (F)** | Robes of the Titan Slayer (F) | 83 | Taoist | Female | 44 | 3341 | `WM_HumEx4.Zl (Sub #0)` | 30 | 35000 |
| 1270 | **Santa Outfit (F)** | Santa Outfit (F) | 0 | All | Female | 17 | 5352 | `WM_HumEx1.Zl (Sub #6)` | 0 | 50000 |
| 1271 | **Santa Outfit (M)** | Santa Outfit (M) | 0 | All | Male | 17 | 5342 | `M_HumEx1.Zl (Sub #6)` | 0 | 50000 |


### <a id="helmet"></a>3.3 头盔 (Helmet) (13件)

| 序号 | 中文名 | 英文名 | 需求等级 | 需求职业 | 地面(Ground) | 图标(StoreItem) | 纸娃娃头盔库及子序号 | 重量 | 持久 |
| :---: | :--- | :--- | :---: | :---: | :---: | :---: | :--- | :---: | :---: |
| 231 | **青铜头盔** | Bronze Helmet | 9 | WarWizTao | 1 | 370 | `M_Helmet1.Zl / WM_Helmet1.Zl (Sub #0)` | 4 | 8000 |
| 234 | **魔法头盔** | Magic Bronze Helmet | 15 | WarWizTao | 1 | 370 | `M_Helmet1.Zl / WM_Helmet1.Zl (Sub #0)` | 4 | 8000 |
| 311 | **道士头盔** | Helmet Of Shaman | 20 | WarWizTao | 2 | 372 | `M_Helmet1.Zl / WM_Helmet1.Zl (Sub #1)` | 3 | 8000 |
| 317 | **沃玛头盔** | White Skull Helmet | 21 | WarWizTao | 3 | 373 | `M_Helmet1.Zl / WM_Helmet1.Zl (Sub #2)` | 5 | 8000 |
| 324 | **战神头盔** | Helmet Of The War God | 0 | WarWizTao | 11 | 414 | `M_Helmet2.Zl / WM_Helmet2.Zl (Sub #0)` | 20 | 10000 |
| 325 | **虎面头盔** | Crown Of Feral Lord | 0 | WarWizTao | 12 | 415 | `M_Helmet2.Zl / WM_Helmet2.Zl (Sub #1)` | 4 | 8000 |
| 362 | **记忆头盔** | Head-Guard Of Summoning | 22 | WarWizTao | 4 | 390 | `M_Helmet1.Zl / WM_Helmet1.Zl (Sub #3)` | 7 | 8000 |
| 382 | **斗笠** | Bamboo Hat | 27 | WarWizTao | 8 | 411 | `M_Helmet1.Zl / WM_Helmet1.Zl (Sub #7)` | 4 | 8000 |
| 391 | **天藤头盔** | Iron Wood Helmet | 29 | WarWizTao | 7 | 410 | `M_Helmet1.Zl / WM_Helmet1.Zl (Sub #6)` | 6 | 8000 |
| 407 | **腐烂骷髅头盔** | Laurel Mask | 35 | Assassin | 8 | 2107 | `M_Helmet1.Zl / WM_Helmet1.Zl (Sub #7)` | 6 | 8000 |
| 557 | **黑铁头盔** | Crown Of Dark Crusader | 0 | WarWizTao | 6 | 374 | `M_Helmet1.Zl / WM_Helmet1.Zl (Sub #5)` | 20 | 10000 |
| 560 | **行者帽** | Turban Of Discipline | 35 | WarWizTao | 10 | 413 | `M_Helmet1.Zl / WM_Helmet1.Zl (Sub #9)` | 5 | 8000 |
| 561 | **霸龙头盔** | Helmet Of The Conqueror | 37 | WarWizTao | 9 | 412 | `M_Helmet1.Zl / WM_Helmet1.Zl (Sub #8)` | 7 | 8000 |


### <a id="necklace"></a>3.4 项链 (Necklace) (55件)

| 序号 | 中文名 | 英文名 | 需求 | 职业 | 地面外观 (Ground Shape) | 背包图标 (StoreItem Image) | 重量 | 持久/堆叠 | 商店买价 |
| :---: | :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| 162 | **传统项链** | Necklace Of Tranquility | Level 3 | All | 0 | 873 | 1 | 8000 | 150 |
| 164 | **金项链** | Gold Necklace | Level 2 | All | 0 | 870 | 1 | 8000 | 100 |
| 204 | **魔鬼项链** | Naga Necklace | Level 15 | All | 0 | 811 | 1 | 9000 | 2500 |
| 205 | **灯笼项链** | Necklace Of Lantern | Level 13 | All | 0 | 872 | 1 | 8000 | 4000 |
| 206 | **真善项链** | Necklace Of Meditation | Level 15 | All | 0 | 830 | 1 | 7000 | 5000 |
| 207 | **白色虎齿项链** | White Tiger Tooth Necklace | Level 18 | All | 0 | 871 | 1 | 8000 | 6000 |
| 212 | **黑色水晶项链** | Black Crystal Necklace | Level 11 | All | 0 | 810 | 1 | 9000 | 2000 |
| 213 | **黑檀项链** | Arisu Necklace | Level 11 | All | 0 | 850 | 1 | 6000 | 6000 |
| 295 | **白金项链** | Platinum Necklace | MC 10 | All | 0 | 851 | 1 | 6000 | 4500 |
| 301 | **技巧项链** | Choker Of Learning | - | All | 0 | 891 | 1 | 8000 | 50000 |
| 305 | **探测项链** | Pendant Of Tracking | - | All | 0 | 890 | 0 | 8000 | 100000 |
| 309 | **蓝翡翠项链** | Blue Jade Necklace | Level 21 | All | 0 | 812 | 1 | 9000 | 5000 |
| 310 | **放大镜** | Pendant Of Image | Level 22 | All | 0 | 853 | 1 | 6000 | 15000 |
| 334 | **幽灵项链** | Claw Necklace | Level 27 | All | 0 | 813 | 1 | 9000 | 10000 |
| 343 | **银镜项链** | Tri Stone Necklace | Level 29 | All | 0 | 895 | 1 | 7000 | 23000 |
| 344 | **勇士项链** | Butcher's Necklace | Level 30 | All | 0 | 835 | 2 | 9000 | 12500 |
| 355 | **狂风项链** | Gale Necklace | Level 10 | All | 0 | 877 | 1 | 7000 | 50000 |
| 360 | **记忆项链** | Amulet Of Summoning | Level 22 | All | 0 | 910 | 1 | 8000 | 8000 |
| 366 | **魔血项链** | Pendant Of Vigor | Level 17 | All | 0 | 913 | 2 | 7000 | 7000 |
| 368 | **复血** | Pendant Of Courage | Level 13 | All | 0 | 818 | 3 | 9000 | 15000 |
| 370 | **绿色项链** | Green Bead Necklace | Level 13 | All | 0 | 814 | 3 | 9000 | 15000 |
| 371 | **恶魔铃铛** | Anti Evil Necklace | Level 13 | All | 0 | 855 | 1 | 6000 | 40000 |
| 374 | **虹魔项链** | Pendant Of Life Stealing | Level 13 | All | 0 | 912 | 2 | 7000 | 9000 |
| 387 | **气血项链** | Amulet Of Empowerment | Level 23 | All | 0 | 916 | 2 | 8000 | 20000 |
| 395 | **怨恨项链** | Amulet Of Vanquishment | Level 18 | All | 0 | 815 | 2 | 7000 | 50000 |
| 400 | **流星项链** | Meteorite Pendant | Level 28 | All | 0 | 898 | 4 | 9000 | 30000 |
| 432 | **金刚铃铛** | Amulet Of Ancient Kingdom | Level 15 | All | 0 | 914 | 1 | 7000 | 6000 |
| 450 | **黄色水晶项链** | Yellow Crystal Necklace | Level 11 | All | 0 | 830 | 1 | 7000 | 4000 |
| 453 | **琥珀项链** | Amber Necklace | Level 15 | All | 0 | 852 | 1 | 6000 | 7500 |
| 469 | **竹笛** | Bamboo Necklace | Level 22 | All | 0 | 832 | 1 | 7000 | 10000 |
| 473 | **震天项链** | Necklace Of Nature | Level 29 | All | 0 | 896 | 1 | 6000 | 30500 |
| 477 | **猫眼** | Amulet Of Wisdom | Level 13 | All | 0 | 838 | 2 | 7000 | 30000 |
| 478 | **灵魂项链** | Pendent Of Holy Spirit | Level 13 | All | 0 | 834 | 2 | 7000 | 30000 |
| 482 | **破坏项链** | Pendant Of Destruction | Level 23 | All | 0 | 836 | 4 | 9000 | 30000 |
| 517 | **五行神镜** | Amulet Of Five Elements | Level 28 | All | 0 | 915 | 1 | 6000 | 100000 |
| 518 | **乾坤一气** | Amulet Of The Enlightened | Level 28 | All | 0 | 897 | 2 | 7000 | 80000 |
| 629 | **追魂项链** | Amulet Of Absorption | Level 18 | All | 0 | 892 | 1 | 6000 | 50000 |
| 630 | **追风项链** | Pendant Of Wind Elemental | Level 18 | All | 0 | 893 | 1 | 8000 | 20000 |
| 631 | **魔令项链** | Soul Trapped Necklace | Level 18 | All | 0 | 894 | 1 | 7000 | 40000 |
| 661 | **破荒项链** | Charm Of The Destroyer | Level 23 | All | 0 | 819 | 3 | 8000 | 30000 |
| 662 | **魔云项链** | Amulet Of Dark Sorcery | Level 23 | All | 0 | 859 | 1 | 6000 | 80000 |
| 663 | **定心项链** | Pendant Of Purification | Level 23 | All | 0 | 839 | 2 | 7000 | 50000 |
| 866 | **圣山项链** | Symbol Of Holy Grounds | - | All | 0 | 879 | 2 | 9000 | 30000 |
| 875 | **Rusty Medallion Of Overlord** | Rusty Medallion Of Overlord | Level 20 | All | 0 | 924 | 2 | 3000 | 65000 |
| 876 | **Cracked Medallion Of Overlord** | Cracked Medallion Of Overlord | Level 20 | All | 0 | 924 | 2 | 3000 | 80000 |
| 877 | **Worn Medallion Of Overlord** | Worn Medallion Of Overlord | Level 20 | All | 0 | 924 | 2 | 3000 | 65000 |
| 885 | **Rusty Pendant Of Moon** | Rusty Pendant Of Moon | Level 20 | All | 0 | 926 | 2 | 2000 | 80000 |
| 886 | **Cracked Pendant Of Moon** | Cracked Pendant Of Moon | Level 20 | All | 0 | 926 | 2 | 2000 | 80000 |
| 887 | **Worn Pendant Of Moon** | Worn Pendant Of Moon | Level 20 | All | 0 | 926 | 2 | 2000 | 80000 |
| 895 | **Rusty Amulet Of Dignity** | Rusty Amulet Of Dignity | Level 20 | All | 0 | 925 | 1 | 1000 | 80000 |
| 896 | **Cracked Amulet Of Dignity** | Cracked Amulet Of Dignity | Level 20 | All | 0 | 925 | 1 | 1000 | 60000 |
| 897 | **Worn Amulet Of Dignity** | Worn Amulet Of Dignity | Level 20 | All | 0 | 925 | 1 | 1000 | 60000 |
| 925 | **Medallion Of Overlord** | Medallion Of Overlord | Level 65 | All | 0 | 924 | 2 | 9000 | 250000 |
| 928 | **Arcanist's Amulet Of Dignity** | Arcanist's Amulet Of Dignity | Level 65 | All | 0 | 925 | 1 | 6000 | 250000 |
| 931 | **Hierophant's Pendant Of Moon** | Hierophant's Pendant Of Moon | Level 65 | All | 0 | 926 | 2 | 7000 | 250000 |


### <a id="bracelet"></a>3.5 手镯 (Bracelet) (49件)

| 序号 | 中文名 | 英文名 | 需求 | 职业 | 地面外观 (Ground Shape) | 背包图标 (StoreItem Image) | 重量 | 持久/堆叠 | 商店买价 |
| :---: | :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| 161 | **铁手镯** | Iron Bracer | Level 3 | All | 0 | 646 | 1 | 7000 | 300 |
| 163 | **银手镯** | Silver Bracelet | Level 5 | All | 0 | 722 | 1 | 7000 | 500 |
| 168 | **大手镯** | Golden Bracelet | Level 7 | All | 0 | 644 | 1 | 7000 | 1500 |
| 169 | **魔法手镯** | Magic Bracelet | Level 12 | All | 0 | 647 | 1 | 7000 | 2500 |
| 170 | **黑檀手镯** | Arisu Bracelet | Level 10 | All | 0 | 700 | 1 | 5000 | 3000 |
| 171 | **小手镯** | Bracelet Of Exertion | Level 15 | All | 0 | 660 | 1 | 8000 | 1500 |
| 210 | **死神手套** | Gauntlet Of Deathbringer | Level 25 | All | 0 | 661 | 2 | 8000 | 5000 |
| 211 | **思贝儿手镯** | Bracelet Of Sorcery | Level 22 | All | 0 | 701 | 1 | 5000 | 8000 |
| 293 | **坚固手套** | Rugged Leather Gauntlet | Level 17 | All | 0 | 642 | 3 | 10000 | 5000 |
| 361 | **记忆手镯** | Bracelet Of Summoning | Level 22 | All | 0 | 760 | 1 | 6000 | 8000 |
| 365 | **魔血手镯** | Arm Wrap Of Vigor | Level 17 | All | 0 | 763 | 1 | 6000 | 7000 |
| 373 | **虹魔手镯** | Wrist Guard Of Life Stealing | Level 13 | All | 0 | 762 | 1 | 6000 | 9000 |
| 375 | **如来手镯** | Bracer Of Artisan | Level 26 | All | 0 | 663 | 2 | 6000 | 30000 |
| 376 | **铁炼腕** | Dark Iron Gauntlet | Level 28 | All | 0 | 667 | 9 | 8000 | 30000 |
| 377 | **火玉手镯** | Tainted Bracelet | Level 20 | All | 0 | 684 | 3 | 8000 | 15000 |
| 380 | **幽灵手套** | Bronze Gauntlet | Level 28 | All | 0 | 641 | 5 | 10000 | 15000 |
| 381 | **金手镯** | Gold Plated Bracelet | Level 25 | All | 0 | 645 | 1 | 7000 | 10000 |
| 388 | **黑皮手套** | Gloves Of Black Guard | Level 32 | All | 0 | 669 | 5 | 8000 | 20000 |
| 389 | **英雄手套** | Gauntlet Of Hero | Level 33 | All | 0 | 666 | 12 | 8000 | 30000 |
| 435 | **金刚防御手镯** | Armoured Bracer Of Ancient Kingdom | Level 15 | All | 0 | 764 | 1 | 6000 | 6000 |
| 436 | **金刚魔法手镯** | Holy Bracer Of Ancient Kingdom | Level 15 | All | 0 | 764 | 1 | 6000 | 6000 |
| 443 | **钢手镯** | Steel Bracelet | Level 8 | All | 0 | 646 | 1 | 7000 | 1000 |
| 451 | **道士手镯** | Bracer Of Magic | Level 10 | All | 0 | 680 | 1 | 8000 | 2000 |
| 470 | **三眼手镯** | Bracelet Of Three Eyes | Level 22 | All | 0 | 681 | 1 | 6000 | 8000 |
| 480 | **毁灭手镯** | Ring Of Destruction | Level 20 | All | 0 | 683 | 1 | 5000 | 50000 |
| 487 | **夏普儿手镯** | Dark Blade | Level 7 | All | 0 | 721 | 1 | 6000 | 30000 |
| 504 | **骑士手镯** | Hero's Bracelet | Level 10 | All | 0 | 662 | 2 | 8000 | 10000 |
| 505 | **心灵手镯** | Holy Bracer | Level 10 | All | 0 | 682 | 2 | 6000 | 15000 |
| 506 | **龙之手镯** | Wyvern Bracelet | Level 10 | All | 0 | 702 | 1 | 5000 | 45000 |
| 556 | **阎罗手套** | Augmented Bronze Gauntlet | Level 13 | All | 0 | 643 | 10 | 10000 | 30000 |
| 664 | **金棱手镯** | Bracer Of Revelation | Level 30 | All | 0 | 685 | 5 | 8000 | 20000 |
| 665 | **思过手镯** | Ring Of Enlightenment | Level 30 | All | 0 | 703 | 2 | 5000 | 80000 |
| 666 | **世尊手镯** | Bracelet Of Ascension | Level 30 | All | 0 | 725 | 3 | 6000 | 50000 |
| 867 | **心念手镯** | Holy Bracer Of Grace | - | All | 0 | 741 | 6 | 7000 | 30000 |
| 871 | **Rusty Bracelet Of Overlord** | Rusty Bracelet Of Overlord | Level 20 | All | 0 | 689 | 3 | 3000 | 55000 |
| 872 | **Cracked Bracelet Of Overlord** | Cracked Bracelet Of Overlord | Level 20 | All | 0 | 689 | 3 | 3000 | 55000 |
| 873 | **Worn Bracelet Of Overlord** | Worn Bracelet Of Overlord | Level 20 | All | 0 | 689 | 3 | 3000 | 55000 |
| 874 | **Scratched Bracelet Of Overlord** | Scratched Bracelet Of Overlord | Level 20 | All | 0 | 689 | 3 | 3000 | 55000 |
| 881 | **Rusty Bracer Of Moon** | Rusty Bracer Of Moon | Level 20 | All | 0 | 691 | 2 | 2000 | 60000 |
| 882 | **Cracked Bracer Of Moon** | Cracked Bracer Of Moon | Level 20 | All | 0 | 691 | 2 | 2000 | 65000 |
| 883 | **Worn Bracer Of Moon** | Worn Bracer Of Moon | Level 20 | All | 0 | 691 | 2 | 2000 | 65000 |
| 884 | **Scratched Bracer Of Moon** | Scratched Bracer Of Moon | Level 20 | All | 0 | 691 | 2 | 2000 | 65000 |
| 891 | **Rusty Bracelet Of Dignity** | Rusty Bracelet Of Dignity | Level 20 | All | 0 | 690 | 1 | 1000 | 50000 |
| 892 | **Cracked Bracelet Of Dignity** | Cracked Bracelet Of Dignity | Level 20 | All | 0 | 690 | 1 | 1000 | 50000 |
| 893 | **Worn Bracelet Of Dignity** | Worn Bracelet Of Dignity | Level 20 | All | 0 | 690 | 1 | 1000 | 50000 |
| 894 | **Scratched Bracelet Of Dignity** | Scratched Bracelet Of Dignity | Level 20 | All | 0 | 690 | 1 | 1000 | 50000 |
| 924 | **Bracelet Of Overlord** | Bracelet Of Overlord | Level 65 | All | 0 | 689 | 3 | 8000 | 180000 |
| 927 | **Arcanist's Bracelet Of Dignity** | Arcanist's Bracelet Of Dignity | Level 65 | All | 0 | 690 | 1 | 5000 | 180000 |
| 930 | **Hierophant's Bracer Of Moon** | Hierophant's Bracer Of Moon | Level 65 | All | 0 | 691 | 2 | 6000 | 180000 |


### <a id="ring"></a>3.6 戒指 (Ring) (61件)

| 序号 | 中文名 | 英文名 | 需求 | 职业 | 地面外观 (Ground Shape) | 背包图标 (StoreItem Image) | 重量 | 持久/堆叠 | 商店买价 |
| :---: | :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| 165 | **牛角戒指** | Horned Ring | Level 7 | All | 0 | 470 | 1 | 6000 | 1200 |
| 172 | **黑色水晶戒指** | Black Crystal Ring | Level 13 | All | 0 | 531 | 1 | 7000 | 2000 |
| 173 | **珍珠戒指** | Pearl Ring | Level 13 | All | 0 | 491 | 1 | 5000 | 2500 |
| 208 | **珊瑚戒指** | Coral Ring | Level 20 | All | 0 | 533 | 1 | 7000 | 3000 |
| 209 | **白月银蛇戒指** | White Serpent Ring | Level 19 | All | 0 | 511 | 1 | 4000 | 6000 |
| 299 | **传送戒指** | Loop Of Teleportation | - | All | 0 | 573 | 1 | 5000 | 100000 |
| 300 | **麻痹戒指** | Loop Of Paralysis | - | All | 0 | 571 | 1 | 5000 | 100000 |
| 302 | **复活戒指** | Loop Of Ressurection | - | All | 0 | 575 | 1 | 5000 | 100000 |
| 303 | **护身戒指** | Loop Of Protection | - | All | 0 | 576 | 1 | 5000 | 100000 |
| 304 | **隐身戒指** | Loop Of Invisibility | - | All | 0 | 574 | 1 | 5000 | 100000 |
| 307 | **金戒指** | Gold Ring | Level 20 | All | 0 | 473 | 1 | 6000 | 7000 |
| 308 | **魅力戒指** | Band Of Sorcery | Level 19 | All | 0 | 512 | 1 | 4000 | 9000 |
| 326 | **心魔戒指** | Glowing Diamond Band | Level 30 | All | 0 | 477 | 1 | 4000 | 70000 |
| 333 | **降妖除魔戒指** | Ring Of Exorcism | Level 27 | All | 0 | 474 | 2 | 6000 | 10000 |
| 354 | **狂风戒指** | Gale Ring | Level 10 | All | 0 | 550 | 1 | 5000 | 50000 |
| 359 | **记忆戒指** | Signet Of Summoning | Level 22 | All | 0 | 590 | 1 | 7000 | 8000 |
| 364 | **魔血戒指** | Signet Of Vigor | Level 17 | All | 0 | 593 | 1 | 5000 | 7000 |
| 372 | **虹魔戒指** | Ring Of Life Stealing | Level 13 | All | 0 | 592 | 1 | 5000 | 9000 |
| 396 | **天机戒指** | Ring Of Endless Circle | Level 35 | All | 0 | 476 | 2 | 5000 | 50000 |
| 397 | **紫金环** | Ancient Myrmidon Band | Level 40 | All | 0 | 497 | 3 | 7000 | 19000 |
| 398 | **武圣之戒** | Loop Of Endless Combat | Level 40 | All | 0 | 496 | 4 | 7000 | 19000 |
| 399 | **七彩金环** | Loop Of Seven Gems | Level 35 | All | 0 | 478 | 2 | 5000 | 100000 |
| 434 | **金刚精神戒指** | Spirit Band Of Ancient Kingdom | Level 15 | All | 0 | 594 | 1 | 5000 | 6000 |
| 440 | **古铜戒指** | Copper Ring | Level 7 | All | 0 | 530 | 1 | 7000 | 800 |
| 441 | **蓝色水晶戒指** | Blue Crystal Ring | Level 9 | All | 0 | 472 | 1 | 6000 | 1500 |
| 452 | **蛇眼戒指** | Serpent Ring | Level 13 | All | 0 | 511 | 1 | 4000 | 3750 |
| 454 | **神力戒指** | Seal Of Titans | - | All | 0 | 572 | 1 | 5000 | 100000 |
| 468 | **道德戒指** | Ring Of Discipline | Level 19 | All | 0 | 492 | 1 | 5000 | 6000 |
| 489 | **力量戒指** | Spiked Ring | Level 29 | All | 0 | 535 | 3 | 7000 | 9000 |
| 490 | **紫碧螺** | Opal Ring | Level 27 | All | 0 | 514 | 1 | 4000 | 15000 |
| 491 | **泰坦戒指** | Hieroglyphic Ring | Level 27 | All | 0 | 494 | 2 | 5000 | 10000 |
| 492 | **红叶血环** | Red Maple Ring | Level 26 | All | 0 | 499 | 2 | 7000 | 7000 |
| 530 | **六棱戒** | Ring Of Unification | Level 35 | All | 0 | 498 | 2 | 7000 | 16000 |
| 531 | **红宝石戒指** | Ruby Ring | MC 17 | All | 0 | 513 | 1 | 4000 | 15000 |
| 532 | **铂金戒指** | Platinum Ring | SC 17 | All | 0 | 493 | 1 | 5000 | 10000 |
| 533 | **龙之戒指** | Gold Dragon Ring | DC 37 | All | 0 | 534 | 2 | 7000 | 5000 |
| 536 | **虚空道环** | Loop Of Secrets | Level 30 | All | 0 | 479 | 1 | 5000 | 40000 |
| 558 | **雷神戒指** | Blue Jade Signet | Level 20 | All | 0 | 857 | 1 | 4000 | 45000 |
| 559 | **润神戒指** | Loop Of Reincarnation | Level 20 | All | 0 | 475 | 2 | 5000 | 20000 |
| 568 | **帝王戒指** | Ring Of Sovereignty | Level 20 | All | 0 | 515 | 4 | 7000 | 10000 |
| 581 | **骷髅戒指** | Skeleton Ring | DC 30 | All | 0 | 532 | 1 | 7000 | 3000 |
| 624 | **魔灵戒指** | Band Of Netherworld | Level 25 | All | 0 | 495 | 1 | 6000 | 30000 |
| 625 | **石榴戒指** | Diamond Encrusted Band | Level 25 | All | 0 | 516 | 1 | 5000 | 60000 |
| 626 | **青摇戒指** | Perfection Ring | Level 25 | All | 0 | 536 | 1 | 7000 | 13000 |
| 627 | **莲丸戒指** | Universe Ring | Level 25 | All | 0 | 537 | 1 | 7000 | 13000 |
| 658 | **师承戒指** | Signet Of Myrmidon | Level 30 | All | 0 | 538 | 1 | 7000 | 13000 |
| 659 | **龙马戒指** | Signet Of Evoker | Level 25 | All | 0 | 518 | 1 | 5000 | 60000 |
| 660 | **青云戒指** | Signet Of Vicar | Level 25 | All | 0 | 553 | 1 | 6000 | 30000 |
| 682 | **全能戒指** | Hero's Band Of Supremacy | - | All | 0 | 517 | 2 | 6000 | 50000 |
| 868 | **Rusty Seal Of Overlord** | Rusty Seal Of Overlord | Level 20 | All | 0 | 559 | 3 | 3000 | 45000 |
| 869 | **Cracked Seal Of Overlord** | Cracked Seal Of Overlord | Level 20 | All | 0 | 559 | 3 | 3000 | 45000 |
| 870 | **Worn Seal Of Overlord** | Worn Seal Of Overlord | Level 20 | All | 0 | 559 | 3 | 3000 | 45000 |
| 878 | **Rusty Signet Of Moon** | Rusty Signet Of Moon | Level 20 | All | 0 | 561 | 2 | 2000 | 45000 |
| 879 | **Cracked Signet Of Moon** | Cracked Signet Of Moon | Level 20 | All | 0 | 561 | 2 | 2000 | 45000 |
| 880 | **Worn Signet Of Moon** | Worn Signet Of Moon | Level 20 | All | 0 | 561 | 2 | 2000 | 65000 |
| 888 | **Rusty Band Of Dignity** | Rusty Band Of Dignity | Level 20 | All | 0 | 560 | 1 | 1000 | 45000 |
| 889 | **Cracked Band Of Dignity** | Cracked Band Of Dignity | Level 20 | All | 0 | 560 | 1 | 1000 | 45000 |
| 890 | **Worn Band Of Dignity** | Worn Band Of Dignity | Level 20 | All | 0 | 560 | 1 | 1000 | 45000 |
| 923 | **Seal Of Overlord** | Seal Of Overlord | Level 65 | All | 0 | 559 | 3 | 7000 | 180000 |
| 926 | **Arcanist's Band Of Dignity** | Arcanist's Band Of Dignity | Level 65 | All | 0 | 560 | 1 | 4000 | 180000 |
| 929 | **Hierophant's Signet Of Moon** | Hierophant's Signet Of Moon | Level 65 | All | 0 | 561 | 2 | 6000 | 180000 |


### <a id="shoes"></a>3.7 鞋靴 (Shoes) (11件)

| 序号 | 中文名 | 英文名 | 需求 | 职业 | 地面外观 (Ground Shape) | 背包图标 (StoreItem Image) | 重量 | 持久/堆叠 | 商店买价 |
| :---: | :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| 235 | **草鞋** | Straw Sandles | Level 6 | All | 0 | 1360 | 1 | 6000 | 1000 |
| 236 | **皮靴** | Leather Shoes | Level 16 | All | 0 | 1361 | 1 | 8000 | 5000 |
| 318 | **武神之靴** | Boots Of Crimson Steed | - | All | 0 | 1376 | 4 | 12000 | 50000 |
| 390 | **五彩鞋** | Brightly Coloured Shoes | Level 26 | All | 0 | 1374 | 1 | 8000 | 8000 |
| 519 | **月光鞋** | Boots Of Despair | - | All | 0 | 1371 | 2 | 8000 | 30000 |
| 520 | **仙云靴** | Boots Of Swiftness | - | All | 0 | 1375 | 4 | 12000 | 30000 |
| 521 | **绝地靴** | Boots Of Levitation | - | All | 0 | 1377 | 2 | 8000 | 30000 |
| 525 | **赤飞靴子** | Rugged Leather Boots | Level 33 | All | 0 | 1363 | 2 | 10000 | 10000 |
| 526 | **黑皮靴子** | Boots Of Black Tortoise | - | All | 0 | 1364 | 3 | 12000 | 30000 |
| 527 | **天掌靴子** | Silk Boots | Level 38 | All | 0 | 1362 | 2 | 10000 | 20000 |
| 587 | **无影靴** | Shadow Chaser | - | All | 0 | 1373 | 4 | 12000 | 30000 |


### <a id="consumable"></a>3.8 消耗品/药水 (Consumable) (8件)

| 序号 | 中文名 | 英文名 | 需求 | 职业 | 地面外观 (Ground Shape) | 背包图标 (StoreItem Image) | 重量 | 持久/堆叠 | 商店买价 |
| :---: | :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| 153 | **太阳水** | Rejuvenation Potion | - | All | 0 | 20 | 0 | 750 | 500 |
| 155 | **随机传送卷** | Scroll Of Random Teleport | - | All | 3 | 205 | 1 | 750 | 100 |
| 156 | **回城卷** | Scroll Of Town Portal | - | All | 2 | 207 | 1 | 750 | 500 |
| 296 | **祝福油** | Oil Of Benediction | - | All | 4 | 63 | 1 | 1000 | 1000 |
| 297 | **祝福油** | Oil Of Conservation | - | All | 5 | 53 | 1 | 1000 | 10000 |
| 298 | **战神油** | Oil Of The War God | - | All | 6 | 61 | 1 | 1000 | 1000 |
| 323 | **万年雪霜** | Ginseng Of Eternity | - | All | 0 | 70 | 0 | 750 | 5000 |
| 438 | **鸡血** | Chicken Blood | - | All | 0 | 31 | 1 | 750 | 10 |


### <a id="ore"></a>3.9 矿石 (Ore) (9件)

| 序号 | 中文名 | 英文名 | 需求 | 职业 | 地面外观 (Ground Shape) | 背包图标 (StoreItem Image) | 重量 | 持久/堆叠 | 商店买价 |
| :---: | :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| 537 | **铁矿** | Copper Ore | - | All | 0 | 216 | 4 | 10000 | 500 |
| 538 | **银矿** | Iron Ore | - | All | 0 | 211 | 4 | 10000 | 1000 |
| 539 | **铜矿** | Silver Ore | - | All | 0 | 215 | 4 | 10000 | 2500 |
| 540 | **金矿** | Gold Ore | - | All | 0 | 210 | 4 | 10000 | 6000 |
| 541 | **黑铁** | Black Iron Ore | - | All | 0 | 214 | 4 | 10000 | 1000 |
| 542 | **紫水晶** | Amethyst | - | All | 0 | 217 | 4 | 10000 | 500 |
| 543 | **石榴石** | Garnet | - | All | 0 | 218 | 4 | 10000 | 1000 |
| 544 | **金刚石** | Diamond | - | All | 0 | 219 | 4 | 10000 | 2500 |
| 545 | **刚玉矿** | Corundum | - | All | 0 | 220 | 4 | 10000 | 6000 |


### <a id="darkstone"></a>3.10 暗石/强化石 (DarkStone) (2件)

| 序号 | 中文名 | 英文名 | 需求 | 职业 | 地面外观 (Ground Shape) | 背包图标 (StoreItem Image) | 重量 | 持久/堆叠 | 商店买价 |
| :---: | :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| 426 | **Gusting Dark Stone (IV)** | Gusting Dark Stone (IV) | Level 50 | All | 0 | 343 | 12 | 20000 | 150000 |
| 427 | **Gusting Dark Stone (V)** | Gusting Dark Stone (V) | Level 55 | All | 0 | 343 | 17 | 40000 | 250000 |


### <a id="torch"></a>3.11 照明道具 (Torch) (2件)

| 序号 | 中文名 | 英文名 | 需求 | 职业 | 地面外观 (Ground Shape) | 背包图标 (StoreItem Image) | 重量 | 持久/堆叠 | 商店买价 |
| :---: | :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| 132 | **蜡烛** | Candle | - | All | 0 | 290 | 1 | 8000 | 10 |
| 158 | **火把** | Torch | - | All | 0 | 291 | 3 | 20000 | 500 |


### <a id="meat"></a>3.12 食材肉品 (Meat) (1件)

| 序号 | 中文名 | 英文名 | 需求 | 职业 | 地面外观 (Ground Shape) | 背包图标 (StoreItem Image) | 重量 | 持久/堆叠 | 商店买价 |
| :---: | :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| 179 | **鸡肉** | Chicken Meat | - | All | 0 | 301 | 1 | 4000 | 80 |


### <a id="currency"></a>3.13 货币 (Currency) (3件)

| 序号 | 中文名 | 英文名 | 需求 | 职业 | 地面外观 (Ground Shape) | 背包图标 (StoreItem Image) | 重量 | 持久/堆叠 | 商店买价 |
| :---: | :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| 1 | **金币** | Gold | - | All | 0 | 0 | 0 | 25000 | 0 |
| 1127 | **Fame Point** | Fame Point | - | All | 0 | 4010 | 0 | 25000 | 0 |
| 1128 | **Contribution Point** | Contribution Point | - | All | 0 | 4012 | 0 | 25000 | 0 |


### <a id="nothing"></a>3.14 杂物与任务信物 (Nothing) (65件)

| 序号 | 中文名 | 英文名 | 需求 | 职业 | 地面外观 (Ground Shape) | 背包图标 (StoreItem Image) | 重量 | 持久/堆叠 | 商店买价 |
| :---: | :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| 241 | **食人树叶** | Carnivorous Plant Leaf | - | All | 0 | 113 | 0 | 10000 | 50 |
| 244 | **毒蜘蛛牙齿** | Spitting Spider Tooth | - | All | 0 | 111 | 0 | 10000 | 50 |
| 245 | **牙齿** | Tooth | - | All | 0 | 1431 | 0 | 10000 | 800 |
| 256 | **骷髅骨** | Skeleton Bone | - | All | 0 | 103 | 0 | 10000 | 200 |
| 258 | **僵尸骨头** | Zombie Bone | - | All | 0 | 1149 | 0 | 10000 | 400 |
| 259 | **蛆卵** | Maggot Pill | - | All | 0 | 110 | 0 | 10000 | 500 |
| 260 | **蚂蚁卵** | Ant Egg | - | All | 0 | 102 | 0 | 10000 | 600 |
| 261 | **跳蚤皮** | Flea Husk | - | All | 0 | 1444 | 0 | 10000 | 400 |
| 322 | **皮** | Husk | - | All | 0 | 107 | 0 | 10000 | 800 |
| 327 | **水晶** | Crystal | - | All | 0 | 221 | 1 | 5000 | 1000000 |
| 332 | **潘夜珠** | Banya Gem Stone | - | All | 0 | 1349 | 0 | 10000 | 600 |
| 356 | **号角** | Horn | - | All | 0 | 1338 | 0 | 10000 | 600 |
| 358 | **沃玛号角** | Horn Of Uma King | - | All | 0 | 100 | 1 | 5 | 1000 |
| 369 | **蜘蛛线** | Spider Web Thread | - | All | 0 | 105 | 0 | 10000 | 1000 |
| 378 | **指甲** | Claw | - | All | 0 | 1340 | 0 | 10000 | 1200 |
| 394 | **祖玛头像** | Fragment Of Zuma King | - | All | 0 | 101 | 1 | 0 | 1000 |
| 405 | **神灵雕像** | Statue Fragment | - | All | 0 | 1430 | 0 | 10000 | 1400 |
| 483 | **回城卷** | Freedom Pass | - | All | 0 | 180 | 0 | 5000 | 50 |
| 493 | **宝玉** | Jewel | - | All | 0 | 1424 | 0 | 10000 | 1000 |
| 508 | **夜明珠** | Dark Banya Gem Stone | - | All | 0 | 1350 | 0 | 10000 | 1400 |
| 528 | **潘夜之泪** | Tears Of Banya | - | All | 0 | 1348 | 0 | 10000 | 800 |
| 640 | **Rusty Signet Of Myrmidon** | Rusty Signet Of Myrmidon | - | All | 0 | 538 | 1 | 0 | 1000 |
| 641 | **Rusty Signet Of Evoker** | Rusty Signet Of Evoker | - | All | 0 | 518 | 1 | 0 | 1000 |
| 642 | **Rusty Signet Of Vicar** | Rusty Signet Of Vicar | - | All | 0 | 553 | 1 | 0 | 1000 |
| 643 | **Rusty Charm Of The Destroyer** | Rusty Charm Of The Destroyer | - | All | 0 | 819 | 1 | 0 | 1000 |
| 644 | **Rusty Amulet Of Dark Sorcery** | Rusty Amulet Of Dark Sorcery | - | All | 0 | 859 | 1 | 0 | 1000 |
| 645 | **Rusty Pendant Of Purification** | Rusty Pendant Of Purification | - | All | 0 | 839 | 1 | 0 | 1000 |
| 646 | **Rusty Bracer Of Revelation** | Rusty Bracer Of Revelation | - | All | 0 | 685 | 1 | 0 | 1000 |
| 647 | **Rusty Ring Of Enlightenment** | Rusty Ring Of Enlightenment | - | All | 0 | 703 | 1 | 0 | 1000 |
| 648 | **Rusty Bracelet Of Ascension** | Rusty Bracelet Of Ascension | - | All | 0 | 725 | 1 | 0 | 1000 |
| 803 | **Venom** | Venom | - | All | 0 | 60 | 0 | 0 | 0 |
| 804 | **Arachnid Teeth** | Arachnid Teeth | - | All | 0 | 111 | 0 | 0 | 0 |
| 805 | **Spider Curare** | Spider Curare | - | All | 0 | 2755 | 0 | 0 | 0 |
| 806 | **Edible Chestnut** | Edible Chestnut | - | All | 0 | 1411 | 0 | 0 | 0 |
| 807 | **Skeletal Spine** | Skeletal Spine | - | All | 0 | 2644 | 0 | 0 | 0 |
| 808 | **Henry's Journal** | Henry's Journal | - | All | 0 | 1572 | 0 | 0 | 0 |
| 809 | **David's Key** | David's Key | - | All | 0 | 2950 | 0 | 0 | 0 |
| 810 | **Haylee's Key** | Haylee's Key | - | All | 0 | 2950 | 0 | 0 | 0 |
| 811 | **Kacy's Key** | Kacy's Key | - | All | 0 | 2950 | 0 | 0 | 0 |
| 812 | **Zombie Flesh** | Zombie Flesh | - | All | 0 | 1556 | 0 | 0 | 0 |
| 813 | **Gresham's Journal** | Gresham's Journal | - | All | 0 | 1572 | 0 | 0 | 0 |
| 814 | **Isaac's Journal** | Isaac's Journal | - | All | 0 | 1572 | 0 | 0 | 0 |
| 828 | **Refinement Stone** | Refinement Stone | - | All | 0 | 224 | 1 | 10000 | 0 |
| 829 | **Fragment** | Fragment | - | All | 0 | 271 | 0 | 10000 | 0 |
| 830 | **Fragment (II)** | Fragment (II) | - | All | 0 | 272 | 0 | 10000 | 0 |
| 831 | **Fragment (III)** | Fragment (III) | - | All | 0 | 273 | 0 | 10000 | 0 |
| 972 | **Yellow Cube** | Yellow Cube | - | All | 4 | 3130 | 0 | 5000 | 0 |
| 973 | **Blue Cube** | Blue Cube | - | All | 4 | 3131 | 0 | 5000 | 0 |
| 974 | **Red Cube** | Red Cube | - | All | 4 | 3132 | 0 | 5000 | 0 |
| 975 | **Purple Cube** | Purple Cube | - | All | 4 | 3133 | 0 | 5000 | 0 |
| 976 | **Green Cube** | Green Cube | - | All | 4 | 3134 | 0 | 5000 | 0 |
| 977 | **Grey Cube** | Grey Cube | - | All | 4 | 3135 | 0 | 5000 | 0 |
| 978 | **Yellow Orb** | Yellow Orb | - | All | 1 | 3140 | 0 | 5000 | 0 |
| 979 | **Blue Orb** | Blue Orb | - | All | 1 | 3141 | 0 | 5000 | 0 |
| 980 | **Red Orb** | Red Orb | - | All | 1 | 3142 | 0 | 5000 | 0 |
| 981 | **Purple Orb** | Purple Orb | - | All | 1 | 3143 | 0 | 5000 | 0 |
| 982 | **Green Orb** | Green Orb | - | All | 1 | 3144 | 0 | 5000 | 0 |
| 983 | **Grey Orb** | Grey Orb | - | All | 1 | 3145 | 0 | 5000 | 0 |
| 984 | **Yellow Trinket** | Yellow Trinket | - | All | 2 | 3150 | 0 | 5000 | 0 |
| 985 | **Blue Trinket** | Blue Trinket | - | All | 2 | 3151 | 0 | 5000 | 0 |
| 986 | **Red Trinket** | Red Trinket | - | All | 2 | 3152 | 0 | 5000 | 0 |
| 987 | **Purple Trinket** | Purple Trinket | - | All | 2 | 3153 | 0 | 5000 | 0 |
| 988 | **Green Trinket** | Green Trinket | - | All | 2 | 3154 | 0 | 5000 | 0 |
| 989 | **Grey Trinket** | Grey Trinket | - | All | 2 | 3155 | 0 | 5000 | 0 |
| 1097 | **金盒** | Gold Chest | - | All | 0 | 127 | 1 | 5000 | 1000000000 |


---

## 四、客户端资源文件物理分布表

所有渲染贴图均存放在 `/home/tetsuya/mir2ei/Data/` 目录中，格式为压缩优化的 `.Zl` 库：

| 资源库名 | 文件路径 | 包含帧数 | 用途说明 |
| :--- | :--- | :---: | :--- |
| `StoreItem.Zl` | `Data/StoreItem.Zl` | 8590 | 背包、快捷栏、商店、仓库界面的物品道具图标 |
| `Ground.Zl` | `Data/Ground.Zl` | 800+ | 地面掉落、丢弃物品的静态外观模型 |
| `M_Hum.Zl` | `Data/M_Hum.Zl` | 13200 | 男性角色基础衣服纸娃娃动作序列 (Shape 0..10) |
| `WM_Hum.Zl` | `Data/WM_Hum.Zl` | 13200 | 女性角色基础衣服纸娃娃动作序列 (Shape 0..10) |
| `M_HumEx1.Zl` ~ `Ex13.Zl` | `Data/M_HumEx*.Zl` | 各13200 | 男性进阶/重装盔甲纸娃娃动作序列 |
| `WM_HumEx1.Zl` ~ `Ex13.Zl` | `Data/WM_HumEx*.Zl` | 各13200 | 女性进阶/重装盔甲纸娃娃动作序列 |
| `M_Weapon1.Zl` ~ `16.Zl` | `Data/M_Weapon*.Zl` | 各12000 | 男性手持武器全身挥砍/施法/跑动贴图 |
| `WM_Weapon1.Zl` ~ `16.Zl` | `Data/WM_Weapon*.Zl` | 各12000 | 女性手持武器全身挥砍/施法/跑动贴图 |
| `M_Helmet1.Zl` ~ `14.Zl` | `Data/M_Helmet*.Zl` | 各13200 | 男性头盔穿戴动作序列贴图 |
| `WM_Helmet1.Zl` ~ `14.Zl` | `Data/WM_Helmet*.Zl` | 各13200 | 女性头盔穿戴动作序列贴图 |
| `Interface.Zl` | `Data/Interface.Zl` | 3000+ | UI 底图（F250背包、F280锁链、按钮、边框） |