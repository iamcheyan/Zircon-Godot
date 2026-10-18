# 技能/魔法对账报告（网站基准 vs 当前 System.db）

- 日期：2026-10-18
- 范围：`skills`（技能/魔法）侧的**只读**对账；`ItemInfo`/`StoreInfo`/`DropInfo`/`NPCGood` 只做统计，不改库
- 结论口径：**确认缺陷**（有资产/源码/多源互证的实证）> **待人工确认**（需人裁决或证据不足）> **已解释**（清洗/自建/未启用等已知原因）
- 证据等级：本报告为**静态资产级 + 静态源码级**（唯一的人工核对是离线解码图库看图）；实机证据本轮为 0，原因见 §5.5
- 结果机读文件：[.artifacts/game-audit-2026-10-18/data/skills.json](../../.artifacts/game-audit-2026-10-18/data/skills.json)
- 无关项：本报告不涉及登录/地图/NPC 巡检，不改动任何既有文件，未启动客户端或服务端

## 0. 一句话结论

**当前库只有 61 个技能是"可玩"的**（`MagicInfo` 61 行 = 61 本技能书 = 61 个服务端实现类，一一对应），
而 2026-09-25 的旧库还有 **174** 条 —— 也就是说这轮库里**少掉了 113 个技能行**，
其中 **112 个服务端早已写好实现类**（永远学不到、技能窗口也不显示），
另有 **44 个只属于 MUD3 老版 dat 的技能**（本就不该在 Zircon 里）。

留下来的 61 行里有 **8 行数据是坏的**，且**可用 2026-09-25 旧库快照逐条证明是"回退"而不是"口径差异"**：
全部集中在 `MagicInfo.Index = 191..197`（终局 7 个法/道技能）与 `Index = 18`（Reflect Damage）：

| 坏在哪 | 影响 |
|---|---|
| `Index 193/194/196/197` 的 `Icon` 指向 `MIcon.Zl` **不存在的帧**（114/120/138/140；旧库是 218/266/238/240） | 技能窗口与技能栏图标**空白** |
| `Index 195` 的 `Icon=218` 与 193 的正确值**撞车**（旧库 224） | 两个技能图标相同 |
| `Index 1365–1371` 技能书 `Image=70` 是**人参**素材，不是书 | 背包/商店里 7 本书**画成草根** |
| 同 7 本书 `RequiredAmount=0` | 一旦能拿到，**1 级即可学 38~43 级技能**（当前无来源，属"哑弹"） |
| `Index 18` Reflect Damage：旧库=**战士**、网站=战士、实现目录=`Magics/Warrior/`，**当前库却写成道士** | 该技能**只有道士学得到，而实现按战士写** |
| 默认（legacy EI）技能窗口只有 8 个元素页签 | **14 个库内技能（全部战士 13 + Reflect Damage）在窗口里完全看不到** |

## 1. 方法与复现

### 1.1 输入（全部只读）

| 输入 | 路径 | 说明 |
|---|---|---|
| 网站基准 | `Mir3-Research/docs/research/ei-ui-layout/artifacts/web-entity-audit-2026-09-26/raw/wiki_data_v2.json` | `skills` 218 条、`terminology` 1701 条；`_meta.generated_at=2026-08-11`，生成脚本 `ver_tags.py` |
| 库导出 | `/Users/tetsuya/audit-raw/db-export-2026-10-18/out/*.json` | `MagicInfo` 61 / `ItemInfo` 499 / `DropInfo` 5235 / `NPCGood` 932 / `QuestReward` 4 |
| 客户端源码 | `LibraryCore/Enum.cs`、`GodotClient/Controls/MagicDialog.cs`、`GodotClient/Controls/MagicBar.cs`、`GodotClient/translations/db_names.json`、`ClientData/Magic.exp.txt`、`ClientData/zh/zh-glossary.json` | 枚举、页签顺序、EI 技能书文本、中文显示表 |
| 服务端源码 | `ServerLibrary/Models/Magics/<职业>/*.cs`（192 个实现） | `[MagicType(MagicType.X)]` 是"是否实现"的权威标记（`SEnvir.cs:1271` 用它建注册表） |
| 客户端图库 | `/Users/tetsuya/mir2ei/Data/MIcon.Zl`（另交叉 `StoreItem.Zl`/`Inventory.Zl`） | 帧存在性 |
| **旧库快照** | `Mir3-Research/docs/research/ei-ui-layout/magic-icon-metadata-2026-09-25.json` | 2026-09-25 由 `Tools/MagicInfoDump --icons` 从 `Debug/Client/Data/System.db` 导出，含 **174 条** MagicInfo 的 `icon/class/school/need_level1` —— **回归判定的独立佐证** |

导出库与客户端库同一份：`audit-raw/db-export-2026-10-18/db/System.db` 与 `~/mir2ei/Data/System.db`
MD5 均为 `1a5e0e4829abc98d7c6021650fd8c956`。

### 1.2 复现命令

```bash
cd /Users/tetsuya/Development/Zircon
python3.14 tools/audit/skills_audit.py \
  --out .artifacts/game-audit-2026-10-18/data/skills.json
```

脚本纯离线、stdlib-only、不 import 任何生产/生成工具（`zlsdk.py` 等），自带独立的 `.Zl` 元数据解析器。
stdout 打印全部摘要数字；`--assets` 指向别的客户端 Data 目录即可换图库，缺失则资产检查标 `skipped`；
`--old-snapshot` 指向 2026-09-25 的 `MagicInfoDump --icons` 产物即可复现"回归判定"（默认路径已内置），缺失则跳过。

### 1.3 映射规则（先定规则再对账，不猜）

1. **库 ↔ 网站**：`name`/`magic` 归一（小写、去非字母数字）后与网站 `type` 或 `name` 相等即匹配；一条库行只能匹配一条网站条目。结果：**61/61 双向零缺口**（没有歧义或多配）。
2. **网站 `type` → `MagicType` 枚举**：同名归一匹配；`Advanced* ↔ Augment*` 前缀规则（已由
   `Advanced Defiance↔AugmentDefiance`、`Advanced Destructive Surge↔AugmentDestructiveSurge`、
   `Advanced Reflect Damage↔AugmentReflectDamage` 三组条目自证，故 `Advanced Potion Mastery↔AugmentPotionMastery` 是规则推导，不是猜测）。
3. **EI 技能号 = `MagicInfo.Icon // 2 + 1`**：来源 `MagicBar.cs:287`（`1000 + Icon/2`）与
   `MagicDialog.cs:982`（`ID = Icon/2 + 1`）。用它对 `ClientData/Magic.exp.txt` 的 `#id`。
   校验：EI 号 1–41 与 `Magic.exp.txt` 的 41 个段落**一一对应且中文名同族**（火球术/治愈术/基本剑术/攻杀剑术/刺杀剑术/半月弯刀/烈火剑法/莲月剑法…），61 行里 49 行的 EI 号能在 `Magic.exp.txt` 找到。
4. **中文名不投票、不合并**：四源（`magic.exp` / `zh-glossary` / `wiki terminology` / 游戏显示表 `db_names.json`）全部列出，冲突即标"待人工确认"。
5. **图标可解性**：独立实现 `.Zl` 帧索引解析（ZLv0/v1/ZL2 三容器）。与 `zlsdk.py`（独立实现）交叉验证 5 个图库，**可解帧集合完全一致（对称差 0）**：`MIcon.Zl` 224、`GameInter.Zl` 2485、`Inventory.Zl` 2365、`StoreItem.Zl` 2370、`ProgUse.Zl` 392。

### 1.4 基准的效力边界（重要）

`Mir3-Research/Tools/common/ver_tags.py:195-203`：技能的 `ver` 是**先恒置 `["zircon"]`**，
再按 MUD3 老版 dat 是否有同名对应追加 `"mud3"` —— 即 `zircon` 是**基标签**，不是"逐个技能验证过"。
但本次对账发现一个更强的巧合：`wiki_data.json` 的技能集**恰好等于 2026-09-25 旧库的 174 条 MagicInfo**
（名称对称差 0、`icon` 与 `need_level1` 逐条一致），说明百科数据当初是**按那版库**整理的。

结论：**`ver` 含 `zircon` 的 174 条对 2026-09-25 的库是有意义的，对当前库（61 条）无意义** ——
两者之间少了 113 行，这正是本报告最重要的发现。技能 `icon` 字段的可信度较高：
`wiki_images.json`（`SystemDbProbe --images` 探针）优先、缺省才回落百科自身字段。

## 2. 覆盖对账（数字）

| 口径 | 数量 |
|---|---|
| 网站 `skills` 总数 | **218** |
| 网站 `ver` 含 `zircon` | 174 |
| 网站**仅** `mud3`（老版，负 id） | 44 |
| 当前库 `MagicInfo` 行数 | **61** |
| 2026-09-25 旧库快照 MagicInfo 条数 | **174** |
| 旧库快照技能名集 vs 网站 zircon 集，对称差 | **0** |
| 当前库比旧库少的技能行 | **113** |
| 旧库 → 当前库的字段差异（icon/class/school/need_level1） | **9 处** |
| 库 → 网站：库行没有对应网站条目 | **0** |
| 网站(zircon) → 库：无 `MagicInfo` 行 | **113** |
| `MagicType` 枚举玩家段（100–499） | 195 |
| 枚举玩家段中：无网站条目 | 22 |
| 枚举玩家段中：无库行 | 134（= 195 − 61） |
| 服务端 `Magics/**/*.cs` 实现类 | 192（覆盖全部 61 个库内技能 + **112/113** 个库外技能） |

### A. 库内已实现（61 条，逐行对账表）

见第 3 节完整表。

### B. 网站标 zircon、服务端有实现、但库内无 `MagicInfo` 行（113 条）

| 职业 | 条数 |
|---|---|
| 刺客 | 53 |
| 道士 | 27 |
| 战士 | 18 |
| 法师 | 15 |

其中 **112 条服务端已有实现类**（`ServerLibrary/Models/Magics/...`），只有 1 条
`_Blank_`（枚举 `Unused=438`）没有实现。**这 112 条是"代码写好了但永远进不了游戏"的存量**：
玩家学不到（无技能书），技能窗口也不显示（窗口遍历 `Globals.MagicInfoList`）。
旧库快照证明这 113 条**在 2026-09-25 的库里本来都有 MagicInfo 行**（174 条与网站对称差 0）。

| 网站 id | 名称 | 职业 | 枚举 | 服务端实现文件 |
|---|---|---|---|---|
| 2 | Potion Mastery | 战士 | PotionMastery | `ServerLibrary/Models/Magics/Warrior/PotionMastery.cs` |
| 135 | Advanced Potion Mastery | 战士 | AugmentPotionMastery | `.../Warrior/AugmentPotionMastery.cs` |
| 15 | Swift Blade | 战士 | SwiftBlade | `.../Warrior/SwiftBlade.cs` |
| 16 | Assault | 战士 | Assault | `.../Warrior/Assault.cs` |
| 17 | Endurance | 战士 | Endurance | `.../Warrior/Endurance.cs` |
| 19 | Fetter | 战士 | Fetter | `.../Warrior/Fetter.cs` |
| 138 | Mass Beckon | 战士 | MassBeckon | `.../Warrior/MassBeckon.cs` |
| 146 | Invincibility | 战士 | Invincibility | `.../Warrior/Invincibility.cs` |
| 152 | Defensive Mastery | 战士 | DefensiveMastery | `.../Warrior/DefensiveMastery.cs` |
| 21 | Advanced Defiance | 战士 | AugmentDefiance | `.../Warrior/AugmentDefiance.cs` |
| 153 | Physical Immunity | 战士 | PhysicalImmunity | `.../Warrior/PhysicalImmunity.cs` |
| 154 | Magic Immunity | 战士 | MagicImmunity | `.../Warrior/MagicImmunity.cs` |
| 22 | Advanced Reflect Damage | 战士 | AugmentReflectDamage | `.../Warrior/AugmentReflectDamage.cs` |
| 142 | Seismic Slam | 战士 | SeismicSlam | `.../Warrior/SeismicSlam.cs` |
| 20 | Advanced Destructive Surge | 战士 | AugmentDestructiveSurge | `.../Warrior/AugmentDestructiveSurge.cs` |
| 155 | Defensive Blow | 战士 | DefensiveBlow | `.../Warrior/DefensiveBlow.cs` |
| 147 | Crushing Wave | 战士 | CrushingWave | `.../Warrior/CrushingWave.cs` |
| 156 | Elemental Swords | 战士 | ElementalSwords | `.../Warrior/ElementalSwords.cs` |
| 53 | Fire Bounce | 法师 | FireBounce | `.../Wizard/FireBounce.cs` |
| 47 | Chain Lightning | 法师 | ChainLightning | `.../Wizard/ChainLightning.cs` |
| 50 | Tempest | 法师 | Tempest | `.../Wizard/Tempest.cs` |
| 51 | Judgement Of Heaven | 法师 | JudgementOfHeaven | `.../Wizard/JudgementOfHeaven.cs` |
| 131 | Mirror Image | 法师 | MirrorImage | `.../Wizard/MirrorImage.cs` |
| 139 | Frost Bite | 法师 | FrostBite | `.../Wizard/FrostBite.cs` |
| 55 | Superior Magic Shield | 法师 | SuperiorMagicShield | `.../Wizard/SuperiorMagicShield.cs` |
| 56 | Burning | 法师 | Burning | `.../Wizard/Burning.cs` |
| 144 | Asteroid | 法师 | Asteroid | `.../Wizard/Asteroid.cs` |
| 137 | Ice Rain | 法师 | IceRain | `.../Wizard/IceRain.cs` |
| 54 | Elemental Hurricane | 法师 | ElementalHurricane | `.../Wizard/ElementalHurricane.cs` |
| 57 | Shock | 法师 | Shocked | `.../Wizard/Shocked.cs` |

（以上 30 条；**113 条全表（含 IMPL 文件路径）在 `skills.json` 的
`coverage.website_only_zircon_tagged_with_server_impl`**。刺客 53 个技能全部在此列，
即刺客职业在当前库里**一个技能都没有**。）

### C. 枚举玩家段有、网站没有（22 条，"待人工确认"）

`AugmentPotionMastery(122)`、`Shuriken(132)`、`HundredFist(133)`、`OffensiveBlow(134)`、
`TaecheonSword(135)`、`FireSword(136)`、`FlameArt(137)`、`IceAura(243)`、`IceDragon(244)`、
`IceBreaker(245)`、`FrozenDragon(246)`、`UnityWithNature(247)`、`BindingTalisman(347)`、
`BrainStorm(348)`、`HeavenlySky(349)`、`PoisonCloud(350)`、`SupremeHealing(351)`、
`FlamingDaggers(454)`、`Shredding(455)`、`FourWheels(456)`、`CrescentMoon(457)`、`ManaBurn(458)`

（其中 `FlameArt/UnityWithNature/Storm/Tornado/ManaBurn/SupremeHealing` 在 `Enum.cs` 里带 `//NOT CODED` 注释。
`AugmentPotionMastery` 对应网站 id 135，属上文 Advanced* 命名差异。）

### D. 仅 MUD3 老版（44 条，负 id）

44 条全部是 `klass="老版·*"`、`ver=["mud3"]`、id 为 −44…−1 的老版 dat 技能
（云石召唤、幽灵盾（火/冰/雷/风）、通天*、强魔震法、连锁*、聚集*、分散*、分身术、凝血离魂 …）。
**它们不在 `MagicType` 枚举里**，也不该出现在客户端——与库不冲突。全表见 JSON。

## 3. 名称与枚举映射表（61 条，含差异标记）

- 规则见 §1.3。**枚举命中率：61/61（100%）**；**网站条目 type→枚举：174/218（79.8%，未命中的 44 条全是老版中文类型）**。
- **中文名四源统计：一致 22 / 仅单一源 1 / 冲突 38 / 全缺 0**。冲突中 29 条的"游戏显示值"至少与另一源相同。
- **冲突族归类**（不裁决，交人工）：
  1. **EI 用词 vs 现代译名**：如 Dragon Rise = 翔空剑法（EI/magic.exp/游戏显示）vs 龙影剑法（glossary/terminology）；
     Ice Bolt = 冰月神掌 vs 冰箭术；Cyclone = 击风 vs 龙卷风。
  2. **网站侧疑似错挂**：`Evil Slayer` 在 terminology/glossary 记为**召唤骷髅**、`Greater Evil Slayer` 记为**超强召唤骷髅**，
     而 `Magic.exp.txt #37=月魂断玉`、`#38=月魂灵波`、`#105=超强召唤骷髅`，游戏显示表 `db_names.json` 与 EI 一致
     （Evil Slayer=月魂断玉）。→ **错在网站侧，不要按网站改库。**
  3. **游戏显示表内部可疑**（玩家直接看到）：`Destructive Surge→十方斩`、`Beckon→斗转星移`、`Might→破血狂杀`，
     而 glossary 与 terminology 一致认为 `Destructive Surge=破血狂杀`。这 3 条建议人工核对 `db_names.json`。
  4. **纯错字级**：`ScortchedEarth` EI 写作"地域火"，游戏显示"地狱火"。

| # | MagicType | 值 | EI号 | 网站名 | DB Name | 图标 | School | 中文(magic.exp) | 中文(游戏显示) | 默认窗口 | 差异字段 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Swordsmanship | 100 | 3 | Swordsmanship | Swordsmanship | 4 | Passive | 基本剑术 | 基本剑术 | 不可见 |  |
| 3 | Slaying | 102 | 7 | Slaying | Slaying | 12 | Passive | 攻杀剑术 | 攻杀剑术 | 不可见 |  |
| 4 | Thrusting | 103 | 12 | Thrusting | Thrusting | 22 | Toggle | 刺杀剑术 | 刺杀剑术 | 不可见 |  |
| 5 | HalfMoon | 104 | 25 | Half Moon | Half Moon | 48 | Toggle | 半月弯刀 | 半月弯刀 | 不可见 |  |
| 6 | ShoulderDash | 105 | 27 | Shoulder Dash | Shoulder Dash | 52 | Active | 野蛮冲撞 | 野蛮冲撞 | 不可见 |  |
| 7 | FlamingSword | 106 | 26 | Flaming Sword | Flaming Sword | 50 | Active | 烈火剑法 | 烈火剑法 | 不可见 |  |
| 8 | DragonRise | 107 | 35 | Dragon Rise | Dragon Rise | 68 | Active | 翔空剑法 | 翔空剑法 | 不可见 |  |
| 9 | BladeStorm | 108 | 34 | Blade Storm | Blade Storm | 66 | Active | 莲月剑法 | 莲月剑法 | 不可见 |  |
| 10 | DestructiveSurge | 109 | 103 | Destructive Surge | Destructive Surge | 204 | Toggle | — | 十方斩 | 不可见 |  |
| 11 | Interchange | 110 | 107 | Interchange | Interchange | 212 | Active | — | 乾坤大挪移 | 不可见 |  |
| 12 | Defiance | 111 | 102 | Defiance | Defiance | 202 | Active | — | 铁布衫 | 不可见 |  |
| 13 | Beckon | 112 | 108 | Beckon | Beckon | 214 | Active | — | 斗转星移 | 不可见 |  |
| 14 | Might | 113 | 106 | Might | Might | 210 | Active | — | 破血狂杀 | 不可见 |  |
| 18 | ReflectDamage | 117 | 126 | Reflect Damage | Reflect Damage | 250 | Active | — | 移花接玉 | 不可见 | class；cost；exp；levels；power |
| 23 | FireBall | 201 | 1 | Fire Ball | Fire Ball | 0 | Fire | 火球术 | 火球术 | 可见 |  |
| 24 | LightningBall | 202 | 41 | Lightning Ball | Lightning Ball | 80 | Lightning | 霹雳掌 | 霹雳掌 | 可见 |  |
| 25 | IceBolt | 203 | 39 | Ice Bolt | Ice Bolt | 76 | Ice | 冰月神掌 | 冰月神掌 | 可见 |  |
| 26 | GustBlast | 204 | 67 | Gust Blast | Gust Blast | 132 | Wind | 风掌 | 风掌 | 可见 |  |
| 27 | Repulsion | 205 | 8 | Repulsion | Repulsion | 14 | Wind | 抗拒火环 | 抗拒火环 | 可见 |  |
| 28 | ElectricShock | 206 | 20 | Electric Shock | Electric Shock | 38 | Lightning | 诱惑之光 | 诱惑之光 | 可见 |  |
| 29 | Teleportation | 207 | 21 | Teleportation | Teleportation | 40 | Phantom | 瞬间移动 | 瞬息移动 | 可见 |  |
| 30 | AdamantineFireBall | 208 | 5 | Adamantine Fire Ball | Adamantine Fire Ball | 8 | Fire | 大火球 | 大火球 | 可见 |  |
| 31 | ThunderBolt | 209 | 11 | Thunder Bolt | Thunder Bolt | 20 | Lightning | 雷电术 | 雷电术 | 可见 |  |
| 32 | IceBlades | 210 | 40 | Ice Blades | Ice Blades | 78 | Ice | 冰月震天 | 冰月震天 | 可见 |  |
| 33 | Cyclone | 211 | 74 | Cyclone | Cyclone | 146 | Wind | 击风 | 击风 | 可见 |  |
| 36 | FrozenEarth | 214 | 53 | Frozen Earth | Frozen Earth | 104 | Ice | 冰沙掌 | 冰沙掌 | 可见 |  |
| 38 | FireWall | 216 | 22 | Fire Wall | Fire Wall | 42 | Fire | 火墙 | 火墙 | 可见 |  |
| 39 | ExpelUndead | 217 | 32 | Expel Undead | Expel Undead | 62 | Phantom | 圣言术 | 圣言术 | 可见 |  |
| 40 | GeoManipulation | 218 | 104 | Geo Manipulation | Geo Manipulation | 206 | Phantom | 异形换位 | 异形换位 | 可见 |  |
| 41 | MagicShield | 219 | 31 | Magic Shield | Magic Shield | 60 | Phantom | 魔法盾 | 魔法盾 | 可见 |  |
| 42 | FireStorm | 220 | 23 | Fire Storm | Fire Storm | 44 | Fire | 爆裂火焰 | 爆裂火焰 | 可见 |  |
| 43 | LightningWave | 221 | 24 | Lightning Wave | Lightning Wave | 46 | Lightning | 地狱雷光 | 地狱雷光 | 可见 |  |
| 44 | IceStorm | 222 | 33 | Ice Storm | Ice Storm | 64 | Ice | 冰咆哮 | 冰咆哮 | 可见 |  |
| 45 | DragonTornado | 223 | 72 | Dragon Tornado | Dragon Tornado | 142 | Wind | 龙卷风 | 龙卷风 | 可见 |  |
| 49 | Renounce | 227 | 112 | Renounce | Renounce | 222 | Phantom | — | 凝血离魂 | 可见 |  |
| 59 | Heal | 300 | 2 | Heal | Heal | 2 | Holy | 治愈术 | 治愈术 | 可见 |  |
| 60 | SpiritSword | 301 | 4 | Spirit Sword | Spirit Sword | 6 | Physical | 精神力战法 | 精神力战法 | 可见 |  |
| 61 | PoisonDust | 302 | 6 | Poison Dust | Poison Dust | 10 | Dark | 施毒术 | 施毒术 | 可见 |  |
| 175 | LightningBeam | 213 | 10 | Lightning Beam | LightningBeam | 18 | Lightning | 疾光电影 | 疾光电影 | 可见 |  |
| 176 | ExplosiveTalisman | 303 | 13 | Explosive Talisman | ExplosiveTalisman | 24 | Dark | 灵魂火符 | 灵魂火符 | 可见 | exp |
| 177 | EvilSlayer | 304 | 37 | Evil Slayer | EvilSlayer | 72 | Holy | 月魂断玉 | 月魂断玉 | 可见 |  |
| 178 | SummonSkeleton | 332 | 17 | Summon Skeleton | SummonSkeleton | 32 | Phantom | 召唤骷髅 | 召唤骷髅 | 可见 |  |
| 179 | Invisibility | 305 | 18 | Invisibility | Invisibility | 34 | Dark | 隐身术 | 隐身术 | 可见 |  |
| 180 | MagicResistance | 306 | 14 | Magic Resistance | MagicResistance | 26 | Dark | 幽灵盾 | 幽灵盾 | 可见 |  |
| 181 | MassInvisibility | 307 | 19 | Mass Invisibility | MassInvisibility | 36 | Dark | 集体隐身术 | 集体隐身术 | 可见 |  |
| 182 | GreaterEvilSlayer | 308 | 38 | Greater Evil Slayer | GreaterEvilSlayer | 74 | Holy | 月魂灵波 | 月魂灵波 | 可见 |  |
| 183 | Resilience | 309 | 15 | Resilience | Resilience | 28 | Dark | 神圣战甲术 | 神圣战甲术 | 可见 |  |
| 184 | TrapOctagon | 310 | 16 | Trap Octagon | TrapOctagon | 30 | Dark | 困魔咒 | 困魔咒 | 可见 |  |
| 185 | CombatKick | 311 | 36 | Combat Kick | CombatKick | 70 | Physical | 空拳刀法 | 空拳刀法 | 可见 |  |
| 186 | SummonShinsu | 333 | 30 | Summon Shinsu | SummonShinsu | 58 | Phantom | 召唤神兽 | 召唤神兽 | 可见 |  |
| 187 | MassHeal | 313 | 29 | Mass Heal | MassHeal | 56 | Holy | 群体治愈术 | 群体治愈术 | 可见 | exp |
| 188 | SummonJinSkeleton | 334 | 105 | Summon Jin Skeleton | SummonJinSkeleton | 208 | Phantom | 超强召唤骷髅 | 超强召唤骷髅 | 可见 |  |
| 189 | BloodLust | 314 | 94 | Blood Lust | BloodLust | 186 | Dark | 猛虎强势 | 猛虎强势 | 可见 |  |
| 190 | Resurrection | 315 | 77 | Resurrection | Resurrection | 152 | Holy | 回生术 | 回生术 | 可见 |  |
| 191 | ScortchedEarth | 212 | 9 | Scorched Earth | ScortchedEarth | 16 | Fire | 地域火 | 地狱火 | 可见 | cost；power |
| 192 | BlowEarth | 215 | 73 | Blow Earth | BlowEarth | 144 | Wind | 风震天 | 风震天 | 可见 | cost；power |
| 193 | GreaterFrozenEarth | 224 | 58 | Greater Frozen Earth | GreaterFrozenEarth | 114 | Ice | — | 魄冰刺 | 可见 | cost；exp；icon；power；**图标缺帧** |
| 194 | ThunderStrike | 230 | 61 | Thunder Storm | ThunderStrike | 120 | Lightning | — | 怒神霹雳 | 可见 | cost；exp；icon；levels；power；**图标缺帧** |
| 195 | MeteorShower | 226 | 110 | Meteor Shower | MeteorShower | 218 | Fire | — | 焰天火雨 | 可见 | cost；exp；icon；levels；power |
| 196 | Purification | 316 | 70 | Purification | Purification | 138 | Holy | — | 云寂术 | 可见 | cost；exp；icon；levels；power；**图标缺帧** |
| 197 | Transparency | 317 | 71 | Transparency | Transparency | 140 | Phantom | — | 妙影无踪 | 可见 | cost；delay；exp；icon；levels；power；school；**图标缺帧** |

> 表内 "School" 是库值（`MagicInfo.School`）；"默认窗口 不可见" 见 §5.2。
> `Index 195` 的 `Icon=218` 帧**存在**，但与 193 的正确值 218 重复（旧库/网站均为 224），表里只在"差异字段"列体现。

## 4. 字段级差异（能映射上的 61 对）

| 字段（库字段） | 一致 | 不一致 | 网站无 | 库无 |
|---|---|---|---|---|
| `power` → Min/MaxBasePower + Min/MaxLevelPower | 53 | **8** | 0 | 0 |
| `cost` → BaseCost/LevelCost | 53 | **8** | 0 | 0 |
| `delay` → Delay | 60 | **1** | 0 | 0 |
| `levels` → NeedLevel1/2/3 | 56 | **5** | 0 | 0 |
| `exp` → Experience1/2/3 | 53 | **8** | 0 | 0 |
| `class` → RequiredClass | 60 | **1** | 0 | 0 |
| `icon` → Icon | 56 | **5** | 0 | 0 |
| `school` → School | 60 | **1** | 0 | 0 |
| `prop` → Property | 61 | 0 | 0 | 0 |

**所有不一致都落在同样的 8 行上**：`Index 18` 与 `Index 191–197`（共 8 行 / 61 行 = 13.1%）。
其余 53 行与网站基准**逐字段完全一致**——所以这不是"基准不可靠"，而是**定位到一处局部坏数据簇**。

**更关键：这些不一致是"回退"，不是"基准与库的口径差异"。** 用 2026-09-25 的旧库快照
（`magic-icon-metadata-2026-09-25.json`，独立来源、独立工具）比对，得到 9 处字段差异，全部落在同一簇：

| Magic | 字段 | 2026-09-25 旧库 | 当前库(10-18) | 网站 |
|---|---|---|---|---|
| GreaterFrozenEarth | icon | **218** | 114 | 218 |
| MeteorShower | icon | **224** | 218 | 224 |
| ThunderStrike | icon | **266** | 120 | 266 |
| ThunderStrike | need_level1 | **54** | 38 | 54（levels 54/59/64） |
| Purification | icon | **238** | 138 | 238 |
| Transparency | icon | **240** | 140 | 240 |
| Transparency | school | **Dark** | Phantom | Dark |
| ReflectDamage | class | **Warrior** | Taoist | 战士 |
| ReflectDamage | need_level1 | **53** | 48 | 53（levels 53/58/63） |

并且：旧库快照的 **174 条技能名与网站 `ver` 含 zircon 的 174 条对称差 = 0**（完全没有对不上的），
`icon` 与 `need_level1` 逐条一致 → **网站基准是 2026-09-25 库的忠实快照**。
所以 §4 表里的 `power/cost/exp` 差异（191–197）虽无快照字段可比，但与已确认回退的 `icon` 同簇，
判为同一批导入问题（列为"待人工确认"仅因缺第三方旁证）。

> 另一方面：旧库有 **174** 条 MagicInfo，当前库只剩 **61** 条 → 也被删掉了 **113 行**。
> 这是"有意精简"还是"导入遗漏"需要确认（见 §6 P1）。

| # | 技能 | 字段 | 网站 | 当前库 |
|---|---|---|---|---|
| 18 | Reflect Damage | power | 35-35，每级 +40-40 | 30/30，每级 +90/90 |
| 18 | Reflect Damage | cost | 10，每级 +10 | 50，每级 +100 |
| 18 | Reflect Damage | levels | 53/58/63 | **48/51/54**（技能书 `RequiredAmount=53` 与网站一致 → 库内自相矛盾） |
| 18 | Reflect Damage | exp | 10000/18000/32000 | 10000/15000/30000 |
| 18 | Reflect Damage | class | 战士 | **道士**（服务端实现却在 `Magics/Warrior/`） |
| 191 | Scorched Earth | power/cost | 12-16，每级+14-18；15，每级+11 | 8-12，每级+10-14；10/10 |
| 192 | Blow Earth | power/cost | 12-16，每级+13-17；15，每级+13 | 8-12，每级+10-14；10/10 |
| 193 | Greater Frozen Earth | power/cost/exp/icon | 16-20，每级+16-20；20，每级+20；1000/1500/3000；icon 218 | 15/20，每级+15/20；25/20；2000/3000/6000；icon **114** |
| 194 | ThunderStrike | power/cost/levels/exp/icon | 14-18，每级+14-18；30，每级+70；54/59/64；15000/20000/30000；icon 266 | 18/24，每级+16/22；30/25；38/41/44；2000/3000/6000；icon **120** |
| 195 | Meteor Shower | power/cost/levels/exp/icon | 14-22，每级+32-40；40，每级+38；43/45/47；5000/7500/15000；icon 224 | 25/35，每级+20/30；45/35；43/46/49；4000/6000/12000；icon **218** |
| 196 | Purification | power/cost/levels/exp/icon | 10-20，每级+15-30；10，每级+20；38/41/44；1000/1500/3000；icon 238 | 0/0，每级+0/0；20/10；38/40/42；2000/3000/6000；icon **138** |
| 197 | Transparency | power/cost/delay/levels/exp/icon/school | 5-10，每级+5-15；80，每级+120；5000ms；43/45/47；5000/7500/15000；icon 240；school Dark | 0/0；30/20；0ms；43/46/49；4000/6000/12000；icon **140**；school **Phantom** |

> `Index 193–197` 这 5 行与 `191–192` 一起，是**同一批导入**的痕迹（图标编号错位、
> `RequiredAmount=0`、`Image=70` 全部只出现在这个区间）。

## 5. 客户端可用性

### 5.1 技能图标（有实证）

`MIcon.Zl`（`/Users/tetsuya/mir2ei/Data/`，ZLv0 容器）：**声明 1773 帧槽位，实际可解 224 帧**（帧号 0–544 的偶数 + 1003–1010）。
- 库内 61 个 `Icon` 中 **4 个指向不存在的帧**：`114 / 120 / 138 / 140`（Index 193/194/196/197）。
- 客户端两处渲染都是 `if (tex != null)` 才画（`MagicDialog.cs:1079` 列表、`MagicDialog.cs:1444` 详情、`MagicBar.cs:207` 技能栏）→ **不画，槽位只剩分类底板**，表现为"图标空白/灰块"。
- 交叉验证：网站 174 条技能的 `icon` **全部 174/174 都落在可解帧集合内**（含这 5 个差异项的 218/224/266/238/240）。
- 三重佐证（**这是回退，不是口径差异**）：
  1. **旧库快照**：2026-09-25 的同一项目库在这 4 行用的就是 `218/266/238/240`，`Icon=114/120/138/140` 是**新库才出现的值**；
  2. **网站基准**：同样给出 218/266/238/240（且 174 条全在图库内）；
  3. **帧存在性**：114/120/138/140 在 `MIcon.Zl` 里**根本不存在**，而 218/266/238/240 存在且是**正常的 36×36 技能图标**（已渲染核对：218 冰系、224 旋刃、238 双叶、240 条纹盾、266 双箭——都不是空白帧）。
- 反证：`Debug/Client.before-mir2ei-20260925/Data/MIcon.Zl`（改造前副本）同样缺 114/120/138/140 → **不是 ZL 转换丢帧**。
- 未决（已不重要）：本机没有 EI 原版 `MIcon.wil`（`~/mir2ei/LegacyEI/Data/` 里没有），无法直接证 114 在原版 WIL 是否存在。
  但"库指向图库不存在帧"这一事实与 WIL 无关，缺陷本身成立；且旧库快照已给出替换值。

| # | Magic | DB Icon | 该帧在图库? | 网站 icon | 网站帧在图库? |
|---|---|---|---|---|---|
| 193 | GreaterFrozenEarth | 114 | ✗ | 218 | ✓ |
| 194 | ThunderStrike | 120 | ✗ | 266 | ✓ |
| 195 | MeteorShower | 218 | ✓（但已被 193 占用 → 重复） | 224 | ✓ |
| 196 | Purification | 138 | ✗ | 238 | ✓ |
| 197 | Transparency | 140 | ✗ | 240 | ✓ |

建议修复值（**回退到旧库/网站的值；5 个帧在 `MIcon.Zl` 中都已渲染核对为正常技能图标**）：
`193→218`、`194→266`、`195→224`、`196→238`、`197→240`。

### 5.2 技能窗口：默认（legacy EI）布局只显示 8 个元素页签（源码实证）

- 默认 UI 是 legacy：`AutoLoginArgs.cs:279` `LegacyUi => Has("--legacy-ui") || !Has("--zircon-ui")`；
  `LegacyUiSkin.cs:123` 对 `MagicDialog` 调 `ApplyLegacyEiLayout()`。
- legacy 页签固定为 `LegacySchoolOrder = {Fire, Ice, Lightning, Wind, Holy, Dark, Phantom, Physical}`
  （`MagicDialog.cs:39-49`，对应 EI 原版 8 个分类文字 火/冰/电/风/神圣/黑暗/幻影/剑）。
- 技能列表按 `x.Info.School == 当前页签` 过滤（`MagicDialog.cs:671-672`），`School ∈ {None, Discipline}` 另行过滤。
- 结果：**61 个技能里 14 个不会出现在任何页签**：
  - `Passive` 2 个：Swordsmanship、Slaying
  - `Toggle` 3 个：Thrusting、Half Moon、Destructive Surge
  - `Active` 9 个：Shoulder Dash、Flaming Sword、Dragon Rise、Blade Storm、Interchange、Defiance、Beckon、Might、**Reflect Damage**
  - → **战士玩家打开技能书是空列表**；道士的 Reflect Damage 也看不到。
- 源码注释已主动记录此差异（"这是数据分类差异，不是渲染缺陷"）。参考实现 `Client/Scenes/Views/MagicDialog.cs`
  是**按 `MagicInfo.School` 动态建页签**（含 Active/Passive/Toggle，`MagicDialog.cs:291-380`），原版客户端没有这个缺口。
- 用 `--zircon-ui` 启动会走动态页签（可见），但这会让 UI 退出原版 EI 外观 —— **属产品决策，不是纯 bug**。

### 5.3 技能书物品（统计，不改）

61 本 `ItemType=Book`，`Shape` = `MagicInfo.Index`（**61/61 全部可连接，0 个悬挂**）。

| 现象 | 数量 | 明细 |
|---|---|---|
| `Image=70`（主流是 304） | **7** | ItemInfo 1365–1371（ScortchedEarth…Transparency） |
| `RequiredAmount ≠ MagicInfo.NeedLevel1` | **9** | 1282 Defiance(33 vs 44)、1285 Reflect Damage(53 vs 48)、1365–1371（全 0） |
| 掉落/出售/任务奖励三处皆无 | **26** | 见 JSON `client_usability.skill_books.unobtainable_no_drop_no_shop_no_quest` |

**`Image=70` 是真错，已渲染核对**：把 `StoreItem.Zl` 帧 304/70/71 与 `Inventory.Zl` 帧 304/70 解出看图——
**304 是合着的书**，**70/71 是人参（草根）**。帧 70 在整个 `ItemInfo` 里只被这 7 本书 + 1 个消耗品
`Ginseng Of Eternity`（人参）使用。→ 这 7 本书在背包和商店里会画成草根。

**`RequiredAmount=0` 的后果已确认代码路径**：
- 学习条件只看物品：`PlayerObject.cs:7525-7527`（`case RequiredType.Level: if (Level < item.Info.RequiredAmount && Stats[Stat.Rebirth] == 0) return false;`）
- `ItemUse` 的 Book 分支（`PlayerObject.cs:7324-7405`）**完全不检查 `MagicInfo.NeedLevel1`**，
  只检查 `School != None`、已学等级、以及 `Random(100) >= item.CurrentDurability` 的成功率。
- ⇒ 谁拿到 1367 GreaterFrozenEarth 书，**1 级就能学（38 级技能）**。
- **但当前不可达**：这 7 本书 `DropInfo=0`、`NPCGood=0`、`QuestReward=0`。
  （`@make` GM 命令能直接产出来，所以仍是"哑弹"。）

### 5.4 引用完整性（实体引用是否指向存在对象）

| 引用 | 结果 |
|---|---|
| 技能书 `ItemInfo.Shape` → `MagicInfo.Index` | 61/61 命中，**0 悬挂** |
| `MagicInfo.Magic` → `MagicType` 枚举 | 61/61 命中，0 缺失 |
| `MagicInfo.Magic` → 服务端实现类（`[MagicType]`） | 61/61 命中，0 缺失 |
| `ItemEffect=MagicRing` 物品 → `Shape` → `MagicInfo` | **0 个 MagicRing 物品** ⇒ 无技能处于 `ItemRequired` 态（`PlayerObject.cs:2360-2376` 只在该物品效果下置位；`MagicDialog.cs:899-905` 的 MagicRing 分支当前不会触发）—— 属"未启用"，不算缺陷 |
| `MagicInfo.Icon` → `MIcon.Zl` 帧 | 57/61 命中，**4 缺失**（§5.1） |
| 技能书 `ItemInfo.Image` → `Inventory.Zl`/`StoreItem.Zl` 帧 | 61/61 帧存在；但 7 本取到了**错误素材**（§5.3） |

### 5.5 实机证据：本轮为 0（说明）

按审计纪律，"技能窗口/特效画廊截图"属实机证据（`Docs/AUDIT/README.md` §B），
但本次任务明确**不得启动客户端/服务端**（实机巡检正在跑），因此本报告的结论等级为
**静态资产级 + 静态源码级**，唯一例外是 §5.1/§5.3 的**图片素材已离线解码并肉眼核对**（等同资产级实证）。
技能特效画廊可复用既有 `Mir3-Research/Tools/magiclab/batch_run.mjs`（174 技能回归画廊），待巡检结束后跑。

| 优先级 | 类别 | 问题 | 涉及表/字段 | 建议 |
|---|---|---|---|---|
| P0 | 确认缺陷（回归，独立佐证） | 当前库相对 2026-09-25 旧库快照回退 9 处字段值（icon/class/need_level1/school） | `System.db / MagicInfo`(18, 193–197) | 按旧库快照 + 网站值回滚（见 §4 回归表） |
| P0 | 确认缺陷 | `Index 193/194/196/197` 图标指向不存在帧 → 技能图标空白 | `System.db / MagicInfo.Icon` | 改为 218 / 266 / 238 / 240（195 同时改 224 去重）；改后跑一次窗口/快捷栏实机确认 |
| P0 | 确认缺陷 | 默认 UI 下 14 个技能（全部战士 13 + Reflect Damage）技能窗口不显示 | `GodotClient/Controls/MagicDialog.cs`（页签→School 映射） | 二选一：① 显示层把 `Passive/Toggle/Active` 归入"剑"页（并补页签文字）；② 开会讨论是否改 `MagicInfo.School`。**不要只改数据**，会连带影响现代布局分组 |
| P1 | 待人工确认 | 174 → 61：当前库比旧库少 113 条 MagicInfo 行（含刺客 53 个全灭） | `MagicInfo` 缺行 | 确认"有意精简"还是"导入遗漏"；若是遗漏，用旧库快照恢复 `icon/class/school/need_level1`（快照与网站 174 条完全一致） |
| P1 | 确认缺陷 | 7 本书画成人参 | `System.db / ItemInfo.Image`（1365–1371） | 改为 304（与其余 54 本一致） |
| P1 | 确认缺陷 | Reflect Damage 职业三方不一致：当前库=道士 / 旧库=战士 / 网站=战士 / 实现目录=Warrior | `MagicInfo.RequiredClass`(Index 18)、`ItemInfo.RequiredClass`(1285) | 按旧库/实现改回 Warrior，并同步 `NeedLevel1`（旧库 53 = 技能书 53 = 网站 53/58/63） |
| P2 | 确认缺陷（当前不可达） | 7 本书 `RequiredAmount=0` | `ItemInfo.RequiredAmount`（1365–1371） | 改为 20/23/38/38/43/38/43（= `MagicInfo.NeedLevel1`） |
| P2 | 确认缺陷 | 26 本书无任何来源 | `DropInfo` / `NPCGood` | 与策划确认，至少把 P0/P1 涉及的 7 本补上或明确不开放 |
| P2 | 待人工确认 | `Index 191–197` 的 `power/cost/exp` 与网站不符（无第三方旁证） | `MagicInfo` | 与已确认回退的 `icon` 同簇，建议一并按网站/旧库口径核对 |
| P3 | 待人工确认 | 中文名 38 条四源冲突 | 网站 `terminology`/`zh-glossary`、`GodotClient/translations/db_names.json` | 逐条裁决；已定位 3 条**网站侧错挂**（Evil Slayer/Greater Evil Slayer 记成召唤骷髅）、3 条**游戏显示表可疑**（Destructive Surge/Beckon/Might）。**不要按网站改库**，改显示表要单独评估 |
| P3 | 确认缺陷（已回归） | `Index 197` School：旧库=Dark、网站=Dark、**当前库=Phantom** | `MagicInfo.School`(197) | 改回 Dark |
| P3 | 口径 | 网站 `ver` 含 `zircon` 是**基标签**，其 174 条对应的是 2026-09-25 旧库而非当前库 | 文档/对账口径 | 引用网站技能数时注明版本；见 §1.4 |
| 已解释 | 非缺陷 | 44 条「仅 mud3 老版」（负 id、不在 `MagicType` 枚举）+ 22 条「枚举有、网站无」（含 `Unused`=_Blank_ 与 `//NOT CODED` 条目） | 网站基准 / `Enum.cs` | 无需处理；列入以免后续当成缺陷反复排查 |
| 已解释 | 非缺陷 | `ItemEffect=MagicRing` 物品为 0 ⇒ 无技能处于 `ItemRequired` | `ItemInfo` | 该机制在当前库未启用；若将来加 MagicRing 物品，注意 `Shape` 必须等于 `MagicInfo.Index` |

## 7. 不确定项 / 下一步

1. **EI 原版 `MIcon.wil` 未在本机**（`~/mir2ei/LegacyEI/Data/` 只有 GameInter/Interface1c/NPCface/ProgUse/inventory）。
   因此"114/120/138/140 在原版 WIL 里是否存在"未直接证。**但结论已不依赖它**：
   当前库指向图库不存在帧（事实）+ 旧库快照与网站都用 218/266/238/240（双源互证 + 帧已渲染核对为正常图标）。
   若需要字节级闭环，可在 82 或 EI 安装目录用 `wilsdk.py` 复核 `MIcon.wil` 帧 114/120/138/140。
2. **195 Meteor Shower 的 `Icon=218` 帧本身存在**，问题是它与 193 的正确值**重复**；
   旧库快照与网站都指向 224。→ 这 5 个值建议**先用 GM 把所有技能刷一遍实机看一眼再落库**。
3. **`Index 191–197` 的 `power/cost/levels/exp`** 只有在网站基准里有对照（旧库快照不含这些字段），
   所以这部分判"待人工确认"；但 `icon/class/need_level1/school` 的 9 处回退是硬证据，
   建议同一簇一起改，避免只改一半。
4. **未覆盖的获取途径**：宝箱（`LootBoxInfo` 空表）、活动、脚本发放未检查，所以"26 本无来源"是**三处口径**，不等于绝对不可得。
5. **`ItemInfo` 只有 499 行**（旧库/网站基准的口径是 1076），差 577 条。
   技能侧不受影响（61 本书齐全、中文名 0 缺口），但**物品侧对账前必须先确认哪一份才是目标库**。
   这很可能与 174 → 61 是同一件事的两个表现：当前库是"精简/重建"版本。
6. **服务端实现类的职业目录**只证明"代码按哪个职业写"；Reflect Damage 的最终裁决要人（但旧库+网站+实现目录三方一致指向 Warrior）。
7. **旧库快照的 `frames` 字段不能当作 WIL 帧存在性证据**：它把 176/218/224/238/240/266/262 等帧都标成
   `header: null`，而 `MIcon.Zl` 里这些帧确实存在且能解出图。→ 该字段口径未查清，本报告只使用它的 `records`。

## 8. 产物

| 文件 | 说明 |
|---|---|
| [tools/audit/skills_audit.py](../../tools/audit/skills_audit.py) | 只读对账脚本（纯离线、stdlib-only、自带独立 .Zl 解析；`--old-snapshot` 做回归判定） |
| [.artifacts/game-audit-2026-10-18/data/skills.json](../../.artifacts/game-audit-2026-10-18/data/skills.json) | 机读全量结果（含 113 条 + 44 条 + 61 行映射 + 字段差异 + **旧库快照回归判定** + 资产检查 + 全部输入 MD5） |
| `docs/AUDIT/skills.md` | 本报告 |

---

> **现状（2026-10-18 晚，修复后）**：本册记录的是**审计当日的发现**，不是当前状态。
> 各项修复进展、验证方式与仍未做的边界，见总报告 [GAME_AUDIT_2026-10-18.md](../GAME_AUDIT_2026-10-18.md)
> 的「修复台账」（已到第 20 行）与「口径类问题」；过程与教训见 [WORKLOG.md](WORKLOG.md)。
> 与修复相关的关键变化（摘要）：
> - 数据类：580 处清洗回归内容按旧库原值补回；15 只零属性怪物按同等级参照怪推导补属性；
>   16+2 条技能显示名从枚举标识改回旧库显示名；34 条经典任务恢复 `Experience` 令牌奖励 + 反链；
>   怪物名尾空格 `Pachon ` 已 trim。
> - 代码类：跨天行会重置的空引用（及同形状 91 处）、`@monster` 名字含空格刷不出、任务/名望奖励空 Item 守卫、
>   13 字节/格地图、地图名大小写回退、技能窗新学派专属页签、两处「控件被压住/尺寸被清成 0」的点击缺陷。
> - 口径未做（需决策）：被有意清洗的 199 怪物 / 582 物品 / 175 地图 / 8 图 32 行刷怪；
>   3 条空 `Item` 奖励行（旧库无来源）；5–6 个无交互 NPC（缺 MUD3 脚本源）。
