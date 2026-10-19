# 技能实机验收报告（--magic-pilot）

> 本报告由实机运行产生，逐条结论都对应客户端**真实发生**的状态；未发生的动作不会被记成通过。
> 原始数据：`magic_log.jsonl`（62 行，61 个技能 + 1 行汇总）。施法瞬间截图：`screenshots/`（52 张）。

## 1. 范围与数据来源

- 技能全集来自服务端下发的 `S.NewMagic`/`S.MagicLeveled`（`@giveskills` 后），**61 个技能**全部覆盖：
  - Wizard 27 / Taoist 20 / Warrior 14
  - 学派分布：Active 9、Phantom 9、Dark 8、Fire 6、Lightning 6、Holy 6、Ice 5、Wind 5、Toggle 3、Passive 2、Physical 2
- 验收入口：`--magic-pilot`（`GodotClient/Scripts/GameScene.MagicPilot.cs`）
- 验收角色：`test@test.com / TestHero`，`@giveskills` 后等级足够、技能全满级（Lv3）
- 验收环境：野外图 `31`（祖玛神殿，非安全区），光照临时抬亮以便截图辨认

## 2. 判定口径（每个技能采三类证据）

| 证据 | 含义 | 取得方式 |
| --- | --- | --- |
| `castAnimation` | 玩家真实进入施法动作 | 逐帧记录 `_player.Animation/FrameIndex`（见 `animations` 数组） |
| `releaseEffect` | 抬手/释放/飞行体特效出现 | 场景内 `MirEffectNode` 节点数增加 |
| `targetHit` | 目标受击 | 目标进入 `Struck` 动作，或在血量有效时掉血 |

判定规则：
- 普通主动技能：`castAnimation` 为真，且（无需敌人时）或（有特效/命中）→ 通过
- Toggle / 强化态：本就没有施法动作与特效 → 走 `SendMagicToggle`，服务端接受即通过
- 被动技能：按语义记 `blocked`，不伪造"施法通过"
- 需要敌对目标的技能：另外要求 `targetHit`

## 3. 总结果

**61 个技能：58 通过、6 个 toggle 验证通过、3 个 blocked、0 个失败。**

按职业：

| 职业 | 技能数 | 通过 | 说明 |
| --- | ---: | ---: | --- |
| Wizard | 27 | 27 | 全部通过 |
| Taoist | 20 | 19 | Transparency 需护符（见下） |
| Warrior | 14 | 12 | Swordsmanship / Slaying 为被动技能 |

### blocked 明细（3 个，均非缺陷）

| 技能 | 学派 | 原因 |
| --- | --- | --- |
| Swordsmanship | Passive | 被动技能，没有施法动作 |
| Slaying | Passive | 被动技能，没有施法动作 |
| Transparency | Phantom | 需要消耗护符；`Player.UseAmulet(10, 0)` 在无护符时直接 `response.Cast = false`（`ServerLibrary/Models/Magics/Taoist/Transparency.cs:24`），属前置道具缺失 |

### toggle / 强化态（6 个，走 `SendMagicToggle` 链路，不发 `C.Magic`）

Thrusting、Half Moon、Flaming Sword、Dragon Rise、Blade Storm、Destructive Surge。
这些技能本就没有施法动作与特效，按"服务端接受并回包"判定通过。

## 4. 验收过程中修掉的 4 处真实问题

### 4.1 怪物普通攻击播错动画（真实客户端缺陷，已修）

`GodotClient` 把怪物普通攻击与远程攻击**都播成 Combat2**。旧端语义是
`S.ObjectAttack → MirAction.Attack → Combat1`、`S.ObjectRangeAttack → Combat2`
（`Client/Models/MonsterObject.cs:2520-2524`）。只配 Combat1 帧表的怪物（异界系等）
攻击时回退到待机动画，表现为"完全看不到攻击动作"。

修复：`ObjectRenderer.PlayAttack()`（Combat1）+ `OnObjectAttack` 用它；
`OnObjectRangeAttack` 改用 `PlayRangeAttack()`（Combat2）。与既有
`MapTestScene` 的 "Attack=Combat1 / Range=Combat2" 断言一致。

> 这一条是上一轮怪物验收发现并修复的；本轮技能验收复用了同一套客户端构建，
> 命中类技能的 `targetHit` 才得以稳定成立。

### 4.2 靶子复用导致命中类技能大面积误判（harness 缺陷）

命中类技能会把靶子打死；复用已死的靶子会让后续技能完全无效，
一轮出现 **14 个技能**误判 `cast-but-target-not-hit`（Fire Ball / Lightning Ball /
Ice Bolt / Gust Blast 等）。改为每次刷新的靶子后归零。

### 4.3 血量比较在 `DataObjectMonster` 到达前恒为 0（harness 缺陷）

`ObjectRenderer.Health` 只在 `S.DataObjectMonster` 到达后才有效，刚刷出的靶子 `Health=0`。
用它做掉血比较会恒为 false。改为以 `Struck` 动作为主，血量只在有效时参与比较。

### 4.4 连续验收被服务端冷却/动作时间整包拒绝（harness 缺陷）

服务端 `C.Magic` 在 `SEnvir.Now < ActionTime || < MagicTime || < Cooldown` 时
整包拒绝（`PlayerObject.cs:15015`），客户端连施法动作都不会有。
连续验收里前一个技能的长动作压住后一个，Meteor Shower（cost 80）曾因此被误判。
现在施法前等待冷却窗口，并在首轮未出手时自动重试一次。

## 5. 未能覆盖 / 需要人工确认的部分

- **护符类技能**（Transparency 等 12 个会调用 `UseAmulet`）：验收角色未装备护符，
  除 Transparency 外本轮仍能通过（其余技能不要求护符或走其它分支），
  但**带上护符后的完整效果**未验证。若需要，可补 `@make Talisman` + 手工装备后重跑。
- **技能伤害数值/治疗量**不在本轮范围：本轮只验证"技能放得出来、动作/特效/命中正确"，
  不验证平衡性数值。
- **召唤物**（Summon Skeleton / Shinsu / Jin Skeleton）验证了施法动作与特效，
  但召唤物自身的 AI 与动作属于怪物验收范围，不在本报告内。

## 6. 复现方式

```
godot-mono --path GodotClient -- --server 127.0.0.1 --port 7000 \
  --user test@test.com --pass test123 --char TestHero --window \
  --magic-pilot --magic-pilot-out /tmp/magic_verify
# 只测某些技能：
#   --magic-pilot-only "Fire Ball,Ice Bolt,Heal"
```
结果写进 `<out>/magic_log.jsonl`，截图写进 `<out>/*.png`。
