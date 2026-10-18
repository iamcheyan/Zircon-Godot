# 怪物动作实机验收账本（Monster Animation Audit）

目标：对 Zircon 当前实际使用的怪物**逐只**做客户端实机动作验收（站立 / 行走 / 攻击 / 受击 / 死亡），
以真实 `Animation` / `FrameIndex` / `Direction` / `Cell` 状态 + 截图取证；发现缺陷先修复再记录，
每只怪物一组证据、一笔提交。

- 网站候选名单：`https://mir3.iamcheyan.com`（仓库 `~/development/mir3-website/data/monsters.json`）
- 游戏实际使用集合：Zircon `System.db` 的 `MonsterInfo` × `RespawnInfo`，与网站名单按中文名对齐
  （导出工具 `Tools/MonsterAnimInUseExport`，产物 `.artifacts/monster-anim-2026-10-18/in_use_intersection.json`）
- 当前交集：DB 怪物 126 只 → 与网站命中 85 只（`matched=85`，`onlyDb=41`，`onlyWebsite=73`；
  网站 id ≠ DB Index，必须按名字对齐，不能按 id）

## 验收工具

`--monster-pilot`（`GodotClient/Scripts/GameScene.MonsterPilot.cs`）：

```
godot-mono --path GodotClient -- --server 127.0.0.1 --port 7000 \
  --user test@test.com --pass test123 --char TestHero --window \
  --monster-pilot --monster-pilot-only Wolf --monster-pilot-out /tmp/mp_wolf
```

规则（实现即约束）：

1. 只按状态取证：目标真实进入该 `MirAnimation` 且帧/格发生跃迁才截图，**绝不把到时间拍的图当成某动作证据**。
2. 只操作本 pilot 自己 `@monster` 刷出的那一只：以「刷出前后的 ObjectID 差集」锁定，不碰同屏其它怪。
3. 起点地图取目标怪 `RespawnInfo` 里第一个刷怪图，并且**断言传送真的生效**（`MapView.MapFileName`），
   失败即中止——在错误地图继续跑出的“证据”无效。
4. 证据截图前把光照临时抬到 `LightSetting.Light`（仅本会话内存状态，不改 MapInfo/数据库），
   否则夜间图（如 31 祖玛神殿，环境光 0.25）截图全黑，无法辨认动作。
5. 每阶段写一行 JSON 到 `pilot_log.jsonl`（动画/帧/方向/格/时间戳/截图路径），与截图一一对应。

踩过的坑（都已修进实现，勿回退）：

| 现象 | 根因 | 处理 |
| --- | --- | --- |
| `@monster Chicken` 回 `Could not find player: Chicken` | 输入走错链路，被当成玩家名解析 | 改走 `SendChat` 的真实 GM 命令链路 |
| Wolf 全程原地、无追击无攻击 | 起点在比奇城（`0.map`）**安全区**，`MonsterObject.ShouldAttackTarget` 直接 `if (InSafeZone \|\| player.InSafeZone) return false` | 起点改为目标怪的刷怪图（野外） |
| `@move 31 100 100` 无效，仍在比奇城 | 该图没有 100,100，服务端回 `Coords outside of map boundary` | `@move <map>` 不写坐标（服务端取随机可站点）+ 断传送生效 |
| 被动怪（Chicken/Pig，AI=1/2 → `Passive=true`）采不到 walk/combat | 设计使然：只在 `SeenByPlayers>0` 时 2s 间隔 10% 概率漫步，且不主动攻击 | 被动怪 walk/combat 记 `blocked`（附原因），不为它伪造通过 |
| 主动怪 walk 偶发 `false` | 怪物刷出时就贴身，没有走路必要 | 连续单步真实退开 3 格，逼它必须追；以**帧推进**判定行走 |
| `Die` 帧永远抓不到 | 一击秒杀时 `Struck/Died/Remove` 同批到达；且死亡段再 `@gamemaster` 会让怪停下、玩家反而进不了身 | 开打前启动**持续采样协程**，先观察后打击；死亡段不切 GM |
| 截图全黑看不清怪物 | 夜间图光照 0.25 | pilot 会话内临时抬亮（见规则 4） |

## 逐只结果

| # | 怪物 | DB Index | Image / 图库 | AI | 起点图 | idle | walk | combat | dead | 结论 | 证据 |
| --: | --- | --: | --- | --: | --- | --- | --- | --- | --- | --- | --- |
| 1 | Wolf（狼） | 14 | `Wolf` / Mon-7.Zl shape 5 | 3（主动） | 31 祖玛神殿 | ✅ 2 帧 | ✅ 3 帧 | ✅ `Combat2` | ✅ `Die`+`Struck` | **通过**，无缺陷 | `screenshots/` + `screenshots/pilot_log.jsonl` |
| 2 | Chicken（鸡） | 8 | `Chicken` | 1（被动，可收割） | 01 边境城市 | ✅ | ⛔ blocked（被动怪不主动走/攻） | ⛔ blocked | 待测（需玩家击杀采样） | 进行中 | `mp_chicken` 会话 |

> 说明：本站账本只记录**已经跑出真实状态证据**的结论；未测项一律留空，不写成通过。

## 后续待办

1. 被动怪（Chicken/Pig/Deer/Cow/Sheep 等 AI=1/2）：补「玩家击杀 → `Struck`/`Die`」采样，walk/combat 保持 `blocked` 并注明设计原因。
2. 剩余在用怪物按 `in_use_intersection.json` 顺序逐只跑 pilot；每只一组截图 + 账本行 + 单独提交。
3. 特殊形态（多形态 BOSS、`Shinsu` 双形态、`MonsterLookup` 里有额外外观的类型）需要在账本里单列，按实际存在的状态验收。
