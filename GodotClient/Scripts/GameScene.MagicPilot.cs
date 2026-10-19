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

    /// <summary>技能验收用的施法入口：与玩家按技能键走同一条发包链路（C.Magic）。</summary>
    private bool TryCastMagicForAudit(ClientUserMagic magic, System.Drawing.Point cell, uint targetId)
    {
        if (magic?.Info == null || _net?.Connection?.Connected != true || _player == null)
        {
            GD.PrintErr($"[MagicPilot] 无法施放 {magic?.Info?.Name}: info={magic?.Info != null} " +
                $"connected={_net?.Connection?.Connected} player={_player != null}");
            return false;
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

        // 被动/开关/强化类技能没有"施法动作"，按语义记 blocked 并注明原因。
        if (info.School == MagicSchool.Passive)
        {
            row["status"] = "blocked";
            row["reason"] = "passive-skill-no-cast-animation";
            row["ok"] = false;
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
        var castCell = target != null
            ? new System.Drawing.Point(target.CellX, target.CellY)
            : Functions.Move(_playerLocation, _playerDirection);
        int targetHpBefore = target?.Health ?? 0;
        int effectBefore = CountActiveEffects();

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
                if (target.Health < targetHpBefore || target.Animation == MirAnimation.Struck)
                    sawTargetHit = true;
            }
            if (sawCastAnim && sawEffect && (target == null || sawTargetHit)) break;
            if (sawCastAnim && target == null && sawEffect) break;
        }

        row["castAnimation"] = sawCastAnim;
        row["releaseEffect"] = sawEffect;
        row["targetHit"] = target != null ? sawTargetHit : (object)null;
        row["animations"] = animSeen.ToList();
        if (!string.IsNullOrEmpty(castShot)) row["castShot"] = castShot;
        row["ok"] = sawCastAnim && sawEffect;

        // 需要目标但没打出效果：如实记失败（这是真正的技能缺陷候选）。
        if (target != null && sawCastAnim && !sawTargetHit)
        {
            row["ok"] = false;
            row["reason"] = "cast-but-target-not-hit";
        }

        WriteMagicLog(outDir, info.Name, row);
        _magicPilotRows.Add(row);

        // 靶子不主动清：QueueFree 会连带触发客户端的对象移除逻辑，下一次技能
        // 刷靶时容易撞上"刚 free 还在队列里"的窗口，出现 cast-not-dispatched /
        // no-audit-target-available 的伪失败（实测 Ice Bolt、Heal 连续两次）。
        // 靶子血量由服务端管理，技能打死也无妨；这里只等动作收尾。
        await WaitSecondsAsync(0.8);
    }

    /// <summary>该技能是否需要敌对目标才能体现效果。</summary>
    private static bool NeedsHostileTarget(MagicType type) => type switch
    {
        MagicType.Heal or MagicType.MassHeal or MagicType.Purification
            or MagicType.Teleportation or MagicType.MagicShield or MagicType.SuperiorMagicShield
            or MagicType.Transparency or MagicType.MassInvisibility or MagicType.Invisibility
            or MagicType.MagicResistance or MagicType.Resilience or MagicType.Defiance
            or MagicType.Might or MagicType.ReflectDamage or MagicType.Endurance
            or MagicType.Invincibility or MagicType.ElementalSuperiority
            or MagicType.SummonSkeleton or MagicType.SummonShinsu or MagicType.SummonJinSkeleton
            or MagicType.Resurrection or MagicType.BloodLust or MagicType.LifeSteal
            or MagicType.Renounce or MagicType.Beckon or MagicType.MassBeckon
            or MagicType.BlowEarth or MagicType.GeoManipulation => false,
        _ => true,
    };

    /// <summary>刷一只验收靶子（低血量、可被索敌），返回其渲染对象。</summary>
    private async Task<ObjectRenderer> SpawnAuditTargetAsync()
    {
        const string targetName = "Chicken";
        // 先复用场上已有的、活着的靶子（上一轮技能刷的那只），避免反复刷怪
        // 触发服务端刷怪点占用与客户端对象表抖动。
        var existing = _objects.Values
            .Where(o => o?.Type == ObjectRenderer.Kind.Monster
                && o.MonsterInfo?.MonsterName == targetName
                && !o.Dead && IsInstanceValid(o))
            .OrderBy(o => Functions.Distance(_playerLocation, new System.Drawing.Point(o.CellX, o.CellY)))
            .FirstOrDefault();
        if (existing != null) return existing;
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
