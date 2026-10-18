# 清洗回归内容补回补丁（组 1/2/3/4）—— 脚本说明与 dry-run 报告

**脚本**：`tools/restore/restore_cleaned_content.py`（默认 dry-run，只写 `/tmp`）
**预览产物**：`/tmp/restore_patch_preview.json`
**日期**：2026-10-18
**上游审计**：`docs/AUDIT/monsters.md`（R2/R7）、`docs/AUDIT/items.md`（C1/C2/C4）
**机器可读证据**：`.artifacts/game-audit-2026-10-18/data/{monsters,items}.json`

---

## 0. 一句话结论

| 组 | 内容 | 目标对象 | 将写入 | 受阻 |
|---|---|---:|---:|---:|
| 组 1 | `MonsterInfoStat` 为 0 行的 15 只怪物（R2） | 15 | **0** | **15（BLOCKED：无权威数值来源）** |
| 组 2 | 仍在刷怪但掉落被清空的 12 只怪物（R7） | 12 | **382 行 `DropInfo`** | 179 行 |
| 组 3 | 19 件药水 `Health/Mana` + 7 个护身符 `*Affinity`（C1/C2） | 26 | **27 行 `ItemInfoStat`** | 0 |
| 组 4 | 4 件消耗品 `ItemInfo.Shape` 改回 11/12/13/15（C4） | 4 | **4 行 `ItemInfo` 改字段** | 0 |
| — | 派生回链同步（不做会卡 sync 第 4/7 步） | — | **164 处字段更新**（`ItemInfo` 156 + `MonsterInfo` 12） | 0 |
| **合计** | | | **新增 409 行 + 修改 159 行**（168 处字段更新） | **194 条** |

导入器实测（`--verify`，对**补丁前**的库副本跑真写库链路）：**568 处变更**
（409 新增 + 159 个唯一行被 mod；156+12=168 处字段更新落在 159 行上，有 9 行同时改两个字段），
新库版本 `2026.10.18.2`，`compare_sync.py` round-trip 语义一致（见 §6）。
（若跑的时候工作区里还压着**别的任务**的改动，数字会更大：`MagicInfo×6` / `MapRegion×6`
各 +6 —— 别人那次 apply 报的「574 / 580 处」就是这么来的，见 §8.1。）

---

## 1. 范围（严格限定，未越界）

**做**：只补「仍在刷怪 / 仍在使用」的对象，且只补审计已定性的三类回归。

**明确不做**（本次范围外，一行都不动）：

- 202 只被清洗移除的怪物（含 50 BOSS）、975 行刷怪、6467 行掉落（审计 R1，需先裁决口径）；
- 581 件被移除的物品（审计 C6）；`Carrot`/`Masters Emblem`/`Scorpion Tail`/`White Whistle` 这类
  「经典掉落物」本身已被清洗删除 → 对应掉落行进 §5 受阻清单，**不顺手补物品**；
- C3（明亮蜡烛/火把 `Light 25`）：默认不写，`--include-light` 显式开启；
- 重生丹 `ItemReviveTime 300`（C4 配套）：默认不写，`--include-revive-time` 显式开启；
- `StoreInfo` 92→1、套装修复（C5）、技能书 H1、孤儿 `ItemInfoStat` 清理（C7）等其它审计项。

---

## 2. 目标清单与数值来源

### 组 2：怪物掉落（`DropInfo`）—— 来源 = 清洗前旧库全量行

旧库 = `/tmp/preclean_out`（由 `Debug/ServerCore/Database/Backup/System/System 2026-09-25 21-47.db.gz`
解压 + `SystemDbProbe --json` 导出；即审计 §2.1 验证过的「网站基准镜像」）。
`--prepare-old` 可自动重建该导出（只写 `/tmp`）。

怪物身份：旧库与现库 **Index 1:1 一致**（脚本逐只按名字核对，并要求旧库候选 Index 集合唯一）。
物品身份：**按名字匹配**（现库 vs 旧库有 172 件 Index 漂移）——先精确名、再归一化名（去非字母数字+小写），
两者都命中唯一才写；多候选/零命中的行进受阻清单。

| 现库 Index | 怪物 | 旧库行 | 将新增 | 已存在 | 受阻 | 现库原有行 |
|---:|---|---:|---:|---:|---:|---:|
| 10 | Deer | 10 | 8 | 0 | 2 | 0 |
| 25 | Scorpion | 8 | 3 | 0 | 5 | 0 |
| 33 | Ghost Mage | 79 | 50 | 0 | 29 | 0 |
| 34 | Voracious Ghost | 76 | 51 | 0 | 25 | 0 |
| 35 | Devouring Ghost | 76 | 51 | 0 | 25 | 0 |
| 36 | Corpse Raising Ghost | 76 | 51 | 0 | 25 | 0 |
| 71 | Venomous Arachnid | 20 | 16 | 0 | 4 | 0 |
| 89 | Numa Grunt | 23 | 18 | 0 | 5 | 0 |
| 106 | Mutant Lizard | 42 | 16 | 0 | 26 | 0 |
| 119 | Bone Soldier | 72 | 57 | 0 | 15 | 0 |
| 120 | Skeleton Enforcer | 76 | 60 | 0 | 16 | 0 |
| 220 | Sabuk Lord | 3 | 1 | 0 | 2 | 0 |
| **合计** | | **561** | **382** | **0** | **179** | **0** |

行内容 = 旧库该行的 `Chance / Amount / DropSet / PartOnly / EasterEvent` 原值 + `Monster`/`Item`
重指向**现库** Index。382 行里 **377 行按精确名**命中、**5 行按归一化名**命中，明细：

| 怪物 | 旧库物品名 | 现库物品（Index） | 说明 |
|---|---|---|---|
| Ghost Mage | `Blow Earth` | `BlowEarth`（1366） | 现库书名去掉了空格（审计 H8 命名口径） |
| Voracious Ghost | `Blow Earth` | `BlowEarth`（1366） | 同上 |
| Devouring Ghost | `Blow Earth` | `BlowEarth`（1366） | 同上 |
| Corpse Raising Ghost | `Blow Earth` | `BlowEarth`（1366） | 同上 |
| Mutant Lizard | `Meteor Shower` | `MeteorShower`（1369） | 同上 |

> 5 行归一化命中都说清了是同一个物品（同批 7 本技能书里的 2 本，审计 §3 已列明是命名而非缺失），
> 不是模糊匹配。脚本对模糊相似只做**提示**、绝不自动命中。

### 组 3：物品词条（`ItemInfoStat`）—— 来源 = 旧库基线 B（Aug-13）

数值同时从两个旧库读（A = 2026-09-25 备份、B = `Tools/dbeditor/workspace/baseline`），
**两者必须逐条一致**，且必须等于脚本常量表里写死的期望值，否则该条算受阻、不写（防来源漂移）。

| 分组 | 对象数 | 行数 | 明细 |
|---|---:|---:|---|
| C1 药水 | 19 | 20 | Healing Potion I–V `Health 30/70/110/170/250`；Life Pill I–V 同上；Mana Potion I–IV `Mana 40/110/180/250`；Mana Pill I–IV 同上；Rejuvenation Potion (II) `Health 110 + Mana 180`（两行） |
| C2 护身符 | 7 | 7 | Talisman Of Darkness/Fire/Holiness/Ice/Illusions/Lightning/Wind Storm → `Dark/Fire/Holy/Ice/Phantom/Lightning/WindAffinity = 1` |
| **合计** | **26** | **27** | |

现库 Index（全部按名字唯一命中）：药水 1323–1341，护身符 1350–1356。
两套旧库值与脚本期望值 **26/26 全部一致、0 冲突**。

### 组 4：物品 `Shape`（`ItemInfo` 改字段）—— 来源 = 旧库基线 B

| 现库 Index | 物品 | 现库 Shape | 旧库/目标 Shape |
|---:|---|---:|---:|
| 1361 | Superior Repair Oil | 0 | 11 |
| 1362 | Accessory Repair Oil | 0 | 12 |
| 1363 | Armour Repair Oil | 0 | 13 |
| 1364 | Pill Of Reincarnation | 0 | 15 |

守卫条件：**只有现库 Shape 恰为 0** 才覆盖（非 0 视为有意改动 → 受阻、不覆盖）；已是目标值则跳过。

### 组 1：15 只怪物属性行（`MonsterInfoStat`）—— **BLOCKED，默认 0 行**

见 §4。

---

## 3. 落点：写 dbeditor 工作区（+ 一个必须处理的坑）

链路：本脚本改 `Mir3-Research/Tools/dbeditor/workspace/*.json`
→ 在 82 上 `bash Tools/dbeditor/sync.sh`（导入器只应用「工作区 vs `workspace/_baseline`」的差）。

- 新行 `Index` 取「工作区 ∪ `_baseline` 该表最大 Index + 1」起顺序分配：
  `DropInfo` 21517–21898（382 行）、`ItemInfoStat` 4368–4394（27 行）。
  实测导入器**无需重映射**（`Index 自洽`，与 MirDB 计数器一致）。
- 所有 ref 都指向**当前工作区**里存在的对象 Index（脚本自检，悬空即拒绝写）。
- **必须同步派生回链**：`ItemInfo.Drops / ItemInfo.ItemStats / MonsterInfo.Drops /
  MonsterInfo.MonsterInfoStats` 是「子行带 ref 指回父行」的派生 reflist——导入器写库时会**跳过**它们
  （由子行 setter 自动维护），但 `sync.sh` 第 4/7 步的 `compare_sync.py` 会拿工作区与重导出
  **逐行比这些回链的 Index 集合**。工作区若不补，sync 会在第 4/7 步整体失败（本次 `--verify`
  实测踩到：80 处 `ItemInfo#*.Drops 回链集合不一致`）。本补丁因此额外更新 **164 行回链**
  （`ItemInfo` 156 行 = 4 个 Shape 行 + 126 个掉落子行的父物品 + 26 个词条父物品；`MonsterInfo` 12 行）。
  回链元素的 `Name` 用子行 `_Identity` 约定（Probe 的 reflist 元素 Name 就是子行 `_Identity`，
  例如 `"Deer / Deer Meat"`）。

---

## 4. 组 1 为什么是 BLOCKED：数值来源不存在

审计 R2 建议「数值参照基准库同类怪与网站 attrs」。脚本把本机**所有** Zircon 期库副本都查了一遍，
这 15 只在**任何** Zircon 库里都**从来没有过**属性行：

| 库副本 | 怪物数 / 属性行 | 这 15 只的属性行 |
|---|---:|---:|
| `/tmp/preclean_out`（2026-09-25 备份） | 309 / 4117 | **0** |
| `workspace/baseline`（Aug-13，`_baseline` 的来源） | 434 / 4117 | **0** |
| `mir2ei/Backup/clean_165708`（清洗后 116 怪） | 116 / 1048 | **0** |
| `mir2ei/Data/Backup/dbeditor-20260813-215829` | 309 / 4117 | **0** |
| `Development/Debug/ServerCore/Database/System.db`（11.6 MB 大库） | 309 / 4117 | **0** |
| 现库（审计导出） | 126 / 1165 | **0** |

原因：这 15 只是 **EI 迁移新加的怪**（`Tools/DbMigrationTool/Program.cs:1136 ImportMonsters()`），
那批导入**从不写** `MonsterInfoStat`（`data/monsters_import.json` 里也确无 HP/攻防字段）。
所以「旧库原值」对这批对象根本不存在 —— 不是脚本拿不到，是**没有**。

**MUD3 DAT 候选也不能用**（`docs/research/mud3-dat-decoded/monster.json` +
`docs/research/mud3-identity/monsters.json`），两组独立证据：

1. 13 只里 8 条是 `confidence=pending`，mud3-identity 明确写「未定案，**禁止据此写库**」；
2. 等级/体量对不上（同 Appr 也救不了，现库的是后期强化版）：

| 现库怪物 | 现库 Level/Exp | MUD3 候选（置信度） | 候选 Level/Exp | 候选 HP/AC/MR/DC |
|---|---|---|---|---|
| Fierce Wind Demon | 105 / 3,000,000 | #294 潘夜风魔（pending） | 45 / 1200 | 2600 / 5-5 / 11 / 37-56 |
| Flame Demon Soldier | 180 / 4,000,000 | #297 潘夜火魔（pending） | 45 / 1200 | 2600 / 5-5 / 11 / 37-56 |
| Ice Break Demon Soldier | 180 / 4,000,000 | #292 潘夜冰魔（pending） | 45 / 1200 | 2600 / 5-5 / 11 / 37-56 |
| Flame Lion | 55 / 800,000 | #9 火焰狮子（closed） | 48 / 1600 | 3600 / 11-11 / 13 / 50-60 |
| Stone Lion | 55 / 800,000 | #350 石像狮子（closed） | 49 / 1300 | 3800 / 11-11 / 13 / 50-60 |
| Dungeon Goddess 1 | 55 / 800,000 | #67 地牢女神1（closed） | 50 / 1800 | 4000 / 13-13 / 15 / 44-55 |
| Dungeon Goddess 2 | 55 / 800,000 | #69 地牢女神2（closed） | 50 / 1500 | 4100 / 13-13 / 15 / 44-55 |
| Otherworld Sea General | 100 / 3,000,000 | #98 海神将领（pending） | 55 / 2400 | 8200 / 26-26 / 69 / 107-140 |
| Otherworld Poison Demon | 100 / 3,000,000 | #45 爆毒神魔（pending） | 55 / 2600 | 8300 / 23-23 / 53 / 0-0 |
| Otherworld Ship Guard | 100 / 3,000,000 | #343 神舰守卫（pending） | 55 / 3000 | 8700 / 37-37 / 79 / 165-247 |
| Otherworld Red Mage | 100 / 3,000,000 | #113 红衣法师（pending） | 55 / 2800 | 8800 / 17-17 / 53 / 115-148 |
| Otherworld Light Guard | 100 / 3,000,000 | #313 轻甲守卫（pending） | 55 / 2150 | 7700 / 25-25 / 53 / 50-165 |
| Otherworld Tentacle Demon | 100 / 3,000,000 | #56 触角神魔（closed） | 55 / 2500 | 8000 / 20-20 / 53 / 0-0 |
| Fierce Falcon | 105 / 3,000,000 | **无 DAT 条目**（zircon-only） | — | — |
| Otherworld Ship Guard 1 | 100 / 2,500,000 | **无 DAT 条目**（zircon-only） | — | — |

另外**对照组**：把 38 只在 mud3-identity 里 `closed` 且现在库里也有属性行的怪物逐字段对比，
DAT 与现库 **0/38 完全一致**（例：Red Moon The Fallen 现库 Lv250/HP19500 vs DAT Lv93/HP13000）。
结论：MUD3 DAT 不是现库平衡的来源，照抄会凭空造出一套错数值。

→ 因此本组**默认一行不写**，只在预览里列出 15 个对象 + 候选证据（`unresolved_class_1`）。
拿到人工裁决的数值后可用 `--monster-stats-file`（schema 见脚本常量区
`MONSTER_STATS_FILE_SCHEMA`）写入，脚本会校验怪物身份（名字唯一 + Index 一致）。

> 15 只里 **14 只在刷怪**（`Docs/spawn_audit/mobcensus_raw.txt` 可见 D1101/D9021 等图上真实刷出），
> 只有 `Dungeon Goddess 1`(395) 是 0 刷怪 + 0 掉落（可能刻意不用）——它仍列在目标里，但优先级最低。

---

## 5. 受阻清单（194 条）

### 5.1 组 1：15 个对象（见 §4）

### 5.2 组 2：179 行掉落，涉及 73 个物品名

原因：**旧库该物品已被清洗移除**，现库 `ItemInfo`（499 件）里按精确/归一化都找不到唯一目标。
补它们等于顺手恢复 581 件被清洗物品（超出本次范围），因此只报告不写。

按出现次数（行数）排序：

| 次数 | 物品名 | 备注 |
|---:|---|---|
| 12 | Masters Emblem | 经典材料，现库已无 |
| 11 | Carrot | 经典材料（鹿/蝎子的经典掉落），现库已无 |
| 4 × 22 组 | Bloody Flower / Calamity Of Full Moon / Cloak / Discipline / Empowered Evil Slayer / Empowered Explosive Talisman / Empowered Poison Dust / Full Bloom / Ghost Walk / Hell Fire / Pledge Of Blood / Poisonous Cloud / Potion Mastery / Red Lotus / **Scorched Earth** / Summon Puppet / Sweetbrier / Touch Of The Departed / Vine Tree Dance / Waning Moon / White Lotus / Willow Dance / Wraith Grip | 4 只矿区幽灵各自一份：多为被清洗的技能书 |
| 3 | Scorpion Tail | 经典材料，现库已无 |
| 2 × 15 组 | Apprentice's Mask / Assassination Mask / Blue Sword Of Purification / Claw Of Horror / Conqueror's Talon / Death Glaive / Deathblow Talon / Disguise Of The Master / Hexagon Blade / Iron Ring / Master's Glaive / Tunic Of Velocity (M)/(F) / White Whistle | Bone Soldier + Skeleton Enforcer |
| 1 | Odyn Sin/Son/Mythical/Elemental、Crystallized Dark Ore、Pendant Of Luminary、Glaive Of Frozen Heart、Necklace/Bracelet Of Evasion、Rogue's Render、Chain Lightning、Blessed Ring Of Exorcism、Celestial Light、Assault、Tempest、Improved Explosive Talisman、Ambitious *、Swift Blade、Life Steal、Dragon Repulse、Advent Of Demon/Devil、Lance Of Obedience、Dance Of Swallow、Dark Conversion、Abyss、Flash Of Light、Evasion、Raging Wind、Strength Of Faith、Heavy Boots | 主要是 Mutant Lizard / Numa Grunt / Venomous Arachnid / Sabuk Lord 的后期装备 |

**一个需要人裁决的疑似错拼**（脚本只提示，不自动命中）：
旧库 `Scorched Earth`（4 行）在现库最接近的是 `ScortchedEarth`（Index 1365，多了一个 `t`，审计 H8）。
若确认改名/错拼，需要单独决定是否 `ItemInfo.ItemName` 改名（会牵动网站与文案），本补丁不动。

**低覆盖提醒**（恢复比例过低，值不值得再谈口径）：`Sabuk Lord` 1/3（33%）、`Scorpion` 3/8（38%）、
`Mutant Lizard` 16/42（38%）、`Ghost Mage` 50/79（63%）、`Venomous Arachnid` 16/20（80%）、
`Deer` 8/10（80%）；其余 6 只在 74%~79%。鹿的主要产出（`Deer Meat` ×6 档、`Crystal`、`Oil Of Benediction`）已恢复。

---

## 6. 已验证的证据（行为 ≥ 编译）

1. **导入器 round-trip（--verify，真实写库链路，只在 /tmp 副本上跑）**：
   把 System.db 与工作区各复制到 `/tmp`，跑 `DBImporter --no-port-check` →
   **568/568 处变更应用成功、无悬空引用**、新库版本 `2026.10.18.2`；
   再 `SystemDbProbe --json` 重导出 + `compare_sync.py` 对比 → **[OK] round-trip 语义一致**。
   （这一步抓到了下面 §3 的回链坑——只靠 dry-run 看不出来。）
   复现命令（用**补丁前**的库副本，否则现库已有补丁会正当拒绝，见 §8.3）：
   ```bash
   python3 tools/restore/restore_cleaned_content.py --workspace /tmp/ws_prepatch --verify \
     --verify-db /tmp/restore_verify_20261018-150822_imdc6gw3/System.db
   ```
2. **独立对账（不复用脚本逻辑）**：读 round-trip 出来的新库导出，与旧库两份基线按名字对账：
   - 12 只怪物的掉落行**多重集完全相等**（8/3/50/51/51/51/16/18/16/57/60/1）；
   - 26 件物品词条 **26/26** 恢复正确；4 件 `Shape` 正确（11/12/13/15）；
   - `MonsterInfoStat` 行数变化 = **空**（组 1 确实没写）；
   - 掉落行数发生变化的怪物 = **恰好那 12 只**；词条变化的物品 = **恰好那 26 件**；
     Shape 变化的物品 = **恰好那 4 件** → 范围未外溢。
3. **幂等**：`--apply` 连跑两次，第二次输出「无改动（幂等：目标内容已在工作区里），不写文件」；
   重复 dry-run 的 `blocked` 列表逐字节一致。
4. **默认 dry-run 全程只读**：对真工作区反复跑 dry-run，工作区 4 个文件的 md5 全程不变。
5. **现库（15:12 已被写入的 `System.db`）独立复核**：读 `/tmp/curdb_out`（现库导出）重跑同一套
   对账 → 12 只怪物掉落多重集全等、词条 27/27、`Shape` 11/12/13/15、库版本 `2026.10.18.2`、
   15 只怪物仍 0 行属性（组 1 未写）。见 §8.2。
6. **`--verify` 能预测 `sync.sh` 的结果**：在「库已有补丁、工作区仍带差量」的状态下跑
   `--verify`（或真 sync），`compare_sync.py` 报 **409 处「round-trip 多出旧行」**并中止，
   DB 不会被再次写入 —— 这条安全网已实测（见 §8.3）。

---

## 7. 怎么用

> ⚠️ **先读 §8**：补丁已经被别的 agent `--apply` 进工作区（并已入现库、库版本 `2026.10.18.2`）。
> 对**当前**工作区再跑 `--apply` 是幂等的（输出「无改动」）；但**不要**在 `_baseline` 落后于现库时
> 直接跑 `sync.sh`（会被 round-trip 对比拒绝，见 §8.3）。

```bash
cd /Users/tetsuya/Development/Zircon

# 0) 旧库导出缺失时先重建（只写 /tmp，不碰仓库 System.db）
python3 tools/restore/restore_cleaned_content.py --prepare-old

# 1) dry-run（默认；只打印 + 写 /tmp/restore_patch_preview.json）
python3 tools/restore/restore_cleaned_content.py

# 2) 想在真写库链路前验一遍（在 /tmp 副本上跑导入器 + round-trip，不碰真库/真工作区）
python3 tools/restore/restore_cleaned_content.py --verify

# 3) 写工作区（自动把被改文件整份备份到 /tmp/dbeditor_ws_backup_<时间戳>/）
python3 tools/restore/restore_cleaned_content.py --apply

# 3b) 可选：把 C3 照明 / 重生丹 ItemReviveTime 一起补（默认关）
python3 tools/restore/restore_cleaned_content.py --apply --include-light --include-revive-time

# 4) 提交后，在 82 上同步入库
bash /Users/tetsuya/Development/Mir3-Research/Tools/dbeditor/sync.sh
```

**回滚**（把备份里的整份文件拷回工作区，逐文件校验 md5）：

```bash
python3 tools/restore/restore_cleaned_content.py --rollback /tmp/dbeditor_ws_backup_<时间戳>
```

> 注意：`--rollback` 只回退**工作区**。现库（`System.db` 三份镜像）在 15:12 已被写入补丁，
> 回退工作区会让「工作区 vs 现库」不一致（下一次 sync 会重新应用，round-trip 对比也会拦）；
> 要连库一起回退见 §8.3 的字节副本。

其它开关：`--workspace <dir>`（默认真工作区）、`--old-export <dir>`（旧库基线 A 导出）、
`--old-baseline <dir>`（旧库基线 B）、`--out <path>`、`--monster-stats-file <json>`（组 1 数值）、
`--verify-db <path>`（`--verify` 用的源 System.db；要复现历史结论就指向补丁前的库副本）。

---

## 8. 当前状态（2026-10-18 15:2x，**重要：补丁已被别人 apply 并已入现库**）

本节是我这条会话之外的 agent 的动作 + 我的核对结果，逐条可复核。

### 8.1 已发生的事（时间线）

| 时间 | 动作 | 证据 |
|---|---|---|
| 15:09:42 | 有人跑了 `restore_cleaned_content.py --apply`（默认路径） | `/tmp/dbeditor_ws_backup_20261018-150942/`（含 4 个文件的原值）+ `/tmp/restore_patch_preview.json` 里 `mode=apply` |
| 15:11:03 | Mir3-Research 仓库提交 `e1fa3e3a data(dbeditor): 补回三类确认缺陷（580 处变更）` | `git log` / `git show --stat`（该提交同时含别的任务的 `MagicInfo.json`/`MapRegion.json`） |
| 15:12:17 | **现库被写入**：`System.db` 三份 macOS 镜像全部变为 `f7b9d784…`（3397647 B，库版本 `2026.10.18.2`） | 与 `--verify` 里导入器产出的版本号完全一致；导出后 DropInfo 5617 / ItemInfoStat 1564 行 |
| 15:12:17 | 工作区那 4 个文件被重写（内容不变） | mtime |
| — | **`sync.sh` 没有跑过**（第 5 步的 `System-copyN <stamp>.db.gz` 没有生成，最后一个备份还是 2026-10-17 14:12） | `Debug/ServerCore/Database/Backup/System/` 目录清单 |

> 我（本会话）误判为「非预期写入」时回滚过一次工作区（15:1x），随即发现该状态已被提交，
> 于是**按 commit 内容原样恢复**了 4 个文件。当前 `git status` 对这 4 个文件干净，
> `git show HEAD:<file>` 与本脚本默认参数**重新算出的补丁逐字节相同**
> （md5 `0476983d / 2c1ad304 / 6bd81f10 / ed13540d`）——即提交内容 = 本补丁默认形态
> （未含 `--include-light` / `--include-revive-time`）。
> 提交信息里的「580 处变更」= 本补丁 **568 处** + 当时工作区里**另一个任务的** `MapRegion×6`
> 与 `MagicInfo×6`（我用补丁前库副本重跑 `--verify` 得到 580/580，逐表分解对得上）。

### 8.2 现库内容独立复核（读 `/tmp/curdb_out`，不复用脚本逻辑）

- 12 只怪物的掉落行与旧库「现库仍有该物品」的子集**多重集完全相等**（8/3/50/51/51/51/16/18/16/57/60/1）；
- 26 件物品词条 **27/27 行**与旧库基线一致；4 件 `Shape` = 11/12/13/15；
- `MonsterInfoStat`：111/126 只有属性行，**仍是那 15 只 0 行**（组 1 未写，符合设计）。
- 库版本 `2026.10.18.2`（= `--verify` 里导入器写出的版本）。

### 8.3 ⚠️ 遗留隐患：`_baseline` 没跟着更新 → 再跑 sync 会被拒

`workspace/_baseline/` 仍是 **2026-10-17 14:12** 的旧状态（DropInfo 5235 行），而现库已经有补丁。
也就是说工作区相对基线**仍存在那份差量**。此时若再跑 `bash Tools/dbeditor/sync.sh`：

- 导入器会把这 382+27 行**再加一遍**（MirDB 用 counter 分配新 Index，实测 counter 已是 21898 →
  新行落在 21899…22280），然后 `compare_sync.py` 会报 **409 处「round-trip 多出旧行」**
  （382 `DropInfo` + 27 `ItemInfoStat`）→ `set -e` **在第 4/7 步中止，DB 不会被写**（不会重复入库）。
  我用 `/tmp/restore_verify_20261018-151445_mbcd6l_q/` 的产物重放过 `compare_sync.py`，
  确认就是这个结果（409 条 `多出旧行`）。

**正确做法**（二选一，都不必重跑导入）：

```bash
# (A) 让基线追上现库（等价于 sync.sh 第 7 步；之后工作区相对基线无差量）
WS=/Users/tetsuya/Development/Mir3-Research/Tools/dbeditor/workspace
for f in "$WS"/*.json; do
  case "$(basename "$f")" in baseline.json|meta.json|state.json) continue;; esac
  cp "$f" "$WS/_baseline/$(basename "$f")"
done
# 同步更新 baseline.json（版本取现库 2026.10.18.2，md5 取现库 f7b9d784…）

# (B) 或者把工作区回退到基线（撤销补丁），下次 sync 才重新应用
python3 tools/restore/restore_cleaned_content.py --rollback /tmp/dbeditor_ws_backup_20261018-150942
```

**如需把现库整体回退到我打补丁之前**：打补丁前的库字节副本还在
`/tmp/restore_verify_20261018-150822_imdc6gw3/System.db`（以及
`/Users/tetsuya/audit-raw/db-export-2026-10-18/db/System.db`），md5 均为 `1a5e0e48…`（3385647 B）。

### 8.4 我这条会话碰过 / 没碰过的东西

- 未跑 `sync.sh`；未写 `System.db`（15:12 那次写入不是我做的，见 8.1）；
- 未动 `~/mir2ei`（只读过它下面的 `Data/Storeitem.Zl` 之外未使用；实际只读 `System.db` 副本）；
- 未动 `ServerLibrary/`/`GodotClient/`，未启停服务端/客户端；
- 只在 `/tmp` 下写：`restore_patch_preview.json`、`dbeditor_ws_backup_20261018-1509{42,49}/`、
  `restore_verify_*/`、`ws_idem|ws_prepatch|ws_ms|ws_fresh|curdb_out|old_test/`；
- Zircon 仓库里只新增了本脚本与本文件（未 git add/commit）。
- `/tmp` 里留下的证据：`restore_patch_preview.json`（本补丁预览）、
  `dbeditor_ws_backup_20261018-1509{42,49}/`（apply 前备份）、
  `restore_verify_*/`（两次 `--verify` 的全部产物：导入器报告、新库、重导出）。

> 注：`/tmp/restore_patch_preview.json` 是对**打补丁前**工作区快照（`/tmp/ws_prepatch`，
> = 现库 = `_baseline`）算出来的，所以里面是「要补什么」而不是「已经补过 → 0 行」。
> 对当前（已 apply 的）工作区跑 dry-run 会得到 `rows_to_add={}`，这正是幂等性的表现。

---

## 9. 不确定 / 未覆盖（诚实清单）

1. **组 1 没有任何数值**（§4）。这是本次唯一没做完的组，卡点是「来源不存在」而非脚本问题。
   需要人裁决：是补上游 Zircon/原版服务端的平衡值，还是按现库同级怪的体量新定一套（并记录口径）。
   在此之前这 15 只怪（14 只在刷）生命/攻防仍为 0。
2. **「全量恢复」实现为「现库仍存在的物品的子集」**：179/561 行的物品已被清洗移除，
   补它们需要先决定是否恢复那些物品（审计 C6，范围外）。鹿掉不出鹿肉的问题已解决，
   但「Masters Emblem / Carrot / Scorpion Tail」这类经典材料仍缺。
3. **掉落行的 `Chance` 语义未复核**：Zircon 里 `Chance` 是概率分母（越大越难），旧库同一物品
   存在多行不同 `Chance`（例如 Deer Meat 有 1/10/20/30/40/50 六行）。本次照抄旧库原值，
   **没有**重新评估是否与现在的 `DropSet`/摇奖机制自洽（审计 §6.2 也把「2884 行重复登记」
   列为待确认）。若认定旧库那批多行是导入重复，这 382 行需要一起复核。
4. **组 2 的「刷怪仍存在」是按审计/`mobcensus_raw.txt` 的口径**：脚本只复核了
   「现库该怪物有属性行/掉落行为 0」与名字唯一性，**没有**在游戏里逐个确认刷怪点仍生效。
5. **组 4 只改 `Shape` 字段，不改服务端代码**；若项目决定继续用 `Shape=0` + 代码分发，
   则应放弃这 4 行改动（脚本会因「非 0 则不覆盖」的规则尊重任何人工改动）。
6. **重生丹只补了 `Shape=15`**（默认未补 `ItemReviveTime 300`）→ 复活动作会执行但
   `ReincarnationPillTime += 0`。要么加 `--include-revive-time`，要么明确接受半修。
7. **90 天/线上数据未核**：`Users.db` 里玩家背包中这些物品/掉落是否受影响未检查（本次全程只读
   System 侧 JSON）。
8. **`_baseline` 未动**是刻意的（sync.sh 自己会重置它）；但现在它**已落后于现库**，
   见 §8.3 —— 这是当前最需要处理的一条。
9. **`compare_sync.py` 对工作区里的杂项 JSON 很脆**：只要工作区目录里出现一个没有 `"rows"`
   键的 `*.json`（例如把 `_baseline` 之外的备份 `manifest.json` 拷进去），它会在
   `load()` 里 `KeyError: 'rows'` 崩掉，sync 的第 4/7 步会以这个不友好的报错失败。
   我在做快照时踩到过一次（`/tmp/ws_prepatch/manifest.json`），删掉即恢复。
   建议给 dbeditor 提一个「跳过非 probe 输出」的小修（本次未改别人的脚本）。
10. **没有实机验证**：本次没启客户端/服务端，掉落与词条的**游戏内**表现（鹿是否掉鹿肉、
    药水是否回血）未实测；数据级结论只证明「库里与旧库一致」。
