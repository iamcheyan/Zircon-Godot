# 怪物动作实机验收台账

> 本文件由 `~/.hermes/scripts/gen_monster_ledger.py` 从各怪物目录的
> `pilot_log.jsonl` 自动生成。每条结论都对应真实 `Animation`/`FrameIndex` 状态跃迁；
> 未出现的动作不会被记成通过。

| 怪物 | idle | walk | combat | dead | 证据目录（screenshots/ 下） |
| --- | --- | --- | --- | --- | --- |
| Ant Commander | ✅ | ✅ | ✅ | ✅ | `Ant_Commander` |
| Ant Healer | ✅ | ✅ | ❌ | ✅ | `Ant_Healer` |
| Arachnid Broodmother | ✅ | ❌ | ✅ | ✅ | `Arachnid_Broodmother` |
| Arachnid Gazer | ✅ | ❌ | ✅ | ✅ | `Arachnid_Gazer` |
| Armoured Ant | ✅ | ✅ | ✅ | ✅ | `Armoured_Ant` |
| Banya Guardian | ✅ | ✅ | ✅ | ⛔ | `Banya_Guardian` |
| Banya Left Guard | ✅ | ✅ | ✅ | ✅ | `Banya_Left_Guard` |
| Banya Right Guard | ✅ | ✅ | ✅ | ⛔ | `Banya_Right_Guard` |
| Beetle | ✅ | ✅ | ✅ | ✅ | `Beetle` |
| Butterfly Worm | ✅ | ✅ | ✅ | ✅ | `Butterfly_Worm` |
| Carnivorous Plant | — | — | — | — | `Carnivorous_Plant` |
| Cave Bat | ✅ | ✅ | ✅ | ✅ | `Cave_Bat` |
| Cave Maggot | ✅ | ✅ | ✅ | ✅ | `Cave_Maggot` |
| Centipede | ✅ | ✅ | ✅ | ✅ | `Centipede` |
| Claw Cat | ✅ | ✅ | ✅ | ✅ | `Claw_Cat` |
| Cow | ✅ | ⛔ | ⛔ | ✅ | `Cow` |
| Decaying Ghoul | ✅ | ✅ | ✅ | ✅ | `Decaying_Ghoul` |
| Deer | ✅ | ⛔ | ⛔ | ✅ | `Deer` |
| Devouring Ghost | ✅ | ❌ | ✅ | ✅ | `Devouring_Ghost` |
| Emperor Sa'Woo | ✅ | ✅ | ✅ | ⛔ | `Emperor_Sa'Woo` |
| Evil Monkey | ✅ | ✅ | ✅ | ✅ | `Evil_Monkey` |
| Flame Minotaur | ✅ | ✅ | ✅ | ✅ | `Flame_Minotaur` |
| Forest Yeti | ✅ | ✅ | ✅ | ✅ | `Forest_Yeti` |
| GhostSorcerer | ✅ | ❌ | ✅ | ✅ | `GhostSorcerer` |
| Ghoul Champion | — | — | — | — | `Ghoul_Champion` |
| Giant Lizard | ✅ | ✅ | ✅ | ⛔ | `Giant_Lizard` |
| Monkey | ✅ | ✅ | ✅ | ✅ | `Monkey` |
| Mutant Lizard | ✅ | ✅ | ✅ | ✅ | `Mutant_Lizard` |
| Mutant Maggot | ✅ | ✅ | ✅ | ✅ | `Mutant_Maggot` |
| Oma | ✅ | ✅ | ✅ | ✅ | `Oma` |
| Oma Warrior | ✅ | ✅ | ✅ | ❌ | `Oma_Warrior` |
| Pig | ✅ | ⛔ | ⛔ | ✅ | `Pig` |
| Red Moon Guardian | ✅ | ✅ | ✅ | ✅ | `Red_Moon_Guardian` |
| Red Moon Protector | ✅ | ✅ | ✅ | ✅ | `Red_Moon_Protector` |
| Red Moon The Fallen | ✅ | ❌ | ✅ | ⛔ | `Red_Moon_The_Fallen` |
| Rotting Ghoul | ✅ | ✅ | ✅ | ✅ | `Rotting_Ghoul` |
| Sand Shark | ✅ | ✅ | ✅ | ✅ | `Sand_Shark` |
| Scarecrow | ✅ | ❌ | ✅ | ✅ | `Scarecrow` |
| Scorpion | ✅ | ❌ | ✅ | ✅ | `Scorpion` |
| Sheep | ✅ | ⛔ | ⛔ | ✅ | `Sheep` |
| Skeleton | ✅ | ✅ | ✅ | ✅ | `Skeleton` |
| Skeleton Axe Thrower | ✅ | ✅ | ✅ | ✅ | `Skeleton_Axe_Thrower` |
| Skeleton Lord | ✅ | ✅ | ✅ | ❌ | `Skeleton_Lord` |
| Spiked Beetle | ✅ | ✅ | ✅ | ✅ | `Spiked_Beetle` |
| Spined Dark Lizard | ✅ | ✅ | ✅ | ✅ | `Spined_Dark_Lizard` |
| Spitting Spider | ✅ | ✅ | ✅ | ✅ | `Spitting_Spider` |
| Tiger Snake | ✅ | ✅ | ✅ | ✅ | `Tiger_Snake` |
| Uma Anguisher | ✅ | ✅ | ✅ | ✅ | `Uma_Anguisher` |
| Uma Flame Thrower | ✅ | ✅ | ✅ | ✅ | `Uma_Flame_Thrower` |
| Uma Infidel | ✅ | ❌ | ✅ | ✅ | `Uma_Infidel` |
| Uma King | ✅ | ✅ | ✅ | ⛔ | `Uma_King` |
| Vicious Rat | ✅ | ✅ | ✅ | ✅ | `Vicious_Rat` |
| Wasp Hatchling | ✅ | ✅ | ✅ | ❌ | `Wasp_Hatchling` |
| Zuma Fanatic | ❌ | ✅ | ✅ | ✅ | `Zuma_Fanatic` |
| Zuma Guardian | ❌ | ✅ | ✅ | ✅ | `Zuma_Guardian` |
| Zuma Keeper | ❌ | ✅ | ✅ | ⛔ | `Zuma_Keeper` |
| Zuma King | ❌ | ✅ | ✅ | ⛔ | `Zuma_King` |
| Zuma Sharpshooter | ✅ | ✅ | ✅ | ✅ | `Zuma_Sharpshooter` |
| Chicken | ✅ | ⛔ | ⛔ | ✅ | `chicken` |
| Wolf | ✅ | ✅ | ✅ | ✅ | `wolf` |

图例：✅ 采到真实动画帧证据；⛔ 按怪物 AI/地形设计不发生（原因见该目录 `pilot_log.jsonl` 的 `reason`）；❌ 未采到，已进复验队列。

常见 blocked 原因：
- `passive-monster-no-active-chase` / `passive-monster-no-active-attack`：AI=1/2 的被动可收割怪（`Passive=true`），只在 `SeenByPlayers>0` 时 2s 间隔 10% 概率漫步、且不主动攻击玩家。
- `ai-semantics-no-player-attack`：AI=12 `HealerAnt` 只治疗同类（`CanAttackTarget => CanHelpTarget`），玩家不是其目标。
- `too-tanky-for-test-character`：Lv250 级 BOSS 在测试角色正常输出下 90s 内无法击杀，属测试手段限制。
- `no-observed-walking-after-retreat-retry`：BOSS 原地输出或地形堵死，退却/跑开两次重试后仍无 Walking。
