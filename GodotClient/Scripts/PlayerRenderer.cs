using System;
using System.Collections.Generic;
using Godot;
using Library;
using Library.Network;
using ZirconClient.Controls;
using Library.SystemModels;
using ZirconClient.Formats;
using S = Library.Network.ServerPackets;

namespace ZirconClient.Scripts;

// 玩家外观渲染 (移植自 Client/Models/PlayerObject.cs DrawBody + UpdateLibraries)
// 分层顺序: 背武器/盾 -> 身体(ArmourFrame) -> 头(盔/发) -> 前武器
// 帧号公式 (4.3 节):
//   ArmourFrame = DrawFrame + (Costume>=0 ? Costume%10 : ArmourShape%11) * ArmourShapeOffSet + ArmourShift
//   HairFrame   = DrawFrame + (HairType-1) * 5000
//   WeaponFrame = DrawFrame + (WeaponShape%10) * 5000
//   DrawFrame   = FrameIndex + StartIndex + OffSet * (int)Direction   (OffSet=10 每方向)
public partial class PlayerRenderer : Node2D
{
    // ---- 外观状态 (与 ObjectPlayer/StartInformation 字段对应) ----
    public MirClass Class;
    // 旧版 Ctrl+右键查看玩家所需的角色索引。
    public int CharacterIndex;
    public MirGender Gender;
    public int HairType = 1;
    public Godot.Color HairColour = Colors.Black;
    public int ArmourShape;
    public Godot.Color ArmourColour = Colors.White;
    public int CostumeShape = -1;
    public int HelmetShape = 0;
    public int ShieldShape = -1;
    public int LibraryWeaponShape;
    public bool HideHead;
    public HorseType Horse = HorseType.None;
    public int HorseShape;
    public bool Dead;
    public bool Cloaked, GhostWalking, DragonRepulsed, ElementalHurricane;
    public bool DrawWeapon = true;

    // ---- M5 战斗: 玩家血量 (DataObjectHealthMana/MaxHealthMana) ----
    public int Health;
    public int MaxHealth;
    public int MaxMana;
    public bool ShowHealthBar;
    public string DisplayName;
    public string GuildName;
    public Color NameColour = Colors.White;
    public bool TargetHighlighted;
    public bool NameHovered;
    public Color TargetOutlineColour = Colors.Transparent;
    public int Light;
    public string ChatText;
    private double _chatUntil;
    public ExteriorEffect ArmourEffect, EmblemEffect, WeaponEffect, ShieldEffect;

    // ---- 动画状态 ----
    public MirDirection Direction;
    public MirAnimation Animation = MirAnimation.Standing;
    public int FrameIndex;
    public double FrameStartMs;   // 本帧序列开始时间
    private Frame _currentFrame;

    // 一次性动作动画 (Combat/Struck): 播完回 Standing
    private MirAnimation _oneShotAnim = MirAnimation.Standing;
    private MagicType _spellType = MagicType.None;
    private bool _rangeAttack;

    // ---- 施法动画结束事件 (原版 SetAction 用 OLD 动作跑 release switch 的移植) ----
    // 玩家动画有"忙碌则入队"的衔接逻辑, 施法实例必须和 _animationQueue 对齐:
    // 每个入队动作带一个 pending 记录, ApplyAnimation 应用动作时先触发上一个
    // 播放中的施法实例, 再消费本动作的 pending 记录成为新的"播放中"。
    public event Action<int, MagicType> SpellAnimEnded;
    private int _spellInstanceCounter;
    private (int Instance, MagicType Magic) _pendingSpell;
    private readonly Queue<(int Instance, MagicType Magic)> _pendingSpellQueue = new();
    private int _lastSpellInstance;
    private MagicType _lastSpellType = MagicType.None;
    public int LastSpellInstance => _lastSpellInstance;

    public int BeginSpell(MagicType magic)
    {
        _spellInstanceCounter++;
        _pendingSpell = (_spellInstanceCounter, magic);
        return _spellInstanceCounter;
    }
    private double _stanceUntilMs;
    private readonly Queue<MirAnimation> _animationQueue = new();
    private bool _animationComplete = true;
    public Action<MirAnimation, int, MagicType> FrameChanged;
    public Action<SoundIndex> SoundCue;
    public System.Drawing.Point FishingLocation;
    public bool FishFound;
    public uint TamingObjectID;

    // 移动插值 (格子坐标 -> 屏幕偏移)
    public int CellX, CellY;          // 服务端权威格子坐标
    public float OffsetX, OffsetY;    // 平滑移动的像素偏移
    public int MoveDistance { get; private set; }
    public bool IsMoveAnimation => Animation is MirAnimation.Walking or MirAnimation.Running
        or MirAnimation.HorseWalking or MirAnimation.HorseRunning
        or MirAnimation.CreepWalkSlow or MirAnimation.CreepWalkFast;
    // 原版 MapObject.MovingOffSet 使用当前走/跑帧表的 Sum，
    // 不应把所有职业、坐骑和特殊外观都硬编码成 600ms。
    public double MovementDurationMs => _currentFrame?.Sum > 0 ? _currentFrame.Sum : 600.0;
    public int MovementFrameCount => _currentFrame?.FrameCount ?? 1;
    public int MovementFrame => FrameIndex;
    /// <summary>施法动作正在播放；施法期间 MouseWalker 必须停止发移动。</summary>
    public bool IsSpellAnimation => _spellType != MagicType.None
        && Animation is MirAnimation.Combat1 or MirAnimation.Combat2 or MirAnimation.Combat3
            or MirAnimation.Combat4 or MirAnimation.Combat5 or MirAnimation.Combat6
            or MirAnimation.Combat7 or MirAnimation.Combat8 or MirAnimation.Combat9
            or MirAnimation.Combat10 or MirAnimation.Combat11 or MirAnimation.Combat12
            or MirAnimation.Combat13 or MirAnimation.Combat14 or MirAnimation.Combat15
            or MirAnimation.ChannellingStart or MirAnimation.ChannellingMiddle
            or MirAnimation.ChannellingEnd;

    // 旧端魔法特效不是收到 ObjectMagic 的瞬间就落地：人物先完成抬手，
    // 到施法动作的释放关键帧后才出现轨迹/命中特效。普通施法动作以第
    // 4 个逻辑帧作为释放点；短动作则取最后一帧前的时刻。
    public double SpellReleaseDelayMs
    {
        get
        {
            if (_currentFrame == null || _currentFrame.FrameCount <= 1)
                return 0;
            int releaseFrame = Math.Min(3, _currentFrame.FrameCount - 1);
            double delay = 0;
            for (int i = 0; i < releaseFrame && i < _currentFrame.Delays.Length; i++)
                delay += _currentFrame.Delays[i].TotalMilliseconds;
            return delay;
        }
    }
    private bool _remoteMoving;
    private System.Drawing.Point _remoteMoveFrom;
    private double _remoteMoveStartMs;
    private int _remoteMoveTargetX, _remoteMoveTargetY;
    private readonly Queue<(System.Drawing.Point To, MirDirection Direction, int Distance, bool Mounted)> _remoteMoveQueue = new();

    private const int CellWidth = 48;
    private const int CellHeight = 32;

    private ZlLibrary _bodyLib, _hairLib, _helmetLib, _weaponLib1, _weaponLib2, _shieldLib;
    private ZlLibrary _horseLib, _horseShadowLib, _horseEffectLib;

    public void UpdateAppearance(StartInformation info)
    {
        Class = info.Class;
        DisplayName = info.Name;
        GuildName = info.GuildName;
        NameColour = info.NameColour == System.Drawing.Color.Empty ? Colors.White : ToGodot(info.NameColour);
        Gender = info.Gender;
        HairType = info.HairType;
        HairColour = ToGodot(info.HairColour);
        ArmourShape = info.Armour;
        ArmourColour = ToGodot(info.ArmourColour);
        CostumeShape = info.Costume;
        HelmetShape = info.HelmetShape;
        ShieldShape = info.Shield;
        LibraryWeaponShape = info.Weapon;
        HideHead = info.HideHead;
        Horse = info.Horse;
        HorseShape = info.HorseShape;
        ArmourEffect = info.ArmourEffect;
        EmblemEffect = info.EmblemEffect;
        WeaponEffect = info.WeaponEffect;
        ShieldEffect = info.ShieldEffect;
        Direction = info.Direction;
        Dead = info.Dead;
        RefreshLibraries();
        // 原版构造 PlayerObject 后调用 SetFrame(Standing/Dead)，而 SetFrame
        // 会把有坐骑的 Standing 映射为 HorseStanding。不能沿用字段默认的
        // Standing，否则坐骑外观库虽然已加载，首帧仍会绘制人物身体。
        SetAnimation(Dead ? MirAnimation.Dead
            : Horse != HorseType.None ? MirAnimation.HorseStanding
            : MirAnimation.Standing);
        QueueRedraw();
    }

    public void UpdateAppearance(Library.Network.ServerPackets.ObjectPlayer info)
    {
        Class = info.Class;
        DisplayName = info.Name;
        GuildName = info.GuildName;
        NameColour = info.NameColour == System.Drawing.Color.Empty ? Colors.White : ToGodot(info.NameColour);
        Gender = info.Gender;
        HairType = info.HairType;
        HairColour = ToGodot(info.HairColour);
        ArmourShape = info.Armour;
        ArmourColour = ToGodot(info.ArmourColour);
        CostumeShape = info.Costume;
        HelmetShape = info.Helmet;
        ShieldShape = info.Shield;
        LibraryWeaponShape = info.Weapon;
        HideHead = info.HideHead;
        Horse = info.Horse;
        HorseShape = info.HorseShape;
        ArmourEffect = info.ArmourEffect;
        EmblemEffect = info.EmblemEffect;
        WeaponEffect = info.WeaponEffect;
        ShieldEffect = info.ShieldEffect;
        Direction = info.Direction;
        Dead = info.Dead;
        RefreshLibraries();
        SetAnimation(Dead ? MirAnimation.Dead
            : Horse != HorseType.None ? MirAnimation.HorseStanding
            : MirAnimation.Standing);
        QueueRedraw();
    }

    public void ApplyUpdate(S.PlayerUpdate info)
    {
        if (info == null) return;
        LibraryWeaponShape = info.Weapon;
        ShieldShape = info.Shield;
        ArmourShape = info.Armour;
        CostumeShape = info.Costume;
        ArmourColour = ToGodot(info.ArmourColour);
        ArmourEffect = info.ArmourEffect;
        EmblemEffect = info.EmblemEffect;
        WeaponEffect = info.WeaponEffect;
        ShieldEffect = info.ShieldEffect;
        HelmetShape = info.Helmet;
        HideHead = info.HideHead;
        Light = info.Light;
        int scalePercent = Math.Clamp(info.SizePercent, -50, 50);
        Scale = Vector2.One * ((100f + scalePercent) / 100f);
        RefreshLibraries();
        QueueRedraw();
    }

    public void ApplyCharacterUpdate(S.PlayerChangeUpdate info)
    {
        if (info == null) return;
        DisplayName = info.Name ?? DisplayName;
        Gender = info.Gender;
        HairType = info.HairType;
        HairColour = ToGodot(info.HairColour);
        ArmourColour = ToGodot(info.ArmourColour);
        RefreshLibraries();
        QueueRedraw();
    }

    private static Godot.Color ToGodot(System.Drawing.Color c)
    {
        return new Godot.Color(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
    }

    // 切换动画帧表 (Start/Count/OffSet), 参考 FrameSet.Players
    public void SetAnimation(MirAnimation anim)
    {
        // 原版 SetFrame: Standing/Dead 立即打断；其它动作在当前一次性
        // 动作播完后衔接，避免攻击/受击/施法互相覆盖第一帧。
        if (!_animationComplete && _oneShotAnim != MirAnimation.Standing
            && anim is not (MirAnimation.Standing or MirAnimation.Dead))
        {
            _animationQueue.Enqueue(anim);
            _pendingSpellQueue.Enqueue(_pendingSpell);
            _pendingSpell = (0, MagicType.None);
            return;
        }
        ApplyAnimation(anim, _pendingSpell);
        _pendingSpell = (0, MagicType.None);
    }

    private void ApplyAnimation(MirAnimation anim, (int Instance, MagicType Magic) pending)
    {
        // 离开上一个施法动作 → 释放 (原版 SetAction 的 OLD 动作 release switch)
        if (_lastSpellInstance != 0)
        {
            SpellAnimEnded?.Invoke(_lastSpellInstance, _lastSpellType);
            _lastSpellInstance = 0;
        }
        if (pending.Instance != 0)
        {
            _lastSpellInstance = pending.Instance;
            _lastSpellType = pending.Magic;
        }
        DrawWeapon = true;
        Animation = anim;
        _currentFrame = GetFrameTable(anim);
        FrameStartMs = Godot.Time.GetTicksMsec(); // 从当前时刻起播, 保证从第 0 帧开始
        FrameIndex = 0;
        // 一次性动作: 播完回 Standing；死亡动作保持最后一帧。
        _oneShotAnim = anim is MirAnimation.Combat1 or MirAnimation.Combat2 or MirAnimation.Combat3
            or MirAnimation.Combat4 or MirAnimation.Combat5 or MirAnimation.Combat6 or MirAnimation.Combat7
            or MirAnimation.Combat8 or MirAnimation.Combat9 or MirAnimation.Combat10 or MirAnimation.Combat11
            or MirAnimation.Combat12 or MirAnimation.Combat13 or MirAnimation.Combat14 or MirAnimation.Combat15
            or MirAnimation.Struck or MirAnimation.Pushed or MirAnimation.Harvest
            or MirAnimation.FishingCast or MirAnimation.FishingReel or MirAnimation.TamingCast
            or MirAnimation.ChannellingStart or MirAnimation.ChannellingEnd
            or MirAnimation.DragonRepulseStart or MirAnimation.DragonRepulseEnd
            or MirAnimation.Die or MirAnimation.Dead ? anim : MirAnimation.Standing;
        _animationComplete = _oneShotAnim == MirAnimation.Standing;
        QueueRedraw();
    }

    // M5 战斗: 玩家攻击/受击/死亡动画
    public void PlayCombat(MagicType magic)
    {
        _rangeAttack = false;
        _spellType = magic;
        _stanceUntilMs = Godot.Time.GetTicksMsec() + 3000.0;
        SetAnimation(Functions.GetAttackAnimation(Class, LibraryWeaponShape, magic));
    }

    public void PlayRangeAttack()
    {
        _rangeAttack = true;
        _spellType = MagicType.None;
        SetAnimation(MirAnimation.Combat1);
    }

    public void PlayDash(MagicType magic)
    {
        SetAnimation(magic is MagicType.ShoulderDash or MagicType.Assault
            ? MirAnimation.Combat8
            : Functions.GetAttackAnimation(Class, LibraryWeaponShape, magic));
    }

    public int PlaySpell(MagicType magic)
    {
        _spellType = magic;
        _stanceUntilMs = Godot.Time.GetTicksMsec() + 3000.0;
        MirAnimation anim;
        try { anim = Functions.GetMagicAnimation(magic); }
        catch (NotImplementedException) { anim = MirAnimation.Combat1; }
        // 原版在元素风暴持续施法结束时收到 Spell 动作，会播放收尾帧，
        // 而不是把所有阶段都当成普通一次性施法。
        if (magic == MagicType.ElementalHurricane && ElementalHurricane)
            anim = MirAnimation.ChannellingEnd;
        // Functions.GetMagicAnimation 与原版共用同一张技能动作表。
        // 不要把 Channelling/DragonRepulse 等合法动作降级成 Combat1。
        if (!FrameSet.Players.ContainsKey(anim))
        {
            GD.PrintErr($"[PlayerSpell] 缺少玩家动作帧表: Magic={magic}, Animation={anim}");
            anim = MirAnimation.Combat1;
        }
        int instance = BeginSpell(magic);
        SetAnimation(anim);
        if (magic == MagicType.PoisonousCloud)
            DrawWeapon = false;
        QueueRedraw();
        return instance;
    }

    public void PlayHarvest() => SetAnimation(MirAnimation.Harvest);

    public void PlayPushed() => SetAnimation(MirAnimation.Pushed);
    public void PlayMining()
    {
        _spellType = MagicType.None;
        SetAnimation(Functions.GetAttackAnimation(Class, LibraryWeaponShape, MagicType.None));
        SoundCue?.Invoke(SoundIndex.MiningHit);
    }

    public void PlayStandingForState()
    {
        MirAnimation standing = Godot.Time.GetTicksMsec() < _stanceUntilMs
            ? MirAnimation.Stance : MirAnimation.Standing;
        // 与原版 PlayerObject.SetFrame(Standing) 相同：持续施法/龙威压制
        // 覆盖坐骑和隐身站立动作，坐骑再覆盖普通站立。
        SetAnimation(ElementalHurricane ? MirAnimation.ChannellingMiddle
            : DragonRepulsed ? MirAnimation.DragonRepulseMiddle
            : Horse != HorseType.None ? MirAnimation.HorseStanding
            : Cloaked ? MirAnimation.CreepStanding
            : standing);
    }

    public void PlayDragonRepulseEnd()
    {
        SetAnimation(MirAnimation.DragonRepulseEnd);
    }

    public void RefreshAppearanceLibraries() => RefreshLibraries();

    /// <summary>
    /// 对照原版 PlayerObject.UpdateLibraries/DrawBody 的帧公式做组合矩阵检查。
    /// 该检查只验证“原版会选择的图库和帧确实存在”，不把缺少某件服务器装备
    /// 当成渲染成功；用于覆盖性回归而不是替代截图。
    /// </summary>
    public static bool RunAppearanceMatrixAudit(out int tested, out string failure)
    {
        tested = 0;
        failure = string.Empty;
        var directions = Enum.GetValues<MirDirection>();
        var classes = Enum.GetValues<MirClass>();
        var genders = Enum.GetValues<MirGender>();
        var animations = new[]
        {
            MirAnimation.Standing, MirAnimation.Walking, MirAnimation.Running,
            MirAnimation.HorseStanding, MirAnimation.HorseWalking, MirAnimation.HorseRunning,
            MirAnimation.Combat1, MirAnimation.Struck
        };

        foreach (var gender in genders)
        foreach (var @class in classes)
        for (int horseShape = 0; horseShape <= 7; horseShape++)
        foreach (var direction in directions)
        foreach (var animation in animations)
        {
            var player = new PlayerRenderer
            {
                Gender = gender,
                Class = @class,
                Direction = direction,
                HairType = 1,
                ArmourShape = 0,
                CostumeShape = -1,
                HelmetShape = 0,
                ShieldShape = -1,
                LibraryWeaponShape = 0,
                Horse = HorseType.Brown,
                HorseShape = horseShape,
            };
            player.RefreshLibraries();
            player.SetAnimation(animation);
            tested++;

            bool horseAnimation = animation is MirAnimation.HorseStanding
                or MirAnimation.HorseWalking or MirAnimation.HorseRunning;
            var checks = new List<(string Name, ZlLibrary Library, int Frame)>();
            if (horseAnimation)
            {
                int horseFrame = horseShape >= 4
                    ? player.DrawFrame
                    : player.DrawFrame + ((int)player.Horse - 1) * 5000;
                checks.Add(("horse-body", player._horseLib, horseFrame));

                int shadowFrame = horseShape >= 6
                    ? player.DrawFrame
                    : player.DrawFrame + ((int)player.Horse - 1) * 5000;
                checks.Add(("horse-shadow", horseShape >= 6 ? player._horseLib : player._horseShadowLib,
                    shadowFrame));
            }
            else
            {
                checks.Add(("body", player._bodyLib, player.ArmourFrame));
                checks.Add(("hair", player._hairLib, player.HairFrame));
                checks.Add(("weapon", player._weaponLib1, player.WeaponFrame));
            }
        foreach (var check in checks)
            {
                if (check.Library == null || check.Frame < 0 || check.Frame >= check.Library.Images.Length
                    || check.Library.Images[check.Frame] == null)
                {
                    failure = $"gender={gender} class={@class} horseShape={horseShape} " +
                              $"direction={direction} animation={animation} layer={check.Name} frame={check.Frame} " +
                              $"count={check.Library?.Images.Length ?? 0}";
                    return false;
                }
            }
        }

        // 装备矩阵：原版 UpdateLibraries 使用的是“库键”和“库内形状偏移”两套
        // 索引。仅用默认装备会漏掉女装、刺客库、时装、盾牌和双手武器的错位。
        // 这里逐项使用原版实际存在的键，并覆盖所有方向/动作，确保每一层都
        // 指向正确图库且帧号没有越界。
        var armourShapes = new[] { 0, 11, 22, 33, 44, 110, 121, 132, 143, 220 };
        var costumeShapes = new[] { 0, 10 };
        var helmetShapes = new[] { 1, 2, 3, 4, 5, 11, 12, 13, 14, 21 };
        var shieldShapes = new[] { 0, 10 };
        var weaponShapes = new[] { 0, 10, 20, 30, 40, 50, 60, 90, 100, 1100, 1110, 1120, 1250, 1700 };
        foreach (var gender in genders)
        foreach (var @class in classes)
        foreach (var direction in directions)
        foreach (var animation in animations)
        {
            foreach (var armourShape in armourShapes)
            {
                var player = CreateAppearanceAuditPlayer(gender, @class, direction, animation);
                player.ArmourShape = armourShape;
                player.RefreshLibraries();
                if (!ValidateAppearanceLayer(player, "armour", player._bodyLib, player.ArmourFrame,
                        gender, @class, armourShape, direction, animation, out failure)) return false;
                tested++;
            }
            foreach (var costumeShape in costumeShapes)
            {
                var player = CreateAppearanceAuditPlayer(gender, @class, direction, animation);
                player.CostumeShape = costumeShape;
                player.RefreshLibraries();
                if (!ValidateAppearanceLayer(player, "costume", player._bodyLib, player.ArmourFrame,
                        gender, @class, costumeShape, direction, animation, out failure)) return false;
                tested++;
            }
            foreach (var helmetShape in helmetShapes)
            {
                var player = CreateAppearanceAuditPlayer(gender, @class, direction, animation);
                player.HelmetShape = helmetShape;
                player.RefreshLibraries();
                if (!ValidateAppearanceLayer(player, "helmet", player._helmetLib, player.HelmetFrame,
                        gender, @class, helmetShape, direction, animation, out failure)) return false;
                tested++;
            }
            foreach (var shieldShape in shieldShapes)
            {
                var player = CreateAppearanceAuditPlayer(gender, @class, direction, animation);
                player.ShieldShape = shieldShape;
                player.RefreshLibraries();
                if (!ValidateAppearanceLayer(player, "shield", player._shieldLib, player.ShieldFrame,
                        gender, @class, shieldShape, direction, animation, out failure)) return false;
                tested++;
            }
            foreach (var weaponShape in weaponShapes)
            {
                var player = CreateAppearanceAuditPlayer(gender, @class, direction, animation);
                player.LibraryWeaponShape = weaponShape;
                player.RefreshLibraries();
                if (!ValidateAppearanceLayer(player, "weapon", player._weaponLib1, player.WeaponFrame,
                        gender, @class, weaponShape, direction, animation, out failure)) return false;
                if (weaponShape >= 1200 && weaponShape != 1263 && player._weaponLib2 != null
                    && !ValidateAppearanceLayer(player, "weapon-right", player._weaponLib2, player.WeaponFrame,
                        gender, @class, weaponShape, direction, animation, out failure)) return false;
                tested++;
            }
        }
        return true;
    }

    private static PlayerRenderer CreateAppearanceAuditPlayer(MirGender gender, MirClass @class,
        MirDirection direction, MirAnimation animation)
    {
        var player = new PlayerRenderer
        {
            Gender = gender, Class = @class, Direction = direction,
            HairType = 1, ArmourShape = 0, CostumeShape = -1, HelmetShape = 0,
            ShieldShape = -1, LibraryWeaponShape = 0, Horse = HorseType.None,
        };
        player.RefreshLibraries();
        player.SetAnimation(animation);
        return player;
    }

    private static bool ValidateAppearanceLayer(PlayerRenderer player, string layer, ZlLibrary library,
        int frame, MirGender gender, MirClass @class, int shape, MirDirection direction,
        MirAnimation animation, out string failure)
    {
        // ZL 装备库允许合法的空帧（原版 DrawBody 对 GetImage==null 直接跳过），
        // 但图库缺失或帧越界一定是映射错误。基础身体/发型矩阵仍在上面的
        // 主循环中严格检查像素；这里的装备组合重点检查原版映射与帧地址。
        if (library == null || frame < 0 || frame >= library.Images.Length)
        {
            failure = $"gender={gender} class={@class} shape={shape} direction={direction} "
                    + $"animation={animation} layer={layer} frame={frame} count={library?.Images.Length ?? 0}";
            return false;
        }
        failure = string.Empty;
        return true;
    }

    public void PlayFishing(FishingState state, bool fishFound, System.Drawing.Point floatLocation)
    {
        FishFound = fishFound;
        FishingLocation = floatLocation;
        SetAnimation(state == FishingState.Cast
            ? (Animation is MirAnimation.FishingWait or MirAnimation.FishingCast ? MirAnimation.FishingWait : MirAnimation.FishingCast)
            : (Animation == MirAnimation.FishingWait ? MirAnimation.FishingReel : MirAnimation.Standing));
    }

    public void PlayTaming(TamingState state, uint tamingObjectID = 0)
    {
        if (tamingObjectID != 0) TamingObjectID = tamingObjectID;
        SetAnimation(Animation is MirAnimation.TamingCast or MirAnimation.TamingWait
            ? MirAnimation.TamingWait : MirAnimation.TamingCast);
    }

    public void BeginMove(MirDirection direction, int distance, bool mounted)
        => BeginMove(direction, distance, mounted, distance >= 2);

    public void BeginMove(MirDirection direction, int distance, bool mounted, bool running)
    {
        Direction = direction;
        MoveDistance = Math.Max(1, distance);
        InterruptActionForMovement();
        // 原版 Moving 的优先级是：隐身步行先于跑步；普通状态才按
        // distance>=2 切换 Running/HorseRunning。
        ApplyAnimation(Cloaked
            ? (GhostWalking ? MirAnimation.CreepWalkFast : MirAnimation.CreepWalkSlow)
            : running
                ? (mounted ? MirAnimation.HorseRunning : MirAnimation.Running)
                : (mounted ? MirAnimation.HorseWalking : MirAnimation.Walking),
            (0, MagicType.None));
        _pendingSpell = (0, MagicType.None);
    }

    /// <summary>
    /// 移动插值期间保持动作与位移一致。
    /// 回包可能同时带来转身/状态更新；这些更新不能把正在移动的人物切回站立，
    /// 否则会出现“站立姿势漂移到目标格”。已经是正确动作时不重置帧起点。
    /// </summary>
    public void EnsureMoveAnimation(bool mounted, bool running)
    {
        MirAnimation expected = Cloaked
            ? (GhostWalking ? MirAnimation.CreepWalkFast : MirAnimation.CreepWalkSlow)
            : running
                ? (mounted ? MirAnimation.HorseRunning : MirAnimation.Running)
                : (mounted ? MirAnimation.HorseWalking : MirAnimation.Walking);

        if (Animation == expected) return;

        InterruptActionForMovement();
        ApplyAnimation(expected, (0, MagicType.None));
        _pendingSpell = (0, MagicType.None);
    }

    /// <summary>
    /// 用户主动点击空白或切换目标时，立即结束旧的一次性动作。
    /// SetAnimation 的默认语义是排队衔接，适合服务端连续动作，但不适合
    /// “攻击中改走位”：如果把 Walking 排在 Combat 后面，角色会保持攻击
    /// 姿势滑行。移动和主动取消都必须清掉这条队列。
    /// </summary>
    public void StopActionForInput()
    {
        InterruptActionForMovement();
        PlayStandingForState();
    }

    private void InterruptActionForMovement()
    {
        _animationQueue.Clear();
        _pendingSpellQueue.Clear();
        _pendingSpell = (0, MagicType.None);
        _oneShotAnim = MirAnimation.Standing;
        _animationComplete = true;
        _spellType = MagicType.None;
        _rangeAttack = false;
        _stanceUntilMs = 0;
    }

    // 其他玩家的移动回包：权威坐标立即到终点，画面从起点平滑回拉。
    public void StartMove(System.Drawing.Point to, MirDirection direction, int distance, bool mounted)
    {
        distance = Math.Max(1, distance);
        if (_remoteMoving)
        {
            _remoteMoveQueue.Enqueue((to, direction, distance, mounted));
            return;
        }
        StartRemoteMove(to, direction, distance, mounted);
    }

    private void StartRemoteMove(System.Drawing.Point to, MirDirection direction, int distance, bool mounted)
    {
        _remoteMoveFrom = new System.Drawing.Point(CellX, CellY);
        _remoteMoveTargetX = to.X;
        _remoteMoveTargetY = to.Y;
        CellX = to.X;
        CellY = to.Y;
        _remoteMoveStartMs = Godot.Time.GetTicksMsec();
        _remoteMoving = true;
        BeginMove(direction, distance, mounted, distance >= 2);
        // 立即把画面停在移动起点（权威格已到终点，靠 Offset 反向回拉补间）。
        // 否则调用方（GameScene.OnObjectMove）随后同步 Position 时 Offset 还是 0，
        // 会先瞬移到终点再补间，视觉上仍是跳格。
        float xStep = CellWidth * MoveDistance;
        float yStep = CellHeight * MoveDistance;
        switch (Direction)
        {
            case MirDirection.Up: OffsetY = yStep; break;
            case MirDirection.UpRight: OffsetX = -xStep; OffsetY = yStep; break;
            case MirDirection.Right: OffsetX = -xStep; break;
            case MirDirection.DownRight: OffsetX = -xStep; OffsetY = -yStep; break;
            case MirDirection.Down: OffsetY = -yStep; break;
            case MirDirection.DownLeft: OffsetX = xStep; OffsetY = -yStep; break;
            case MirDirection.Left: OffsetX = xStep; break;
            case MirDirection.UpLeft: OffsetX = xStep; OffsetY = yStep; break;
        }
    }

    public int RenderY => OffsetX != 0 || OffsetY != 0
        ? (Direction is MirDirection.Up or MirDirection.UpRight or MirDirection.UpLeft
            ? CellY + MoveDistance : CellY)
        : CellY;

    public void PlayStruck() => SetAnimation(Horse != HorseType.None
        ? MirAnimation.HorseStruck : MirAnimation.Struck);

    public void PlayDie()
    {
        SetAnimation(MirAnimation.Die);
        Dead = true;
    }

    private Frame GetFrameTable(MirAnimation anim)
    {
        // FrameSet.Players 同时包含普通、施法、持续施法和骑马动作。
        // 之前的 switch 漏掉 Channelling/DragonRepulse，导致这些技能直接
        // 使用 DefaultMonster.Standing，表现为人物不做施法动作。
        if (FrameSet.Players.TryGetValue(anim, out var frame)) return frame;
        return FrameSet.DefaultMonster[MirAnimation.Standing];
    }

    // 由 FrameSet.Frame 结构: Start/Count/OffSet/Delays
    private int GetFrameIndex(double nowMs, bool loop)
    {
        if (_currentFrame == null) return 0;
        if (_currentFrame.FrameCount <= 1) return 0;

        double sum = _currentFrame.Sum;
        if (sum <= 0) return 0;
        double elapsed = nowMs - FrameStartMs;
        if (loop)
            elapsed %= sum;
        else if (elapsed >= sum)
            return _currentFrame.Reversed ? 0 : _currentFrame.FrameCount - 1;

        int frame = 0;
        double acc = 0;
        if (_currentFrame.Reversed)
        {
            for (int i = 0; i < _currentFrame.FrameCount; i++)
            {
                int source = _currentFrame.FrameCount - 1 - i;
                acc += _currentFrame.Delays[source].TotalMilliseconds;
                if (elapsed < acc) return source;
            }
            return 0;
        }

        for (int i = 0; i < _currentFrame.FrameCount; i++)
        {
            acc += _currentFrame.Delays[i].TotalMilliseconds;
            if (elapsed < acc) return i;
        }
        return frame;
    }

    public override void _Process(double delta)
    {
        double nowMs = Godot.Time.GetTicksMsec();
        int frame = GetFrameIndex(nowMs, _oneShotAnim == MirAnimation.Standing);
        if (frame != FrameIndex)
        {
            FrameIndex = frame;
            FrameChanged?.Invoke(Animation, frame, _spellType);
            if (Animation is MirAnimation.Combat1 or MirAnimation.Combat2 or MirAnimation.Combat3
                or MirAnimation.Combat4 or MirAnimation.Combat5 or MirAnimation.Combat6
                or MirAnimation.Combat7 or MirAnimation.Combat8 or MirAnimation.Combat9
                or MirAnimation.Combat10 or MirAnimation.Combat11 or MirAnimation.Combat12
                or MirAnimation.Combat13 or MirAnimation.Combat14 or MirAnimation.Combat15)
            {
                // MapObject.FrameIndexChanged: normal attack sound at frame 1,
                // ranged attack/projectile sound at frame 4.
                if (frame == 1) SoundCue?.Invoke(GetAttackSound());
                else if (frame == 4 && _rangeAttack)
                    SoundCue?.Invoke(GetAttackSound());
            }
            else if (Animation is MirAnimation.Struck or MirAnimation.HorseStruck && frame == 0)
            {
                SoundCue?.Invoke(Gender == MirGender.Male ? SoundIndex.MaleStruck : SoundIndex.FemaleStruck);
                SoundCue?.Invoke(SoundIndex.GenericStruckPlayer);
            }
            else if (Animation == MirAnimation.Die && frame == 0)
                SoundCue?.Invoke(Gender == MirGender.Male ? SoundIndex.MaleDie : SoundIndex.FemaleDie);
            if (_spellType == MagicType.CrushingWave && frame == 4)
                SoundCue?.Invoke(SoundIndex.DestructiveSurge);
            if (_spellType == MagicType.OffensiveBlow && frame == 3)
                SoundCue?.Invoke(SoundIndex.OffensiveBlow);
            if (_spellType == MagicType.SweetBrier && frame == 1)
                SoundCue?.Invoke(Gender == MirGender.Male ? SoundIndex.SweetBrierMale : SoundIndex.SweetBrierFemale);
            if (Animation is MirAnimation.Walking or MirAnimation.Running
                or MirAnimation.CreepWalkSlow or MirAnimation.CreepWalkFast
                && frame is 1 or 4)
                SoundCue?.Invoke((SoundIndex)((int)SoundIndex.Foot1 + (int)(GD.Randi() % 4)));
            else if (Animation == MirAnimation.HorseWalking && frame == 1)
                SoundCue?.Invoke(SoundIndex.HorseWalk1);
            else if (Animation == MirAnimation.HorseWalking && frame == 4)
                SoundCue?.Invoke(SoundIndex.HorseWalk2);
            else if (Animation == MirAnimation.HorseRunning && frame == 1)
                SoundCue?.Invoke(SoundIndex.HorseRun);
            QueueRedraw();
        }

        // 一次性动作播完回 Standing (死亡保持 Die 帧)
        if (_oneShotAnim != MirAnimation.Standing && _oneShotAnim != MirAnimation.Die
            && _oneShotAnim != MirAnimation.Dead && !Dead)
        {
            var f = _currentFrame;
            if (f != null && nowMs - FrameStartMs >= f.Sum)
            {
                // 原版持续施法不是起手动作结束就回到站立。
                if (Animation == MirAnimation.ChannellingStart &&
                    _spellType == MagicType.ElementalHurricane)
                {
                    ApplyAnimation(MirAnimation.ChannellingMiddle, (0, MagicType.None));
                }
                else if (_animationQueue.Count > 0)
                {
                    var record = _pendingSpellQueue.Count > 0
                        ? _pendingSpellQueue.Dequeue()
                        : (0, MagicType.None);
                    ApplyAnimation(_animationQueue.Dequeue(), record);
                }
                else
                {
                    _oneShotAnim = MirAnimation.Standing;
                    _animationComplete = true;
                    PlayStandingForState();
                }
            }
        }

        if (_remoteMoving && _currentFrame != null)
        {
            double k;
            if (ClientSettings.SmoothMove)
            {
                double t = Math.Clamp((nowMs - _remoteMoveStartMs) / Math.Max(1.0, _currentFrame.Sum), 0.0, 1.0);
                k = 1.0 - t;
            }
            else
            {
                k = Math.Max(0.0, (_currentFrame.FrameCount - (FrameIndex + 1)) / (double)Math.Max(1, _currentFrame.FrameCount));
            }
            float xStep = CellWidth * MoveDistance * (float)k;
            float yStep = CellHeight * MoveDistance * (float)k;
            OffsetX = 0f;
            OffsetY = 0f;
            switch (Direction)
            {
                case MirDirection.Up: OffsetY = yStep; break;
                case MirDirection.UpRight: OffsetX = -xStep; OffsetY = yStep; break;
                case MirDirection.Right: OffsetX = -xStep; break;
                case MirDirection.DownRight: OffsetX = -xStep; OffsetY = -yStep; break;
                case MirDirection.Down: OffsetY = -yStep; break;
                case MirDirection.DownLeft: OffsetX = xStep; OffsetY = -yStep; break;
                case MirDirection.Left: OffsetX = xStep; break;
                case MirDirection.UpLeft: OffsetX = xStep; OffsetY = yStep; break;
            }
            if (k <= 0.0)
            {
                _remoteMoving = false;
                OffsetX = 0f;
                OffsetY = 0f;
                if (_remoteMoveQueue.Count > 0)
                {
                    var next = _remoteMoveQueue.Dequeue();
                    StartRemoteMove(next.To, next.Direction, next.Distance, next.Mounted);
                }
                else if (Animation is MirAnimation.Walking or MirAnimation.Running
                    or MirAnimation.HorseWalking or MirAnimation.HorseRunning
                    or MirAnimation.CreepWalkSlow or MirAnimation.CreepWalkFast)
                {
                    PlayStandingForState();
                }
            }
        }
    }

    public void StopRemoteMovement()
    {
        _remoteMoveQueue.Clear();
        _remoteMoving = false;
        OffsetX = 0f;
        OffsetY = 0f;
    }

    private SoundIndex GetAttackSound()
    {
        if (Class == MirClass.Assassin)
        {
            if (LibraryWeaponShape >= 1200) return SoundIndex.ClawAttack;
            if (LibraryWeaponShape >= 1100) return SoundIndex.GlaiveAttack;
        }
        return LibraryWeaponShape switch
        {
            100 => SoundIndex.WandSwing,
            9 or 101 => SoundIndex.WoodSwing,
            102 => SoundIndex.AxeSwing,
            103 => SoundIndex.DaggerSwing,
            104 => SoundIndex.ShortSwordSwing,
            26 or 105 => SoundIndex.IronSwordSwing,
            _ => SoundIndex.FistSwing,
        };
    }

    // 帧号计算 (4.3 节)
    private int DrawFrame => _currentFrame == null
        ? 0
        : FrameIndex + _currentFrame.StartIndex + _currentFrame.OffSet * (int)Direction;
    private int ArmourShapeOffSet => Class == MirClass.Assassin ? 3000 : 5000;
    private int ArmourShift => Class != MirClass.Assassin ? 0 : Animation switch
    {
        MirAnimation.Standing => 0,
        MirAnimation.Walking or MirAnimation.Running => 1600,
        MirAnimation.CreepStanding or MirAnimation.CreepWalkSlow or MirAnimation.CreepWalkFast => 240,
        MirAnimation.Pushed => 160,
        MirAnimation.Combat1 => -400,
        MirAnimation.Combat2 => 0,
        MirAnimation.Combat3 => 0,
        MirAnimation.Combat4 => 80,
        MirAnimation.Combat5 or MirAnimation.Combat6 or MirAnimation.Combat7 => 400,
        MirAnimation.Combat8 => 720,
        MirAnimation.Combat9 => -960,
        MirAnimation.Combat10 => -480,
        MirAnimation.Combat11 or MirAnimation.Combat12 or MirAnimation.Combat13 => -400,
        MirAnimation.Combat14 or MirAnimation.DragonRepulseStart
            or MirAnimation.DragonRepulseMiddle or MirAnimation.DragonRepulseEnd => 0,
        MirAnimation.Harvest => 160,
        MirAnimation.TamingCast or MirAnimation.TamingWait => 0,
        MirAnimation.Stance => 160,
        MirAnimation.Struck => -640,
        MirAnimation.Die or MirAnimation.Dead => -400,
        MirAnimation.HorseStanding or MirAnimation.HorseWalking
            or MirAnimation.HorseRunning or MirAnimation.HorseStruck
            or MirAnimation.FishingCast or MirAnimation.FishingWait
            or MirAnimation.FishingReel => 80,
        _ => 0,
    };
    public double DrawHealthUntilMs;

    public void ApplyPacket(MirDirection dir, System.Drawing.Point location)
    {
        Direction = dir;
        CellX = location.X;
        CellY = location.Y;
    }

    private int ArmourFrame => DrawFrame + (CostumeShape >= 0 ? (CostumeShape % 10) : (ArmourShape % 11)) * ArmourShapeOffSet + ArmourShift;
    private int HairFrame => DrawFrame + (HairType - 1) * 5000;
    private int HelmetFrame => DrawFrame + ((HelmetShape - 1) % 10) * ArmourShapeOffSet + ArmourShift;
    private int WeaponFrame => DrawFrame + (WeaponShape % 10) * 5000;
    private int ShieldFrame => DrawFrame + (ShieldShape % 10) * ArmourShapeOffSet + ArmourShift;
    private int WeaponShape => LibraryWeaponShape >= 1000 ? LibraryWeaponShape - 1000 : LibraryWeaponShape;

    private static readonly HashSet<int> CostumeShapeHideWeapon = new() { 6, 7, 8, 9, 10, 11, 12, 13, 16, 17, 18 };

    private bool _debugLogged;
    private readonly List<BlendImageLayerNode> _exteriorBlendLayers = new();

    public override void _Draw()
    {
        if (_bodyLib == null) return;
        foreach (var layer in _exteriorBlendLayers)
            layer.Visible = false;
        if (!_debugLogged)
        {
            _debugLogged = true;
            GD.Print($"[PlayerView] 首帧诊断: body={_bodyLib.FileName} hair={_hairLib?.FileName ?? "null"} " +
                     $"helmet={_helmetLib?.FileName ?? "null"} weapon1={_weaponLib1?.FileName ?? "null"} " +
                     $"dir={Direction} frame={FrameIndex} anim={Animation} " +
                     $"DrawFrame={DrawFrame} ArmourFrame={ArmourFrame} HairFrame={HairFrame} " +
                     $"Cell=({CellX},{CellY}) Pos={Position}");
        }
        DrawShadow();
        DrawExteriorEffects(true);
        if (TargetHighlighted && ClientSettings.ShowTargetOutline)
            DrawTargetOutline();
        DrawPlayerAt(0, 0);
        DrawExteriorEffects(false);
        float nameY = RenderPrimitives.NameAboveHealthBarBaseline(9f);
        if (NameHovered && ClientSettings.ShowPlayerNames && !string.IsNullOrWhiteSpace(DisplayName))
            RenderPrimitives.DrawLabel(this, DisplayName, new Vector2(24f, nameY), NameColour, 9f);
        if (NameHovered && ClientSettings.ShowPlayerNames && !string.IsNullOrWhiteSpace(GuildName))
            RenderPrimitives.DrawLabel(this, GuildName, new Vector2(24f, nameY - 11f), new Color(0.8f, 0.8f, 0.4f), 8f);
        if (NameHovered && ClientSettings.ShowPlayerNames && !string.IsNullOrWhiteSpace(ChatText) && Godot.Time.GetTicksMsec() < _chatUntil)
            RenderPrimitives.DrawLabel(this, ChatText, new Vector2(24f, nameY - 22f), Colors.White, 9f);

        // 玩家头顶血条 (受击显示 5 秒)
        if (ShowHealthBar && ClientSettings.ShowUserHealth && !Dead && MaxHealth > 0 && Godot.Time.GetTicksMsec() <= DrawHealthUntilMs)
        {
            float percent = Math.Clamp(Health / (float)MaxHealth, 0f, 1f);
            if (percent > 0f)
            {
                var background = MirSkin.GetTexture(LibraryFile.Interface, 80);
                var fill = MirSkin.GetTexture(LibraryFile.Interface, 79);
                if (background == null || fill == null) return;
                Vector2 bgSize = background.GetSize();
                Vector2 fillSize = fill.GetSize();
                float x = 24f - bgSize.X / 2f, y = -55f;
                DrawTextureRect(background, new Rect2(x, y, bgSize.X, bgSize.Y), false);
                float width = Math.Clamp((int)(fillSize.X * percent), 1, (int)fillSize.X);
                DrawTextureRectRegion(fill, new Rect2(x + 1f, y + 1f, width, fillSize.Y),
                    new Rect2(0, 0, width, fillSize.Y), Colors.White);
            }
        }
    }

    public void SetChat(string text)
    {
        ChatText = text;
        _chatUntil = Godot.Time.GetTicksMsec() + 5000;
        QueueRedraw();
    }

    // 供 GameScene 调用: 计算本节点屏幕位置
    public void ComputeScreenPos(int camCenterX, int camCenterY, int viewRangeX, int viewRangeY, float screenOffsetX, float screenOffsetY)
    {
        Position = new Vector2(
            (CellX - camCenterX + viewRangeX) * CellWidth + screenOffsetX + OffsetX,
            (CellY - camCenterY + viewRangeY + 1) * CellHeight + screenOffsetY - 34 + OffsetY
        );
    }

    private void DrawPlayerAt(float px, float py, Color? tint = null)
    {
        bool hideWeapon = CostumeShapeHideWeapon.Contains(CostumeShape);

        // 坐骑必须位于人物所有装备层之前，阴影单独绘制在人物和坐骑的共同基线。
        if (Animation is MirAnimation.HorseStanding or MirAnimation.HorseWalking
            or MirAnimation.HorseRunning or MirAnimation.HorseStruck)
            DrawHorse(px, py, tint);

        // 1. 背武器 (Up/DownLeft/Left/UpLeft 方向)
        if (!hideWeapon && DrawWeapon)
        {
            if (Direction is MirDirection.Up or MirDirection.DownLeft or MirDirection.Left or MirDirection.UpLeft)
                DrawLayer(_weaponLib2 ?? _weaponLib1, WeaponFrame, px, py, tint);

            // 2. 背盾 (UpRight/Right/DownRight)
            if (ShieldShape >= 0 && Direction is MirDirection.UpRight or MirDirection.Right or MirDirection.DownRight)
                DrawLayer(_shieldLib, ShieldFrame, px, py, tint);
        }

        // 3. 身体
        DrawLayer(_bodyLib, ArmourFrame, px, py, tint);
        if (!tint.HasValue && ArmourColour != Colors.White)
            DrawOverlay(_bodyLib, ArmourFrame, px, py, ArmourColour);

        // 4. 头 (盔优先, 否则发)
        if (!HideHead)
        {
            if (HelmetShape > 0)
                DrawLayer(_helmetLib, HelmetFrame, px, py, tint);
            else if (HairType > 0)
                DrawLayer(_hairLib, HairFrame, px, py, tint ?? HairColour);
        }

        // 5. 前武器 (UpRight/Right/DownRight/Down)
        if (!hideWeapon && DrawWeapon)
        {
            if (Direction is MirDirection.UpRight or MirDirection.Right or MirDirection.DownRight or MirDirection.Down)
                DrawLayer(_weaponLib1, WeaponFrame, px, py, tint);
        }
    }

    private void DrawTargetOutline()
    {
        var colour = TargetOutlineColour.A > 0f ? TargetOutlineColour : Colors.Cyan;
        colour.A = 0.92f;
        // 原版 EnableOutlineEffect 是主体合成纹理的 2px 外扩；先画外圈，
        // 再由正常 DrawPlayerAt 覆盖内部，保留坐骑和装备的真实轮廓。
        for (int y = -2; y <= 2; y++)
        for (int x = -2; x <= 2; x++)
            if (Math.Abs(x) == 2 || Math.Abs(y) == 2)
                DrawPlayerAt(x, y, colour);
    }

    private void DrawExteriorEffects(bool behind)
    {
        if (!ClientSettings.DrawEffects) return;
        if (CostumeShape < 0) DrawExteriorEffect(ArmourEffect, behind);
        DrawExteriorEffect(EmblemEffect, behind);
        if (!CostumeShapeHideWeapon.Contains(CostumeShape))
        {
            DrawExteriorEffect(WeaponEffect, behind);
            DrawExteriorEffect(ShieldEffect, behind);
        }
    }

    private void DrawExteriorEffect(ExteriorEffect effect, bool behind)
    {
        if (effect == ExteriorEffect.None) return;
        if (behind != DrawExteriorEffectBehind(Direction, effect)) return;

        int tick = (int)(Godot.Time.GetTicksMsec() / 100);
        int slowTick = tick / 2; // old ExteriorEffectManager uses Animation / 2
        int dir = (int)Direction;
        float drawX = 0f, drawY = 0f;
        DetermineExteriorOffset(effect, out drawX, out drawY);
        ZlLibrary lib = null;
        int frame = 0;
        float alpha = 1f;
        switch (effect)
        {
            case ExteriorEffect.A_WhiteAura: lib = LibraryCache.Get(LibraryFile.EquipEffect_Part); frame = 800 + slowTick % 13; alpha = 0.7f; break;
            case ExteriorEffect.A_FlameAura: lib = LibraryCache.Get(LibraryFile.EquipEffect_Part); frame = 820 + slowTick % 13; alpha = 0.7f; break;
            case ExteriorEffect.A_BlueAura: lib = LibraryCache.Get(LibraryFile.EquipEffect_Part); frame = 840 + slowTick % 13; alpha = 0.7f; break;
            case ExteriorEffect.A_FlameAura2: lib = LibraryCache.Get(LibraryFile.EquipEffect_Part); frame = (Gender == MirGender.Male ? 1700 : 1720) + tick % 10; break;
            case ExteriorEffect.A_GreenWings: lib = LibraryCache.Get(LibraryFile.EquipEffect_Part); frame = 400 + slowTick % 15 + dir * 20; break;
            case ExteriorEffect.A_FlameWings: lib = LibraryCache.Get(LibraryFile.EquipEffect_Part); frame = 200 + slowTick % 15 + dir * 20; break;
            case ExteriorEffect.A_BlueWings: lib = LibraryCache.Get(LibraryFile.EquipEffect_Part); frame = slowTick % 15 + dir * 20; break;
            case ExteriorEffect.A_RedSinWings: lib = LibraryCache.Get(LibraryFile.EquipEffect_Part); frame = 600 + slowTick % 13 + dir * 20; break;
            case ExteriorEffect.A_PurpleTentacles2: lib = LibraryCache.Get(LibraryFile.EquipEffect_Part); frame = 4454 + slowTick % 4 + dir * 9; break;
            case ExteriorEffect.A_DiamondFireWings: lib = LibraryCache.Get(LibraryFile.EquipEffect_Part); frame = 4566 + slowTick % 4 + dir * 9; break;
            case ExteriorEffect.A_PhoenixWings: lib = LibraryCache.Get(LibraryFile.EquipEffect_Part); frame = 4062 + slowTick % 8 + dir * 20; break;
            case ExteriorEffect.A_IceKingWings: lib = LibraryCache.Get(LibraryFile.EquipEffect_Part); frame = 4258 + slowTick % 8 + dir * 20; break;
            case ExteriorEffect.A_BlueButterflyWings: lib = LibraryCache.Get(LibraryFile.EquipEffect_Part); frame = 4678 + slowTick % 8 + dir * 20; break;
            case ExteriorEffect.A_FireDragonWings: lib = LibraryCache.Get(LibraryFile.EquipEffect_Full); frame = 0 + (int)Gender * 5000 + DrawFrame; break;
            case ExteriorEffect.A_SmallYellowWings: lib = LibraryCache.Get(LibraryFile.EquipEffect_Full); frame = 10000 + (int)Gender * 5000 + DrawFrame; break;
            case ExteriorEffect.A_GreenFeatherWings: lib = LibraryCache.Get(LibraryFile.EquipEffect_Full); frame = 50000 + (int)Gender * 5000 + DrawFrame; break;
            case ExteriorEffect.A_RedFeatherWings: lib = LibraryCache.Get(LibraryFile.EquipEffect_Full); frame = 60000 + (int)Gender * 5000 + DrawFrame; break;
            case ExteriorEffect.A_BlueFeatherWings: lib = LibraryCache.Get(LibraryFile.EquipEffect_Full); frame = 70000 + (int)Gender * 5000 + DrawFrame; break;
            case ExteriorEffect.A_WhiteFeatherWings: lib = LibraryCache.Get(LibraryFile.EquipEffect_Full); frame = 80000 + (int)Gender * 5000 + DrawFrame; break;
            case ExteriorEffect.A_PurpleTentacles: lib = LibraryCache.Get(LibraryFile.EquipEffect_Full); frame = 90000 + (int)Gender * 5000 + DrawFrame; break;
            case ExteriorEffect.A_LionWings: lib = LibraryCache.Get(LibraryFile.EquipEffect_FullEx1); frame = DrawFrame; break;
            case ExteriorEffect.A_AngelicWings: lib = LibraryCache.Get(LibraryFile.EquipEffect_FullEx1); frame = 10000 + DrawFrame; break;
            case ExteriorEffect.A_BlueDragonWings: lib = LibraryCache.Get(LibraryFile.EquipEffect_FullEx2); frame = DrawFrame; break;
            case ExteriorEffect.A_RedWings2: lib = LibraryCache.Get(LibraryFile.EquipEffect_FullEx3); frame = DrawFrame; break;
            case ExteriorEffect.W_ChaoticHeavenBlade: lib = LibraryCache.Get(LibraryFile.EquipEffect_Full); frame = 40000 + (int)Gender * 5000 + DrawFrame; break;
            case ExteriorEffect.W_JanitorsScimitar or ExteriorEffect.W_JanitorsDualBlade: lib = LibraryCache.Get(LibraryFile.EquipEffect_Full); frame = 20000 + (int)Gender * 5000 + DrawFrame; break;
            case ExteriorEffect.E_RedEyeRing: lib = LibraryCache.Get(LibraryFile.MonMagicEx26); frame = 90 + slowTick % 24; break;
            case ExteriorEffect.E_BlueEyeRing: lib = LibraryCache.Get(LibraryFile.MonMagicEx26); frame = 220 + slowTick % 25; break;
            case ExteriorEffect.E_GreenSpiralRing: lib = LibraryCache.Get(LibraryFile.MonMagicEx26); frame = 330 + slowTick % 20; break;
            case ExteriorEffect.E_Fireworks: lib = LibraryCache.Get(LibraryFile.MonMagicEx26); frame = 360 + slowTick % 10; break;
            case ExteriorEffect.S_WarThurible: DrawThurible(LibraryFile.EquipEffect_Part, 900, 1000, tick, dir, drawX, drawY, behind); return;
            case ExteriorEffect.S_PenanceThurible: DrawThurible(LibraryFile.EquipEffect_Part, 1100, 1200, tick, dir, drawX, drawY, behind); return;
            case ExteriorEffect.S_CensorshipThurible: DrawThurible(LibraryFile.EquipEffect_Part, 1300, 1400, tick, dir, drawX, drawY, behind); return;
            case ExteriorEffect.S_PetrichorThurible: DrawThurible(LibraryFile.EquipEffect_Part, 1500, 1600, tick, dir, drawX, drawY, behind); return;
            default: return;
        }
        if (lib == null || frame < 0 || frame >= lib.Images.Length) return;
        // 旧端 ExteriorEffectManager 使用 ImageType.Image；外观素材的黑色
        // 像素可能是合法细节，不能套用技能特效的黑色透明键。
        DrawExteriorBlendLayer(lib, frame, alpha, behind, drawX, drawY);
    }

    private void DrawThurible(LibraryFile file, int first, int second, int tick, int dir,
        float drawX, float drawY, bool behind)
    {
        var lib = LibraryCache.Get(file);
        if (lib == null) return;
        int frame = tick % 4 + dir * 10;
        // 旧端第一层是 Draw(Image)，第二层才是 DrawBlend(Image)。
        DrawExteriorBlendLayer(lib, first + frame, 1f, behind, drawX, drawY, false);
        DrawExteriorBlendLayer(lib, second + frame, 1f, behind, drawX, drawY, true);
    }

    private void DrawExteriorBlendLayer(ZlLibrary lib, int frame, float alpha, bool behind,
        float offsetX = 0f, float offsetY = 0f, bool blend = true)
    {
        BlendImageLayerNode layer = null;
        foreach (var candidate in _exteriorBlendLayers)
        {
            if (!candidate.Visible) { layer = candidate; break; }
        }
        if (layer == null)
        {
            layer = new BlendImageLayerNode();
            _exteriorBlendLayers.Add(layer);
            AddChild(layer);
        }
        layer.Configure(lib, frame, new Color(1f, 1f, 1f, alpha), behind ? -1 : 1, offsetX, offsetY, blend);
    }

    private bool DrawExteriorEffectBehind(MirDirection direction, ExteriorEffect effect)
    {
        if (effect is ExteriorEffect.E_BlueEyeRing or ExteriorEffect.E_RedEyeRing
            or ExteriorEffect.E_GreenSpiralRing)
            return true;
        if (effect is ExteriorEffect.A_BlueAura or ExteriorEffect.A_FlameAura
            or ExteriorEffect.A_WhiteAura or ExteriorEffect.A_FlameAura2)
            return false;
        if (effect is ExteriorEffect.W_ChaoticHeavenBlade or ExteriorEffect.W_JanitorsScimitar
            or ExteriorEffect.W_JanitorsDualBlade)
            return direction is MirDirection.Up or MirDirection.UpLeft or MirDirection.Left or MirDirection.DownLeft;
        if (effect is ExteriorEffect.S_WarThurible or ExteriorEffect.S_PenanceThurible
            or ExteriorEffect.S_CensorshipThurible or ExteriorEffect.S_PetrichorThurible)
            return direction is MirDirection.UpRight or MirDirection.Right or MirDirection.DownRight;
        return direction is MirDirection.DownRight or MirDirection.Down or MirDirection.DownLeft;
    }

    private void DetermineExteriorOffset(ExteriorEffect effect, out float x, out float y)
    {
        x = 0f; y = 0f;
        if (Horse == HorseType.None) return;
        if (effect is ExteriorEffect.A_WhiteAura or ExteriorEffect.A_BlueAura
            or ExteriorEffect.A_FlameAura or ExteriorEffect.A_FlameAura2)
        {
            if (Direction is MirDirection.UpRight or MirDirection.Right or MirDirection.DownRight) x = 7f;
            else if (Direction is MirDirection.DownLeft or MirDirection.Left or MirDirection.UpLeft) x = -8f;
            y = -25f;
            return;
        }
        if (effect is ExteriorEffect.A_GreenWings or ExteriorEffect.A_BlueWings
            or ExteriorEffect.A_FlameWings or ExteriorEffect.A_RedSinWings
            or ExteriorEffect.A_DiamondFireWings or ExteriorEffect.A_PurpleTentacles2
            or ExteriorEffect.A_PhoenixWings or ExteriorEffect.A_IceKingWings
            or ExteriorEffect.A_BlueButterflyWings)
        {
            float movement = Animation == MirAnimation.HorseWalking ? 4f
                : Animation == MirAnimation.HorseRunning ? 8f : 0f;
            if (Direction is MirDirection.UpRight or MirDirection.Right or MirDirection.DownRight) x = 7f + movement;
            else if (Direction == MirDirection.DownLeft) x = -5f - movement;
            else if (Direction is MirDirection.Left or MirDirection.UpLeft) x = -8f - movement;
            y = Direction is MirDirection.Down or MirDirection.DownLeft or MirDirection.DownRight ? -16f : -30f;
        }
    }

    private void DrawShadow()
    {
        bool horse = Animation is MirAnimation.HorseStanding or MirAnimation.HorseWalking
            or MirAnimation.HorseRunning or MirAnimation.HorseStruck;
        bool resourceShadow = false;
        if (horse)
        {
            // 原版 DrawShadow 对应 DrawBody 的 HorseShape 分支：普通/铁/银/金/暗马
            // 的影子仍来自基础 HorseLibrary + HorseFrame；皇家和蓝龙才使用
            // 各自外观库的 DrawFrame。此前 Godot 把外观库统一当作影子库，导致
            // 坐骑影子形状、方向和装备层不一致。
            var shadowLibrary = HorseShape is >= 6 and <= 7 ? _horseLib : _horseShadowLib;
            int shadowFrame = HorseShape is >= 6 and <= 7
                ? DrawFrame
                : DrawFrame + ((int)Horse - 1) * 5000;
            resourceShadow = DrawResourceShadow(shadowLibrary, shadowFrame);
        }
        // 原版普通玩家使用 DrawShadow2：当前人物帧的轮廓做斜切压扁，
        // 而不是直接把 Shadow 通道矩形贴在节点左上角。坐骑则保留
        // HorseLibrary 的专用 Shadow 通道。
        if (!horse)
            resourceShadow = DrawPlayerSilhouetteShadow();
        if (!resourceShadow && !horse)
            resourceShadow = DrawResourceShadow(_bodyLib, ArmourFrame);
        // 原版没有通用几何椭圆兜底：坐骑只使用专用 Shadow 通道，
        // 普通玩家只使用 DrawShadow2/身体资源 Shadow。资源缺失时保持无影，
        // 不能制造与对象脚底无关的统一小圆盘。
    }

    private bool DrawResourceShadow(ZlLibrary lib, int frame)
    {
        if (lib == null || frame < 0 || frame >= lib.Images.Length) return false;
        var img = lib.Images[frame];
        if (img == null) return false;
        var texture = lib.GetShadowTexture(frame);
        if (RenderPrimitives.IsUsableResourceShadow(texture, img.ShadowWidth, img.ShadowHeight))
        {
            DrawTextureRectRegion(texture,
                new Rect2(img.ShadowOffSetX, img.ShadowOffSetY, img.ShadowWidth, img.ShadowHeight),
                new Rect2(0, 0, img.ShadowWidth, img.ShadowHeight),
                new Color(0f, 0f, 0f, 0.5f));
            return true;
        }

        // 与原版 MirLibrary.Draw 保持一致：Shadow payload 不可用时按
        // ShadowType 从主体帧生成投影，而不是把该帧静默丢弃。
        return RenderPrimitives.DrawShadowTypeFallback(this, lib.GetImageTexture(frame), img, 0.5f);
    }

    private bool DrawSilhouetteShadow(ZlLibrary lib, int frame, float alpha = 0.5f, ZlImage anchor = null)
    {
        if (lib == null || frame < 0 || frame >= lib.Images.Length) return false;
        var img = lib.Images[frame];
        var texture = lib.GetImageTexture(frame);
        return RenderPrimitives.DrawSilhouetteShadow(this, texture, img, alpha, anchorImage: anchor);
    }

    private bool DrawPlayerSilhouetteShadow()
    {
        bool hideWeapon = CostumeShapeHideWeapon.Contains(CostumeShape);
        if (_bodyLib == null || ArmourFrame < 0 || ArmourFrame >= _bodyLib.Images.Length)
            return false;
        var anchor = _bodyLib.Images[ArmourFrame];
        if (anchor == null) return false;

        bool drawn = DrawSilhouetteShadow(_bodyLib, ArmourFrame, 0.5f, anchor);

        // DrawShadow2 在原版先把所有可见装备层合成 scratch，再统一投影；
        // 这里逐层投影到同一脚底锚点，保留武器、盾牌、头盔/头发的真实轮廓。
        if (!hideWeapon && DrawWeapon)
        {
            if (Direction is MirDirection.Up or MirDirection.DownLeft or MirDirection.Left or MirDirection.UpLeft)
                drawn |= DrawSilhouetteShadow(_weaponLib2 ?? _weaponLib1, WeaponFrame, 0.42f, anchor);
            if (ShieldShape >= 0 && Direction is MirDirection.UpRight or MirDirection.Right or MirDirection.DownRight)
                drawn |= DrawSilhouetteShadow(_shieldLib, ShieldFrame, 0.42f, anchor);
        }

        if (!HideHead)
        {
            if (HelmetShape > 0)
                drawn |= DrawSilhouetteShadow(_helmetLib, HelmetFrame, 0.42f, anchor);
            else if (HairType > 0)
                drawn |= DrawSilhouetteShadow(_hairLib, HairFrame, 0.35f, anchor);
        }

        if (!hideWeapon && DrawWeapon &&
            Direction is MirDirection.UpRight or MirDirection.Right or MirDirection.DownRight or MirDirection.Down)
            drawn |= DrawSilhouetteShadow(_weaponLib1, WeaponFrame, 0.42f, anchor);

        return drawn;
    }

    private void DrawHorse(float px, float py, Color? tint = null)
    {
        if (_horseLib == null) return;
        int frame = DrawFrame + ((int)Horse - 1) * 5000;
        // 原版 HorseShape 4(蓝)、5(暗)、6(皇)、7(蓝龙) 使用外观库的
        // DrawFrame；只有基础/铁/银/金马使用 HorseFrame 的 5000 偏移。
        if (HorseShape is >= 4 and <= 7) frame = DrawFrame;
        DrawLayer(_horseLib, frame, px, py, tint);
        if (_horseEffectLib != null && HorseShape is 5 or 6)
            DrawExteriorBlendLayer(_horseEffectLib, DrawFrame, 1f, true);
    }

    private void DrawLayer(ZlLibrary lib, int frame, float px, float py, Color? tint = null, bool effectTexture = false)
    {
        if (lib == null) return;
        if (frame < 0 || frame >= lib.Images.Length) return;
        if (lib.Images[frame] == null) return;

        var texture = effectTexture ? lib.GetEffectTexture(frame) : lib.GetImageTexture(frame);
        if (texture == null) return;

        var img = lib.Images[frame];
        var dest = new Rect2(px + img.OffSetX, py + img.OffSetY, img.Width, img.Height);
        if (tint.HasValue)
            DrawTextureRectRegion(texture, dest, new Rect2(0, 0, img.Width, img.Height), tint.Value);
        else
            DrawTextureRectRegion(texture, dest, new Rect2(0, 0, img.Width, img.Height));
    }

    private void DrawOverlay(ZlLibrary lib, int frame, float px, float py, Color tint)
    {
        if (lib == null || frame < 0 || frame >= lib.Images.Length) return;
        var img = lib.Images[frame];
        if (img == null || img.OverlayWidth <= 0 || img.OverlayHeight <= 0) return;
        var texture = lib.GetOverlayTexture(frame);
        if (texture == null) return;
        DrawTextureRectRegion(texture,
            new Rect2(px + img.OffSetX, py + img.OffSetY, img.OverlayWidth, img.OverlayHeight),
            new Rect2(0, 0, img.OverlayWidth, img.OverlayHeight), tint);
    }

    // 库选择 (UpdateLibraries 移植)
    private void RefreshLibraries()
    {
        _weaponLib2 = null;
        _horseLib = null;
        _horseShadowLib = null;
        _horseEffectLib = null;

        bool isFemale = Gender == MirGender.Female;
        bool isAssassin = Class == MirClass.Assassin;
        int femaleOff = isFemale ? 5000 : 0;
        int assassinOff = isAssassin ? 50000 : 0;

        // 身体 (ArmourList: ArmourShape/11)
        LibraryFile bodyFile = LibraryFile.M_Hum;
        if (isAssassin)
            bodyFile = isFemale ? LibraryFile.WM_HumA : LibraryFile.M_HumA;
        else
            bodyFile = isFemale ? LibraryFile.WM_Hum : LibraryFile.M_Hum;

        // 查 ArmourList 字典 (30 项: 键 ArmourShape/11 + 偏移)
        var armourKey = ArmourShape / 11 + femaleOff + (isAssassin ? assassinOff : 0);
        if (TryArmour(armourKey, out var armourFile))
            bodyFile = armourFile;

        // 时装优先
        if (CostumeShape >= 0)
        {
            var costumeKey = CostumeShape / 10 + femaleOff + (isAssassin ? assassinOff : 0);
            if (TryCostume(costumeKey, out var costumeFile))
                bodyFile = costumeFile;
            else
                bodyFile = isAssassin ? (isFemale ? LibraryFile.WM_HumA : LibraryFile.M_HumA)
                                      : (isFemale ? LibraryFile.WM_Hum : LibraryFile.M_Hum);
        }

        _bodyLib = LibraryCache.Get(bodyFile);

        // 发型
        _hairLib = LibraryCache.Get(isAssassin ? (isFemale ? LibraryFile.WM_HairA : LibraryFile.M_HairA)
                                                : (isFemale ? LibraryFile.WM_Hair : LibraryFile.M_Hair));

        // 头盔 (HelmetList: (HelmetShape-1)/10)
        if (HelmetShape > 0)
        {
            var helmetKey = (HelmetShape - 1) / 10 + femaleOff + (isAssassin ? assassinOff : 0);
            if (TryHelmet(helmetKey, out var helmetFile))
                _helmetLib = LibraryCache.Get(helmetFile);
            else
                _helmetLib = null;
        }
        else _helmetLib = null;

        // 武器 (WeaponList: LibraryWeaponShape/10)
        var weaponKey = LibraryWeaponShape / 10 + femaleOff;
        if (TryWeapon(weaponKey, out var weaponFile))
            _weaponLib1 = LibraryCache.Get(weaponFile);
        else
            _weaponLib1 = LibraryCache.Get(LibraryFile.M_Weapon1);

        if (LibraryWeaponShape >= 1200 && LibraryWeaponShape != 1263)
        {
            var rightKey = LibraryWeaponShape / 10 + femaleOff + 50;
            if (TryWeapon(rightKey, out var rightFile))
                _weaponLib2 = LibraryCache.Get(rightFile);
        }

        // 盾 (ShieldList: ShieldShape/10)
        _shieldLib = null;
        if (ShieldShape >= 0)
        {
            var shieldKey = ShieldShape / 10 + femaleOff;
            if (TryShield(shieldKey, out var shieldFile))
                _shieldLib = LibraryCache.Get(shieldFile);
        }

        if (Horse != HorseType.None)
            _horseShadowLib = LibraryCache.Get(LibraryFile.Horse);

        switch (HorseShape)
        {
            case 1: _horseLib = LibraryCache.Get(LibraryFile.HorseIron); break;
            case 2: _horseLib = LibraryCache.Get(LibraryFile.HorseSilver); break;
            case 3: _horseLib = LibraryCache.Get(LibraryFile.HorseGold); break;
            case 4: _horseLib = LibraryCache.Get(LibraryFile.HorseBlue); break;
            case 5:
                _horseLib = LibraryCache.Get(LibraryFile.HorseDark);
                _horseEffectLib = LibraryCache.Get(LibraryFile.HorseDarkEffect);
                break;
            case 6:
                _horseLib = LibraryCache.Get(LibraryFile.HorseRoyal);
                _horseEffectLib = LibraryCache.Get(LibraryFile.HorseRoyalEffect);
                break;
            case 7:
                _horseLib = LibraryCache.Get(LibraryFile.HorseBlueDragon);
                _horseEffectLib = LibraryCache.Get(LibraryFile.HorseBlueDragonEffect);
                break;
            default: _horseLib = LibraryCache.Get(LibraryFile.Horse); break;
        }
        if (Horse == HorseType.None) _horseLib = null;
    }

    // ---- 装备库字典 (4.2 节) ----
    private static bool TryArmour(int key, out LibraryFile file)
    {
        switch (key)
        {
            case 0: file = LibraryFile.M_Hum; return true;
            case 1: file = LibraryFile.M_HumEx1; return true;
            case 2: file = LibraryFile.M_HumEx2; return true;
            case 3: file = LibraryFile.M_HumEx3; return true;
            case 4: file = LibraryFile.M_HumEx4; return true;
            case 10: file = LibraryFile.M_HumEx10; return true;
            case 11: file = LibraryFile.M_HumEx11; return true;
            case 12: file = LibraryFile.M_HumEx12; return true;
            case 13: file = LibraryFile.M_HumEx13; return true;
            case 20: file = LibraryFile.M_HumCx1; return true;
            case 5000: file = LibraryFile.WM_Hum; return true;
            case 5001: file = LibraryFile.WM_HumEx1; return true;
            case 5002: file = LibraryFile.WM_HumEx2; return true;
            case 5003: file = LibraryFile.WM_HumEx3; return true;
            case 5004: file = LibraryFile.WM_HumEx4; return true;
            case 5010: file = LibraryFile.WM_HumEx10; return true;
            case 5011: file = LibraryFile.WM_HumEx11; return true;
            case 5012: file = LibraryFile.WM_HumEx12; return true;
            case 5013: file = LibraryFile.WM_HumEx13; return true;
            case 5020: file = LibraryFile.WM_HumCx1; return true;
            case 50000: file = LibraryFile.M_HumA; return true;
            case 50001: file = LibraryFile.M_HumAEx1; return true;
            case 50002: file = LibraryFile.M_HumAEx2; return true;
            case 50003: file = LibraryFile.M_HumAEx3; return true;
            case 50020: file = LibraryFile.M_HumACx1; return true;
            case 55000: file = LibraryFile.WM_HumA; return true;
            case 55001: file = LibraryFile.WM_HumAEx1; return true;
            case 55002: file = LibraryFile.WM_HumAEx2; return true;
            case 55003: file = LibraryFile.WM_HumAEx3; return true;
            case 55020: file = LibraryFile.WM_HumACx1; return true;
            default: file = LibraryFile.M_Hum; return false;
        }
    }
    private static bool TryCostume(int key, out LibraryFile file)
    {
        switch (key)
        {
            case 0: file = LibraryFile.M_Costume; return true;
            case 1: file = LibraryFile.M_CostumeEx1; return true;
            case 5000: file = LibraryFile.WM_Costume; return true;
            case 5001: file = LibraryFile.WM_CostumeEx1; return true;
            case 50000: file = LibraryFile.M_CostumeA; return true;
            case 55000: file = LibraryFile.WM_CostumeA; return true;
            default: file = LibraryFile.M_Hum; return false;
        }
    }
    private static bool TryHelmet(int key, out LibraryFile file)
    {
        switch (key)
        {
            case 0: file = LibraryFile.M_Helmet1; return true;
            case 1: file = LibraryFile.M_Helmet2; return true;
            case 2: file = LibraryFile.M_Helmet3; return true;
            case 3: file = LibraryFile.M_Helmet4; return true;
            case 4: file = LibraryFile.M_Helmet5; return true;
            case 10: file = LibraryFile.M_Helmet11; return true;
            case 11: file = LibraryFile.M_Helmet12; return true;
            case 12: file = LibraryFile.M_Helmet13; return true;
            case 13: file = LibraryFile.M_Helmet14; return true;
            case 20: file = LibraryFile.M_HelmetCx1; return true;
            case 5000: file = LibraryFile.WM_Helmet1; return true;
            case 5001: file = LibraryFile.WM_Helmet2; return true;
            case 5002: file = LibraryFile.WM_Helmet3; return true;
            case 5003: file = LibraryFile.WM_Helmet4; return true;
            case 5004: file = LibraryFile.WM_Helmet5; return true;
            case 5010: file = LibraryFile.WM_Helmet11; return true;
            case 5011: file = LibraryFile.WM_Helmet12; return true;
            case 5012: file = LibraryFile.WM_Helmet13; return true;
            case 5013: file = LibraryFile.WM_Helmet14; return true;
            case 5020: file = LibraryFile.WM_HelmetCx1; return true;
            case 50000: file = LibraryFile.M_HelmetA1; return true;
            case 50001: file = LibraryFile.M_HelmetA2; return true;
            case 50002: file = LibraryFile.M_HelmetA3; return true;
            case 50003: file = LibraryFile.M_HelmetA4; return true;
            case 50020: file = LibraryFile.M_HelmetACx1; return true;
            case 55000: file = LibraryFile.WM_HelmetA1; return true;
            case 55001: file = LibraryFile.WM_HelmetA2; return true;
            case 55002: file = LibraryFile.WM_HelmetA3; return true;
            case 55003: file = LibraryFile.WM_HelmetA4; return true;
            case 55020: file = LibraryFile.WM_HelmetACx1; return true;
            default: file = LibraryFile.None; return false;
        }
    }
    private static bool TryWeapon(int key, out LibraryFile file)
    {
        // WeaponList (52 项): 0-15 基础 + 女版 + 右手版
        switch (key)
        {
            case 0: file = LibraryFile.M_Weapon1; return true;
            case 1: file = LibraryFile.M_Weapon2; return true;
            case 2: file = LibraryFile.M_Weapon3; return true;
            case 3: file = LibraryFile.M_Weapon4; return true;
            case 4: file = LibraryFile.M_Weapon5; return true;
            case 5: file = LibraryFile.M_Weapon6; return true;
            case 6: file = LibraryFile.M_Weapon7; return true;
            case 9: file = LibraryFile.M_Weapon10; return true;
            case 10: file = LibraryFile.M_Weapon11; return true;
            case 11: file = LibraryFile.M_Weapon12; return true;
            case 12: file = LibraryFile.M_Weapon13; return true;
            case 13: file = LibraryFile.M_Weapon14; return true;
            case 14: file = LibraryFile.M_Weapon15; return true;
            case 15: file = LibraryFile.M_Weapon16; return true;
            case 110: file = LibraryFile.M_WeaponAOH1; return true;
            case 111: file = LibraryFile.M_WeaponAOH2; return true;
            case 112: file = LibraryFile.M_WeaponAOH3; return true;
            case 113: file = LibraryFile.M_WeaponAOH4; return true;
            case 114: file = LibraryFile.M_WeaponAOH5; return true;
            case 115: file = LibraryFile.M_WeaponAOH6; return true;
            case 120: file = LibraryFile.M_WeaponADL1; return true;
            case 121: file = LibraryFile.M_WeaponADL2; return true;
            case 125: file = LibraryFile.M_WeaponADL6; return true;
            case 170: file = LibraryFile.M_WeaponADR1; return true;
            case 171: file = LibraryFile.M_WeaponADR2; return true;
            case 175: file = LibraryFile.M_WeaponADR6; return true;
            case 5000: file = LibraryFile.WM_Weapon1; return true;
            case 5001: file = LibraryFile.WM_Weapon2; return true;
            case 5002: file = LibraryFile.WM_Weapon3; return true;
            case 5003: file = LibraryFile.WM_Weapon4; return true;
            case 5004: file = LibraryFile.WM_Weapon5; return true;
            case 5005: file = LibraryFile.WM_Weapon6; return true;
            case 5006: file = LibraryFile.WM_Weapon7; return true;
            case 5009: file = LibraryFile.WM_Weapon10; return true;
            case 5010: file = LibraryFile.WM_Weapon11; return true;
            case 5011: file = LibraryFile.WM_Weapon12; return true;
            case 5012: file = LibraryFile.WM_Weapon13; return true;
            case 5013: file = LibraryFile.WM_Weapon14; return true;
            case 5014: file = LibraryFile.WM_Weapon15; return true;
            case 5015: file = LibraryFile.WM_Weapon16; return true;
            case 5110: file = LibraryFile.WM_WeaponAOH1; return true;
            case 5111: file = LibraryFile.WM_WeaponAOH2; return true;
            case 5112: file = LibraryFile.WM_WeaponAOH3; return true;
            case 5113: file = LibraryFile.WM_WeaponAOH4; return true;
            case 5114: file = LibraryFile.WM_WeaponAOH5; return true;
            case 5115: file = LibraryFile.WM_WeaponAOH6; return true;
            case 5120: file = LibraryFile.WM_WeaponADL1; return true;
            case 5121: file = LibraryFile.WM_WeaponADL2; return true;
            case 5125: file = LibraryFile.WM_WeaponADL6; return true;
            case 5170: file = LibraryFile.WM_WeaponADR1; return true;
            case 5171: file = LibraryFile.WM_WeaponADR2; return true;
            case 5175: file = LibraryFile.WM_WeaponADR6; return true;
            default: file = LibraryFile.M_Weapon1; return false;
        }
    }
    private static bool TryShield(int key, out LibraryFile file)
    {
        switch (key)
        {
            case 0: file = LibraryFile.M_Shield1; return true;
            case 1: file = LibraryFile.M_Shield2; return true;
            case 5000: file = LibraryFile.WM_Shield1; return true;
            case 5001: file = LibraryFile.WM_Shield2; return true;
            default: file = LibraryFile.None; return false;
        }
    }
}
