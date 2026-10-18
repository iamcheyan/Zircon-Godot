using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Library;
using Library.Network;
using ZirconClient.Scripts;

namespace ZirconClient.Scripts;

/// <summary>
/// 单只怪物受控 pilot（--monster-pilot）。证据规则：
///   1. 只在客户端运行状态（Animation/FrameIndex/Direction/Cell）真实出现目标动作时
///      才写截图；没出现就记 failed，绝不把"到时间拍的图"当成某动作的证据。
///   2. 只操作本 pilot 自己 @monster 生成的那一只（按 ObjectID 锁定）；不攻击、
///      不选择其它对象；不给其它玩家/对象发包。
///   3. 攻击/死亡阶段由 pilot 选中目标后靠 CombatController 顶部自动攻击分支
///      发真实 C.Attack（只打这一只），不手动发包、不误伤同屏其它怪。
///   4. 每阶段写一份 JSON 状态行（时间戳、动画、帧、方向、格坐标、对象 ID），
///      与截图一一对应；截图用 Viewport 图像，不依赖桌面截屏工具。
///   5. 起点必须是**非安全区**野外图：ShouldAttackTarget 对 `InSafeZone` 直接
///      return false（MonsterObject.cs:1487），安全区内怪物永不索敌，chase/combat
///      无法验证（比奇城 0.map 实测踩坑，见账本备注）。
/// </summary>
public partial class GameScene
{
    private bool _pilotStarted;
    internal void StartMonsterPilot() => _ = RunMonsterPilotAsync();

    private ObjectRenderer _pilotTarget;
    private uint _pilotTargetId;
    private bool _pilotLastStageOk;
    private Task _pilotWalkWatcher;
    private readonly List<string> _pilotWalkShots = new();
    private readonly List<string> _pilotCombatShots = new();
    private bool _pilotDieCaptured;
    private string _pilotDieShot;
    private readonly List<Dictionary<string, object>> _pilotLog = new();

    /// <summary>当前客户端加载的地图名（MapView.MapFileName）；MirMap 本身不带文件名。</summary>
    private string PilotCurrentMap => _mapView?.MapFileName ?? "";

    /// <summary>
    /// pilot 证据截图必须能看清怪物：祖玛神殿等图是 Night 光照，环境光只有 0.25，
    /// 截图全黑无法作为动作证据。这里仅在本 pilot 会话内把光照临时抬到 Light，
    /// 不改 MapInfo/数据库，pilot 结束前恢复原值。
    /// </summary>
    private async Task ApplyPilotLightingAsync()
    {
        if (_lightLayer == null) return;
        _lightLayer.SetAuditLightOverride(LightSetting.Light);
        _lightLayer.SetDayTime(1f);
        await NextFrameAsync();
    }

    /// <summary>
    /// 从 RespawnInfo 取该怪第一个刷怪图的 FileName（Wolf=31, Chicken=01, Pig=01）。
    /// 用刷怪图当起点：这些图天然是非安全区野外，且目标怪就在附近活动。
    /// </summary>
    private string RespawnMapFor(string monsterName)
    {
        var info = Globals.MonsterInfoList?.Binding.FirstOrDefault(m => m.MonsterName == monsterName);
        var map = info?.Respawns?
            .Where(r => r.Region?.Map != null)
            .Select(r => r.Region.Map.FileName)
            .FirstOrDefault(f => !string.IsNullOrWhiteSpace(f));
        return map ?? "";
    }

    private async Task RunMonsterPilotAsync()
    {
        string outDir = AutoLoginArgs.MonsterPilotOut;
        try { Directory.CreateDirectory(outDir); }
        catch (Exception ex) { GD.PrintErr($"[MonsterPilot] 无法创建输出目录 {outDir}: {ex.Message}"); return; }

        for (int i = 0; i < 240 && (_mapView?.Map == null || !_startGameShown); i++)
            await NextFrameAsync();
        await WaitSecondsAsync(0.6);

        var names = AutoLoginArgs.MonsterPilotOnly();
        if (names.Count != 1)
        {
            GD.PrintErr($"[MonsterPilot] --monster-pilot-only 必须为 1 个名字; got={names.Count}");
            return;
        }
        string name = names[0];

        GD.Print($"[MonsterPilot] start name={name} out={outDir}");
        // 管理员登录默认 GameMaster=true / Superman=true（Config.cs:158），而
        // @gamemaster/@superman 都是 TOGGLE。这里绝不能发 @gamemaster：一发就会
        // 把 GM 关掉，后续 walk 阶段再发又会打开，导致怪物永远不锁定玩家。
        // 安全区（比奇城 0.map）内 ShouldAttackTarget 直接 return false
        // （MonsterObject.cs:1487），怪物永不索敌 → chase/combat 无法验证。
        // 起点地图从目标怪的 RespawnInfo 第一个刷怪图取（Wolf=31, Chicken=01）。
        string pilotMap = RespawnMapFor(name);
        if (string.IsNullOrEmpty(pilotMap))
        {
            GD.PrintErr($"[MonsterPilot] {name} 无 RespawnInfo 刷怪图，无法选非安全区起点");
            WritePilotLog(outDir, "spawn_failed", new Dictionary<string, object>
            {
                ["name"] = name,
                ["reason"] = "no-respawn-map",
            });
            return;
        }
        GD.Print($"[MonsterPilot] pilot map={pilotMap} (from RespawnInfo)");
        // 证据截图必须看得清怪物；Night 图光照 0.25 会让截图全黑。
        await ApplyPilotLightingAsync();
        // 不带坐标 = map.GetRandomLocation()（MapMove.cs），避免写死坐标越界
        // （实测 `@move 31 100 100` 被服务端拒 "Coords outside of map boundary"）。
        SendChat($"@move {pilotMap}");
        // 传送必须断言生效：轮询 MapView.MapFileName，超时即中止，
        // 绝不允许在错误地图（实测曾留在比奇城）继续跑后续阶段。
        bool moved = await WaitUntilAsync(() =>
            !string.IsNullOrEmpty(PilotCurrentMap)
            && string.Equals(PilotCurrentMap, pilotMap, StringComparison.OrdinalIgnoreCase), 15000.0);
        if (!moved)
        {
            string actual = PilotCurrentMap;
            GD.PrintErr($"[MonsterPilot] @move {pilotMap} 未生效，当前图={actual}；中止 pilot");
            WritePilotLog(outDir, "map_move_failed", new Dictionary<string, object>
            {
                ["name"] = name,
                ["wantMap"] = pilotMap,
                ["actualMap"] = actual,
            });
            return;
        }
        GD.Print($"[MonsterPilot] map ready: {PilotCurrentMap} loc=({_playerLocation.X},{_playerLocation.Y})");
        // 传送后要等服务端把目标图的对象流推完（实测刚切图就 @monster，
        // 新怪虽然出现但同类型计数被地图切换重置，导致伪阴性“未生成”）。
        await WaitSecondsAsync(1.5);

        // ---- 阶段 0：生成并锁定唯一目标 ----
        // 用“新出现的 ObjectID”作真值：@monster 刷在玩家正前格，新 ID 只可能是
        // 本 pilot 刚刷出的那只。半径计数会被地图切换/自然刷新干扰（实测踩过）。
        //
        // `@monster` 刷在玩家**正前格**，那格是墙体/已有怪物时会**静默失败**
        // （Carnivorous Plant 实测踩到）。因此先找一个“自身 + 正前格都通行”
        // 的位点站好，再刷怪；仍失败则最多重试 4 次换点。
        ObjectRenderer spawnedTarget = null;
        for (int attempt = 0; attempt < 4 && spawnedTarget == null; attempt++)
        {
            if (attempt > 0)
            {
                // 没 @turn 命令；用真实走一步换位后重新找空场
                SendMouseMove((MirDirection)(attempt % 8), 1, false);
                await WaitSecondsAsync(0.6);
            }
            await MoveToSpawnableCellAsync();
            var preIds = new HashSet<uint>(_objects.Values
                .Where(o => o?.Type == ObjectRenderer.Kind.Monster && o.MonsterInfo?.MonsterName == name)
                .Select(o => o.ObjectID));
            SendChat($"@monster {name} 1");
            spawnedTarget = await WaitForNewMonsterAsync(name, preIds, 6.0);
            if (spawnedTarget == null)
                GD.Print($"[MonsterPilot] {name} 第 {attempt + 1} 次刷怪未出现新对象，换点重试");
        }
        if (spawnedTarget == null)
        {
            GD.PrintErr($"[MonsterPilot] {name} 未生成（4 次换向重试仍无新 ObjectID）");
            WritePilotLog(outDir, "spawn_failed", new Dictionary<string, object> { ["name"] = name });
            return;
        }
        _pilotTarget = spawnedTarget;
        if (_pilotTarget == null)
        {
            GD.PrintErr($"[MonsterPilot] {name} 生成信号成立但找不到对应 ObjectRenderer");
            return;
        }
        _pilotTargetId = _pilotTarget.ObjectID;
        _pilotDieCaptured = false;
        _pilotDieShot = null;
        GD.Print($"[MonsterPilot] locked target name={name} id={_pilotTargetId} cell=({_pilotTarget.CellX},{_pilotTarget.CellY}) image={_pilotTarget.MonsterInfo?.Image}");
        // 从锁定那一刻起就一直挂一个 Walking 观察器：怪物被其它因素驱动走路时
        // （自然漫步 / 我们没预料到的路径）也不会漏记（Red Boar 等实测 walk 伪失败）。
        _pilotWalkWatcher = WatchForAnimationAsync(outDir, name);
        CaptureMonsterAuditShot(outDir, 0, name + "_spawn");

        // ---- 阶段 1：站立待机（真实 Standing / StoneStanding 帧推进 ≥ 2 帧）----
        // Zuma 系（ZumaGuardian/Fanatic/King）默认以“石像”状态存在，客户端播的是
        // MirAnimation.StoneStanding 而不是 Standing（ObjectRenderer.cs:296-298），
        // 只认 Standing 会误判为 failed。两者都算待机。
        bool IsIdleAnim() => _pilotTarget.Animation is MirAnimation.Standing or MirAnimation.StoneStanding;
        // 刚刷出的怪可能立刻开始追人（Zuma Fanatic 实测 8s 内一直是 Walking），
        // 先给它一个安静的待机窗口：连捕 3 轮，任一轮采到 2 个待机帧即通过。
        for (int idleTry = 0; idleTry < 3 && !_pilotLastStageOk; idleTry++)
        {
            await CaptureStageAsync(outDir, 1, name + "_idle" + (idleTry == 0 ? "" : "_r" + idleTry),
                IsIdleAnim,
                (prev, cur) => cur.Animation == prev.Animation && cur.Frame != prev.Frame,
                2, 6.0);
            if (!_pilotLastStageOk) await WaitSecondsAsync(1.0);
        }

        // ---- 阶段 2：行走 ----
        // 被动怪 (AI=1/2 Passive=true)：只在 SeenByPlayers>0 时 10% 概率漫步，
        // 且不主动追击 → walk 阶段记为 blocked（设计如此，非缺陷），不伪造证据。
        // 主动怪 (AI=0/3 default)：退开 3 格让它自己追过来，观察真实 Walking。
        bool passive = IsPassiveAi(_pilotTarget.MonsterInfo?.AI ?? -1);
        if (passive)
        {
            GD.Print($"[MonsterPilot] {name} AI={_pilotTarget.MonsterInfo?.AI} 被动怪（不主动追击），walk 阶段记为 blocked");
            WritePilotLog(outDir, name + "_walk", new Dictionary<string, object>
            {
                ["stage"] = 2,
                ["label"] = name + "_walk",
                ["targetId"] = _pilotTargetId,
                ["ai"] = _pilotTarget.MonsterInfo?.AI ?? -1,
                ["status"] = "blocked",
                ["reason"] = "passive-monster-no-active-chase",
            });
        }
        else
        {
            // MoveDelay=0 的怪（如 AI=15 的异界海将、Fierce Falcon）服务端
            // `MonsterObject.CanMove => ... && MoveDelay > 0`，永远不会走路；
            // 这是数据/设计，不是动画缺陷 → walk 直接记 blocked，不占用 40s 采样。
            int moveDelay = _pilotTarget.MonsterInfo?.MoveDelay ?? 0;
            if (moveDelay <= 0)
            {
                GD.Print($"[MonsterPilot] {name} MoveDelay=0（服务端不可移动），walk 记为 blocked");
                WritePilotLog(outDir, name + "_walk_status", new Dictionary<string, object>
                {
                    ["stage"] = 2,
                    ["label"] = name + "_walk",
                    ["targetId"] = _pilotTargetId,
                    ["ai"] = _pilotTarget.MonsterInfo?.AI ?? -1,
                    ["moveDelay"] = moveDelay,
                    ["status"] = "blocked",
                    ["reason"] = "monster-cannot-move-moveDelay-zero",
                });
            }
            bool canMove = moveDelay > 0;
            if (!canMove)
            {
                // 不移动的怪不需要退却逼迫，直接进入下一阶段
                goto CombatStage;
            }
            // 管理员登录默认 GM=true；这里只发一次把它关掉，让怪能锁定玩家。
            SendChat("@gamemaster");
            await WaitSecondsAsync(0.3);
            // 不要用 @move 传送退开：切图/传送会重建视野，目标可能直接掉出客户端
            // 对象表（实测 wolf8 walk=false）。更不能只退 1 次：怪物刷出时若已贴身，
            // 它根本不需要走路，就永远采不到 Walking（实测 wolfA，全程 0 次 ObjectMove）。
            // 这里连续单步退开（走步不重建视野），逼怪物必须追才够得着。
            // 退却方向必须逐格选“可站且朝向远离目标”的格子：固定朝一个方向
            // 碰到墙/山体就退不动，怪物没路可追 → walk 伪失败（Scarecrow/
            // Scorpion 实测在祖玛神殿石壁边踩到）。
            for (int step = 0; step < 4; step++)
            {
                var tc = new System.Drawing.Point(_pilotTarget.CellX, _pilotTarget.CellY);
                var best = Functions.DirectionFromPoint(tc, _playerLocation);
                bool went = false;
                for (int rot = 0; rot < 8; rot++)
                {
                    var dir = Functions.ShiftDirection(best, rot % 2 == 0 ? 1 : -1);
                    var next = Functions.Move(_playerLocation, dir, 1);
                    if (_mapView?.Map == null) break;
                    if (next.X <= 1 || next.Y <= 1
                        || next.X >= _mapView.Map.Width - 1 || next.Y >= _mapView.Map.Height - 1) continue;
                    if (_mapView.Map.Cells[next.X, next.Y].Flag) continue;
                    if (Functions.Distance(next, tc) <= Functions.Distance(_playerLocation, tc)) continue;
                    SendMouseMove(dir, 1, false);
                    await WaitSecondsAsync(0.5);
                    went = true;
                    break;
                }
                if (!went) break;
            }
            var startCell = new System.Drawing.Point(_pilotTarget.CellX, _pilotTarget.CellY);
            await CaptureStageAsync(outDir, 2, name + "_walk",
                () => _pilotTarget.Animation == MirAnimation.Walking,
                // 走路帧表中的 CellX/CellY 在 StartMove 时已直接跳到终点，
                // “帧推进”才是稳定的步行证据；格变化作为附加信息记到 shot 行。
                (prev, cur) => cur.Animation == MirAnimation.Walking
                    && cur.Frame != prev.Frame, 2, 20.0,
                new Dictionary<string, object> { ["fromCell"] = $"{startCell.X},{startCell.Y}" });

            // 倒退失败时（怪已经贴身且不需要追、或周围都是墙，Uma Infidel 实测）
            // 再补一次：重新靠近目标贴身，然后立即大步跑开 2 格，逼它重新进入走路。
            bool walkOk = _pilotLastStageOk;
            if (!walkOk)
            {
                for (int i = 0; i < 30; i++)
                {
                    if (!IsInstanceValid(_pilotTarget)) break;
                    if (Functions.Distance(_playerLocation,
                            new System.Drawing.Point(_pilotTarget.CellX, _pilotTarget.CellY)) <= 1) break;
                    await NextFrameAsync();
                }
                var runDir = Functions.DirectionFromPoint(_pilotTarget.CellX == _playerLocation.X
                        ? new System.Drawing.Point(_pilotTarget.CellX - 1, _pilotTarget.CellY)
                        : new System.Drawing.Point(_pilotTarget.CellX, _pilotTarget.CellY), _playerLocation);
                SendMouseMove(runDir, 2, true);
                await WaitSecondsAsync(0.5);
                await CaptureStageAsync(outDir, 2, name + "_walk2",
                    () => _pilotTarget.Animation == MirAnimation.Walking,
                    (prev, cur) => cur.Animation == MirAnimation.Walking
                        && cur.Frame != prev.Frame, 2, 20.0,
                    new Dictionary<string, object> { ["retry"] = true });
                // 跑开也看不到走路：BOSS 类（Lv250）通常原地攻击不移动，或地形堵死。
                // 这是怪物行为/地形限制，不是动画缺陷 → 按 blocked 记录并注明原因。
                // 兜底：后台观察器可能已经在别处抓到过真实 Walking 帧
                if (!_pilotLastStageOk && _pilotWalkShots.Count > 0)
                {
                    WritePilotLog(outDir, name + "_walk", new Dictionary<string, object>
                    {
                        ["stage"] = 2,
                        ["label"] = name + "_walk",
                        ["targetId"] = _pilotTargetId,
                        ["ok"] = true,
                        ["hits"] = _pilotWalkShots.Count,
                        ["shots"] = _pilotWalkShots,
                        ["source"] = "background-walk-watcher",
                    });
                    _pilotLastStageOk = true;
                }
                if (!_pilotLastStageOk)
                {
                    WritePilotLog(outDir, name + "_walk_status", new Dictionary<string, object>
                    {
                        ["stage"] = 2,
                        ["label"] = name + "_walk",
                        ["targetId"] = _pilotTargetId,
                        ["ai"] = _pilotTarget?.MonsterInfo?.AI ?? -1,
                        ["level"] = _pilotTarget?.MonsterInfo?.Level ?? 0,
                        ["status"] = "blocked",
                        ["reason"] = "no-observed-walking-after-retreat-retry",
                    });
                    GD.Print($"[MonsterPilot] {name} 退却+跑开重试仍无 Walking，记为 blocked");
                }
            }
        }

        CombatStage:
        // ---- 阶段 3：攻击 ----
        // 被动怪：记为 blocked（CanRetaliate 通常仍会反击，但仅在玩家先打它时；
        // 由阶段 4 的玩家击杀驱动，真实 Struck/Die 已覆盖受击证据）。
        // 主动怪：关 GM 模式 + @superman，怪会自己追上来攻击玩家。
        if (passive)
        {
            int ai = _pilotTarget.MonsterInfo?.AI ?? -1;
            string reason = IsNonAttackingAi(ai)
                ? "ai-semantics-no-player-attack"
                : "passive-monster-no-active-attack";
            GD.Print($"[MonsterPilot] {name} AI={ai} 不主动攻击玩家，combat 阶段记为 blocked（{reason}）");
            WritePilotLog(outDir, name + "_combat", new Dictionary<string, object>
            {
                ["stage"] = 3,
                ["label"] = name + "_combat",
                ["targetId"] = _pilotTargetId,
                ["ai"] = ai,
                ["status"] = "blocked",
                ["reason"] = reason,
            });
        }
        else
        {
            // GM 已在 walk 阶段关掉（保持 off），不再重复 toggle。
            // @superman 也不能发：默认已是无敌，再发会关掉无敌导致测试角色被打死。
            // 不可移动的远程怪（MoveDelay=0，如异界红魔）必须站在其射程内才会出手；
            // 先贴近到相邻再采样；仍采不到时再退开 1 格（它的远程范围）重试。
            await CaptureStageAsync(outDir, 3, name + "_combat",
                () => IsCombatAnim(_pilotTarget.Animation),
                (prev, cur) => IsCombatAnim(cur.Animation)
                    && (cur.Animation != prev.Animation || cur.Frame != prev.Frame), 1, 30.0);
            if (!_pilotLastStageOk && _pilotCombatShots.Count > 0)
            {
                WritePilotLog(outDir, name + "_combat", new Dictionary<string, object>
                {
                    ["stage"] = 3,
                    ["label"] = name + "_combat",
                    ["targetId"] = _pilotTargetId,
                    ["ok"] = true,
                    ["hits"] = _pilotCombatShots.Count,
                    ["shots"] = _pilotCombatShots,
                    ["source"] = "background-combat-watcher",
                });
                _pilotLastStageOk = true;
            }
            if (!_pilotLastStageOk)
            {
                var back = Functions.DirectionFromPoint(
                    new System.Drawing.Point(_pilotTarget.CellX, _pilotTarget.CellY), _playerLocation);
                SendMouseMove(back, 1, false);
                await WaitSecondsAsync(0.6);
                await CaptureStageAsync(outDir, 3, name + "_combat_retry",
                    () => IsCombatAnim(_pilotTarget.Animation),
                    (prev, cur) => IsCombatAnim(cur.Animation)
                        && (cur.Animation != prev.Animation || cur.Frame != prev.Frame), 1, 30.0,
                    new Dictionary<string, object> { ["retry"] = true });
            }
        }

        // ---- 阶段 4：死亡（先观察后打击）----
        // 一击秒杀时 ObjectStruck/ObjectDied/ObjectRemove 同批到达，Die 帧永远抓不到。
        // 因此先启动持续采样协程（记录 Combat*/Struck/Die 每次 (anim,frame,cell)
        // 跃迁并立即截图），再让 CombatController 顶部自动攻击分支真实出手。
        //
        // 实测两种失败模式：
        //  a) 再 @gamemaster 把怪停下 → 玩家进不了相邻格，60~70s 杀不死（只 1 次 C.Attack）；
        //  b) 完全不切 GM 且怪每 800ms 攻击 → 2026-10-19 Oma Warrior 只发出极少次攻击、
        //     175 次 combat / 14 次 struck 却没死（怪的攻击反复打断玩家的出手）。
        // 因此这里折中：**先把目标打到相邻格并确认，再开打**，并在 60s 内
        // 每 3 秒重发一次目标选中（ObjectRemove/受击可能清掉选中），直到死亡或超时。
        // 不 @superman：默认无敌已足够，toggle 反而会关掉无敌。
        var sampler = RunTransitionSamplerAsync(outDir, name);
        await WaitUntilAsync(() =>
            Functions.Distance(_playerLocation, new System.Drawing.Point(_pilotTarget.CellX, _pilotTarget.CellY)) <= 1, 20000.0);
        double killDeadline = Godot.Time.GetTicksMsec() + 90000.0;
        while (Godot.Time.GetTicksMsec() < killDeadline)
        {
            if (!IsInstanceValid(_pilotTarget) || _pilotTarget.Dead) break;
            // 追击/受击可能清掉选中，周期性重新选中（只选本 pilot 自己那只）。
            _combatController.TargetObject = _pilotTarget;
            await WaitSecondsAsync(3.0);
        }
        _combatController.TargetObject = null;
        await WaitSecondsAsync(1.0);
        var deathRow = await sampler;
        // 高级/高血量怪（如 Lv250 BOSS）在测试角色的正常输出下不能在 90s 内击杀，
        // 这是测试手段的限制而不是动画缺陷。未采到 Die 且目标仍活着时，记为
        // blocked（附原因）而不是 FAIL，避免把“打不死”误判成“死亡动画错”。
        // 一击秒杀时逐帧采样来不及看到 Die 帧；OnObjectDied 里有同步瞬时截图兜底。
        if (_pilotDieCaptured && !string.IsNullOrEmpty(_pilotDieShot))
        {
            deathRow["dieInstantShot"] = _pilotDieShot;
            deathRow["ok"] = true;
        }
        if (deathRow.TryGetValue("dieHits", out var dhObj) && dhObj is int dh && dh == 0
            && IsInstanceValid(_pilotTarget) && !_pilotTarget.Dead)
        {
            deathRow["ok"] = false;
            deathRow["status"] = "blocked";
            deathRow["reason"] = "too-tanky-for-test-character";
            GD.Print($"[MonsterPilot] {name} 90s 内未被击杀（高耐久），死亡段记 blocked 而非缺陷");
        }
        WritePilotLog(outDir, name + "_dead", deathRow);

        // ---- 阶段 5：尸体/消失 ----
        bool removed = await WaitForRemovalAsync(90.0);
        if (removed) GD.Print($"[MonsterPilot] {name} 尸体已移除");
        else GD.PrintErr($"[MonsterPilot] {name} 90s 内未观察到尸体移除（DeadDuration 默认 1min，超时仅记录，不算动画缺陷）");

        WritePilotLog(outDir, "summary", new Dictionary<string, object>
        {
            ["name"] = name,
            ["targetId"] = _pilotTargetId,
            ["image"] = _pilotTarget?.MonsterInfo?.Image.ToString(),
            ["ai"] = _pilotTarget?.MonsterInfo?.AI ?? -1,
            ["passive"] = passive,
            ["removed"] = removed,
        });
        GD.Print($"[MonsterPilot] done name={name} out={outDir}");
        _pilotDisposed = true;
        // 恢复原本的地图光照（pilot 只在自身会话内临时抬亮）
        if (_lightLayer != null)
        {
            _lightLayer.SetAuditLightOverride(null);
        }
    }

    /// <summary>
    /// 后台动画观察器：pilot 全程一直跑，目标只要出现新的 Walking / Combat* 帧就立即截图留证。
    /// 兜底两类漏记：(1) 怪物自己走动发生在采样窗口外；(2) 慢出手的怪（AttackDelay 大、
    /// 或要等玩家靠近）在 combat 采样窗口结束后才攻击 —— Flame Lion 实测 60s 窗口过后
    /// 才发出 ObjectAttack。
    /// </summary>
    private async Task WatchForAnimationAsync(string outDir, string name)
    {
        int prevWalkFrame = -1, prevCombatFrame = -1;
        MirAnimation? prevCombatAnim = null;
        while (!_pilotDisposed && IsInstanceValid(_pilotTarget))
        {
            await NextFrameAsync();
            if (!IsInstanceValid(_pilotTarget)) break;
            var anim = _pilotTarget.Animation;
            int frame = _pilotTarget.FrameIndex;
            if (anim == MirAnimation.Walking && frame != prevWalkFrame && _pilotWalkShots.Count < 4)
            {
                prevWalkFrame = frame;
                _pilotWalkShots.Add(CaptureMonsterAuditShot(outDir, 200 + _pilotWalkShots.Count, name + "_walk_bg"));
            }
            else if (IsCombatAnim(anim) && (anim != prevCombatAnim || frame != prevCombatFrame)
                     && _pilotCombatShots.Count < 4)
            {
                prevCombatAnim = anim;
                prevCombatFrame = frame;
                _pilotCombatShots.Add(CaptureMonsterAuditShot(outDir, 300 + _pilotCombatShots.Count, name + "_combat_bg"));
            }
            if (_pilotWalkShots.Count >= 4 && _pilotCombatShots.Count >= 4) return;
        }
    }

    private bool _pilotDisposed;

    /// <summary>AI=1/2 为 Passive=true 怪（不主动索敌追击）；其余 default 主动。</summary>
    private static bool IsPassiveAi(int ai) => ai is 1 or 2 or 4 or 5 or 135 or 136 or 129 or 130 or 12;

    /// <summary>
    /// AI 语义上**不会攻击玩家**的类型：AI=12 HealerAnt 只治疗同类（
    /// CanAttackTarget =&gt; CanHelpTarget，见 Monsters/HealerAnt.cs），
    /// 玩家不是它的治疗对象，因此永远没有 Combat 动作。
    /// </summary>
    private static bool IsNonAttackingAi(int ai) => ai is 12;

    /// <summary>从 from 朝远离 target 的方向走 steps 格（只在 0.map 城内候选格里找可走点）。</summary>
    private System.Drawing.Point StepAwayFrom(System.Drawing.Point from, System.Drawing.Point target, int steps)
    {
        int dx = Math.Sign(from.X - target.X);
        int dy = Math.Sign(from.Y - target.Y);
        if (dx == 0 && dy == 0) { dx = 1; dy = 1; }
        var p = new System.Drawing.Point(from.X + dx * steps, from.Y + dy * steps);
        // 目标格出界或被挡时退回原格附近
        if (_mapView?.Map == null || p.X <= 1 || p.Y <= 1
            || p.X >= _mapView.Map.Width || p.Y >= _mapView.Map.Height
            || _mapView.Map.Cells[p.X, p.Y].Flag)
            return from;
        return p;
    }

    /// <summary>死亡阶段：等到真实 Die 动画出现并逐帧留证；没出现记 failed，不用前序截图冒充。</summary>
    private async Task WaitForDieAnimAsync(string outDir, string name)
    {
        if (!IsInstanceValid(_pilotTarget))
        {
            GD.PrintErr($"[MonsterPilot] {name} 目标在死亡阶段已移除，Die 动画记为 failed");
            WritePilotLog(outDir, name + "_dead", new Dictionary<string, object>
            {
                ["stage"] = 4,
                ["status"] = "failed",
                ["reason"] = "object-removed-before-die-frame",
            });
            return;
        }
        var prev = (_pilotTarget.Animation, _pilotTarget.FrameIndex, _pilotTarget.CellX, _pilotTarget.CellY);
        int hits = 0;
        var row = new Dictionary<string, object>
        {
            ["stage"] = 4,
            ["label"] = name + "_dead",
            ["targetId"] = _pilotTargetId,
        };
        double deadline = Godot.Time.GetTicksMsec() + 15000.0;
        while (Godot.Time.GetTicksMsec() < deadline && hits < 2)
        {
            await NextFrameAsync();
            if (!IsInstanceValid(_pilotTarget)) break;
            var cur = (_pilotTarget.Animation, _pilotTarget.FrameIndex, _pilotTarget.CellX, _pilotTarget.CellY);
            if (_pilotTarget.Animation == MirAnimation.Die
                && (cur.Animation != prev.Animation || cur.FrameIndex != prev.FrameIndex))
            {
                hits++;
                var shot = CaptureMonsterAuditShot(outDir, 40 + hits, name + "_dead");
                row[$"hit{hits}"] = new Dictionary<string, object>
                {
                    ["anim"] = cur.Animation.ToString(),
                    ["frame"] = cur.FrameIndex,
                    ["dir"] = _pilotTarget.Direction.ToString(),
                    ["cell"] = $"{cur.CellX},{cur.CellY}",
                    ["tickMs"] = Godot.Time.GetTicksMsec(),
                    ["shot"] = shot,
                };
                GD.Print($"[MonsterPilot] {name}_dead hit {hits}: anim=Die frame={cur.FrameIndex} dir={_pilotTarget.Direction}");
            }
            prev = cur;
        }
        row["ok"] = hits >= 1;
        row["hits"] = hits;
        if (hits == 0) GD.PrintErr($"[MonsterPilot] {name} 未捕获到 Die 动画，死亡阶段记为 failed");
        WritePilotLog(outDir, name + "_dead", row);
    }

    private static bool IsCombatAnim(MirAnimation anim)
        => anim is MirAnimation.Combat1 or MirAnimation.Combat2 or MirAnimation.Combat3
            or MirAnimation.Combat4 or MirAnimation.Combat5 or MirAnimation.Combat6
            or MirAnimation.Combat7 or MirAnimation.Combat8;

    /// <summary>
    /// 死亡阶段持续采样：一击秒杀时 ObjectStruck/ObjectDied/ObjectRemove 同批到达，
    /// 若等打完再找 Die 帧就永远扑空（实测 object-removed-before-die-frame）。
    /// 这里在开打前就启动、每帧比对 (Animation,FrameIndex,Cell)，一旦进入
    /// Combat*/Struck/Die 就立即截图并记录跃迁；目标死亡且已采到 Die 帧或已移除则退出。
    /// </summary>
    private async Task<Dictionary<string, object>> RunTransitionSamplerAsync(string outDir, string name)
    {
        var row = new Dictionary<string, object>
        {
            ["stage"] = 4,
            ["label"] = name + "_dead",
            ["targetId"] = _pilotTargetId,
        };
        double deadline = Godot.Time.GetTicksMsec() + 70000.0;
        var seen = new List<string>();
        int dieHits = 0, combatHits = 0, struckHits = 0;
        var prev = (Animation: _pilotTarget?.Animation ?? MirAnimation.Standing,
            FrameIndex: _pilotTarget?.FrameIndex ?? 0,
            CellX: _pilotTarget?.CellX ?? 0, CellY: _pilotTarget?.CellY ?? 0);
        while (Godot.Time.GetTicksMsec() < deadline)
        {
            await NextFrameAsync();
            if (!IsInstanceValid(_pilotTarget))
            {
                row["removedBeforeExit"] = true;
                break;
            }
            var cur = (_pilotTarget.Animation, _pilotTarget.FrameIndex, _pilotTarget.CellX, _pilotTarget.CellY);
            bool changed = cur.Animation != prev.Animation || cur.FrameIndex != prev.FrameIndex
                || cur.CellX != prev.CellX || cur.CellY != prev.CellY;
            if (changed && (IsCombatAnim(cur.Animation) || cur.Animation == MirAnimation.Struck
                || cur.Animation == MirAnimation.Die))
            {
                string tag = cur.Animation == MirAnimation.Die ? "die"
                    : cur.Animation == MirAnimation.Struck ? "struck" : "combat";
                if (tag == "die") dieHits++;
                else if (tag == "struck") struckHits++;
                else combatHits++;
                if (seen.Count < 40)
                {
                    var shot = CaptureMonsterAuditShot(outDir, 40 + seen.Count, name + "_" + tag);
                    seen.Add($"{tag}:{cur.Animation}#{cur.FrameIndex}@{cur.CellX},{cur.CellY}");
                    row[$"hit{seen.Count}"] = new Dictionary<string, object>
                    {
                        ["anim"] = cur.Animation.ToString(),
                        ["frame"] = cur.FrameIndex,
                        ["dir"] = _pilotTarget.Direction.ToString(),
                        ["cell"] = $"{cur.CellX},{cur.CellY}",
                        ["tickMs"] = Godot.Time.GetTicksMsec(),
                        ["shot"] = shot,
                    };
                    GD.Print($"[MonsterPilot] {name} {tag} hit: anim={cur.Animation} frame={cur.FrameIndex} dir={_pilotTarget.Direction}");
                }
            }
            prev = cur;
            if (_pilotTarget.Dead && dieHits >= 2) break;
        }
        row["dieHits"] = dieHits;
        row["struckHits"] = struckHits;
        row["combatHits"] = combatHits;
        row["ok"] = dieHits >= 1;
        row["transitions"] = seen;
        if (dieHits == 0)
            GD.PrintErr($"[MonsterPilot] {name} 采样未捕获到 Die 帧（struck={struckHits} combat={combatHits}）");
        return row;
    }

    /// <summary>等待真实出现目标动画/状态变化的阶段；未出现记 failed 并继续，不伪造证据。</summary>
    private async Task CaptureStageAsync(string outDir, int stage, string label,
        Func<bool> enter, Func<(MirAnimation Animation, int Frame, int CellX, int CellY),
            (MirAnimation Animation, int Frame, int CellX, int CellY), bool> advanced,
        int requiredSamples, double timeoutSec,
        Dictionary<string, object> extra = null)
    {
        var row = new Dictionary<string, object>
        {
            ["stage"] = stage,
            ["label"] = label,
            ["targetId"] = _pilotTargetId,
            ["name"] = _pilotTarget?.MonsterInfo?.MonsterName,
            ["image"] = _pilotTarget?.MonsterInfo?.Image.ToString(),
        };
        if (extra != null)
            foreach (var kv in extra) row[kv.Key] = kv.Value;

        double deadline = Godot.Time.GetTicksMsec() + timeoutSec * 1000.0;
        var prev = (_pilotTarget.Animation, _pilotTarget.FrameIndex, _pilotTarget.CellX, _pilotTarget.CellY);
        int hits = 0;
        var shots = new List<string>();
        while (Godot.Time.GetTicksMsec() < deadline && hits < requiredSamples)
        {
            await NextFrameAsync();
            if (!IsInstanceValid(_pilotTarget)) break;
            var cur = (_pilotTarget.Animation, _pilotTarget.FrameIndex, _pilotTarget.CellX, _pilotTarget.CellY);
            if (enter() && advanced(prev, cur))
            {
                hits++;
                var shot = CaptureMonsterAuditShot(outDir, stage * 10 + hits, label);
                if (!string.IsNullOrEmpty(shot)) shots.Add(shot);
                row[$"hit{hits}"] = new Dictionary<string, object>
                {
                    ["anim"] = cur.Animation.ToString(),
                    ["frame"] = cur.FrameIndex,
                    ["dir"] = _pilotTarget.Direction.ToString(),
                    ["cell"] = $"{cur.CellX},{cur.CellY}",
                    ["tickMs"] = Godot.Time.GetTicksMsec(),
                    ["shot"] = shot,
                };
                GD.Print($"[MonsterPilot] {label} hit {hits}: anim={cur.Animation} frame={cur.FrameIndex} dir={_pilotTarget.Direction} cell=({cur.CellX},{cur.CellY})");
            }
            prev = cur;
        }
        row["ok"] = hits >= requiredSamples;
        row["hits"] = hits;
        row["required"] = requiredSamples;
        _pilotLastStageOk = hits >= requiredSamples;
        if (hits == 0)
            GD.PrintErr($"[MonsterPilot] {label} 未捕获到真实动作状态（目标可能未触发该动作），记为 failed");
        WritePilotLog(outDir, label, row);
    }

    /// <summary>
    /// 走到一个 “自身格 + 正前格” 都通行且没有怪物的位置，保证 @monster 能刷成功。
    /// 服务端 SpawnMob 把怪刷在玩家正前格，那格不可站/已占时静默失败（实测踩到）。
    /// </summary>
    private async Task MoveToSpawnableCellAsync()
    {
        if (_mapView?.Map == null) return;
        var map = _mapView.Map;
        bool Free(int x, int y)
            => x > 1 && y > 1 && x < map.Width - 1 && y < map.Height - 1
               && !map.Cells[x, y].Flag
               && !_objects.Values.Any(o => o != null && !o.Dead && o.CellX == x && o.CellY == y);
        for (int radius = 0; radius <= 8; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue;
                    int x = _playerLocation.X + dx, y = _playerLocation.Y + dy;
                    if (!Free(x, y)) continue;
                    var dir = Functions.DirectionFromPoint(new System.Drawing.Point(x, y), _playerLocation);
                    var front = Functions.Move(new System.Drawing.Point(x, y), dir);
                    if (!Free(front.X, front.Y)) continue;
                    SendChat($"@move {PilotCurrentMap} {x} {y}");
                    await WaitSecondsAsync(0.7);
                    return;
                }
            }
        }
        // 半径 8 内找不到“自身+正前格都空”的位点时，也要做点什么：
        // 先走一步换个位置，下一次重试再找（否则会静止在同一面墙前反复失败，
        // Ghoul Champion 在 D203 实测踩到）。
        SendMouseMove((MirDirection)(_pilotTarget?.ObjectID % 8 ?? 0), 1, false);
        await WaitSecondsAsync(0.6);
    }

    private async Task<bool> WaitForSpawnAsync(string name, int before, double timeoutSec)
    {
        double deadline = Godot.Time.GetTicksMsec() + timeoutSec * 1000.0;
        while (Godot.Time.GetTicksMsec() < deadline)
        {
            if (CountMonsterTypeNearby(20, name) > before) return true;
            await NextFrameAsync();
        }
        return false;
    }

    /// <summary>
    /// 等待@monster 刷出的新对象：与 preIds 对比找出新出现的同名怪物。
    /// 比“半径计数上升”可靠（地图切换 / 自然刷新都会干扰计数，实测伪阴性）。
    /// </summary>
    private async Task<ObjectRenderer> WaitForNewMonsterAsync(string name, HashSet<uint> preIds, double timeoutSec)
    {
        double deadline = Godot.Time.GetTicksMsec() + timeoutSec * 1000.0;
        while (Godot.Time.GetTicksMsec() < deadline)
        {
            var hit = _objects.Values
                .Where(o => o?.Type == ObjectRenderer.Kind.Monster
                    && o.MonsterInfo?.MonsterName == name
                    && !preIds.Contains(o.ObjectID))
                .OrderByDescending(o => o.ObjectID)
                .FirstOrDefault();
            if (hit != null) return hit;
            await NextFrameAsync();
        }
        return null;
    }

    private async Task<bool> WaitUntilDeadAsync(double timeoutSec)
    {
        double deadline = Godot.Time.GetTicksMsec() + timeoutSec * 1000.0;
        while (Godot.Time.GetTicksMsec() < deadline)
        {
            if (IsInstanceValid(_pilotTarget) && _pilotTarget.Dead) return true;
            if (!IsInstanceValid(_pilotTarget)) return true; // 已移除
            await NextFrameAsync();
        }
        return false;
    }

    private async Task<bool> WaitForRemovalAsync(double timeoutSec)
    {
        double deadline = Godot.Time.GetTicksMsec() + timeoutSec * 1000.0;
        while (Godot.Time.GetTicksMsec() < deadline)
        {
            if (!IsInstanceValid(_pilotTarget)) return true;
            await NextFrameAsync();
        }
        return false;
    }

    private void WritePilotLog(string outDir, string phase, Dictionary<string, object> row)
    {
        try
        {
            string line = JsonSerializer.Serialize(row, new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
            File.AppendAllText(Path.Combine(outDir, "pilot_log.jsonl"), line + "\n", Encoding.UTF8);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MonsterPilot] 写 {phase} 日志失败: {ex.Message}");
        }
    }

    private async Task WaitForAuditKeyAsync(Key key)
    {
        while (true)
        {
            await NextFrameAsync();
            if (Input.IsKeyPressed(key))
            {
                while (Input.IsKeyPressed(key)) await NextFrameAsync();
                return;
            }
        }
    }
}
