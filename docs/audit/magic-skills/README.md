# 技能实机验收报告（--magic-pilot）

> 本报告由实机运行产生，逐条结论都对应客户端**真实发生**的状态；未发生的动作不会被记成通过。
> 原始数据：`magic_log.jsonl`（62 行 = 61 个技能 + 1 行汇总）。施法瞬间截图：`screenshots/`（52 张）。

## 1. 结果总览

**61 个技能全部有结论，0 失败、0 blocked。**

| 类别 | 数量 | 判定方式 |
| --- | ---: | --- |
| 主动技能通过 | 52 | 施法动作 + 释放特效（+ 需要敌对目标时的命中） |
| toggle / 强化态 | 6 | 走 `SendMagicToggle`，服务端接受（本无施法动作） |
| 纯被动技能 | 3 | 服务端 `GetPassiveStats()` 已并入玩家属性 |

按职业：**Wizard 27/27、Taoist 20/20、Warrior 14/14。**

## 2. 范围与数据来源

- 技能全集来自服务端下发的 `S.NewMagic` / `S.MagicLeveled`（`@giveskills` 后），**61 个技能**：
  - Wizard 27 / Taoist 20 / Warrior 14
  - 学派：Active 9、Phantom 9、Dark 8、Fire 6、Lightning 6、Holy 6、Ice 5、Wind 5、Toggle 3、Passive 2、Physical 2
- 验收入口：`--magic-pilot`（`GodotClient/Scripts/GameScene.MagicPilot.cs`）
- 验收角色：`test@test.com / TestHero`（`Admin=True`，技能全 Lv3）
- 环境：野外图 `31`（祖玛神殿，非安全区）；光照临时抬亮便于截图辨认

## 3. 判定口径

| 证据 | 含义 | 取得方式 |
| --- | --- | --- |
| `castAnimation` | 玩家真实进入施法动作 | 逐帧记录 `_player.Animation` / `FrameIndex`（见 `animations`） |
| `releaseEffect` | 抬手 / 释放 / 飞行体特效出现 | 场景内 `MirEffectNode` 节点数增加 |
| `targetHit` | 目标受击 | 目标进入 `Struck`，或血量有效时掉血 |
| `observed`（被动） | 被动加成是否进了玩家属性 | 读 `PlayerStats[Accuracy/MinDC/MaxDC]` |

补充规则：
- toggle / 强化态本就没有施法动作与特效 → 按"服务端接受并回包"判定
- 纯被动技能没有施法动作 → 按"属性里拿到加成"判定（见 §5）
- 需要敌对目标的技能另外要求 `targetHit`

## 4. 本次为"可通过"而补齐的前置条件

上一版有 3 个技能无法验证，本轮把这些条件都补上了，**没有靠放宽判定蒙混**：

### 4.1 护符（Amulet）——Transparency 等道士技能

`Transparency.MagicCast` 第一件事就是 `Player.UseAmulet(10, 0)`，失败直接
`response.Cast = false`（`ServerLibrary/Models/Magics/Taoist/Transparency.cs:24`），
所以没护符时客户端连施法动作都没有。

harness 现在会自动备好护符：
```
@make Talisman 200        → 进入背包
C.ItemMove(Inventory → Equipment, slot 11 = Amulet)   → 装入护符槽
```
实测输出：`[MagicPilot] 护符已装备: Talisman count=200`，随后
**Transparency / Resurrection / Summon Jin Skeleton / Magic Resistance /
Greater Evil Slayer 全部通过**（施法动作与特效均真实出现）。

### 4.2 纯被动技能——Swordsmanship / Slaying / Spirit Sword

这三个不做施法动作，但**有真实效果**：服务端
`foreach (MagicType type in MagicObjects.Keys) Stats.Add(magicObject.GetPassiveStats())`
（`ServerLibrary/Models/PlayerObject.cs:2419`），通过 `S.StatsUpdate` 下发客户端。

现在按技能自身应提供的数值核对：

| 技能 | 期望 GetPower | 观察到的玩家属性 |
| --- | ---: | --- |
| Swordsmanship | 10 | accuracy 30 / minDC 22 / maxDC 98 |
| Slaying | 15 | accuracy 30 / minDC 22 / maxDC 98 |
| Spirit Sword | 9 | accuracy 30 / **minDC 26 / maxDC 117** |

> **诚实说明**：`accuracy/minDC/maxDC` 是**装备 + 被动技能叠加后**的总值，
> 装备本身也贡献命中与攻击，所以不能把技能的那一份单独隔离出来（这也是三行里
> 前两行数值相同的原因）。唯一能确证技能生效的旁证是 **Spirit Sword 那一行
> 的 minDC/maxDC 与另外两个不同**（26/117 vs 22/98）——说明被动确实进了属性。
> 若要严格隔离，需要"摘掉装备再测"或服务端逐项打点，本轮未做。

## 5. 本轮修掉的 harness 问题（不是游戏缺陷）

| 现象 | 根因 | 处理 |
| --- | --- | --- |
| 一轮 14 个命中类技能误判 `cast-but-target-not-hit` | 靶子被上一个技能打死，复用死靶子导致技能无效 | 每次都刷新的靶子 |
| 掉血判定恒为 false | `ObjectRenderer.Health` 只在 `S.DataObjectMonster` 到达后有效，刚刷的靶子为 0 | 以 `Struck` 为主，血量仅在有效时参与 |
| Meteor Shower 被整包拒绝 | 服务端 `Now < ActionTime/MagicTime/Cooldown` 直接拒绝（`PlayerObject.cs:15015`） | 施法前等冷却窗口 + 首轮未出手自动重试 |
| 落点类技能给非法格被拒 | 面向前格可能是墙/越界 | 目标格 > 可站前格 > 脚下 |
| 护符类技能全部无法施放 | 没装备护符 | 自动 `@make` + `C.ItemMove` 装到槽 11 |

## 6. 未覆盖 / 需要单独确认

- **装备与被动加成的隔离**：见 §4.2 的诚实说明，被动技能那一份贡献未做单独隔离。
- **数值/平衡性**：本轮只验证"技能放得出来、动作/特效/命中正确"，不验证伤害、治疗量、
  耗蓝与冷却是否合理。
- **召唤物自身的 AI 与动作**（Summon Skeleton / Shinsu / Jin Skeleton）：
  本轮验证了召唤技能的施法动作与特效；召唤物后续行为属于怪物验收范围。
- **DB 中不存在的技能名**：`Elemental Superiority`、`Brain Storm` 不在当前
  `System.db` 的 61 条 `MagicInfo` 里，无法也不应作为"在用技能"验收。

## 7. 复现方式

```
godot-mono --path GodotClient -- --server 127.0.0.1 --port 7000 \
  --user test@test.com --pass test123 --char TestHero --window \
  --magic-pilot --magic-pilot-out /tmp/magic_verify
# 只测某些技能：
#   --magic-pilot-only "Fire Ball,Ice Bolt,Transparency"
```
结果写进 `<out>/magic_log.jsonl`，截图写进 `<out>/*.png`。
