# 技能实机验收台账（自动生成）

> 由 `~/.hermes/scripts/gen_magic_ledger.py` 从 `magic_log.jsonl` 生成。
> 结论都对应真实发生的客户端状态：施法动作 / 释放特效 / 目标受击。

| 技能 | 职业 | 学派 | 施法动作 | 释放特效 | 目标命中 | 结论 |
| --- | --- | --- | --- | --- | --- | --- |
| Heal | Taoist | Holy | ✅ | ❌ | — | ✅  |
| Spirit Sword | Taoist | Physical | — | — | — | 🧩  |
| Poison Dust | Taoist | Dark | ✅ | ✅ | — | ✅  |
| Explosive Talisman | Taoist | Dark | ✅ | ✅ | — | ✅  |
| Evil Slayer | Taoist | Holy | ✅ | ✅ | ✅ | ✅  |
| Summon Skeleton | Taoist | Phantom | ✅ | ✅ | — | ✅  |
| Invisibility | Taoist | Dark | ✅ | ❌ | — | ✅  |
| Magic Resistance | Taoist | Dark | ✅ | ✅ | — | ✅  |
| Mass Invisibility | Taoist | Dark | ✅ | ✅ | — | ✅  |
| Greater Evil Slayer | Taoist | Holy | ✅ | ✅ | ✅ | ✅  |
| Resilience | Taoist | Dark | ✅ | ❌ | — | ✅  |
| Trap Octagon | Taoist | Dark | ✅ | ✅ | — | ✅  |
| Combat Kick | Taoist | Physical | ✅ | ❌ | ✅ | ✅  |
| Summon Shinsu | Taoist | Phantom | ✅ | ❌ | — | ✅  |
| Mass Heal | Taoist | Holy | ✅ | ✅ | — | ✅  |
| Summon Jin Skeleton | Taoist | Phantom | ✅ | ❌ | — | ✅  |
| Blood Lust | Taoist | Dark | ✅ | ✅ | — | ✅  |
| Resurrection | Taoist | Holy | ✅ | ❌ | — | ✅  |
| Purification | Taoist | Holy | ✅ | ✅ | — | ✅  |
| Transparency | Taoist | Phantom | ✅ | ❌ | — | ✅  |
| Swordsmanship | Warrior | Passive | — | — | — | 🧩  |
| Slaying | Warrior | Passive | — | — | — | 🧩  |
| Thrusting | Warrior | Toggle | ❌ | ❌ | ❌ | 🔁  |
| Half Moon | Warrior | Toggle | ❌ | ❌ | ❌ | 🔁  |
| Shoulder Dash | Warrior | Active | ✅ | ❌ | — | ✅  |
| Flaming Sword | Warrior | Active | ❌ | ❌ | ❌ | 🔁  |
| Dragon Rise | Warrior | Active | ❌ | ❌ | ❌ | 🔁  |
| Blade Storm | Warrior | Active | ❌ | ❌ | ❌ | 🔁  |
| Destructive Surge | Warrior | Toggle | ❌ | ❌ | ❌ | 🔁  |
| Interchange | Warrior | Active | ✅ | ✅ | — | ✅  |
| Defiance | Warrior | Active | ✅ | ✅ | — | ✅  |
| Beckon | Warrior | Active | ✅ | ❌ | — | ✅  |
| Might | Warrior | Active | ✅ | ✅ | — | ✅  |
| Reflect Damage | Warrior | Active | ✅ | ✅ | — | ✅  |
| Fire Ball | Wizard | Fire | ✅ | ✅ | ✅ | ✅  |
| Lightning Ball | Wizard | Lightning | ✅ | ✅ | ✅ | ✅  |
| Ice Bolt | Wizard | Ice | ✅ | ✅ | ✅ | ✅  |
| Gust Blast | Wizard | Wind | ✅ | ✅ | ✅ | ✅  |
| Repulsion | Wizard | Wind | ✅ | ❌ | — | ✅  |
| Electric Shock | Wizard | Lightning | ✅ | ❌ | ✅ | ✅  |
| Teleportation | Wizard | Phantom | ✅ | ✅ | — | ✅  |
| Adamantine Fire Ball | Wizard | Fire | ✅ | ✅ | ✅ | ✅  |
| Thunder Bolt | Wizard | Lightning | ✅ | ✅ | ✅ | ✅  |
| Ice Blades | Wizard | Ice | ✅ | ✅ | ✅ | ✅  |
| Cyclone | Wizard | Wind | ✅ | ✅ | — | ✅  |
| Frozen Earth | Wizard | Ice | ✅ | ✅ | ✅ | ✅  |
| Fire Wall | Wizard | Fire | ✅ | ❌ | — | ✅  |
| Expel Undead | Wizard | Phantom | ✅ | ❌ | ✅ | ✅  |
| Geo Manipulation | Wizard | Phantom | ✅ | ✅ | — | ✅  |
| Magic Shield | Wizard | Phantom | ✅ | ✅ | — | ✅  |
| Fire Storm | Wizard | Fire | ✅ | ✅ | — | ✅  |
| Lightning Wave | Wizard | Lightning | ✅ | ❌ | — | ✅  |
| Ice Storm | Wizard | Ice | ✅ | ✅ | — | ✅  |
| Dragon Tornado | Wizard | Wind | ✅ | ❌ | — | ✅  |
| Renounce | Wizard | Phantom | ✅ | ✅ | — | ✅  |
| Lightning Beam | Wizard | Lightning | ✅ | ✅ | ✅ | ✅  |
| Scorched Earth | Wizard | Fire | ✅ | ✅ | — | ✅  |
| Blow Earth | Wizard | Wind | ✅ | ❌ | — | ✅  |
| Greater Frozen Earth | Wizard | Ice | ✅ | ❌ | — | ✅  |
| Thunder Strike | Wizard | Lightning | ✅ | ✅ | — | ✅  |
| Meteor Shower | Wizard | Fire | ✅ | ❌ | — | ✅  |

图例：✅ 通过（施法动作 + 特效/命中）｜🔁 toggle/强化态（走 SendMagicToggle，服务端接受）｜🧩 纯被动（GetPassiveStats 已并入玩家属性）｜⛔ 按设计不发生｜❌ 失败。
