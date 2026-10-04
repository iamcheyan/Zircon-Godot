# 经典传奇 3 核心循环三阶段实施报告（2026-10-04）

> 执行对象：`docs/CLASSIC_CORE_TRIAD_PIPELINE_SPEC.md`
> 三次独立提交：`e4dc7213`（阶段一）→ `31578588`（阶段二）→ `175100c5`（阶段三）
> 统一工具：`Tools/ClassicMagicFixer/`（dump / apply / restore / drops / shop / npcs / npcpos / tp / gm）

---

## 零、规格勘误（执行前发现并修正的事实）

规范文档引用的三条「权威数据源」路径**实际不存在**：

| 文档引用 | 实际情况 | 实际使用 |
|---|---|---|
| `Mud3-Config/Envir/Magic.txt` | 不存在（魔法表来自 SQL `TABLE_MAGIC`，非文本配置） | `ClientData/Magic.exp.txt`（原版客户端技能手册，含日文标准名 + 1/2/3 级需求等级与修炼值） |
| `Mud3-Config/Envir/MonDrop/` | 不存在（无任何 MonDrop/ItemDrop/Rate 目录） | `Mud3-Config/Envir3/MonItems/`（321 个怪物掉落表，格式 `1/N <物品> [数量]`） |
| `Mud3-Config/Envir/Market_Prices/` | 不存在（二进制 `.prc`，被上游仓库排除） | `Mud3-Config/Envir3/Market_Def/*` 的 `[Goods]` 段 + 17173 物品价格 |

另：`GoldDrop` 字段在代码库中**不存在**，金币是 `Item == SEnvir.GoldInfo` 的普通 `DropInfo` 行。

---

## 一、阶段一：技能纯净化

### 关键发现：10-03 清洗存在系统性缺陷

`docs/DATABASE_CLASSIC_PURITY_CLEANUP_SPEC` 的清洗把 MagicInfo 从 174 行裁到 59 行，
判据是 `db_names.json` 的**英文别名**匹配。而引擎行的 `Name` 与 17173 技能名并不一致：

| 17173 技能 | 被误匹配的别名 | 经典正确行 | 后果 |
|---|---|---|---|
| 雷电术 | `Lightning Ball` | **`Thunder Bolt`** | 法师 16 级技能错位 |
| 召唤骷髅 | `Evil Slayer` | **`Summon Skeleton`** | 道士 19 级技能错位 |
| 龙卷风 | `Cyclone` | **`Dragon Tornado`** | 法师 35 级技能错位 |
| 冰月震天 | `Frost Bite`(58 级) | **`Ice Blades`** | 道士/法师两侧都错 |

结果：**10 个经典技能被误删，25 个经典技能从未被收录**。

### 重新定位方法

`Magic.exp.txt` 的**日文标准名**与 `db_names.json` 的 `ja` 列一致，且 1/2/3 级需求等级
与引擎行 `NeedLevel1..3` **逐条精确吻合**。据此以「日文标准名 + 等级三元组」双向定位，
52/61 唯一闭合；另 2 个由你裁定（瞬息移动→`Teleportation`、移花接玉→`Reflect Damage` 且改归道士）。

### 结果

- **54 个可实现经典技能**：战士 13 / 法师 22 / 道士 19，刺客残留 **0**
- 恢复 10 个被误删行，修正 22 条客户端显示名（`db_names.json`）
- 7 个经典技能在 `ServerLibrary` 中**无 `[MagicType]` 实现**，为避免「扣蓝但无效果」的哑技能，
  不写入 MagicInfo，列入工具输出的 `[待实现]` 清单：
  地狱火（引擎仅有刺客系 `HellFire`）、魄冰刺、怒神霹雳、焰天火雨、云寂术、妙影无踪、阴阳法环

### 验证

`Tools/ClassicMagicFixer dump` 逐字段回读校验 54/54 全绿；服务端 1 秒启动、0 孤儿外键；
实机登录 TestHero 客户端报 `加载已有技能 54 个`，F11 技能面板显示
火球术 / 大火球 / 火墙 / 爆裂火焰。
截图：`docs/screenshots/phase1_magic/`

---

## 二、阶段二：怪物掉落对齐

### 前置修复：被覆盖丢失的消耗品与技能书

审计发现当前 399 件物品中**完全没有药水和技能书**（`ItemType.Book` 173→0、
四级药水全部消失）。追溯备份确认：`pre-dbsync-20261004-094734` 有，
`pre-armour-restore-20261004-105308` 已无——是 10-04 上午的一次库回写把 10-03 清洗结果
连同药水和技能书一起覆盖掉了。若不修复，阶段三的商店与阶段二的「尸王爆高级书」都无从谈起。

从 10-03 清洗后快照恢复 **93 件**（标量字段 + 技能书 `Shape` 重映射到新 `MagicInfo.Index`）：

- 技能书 54 本（按 54 技能白名单，刺客类排除）
- 药水/丸 23 件（四级金创药/魔法药 + 金创丸/魔法丸 + 太阳水/强效太阳水）
- 道士符咒 9 件、肉食 5 件、明亮蜡烛/火把 2 件

现 `ItemInfo` 399 → **492**，刺客物品 0。

### 掉落重建

- 旧 `DropInfo` 1746 行**全部清除**（含私服掉落），悬空外键 0
- 以 `Envir3/MonItems/` 321 张表重建 **5235 行**
- 概率语义一致：Mud3 `1/N` ↔ Zircon `Chance`，运行时 `int.MaxValue / Chance`
- 怪物按 `canonical_identity.json` 的 `zircon_index` 映射（白野猪 → 127 黑野猪，
  因 Zircon 无独立 White Boar 行）
- 物品经三层解析器：ItemInfo 直查 → Mud3 中英别名表（668 条）→ 秘籍后缀 + 技能白名单
- 未映射 1428 次（沃玛/祖玛首饰、五彩项链等经典高阶饰品在当前物品表中确实不存在，优雅跳过）

### 验证（实机）

击杀「尸王」后客户端日志实录：

```
[ObjectView] type=Item name=Gold              drawImage=124
[ObjectView] type=Item name=魔法药（大）        drawImage=17   ×3
[ObjectView] type=Item name=野蛮冲撞            drawImage=304
[Net] 入队: ItemsGained
```

金币、药水、**技能书**三类掉落全部按 Mud3 原爆率落地并成功拾取入包。
数据层另验证：沃玛教主掉 `Golden Bracelet`/`Light Armour (M)`/`Bronze Axe` 等沃玛级装备，
祖玛教主掉 `Robe Of Dark Flame`/`Wyvern Armour` 等祖玛级装备。
截图：`docs/screenshots/phase2_drops/`

---

## 三、阶段三：城镇商店货架与经济

### 审计结论：入口完整但货架全空

`Tools/ClassicMagicFixer npcs` 实测：**24 个商店 NPC 入口与按钮链完整**
（比奇/盟重/潘夜各 8 家：武器·书店·药店·杂货·首饰·收藏·衣服·肉店），
但所有 `* Main` 目标页 `goods=0`——买药/学技能/修装备闭环完全断裂。

### 货架补齐

| 页面 | 数量 | 内容 |
|---|---|---|
| `Basic Potion BuySell` | 16 | 四级金创药/魔法药 + 金创丸/魔法丸 + 太阳水/强效太阳水 |
| `Basic Potion BuySell - Castle` | 10 | 药店（David 实际指向的页） |
| `Essentials BuySell` | 7 | 蜡烛/火把/随机传送/回城 + 三种修理油 |
| `Amulet BuySell` | 9 | 道士符咒全套 |
| `Warrior / Wizard / Taoist Books` | 13 / 22 / 19 | 54 门经典技能书按职业拆分 |
| `Assassin Books` | 0 | **已清空**（经典三职业无刺客） |

`NPCGood` 63 → **154**，未映射物品 **0**。
货架价格自动取 `ItemInfo.Price`（`NPCGood.BaseCost` 是派生只读属性 `Price * Rate`），
金创药 80/200/500/1250 与 Mud3 原价逐条一致。
**沃玛/祖玛级装备未进入任何商店货架**（校验计数 0），仍只能靠阶段二打怪产出。

修理费率沿用引擎既有 Mir 标准公式（`UserItem.RepairCost`：`MaxDurability × (Price/2/Duration) + Price/2`，
特殊修理 ×2），与 Mud3 一致，未作改动。

### 验证（实机）

比奇城药店（David）→ 浏览药水 → 货架显示 `金创药(小) 80 / (中) 200 / (大) 500 /
强效金创药 1250 / 魔法药(小)` → 选中 → 选择数量 → 确定 → 客户端 `ItemsGained`，
金币由 99999908 扣至 **99999828**（-80），药水入包。
书店（艾萨克）13 本战士技能书全部上架（基本剑术/攻杀剑术/刺杀剑术/半月弯刀/烈火剑法…）。
截图：`docs/screenshots/phase3_store/`

---

## 四、总验收 checklist

- [x] **1. 阶段提交完整性**：`e4dc7213` / `31578588` / `175100c5` 三次独立提交，无大泥球
- [x] **2. 数据库一致性**：4 处 `System.db` MD5 全等 `06fa8be7fd60bf89a3e4cc6503b8bf23`，服务端 1 秒启动 0 报错
- [x] **3. 技能纯净度**：`MagicInfo` 54 行（13/22/19），刺客残留 0，无哑技能
- [x] **4. 打怪掉落正常**：尸王爆金币+药水+技能书（实机 ItemsGained）；沃玛/祖玛教主爆对应级装备
- [x] **5. 商店经济闭环**：4 城 24 店货架补齐，价格与 Mud3 一致，实机买药扣款成功
- [x] **6. 截图取证齐全**：`phase1_magic`(2) / `phase2_drops`(3) / `phase3_store`(6)

## 五、遗留（超出本流水线范围，需独立立项）

1. **7 个经典技能无引擎实现**（见阶段一清单）——需新增 `[MagicType]` 子类，
   涉及战斗数值、特效资源与客户端表现，属引擎工程而非数据清洗。
2. **沃玛/祖玛级饰品缺失**（五彩项链、巨龙戒指、天鸣戒指、愤怒之钟、莲花宝镜等 138 个
   Mud3 掉落项在当前物品表中无对应）——10-03 清洗按窄口径裁剪所致。
   要让爆率表 100% 落地，需先把 `ItemInfo` 恢复到完整经典集合。
3. **`Tools/dbeditor/workspace/` 仍是旧状态**（326 行物品）——若执行 `sync.sh` 会再次删除
   阶段一至三的全部成果（这正是本次药水/技能书丢失的机制）。**同步前必须先重导出 workspace。**
