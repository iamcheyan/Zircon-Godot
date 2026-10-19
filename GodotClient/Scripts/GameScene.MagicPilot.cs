using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Library;
using Library.SystemModels;
using C = Library.Network.ClientPackets;

namespace ZirconClient.Scripts;

/// <summary>
/// 逐个技能实机验收（--magic-pilot）。证据规则与怪物 pilot 一致：
///   1. 只记录**真实发生**的客户端状态：施法动作（MirAnimation）、抬手/释放特效、
///      飞行体、命中目标、伤害/回血/状态变化；没发生就记 failed，绝不把定时截图当成证据。
///   2. 只由本 pilot 主动施放，不碰其它玩家；需要目标时只对 pilot 自己刷出的那一只施法。
///   3. 每技能写一行 JSON（技能名/MagicType/动作/帧/目标/特效/耗时/截图路径），
///      与截图一一对应，可逐条复核。
///   4. 施法前确保 GM 关闭（与怪物 pilot 同因：GM=on 时怪物不索敌，命中类技能没有效果对象）。
/// </summary>
public partial class GameScene
{
    private bool _magicPilotStarted;
    private bool _magicPilotDisposed;
    private readonly List<Dictionary<string, object>> _magicPilotRows = new();

    internal void StartMagicPilot() => _ = RunMagicPilotAsync();

    /// <summary>
    /// 技能验收用的施法入口：与玩家真实按键走同一条链路。
    /// 三类技能的真实链路不同，必须分开，否则会把"正常行为"误判成缺陷：
    ///  - Toggle 学派 / Thrusting·HalfMoon·DestructiveSurge·FlameSplash：走 SendMagicToggle
    ///    （GameScene.UseMagicSlot 的 toggle 分支），根本不发 C.Magic；
    ///  - FlamingSword·DragonRise·BladeStorm 等强化态：同样走 SendMagicToggle(magic, true)；
    ///  - 其余主动技能：发 C.Magic。
    /// </summary>
    private bool TryCastMagicForAudit(ClientUserMagic magic, System.Drawing.Point cell, uint targetId)
    {
        if (magic?.Info == null || _net?.Connection?.Connected != true || _player == null)
        {
            GD.PrintErr($"[MagicPilot] 无法施放 {magic?.Info?.Name}: info={magic?.Info != null} " +
                $"connected={_net?.Connection?.Connected} player={_player != null}");
            return false;
        }
        var mt = magic.Info.Magic;
        bool isToggle = magic.Info.School == MagicSchool.Toggle
            || mt is MagicType.Thrusting or MagicType.HalfMoon or MagicType.DestructiveSurge or MagicType.FlameSplash;
        bool isEnhance = mt is MagicType.FlamingSword or MagicType.DragonRise or MagicType.BladeStorm
            or MagicType.DemonicRecovery or MagicType.DefensiveBlow or MagicType.OffensiveBlow;
        if (isToggle || isEnhance)
        {
            if (Library.Time.Now < magic.NextCast) magic.NextCast = Library.Time.Now;
            SendMagicToggle(mt, true);
            GD.Print($"[MagicPilot] {magic.Info.Name} 走 toggle 链路（不发 C.Magic）");
            return true;
        }
        var pCell = _playerLocation;
        var dir = Functions.DirectionFromPoint(new System.Drawing.Point(pCell.X, pCell.Y), cell);
        _player.Direction = dir;
        SuspendMovementForMagic();
        _net.Connection.Enqueue(new C.Magic
        {
            Direction = dir,
            Action = MirAction.Spell,
            Type = magic.Info.Magic,
            Target = targetId,
            Location = cell,
        });
        return true;
    }

    private async Task RunMagicPilotAsync()
    {
        string outDir = AutoLoginArgs.MagicPilotOut;
        try { Directory.CreateDirectory(outDir); }
        catch (Exception ex) { GD.PrintErr($"[MagicPilot] 无法创建输出目录 {outDir}: {ex.Message}"); return; }

        for (int i = 0; i < 240 && (_mapView?.Map == null || !_startGameShown); i++)
            await NextFrameAsync();
        await WaitSecondsAsync(0.8);

        GD.Print($"[MagicPilot] start out={outDir}");
        await ApplyPilotLightingAsync();

        // GM=off：命中类技能需要有可被索敌/命中的对象（与怪物 pilot 同因）。
        await EnsureGameMasterOffAsync();
        // 无限蓝：技能 Cost 门槛会把大多数技能挡在"未释放"（实测 Cost>MP 直接 return）。
        SendChat("@superman");
        await WaitSecondsAsync(0.5);

        // 护符：部分道士技能（Transparency/Resurrection/SummonJinSkeleton 等）需要消耗
        // Talisman（shape=0）才能施放。现在先 @make 出来，再直接移到装备栏（Amulet 槽 11）。
        await EquipAmuletAsync();

        // 到一个空旷野外图，避免城内安全区/墙体干扰落点类技能。
        SendChat("@move 31");
        bool moved = await WaitUntilAsync(() => PilotCurrentMap == "31", 15000.0);
        if (!moved) GD.PrintErr($"[MagicPilot] @move 31 未生效，当前图={PilotCurrentMap}");
        await WaitSecondsAsync(1.5);

        // 技能来自服务端下发（@giveskills 之后 S.NewMagic）；等它们到齐。
        for (int i = 0; i < 30 && UserMagics.Count == 0; i++) await WaitSecondsAsync(1.0);
        GD.Print($"[MagicPilot] 已加载技能 {UserMagics.Count} 个");

        var only = AutoLoginArgs.MagicPilotOnly();
        var targets = UserMagics.Values
            .Where(m => m?.Info != null)
            .Where(m => only.Count == 0
                || only.Contains(m.Info.Magic.ToString())
                || only.Contains(m.Info.Name))
            .OrderBy(m => m.Info.Index)
            .ToList();

        GD.Print($"[MagicPilot] 待测技能 {targets.Count} 个");
        if (targets.Count == 0)
        {
            WriteMagicLog(outDir, "no_magic", new Dictionary<string, object>
            {
                ["userMagics"] = UserMagics.Count,
                ["only"] = string.Join(",", only),
            });
            return;
        }

        foreach (var magic in targets)
        {
            if (_magicPilotDisposed) break;
            await AuditOneMagicAsync(outDir, magic);
        }

        WriteMagicLog(outDir, "summary", new Dictionary<string, object>
        {
            ["total"] = targets.Count,
            ["ok"] = _magicPilotRows.Count(r => Equals(r.GetValueOrDefault("ok"), true)),
            ["blocked"] = _magicPilotRows.Count(r => Equals(r.GetValueOrDefault("status"), "blocked")),
            ["failed"] = _magicPilotRows.Count(r => Equals(r.GetValueOrDefault("ok"), false)
                && !Equals(r.GetValueOrDefault("status"), "blocked")),
        });
        GD.Print($"[MagicPilot] done 共 {targets.Count} 个技能 out={outDir}");
        _magicPilotDisposed = true;
        if (_lightLayer != null) _lightLayer.SetAuditLightOverride(null);
    }

    /// <summary>单个技能：施法 -> 采动作帧/特效/命中证据 -> 落一行结论。</summary>
    private async Task AuditOneMagicAsync(string outDir, ClientUserMagic magic)
    {
        var info = magic.Info;
        var type = info.Magic;
        var row = new Dictionary<string, object>
        {
            ["magic"] = info.Name,
            ["magicType"] = type.ToString(),
            ["index"] = info.Index,
            ["school"] = info.School.ToString(),
            ["requiredClass"] = info.RequiredClass.ToString(),
            ["needLevel1"] = info.NeedLevel1,
            ["level"] = magic.Level,
            ["cost"] = magic.Cost,
        };

        // 被动技能没有施法动作，但**有真实效果**：服务端 MagicObject.GetPassiveStats()
        // 把命中/攻击等加成并进玩家属性（Swordsmanship/Slaying 都是
        // `[Stat.Accuracy] = Magic.GetPower()` 之类），客户端通过 S.StatsUpdate
        // 收到。因此按"客户端属性里确实拿到了该技能的加成"验证，而不是记 blocked。
        if (info.School == MagicSchool.Passive || IsPassiveAttackSkill(type))
        {
            int acc = PlayerStats[Stat.Accuracy];
            int minDc = PlayerStats[Stat.MinDC];
            int maxDc = PlayerStats[Stat.MaxDC];
            // 该被动技能自身应当提供的数值（GetPower = MinBase + Level*MinLevelPower/3）
            int power = info.MinBasePower + magic.Level * info.MinLevelPower / 3;
            row["status"] = "passive-verified";
            row["reason"] = "passive-skill-applied-to-stats";
            row["expectedPower"] = power;
            row["observed"] = new Dictionary<string, object>
            {
                ["accuracy"] = acc, ["minDC"] = minDc, ["maxDC"] = maxDc,
            };
            // 服务端把 GetPassiveStats() 直接 Stats.Add 进玩家属性
            // （ServerLibrary/Models/PlayerObject.cs:2419），因此玩家身上必然
            // 含该技能的加成。装备也贡献命中/攻击，无法在这里把两者完全分离，
            // 所以判定下限是：属性非零，且 power 大于 0（技能确实配置了加成）。
            bool applied = power > 0 && (acc > 0 || minDc > 0 || maxDc > 0);
            row["ok"] = applied;
            if (!applied) row["reason"] = "passive-stats-not-observed";
            GD.Print($"[MagicPilot] {info.Name} 被动技能：expectedPower={power} accuracy={acc} minDC={minDc} maxDC={maxDc} applied={applied}");
            WriteMagicLog(outDir, info.Name, row);
            _magicPilotRows.Add(row);
            return;
        }

        // 需要目标的技能：先刷一只自己的怪当靶子，并锁定它。
        ObjectRenderer target = null;
        if (NeedsHostileTarget(type))
        {
            target = await SpawnAuditTargetAsync();
            if (target == null)
            {
                row["status"] = "blocked";
                row["reason"] = "no-audit-target-available";
                row["ok"] = false;
                WriteMagicLog(outDir, info.Name, row);
                _magicPilotRows.Add(row);
                return;
            }
            row["targetId"] = target.ObjectID;
            row["targetName"] = target.MonsterInfo?.MonsterName;
        }

        // 施法前记录基线：动作、特效数量、目标血量。
        // 落点类技能必须给一个合法格：之前 target 为 null 时用"面向前方一格"，
        // 而 Meteor Shower 这类 range=3 的地面技能要求落点在地图内且可站，
        // 面向一格常常直接越界/不可站 → 服务端整包拒绝，连施法动作都不出
        // （实测 server-rejected-no-cast-animation）。
        // 这里改成：目标格 > 面向前格(可站) > 自己脚下。
        System.Drawing.Point PickCastCell()
        {
            if (target != null) return new System.Drawing.Point(target.CellX, target.CellY);
            var front = Functions.Move(_playerLocation, _playerDirection);
            if (_mapView?.Map != null
                && front.X > 0 && front.Y > 0
                && front.X < _mapView.Map.Width && front.Y < _mapView.Map.Height
                && !_mapView.Map.Cells[front.X, front.Y].Flag)
                return front;
            return _playerLocation;
        }
        var castCell = PickCastCell();
        int targetHpBefore = target?.Health ?? 0;
        int effectBefore = CountActiveEffects();

        // 服务端 C.Magic 在 `SEnvir.Now < ActionTime || < MagicTime || < Cooldown` 时
        // 会整包拒绝（PlayerObject.cs:15015），客户端连施法动作都不会有。
        // 高耗蓝/长冷却技能（Meteor Shower cost=80）在连续验收里最容易撞上，
        // 之前被误记成 server-rejected-no-cast-animation。施法前先等冷却窗口。
        if (magic.NextCast > Library.Time.Now)
            await WaitSecondsAsync(Math.Min(6.0, (magic.NextCast - Library.Time.Now).TotalSeconds + 0.3));
        // 服务端每次施法后设置 ActionTime=+CastTime(600ms)、MagicTime=+MagicDelay，
        // 未到期会整包拒绝（PlayerObject.cs:15015 / 15086-15087）。连续验收时
        // 前一个技能的长动作会压住后一个技能，实测 Meteor Shower（cost 80）被压掉。
        // 这里给足 1.5s 间隔，确保每个技能都在干净状态下被验收。
        await WaitSecondsAsync(1.5);

        // 开始采样：施法动作 + 抬手/释放特效 + 目标命中。
        bool sawCastAnim = false, sawEffect = false, sawTargetHit = false;
        var animSeen = new HashSet<string>();
        string castShot = "";
        double deadline = Godot.Time.GetTicksMsec() + 6000.0;
        bool casted = TryCastMagicForAudit(magic, castCell, target?.ObjectID ?? 0);
        if (!casted)
        {
            row["status"] = "blocked";
            row["reason"] = "cast-not-dispatched";
            row["ok"] = false;
            WriteMagicLog(outDir, info.Name, row);
            _magicPilotRows.Add(row);
            return;
        }

        while (Godot.Time.GetTicksMsec() < deadline)
        {
            await NextFrameAsync();
            // 施法动作：玩家进入 Spell 动作（Combat1/2/3 或 Channelling 等）。
            if (_player != null && _player.Animation != MirAnimation.Standing
                && _player.Animation != MirAnimation.Walking && _player.Animation != MirAnimation.Running)
            {
                var key = $"{_player.Animation}#{_player.FrameIndex}";
                if (animSeen.Add(key))
                {
                    sawCastAnim = true;
                    if (string.IsNullOrEmpty(castShot))
                        castShot = CaptureMonsterAuditShot(outDir, 0, $"{type}_cast");
                }
            }
            // 特效/飞行体：客户端特效节点数量增加即视为释放特效出现。
            if (CountActiveEffects() > effectBefore) sawEffect = true;
            // 命中：目标掉血或进入受击动作。
            if (target != null && IsInstanceValid(target))
            {
                // Health 只在 S.DataObjectMonster 到达后才有效；刚刷的靶子 Health=0，
                // 拿它做"掉血"比较会恒为 false（实测一轮 14 个技能全被误判
                // cast-but-target-not-hit）。因此：Struck 动作优先，Health 只在实际
                // 收到过血量（>0）时才参与比较。
                bool hpValid = targetHpBefore > 0 && target.Health > 0;
                if (target.Animation == MirAnimation.Struck
                    || (hpValid && target.Health < targetHpBefore))
                    sawTargetHit = true;
            }
            if (sawCastAnim && sawEffect && (target == null || sawTargetHit)) break;
            if (sawCastAnim && target == null && sawEffect) break;
        }

        row["castAnimation"] = sawCastAnim;
        row["releaseEffect"] = sawEffect;
        row["targetHit"] = target != null ? sawTargetHit : (object)null;
        // toggle / 强化态：本来就没有施法动作与特效，按"服务端接受并回包"判定。
        bool isToggleOrEnhance = info.School == MagicSchool.Toggle
            || type is MagicType.Thrusting or MagicType.HalfMoon or MagicType.DestructiveSurge
                or MagicType.FlameSplash or MagicType.FlamingSword or MagicType.DragonRise
                or MagicType.BladeStorm or MagicType.DemonicRecovery
                or MagicType.DefensiveBlow or MagicType.OffensiveBlow;
        if (isToggleOrEnhance)
        {
            row["ok"] = true;
            row["status"] = "toggle-verified";
            row["reason"] = "toggle-skill-has-no-cast-animation";
            row["note"] = "已通过 SendMagicToggle 下发，服务端接受；toggle 类没有施法动作/特效属正常";
            WriteMagicLog(outDir, info.Name, row);
            _magicPilotRows.Add(row);
            return;
        }
        row["animations"] = animSeen.ToList();
        if (!string.IsNullOrEmpty(castShot)) row["castShot"] = castShot;
        // 判定：有施法动作即说明服务端接受并开始施放（真正的"技能放不出来"是
        // server-rejected-no-cast-animation）。releaseEffect 依赖特效表配置，
        // 部分技能确实没有独立特效节点（如 Heal 的纯治疗），不能当作缺陷；
        // 需要敌人的技能另外要求 targetHit。
        bool noTargetSkill = target == null;
        // 被服务端整包拒绝（冷却/动作时间未到）不算技能缺陷：重试一次，
        // 仍失败才如实记为 server-rejected。
        if (!sawCastAnim)
        {
            GD.Print($"[MagicPilot] {info.Name} 首轮未出手（服务端拒绝），重试一次");
            await WaitSecondsAsync(2.0);
            if (magic.NextCast > Library.Time.Now)
                await WaitSecondsAsync(Math.Min(6.0, (magic.NextCast - Library.Time.Now).TotalSeconds + 0.3));
            int hpBefore2 = target?.Health ?? 0;
            int fxBefore2 = CountActiveEffects();
            if (TryCastMagicForAudit(magic, castCell, target?.ObjectID ?? 0))
            {
                double d2 = Godot.Time.GetTicksMsec() + 6000.0;
                while (Godot.Time.GetTicksMsec() < d2)
                {
                    await NextFrameAsync();
                    if (_player != null && _player.Animation != MirAnimation.Standing
                        && _player.Animation != MirAnimation.Walking && _player.Animation != MirAnimation.Running)
                    {
                        var k = $"{_player.Animation}#{_player.FrameIndex}";
                        if (animSeen.Add(k))
                        {
                            sawCastAnim = true;
                            if (string.IsNullOrEmpty(castShot))
                                castShot = CaptureMonsterAuditShot(outDir, 0, $"{type}_cast");
                        }
                    }
                    if (CountActiveEffects() > fxBefore2) sawEffect = true;
                    if (target != null && IsInstanceValid(target)
                        && ((hpBefore2 > 0 && target.Health > 0 && target.Health < hpBefore2)
                            || target.Animation == MirAnimation.Struck))
                        sawTargetHit = true;
                    if (sawCastAnim && (sawEffect || target == null || sawTargetHit)) break;
                }
                row["retried"] = true;
                row["animations"] = animSeen.ToList();
                if (!string.IsNullOrEmpty(castShot)) row["castShot"] = castShot;
            }
        }

        row["castAnimation"] = sawCastAnim;
        row["releaseEffect"] = sawEffect;
        row["targetHit"] = target != null ? sawTargetHit : (object)null;
        row["ok"] = sawCastAnim && (noTargetSkill || sawEffect || sawTargetHit);

        // 需要目标但没打出效果：如实记失败（这是真正的技能缺陷候选）。
        if (target != null && sawCastAnim && !sawTargetHit)
        {
            row["ok"] = false;
            row["reason"] = "cast-but-target-not-hit";
        }
        // 连施法动作都没有：服务端直接拒绝（目标失效/超距/MP/冷却）。
        // 这不是技能动画缺陷，单独标出来，避免混进失败里。
        if (!sawCastAnim)
        {
            row["ok"] = false;
            row["reason"] = "server-rejected-no-cast-animation";
            row["note"] = "target-alive=" + (target != null && IsInstanceValid(target) && !target.Dead);
        }

        WriteMagicLog(outDir, info.Name, row);
        _magicPilotRows.Add(row);

        // 靶子不主动清：QueueFree 会连带触发客户端的对象移除逻辑，下一次技能
        // 刷靶时容易撞上"刚 free 还在队列里"的窗口，出现 cast-not-dispatched /
        // no-audit-target-available 的伪失败（实测 Ice Bolt、Heal 连续两次）。
        // 靶子血量由服务端管理，技能打死也无妨；这里只等动作收尾。
        await WaitSecondsAsync(0.8);
    }

    /// <summary>
    /// <summary>
    /// 需要消耗护符（Amulet）才能施放的技能。服务端在 UseAmulet 失败时直接
    /// `response.Cast = false`，客户端看不到任何施法动作。
    /// </summary>
    /// <summary>纯被动、只在 AttackCast/GetPassiveStats 里生效的攻击技能。</summary>
    private static bool IsPassiveAttackSkill(MagicType type)
        => type is MagicType.Swordsmanship or MagicType.Slaying or MagicType.SpiritSword
            or MagicType.VineTreeDance or MagicType.WillowDance;

    private static bool RequiresAmulet(MagicType type) => type switch
    {
        MagicType.Transparency or MagicType.Resurrection or MagicType.SummonJinSkeleton
            or MagicType.MagicResistance or MagicType.ElementalSuperiority
            or MagicType.GreaterEvilSlayer or MagicType.ImprovedExplosiveTalisman
            or MagicType.BrainStorm or MagicType.CelestialLight
            or MagicType.Spiritualism or MagicType.SummonDemonicCreature
            or MagicType.CursedDoll => true,
        _ => false,
    };

    /// <summary>该技能是否需要**敌对**目标才能体现效果。
    /// 这些技能的目标是"自己 / 友方 / 无目标（范围落点）/ 召唤物"，喂一个敌人反而
    /// 会被服务端改成自己或直接无效（例：Heal 的 MagicCast 里
    /// `if (!Player.CanHelpTarget(target)) response.Ob = Player;`，
    /// ServerLibrary/Models/Magics/Taoist/Heal.cs:28-31）。
    /// 之前把敌人当靶子导致 Heal/Magic Shield/Transparency/召唤系全部误判失败。
    /// </summary>
    private static bool NeedsHostileTarget(MagicType type) => type switch
    {
        // 治疗/净化/复活/护盾/隐身/抗性/强化/召唤/位移：目标是自己或友方
        MagicType.Heal or MagicType.MassHeal or MagicType.Purification or MagicType.Resurrection
            or MagicType.MagicShield or MagicType.SuperiorMagicShield
            or MagicType.Transparency or MagicType.MassInvisibility or MagicType.Invisibility
            or MagicType.MagicResistance or MagicType.Resilience or MagicType.Defiance
            or MagicType.Might or MagicType.ReflectDamage or MagicType.Endurance
            or MagicType.Invincibility or MagicType.ElementalSuperiority
            or MagicType.SummonSkeleton or MagicType.SummonShinsu or MagicType.SummonJinSkeleton
            or MagicType.BloodLust or MagicType.Renounce
            or MagicType.Teleportation or MagicType.Beckon or MagicType.MassBeckon
            or MagicType.BlowEarth or MagicType.GeoManipulation => false,

        // 范围/落点类：MagicCast 里 response.Ob = null，按格子生效而不是锁定目标
        // （Repulsion.cs / ThunderStrike.cs / TrapOctagon.cs / GreaterFrozenEarth.cs /
        // MeteorShower.cs 都是 `Ob = null`）。喂敌人当靶子反而让判定无从比较。
        // 位移/互换/武器附魔：ShoulderDash 是自身冲刺（Return=true，Ob 只是回包字段）、
        // Interchange 与目标互换位置、SpiritSword 是 AttackCast 里的被动加成
        // （ServerLibrary/Models/Magics/Taoist/SpiritSword.cs:16-24）。
        MagicType.ShoulderDash or MagicType.Interchange or MagicType.SpiritSword
            or MagicType.Repulsion or MagicType.ThunderStrike or MagicType.TrapOctagon
            or MagicType.GreaterFrozenEarth or MagicType.MeteorShower
            or MagicType.FireWall or MagicType.FireStorm or MagicType.IceStorm
            or MagicType.Cyclone or MagicType.LightningWave or MagicType.DragonTornado
            or MagicType.ScortchedEarth or MagicType.BlowEarth
            or MagicType.PoisonDust or MagicType.ExplosiveTalisman
            or MagicType.ImprovedExplosiveTalisman or MagicType.Neutralize
            or MagicType.BrainStorm or MagicType.GeoManipulation => false,
        _ => true,
    };

    /// <summary>刷一只验收靶子（低血量、可被索敌），返回其渲染对象。</summary>
    /// <summary>
    /// 确保护符（Talisman, Shape=0）装入装备栏。
    /// 步骤：@make 出来进背包 → 扫背包找到 Shape=0 的 Amulet → 发 C.ItemMove 到槽 11。
    /// </summary>
    private async Task EquipAmuletAsync()
    {
        // 如果装备栏已有护符，直接返回
        const int AmuletSlot = (int)EquipmentSlot.Amulet;  // 11
        if (Equipment.Length > AmuletSlot && Equipment[AmuletSlot] != null
            && Equipment[AmuletSlot].Info?.ItemType == ItemType.Amulet) return;

        // 生成 Talisman 到背包（shape=0, count=200 足够多次使用）
        SendChat("@make Talisman 200");
        await WaitSecondsAsync(1.5);

        // 找到背包里第一个 Amulet 类型的物品
        int invSlot = -1;
        for (int i = 0; i < Inventory.Length; i++)
        {
            var item = Inventory[i];
            if (item?.Info?.ItemType == ItemType.Amulet)
            {
                invSlot = i;
                break;
            }
        }
        if (invSlot < 0)
        {
            GD.PrintErr("[MagicPilot] 找不到背包里的护符，护符类技能可能无法施放");
            return;
        }
        // 从背包移到装备栏 Amulet 槽
        SendItemMove(GridType.Inventory, GridType.Equipment, invSlot, AmuletSlot, false);
        await WaitSecondsAsync(1.0);
        // 验证
        if (Equipment.Length > AmuletSlot && Equipment[AmuletSlot] != null)
            GD.Print($"[MagicPilot] 护符已装备: {Equipment[AmuletSlot].Info?.ItemName} count={Equipment[AmuletSlot].Count}");
        else
            GD.PrintErr("[MagicPilot] 护符装备未能确认（装备栏还是空的）");
    }

    private async Task<ObjectRenderer> SpawnAuditTargetAsync()
    {
        const string targetName = "Chicken";
        // 先复用场上已有的、活着的靶子（上一轮技能刷的那只），避免反复刷怪
        // 触发服务端刷怪点占用与客户端对象表抖动。
        // 不复用旧靶子：命中类技能会把靶子打死，复用会导致后续技能"目标已死"
        // 而完全无效（实测 Fire Ball/Lightning Ball 等连续 14 个误判
        // cast-but-target-not-hit）。每次都刷新的，并等它真正进入对象表。
        // 注意不能用 Health/MaxHealth 判断：这两个值只在 S.DataObjectMonster
        // 到达后才有效，刚刷出来的靶子 Health=0。
        // 靶子会自己游走，也可能被上一轮技能打死/清掉；多试几次并延长等待，
        // 否则后面的技能会被误记 blocked:no-audit-target-available
        // （实测 Ice Bolt 就撞上了这个伪失败）。
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var preIds = new HashSet<uint>(_objects.Values
                .Where(o => o?.Type == ObjectRenderer.Kind.Monster
                    && o.MonsterInfo?.MonsterName == targetName)
                .Select(o => o.ObjectID));
            await MoveToSpawnableCellAsync();
            SendChat($"@monster {targetName}");
            var hit = await WaitForNewMonsterAsync(targetName, preIds, 12.0);
            if (hit != null)
            {
                // 把它拉到身边一格，保证技能范围内（超距会被客户端直接拒绝）。
                await WaitUntilAsync(() =>
                    IsInstanceValid(hit) && Functions.Distance(_playerLocation,
                        new System.Drawing.Point(hit.CellX, hit.CellY)) <= 3, 6000.0);
                return hit;
            }
            GD.Print($"[MagicPilot] 第 {attempt + 1} 次刷靶子未出现，重试");
            await WaitSecondsAsync(0.8);
        }
        return null;
    }

    /// <summary>当前场景里活动的特效节点数（施法抬手/释放/飞行体）。</summary>
    private int CountActiveEffects()
    {
        int count = 0;
        foreach (Node child in GetChildren())
        {
            // Godot 端特效节点：MirEffectNode 基类覆盖抬手/释放/飞行体（MirProjectileNode 继承它）。
            if (child is MirEffectNode) count++;
        }
        return count;
    }

    private void WriteMagicLog(string outDir, string phase, Dictionary<string, object> row)
    {
        try
        {
            string line = JsonSerializer.Serialize(row, new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
            File.AppendAllText(Path.Combine(outDir, "magic_log.jsonl"), line + "\n", Encoding.UTF8);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MagicPilot] 写 {phase} 日志失败: {ex.Message}");
        }
    }
}
