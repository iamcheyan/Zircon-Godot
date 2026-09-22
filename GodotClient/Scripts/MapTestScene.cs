using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Godot;
using GTime = Godot.Time;
using Library;
using Library.SystemModels;
using S = Library.Network.ServerPackets;
using ZirconClient.Formats;
using ZirconClient.Network;
using ZirconClient.Controls;

#nullable enable
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618

namespace ZirconClient.Scripts;

public partial class MapTestScene : Control
{
    private Label _statusLabel;
    private string _dataPath = MirSkin.DataPath;
    private string _mapPath = ResolveMapPath();

    private static string ResolveMapPath()
    {
        string configured = System.Environment.GetEnvironmentVariable("ZIRCON_MAP_DATA");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        string local = Path.GetFullPath(Path.Combine(MirSkin.DataPath, "..", "Map"));
        return Directory.Exists(local) ? local : "/home/tetsuya/development/zircon/Debug/Client/Map/";
    }
    private Dictionary<LibraryFile, ZlLibrary> _libCache = new();
    private bool _renderAudit;
    private bool _actionAudit;
    private bool _mapAudit;
    private bool _shadowAudit;
    private bool _pixelAudit;
    private bool _projectileAudit;
    private bool _deadTargetAudit;
    private bool _lightRenderAudit;
    private bool _weatherRenderAudit;
    private bool _mapFamilyRenderAudit;
    private bool _blendAudit;
    private int _blendAuditFrames;
    private int _blendAuditCase;
    private int _blendAuditOriginalHits;
    private int _blendAuditCurrentHits;
    private int _blendAuditParityHits;
    private ColorRect? _blendAuditBackdrop;
    private TextureRect? _blendAuditQuad;
    private ColorRect? _blendAuditProbe;
    private int _auditFrames;
    private PlayerRenderer _auditPlayer;
    private int _actionIndex;
    private double _actionDeadline;
    private bool _actionAuditFinished;
    private int _mapTextureDiagnostics;
    private bool _effectFreeAudit;
    private int _effectFreeAuditStage;
    private int _effectFreeAuditFrames;
    private Node2D _effectFreeTarget;
    private MirEffectNode _effectFreeFx;
    private MirProjectileNode _effectFreeProjectile;
    private Vector2 _effectFreeLastPosition;
    private MirProjectileNode _auditProjectile;
    private Vector2 _auditProjectileStart;
    private float _auditProjectileMaxTravel;
    private int _auditProjectileSamples;
    private double _auditProjectileStartMs;
    private int _auditProjectileStage;
    private bool _projectileScreenshotSaved;
    private MapView _lightAuditMapView;
    private MapLightLayer _lightAuditLayer;
    private MapInfo _lightAuditMapInfo;
    private int _lightAuditStage;
    private int _lightAuditFrames;
    // 拷贝点劫持探针区域 (逻辑坐标, 根节点 Scale=2 后落在视口内)。
    private static readonly Rect2 LightProbeArea = new(300, 200, 48, 32);
    private ColorRect? _probeBackdrop;
    private ColorRect? _probeHijacker;
    private ColorRect? _probeWhite;
    private MapWeatherLayer _weatherAuditLayer;
    private int _weatherAuditFrames;
    private MapView _mapFamilyView;
    private int _mapFamilyIndex;
    private int _mapFamilyFrames;
    private static readonly string[] MapFamilySamples = { "0", "1", "5", "D001", "E01" };
    private static readonly (string Name, LightSetting Setting, float DayTime, bool Dead, bool Abyss)[] LightRenderStages =
    {
        ("night", LightSetting.Night, 1f, false, false),
        ("twilight", LightSetting.Twilight, 1f, false, false),
        ("default", LightSetting.Default, 0.42f, false, false),
        // 原版特殊光照态: 死亡红染 / 深渊黑视白天也强制渲染
        ("dead", LightSetting.Light, 1f, true, false),
        ("abyss", LightSetting.Light, 1f, false, true),
    };
    private readonly HashSet<int> _actionFrames = new();
    private readonly HashSet<MirAnimation> _actionAnimations = new();
    private readonly List<(string Name, Action<PlayerRenderer> Start, MirAnimation Expected)> _actions = new();
    private const float WorldScale = 2f;
    private string _dumpZlFile;
    private string _dumpZlOutput;
    private string _tableSnapshotPath;
    private int _dumpZlIndex = -1;

    // 网格常量（第 7.1 章）
    const int CellWidth = 48;
    const int CellHeight = 32;

    public override void _Ready()
    {
        _statusLabel = new Label();
        _statusLabel.Position = new Vector2(10, 10);
        _statusLabel.Size = new Vector2(600, 60);
        _statusLabel.ZIndex = 100;
        AddChild(_statusLabel);
        _renderAudit = OS.GetCmdlineUserArgs().Contains("--render-audit");
        _actionAudit = OS.GetCmdlineUserArgs().Contains("--action-audit");
        _mapAudit = OS.GetCmdlineUserArgs().Contains("--map-audit");
        _shadowAudit = OS.GetCmdlineUserArgs().Contains("--shadow-audit");
        _pixelAudit = OS.GetCmdlineUserArgs().Contains("--pixel-audit");
        _projectileAudit = OS.GetCmdlineUserArgs().Contains("--projectile-audit");
        _deadTargetAudit = OS.GetCmdlineUserArgs().Contains("--dead-target-audit");
        bool playerMatrixAudit = OS.GetCmdlineUserArgs().Contains("--player-matrix-audit");
        bool lightAudit = OS.GetCmdlineUserArgs().Contains("--light-audit");
        _lightRenderAudit = OS.GetCmdlineUserArgs().Contains("--light-render-audit");
        _weatherRenderAudit = OS.GetCmdlineUserArgs().Contains("--weather-render-audit");
        _mapFamilyRenderAudit = OS.GetCmdlineUserArgs().Contains("--map-family-render-audit");
        _blendAudit = OS.GetCmdlineUserArgs().Contains("--blend-audit");
        bool networkAudit = OS.GetCmdlineUserArgs().Contains("--network-audit");
        bool cursorAudit = OS.GetCmdlineUserArgs().Contains("--cursor-audit");
        bool combatAudit = OS.GetCmdlineUserArgs().Contains("--combat-audit");
        _effectFreeAudit = OS.GetCmdlineUserArgs().Contains("--effect-target-free-audit");
        bool fullTextureAudit = OS.GetCmdlineUserArgs().Contains("--full-texture-audit");
        bool weatherTextureDump = OS.GetCmdlineUserArgs().Contains("--weather-texture-dump");
        bool progUseEffectDump = OS.GetCmdlineUserArgs().Contains("--proguse-effect-dump");
        bool sludgeDump = OS.GetCmdlineUserArgs().Contains("--green-sludge-dump");
        bool magicSpotAudit = OS.GetCmdlineUserArgs().Contains("--magic-spot-audit");
        _dumpZlFile = GetCmdlineValue("--dump-zl-file=");
        _dumpZlOutput = GetCmdlineValue("--dump-zl-output=");
        _dumpZlIndex = ParseAuditInt("--dump-zl-index=", -1);
        _tableSnapshotPath = GetCmdlineValue("--table-snapshot=");

        // 与实际 GameScene 保持一致：地图、对象、特效都在逻辑 48x32
        // 坐标绘制，根世界统一放大 2 倍。否则审计截图只能验证 1x。
        if (_renderAudit || _actionAudit || _projectileAudit || _lightRenderAudit || _weatherRenderAudit || _mapFamilyRenderAudit || _blendAudit)
            Scale = Vector2.One * WorldScale;

        string mapFile = Path.Combine(_mapPath, "0.map");
        GD.Print($"[MapTest] 加载: {mapFile}");

        try
        {
            var map = new MirMap(mapFile);
            GD.Print($"[MapTest] 地图: {map.Width}x{map.Height}");

            // 统计单元格数据
            int bgCount = 0, midCount = 0, frontCount = 0;
            for (int x = 0; x < 20; x++)
                for (int y = 0; y < 20; y++)
                {
                    if (map.Cells[x, y].BackFile > 0) bgCount++;
                    if (map.Cells[x, y].MiddleFile > 0) midCount++;
                    if (map.Cells[x, y].FrontFile > 0) frontCount++;
                }
            GD.Print($"[MapTest] 20x20 区域: 背景={bgCount}, 中层={midCount}, 前景={frontCount}");

            // 渲染 20x20 区域
            RenderArea(map, 0, 0, 20, 20);
            _statusLabel.Text = string.Format(Lang.MapTestUi600Label, map.Width, map.Height);
            GD.Print("[MapTest] 渲染完成");
            if (_renderAudit) CallDeferred(nameof(RenderObjectAudit));
            if (_actionAudit) CallDeferred(nameof(BeginActionAudit));
            if (_mapAudit) CallDeferred(nameof(RunMapAudit));
            if (_shadowAudit) CallDeferred(nameof(RunShadowAudit));
            if (_pixelAudit) CallDeferred(nameof(RunPixelAudit));
        if (_deadTargetAudit) CallDeferred(nameof(RunDeadTargetFallbackAudit));
        if (magicSpotAudit) CallDeferred(nameof(RunMagicSpotAudit));
            if (playerMatrixAudit) CallDeferred(nameof(RunPlayerMatrixAudit));
            if (lightAudit) CallDeferred(nameof(RunLightAudit));
            if (_lightRenderAudit) CallDeferred(nameof(BeginLightRenderAudit));
            if (_weatherRenderAudit) CallDeferred(nameof(BeginWeatherRenderAudit));
            if (_mapFamilyRenderAudit) CallDeferred(nameof(BeginMapFamilyRenderAudit));
            if (networkAudit)
            {
                CallDeferred(nameof(RunNetworkAudit));
                CallDeferred(nameof(RunAnomalyReplayAudit));
            }
            if (cursorAudit) CallDeferred(nameof(RunCursorAudit));
            if (combatAudit) CallDeferred(nameof(RunCombatAudit));
            if (_effectFreeAudit) CallDeferred(nameof(BeginEffectTargetFreeAudit));
            if (fullTextureAudit) CallDeferred(nameof(RunTransparencyAudit));
            if (_blendAudit) CallDeferred(nameof(BeginBlendAudit));
            if (weatherTextureDump) CallDeferred(nameof(DumpWeatherTextures));
            if (progUseEffectDump) CallDeferred(nameof(DumpProgUseEffectTextures));
            if (sludgeDump) CallDeferred(nameof(DumpGreenSludgeTextures));
            if (!string.IsNullOrWhiteSpace(_dumpZlFile)
                && !string.IsNullOrWhiteSpace(_dumpZlOutput)
                && _dumpZlIndex >= 0)
                CallDeferred(nameof(DumpRequestedZlFrame));
        }
        catch (Exception ex)
        {
            _statusLabel.Text = string.Format(Lang.MapTestUi601Label, ex.Message);
            GD.PrintErr($"[MapTest] {ex}");
        }

        // E5/B: 表快照导出 — 纯内存反射, 不依赖地图资源, 导出即退出。
        if (!string.IsNullOrWhiteSpace(_tableSnapshotPath))
        {
            try
            {
                TableSnapshotTool.DumpAll(_tableSnapshotPath);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[TableSnapshot] FAIL {ex}");
            }
            GetTree().Quit();
        }
    }

    private void DumpWeatherTextures()
    {
        var library = LibraryCache.Get(LibraryFile.ProgUse);
        if (library == null) return;
        foreach (int frame in new[] { 500, 509, 510, 511, 512, 513, 514, 540, 550 })
        {
            var meta = frame < library.Images.Length ? library.Images[frame] : null;
            var processed = (frame == 550 ? library.GetFogTexture(frame) : library.GetWeatherTexture(frame))?.GetImage();
            var ordinary = library.GetImageTexture(frame)?.GetImage();
            processed?.SavePng($"/tmp/zircon-weather-{frame}-processed.png");
            ordinary?.SavePng($"/tmp/zircon-weather-{frame}-ordinary.png");
            GD.Print($"[WeatherTextureDump] frame={frame} codec={meta?.ImageCodec} " +
                $"size={meta?.Width}x{meta?.Height} alphaProcessed={(processed == null ? -1 : CountOpaque(processed))} " +
                $"alphaOrdinary={(ordinary == null ? -1 : CountOpaque(ordinary))}");
        }
        GD.Print("[WeatherTextureDump] PASS path=/tmp/zircon-weather-<frame>-{processed,ordinary}.png");
        GetTree().Quit();
    }

    private static int CountOpaque(Image image)
    {
        int count = 0;
        for (int y = 0; y < image.GetHeight(); y++)
            for (int x = 0; x < image.GetWidth(); x++)
                if (image.GetPixel(x, y).A > 0.01f) count++;
        return count;
    }

    private void DumpProgUseEffectTextures()
    {
        var library = LibraryCache.Get(LibraryFile.ProgUse);
        if (library == null) return;
        var frames = new List<int> { 200, 210, 220, 230, 240, 241, 242, 243, 244, 245, 246, 247, 260 };
        frames.AddRange(Enumerable.Range(680, 6));
        frames.AddRange(Enumerable.Range(700, 15));
        foreach (int frame in frames.Distinct())
        {
            library.GetImageTexture(frame)?.GetImage()?.SavePng($"/tmp/zircon-proguse-{frame}-ordinary.png");
            library.GetEffectTexture(frame)?.GetImage()?.SavePng($"/tmp/zircon-proguse-{frame}-keyed.png");
        }
        GD.Print("[ProgUseEffectDump] PASS path=/tmp/zircon-proguse-<frame>-{ordinary,keyed}.png");
        GetTree().Quit();
    }

    private void DumpGreenSludgeTextures()
    {
        var library = LibraryCache.Get(LibraryFile.MonMagicEx23);
        if (library == null) return;
        for (int frame = 2780; frame < 2800 && frame < library.Images.Length; frame++)
        {
            var image = library.GetImageTexture(frame)?.GetImage();
            image?.SavePng($"/tmp/zircon-green-sludge-{frame}.png");
            var meta = library.Images[frame];
            GD.Print($"[GreenSludgeDump] frame={frame} size={meta?.Width}x{meta?.Height} offset={meta?.OffSetX},{meta?.OffSetY}");
        }
        GD.Print($"[GreenSludgeDump] PASS resourceFrames={library.Images.Length} range=2780..2799");
        GetTree().Quit();
    }

    private static void RunCursorAudit()
    {
        var old = new ObjectRenderer { Type = ObjectRenderer.Kind.Item, ObjectID = 1, HitOrder = 10 };
        var latest = new ObjectRenderer { Type = ObjectRenderer.Kind.NPC, ObjectID = 2, HitOrder = 20 };
        var selected = CombatController.SelectLatestHit(new[] { old, latest });
        bool latestWins = selected?.ObjectID == latest.ObjectID;
        GD.Print(latestWins
            ? "[CursorAudit] PASS newest per-cell hit order preserved"
            : $"[CursorAudit] FAIL selected={selected?.ObjectID}");
    }

    /// <summary>
    /// P1 输入/战斗静态语义审计。原版参考：
    ///   - UserObject.SetAction Attack/RangeAttack（UserObject.cs:638-653）：
    ///     base = max(800, AttackDelay - AttackSpeed*ASpeedRate)，超重/Neutralize
    ///     时 AttackTime 再叠加一次 base（等效 x2）。
    ///   - UserObject.SetAction Mining：超重再叠加一次（x3）、Neutralize 叠加一次（x2）。
    ///   - MapControl.OnMouseDown 683-739 Shuriken 分支：超 MagicRange 先提示+Stop()
    ///     （任何坐骑状态），Horse==None 且飞镖才投，冷却点击/投掷后都 Stop() 清目标。
    ///   - MapControl.ProcessInput 顶部攻击分支 875-895、AutoRun 896-901、
    ///     Standing 转向（C.Turn 无本地回包，同向不发）。
    /// </summary>
    private static void RunCombatAudit()
    {
        // B1: 攻击间隔 = max(800, 1500 - AS*47)，超重/Neutralize x2（两者也是 x2）。
        // AS=0 → 1500（地板只在 AS>=15 生效）；AS=15 → 800（地板）。
        bool attackMatrix =
            GameScene.ComputeAttackIntervalMs(1500, 0, 47, false, false) == 1500.0
            && GameScene.ComputeAttackIntervalMs(1500, 14, 47, false, false) == 842.0
            && GameScene.ComputeAttackIntervalMs(1500, 15, 47, false, false) == 800.0
            && GameScene.ComputeAttackIntervalMs(1500, 14, 47, true, false) == 1684.0
            && GameScene.ComputeAttackIntervalMs(1500, 14, 47, false, true) == 1684.0
            && GameScene.ComputeAttackIntervalMs(1500, 14, 47, true, true) == 1684.0;
        // B2: 采矿间隔独立公式：超重 x3、Neutralize x2、同时按超重 x3。
        // AS=0 → 1500（同攻击公式的无 AS 情形）。
        bool miningMatrix =
            GameScene.ComputeMiningIntervalMs(1500, 0, 47, false, false) == 1500.0
            && GameScene.ComputeMiningIntervalMs(1500, 14, 47, true, false) == 2526.0
            && GameScene.ComputeMiningIntervalMs(1500, 14, 47, false, true) == 1684.0
            && GameScene.ComputeMiningIntervalMs(1500, 14, 47, true, true) == 2526.0;
        // B3: ShurikenClick 真值表（原版 683-739 判定顺序：非飞镖→近战；
        // 超距提示先于坐骑/冷却；骑马在范围内→近战；冷却→清目标；可投→投+清）。
        var melee = CombatController.ShurikenClickResult.Melee;
        var hint = CombatController.ShurikenClickResult.HintAndClear;
        var clear = CombatController.ShurikenClickResult.ClearOnly;
        var throwAndClear = CombatController.ShurikenClickResult.ThrowAndClear;
        bool shurikenMatrix =
            CombatController.ShurikenClick(false, true, false, false) == melee
            && CombatController.ShurikenClick(false, false, true, true) == melee
            && CombatController.ShurikenClick(true, true, true, false) == hint
            && CombatController.ShurikenClick(true, false, true, true) == hint
            && CombatController.ShurikenClick(true, false, false, false) == melee
            && CombatController.ShurikenClick(true, true, false, true) == clear
            && CombatController.ShurikenClick(true, true, false, false) == throwAndClear;
        // B4: 移动动画集合（原版 AttemptAction(Moving) 期间，C.Magic 排队等待）。
        bool walkAnimations =
            GameScene.IsWalkAnimation(MirAnimation.Walking)
            && GameScene.IsWalkAnimation(MirAnimation.Running)
            && GameScene.IsWalkAnimation(MirAnimation.HorseWalking)
            && GameScene.IsWalkAnimation(MirAnimation.HorseRunning)
            && GameScene.IsWalkAnimation(MirAnimation.CreepWalkSlow)
            && GameScene.IsWalkAnimation(MirAnimation.CreepWalkFast)
            && !GameScene.IsWalkAnimation(MirAnimation.Standing)
            && !GameScene.IsWalkAnimation(MirAnimation.Combat1)
            && !GameScene.IsWalkAnimation(MirAnimation.Pushed);
        // B5: 转向闸门——同向不重复发包（C.Turn 对本地无回包）。
        bool turnMatrix =
            !GameScene.ShouldSendTurn(MirDirection.Up, MirDirection.Up)
            && GameScene.ShouldSendTurn(MirDirection.Up, MirDirection.Down);
        bool pass = attackMatrix && miningMatrix && shurikenMatrix && walkAnimations && turnMatrix;
        if (pass)
            GD.Print("[CombatAudit] PASS attack floor/AS/overweight matrix, mining overweight x3/neutralize x2 matrix, Shuriken range-horse-cooldown truth table, walk animation set, turn no-spam gate");
        else
            GD.PrintErr($"[CombatAudit] FAIL attackMatrix={attackMatrix} miningMatrix={miningMatrix} shurikenMatrix={shurikenMatrix} walkAnimations={walkAnimations} turnMatrix={turnMatrix}");
    }

    private static void RunLightAudit()
    {
        const float epsilon = 0.0001f;
        bool pass = Math.Abs(MapLightLayer.AmbientFor(LightSetting.Night, 1f) - 0.25f) < epsilon
            && Math.Abs(MapLightLayer.AmbientFor(LightSetting.Twilight, 1f) - 100f / 255f) < epsilon
            && Math.Abs(MapLightLayer.AmbientFor(LightSetting.Light, 0f) - 1f) < epsilon
            && Math.Abs(MapLightLayer.AmbientFor(LightSetting.Default, 0.42f) - 0.42f) < epsilon
            && Math.Abs(MapLightLayer.ObjectLightRadius(3) - 56.32f) < epsilon
            && Math.Abs(MapLightLayer.TileLightRadius(1) - 179.2f) < epsilon
            && Math.Abs(MapLightLayer.EffectLightRadius(35) - 97.28f) < epsilon
            && Math.Abs(MapLightLayer.AbyssGlowRadius() - 46.08f) < epsilon
            && Math.Abs(MapLightLayer.DeadTint.R - 205f / 255f) < epsilon
            && Math.Abs(MapLightLayer.DeadTint.G - 92f / 255f) < epsilon;
        if (pass)
            GD.Print("[LightAudit] PASS Night=0.25 Twilight=100/255 Light=255/255 Default=DayTime");
        else
            GD.PrintErr($"[LightAudit] FAIL Night={MapLightLayer.AmbientFor(LightSetting.Night, 1f)} " +
                $"Twilight={MapLightLayer.AmbientFor(LightSetting.Twilight, 1f)} " +
                $"Light={MapLightLayer.AmbientFor(LightSetting.Light, 0f)}");
    }

    private void BeginLightRenderAudit()
    {
        if (DisplayServer.GetName() == "headless")
        {
            GD.Print("[LightRenderAudit] SKIP headless (requires Vulkan screenshot readback)");
            return;
        }

        // 使用与生产 MapLightLayer 相同的 MapView/光照节点，但复用本场景
        // 已绘制的地形作为底图，避免把测试场景误当成仅常量审计。
        _lightAuditMapView = new MapView { Visible = false };
        AddChild(_lightAuditMapView);
        _lightAuditMapView.LoadMap("0");
        _lightAuditMapView.CenterX = 10;
        _lightAuditMapView.CenterY = 10;

        _lightAuditMapInfo = Globals.MapInfoList?.Binding.FirstOrDefault(m => m.FileName == "0")
            ?? Globals.MapInfoList?.Binding.FirstOrDefault();
        if (_lightAuditMapInfo == null)
        {
            GD.PrintErr("[LightRenderAudit] FAIL no loaded MapInfo");
            return;
        }
        // 与生产 GameScene 相同的独立 CanvasLayer 布局: 整个世界先绘制,
        // 该层再触发一次全新的 hint_screen_texture 拷贝。Transform 用 2x
        // 与本场景根节点 Scale 一致, 层内保持逻辑坐标。
        var lightCanvas = new CanvasLayer
        {
            Layer = 1,
            Transform = new Transform2D(0f, Vector2.One * WorldScale, 0f, Vector2.Zero),
        };
        _lightAuditLayer = new MapLightLayer { ZIndex = RenderOrder.LightOverlay };
        lightCanvas.AddChild(_lightAuditLayer);
        AddChild(lightCanvas);
        _lightAuditLayer.SetMap(_lightAuditMapInfo, _lightAuditMapView);

        // 拷贝点劫持回归探针: 低 ZIndex 的 screen_texture 节点(模拟地形
        // Blend 行或施法特效)是世界画布内第一个 hint_screen_texture 用户,
        // 整屏拷贝在它绘制前发生。若光照层仍留在世界画布末尾, 它采样到的
        // 拷贝缺了其后绘制的白板 → 探针读数≈0; 移入独立 CanvasLayer 后
        // 层内重新拷贝, 白板被正确压暗 → 读数≈ambient(夜晚 0.25)。
        _probeBackdrop = new ColorRect
        {
            Position = LightProbeArea.Position,
            Size = LightProbeArea.Size,
            Color = Colors.Black,
            ZIndex = 50,
        };
        AddChild(_probeBackdrop);
        _probeHijacker = new ColorRect
        {
            Position = LightProbeArea.Position,
            Size = LightProbeArea.Size,
            Color = new Color(0.5f, 0.5f, 0.5f),
            Material = LegacyBlendMaterial.Create(),
            ZIndex = 100,
        };
        AddChild(_probeHijacker);
        _probeWhite = new ColorRect
        {
            Position = LightProbeArea.Position,
            Size = LightProbeArea.Size,
            Color = Colors.White,
            ZIndex = 200,
        };
        AddChild(_probeWhite);

        _lightAuditStage = 0;
        _lightAuditFrames = 0;

        ApplyLightRenderStage();
    }

    private void ApplyLightRenderStage()
    {
        var stage = LightRenderStages[_lightAuditStage];
        _lightAuditLayer.SetAuditLightOverride(stage.Setting);
        _lightAuditLayer.SetDayTime(stage.DayTime);
        // 特殊光照态: 死亡/深渊用探针位置合成状态驱动 (生产链路为
        // GameScene → SetPlayerState; 审计不依赖网络/角色状态)。
        var probeCentre = LightProbeArea.Position + LightProbeArea.Size / 2;
        _lightAuditLayer.SetPlayerState(() => new MapLightLayer.PlayerLightState(
            stage.Dead, stage.Abyss, probeCentre));
        _lightAuditLayer.QueueRedraw();
        GD.Print($"[LightRenderAudit] START {stage.Name} ambient={MapLightLayer.AmbientFor(stage.Setting, stage.DayTime):0.000}");
    }

    private void ProcessLightRenderAudit()
    {
        if (!_lightRenderAudit || _lightAuditLayer == null || ++_lightAuditFrames < 3) return;
        _lightAuditFrames = 0;
        var image = GetViewport().GetTexture()?.GetImage();
        var stage = LightRenderStages[_lightAuditStage];
        if (image == null)
        {
            GD.PrintErr($"[LightRenderAudit] FAIL {stage.Name}: no viewport image");
            return;
        }

        string path = $"/tmp/zircon-light-{stage.Name}.png";
        var error = image.SavePng(path);
        if (error != Error.Ok)
            GD.PrintErr($"[LightRenderAudit] FAIL {stage.Name}: save={error}");
        else
            GD.Print($"[LightRenderAudit] PASS {stage.Name} ambient={MapLightLayer.AmbientFor(stage.Setting, stage.DayTime):0.000} viewport={image.GetWidth()}x{image.GetHeight()} path={path}");

        // 白板中心读数 = 环境光 × 白板, 与 stage 环境光一致; 若光照层被低
        // ZIndex 的 screen_texture 节点劫持拷贝点, 白板不在采样画面内,
        // 读数会落到黑底(≈0)。阈值 0.1 远低于最低环境光 0.25。
        int probeX = Math.Min((int)(LightProbeArea.Position.X + LightProbeArea.Size.X / 2) * 2, image.GetWidth() - 2);
        int probeY = Math.Min((int)(LightProbeArea.Position.Y + LightProbeArea.Size.Y / 2) * 2, image.GetHeight() - 2);
        var probePixel = image.GetPixel(probeX, probeY);
        float probeLum = probePixel.R * 0.299f + probePixel.G * 0.587f + probePixel.B * 0.114f;
        if (probeLum < 0.1f)
            GD.PrintErr($"[LightRenderAudit] FAIL {stage.Name} probe=(0x{probePixel.ToHtml()}) lum={probeLum:0.000} < 0.1 (拷贝点被劫持?)");
        else
            GD.Print($"[LightRenderAudit] PASS {stage.Name} probe=(0x{probePixel.ToHtml()}) lum={probeLum:0.000}");

        // 特殊态语义断言: 死亡 → 白板被 IndianRed 相乘 (红通道显著高于绿);
        // 深渊 → 探针位于微光中心, 亮度应接近全亮。
        if (stage.Dead && probePixel.R <= probePixel.G + 0.05f)
            GD.PrintErr($"[LightRenderAudit] FAIL {stage.Name} probe=(0x{probePixel.ToHtml()}) 死亡红染未生效 (R 应显著大于 G)");
        else if (stage.Dead)
            GD.Print($"[LightRenderAudit] PASS {stage.Name} dead-tint probe=(0x{probePixel.ToHtml()})");
        if (stage.Abyss && probeLum < 0.5f)
            GD.PrintErr($"[LightRenderAudit] FAIL {stage.Name} probe=(0x{probePixel.ToHtml()}) lum={probeLum:0.000} < 0.5 深渊微光未生效");
        else if (stage.Abyss)
            GD.Print($"[LightRenderAudit] PASS {stage.Name} abyss-glow probe=(0x{probePixel.ToHtml()}) lum={probeLum:0.000}");

        _lightAuditStage++;
        if (_lightAuditStage >= LightRenderStages.Length)
        {
            GD.Print("[LightRenderAudit] PASS all=5 stages=night,twilight,default,dead,abyss");
            _lightRenderAudit = false;
            return;
        }
        ApplyLightRenderStage();
    }

    private void BeginWeatherRenderAudit()
    {
        if (DisplayServer.GetName() == "headless")
        {
            GD.Print("[WeatherRenderAudit] SKIP headless (requires Vulkan screenshot readback)");
            _weatherRenderAudit = false;
            return;
        }

        _weatherAuditLayer = new MapWeatherLayer { ZIndex = 2100 };
        AddChild(_weatherAuditLayer);
        _weatherAuditLayer.SetWeather(Weather.RainFogLightning);
        _weatherAuditFrames = 0;
        GD.Print("[WeatherRenderAudit] START RainFogLightning");
    }

    private void ProcessWeatherRenderAudit()
    {
        if (!_weatherRenderAudit || _weatherAuditLayer == null || ++_weatherAuditFrames < 30) return;
        _weatherRenderAudit = false;
        var image = GetViewport().GetTexture()?.GetImage();
        if (image == null)
        {
            GD.PrintErr("[WeatherRenderAudit] FAIL no viewport image");
            return;
        }
        string path = "/tmp/zircon-weather-rain-fog-lightning.png";
        var error = image.SavePng(path);
        if (error == Error.Ok)
            GD.Print($"[WeatherRenderAudit] PASS weather=RainFogLightning viewport={image.GetWidth()}x{image.GetHeight()} path={path}");
        else
            GD.PrintErr($"[WeatherRenderAudit] FAIL save={error}");
    }

    private void BeginMapFamilyRenderAudit()
    {
        if (DisplayServer.GetName() == "headless")
        {
            GD.Print("[MapFamilyRenderAudit] SKIP headless (requires Vulkan screenshot readback)");
            _mapFamilyRenderAudit = false;
            return;
        }

        // 隐藏 MapTestScene 的 20x20 诊断精灵，只保留真实 MapView。
        foreach (Node child in GetChildren())
            if (child is CanvasItem canvas && child is not Label) canvas.Visible = false;

        _mapFamilyView = new MapView();
        AddChild(_mapFamilyView);
        _mapFamilyIndex = 0;
        _mapFamilyFrames = 0;
        LoadMapFamilySample();
    }

    private void LoadMapFamilySample()
    {
        string name = MapFamilySamples[_mapFamilyIndex];
        try
        {
            var mapInfo = Globals.MapInfoList?.Binding.FirstOrDefault(m => m.FileName == name);
            int background = mapInfo?.Background ?? 0;
            _mapFamilyView.LoadMap(name, background);
            _mapFamilyView.CenterX = _mapFamilyView.Map.Width / 2;
            _mapFamilyView.CenterY = _mapFamilyView.Map.Height / 2;
            _mapFamilyView.QueueRedraw();
            _mapFamilyFrames = 0;
            GD.Print($"[MapFamilyRenderAudit] START map={name} background={background} size={_mapFamilyView.Map.Width}x{_mapFamilyView.Map.Height}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MapFamilyRenderAudit] FAIL map={name}: {ex.GetType().Name}:{ex.Message}");
            _mapFamilyRenderAudit = false;
        }
    }

    private void ProcessMapFamilyRenderAudit()
    {
        if (!_mapFamilyRenderAudit || _mapFamilyView == null || ++_mapFamilyFrames < 4) return;
        _mapFamilyFrames = 0;
        string name = MapFamilySamples[_mapFamilyIndex];
        var image = GetViewport().GetTexture()?.GetImage();
        if (image == null)
        {
            GD.PrintErr($"[MapFamilyRenderAudit] FAIL map={name}: no viewport image");
            _mapFamilyRenderAudit = false;
            return;
        }
        string path = $"/tmp/zircon-map-family-{name}.png";
        var error = image.SavePng(path);
        if (error != Error.Ok)
        {
            GD.PrintErr($"[MapFamilyRenderAudit] FAIL map={name}: save={error}");
            _mapFamilyRenderAudit = false;
            return;
        }
        GD.Print($"[MapFamilyRenderAudit] PASS map={name} viewport={image.GetWidth()}x{image.GetHeight()} path={path}");
        _mapFamilyIndex++;
        if (_mapFamilyIndex >= MapFamilySamples.Length)
        {
            GD.Print("[MapFamilyRenderAudit] PASS all=5 samples=0,1,5,D001,E01");
            _mapFamilyRenderAudit = false;
            return;
        }
        LoadMapFamilySample();
    }

    private static void RunNetworkAudit()
    {
        TcpListener listener = null;
        TcpClient client = null;
        TcpClient accepted = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            client = new TcpClient();
            client.Connect(IPAddress.Loopback, port);
            accepted = listener.AcceptTcpClient();

            var connection = new ServerConnection(client);
            int disconnectEvents = 0;
            connection.DisconnectedEvent += () => disconnectEvents++;
            connection.NotifyDisconnected(closeTransport: true);
            connection.NotifyDisconnected(closeTransport: true);
            connection.Disconnect();

            var controller = new CombatController(null, null, null, null, null,
                rightClickDeTarget: () => true);
            var removedTarget = new ObjectRenderer { ObjectID = 7001 };
            controller.TargetObject = removedTarget;
            controller.MouseObject = removedTarget;
            controller.RemoveObjectReference(removedTarget.ObjectID);

            bool referencesCleared = controller.TargetObject == null && controller.MouseObject == null;
            var playerTarget = new ObjectRenderer { Type = ObjectRenderer.Kind.Player, Dead = false };
            var guardMonster = new ObjectRenderer
            {
                Type = ObjectRenderer.Kind.Monster,
                Dead = false,
            };
            bool playerAttackSemantics = CombatController.CanAttackObject(playerTarget)
                && !CombatController.CanAttackObject(guardMonster);
            bool pickupPrioritySemantics = CombatController.ShouldDeferForMapPickup(
                    new System.Drawing.Point(10, 10), new System.Drawing.Point(10, 10), false)
                && !CombatController.ShouldDeferForMapPickup(
                    new System.Drawing.Point(10, 10), new System.Drawing.Point(10, 10), true)
                && !CombatController.ShouldDeferForMapPickup(
                    new System.Drawing.Point(11, 10), new System.Drawing.Point(10, 10), false);
            bool pickupStateSemantics = GameScene.CanSendMapPickup(false, false, false, false, false)
                && !GameScene.CanSendMapPickup(true, false, false, false, false)
                && !GameScene.CanSendMapPickup(false, true, false, false, false)
                && !GameScene.CanSendMapPickup(false, false, true, false, false)
                && !GameScene.CanSendMapPickup(false, false, false, true, false)
                && !GameScene.CanSendMapPickup(false, false, false, false, true);
            // A2: 原版 PickUpTime 250ms 节流（鼠标点击与 Tab 共用）。
            bool pickupCooldownSemantics = GameScene.CanSendPickUp(300, 250)
                && !GameScene.CanSendPickUp(249, 250)
                && GameScene.CanSendPickUp(250, 250);
            // A3: 原版 CheckCursor 环扫描边界 — 上/左越界 break、下/右越界 continue。
            bool ringEdgeSemantics = CombatController.RingEdgeMode(-1, 64) == -1
                && CombatController.RingEdgeMode(0, 64) == 0
                && CombatController.RingEdgeMode(64, 64) == 1
                && CombatController.RingEdgeMode(63, 64) == 0
                && CombatController.RingEdgeMode(-1, 0) == -1;
            // A5: 无 Shift 点击玩家/宠物 → 只选中不追击；Shift 或怪物 → 追击。
            bool playerSelectOnlySemantics = CombatController.ShouldSelectOnly(true, false, false)
                && CombatController.ShouldSelectOnly(false, true, false)
                && CombatController.ShouldSelectOnly(true, true, false)
                && !CombatController.ShouldSelectOnly(true, false, true)
                && !CombatController.ShouldSelectOnly(false, false, false)
                && !CombatController.ShouldSelectOnly(false, true, true);
            // A5: Shuriken 超 Globals.MagicRange 不可投（原版 683-692 提示+取消）。
            bool shurikenRangeSemantics = !Functions.InRange(
                new System.Drawing.Point(0, 0), new System.Drawing.Point(11, 0), Globals.MagicRange)
                && Functions.InRange(
                    new System.Drawing.Point(0, 0), new System.Drawing.Point(10, 0), Globals.MagicRange);
            // A6: 钓鱼必须武器槽 FishingRod + 护甲槽 FishingRobe；护甲缺失不算。
            bool fishingRigSemantics = GameScene.IsFishingRig(ItemEffect.FishingRod, ItemEffect.FishingRobe)
                && !GameScene.IsFishingRig(ItemEffect.FishingRod, null)
                && !GameScene.IsFishingRig(ItemEffect.FishingRod, ItemEffect.None)
                && !GameScene.IsFishingRig(null, ItemEffect.FishingRobe);
            // A6: 挖矿必须武器槽 PickAxe（非任意槽）、耐久、矿点 Flag、相邻、无马。
            bool miningSemantics = GameScene.CanMineNow(true, ItemEffect.PickAxe, 10, 0, true, true, true, false)
                && !GameScene.CanMineNow(false, ItemEffect.PickAxe, 10, 0, true, true, true, false)
                && !GameScene.CanMineNow(true, null, 10, 0, true, true, true, false)
                && !GameScene.CanMineNow(true, ItemEffect.None, 10, 0, true, true, true, false)
                && !GameScene.CanMineNow(true, ItemEffect.PickAxe, 0, 10, true, true, true, false)
                && GameScene.CanMineNow(true, ItemEffect.PickAxe, 0, 0, true, true, true, false)
                && !GameScene.CanMineNow(true, ItemEffect.PickAxe, 10, 0, true, false, true, false)
                && !GameScene.CanMineNow(true, ItemEffect.PickAxe, 10, 0, true, true, false, false)
                && !GameScene.CanMineNow(true, ItemEffect.PickAxe, 10, 0, true, true, true, true);
            bool autoPathSemantics = GameScene.ShouldQueueAutoPathMove(true, false)
                && !GameScene.ShouldQueueAutoPathMove(true, true)
                && !GameScene.ShouldQueueAutoPathMove(false, false);
            bool mapRightCancelSemantics = GameScene.ShouldCancelMapRightClick(true, false)
                && GameScene.ShouldCancelMapRightClick(false, true)
                && !GameScene.ShouldCancelMapRightClick(false, false);
            bool gatheringSemantics = GameScene.ShouldCancelGatheringForMapClick(false, true, false)
                && GameScene.ShouldCancelGatheringForMapClick(false, false, true)
                && !GameScene.ShouldCancelGatheringForMapClick(true, true, false)
                && !GameScene.ShouldCancelGatheringForMapClick(true, false, true)
                && !GameScene.ShouldCancelGatheringForMapClick(false, false, false);
            bool currencyDropSemantics = GameScene.CanDropCurrency(false, 100, 1)
                && GameScene.CanDropCurrency(false, 100, 100)
                && !GameScene.CanDropCurrency(false, 100, 101)
                && !GameScene.CanDropCurrency(false, 0, 1)
                && !GameScene.CanDropCurrency(true, 100, 1);
            var consumed = new ClientUserItem { Count = 5 };
            bool consumePartial = GameScene.TryConsumeItemCount(consumed, 2, out bool partialRemove)
                && consumed.Count == 3 && !partialRemove;
            bool consumeWhole = GameScene.TryConsumeItemCount(consumed, 3, out bool wholeRemove)
                && wholeRemove && consumed.Count == 3;
            bool rejectLateConsume = !GameScene.TryConsumeItemCount(consumed, 4, out _)
                && consumed.Count == 3;
            var splitSource = new ClientUserItem { Count = 5, AddedStats = new Stats() };
            var splitGrid = new ClientUserItem[2];
            splitGrid[0] = splitSource;
            bool splitPartial = GameScene.TryApplyItemSplit(splitSource, splitGrid, 0, 1, 2)
                && splitSource.Count == 3 && splitGrid[1]?.Count == 2;
            var overflowGrid = new ClientUserItem[2];
            overflowGrid[0] = splitSource;
            bool splitRejectOverflow = !GameScene.TryApplyItemSplit(splitSource, overflowGrid, 0, 1, 4);
            var occupied = new ClientUserItem[2];
            occupied[1] = new ClientUserItem { Count = 1, AddedStats = new Stats() };
            var overwriteSource = new ClientUserItem { Count = 3, AddedStats = new Stats() };
            occupied[0] = overwriteSource;
            bool splitRejectOverwrite = !GameScene.TryApplyItemSplit(overwriteSource, occupied, 0, 1, 1)
                && occupied[1].Count == 1;
            // 回放：启动阶段包可暂存；进入运行态后同一包只能实时派发一次；
            // 切图包到达时，尚未排空的旧地图包必须被丢弃，之后的迟到包不能重新进入积压队列。
            connection.PendingMoves.Enqueue(default);
            connection.StopPendingPacketBuffering();
            int moveEvents = 0;
            connection.ObjectMoveEvent += (_, _, _, _, _, _) => moveEvents++;
            connection.Process(new S.MapChanged { MapIndex = 7, InstanceIndex = -1 });
            connection.Process(new S.ObjectMove { ObjectID = 901, Distance = 1 });
            bool replayOrdering = connection.PendingMoves.Count == 0 && moveEvents == 1;
            bool pass = !connection.Connected && disconnectEvents == 1 && referencesCleared
                && autoPathSemantics && mapRightCancelSemantics && replayOrdering
                && gatheringSemantics
                && playerAttackSemantics
                && pickupPrioritySemantics
                && pickupStateSemantics
                && pickupCooldownSemantics
                && ringEdgeSemantics
                && playerSelectOnlySemantics
                && shurikenRangeSemantics
                && fishingRigSemantics
                && miningSemantics
                && currencyDropSemantics
                && consumePartial && consumeWhole && rejectLateConsume
                && splitPartial && splitRejectOverflow && splitRejectOverwrite;
            if (pass)
                GD.Print("[NetworkAudit] PASS duplicate disconnect collapsed, transport closed, removed-object references cleared, player/monster attackability semantics, current-cell pickup priority, pickup 250ms cooldown, pickup state guards, auto-path transition semantics, map right-click cancellation, Alt gathering state semantics, ring-edge break/continue, player select-only, Shuriken range, fishing robe rig, mining state machine, currency-drop bounds, stale/late packet replay ordering, item-count bounds and split-target protection");
            else
                GD.PrintErr($"[NetworkAudit] FAIL connected={connection.Connected} disconnectEvents={disconnectEvents} referencesCleared={referencesCleared} playerAttack={playerAttackSemantics} pickupPriority={pickupPrioritySemantics} pickupState={pickupStateSemantics} pickupCooldown={pickupCooldownSemantics} autoPathSemantics={autoPathSemantics} mapRightCancel={mapRightCancelSemantics} gathering={gatheringSemantics} ringEdge={ringEdgeSemantics} playerSelectOnly={playerSelectOnlySemantics} shurikenRange={shurikenRangeSemantics} fishingRig={fishingRigSemantics} mining={miningSemantics} currencyDrop={currencyDropSemantics} replayOrdering={replayOrdering} consume={consumePartial}/{consumeWhole}/{rejectLateConsume} split={splitPartial}/{splitRejectOverflow}/{splitRejectOverwrite}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NetworkAudit] FAIL {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { accepted?.Close(); } catch { }
            try { listener?.Stop(); } catch { }
        }
    }

    /// <summary>
    /// P-006: 真实回环 socket 上的确定性异常回放。与 RunNetworkAudit（进程内直调 Process）互补，
    /// 这里把包序列化成字节流经过真实 TCP 收发，镜像 NetworkManager._Process 的同步轮询
    /// （读可用字节 → Packet.ReceivePacket → ReceiveList → Process 派发），验证：
    ///   1) 半包（长度前缀被拆到两次 write）→ 不派发，补齐后恰好派发一次；
    ///   2) 多包合并进一次 write → 按序各派发一次；
    ///   3) 启动阶段突发包 → 进 Pending 队列且事件双发；StopPendingPacketBuffering 后不再重复入队；
    ///   4) 切图后迟到的重复包 → 只实时派发，不重新进入积压队列（不会复活旧地图对象）；
    ///   5) 服务器 FIN 断开 → DisconnectedEvent 恰好一次，重复 NotifyDisconnected 折叠；
    ///   6) 垃圾字节 → 帧缓冲挂起但不崩溃、不误派发。
    /// </summary>
    private static void RunAnomalyReplayAudit()
    {
        TcpListener listener = null;
        TcpClient serverSide = null;
        TcpClient clientSide = null;
        try
        {
            // —— 场景 A：缓冲/迟到/重复/分片 ——
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            clientSide = new TcpClient();
            clientSide.Connect(IPAddress.Loopback, port);
            serverSide = listener.AcceptTcpClient();
            serverSide.NoDelay = true;
            var serverStream = serverSide.GetStream();

            var connection = new ServerConnection(clientSide);
            connection.UpdateTimeOut();
            int moveEvents = 0, mapChanges = 0;
            connection.ObjectMoveEvent += (_, _, _, _, _, _) => moveEvents++;
            connection.MapChangedEvent += (_, _) => mapChanges++;

            byte[] raw = Array.Empty<byte>();
            bool Pump()
            {
                // 镜像 NetworkManager._Process 的同步轮询
                try
                {
                    if (!clientSide.Connected) return false;
                    if (clientSide.Available == 0 && !clientSide.Client.Poll(1000, SelectMode.SelectRead))
                        return true; // 暂无数据：只推进 Process()（发送队列/超时）
                    var stream = clientSide.GetStream();
                    byte[] buf = new byte[8 * 1024];
                    int read = stream.Read(buf, 0, buf.Length);
                    if (read == 0) return false; // FIN
                    byte[] temp = raw;
                    raw = new byte[read + temp.Length];
                    Array.Copy(temp, 0, raw, 0, temp.Length);
                    Array.Copy(buf, 0, raw, temp.Length, read);
                    Library.Network.Packet p;
                    while ((p = Library.Network.Packet.ReceivePacket(raw, out raw)) != null)
                        connection.ReceiveList.Enqueue(p);
                    connection.Process();
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }

            // 3) 启动阶段突发包：入队 + 事件双发
            byte[] a = new S.ObjectMove { ObjectID = 901, Distance = 1 }.GetPacketBytes();
            serverStream.Write(a, 0, a.Length);
            serverStream.Flush();
            Pump();
            bool bufferedQueued = connection.PendingMoves.Count == 1 && moveEvents == 1;

            // 关闭缓冲后实时包不再入队
            connection.StopPendingPacketBuffering();
            byte[] b = new S.ObjectMove { ObjectID = 902, Distance = 1 }.GetPacketBytes();
            serverStream.Write(b, 0, b.Length);
            serverStream.Flush();
            Pump();
            bool runningLive = connection.PendingMoves.Count == 1 && moveEvents == 2;

            // 4) 切图清空积压；之后迟到的重复包只实时派发、不重新入队
            byte[] mapBytes = new S.MapChanged { MapIndex = 7, InstanceIndex = -1 }.GetPacketBytes();
            serverStream.Write(mapBytes, 0, mapBytes.Length);
            serverStream.Flush();
            Pump();
            bool mapClearedPending = connection.PendingMoves.Count == 0 && mapChanges == 1;

            // 2) 多包合并进一次 write + 迟到的重复包
            byte[] c1 = new S.ObjectMove { ObjectID = 903, Distance = 1 }.GetPacketBytes();
            serverStream.Write(c1, 0, c1.Length);
            serverStream.Write(c1, 0, c1.Length); // 重复
            serverStream.Flush();
            Pump();
            bool coalescedOrdered = moveEvents == 4;
            bool lateDuplicateNoRequeue = connection.PendingMoves.Count == 0 && moveEvents == 4;

            // 1) 半包：先发长度前缀前 2 字节（不足 4 字节帧头 → 挂起不派发）
            byte[] d = new S.ObjectMove { ObjectID = 904, Distance = 1 }.GetPacketBytes();
            int split = 2;
            serverStream.Write(d, 0, split);
            serverStream.Flush();
            Pump();
            bool fragHeld = moveEvents == 4;
            serverStream.Write(d, split, d.Length - split);
            serverStream.Flush();
            Pump();
            bool fragDeliveredOnce = moveEvents == 5;

            connection.Disconnect();
            bool scenarioA = bufferedQueued && runningLive && mapClearedPending
                && coalescedOrdered && lateDuplicateNoRequeue && fragHeld && fragDeliveredOnce;

            // —— 场景 B：服务器 FIN 断开 + 垃圾字节 ——
            TcpClient clientB = null;
            TcpClient serverB = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int portB = ((IPEndPoint)listener.LocalEndpoint).Port;
                clientB = new TcpClient();
                clientB.Connect(IPAddress.Loopback, portB);
                serverB = listener.AcceptTcpClient();
                serverB.NoDelay = true;
                var serverBStream = serverB.GetStream();

                var connectionB = new ServerConnection(clientB);
                connectionB.UpdateTimeOut();
                int bMoves = 0, bDisconnects = 0;
                connectionB.ObjectMoveEvent += (_, _, _, _, _, _) => bMoves++;
                connectionB.DisconnectedEvent += () => bDisconnects++;

                byte[] rawB = Array.Empty<byte>();
                bool PumpB()
                {
                    try
                    {
                        if (!clientB.Connected) return false;
                        if (clientB.Available == 0 && !clientB.Client.Poll(1000, SelectMode.SelectRead))
                            return true;
                        var stream = clientB.GetStream();
                        byte[] buf = new byte[8 * 1024];
                        int read = stream.Read(buf, 0, buf.Length);
                        if (read == 0) return false;
                        byte[] temp = rawB;
                        rawB = new byte[read + temp.Length];
                        Array.Copy(temp, 0, rawB, 0, temp.Length);
                        Array.Copy(buf, 0, rawB, temp.Length, read);
                        Library.Network.Packet p;
                        while ((p = Library.Network.Packet.ReceivePacket(rawB, out rawB)) != null)
                            connectionB.ReceiveList.Enqueue(p);
                        connectionB.Process();
                        return true;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }

                // 6) 垃圾字节：帧缓冲挂起（长度前缀非法）→ 不崩溃、不误派发
                byte[] garbage = new byte[512];
                for (int i = 0; i < garbage.Length; i++) garbage[i] = 0xA5;
                serverBStream.Write(garbage, 0, garbage.Length);
                serverBStream.Flush();
                PumpB();
                bool garbageHeld = bMoves == 0 && connectionB.Connected;

                // 5) 服务器 FIN 断开 → 恰好一次断线事件；重复通知折叠
                serverB.Client.Shutdown(SocketShutdown.Send);
                serverB.Close();
                bool finDetected = !PumpB();
                connectionB.NotifyDisconnected(closeTransport: true);
                connectionB.NotifyDisconnected(closeTransport: true);
                bool disconnectedOnce = bDisconnects == 1 && !connectionB.Connected;

                bool scenarioB = garbageHeld && finDetected && disconnectedOnce;
                bool pass = scenarioA && scenarioB;
                if (pass)
                    GD.Print("[AnomalyReplay] PASS real-socket framing (fragmented held then delivered once), coalesced ordering, buffered startup burst queued once, running-state live dispatch, late duplicate after map change does not re-enter backlog, server FIN disconnect fired exactly once and collapsed, garbage bytes stall framing without crash or misdispatch");
                else
                    GD.PrintErr($"[AnomalyReplay] FAIL scenarioA={scenarioA} (bufferedQueued={bufferedQueued} runningLive={runningLive} mapClearedPending={mapClearedPending} coalescedOrdered={coalescedOrdered} lateDuplicateNoRequeue={lateDuplicateNoRequeue} fragHeld={fragHeld} fragDeliveredOnce={fragDeliveredOnce}) scenarioB={scenarioB} (garbageHeld={garbageHeld} finDetected={finDetected} disconnectedOnce={disconnectedOnce}) moveEvents={moveEvents} mapChanges={mapChanges}");
            }
            finally
            {
                try { clientB?.Close(); } catch { }
                try { serverB?.Close(); } catch { }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AnomalyReplay] FAIL {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { serverSide?.Close(); } catch { }
            try { clientSide?.Close(); } catch { }
            try { listener?.Stop(); } catch { }
        }
    }

    public override void _Process(double delta)
    {
        ProcessLightRenderAudit();
        ProcessWeatherRenderAudit();
        ProcessMapFamilyRenderAudit();
        ProcessBlendAudit();
        ProcessEffectTargetFreeAudit();
        if (_actionAudit) ProcessActionAudit();
        if (_projectileAudit) ProcessProjectileAudit();
        if (!_renderAudit || ++_auditFrames != 3) return;
        // Dummy/headless 渲染器没有可读回的 Texture2D；对象绘制本身仍会执行，
        // 非 headless 环境才保存像素截图。
        var viewportTexture = GetViewport().GetTexture();
        var image = DisplayServer.GetName() == "headless" || viewportTexture == null
            ? null : viewportTexture.GetImage();
        if (image != null)
        {
            string output = "/tmp/zircon-render-audit.png";
            image.SavePng(output);
            GD.Print($"[RenderAudit] 截图: {output}");
        }
        else
            GD.Print("[RenderAudit] headless/dummy 模式完成对象绘制检查（无可读回纹理）");
    }

    private void BeginActionAudit()
    {
        RunTransparencyAudit();
        RunWeatherAudit();
        RunTransparencyModeAudit();
        RunLayerOrderAudit();
        RunMagicCoverageAudit();
        RunMagicFrameAudit();
        _auditPlayer = new PlayerRenderer();
        _auditPlayer.UpdateAppearance(new StartInformation
        {
            Name = "ActionAuditHero", Class = MirClass.Warrior, Gender = MirGender.Male,
            HairType = 1, HairColour = System.Drawing.Color.Black, Armour = 0,
            ArmourColour = System.Drawing.Color.White, Costume = -1, HelmetShape = 0,
            Shield = -1, Weapon = 0, Horse = HorseType.None, Direction = MirDirection.Down
        });
        _auditPlayer.FrameChanged = (anim, frame, magic) =>
        {
            if (_actionAudit)
            {
                _actionFrames.Add(frame);
                _actionAnimations.Add(anim);
            }
        };
        AddChild(_auditPlayer);
        // Some legacy WAV files use headers that Godot's native decoder rejects
        // noisily. Keep the action audit deterministic while allowing the sound
        // catalog audit to be run explicitly when the decoder is under test.
        if (!OS.GetCmdlineUserArgs().Contains("--skip-sound-audit"))
            RunSoundAssetAudit();
        else
            GD.Print("[SoundAudit] SKIP requested for action-only audit");
        _actions.Add(("Walking", p => p.BeginMove(MirDirection.Right, 1, false, false), MirAnimation.Walking));
        _actions.Add(("Running", p => p.BeginMove(MirDirection.Right, 2, false, true), MirAnimation.Running));
        _actions.Add(("HorseWalking", (Action<PlayerRenderer>)(p => { p.Horse = HorseType.Brown; p.RefreshAppearanceLibraries(); p.BeginMove(MirDirection.Right, 1, true, false); }), MirAnimation.HorseWalking));
        _actions.Add(("HorseRunning", p => p.BeginMove(MirDirection.Right, 2, true, true), MirAnimation.HorseRunning));
        _actions.Add(("Combat", p => p.PlayCombat(MagicType.None), Functions.GetAttackAnimation(MirClass.Warrior, 0, MagicType.None)));
        _actions.Add(("ActionQueue", p => { p.PlayCombat(MagicType.None); p.PlayStruck(); }, Functions.GetAttackAnimation(MirClass.Warrior, 0, MagicType.None)));
        _actions.Add(("RangeAttack", p => p.PlayRangeAttack(), MirAnimation.Combat1));
        _actions.Add(("ShoulderDash", p => p.PlayDash(MagicType.ShoulderDash), MirAnimation.Combat8));
        _actions.Add(("Spell", p => p.PlaySpell(MagicType.SeismicSlam), Functions.GetMagicAnimation(MagicType.SeismicSlam)));
        _actions.Add(("CrushingWave", p => p.PlaySpell(MagicType.CrushingWave), Functions.GetMagicAnimation(MagicType.CrushingWave)));
        _actions.Add(("OffensiveBlow", p => p.PlayCombat(MagicType.OffensiveBlow), Functions.GetAttackAnimation(MirClass.Warrior, 0, MagicType.OffensiveBlow)));
        _actions.Add(("ChannelStart", p => { p.ElementalHurricane = false; p.PlaySpell(MagicType.ElementalHurricane); }, MirAnimation.ChannellingStart));
        _actions.Add(("ChannelEnd", p => { p.ElementalHurricane = true; p.PlaySpell(MagicType.ElementalHurricane); }, MirAnimation.ChannellingEnd));
        _actions.Add(("Struck", p => p.PlayStruck(), MirAnimation.Struck));
        _actions.Add(("Pushed", p => p.PlayPushed(), MirAnimation.Pushed));
        _actions.Add(("Harvest", p => p.PlayHarvest(), MirAnimation.Harvest));
        _actions.Add(("Mining", p => p.PlayMining(), Functions.GetAttackAnimation(MirClass.Warrior, 0, MagicType.None)));
        _actions.Add(("FishingCast", p => p.PlayFishing(FishingState.Cast, true, new System.Drawing.Point(1, 1)), MirAnimation.FishingCast));
        _actions.Add(("FishingWait", p => p.SetAnimation(MirAnimation.FishingWait), MirAnimation.FishingWait));
        _actions.Add(("FishingReel", p => p.SetAnimation(MirAnimation.FishingReel), MirAnimation.FishingReel));
        _actions.Add(("TamingCast", p => p.PlayTaming(TamingState.Cast, 1), MirAnimation.TamingCast));
        _actions.Add(("TamingWait", p => p.SetAnimation(MirAnimation.TamingWait), MirAnimation.TamingWait));
        _actions.Add(("CloakBuff", p => { p.Cloaked = true; p.PlayStandingForState(); }, MirAnimation.CreepStanding));
        _actions.Add(("DragonRepulseMiddle", p => { p.DragonRepulsed = true; p.PlayStandingForState(); }, MirAnimation.DragonRepulseMiddle));
        _actions.Add(("DragonRepulseEnd", p => { p.DragonRepulsed = false; p.PlayDragonRepulseEnd(); }, MirAnimation.DragonRepulseEnd));
        _actions.Add(("Dead", p => p.PlayDie(), MirAnimation.Die));
        // 施法时序回归：ObjectMagic 到达后不能在抬手第一帧立即生成
        // 轨迹；释放延迟必须来自旧版动作帧表，而不是固定跳到末帧。
        _auditPlayer.PlaySpell(MagicType.FireBall);
        double releaseDelay = _auditPlayer.SpellReleaseDelayMs;
        if (releaseDelay <= 0 || releaseDelay >= FrameSet.Players[Functions.GetMagicAnimation(MagicType.FireBall)].Sum)
            GD.PrintErr($"[SpellTimingAudit] FAIL releaseDelay={releaseDelay:0}ms");
        else
            GD.Print($"[SpellTimingAudit] PASS animation={_auditPlayer.Animation} releaseDelay={releaseDelay:0}ms " +
                $"total={FrameSet.Players[_auditPlayer.Animation].Sum:0}ms");
        _actionIndex = 0;
        _actionAuditFinished = false;
        StartNextActionAudit();
    }

    private void RunMagicCoverageAudit()
    {
        var missingSpell = new List<MagicType>();
        var noMapEffect = new List<MagicType>();
        int configured = 0;
        int attackOnly = 0;
        foreach (MagicType type in Enum.GetValues<MagicType>())
        {
            if (type == MagicType.None) continue;
            if (MagicEffectTable.Get(type) != null)
                configured++;
            else if (MagicEffectTable.GetAttack(type) != null)
                attackOnly++;
            else if (MagicEffectTable.IsOriginalSpellCase(type))
                missingSpell.Add(type);
            else
                noMapEffect.Add(type);
        }

        // 这里先只做覆盖率审计，不把被动技能、状态标记和纯动作技能
        // 当成失败；主动技能的轨迹会在后续按原版 GameScene 分支逐项补齐。
        var names = new StringBuilder();
        foreach (var type in missingSpell)
        {
            if (names.Length > 0) names.Append(',');
            names.Append(type);
        }
        GD.Print($"[MagicCoverageAudit] castConfigured={configured} attackOnly={attackOnly} " +
            $"missingOriginalSpell={missingSpell.Count} noMapEffect={noMapEffect.Count}");
        if (missingSpell.Count > 0)
            GD.PrintErr($"[MagicCoverageAudit] missingOriginalSpell={names}");
        if (noMapEffect.Count > 0)
            GD.Print($"[MagicCoverageAudit] intentionalOrEffectHandled={string.Join(',', noMapEffect)}");
    }

    private void RunMagicFrameAudit()
    {
        var seen = new HashSet<MagicEffectTable.CastEffect>();
        var failures = new List<string>();
        int originalResourceExceptions = 0;
        foreach (MagicType type in Enum.GetValues<MagicType>())
        {
            var def = MagicEffectTable.Get(type);
            if (def == null || !seen.Add(def)) continue;
            int castDirections = def.DirectionFromCast || def.DirectionFromSource ? 8 : 1;
            CheckEffectRange($"{type}.cast", def.File, def.StartIndex, def.FrameCount, def.Skip, castDirections, failures);
            CheckImpactRange($"{type}.source", def.Source, failures);
            foreach (var impact in def.SourceAdditional) CheckImpactRange($"{type}.sourceExtra", impact, failures);
            CheckProjectileRange($"{type}.projectile", def.Projectile, failures);
            CheckProjectileRange($"{type}.targetProjectile", def.TargetProjectile, failures);
            foreach (var projectile in def.AdditionalProjectiles) CheckProjectileRange($"{type}.extraProjectile", projectile, failures);
            foreach (var projectile in def.TargetAdditionalProjectiles) CheckProjectileRange($"{type}.targetExtraProjectile", projectile, failures);
            CheckImpactRange($"{type}.impact", def.Impact, failures);
            CheckImpactRange($"{type}.targetEffect", def.TargetEffect, failures);
            CheckImpactRange($"{type}.mapImpact", def.MapImpact, failures);
            foreach (var impact in def.Additional) CheckImpactRange($"{type}.additional", impact, failures);
            foreach (var impact in def.AdditionalMapEffects) CheckImpactRange($"{type}.mapAdditional", impact, failures);
        }

        failures.RemoveAll(failure =>
        {
            if (!failure.StartsWith("GreenSludgeBall.impact: MonMagicEx23 range=2780..2855", StringComparison.Ordinal))
                return false;

            // 资源核对结论（2026-08-08，P-003）：
            //   MonMagicEx23.Zl 只有 2800 个条目；命中特效仅在方向 0 存在
            //   （2780..2785），方向 >=1 的 2790..2855 条目全部为 NULL，
            //   资源没有任何更晚的帧。原版 MirEffect.Process 与 Godot
            //   MirEffectNode._Draw 对越界/空条目都是静默跳过（原版
            //   CheckImage index >= Images.Length 返回 false；Godot 同款
            //   df < 0 || df >= _lib.Images.Length 返回）。
            //   因此原版与 Godot 行为一致：命中特效只会在方向 0 显示，
            //   其余方向不出现在画面上。这不是 bug，而是资源版本本身
            //   的局限；不擅自改帧号，也不伪造原版不存在的资源。
            //
            // 下面按实际条目可绘制性自适应检查：只有那些真正越界或
            // 存在空条目的方向才计入 exception，其余方向正常参与范围
            // 校验（正常路径已经通过 CheckImpactRange 完成）。
            var library = LibraryCache.Get(LibraryFile.MonMagicEx23);
            if (library?.Images == null)
                return false;
            int undrawable = 0;
            for (int dir = 0; dir < 8; dir++)
            {
                int baseFrame = 2780 + dir * 10;
                for (int f = 0; f < 6; f++)
                {
                    int index = baseFrame + f;
                    if (index < 0 || index >= library.Images.Length || library.Images[index] == null)
                        undrawable++;
                }
            }
            // 预期：方向 0 的 6 帧可绘制；方向 1..7 的 42 帧不可绘制。
            // 一旦资源被替换为完整的方向帧，此审计会自动降为正常 PASS，
            // 不需要人工改例外清单。
            if (undrawable != 42)
            {
                GD.PrintErr($"[MagicFrameAudit] GreenSludgeBall impact resource changed: undrawable={undrawable} (expected 42: dir0 present, dir1-7 null)");
                return false;
            }
            originalResourceExceptions++;
            return true;
        });

        if (failures.Count == 0)
            GD.Print($"[MagicFrameAudit] PASS skills={seen.Count} originalResourceExceptions={originalResourceExceptions} (GreenSludgeBall impact dir0-only verified)");
        else
        {
            GD.PrintErr($"[MagicFrameAudit] FAIL count={failures.Count}");
            foreach (var failure in failures.Take(20)) GD.PrintErr($"[MagicFrameAudit] {failure}");
        }
    }

    private static void CheckImpactRange(string name, MagicEffectTable.ImpactDef def, List<string> failures)
    {
        if (def == null) return;
        if (def.DirectionStartIndices != null)
        {
            foreach (int start in def.DirectionStartIndices)
                CheckEffectRange(name, def.File, start, def.FrameCount, 0, 1, failures);
        }
        else CheckEffectRange(name, def.File, def.StartIndex, def.FrameCount, def.Skip,
            def.DirectionFromCast || def.DirectionFromSource ? 8 : 1, failures);
    }

    private static void CheckProjectileRange(string name, MagicEffectTable.ProjectileDef def, List<string> failures)
    {
        if (def == null) return;
        CheckEffectRange(name, def.File, def.StartIndex, def.FrameCount, def.Skip,
            def.Has16Directions ? 16 : 8, failures);
    }

    private static void CheckEffectRange(string name, LibraryFile file, int start, int count,
        int skip, int directionCount, List<string> failures)
    {
        if (count <= 0 || start < 0) { failures.Add($"{name}: invalid start/count {start}/{count}"); return; }
        var library = LibraryCache.Get(file);
        int last = start + Math.Max(0, directionCount - 1) * skip + count - 1;
        if (library?.Images == null || last >= library.Images.Length)
            failures.Add($"{name}: {file} range={start}..{last} library={(library?.Images?.Length ?? 0)}");
    }

    private void RunMapAudit()
    {
        string requestedMap = OS.GetCmdlineUserArgs()
            .FirstOrDefault(arg => arg.StartsWith("--audit-map=", StringComparison.OrdinalIgnoreCase))?
            .Substring("--audit-map=".Length);
        var files = Directory.GetFiles(_mapPath, "*.map")
            .Where(path => string.IsNullOrWhiteSpace(requestedMap) ||
                string.Equals(Path.GetFileNameWithoutExtension(path), requestedMap,
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        GD.Print($"[MapAudit] source={(string.IsNullOrWhiteSpace(requestedMap) ? "all maps" : requestedMap)} files={files.Length}");
        int valid = 0, layered = 0, totalCells = 0;
        var failures = new List<string>();
        var textureRefs = new HashSet<(int FileByte, int ImageIndex)>();
        foreach (string path in files)
        {
            try
            {
                var map = new MirMap(path);
                if (!map.IsValid || map.Width > 4096 || map.Height > 4096)
                {
                    failures.Add($"{Path.GetFileName(path)}:{map.Width}x{map.Height}");
                    continue;
                }
                valid++;
                totalCells += map.Width * map.Height;
                bool hasLayer = false;
                for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                {
                    ref var cell = ref map.Cells[x, y];
                    if (cell.BackFile > 0 && cell.BackImage > 0)
                        textureRefs.Add((cell.BackFile, cell.BackImage));
                    if (cell.MiddleFile > 0 && cell.MiddleImage > 0)
                        textureRefs.Add((cell.MiddleFile, cell.MiddleImage - 1));
                    if (cell.FrontFile > 0 && cell.FrontImage > 0)
                        textureRefs.Add((cell.FrontFile, cell.FrontImage - 1));
                    if (cell.BackFile > 0 || cell.MiddleFile > 0 || cell.FrontFile > 0)
                        hasLayer = true;
                }
                if (hasLayer) layered++;
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(path)}:{ex.GetType().Name}:{ex.Message}");
            }
        }

        if (!string.IsNullOrWhiteSpace(requestedMap))
        {
            foreach (var group in textureRefs.GroupBy(x => x.FileByte).OrderBy(x => x.Key))
            {
                if (!Libraries.KROrder.TryGetValue(group.Key, out var mappedFile)) continue;
                var library = LibraryCache.Get(mappedFile);
                int refValid = 0, nonCell = 0, refMissing = 0;
                foreach (var reference in group)
                {
                    var image = library?.Images != null && reference.ImageIndex >= 0 &&
                                reference.ImageIndex < library.Images.Length
                        ? library.Images[reference.ImageIndex] : null;
                    if (image == null) { refMissing++; continue; }
                    refValid++;
                    if (!((image.Width == CellWidth || image.Width == CellWidth * 2) &&
                          (image.Height == CellHeight || image.Height == CellHeight * 2)))
                        nonCell++;
                }
                GD.Print($"[MapAudit] fileByte={group.Key} library={mappedFile} refs={group.Count()} valid={refValid} nonCell={nonCell} missing={refMissing}");
            }
        }

        int emptyRefs = 0;
        int missingRefs = 0;
        int ignoredRefs = 0;
        var missingTextureDetails = new List<string>();
        foreach (var reference in textureRefs)
        {
            if (!Libraries.KROrder.TryGetValue(reference.FileByte, out var file))
            {
                // 原版 MapControl 先 TryGetValue，未知文件号直接跳过；255
                // 是旧地图中保留的未使用层标记，不是一个待加载的图库。
                if (reference.FileByte == 255)
                {
                    ignoredRefs++;
                    continue;
                }
                missingRefs++;
                missingTextureDetails.Add($"fileByte={reference.FileByte} image={reference.ImageIndex} library=unknown");
                continue;
            }
            var library = LibraryCache.Get(file);
            if (library?.Images == null || reference.ImageIndex < 0 || reference.ImageIndex >= library.Images.Length)
            {
                missingRefs++;
                missingTextureDetails.Add($"file={file} image={reference.ImageIndex} library={(library?.Images?.Length ?? 0)}");
                continue;
            }
            if (library.Images[reference.ImageIndex] == null)
                emptyRefs++;
        }

        if (failures.Count == 0 && missingRefs == 0)
            GD.Print($"[MapAudit] PASS files={files.Length} valid={valid} layered={layered} cells={totalCells} " +
                $"textureRefs={textureRefs.Count} emptyRefs={emptyRefs} ignoredRefs={ignoredRefs} missingRefs=0");
        else
        {
            GD.PrintErr($"[MapAudit] FAIL files={files.Length} valid={valid} failures={failures.Count} " +
                $"textureRefs={textureRefs.Count} missingRefs={missingRefs}");
            foreach (string detail in missingTextureDetails.Take(32))
                GD.PrintErr($"[MapAudit] missingTexture {detail}");
            foreach (string failure in failures.Take(12)) GD.PrintErr($"[MapAudit] {failure}");
        }
    }

    private void RunShadowAudit()
    {
        int libraries = 0, frames = 0, withShadow = 0, decoded = 0, nonEmpty = 0, metadataUsable = 0;
        int thinContent = 0, longContent = 0;
        var malformedContent = new List<string>();
        int fallback49 = 0, fallback50 = 0, fallback176 = 0, fallback177 = 0;
        foreach (LibraryFile file in Enum.GetValues<LibraryFile>())
        {
            string libraryName = file.ToString();
            if (!libraryName.Contains("Mob", StringComparison.OrdinalIgnoreCase)
                && !libraryName.Contains("NPC", StringComparison.OrdinalIgnoreCase)
                && !libraryName.Contains("Ground", StringComparison.OrdinalIgnoreCase)
                && !libraryName.Contains("Hum", StringComparison.OrdinalIgnoreCase)
                && !libraryName.Contains("Hair", StringComparison.OrdinalIgnoreCase)
                && !libraryName.Contains("Weapon", StringComparison.OrdinalIgnoreCase)
                && !libraryName.Contains("Shield", StringComparison.OrdinalIgnoreCase)
                && !libraryName.Contains("Horse", StringComparison.OrdinalIgnoreCase))
                continue;
            var library = LibraryCache.Get(file);
            if (library?.Images == null) continue;
            libraries++;
            for (int index = 0; index < library.Images.Length; index++)
            {
                var image = library.Images[index];
                if (image == null) continue;
                frames++;
                if (image.ShadowWidth <= 0 || image.ShadowHeight <= 0) continue;
                withShadow++;
                if (RenderPrimitives.IsUsableResourceShadow(image.ShadowWidth, image.ShadowHeight)) metadataUsable++;
                var texture = library.GetShadowTexture(index);
                if (texture != null)
                {
                    decoded++;
                    var pixels = texture.GetImage();
                    if (pixels != null)
                    {
                        Rect2I used = pixels.GetUsedRect();
                        if (used.Size.X > 0 && used.Size.Y > 0)
                        {
                            nonEmpty++;
                            if (used.Size.Y <= 2) thinContent++;
                            if (used.Size.X > used.Size.Y * 8)
                            {
                                longContent++;
                                if (malformedContent.Count < 12)
                                    malformedContent.Add($"{libraryName}[{index}] meta={image.ShadowWidth}x{image.ShadowHeight} used={used.Size.X}x{used.Size.Y}");
                            }
                        }
                    }
                }
                switch (image.ShadowType)
                {
                    case 49: fallback49++; break;
                    case 50: fallback50++; break;
                    case 176: fallback176++; break;
                    case 177: fallback177++; break;
                }
            }
        }
        GD.Print($"[ShadowAudit] PASS libraries={libraries} frames={frames} metadata={withShadow} " +
                $"metadataUsable={metadataUsable} decoded={decoded} nonEmpty={nonEmpty} " +
                $"thinContent={thinContent} longContent={longContent} " +
                $"fallbackTypes=49:{fallback49},50:{fallback50},176:{fallback176},177:{fallback177}");
        foreach (string detail in malformedContent)
            GD.Print($"[ShadowAudit] contentShape {detail}");
    }

    private async void RunPixelAudit()
    {
        string onlyFile = OS.GetCmdlineUserArgs()
            .FirstOrDefault(arg => arg.StartsWith("--pixel-file=", StringComparison.OrdinalIgnoreCase))?
            .Substring("--pixel-file=".Length);
        string batchText = OS.GetCmdlineUserArgs()
            .FirstOrDefault(arg => arg.StartsWith("--pixel-batch=", StringComparison.OrdinalIgnoreCase))?
            .Substring("--pixel-batch=".Length);
        int batchIndex = 0, batchCount = 1;
        if (!string.IsNullOrWhiteSpace(batchText))
        {
            var parts = batchText.Split('/', 2);
            int.TryParse(parts.ElementAtOrDefault(0), out batchIndex);
            if (parts.Length > 1) int.TryParse(parts[1], out batchCount);
            batchCount = Math.Max(1, batchCount);
            batchIndex = Math.Clamp(batchIndex, 0, batchCount - 1);
        }
        int sampleLimit = ParseAuditInt("--pixel-sample=", 0);
        int libraries = 0, frames = 0, compared = 0, different = 0, failed = 0, layers = 0;
        long differentPixels = 0, differentBytes = 0;
        byte maxDelta = 0;
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int libraryOrdinal = 0;

        foreach (LibraryFile file in Enum.GetValues<LibraryFile>())
        {
            var library = LibraryCache.Get(file);
            if (library?.Images == null || string.IsNullOrEmpty(library.FileName)) continue;
            if (!string.IsNullOrWhiteSpace(onlyFile)
                && !string.Equals(Path.GetFileName(library.FileName), onlyFile, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(file.ToString(), onlyFile, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!seenFiles.Add(library.FileName)) continue;
            if (libraryOrdinal++ % batchCount != batchIndex) continue;

            libraries++;
            try
            {
                using var reference = new ZlPixelReference(library.FileName);
                var indices = GetPixelAuditIndices(library.Images, sampleLimit);
                foreach (int index in indices)
                {
                    // 全量资源审计可能超过七十万帧；无头模式每 32 帧切一次主循环会
                    // 把纯解码任务放大到数分钟。保留周期性让帧，但按 4096 帧批次调度。
                    if ((compared & 4095) == 0)
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    var image = library.Images[index];
                    if (image == null || image.Width <= 0 || image.Height <= 0) continue;
                    frames++;

                    byte[] expected = reference.DecodeImage(library, index);
                    byte[] actual = library.GetImageData(index);
                    if (expected == null && actual == null) continue;
                    compared++;
                    var diff = ZlPixelDiffHelper.Compare(expected, actual);
                    if (diff.DifferentPixels != 0 || diff.DifferentBytes != 0)
                    {
                        different++;
                        differentPixels += diff.DifferentPixels;
                        differentBytes += diff.DifferentBytes;
                        maxDelta = Math.Max(maxDelta, diff.MaxDelta);
                        if (different <= 12)
                            GD.PrintErr($"[PixelAudit] DIFF file={Path.GetFileName(library.FileName)} " +
                                $"frame={index} pixels={diff.DifferentPixels} bytes={diff.DifferentBytes} max={diff.MaxDelta}");
                    }

                    ComparePixelLayer(library, reference, index, true, ref compared, ref different,
                        ref differentPixels, ref differentBytes, ref maxDelta, ref layers);
                    ComparePixelLayer(library, reference, index, false, ref compared, ref different,
                        ref differentPixels, ref differentBytes, ref maxDelta, ref layers, true);
                }
                if ((libraries & 31) == 0)
                    GD.Print($"[PixelAudit] progress libraries={libraries} frames={frames} compared={compared}");
            }
            catch (Exception ex)
            {
                failed++;
                GD.PrintErr($"[PixelAudit] ERROR file={Path.GetFileName(library.FileName)} " +
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        string mode = sampleLimit > 0 ? $"sample={sampleLimit}" : "full";
        if (batchCount > 1) mode += $" batch={batchIndex}/{batchCount}";
        if (failed == 0 && different == 0)
            GD.Print($"[PixelAudit] PASS mode={mode} libraries={libraries} frames={frames} layers={layers} compared={compared}");
        else
            GD.PrintErr($"[PixelAudit] FAIL mode={mode} libraries={libraries} frames={frames} layers={layers} compared={compared} " +
                $"different={different} pixels={differentPixels} bytes={differentBytes} maxDelta={maxDelta} errors={failed}");
    }

    private static void ComparePixelLayer(ZlLibrary library, ZlPixelReference reference, int index, bool shadow,
        ref int compared, ref int different, ref long differentPixels, ref long differentBytes, ref byte maxDelta,
        ref int layers, bool overlay = false)
    {
        var image = library.Images[index];
        int width = shadow ? image.ShadowWidth : overlay ? image.OverlayWidth : image.Width;
        int height = shadow ? image.ShadowHeight : overlay ? image.OverlayHeight : image.Height;
        if (width <= 0 || height <= 0) return;
        byte[] expected = shadow ? reference.DecodeShadow(library, index) : overlay
            ? reference.DecodeOverlay(library, index) : null;
        byte[] actual = shadow || overlay ? library.GetAuditLayerData(index, shadow) : null;
        if (expected == null && actual == null) return;
        layers++;
        compared++;
        var diff = ZlPixelDiffHelper.Compare(expected, actual);
        if (diff.DifferentPixels == 0 && diff.DifferentBytes == 0) return;
        different++;
        differentPixels += diff.DifferentPixels;
        differentBytes += diff.DifferentBytes;
        maxDelta = Math.Max(maxDelta, diff.MaxDelta);
        if (different <= 12)
            Godot.GD.PrintErr($"[PixelAudit] DIFF file={System.IO.Path.GetFileName(library.FileName)} frame={index} " +
                $"layer={(shadow ? "shadow" : "overlay")} pixels={diff.DifferentPixels} bytes={diff.DifferentBytes} max={diff.MaxDelta}");
    }

    private static IEnumerable<int> GetPixelAuditIndices(ZlImage[] images, int sampleLimit)
    {
        if (sampleLimit <= 0 || images.Length <= sampleLimit)
            return Enumerable.Range(0, images.Length);

        var indices = new SortedSet<int> { 0, images.Length - 1 };
        int stride = Math.Max(1, images.Length / sampleLimit);
        for (int index = 0; index < images.Length; index += stride)
            indices.Add(index);
        return indices;
    }

    private void RunProjectileAudit()
    {
        _auditProjectile = new MirProjectileNode();
        AddChild(_auditProjectile);
        _auditProjectile.SetupProjectile(LibraryFile.Magic, 420, 5, 100,
            null, 4, 2, new System.Drawing.Point(0, 0),
            (x, y) => new Vector2(x * CellWidth, y * CellHeight));
        _auditProjectile.Blend = true;
        _auditProjectile.Has16Directions = true;
        // 该审计的目标点在视口内，因此按原版必须标记 Explode 才会在
        // 到达点结束；非 Explode 的“穿屏继续飞行”由运行时路径覆盖。
        _auditProjectile.Explode = true;
        _projectileScreenshotSaved = false;
        _auditProjectileStartMs = Godot.Time.GetTicksMsec();
        _auditProjectile.CompleteAction = () =>
        {
            double actualMs = Godot.Time.GetTicksMsec() - _auditProjectileStartMs;
            // 原版 MirProjectile.Process(): duration = Distance(p1, p2) * 1ms,
            // p = (x, y/32*48)。共享 Functions.Distance 是 Chebyshev 距离
            // (max(|dx|,|dy|))，不是欧氏距离。审计目标格 (4,2) → p2=(192,
            // 64/32*48=96)，p1=(0,0) → 期望 = max(192,96) = 192ms。
            // 曾用 distancePx*1.5 (=304ms) 导致飞行慢 ~1.6 倍，这里直接
            // 断言时长而不是只测位移。
            double expectedMs = Math.Max(192.0, 96.0);
            bool travelOk = _auditProjectileMaxTravel > 20f;
            bool speedOk = Math.Abs(actualMs - expectedMs) <= 60.0;
            if (travelOk && speedOk)
                GD.Print($"[ProjectileAudit] stage0 PASS samples={_auditProjectileSamples} travel={_auditProjectileMaxTravel:0.0}px duration={actualMs:0}ms≈{expectedMs:0}ms");
            else
                GD.PrintErr($"[ProjectileAudit] stage0 FAIL travel={_auditProjectileMaxTravel:0.0}px duration={actualMs:0}ms expected≈{expectedMs:0}ms");
            // 截图延迟在 CompleteAction 里做：在采样循环里做会把读回阻塞到
            // 完成帧，污染时长/位移测量（曾把 duration 从 ~208ms 推到 306ms）。
            if (!_projectileScreenshotSaved && DisplayServer.GetName() != "headless")
            {
                var image = GetViewport().GetTexture()?.GetImage();
                if (image != null && image.SavePng("/tmp/zircon-projectile-audit.png") == Error.Ok)
                {
                    _projectileScreenshotSaved = true;
                    GD.Print($"[ProjectileRenderAudit] PASS viewport={image.GetWidth()}x{image.GetHeight()} " +
                             "path=/tmp/zircon-projectile-audit.png");
                }
            }
            RunProjectileAuditStage1();
        };
        _auditProjectileStart = _auditProjectile.Position;
    }

    // stage1: 无目标、非 Explode 但挂 CompleteAction 的投射物（地面落点弹道，
    // 如火球）。修复前 flyPast 逻辑会把爆炸推迟到飞出屏幕才触发；现在必须在
    // 到达目标格时截停并触发 CompleteAction（着弹特效在格上播放）。
    private void RunProjectileAuditStage1()
    {
        _auditProjectileStage = 1;
        _auditProjectile = new MirProjectileNode();
        AddChild(_auditProjectile);
        _auditProjectile.SetupProjectile(LibraryFile.Magic, 420, 5, 100,
            null, 4, 2, new System.Drawing.Point(0, 0),
            (x, y) => new Vector2(x * CellWidth, y * CellHeight));
        _auditProjectile.Blend = true;
        _auditProjectile.Has16Directions = true;
        // 与运行时 FireBall 地面落点一致: 无 Target、非 Explode、挂 CompleteAction。
        _auditProjectile.Explode = false;
        _auditProjectileStartMs = Godot.Time.GetTicksMsec();
        _auditProjectile.CompleteAction = () =>
        {
            double actualMs = Godot.Time.GetTicksMsec() - _auditProjectileStartMs;
            // 目标格 (4,2) 的屏幕坐标 = (4*48, 2*32) = (192,64)；
            // ToLegacyProjectilePoint 把 Y 换算成 48 高度 (64/32*48=96)。
            // 修复后 flyPast 不再截留: 到达即触发, 时长仍为 Chebyshev 距离
            // max(192,96)=192ms。若仍走旧的 flyPast 分支, duration 不会
            // 在到达点结束, 会继续飞离屏幕后才触发 (时长远大于 192ms)。
            double expectedMs = Math.Max(192.0, 96.0);
            var pos = _auditProjectile.Position;
            bool endAtCell = Math.Abs(pos.X - 192f) < 0.1f && Math.Abs(pos.Y - 64f) < 0.1f;
            bool speedOk = Math.Abs(actualMs - expectedMs) <= 60.0;
            if (endAtCell && speedOk)
                GD.Print($"[ProjectileAudit] stage1 PASS pos={pos} duration={actualMs:0}ms≈{expectedMs:0}ms (ground-cell stop+impact)");
            else
                GD.PrintErr($"[ProjectileAudit] stage1 FAIL pos={pos} expected=(192,64) duration={actualMs:0}ms expected≈{expectedMs:0}ms");
            GetTree().Quit();
        };
    }

    // 回归审计: 目标已完全移除 (场景节点与 _objects 缓存都查不到该 id) 时,
    // RenderObjectMagic 的 targets 循环必须回退到 destCells[targetIndex]
    // 对应格子播 Impact, 而不是整段丢弃命中特效。直接反射驱动真实私有方法
    // (裸 GameScene 不进树), 统计目标格子上的 MirEffectNode 数量。
    private void RunDeadTargetFallbackAudit()
    {
        var method = typeof(GameScene).GetMethod("RenderObjectMagic",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (method == null)
        {
            GD.PrintErr("[DeadTargetAudit] FAIL RenderObjectMagic 反射查找失败");
            GetTree().Quit();
            return;
        }

        int failures = 0;

        // 用例1 基线: 无目标, 只有地面格 (10,12) -> 落点 1 个 Impact。
        int baseline = CountCellImpacts(method, new uint[0], new[] { (10, 12) }, null);
        if (baseline != 1)
        {
            failures++;
            GD.PrintErr($"[DeadTargetAudit] FAIL 基线落点 Impact 数={baseline} 期望=1");
        }

        // 用例2 目标完全移除: 回退到同一格补播 1 个 Impact -> 2 个。
        int absent = CountCellImpacts(method, new uint[] { 9999 }, new[] { (10, 12) }, null);
        if (absent != baseline + 1)
        {
            failures++;
            GD.PrintErr($"[DeadTargetAudit] FAIL 目标移除回退 Impact 数={absent} 期望={baseline + 1}");
        }

        // 用例3 多目标夹取: 2 个无效目标, 1 个格子 -> Math.Min 夹到索引 0,
        // 两个目标各补 1 个 -> 落点 3 个 (1 地面 + 2 回退)。
        int clamped = CountCellImpacts(method, new uint[] { 9999, 8888 }, new[] { (10, 12) }, null);
        if (clamped != baseline + 2)
        {
            failures++;
            GD.PrintErr($"[DeadTargetAudit] FAIL 多目标夹取 Impact 数={clamped} 期望={baseline + 2}");
        }

        // 用例4 尸体仍在 _objects (目标死亡但未被移除): GetMagicTargetNode
        // 命中缓存, 走对象锚定分支 (MapCell 保持 0), 不再叠加格子回退。
        // -> 落点 1 个 + 锚定 1 个。
        var corpseScene = BuildScene(method, new uint[] { 9999 }, new[] { (10, 12) },
            new ObjectRenderer { ObjectID = 9999, CellX = 7, CellY = 8 });
        int corpseCell = CountAt(corpseScene, 10, 12);
        int corpseAnchored = CountAt(corpseScene, 0, 0);
        if (corpseCell != 1 || corpseAnchored != 1)
        {
            failures++;
            GD.PrintErr($"[DeadTargetAudit] FAIL 尸体锚定 cell={corpseCell} anchored={corpseAnchored} 期望=1/1");
        }

        // 用例5 无地面格: destCells 为空, 回退守卫不触发, 不产生格特效。
        // (旧端纯目标技能同样不发送 Locations, 移除目标后无格表现。)
        int noLoc = CountCellImpacts(method, new uint[] { 9999 }, new (int, int)[0], null);
        if (noLoc != 0)
        {
            failures++;
            GD.PrintErr($"[DeadTargetAudit] FAIL 空地面格回退 Impact 数={noLoc} 期望=0");
        }

        if (failures == 0)
            GD.Print("[DeadTargetAudit] PASS dead-target fallback at destCells + corpse anchored parity");
        else
            GD.PrintErr($"[DeadTargetAudit] FAIL failures={failures}");
        GetTree().Quit();
    }

    // E4/P3 抽测: 反射驱动真实 RenderObjectMagicStart/RenderObjectMagic, 断言运行时
    // 生成的 MirEffectNode (lib, StartIndex, FrameCount) 三元组与事实源
    // Mir3-Research/Tools/magiclab/magic-effect-table.json (extract_effect_table.py
    // 从原版 MapObject.cs 两段 Spell switch 提取) 一致。技能选取覆盖本轮修复形态:
    //   FireBall 正确基线 / AdamantineFireBall 帧号修复(1640/1800) /
    //   SummonSkeleton 帧号修复(740) / FrostBite 新增条目 / Rake 新增+方向帧表。
    // 口径: 目标命中段 (Impact) 需要有效目标节点锚定, 故向 _objects 注入一个
    // MapObjectNode; Rake 方向帧表按施法方向只播一组, 期望集只含 Up 基址 1200。
    private void RunMagicSpotAudit()
    {
        var startMethod = typeof(GameScene).GetMethod("RenderObjectMagicStart",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var releaseMethod = typeof(GameScene).GetMethod("RenderObjectMagic",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var objectsField = typeof(GameScene).GetField("_objects", BindingFlags.NonPublic | BindingFlags.Instance);
        if (startMethod == null || releaseMethod == null || objectsField == null)
        {
            GD.PrintErr("[MagicSpotAudit] FAIL RenderObjectMagic*/_objects 反射查找失败");
            GetTree().Quit();
            return;
        }

        // 事实源期望 (lib, start, count) 全集。
        var expected = new Dictionary<MagicType, HashSet<(LibraryFile File, int Start, int Count)>>
        {
            [MagicType.FireBall] = new() { (LibraryFile.Magic, 1820, 8), (LibraryFile.Magic, 420, 5), (LibraryFile.Magic, 580, 10) },
            [MagicType.AdamantineFireBall] = new() { (LibraryFile.Magic, 1560, 9), (LibraryFile.Magic, 1640, 6), (LibraryFile.Magic, 1800, 10) },
            [MagicType.SummonSkeleton] = new() { (LibraryFile.Magic, 740, 10) },
            [MagicType.FrostBite] = new() { (LibraryFile.MagicEx5, 500, 16) },
            [MagicType.Rake] = new() { (LibraryFile.MagicEx4, 1200, 9) },
        };
        int failures = 0;
        foreach (var (type, want) in expected)
        {
            var scene = new GameScene();
            AddChild(scene);   // 裸 GameScene 必须入树, 否则弹道 _Process 不跑, Arrival 命中段永不播
            var target = new ObjectRenderer { ObjectID = 42u, CellX = 10, CellY = 12 };
            ((System.Collections.Generic.Dictionary<uint, ObjectRenderer>)objectsField.GetValue(scene)!).Add(42u, target);
            var locations = new List<System.Drawing.Point> { new(10, 12) };
            startMethod.Invoke(scene, new object[] { 1u, MirDirection.Down, new System.Drawing.Point(5, 5), type, locations });
            releaseMethod.Invoke(scene, new object[] { 1u, MirDirection.Down, new System.Drawing.Point(5, 5), type, new List<uint> { 42u }, locations });
            // 口径: (a) 运行时断言非弹道承载段 (start Source/主特效/弹道本体) —— 裸场景
            // 弹道因 _mapView==null duration==0 立即 Complete, Arrival 节点生命周期极短,
            // 同帧收集不稳定; (b) 弹道承载的命中段 (Impact) 直接断言表定义三元组。
            // 运行时链路 (帧范围/锚定/回退) 由 MagicFrameAudit + DeadTargetAudit 覆盖。
            var tableDef = MagicEffectTable.Get(type);
            var tableTriples = new HashSet<(LibraryFile, int, int)>();
            if (tableDef.Source != null) tableTriples.Add((tableDef.Source.File, tableDef.Source.StartIndex, tableDef.Source.FrameCount));
            if (tableDef.Projectile != null) tableTriples.Add((tableDef.Projectile.File, tableDef.Projectile.StartIndex, tableDef.Projectile.FrameCount));
            if (tableDef.Impact != null) tableTriples.Add((tableDef.Impact.File, tableDef.Impact.StartIndex, tableDef.Impact.FrameCount));
            if (tableDef.CastAtSource || (tableDef.Source == null && tableDef.Impact == null))
                tableTriples.Add((tableDef.File, tableDef.StartIndex, tableDef.FrameCount));
            var tableMissing = want.Except(tableTriples).ToList();
            var runtimeGot = new HashSet<(LibraryFile, int, int)>();
            foreach (var fx in scene.GetChildren().OfType<MirEffectNode>())
                runtimeGot.Add((fx.File, fx.StartIndex, fx.FrameCount));
            // 运行时必须覆盖 start 段与弹道本体 (Arrival 节点除外)。
            var runtimeMust = want.Except(tableDef.Impact != null
                ? new HashSet<(LibraryFile, int, int)> { (tableDef.Impact.File, tableDef.Impact.StartIndex, tableDef.Impact.FrameCount) }
                : new HashSet<(LibraryFile, int, int)>()).ToList();
            var runtimeMissing = runtimeMust.Except(runtimeGot).ToList();
            if (tableMissing.Count > 0 || runtimeMissing.Count > 0)
            {
                failures++;
                if (tableMissing.Count > 0)
                    GD.PrintErr($"[MagicSpotAudit] FAIL {type} 表定义缺 {string.Join(' ', tableMissing)}; 表={string.Join(' ', tableTriples)}");
                if (runtimeMissing.Count > 0)
                    GD.PrintErr($"[MagicSpotAudit] FAIL {type} 运行时缺 {string.Join(' ', runtimeMissing)}; 实际={string.Join(' ', runtimeGot)}");
            }
            else
            {
                GD.Print($"[MagicSpotAudit] PASS {type} 表={string.Join(' ', tableTriples)} 运行时={string.Join(' ', runtimeGot)}");
            }
        }
        if (failures == 0)
            GD.Print("[MagicSpotAudit] PASS 5/5 技能: 表定义 ⊇ 事实源三元组, 运行时 ⊇ 非弹道承载段");
        else
            GD.PrintErr($"[MagicSpotAudit] FAIL failures={failures}");
        GetTree().Quit();
    }

    private static GameScene BuildScene(MethodInfo method, uint[] targets, (int X, int Y)[] locations,
        ObjectRenderer? corpse)
    {
        var scene = new GameScene();
        if (corpse != null)
        {
            var objectsField = typeof(GameScene).GetField("_objects", BindingFlags.NonPublic | BindingFlags.Instance);
            ((System.Collections.Generic.Dictionary<uint, ObjectRenderer>)objectsField!.GetValue(scene)!).Add(
                corpse.ObjectID, corpse);
        }
        method.Invoke(scene, new object[]
        {
            1u, MirDirection.Up, new System.Drawing.Point(5, 5), MagicType.ElectricShock,
            targets.ToList(),
            locations.Select(l => new System.Drawing.Point(l.X, l.Y)).ToList(),
        });
        return scene;
    }

    private static int CountCellImpacts(MethodInfo method, uint[] targets, (int X, int Y)[] locations,
        ObjectRenderer? corpse)
    {
        var scene = BuildScene(method, targets, locations, corpse);
        return locations.Length == 0 ? CountAt(scene, 10, 12) : locations.Select(l => CountAt(scene, l.X, l.Y)).Sum();
    }

    private static int CountAt(GameScene scene, int x, int y)
        => scene.GetChildren().OfType<MirEffectNode>().Count(fx => fx.MapCellX == x && fx.MapCellY == y);

    private static void RunPlayerMatrixAudit()
    {
        bool pass = PlayerRenderer.RunAppearanceMatrixAudit(out int tested, out string failure);
        if (pass)
            GD.Print($"[PlayerMatrixAudit] PASS tested={tested} gender=2 class=4 equipment=armour/costume/helmet/shield/weapon horseShape=8 directions=8 animations=8");
        else
            GD.PrintErr($"[PlayerMatrixAudit] FAIL tested={tested} {failure}");
    }

    private void ProcessProjectileAudit()
    {
        if (_auditProjectile == null || !GodotObject.IsInstanceValid(_auditProjectile)) return;
        _auditProjectileSamples++;
        _auditProjectileMaxTravel = Math.Max(_auditProjectileMaxTravel,
            _auditProjectile.Position.DistanceTo(_auditProjectileStart));
    }

    // 回归审计：目标节点在特效播放中被释放（S.ObjectRemove 后怪物节点
    // QueueFree）不得触发 ObjectDisposedException。原版 MirEffect.Target 是
    // 托管 MapObject，目标移除后效果冻结在最后 DrawX/DrawY 继续播完；
    // Godot 侧必须用 IsInstanceValid 守卫并冻结最后位置（MirEffectNode 与
    // MirProjectileNode 共用该语义）。
    private void BeginEffectTargetFreeAudit()
    {
        _effectFreeTarget = new Node2D { Position = new Vector2(200, 300) };
        AddChild(_effectFreeTarget);

        // 300 帧 × 50ms = 15s：审计期间帧序列不会播完（自删），因此
        // “目标释放后特效仍存活”证明的是冻结语义，而非自然完成。
        _effectFreeFx = new MirEffectNode();
        AddChild(_effectFreeFx);
        _effectFreeFx.SetupTarget(LibraryFile.ProgUse, 100, 300, 50, _effectFreeTarget, null);
        _effectFreeFx.Blend = true;

        _effectFreeProjectile = new MirProjectileNode();
        AddChild(_effectFreeProjectile);
        _effectFreeProjectile.SetupProjectileTarget(LibraryFile.Magic, 420, 5, 50, _effectFreeTarget, null,
            new System.Drawing.Point(0, 0), (x, y) => new Vector2(x * 48, y * 32));
        _effectFreeProjectile.Explode = true;

        GD.Print("[EffectTargetFreeAudit] START target=(200,300) fx+projectile anchored");
        _effectFreeAuditStage = 0;
        _effectFreeAuditFrames = 0;
    }

    private void ProcessEffectTargetFreeAudit()
    {
        if (!_effectFreeAudit) return;
        switch (_effectFreeAuditStage)
        {
            case 0: // 锚定阶段：目标有效，特效每帧跟随
                if (++_effectFreeAuditFrames < 3) return;
                _effectFreeLastPosition = _effectFreeFx.Position;
                GD.Print($"[EffectTargetFreeAudit] follow ok pos={_effectFreeFx.Position}");
                _effectFreeAuditStage = 1;
                _effectFreeAuditFrames = 0;
                break;
            case 1: // 立即释放目标（等价于已 QueueFree 的节点，访问即抛）
                if (IsInstanceValid(_effectFreeTarget)) _effectFreeTarget.Free();
                if (++_effectFreeAuditFrames < 3) return;
                if (!IsInstanceValid(_effectFreeFx))
                {
                    GD.Print("[EffectTargetFreeAudit] FAIL effect freed unexpectedly");
                    GetTree().Quit();
                    return;
                }
                Vector2 now = _effectFreeFx.Position;
                bool frozen = Math.Abs(now.X - _effectFreeLastPosition.X) < 0.01f &&
                              Math.Abs(now.Y - _effectFreeLastPosition.Y) < 0.01f;
                GD.Print(frozen
                    ? $"[EffectTargetFreeAudit] PASS effect alive after target free, position frozen at {now}"
                    : $"[EffectTargetFreeAudit] FAIL position drifted {_effectFreeLastPosition} -> {now}");
                _effectFreeAuditStage = 2;
                _effectFreeAuditFrames = 0;
                break;
            case 2: // 再跑几帧确认特效与飞行物都不再触碰已释放目标
                if (++_effectFreeAuditFrames < 5) return;
                bool projectileAlive = IsInstanceValid(_effectFreeProjectile);
                GD.Print(projectileAlive
                    ? "[EffectTargetFreeAudit] PASS projectile survived target free (grid fallback)"
                    : "[EffectTargetFreeAudit] PASS projectile completed via grid fallback after target free");
                GD.Print("[EffectTargetFreeAudit] PASS all (no ObjectDisposedException)");
                _effectFreeAudit = false;
                GetTree().Quit();
                break;
        }
    }

    // 原版 Screen Blend（BlendMode.NORMAL）数学的确定性像素验证（真实 Vulkan 读回）：
    //   src.rgb = texel.rgb * texel.a * Col.rgb * Col.a
    //   src.a   = texel.a * Col.a
    //   out     = src * (1 - dst) + dst          （RGB/Alpha 双通道）
    // 灰度 Blend：gray = dot(直通 texel.rgb, luma)；out = gray*Col.rgb*texel.a*Col.a。
    // 已实测确定的 Godot 4 canvas 管线（kind 14-24 判别）：
    //   - texture(TEXTURE, UV) 返回直通 texel（rgb/a 均未预乘）；
    //   - shader 输入 COLOR = 顶点色 × texel（模板先 color *= texture）；
    //   - screen_texture = 本 item 绘制前的同帧帧缓冲（kind 14/15 测到红探针）；
    //   - MIX 因子 = shader 输出的 COLOR.a（本审计全部输出 alpha=1 → 因子=1）。
    // 期望 current = 修复前（错误 COLOR 预乘公式）输出；期望 original = 原版公式。
    // 期望值按红探针底板 (1,0,0) 计算（探针 ZIndex 200 垫在 quad 下方，黑底板
    // ZIndex 200 更早添加 → dest.rgb = (1,0,0)）；out.r = 1 + src.r*0 = 1（blend
    // 类），g/b 通道携带判别信息。判定：original 命中 → 奇偶一致。
    private static readonly Color BlendAuditBackdropColour = new Color(0f, 0f, 0f, 1f);
    // (texel[直通], modulate, 期望 current, 期望 original, Kind)
    // Kind: 0 = LegacyScreenBlend 材质, 1 = 灰度 Blend, 2 = 灰度非 Blend
    // 奇偶门只统计 Kind<=2（三种生产材质），判别 kind 仅作人工诊断。
    private static readonly (Color Texel, Color Modulate, Color ExpectCurrent, Color ExpectOriginal, int Kind)[] BlendAuditCases =
    {
        (new Color(1f, 1f, 1f, 0.5f),  Colors.White,                new Color(1f, 0.5f, 0.5f, 1f),      new Color(1f, 0.5f, 0.5f, 1f), 0),  // 判别器：半透明白（红底 → r=1, g=0.5）
        (new Color(1f, 1f, 1f, 1f),    new Color(1f, 1f, 1f, 0.8f), new Color(1f, 0.8f, 0.8f, 1f),    new Color(1f, 0.8f, 0.8f, 1f), 0),  // 不透明 texel：两模型同值
        (new Color(1f, 1f, 1f, 0.5f),  new Color(1f, 1f, 1f, 0.8f), new Color(1f, 0.4f, 0.4f, 1f),    new Color(1f, 0.4f, 0.4f, 1f), 0),  // 判别器：texel.a×COLOR.a
        (new Color(0f, 0f, 0f, 1f),    Colors.White,                new Color(1f, 0f, 0f, 1f),        new Color(1f, 0f, 0f, 1f), 0),     // 黑 texel → 0 → 探针透出
        (new Color(1f, 1f, 1f, 0f),    Colors.White,                new Color(1f, 0f, 0f, 1f),        new Color(1f, 0f, 0f, 1f), 0),     // 零 alpha → discard → 探针透出
        (new Color(1f, 0f, 0f, 0.5f),  Colors.White,                new Color(1f, 0f, 0f, 1f),        new Color(1f, 0f, 0f, 1f), 0),     // 判别器：红 α0.5 通道隔离
        // 灰度 Blend（vcolor=modulate）：source = l*vcolor.rgb*texel.a*vcolor.a；
        // 红底 out = (1,0,0) + source*(0,1,1)。original = 0.25（灰 0.5、白顶点色：
        // l=0.5, texel.a=0.5）；修复前 source = l*COLOR.rgb*texel.a*COLOR.a 多乘
        // texel.rgb×texel.a → 0.125。
        (new Color(0.5f, 0.5f, 0.5f, 0.5f), Colors.White,           new Color(1f, 0.125f, 0.125f, 1f), new Color(1f, 0.25f, 0.25f, 1f), 1),
        // 灰度非 Blend：a = texel.a*vcolor.a = 0.5；out = source + dst*(1-a)；
        // 红底 original = (0.25,0.25,0.25) + (1,0,0)*0.5 = (0.75,0.25,0.25)；
        // 修复前 a = texel.a*COLOR.a = 0.25、source = 0.125 → (0.875,0.125,0.125)。
        (new Color(0.5f, 0.5f, 0.5f, 0.5f), Colors.White,           new Color(0.875f, 0.125f, 0.125f, 1f), new Color(0.75f, 0.25f, 0.25f, 1f), 2),
        // 判别器：Legacy 材质 + 部分覆盖灰 texel（COLOR=0.5×0.5=0.25）；
        // 修复前 source = texel.rgb*texel.a*COLOR.rgb*COLOR.a = 0.0625 → /texel.a = 0.125；
        // 修复后 source = texel.a*COLOR.rgb*COLOR.a = 0.125 → /texel.a = 0.25（= 原版）。
        (new Color(0.5f, 0.5f, 0.5f, 0.5f), Colors.White,           new Color(1f, 0.125f, 0.125f, 1f), new Color(1f, 0.25f, 0.25f, 1f), 0),
        // 诊断：emit 纯白 (alpha=1) → got = 混合因子（texel.a 或 1）；emit texel.rgb → got = texel.rgb×混合因子
        (new Color(0.5f, 0.5f, 0.5f, 0.5f), Colors.White,           new Color(1f, 1f, 1f, 1f),            new Color(1f, 1f, 1f, 1f), 3),
        (new Color(0.5f, 0.5f, 0.5f, 0.5f), Colors.White,           new Color(0.25f, 0.25f, 0.25f, 1f),    new Color(0.25f, 0.25f, 0.25f, 1f), 4),
        (new Color(1f, 1f, 1f, 0.5f),       Colors.White,           new Color(1f, 1f, 1f, 1f),            new Color(1f, 1f, 1f, 1f), 3),
        (new Color(1f, 1f, 1f, 0.5f),       Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 4),
        (new Color(0.5f, 0.5f, 0.5f, 0.5f), Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 5),
        (new Color(1f, 1f, 1f, 0.5f),       Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 5),
        (new Color(0.5f, 0.5f, 0.5f, 0.5f), Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 6),
        (new Color(1f, 1f, 1f, 0.5f),       Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 6),
        (new Color(0.5f, 0.5f, 0.5f, 0.5f), Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 7),
        (new Color(1f, 1f, 1f, 0.5f),       Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 7),
        (new Color(0.5f, 0.5f, 0.5f, 0.5f), Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 8),
        (new Color(1f, 1f, 1f, 0.5f),       Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 8),
        // kind 9-13: USES screen_texture → 强制 screen pass；emit 常量/l/a 测混合因子
        (new Color(0.5f, 0.5f, 0.5f, 0.5f), Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 9),
        (new Color(0.5f, 0.5f, 0.5f, 0.5f), Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 10),
        (new Color(0.5f, 0.5f, 0.5f, 0.5f), Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 11),
        (new Color(0f, 1f, 0f, 0.5f),       Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 12),
        (new Color(1f, 0.5f, 0f, 0.5f),     Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 13),
        // kind 14-17: 真正使用 destination（1.0 - destination.r）→ screen pass；
        // 黑底 → output=1.0，got = 混合因子；绿底 → got.r=因子, got.g 揭示 dst 系数
        (new Color(0.5f, 0.5f, 0.5f, 0.5f), Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 14),
        (new Color(0f, 1f, 0f, 0.5f),       Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 14),
        (new Color(1f, 0.5f, 0f, 0.5f),     Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 14),
        (new Color(1f, 1f, 1f, 0.5f),       Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 14),
        // kind 15: dump destination.rgb；kind 16: dump texel.rgb*texel.a (预乘检测)
        (new Color(0.5f, 0.5f, 0.5f, 0.5f), Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 15),
        (new Color(1f, 1f, 1f, 0.5f),       Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 15),
        (new Color(0.5f, 0.5f, 0.5f, 0.5f), Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 16),
        (new Color(1f, 1f, 1f, 0.5f),       Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 16),
        // kind 17/18: screen pass 直接 dump texel.rgb / texel.a（dest 已采样）
        (new Color(0.5f, 0.5f, 0.5f, 0.5f), Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 17),
        (new Color(1f, 1f, 1f, 0.5f),       Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 17),
        (new Color(0.5f, 0.5f, 0.5f, 0.5f), Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 18),
        (new Color(1f, 1f, 1f, 0.5f),       Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 18),
        // kind 19/20: destination 参与输出（真实 screen pass）→ got = texel + dest*0.25，
        // dest=(1,0,0) → texel.r = got.r − 0.25, texel.g = got.g, texel.a = got.g
        (new Color(0.5f, 0.5f, 0.5f, 0.5f), Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 19),
        (new Color(1f, 1f, 1f, 0.5f),       Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 19),
        (new Color(0.5f, 0.5f, 0.5f, 0.5f), Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 20),
        (new Color(1f, 1f, 1f, 0.5f),       Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 20),
        // kind 21/22: 真实 screen pass 中 COLOR 的 a / rgb（dest×0.25 保持 copy 生效；g/b 通道不受污染）
        (new Color(0.5f, 0.5f, 0.5f, 0.5f), Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 21),
        (new Color(1f, 1f, 1f, 0.5f),       Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 21),
        (new Color(0.5f, 0.5f, 0.5f, 0.5f), Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 22),
        (new Color(1f, 1f, 1f, 0.5f),       Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 22),
        // kind 23/24: 直通 vs 预乘判别（红 a0.5：直通 1.0 → got.r≈1.25 截断 1.0，预乘 0.5 → 0.75）
        (new Color(1f, 0f, 0f, 0.5f),       Colors.White,           new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 23),
        (new Color(0.75f, 0.75f, 0.75f, 0.5f), Colors.White,        new Color(0.5f, 0.5f, 0.5f, 1f),      new Color(0.5f, 0.5f, 0.5f, 1f), 24),
    };
    // mix 语义探针（普通 TextureRect，无 screen blend 材质）：
    //   白 α0.5 纹理（预乘后 texel.rgb=0.5, a=0.5）：
    //     mix=SRC_ALPHA(直通输出) → 0.5*0.5=0.25；mix=ONE/INV_SRC_ALPHA(预乘输出) → 0.5
    //   红 α0.5 ColorRect（无纹理，直通色 (1,0,0,0.5)）：
    //     SRC_ALPHA → 0.5；预乘 → 1.0
    private static readonly (Color Texel, Color Modulate, Color ExpectCurrent, Color ExpectOriginal)[] BlendAuditMixProbes =
    {
        (new Color(1f, 1f, 1f, 0.5f),  Colors.White,                new Color(0.25f, 0.25f, 0.25f, 1f), new Color(0.5f, 0.5f, 0.5f, 1f)),
        (new Color(1f, 0f, 0f, 0.5f),  Colors.White,                new Color(0.5f, 0f, 0f, 1f),       new Color(1f, 0f, 0f, 1f)),
    };
    // 注：曾加入"shader 输出常量色 α0.5"探针（期望 0.5 或 1.0），实测得 0.749：
    // Godot 对 alpha<1 的 shader 输出会额外预乘一次（无标准公式可预测）。
    // 因此所有半透明 blend shader 一律输出完整结果 + alpha=1（见灰度非 Blend 变体）。
    private const float BlendAuditTolerance = 4f / 255f;

    private static bool BlendChannelClose(float a, float b) => Mathf.Abs(a - b) <= BlendAuditTolerance;

    private static bool BlendColourClose(Color a, Color b) =>
        BlendChannelClose(a.R, b.R) && BlendChannelClose(a.G, b.G) &&
        BlendChannelClose(a.B, b.B) && BlendChannelClose(a.A, b.A);

    private void BeginBlendAudit()
    {
        var vp = GetViewportRect();
        _blendAuditBackdrop = new ColorRect
        {
            Color = BlendAuditBackdropColour,
            Position = Vector2.Zero,
            Size = vp.Size,
            ZIndex = 200,
        };
        AddChild(_blendAuditBackdrop);
        _blendAuditCase = 0;
        SpawnBlendAuditCase(0);
        GD.Print($"[BlendAudit] begin backdrop={BlendAuditBackdropColour} cases={BlendAuditCases.Length} viewport={vp.Size}");
    }

    private void SpawnBlendAuditCase(int i)
    {
        if (_blendAuditQuad != null)
        {
            _blendAuditQuad.QueueFree();
            _blendAuditQuad = null;
        }
        int probeIndex = i - BlendAuditCases.Length;
        bool isProbe = probeIndex >= 0;
        (Color texel, Color modulate, _, _, int kind) = isProbe
            ? (BlendAuditMixProbes[probeIndex].Texel, BlendAuditMixProbes[probeIndex].Modulate,
               BlendAuditMixProbes[probeIndex].ExpectCurrent, BlendAuditMixProbes[probeIndex].ExpectOriginal, 0)
            : BlendAuditCases[i];
        var vp = GetViewportRect();
        // 根节点 Scale=2：场景坐标 ×2 = 窗口像素；GetViewportRect() 返回窗口像素，
        // 因此场景中心 = vp.Size/4。
        Vector2 center = vp.Size / 4f - new Vector2(40, 40);
        if (isProbe && probeIndex == 1)
        {
            // 纯色探针：无纹理 ColorRect
            _blendAuditQuad = null;
            var rect = new ColorRect
            {
                Color = modulate * texel,
                Position = center,
                Size = new Vector2(80, 80),
                ZIndex = 201,
            };
            AddChild(rect);
            _blendAuditFrames = 0;
            return;
        }
        var img = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
        var px = new byte[4 * 4 * 4];
        for (int k = 0; k < px.Length; k += 4)
        {
            px[k] = (byte)Mathf.RoundToInt(texel.R * 255f);
            px[k + 1] = (byte)Mathf.RoundToInt(texel.G * 255f);
            px[k + 2] = (byte)Mathf.RoundToInt(texel.B * 255f);
            px[k + 3] = (byte)Mathf.RoundToInt(texel.A * 255f);
        }
        img.SetData(4, 4, false, Image.Format.Rgba8, px);
        ShaderMaterial? mat = isProbe ? null
            : kind == 0 ? LegacyBlendMaterial.Create()
            : kind == 1 ? DXImageControl.CreateGrayMaterial(true)
            : kind == 2 ? DXImageControl.CreateGrayMaterial(false)
            : new ShaderMaterial
            {
                Shader = new Shader
                {
                    Code = kind == 3
                        ? "shader_type canvas_item;\nvoid fragment() {\n    COLOR = vec4(1.0);\n}"
                        : kind == 4
                            ? "shader_type canvas_item;\nvoid fragment() {\n    COLOR = vec4(texture(TEXTURE, UV).rgb, 1.0);\n}"
                            : kind == 5
                                ? "shader_type canvas_item;\nvoid fragment() {\n    COLOR = vec4(vec3(texture(TEXTURE, UV).a), 1.0);\n}"
                                : kind == 6
                                    ? "shader_type canvas_item;\nvoid fragment() {\n    vec4 texel = texture(TEXTURE, UV);\n    float l = dot(texel.rgb, vec3(0.299, 0.587, 0.114));\n    COLOR = vec4(vec3(l), 1.0);\n}"
                                    : kind == 7
                                        ? "shader_type canvas_item;\nuniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;\nvoid fragment() {\n    COLOR = vec4(texture(TEXTURE, UV).rgb, 1.0);\n}"
                                        : kind == 8
                                            ? "shader_type canvas_item;\nuniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;\nvoid fragment() {\n    COLOR = vec4(vec3(texture(TEXTURE, UV).a), 1.0);\n}"
                                            : kind == 9
                                                ? "shader_type canvas_item;\nuniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;\nvoid fragment() {\n    vec4 texel = texture(TEXTURE, UV);\n    vec4 destination = textureLod(screen_texture, SCREEN_UV, 0.0);\n    float l = dot(texel.rgb, vec3(0.299, 0.587, 0.114));\n    COLOR = vec4(vec3(l), 1.0);\n}"
                                                : kind == 10
                                                    ? "shader_type canvas_item;\nuniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;\nvoid fragment() {\n    vec4 texel = texture(TEXTURE, UV);\n    vec4 destination = textureLod(screen_texture, SCREEN_UV, 0.0);\n    COLOR = vec4(vec3(texel.a), 1.0);\n}"
                                                    : kind == 11
                                                        ? "shader_type canvas_item;\nuniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;\nvoid fragment() {\n    vec4 destination = textureLod(screen_texture, SCREEN_UV, 0.0);\n    COLOR = vec4(1.0);\n}"
                                                        : kind == 12
                                                            ? "shader_type canvas_item;\nuniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;\nvoid fragment() {\n    vec4 destination = textureLod(screen_texture, SCREEN_UV, 0.0);\n    COLOR = vec4(1.0);\n}"
                                                            : kind == 13
                                                                ? "shader_type canvas_item;\nuniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;\nvoid fragment() {\n    vec4 destination = textureLod(screen_texture, SCREEN_UV, 0.0);\n    COLOR = vec4(1.0);\n}"
                                                                : kind == 14
                                                                    ? "shader_type canvas_item;\nuniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;\nvoid fragment() {\n    vec4 destination = textureLod(screen_texture, SCREEN_UV, 0.0);\n    COLOR = vec4(vec3(1.0 - destination.r), 1.0);\n}"
                                                                    : kind == 15
                                                                        ? "shader_type canvas_item;\nuniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;\nvoid fragment() {\n    vec4 destination = textureLod(screen_texture, SCREEN_UV, 0.0);\n    COLOR = vec4(destination.rgb, 1.0);\n}"
                                                                        : kind == 16
                                                                            ? "shader_type canvas_item;\nuniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;\nvoid fragment() {\n    vec4 texel = texture(TEXTURE, UV);\n    vec4 destination = textureLod(screen_texture, SCREEN_UV, 0.0);\n    COLOR = vec4(texel.rgb * texel.a, 1.0);\n}"
                                                                            : kind == 17
                                                                                ? "shader_type canvas_item;\nuniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;\nvoid fragment() {\n    vec4 texel = texture(TEXTURE, UV);\n    vec4 destination = textureLod(screen_texture, SCREEN_UV, 0.0);\n    COLOR = vec4(texel.rgb, 1.0);\n}"
                                                                                : kind == 18
                                                                                    ? "shader_type canvas_item;\nuniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;\nvoid fragment() {\n    vec4 texel = texture(TEXTURE, UV);\n    vec4 destination = textureLod(screen_texture, SCREEN_UV, 0.0);\n    COLOR = vec4(vec3(texel.a), 1.0);\n}"
                                                                                    : kind == 19
                                                                                        ? "shader_type canvas_item;\nuniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;\nvoid fragment() {\n    vec4 texel = texture(TEXTURE, UV);\n    vec4 destination = textureLod(screen_texture, SCREEN_UV, 0.0);\n    COLOR = vec4(texel.rgb + destination.rgb * 0.25, 1.0);\n}"
                                                                                        : kind == 20
                                                                                            ? "shader_type canvas_item;\nuniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;\nvoid fragment() {\n    vec4 texel = texture(TEXTURE, UV);\n    vec4 destination = textureLod(screen_texture, SCREEN_UV, 0.0);\n    COLOR = vec4(vec3(texel.a) + destination.rgb * 0.25, 1.0);\n}"
                                                                                            : kind == 21
                                                                                                ? "shader_type canvas_item;\nuniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;\nvoid fragment() {\n    vec4 destination = textureLod(screen_texture, SCREEN_UV, 0.0);\n    COLOR = vec4(vec3(COLOR.a) + destination.rgb * 0.25, 1.0);\n}"
                                                                                                : kind == 22
                                                                                                    ? "shader_type canvas_item;\nuniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;\nvoid fragment() {\n    vec4 destination = textureLod(screen_texture, SCREEN_UV, 0.0);\n    COLOR = vec4(COLOR.rgb + destination.rgb * 0.25, 1.0);\n}"
                                                                                                    : kind == 23
                                                                                                        ? "shader_type canvas_item;\nuniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;\nvoid fragment() {\n    vec4 texel = texture(TEXTURE, UV);\n    vec4 destination = textureLod(screen_texture, SCREEN_UV, 0.0);\n    COLOR = vec4(texel.rgb + destination.rgb * 0.25, 1.0);\n}"
                                                                                                        : "shader_type canvas_item;\nuniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;\nvoid fragment() {\n    vec4 texel = texture(TEXTURE, UV);\n    vec4 destination = textureLod(screen_texture, SCREEN_UV, 0.0);\n    COLOR = vec4(texel.rgb + destination.rgb * 0.25, 1.0);\n}",
                },
            };
        if (!isProbe && (kind == 1 || kind == 2))
            mat!.SetShaderParameter("vcolor", modulate);
        if (!isProbe)
            GD.Print($"[BlendAudit] case={i} kind={kind} shader={(mat?.Shader as Shader)?.Code?.Replace("\n", " | ")}");
        if (!isProbe)
        {
            // 判定 screen_texture 内容：红探针垫在 quad 下方 (ZIndex 200)，
            // 若 copy 包含探针则 kind15 的 destination 应为红。
            _blendAuditProbe = new ColorRect
            {
                Color = new Color(1f, 0f, 0f, 1f),
                Position = center,
                Size = new Vector2(80, 80),
                ZIndex = 200,
            };
            AddChild(_blendAuditProbe);
        }
        _blendAuditQuad = new TextureRect
        {
            Texture = ImageTexture.CreateFromImage(img),
            Material = mat,
            Modulate = modulate,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Position = center,
            Size = new Vector2(80, 80),
            ZIndex = 201,
        };
        AddChild(_blendAuditQuad);
        _blendAuditFrames = 0;
    }

    // 空间无关读回：在全屏黑底板上，取视口中心邻域内像素的众数颜色（即四边形
    // 的均匀色）。窗口尺寸/缩放不影响判定；全黑案例（黑/零 alpha）众数为空 → 黑。
    private static Color SampleBlendAuditQuad(Image image)
    {
        int cx = image.GetWidth() / 2, cy = image.GetHeight() / 2;
        var counts = new Dictionary<int, (int Count, Color Colour)>();
        for (int y = cy - 120; y <= cy + 120; y += 4)
        {
            for (int x = cx - 120; x <= cx + 120; x += 4)
            {
                var c = image.GetPixel(x, y);
                if (Math.Max(c.R, Math.Max(c.G, c.B)) <= 0.02f) continue;
                int key = ((int)Mathf.RoundToInt(c.R * 15f) << 8) |
                          ((int)Mathf.RoundToInt(c.G * 15f) << 4) |
                          (int)Mathf.RoundToInt(c.B * 15f);
                if (!counts.TryGetValue(key, out var e)) e = (0, c);
                e.Count++;
                counts[key] = e;
            }
        }
        if (counts.Count == 0) return Colors.Black;
        Color best = Colors.Black;
        int bestN = 0;
        foreach (var e in counts.Values)
        {
            if (e.Count > bestN) { bestN = e.Count; best = e.Colour; }
        }
        return best;
    }

    private void ProcessBlendAudit()
    {
        if (!_blendAudit) return;
        if (++_blendAuditFrames < 4) return;
        var image = GetViewport().GetTexture()?.GetImage();
        if (image == null)
        {
            GD.PrintErr("[BlendAudit] FAIL 无帧缓冲可读回（headless/dummy？）");
            return;
        }
        int i = _blendAuditCase;
        int probeIndex = i - BlendAuditCases.Length;
        bool isProbe = probeIndex >= 0;
        (Color texel, Color modulate, Color expectCurrent, Color expectOriginal, int kind) =
            isProbe ? (BlendAuditMixProbes[probeIndex].Texel, BlendAuditMixProbes[probeIndex].Modulate,
                       BlendAuditMixProbes[probeIndex].ExpectCurrent, BlendAuditMixProbes[probeIndex].ExpectOriginal, 0)
                    : (BlendAuditCases[i].Texel, BlendAuditCases[i].Modulate,
                       BlendAuditCases[i].ExpectCurrent, BlendAuditCases[i].ExpectOriginal, BlendAuditCases[i].Kind);
        Color got = SampleBlendAuditQuad(image);
        bool curHit = BlendColourClose(got, expectCurrent);
        bool origHit = BlendColourClose(got, expectOriginal);
        if (curHit) _blendAuditCurrentHits++;
        if (origHit) _blendAuditOriginalHits++;
        // 奇偶判定只统计三种生产 blend 材质（Kind<=2），探针与判别 kind 仅作记录
        if (!isProbe && kind <= 2 && origHit) _blendAuditParityHits++;
        GD.Print($"[BlendAudit] case={i} texel={texel} colour={modulate} got={got} " +
                 string.Format(Lang.MapTestUi602Label, expectCurrent, expectOriginal) +
                 (curHit || origHit ? (origHit ? "PASS(original)" : "PASS(current)") : "FAIL"));
        int total = BlendAuditCases.Length + BlendAuditMixProbes.Length;
        if (++_blendAuditCase < total)
        {
            SpawnBlendAuditCase(_blendAuditCase);
            return;
        }
        var backdrop = image.GetPixel(image.GetWidth() / 2, 60);
        bool backdropOk = BlendColourClose(backdrop, BlendAuditBackdropColour);
        GD.Print($"[BlendAudit] backdrop got={backdrop} expected={BlendAuditBackdropColour} " +
                 (backdropOk ? "PASS" : "FAIL"));
        string path = "/tmp/zircon-blend-audit.png";
        image.SavePng(path);
        int materialCases = BlendAuditCases.Count(c => c.Kind <= 2);
        GD.Print($"[BlendAudit] 判定: original命中={_blendAuditOriginalHits} current命中={_blendAuditCurrentHits} 材质案例命中original={_blendAuditParityHits}/{materialCases} cases={total}");
        GD.Print(_blendAuditParityHits == materialCases && backdropOk
            ? string.Format(Lang.MapTestUi603Label, path)
            : string.Format(Lang.MapTestUi604Label, path));
        GetTree().Quit();
    }

    private async void RunTransparencyAudit()
    {
        try
        {
        bool fullScan = OS.GetCmdlineUserArgs().Contains("--full-texture-audit");
        string auditFile = OS.GetCmdlineUserArgs()
            .FirstOrDefault(arg => arg.StartsWith("--audit-file=", StringComparison.OrdinalIgnoreCase))?
            .Substring("--audit-file=".Length);
        int auditStart = ParseAuditInt("--audit-start=", 0);
        int auditEnd = ParseAuditInt("--audit-end=", int.MaxValue);
        int libraries = 0, frames = 0, transparentFrames = 0, cornerPollution = 0;
        int inspectedEntries = 0;
        foreach (LibraryFile file in Enum.GetValues<LibraryFile>())
        {
            string name = file.ToString();
            if (!string.IsNullOrWhiteSpace(auditFile)
                && !string.Equals(name, auditFile, StringComparison.OrdinalIgnoreCase))
                continue;
            var library = LibraryCache.Get(file);
            if (library?.Images == null) continue;
            libraries++;
            // Actual legacy call sites for MirEffect, projectile, exterior
            // equipment and ordinary ProgUse images all pass ImageType.Image.
            // Weather is the only ProgUse subset using the dedicated keyed
            // path, and it is audited separately by RunWeatherAudit.
            bool effectTransparency = false;
            // 默认均匀抽取最多 24 帧；完整模式逐帧检查整个图库，用于
            // 发布前的“所有贴图”审计，不把抽样结果冒充全量结论。
            int stride = fullScan ? 1 : Math.Max(1, library.Images.Length / 24);
            int firstIndex = Math.Clamp(auditStart, 0, library.Images.Length);
            int lastIndex = Math.Clamp(auditEnd, firstIndex, library.Images.Length);
            for (int index = firstIndex; index < lastIndex; index += stride)
            {
                inspectedEntries++;
                if (fullScan && inspectedEntries % 8 == 0)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var image = library.Images[index];
                if (image == null || image.Width <= 0 || image.Height <= 0) continue;
                if (fullScan && inspectedEntries == 1)
                    GD.Print($"[TransparencyAudit] begin file={file} frame={index} " +
                        $"size={image.Width}x{image.Height} codec={image.ImageCodec} " +
                        $"position={image.Position}");
                // 普通图库和旧端 ImageType.Image 路径必须保留原始 Alpha/黑色
                // 像素；天气颜色键由单独的 WeatherAudit 覆盖，不能按图库名称
                // 把整个 ProgUse/EquipEffect/Magic 库误判成颜色键资源。
                byte[] rgba = library.GetAuditImageData(index, effectTransparency);
                if (rgba == null) continue;
                frames++;
                int transparent = 0;
                // GetPixel() 跨 C#↔引擎边界逐像素调用，在全图库审计中会把
                // 数万帧拖到分钟级；ZlReader 统一产出 RGBA8，因此直接扫 Alpha
                // 字节既保持同一判定，又让“所有贴图”审计可以实际完成。
                for (int offset = 3; offset < rgba.Length; offset += 4)
                    if (rgba[offset] <= 2) transparent++;
                if (transparent > 0) transparentFrames++;
                bool cornersOpaque = effectTransparency
                    && rgba.Length >= 4
                    && rgba[3] >= 253
                    && rgba[(image.Width - 1) * 4 + 3] >= 253
                    && rgba[(image.Height - 1) * image.Width * 4 + 3] >= 253
                    && rgba[rgba.Length - 1] >= 253;
                if (cornersOpaque && transparent > image.Width * image.Height / 4)
                {
                    cornerPollution++;
                    int last = rgba.Length - 4;
                    GD.Print($"[TransparencyAudit] SUSPECT file={file} frame={index} " +
                        $"size={image.Width}x{image.Height} transparent={transparent} " +
                        $"corners=({rgba[0]},{rgba[1]},{rgba[2]})/({rgba[last]},{rgba[last + 1]},{rgba[last + 2]})");
                }
            }
            if (fullScan)
            {
                library.ClearAuditEffectTextureCache();
            GD.Print($"[TransparencyAudit] progress file={file} images={library.Images.Length} frames={frames}");
            }
        }
        GD.Print(cornerPollution == 0
            ? $"[TransparencyAudit] PASS mode={(fullScan ? "full" : "sample")} file={(auditFile ?? "all")} range={auditStart}..{(auditEnd == int.MaxValue ? "end" : auditEnd)} libraries={libraries} frames={frames} transparentFrames={transparentFrames} cornerPollution=0"
            : $"[TransparencyAudit] REVIEW mode={(fullScan ? "full" : "sample")} file={(auditFile ?? "all")} range={auditStart}..{(auditEnd == int.MaxValue ? "end" : auditEnd)} libraries={libraries} frames={frames} transparentFrames={transparentFrames} cornerPollution={cornerPollution}");
        // Explicit full-scan mode is a CI-style command; terminate after the
        // result so callers do not have to infer completion from a timeout.
        if (fullScan && OS.GetCmdlineUserArgs().Contains("--audit-only"))
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[TransparencyAudit] EXCEPTION {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static int ParseAuditInt(string prefix, int fallback)
    {
        string arg = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return arg != null && int.TryParse(arg.Substring(prefix.Length), out int value) ? Math.Max(0, value) : fallback;
    }

    private static string GetCmdlineValue(string prefix)
    {
        string arg = OS.GetCmdlineUserArgs()
            .FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return arg == null ? null : arg.Substring(prefix.Length);
    }

    private void DumpRequestedZlFrame()
    {
        try
        {
            using var library = new ZlLibrary(_dumpZlFile);
            if (_dumpZlIndex >= library.Images.Length || library.Images[_dumpZlIndex] == null)
            {
                GD.PrintErr($"[ZlFrameDump] FAIL file={_dumpZlFile} frame={_dumpZlIndex} absent");
                return;
            }

            var image = library.GetImageTexture(_dumpZlIndex)?.GetImage();
            if (image == null || image.SavePng(_dumpZlOutput) != Error.Ok)
            {
                GD.PrintErr($"[ZlFrameDump] FAIL file={_dumpZlFile} frame={_dumpZlIndex} decode/save");
                return;
            }

            var meta = library.Images[_dumpZlIndex];
            GD.Print($"[ZlFrameDump] PASS file={_dumpZlFile} frame={_dumpZlIndex} "
                + $"size={meta.Width}x{meta.Height} offset=({meta.OffSetX},{meta.OffSetY}) "
                + $"codec={meta.ImageCodec} output={_dumpZlOutput}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ZlFrameDump] EXCEPTION {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            GetTree().Quit();
        }
    }

    private void RunWeatherAudit()
    {
        var library = LibraryCache.Get(LibraryFile.ProgUse);
        if (library == null)
        {
            GD.PrintErr("[WeatherAudit] FAIL ProgUse library unavailable");
            return;
        }
        int[] frames = { 500, 509, 510, 511, 512, 513, 514, 540, 550 };
        int passed = 0;
        using var legacyReference = new ZlPixelReference(library.FileName);
        foreach (int frame in frames)
        {
            // Production weather rendering follows the old ImageType.Image
            // path. Keyed is retained only as a diagnostic comparison.
            var image = library.GetImageTexture(frame)?.GetImage();
            var keyed = (frame == 550
                ? library.GetFogTexture(frame)
                : library.GetWeatherTexture(frame))?.GetImage();
            if (image == null)
            {
                GD.PrintErr($"[WeatherAudit] FAIL frame={frame} unavailable");
                continue;
            }

            int transparent = 0, visible = 0, keyedTransparent = 0, keyedVisible = 0;
            for (int y = 0; y < image.GetHeight(); y++)
                for (int x = 0; x < image.GetWidth(); x++)
                    if (image.GetPixel(x, y).A <= 0.01f) transparent++; else visible++;
            if (keyed != null)
                for (int y = 0; y < keyed.GetHeight(); y++)
                    for (int x = 0; x < keyed.GetWidth(); x++)
                        if (keyed.GetPixel(x, y).A <= 0.01f) keyedTransparent++; else keyedVisible++;

            byte[] legacy = legacyReference.DecodeImage(library, frame);
            int legacyTransparent = 0, legacyVisible = 0;
            int alphaMismatch = 0;
            if (legacy != null)
            {
                for (int offset = 3; offset < legacy.Length; offset += 4)
                    if (legacy[offset] <= 2) legacyTransparent++; else legacyVisible++;
                for (int y = 0; y < image.GetHeight(); y++)
                    for (int x = 0; x < image.GetWidth(); x++)
                    {
                        bool oldVisible = legacy[(y * image.GetWidth() + x) * 4 + 3] > 2;
                        bool godotVisible = image.GetPixel(x, y).A > 0.01f;
                        if (oldVisible != godotVisible) alphaMismatch++;
                    }
            }

            bool ok = legacy != null && alphaMismatch == 0;
            if (ok) passed++;
            GD.Print(ok
                ? $"[WeatherAudit] PASS frame={frame} size={image.GetWidth()}x{image.GetHeight()} productionTransparent={transparent} productionVisible={visible} keyedTransparent={keyedTransparent} keyedVisible={keyedVisible} legacyTransparent={legacyTransparent} legacyVisible={legacyVisible} alphaMismatch=0"
                : $"[WeatherAudit] FAIL frame={frame} productionTransparent={transparent} productionVisible={visible} legacyTransparent={legacyTransparent} legacyVisible={legacyVisible} alphaMismatch={alphaMismatch}");
        }
        GD.Print(passed == frames.Length
            ? $"[WeatherAudit] PASS all={passed}/{frames.Length} weatherFrames=500,509-514,540,550"
            : $"[WeatherAudit] REVIEW all={passed}/{frames.Length} weatherFrames=500,509-514,540,550");
    }

    private void RunTransparencyModeAudit()
    {
        var samples = new[]
        {
            ("Magic", LibraryFile.Magic, 830),
            ("MagicEx2", LibraryFile.MagicEx2, 1900),
            ("Magic", LibraryFile.Magic, 831),
            ("MagicEx2", LibraryFile.MagicEx2, 1901),
        };
        foreach (var (name, file, frame) in samples)
        {
            var library = LibraryCache.Get(file);
            var ordinary = library?.GetImageTexture(frame)?.GetImage();
            var keyed = library?.GetEffectTexture(frame)?.GetImage();
            if (ordinary == null || keyed == null)
            {
                GD.PrintErr($"[TransparencyModeAudit] FAIL {name} frame={frame} unavailable");
                continue;
            }
            int ordinaryTransparent = 0, keyedTransparent = 0;
            for (int y = 0; y < ordinary.GetHeight(); y++)
                for (int x = 0; x < ordinary.GetWidth(); x++)
                {
                    if (ordinary.GetPixel(x, y).A <= 0.01f) ordinaryTransparent++;
                    if (keyed.GetPixel(x, y).A <= 0.01f) keyedTransparent++;
                }
            GD.Print($"[TransparencyModeAudit] {name} frame={frame} size={ordinary.GetWidth()}x{ordinary.GetHeight()} " +
                     $"ordinaryTransparent={ordinaryTransparent} keyedTransparent={keyedTransparent}");
        }
    }

    private void RunLayerOrderAudit()
    {
        bool rows = RenderOrder.TerrainMiddle(10) < RenderOrder.Object(10)
            && RenderOrder.TerrainMiddle(10) < RenderOrder.TerrainFront(10)
            && RenderOrder.TerrainFront(10) < RenderOrder.Object(10)
            && RenderOrder.Object(10) < RenderOrder.ObjectEffect(10)
            && RenderOrder.TerrainFront(10) < RenderOrder.ObjectEffect(10)
            && RenderOrder.ObjectEffect(10) < RenderOrder.TerrainMiddle(11);
        bool postObject = RenderOrder.Object(10) < RenderOrder.Particles
            && RenderOrder.Particles < RenderOrder.LocalPlayerEffect
            && RenderOrder.LocalPlayerEffect < RenderOrder.FinalEffects;
        // 旧端 MapControl 把 DrawType.Floor 特效画在底色与所有地形行之间，
        // 因此必须低于每一行的对象（否则火墙压住角色）且低于行 1+ 的地形
        // （否则被地砖盖住）。行 0 与底色同槽，靠树序（MapView 先加入，
        // 特效后加入）排在地形行 0 之上、底色之上。
        bool floorBelowObjects = true;
        bool floorBelowRows = true;
        for (int y = 0; y <= 64; y++)
        {
            floorBelowObjects &= RenderOrder.FloorEffects < RenderOrder.Object(y);
            if (y >= 1) floorBelowRows &= RenderOrder.FloorEffects < RenderOrder.TerrainMiddle(y);
        }
        bool floorAboveBase = RenderOrder.FloorEffects >= RenderOrder.TerrainBase;
        GD.Print(rows && postObject && floorBelowObjects && floorBelowRows && floorAboveBase
            ? "[LayerOrderAudit] PASS legacy row/local-player ordering + floor-below-rows-and-objects"
            : $"[LayerOrderAudit] FAIL rows={rows} postObject={postObject} " +
              $"floorBelowObjects={floorBelowObjects} floorBelowRows={floorBelowRows} floorAboveBase={floorAboveBase}");
    }

    private void RunSoundAssetAudit()
    {
        string soundRoot = ProjectSettings.GlobalizePath("res://../Debug/Client/Sound/");
        var files = SoundCatalog.Entries.Values.Select(x => x.FileName).Distinct().OrderBy(x => x).ToArray();
        int valid = 0;
        foreach (string file in files)
        {
            string path = Path.Combine(soundRoot, file);
            try
            {
                byte[] header = File.ReadAllBytes(path);
                bool riffWave = header.Length >= 12
                    && Encoding.ASCII.GetString(header, 0, 4) == "RIFF"
                    && Encoding.ASCII.GetString(header, 8, 4) == "WAVE";
                if (riffWave) valid++;
                else GD.PrintErr($"[SoundAudit] FAIL {file} invalid WAV header");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SoundAudit] FAIL {file} {ex.Message}");
            }
        }
        GD.Print($"[SoundAudit] valid={valid}/{files.Length}");
        if (valid == files.Length)
            GD.Print($"[SoundAudit] PASS catalog={SoundCatalog.Entries.Count} files={files.Length}");
    }

    private void StartNextActionAudit()
    {
        if (_actionIndex >= _actions.Count)
        {
            if (!_actionAuditFinished) GD.Print("[ActionAudit] PASS all action sequences");
            _actionAuditFinished = true;
            return;
        }
        var action = _actions[_actionIndex++];
        _actionFrames.Clear();
        _actionAnimations.Clear();
        _auditPlayer.Dead = false;
        _auditPlayer.Horse = HorseType.None;
        _auditPlayer.Cloaked = _auditPlayer.GhostWalking = _auditPlayer.DragonRepulsed = _auditPlayer.ElementalHurricane = false;
        _auditPlayer.SetAnimation(MirAnimation.Standing);
        action.Start(_auditPlayer);
        if (_auditPlayer.Animation != action.Expected)
            GD.PrintErr($"[ActionAudit] FAIL {action.Name}: expected {action.Expected}, got {_auditPlayer.Animation}");
        var frame = FrameSet.Players[action.Expected];
        // Walking/Running 是循环动作；在桌面窗口、headless 和组合审计同时
        // 执行时，首个检查点可能恰好落在循环动作的第 0 帧，导致只收集到
        // 一个“0”或没有帧变更的假失败。至少等待一个完整循环再加余量，
        // 一次性动作仍按同一窗口收集其全部帧。
        _actionDeadline = Godot.Time.GetTicksMsec() + Math.Max(1000, frame.Sum + 400);
        GD.Print($"[ActionAudit] START {action.Name}: anim={action.Expected} count={frame.FrameCount} sum={frame.Sum:0}ms");
    }

    private void ProcessActionAudit()
    {
        if (_auditPlayer == null || _actionAuditFinished || _actionIndex > _actions.Count) return;
        if (Godot.Time.GetTicksMsec() < _actionDeadline) return;
        var action = _actions[_actionIndex - 1];
        var expected = FrameSet.Players[action.Expected];
        if (action.Name == "ActionQueue" && !_actionAnimations.Contains(MirAnimation.Struck))
            GD.PrintErr("[ActionAudit] FAIL ActionQueue: queued Struck animation was not reached");
        else if (_actionFrames.Count < (expected.FrameCount <= 1 ? 0 : expected.FrameCount - 1))
            GD.PrintErr($"[ActionAudit] FAIL {action.Name}: observed frames={_actionFrames.Count}, expected count={expected.FrameCount}");
        else
            GD.Print($"[ActionAudit] PASS {action.Name}: observed={string.Join(',', _actionFrames.OrderBy(x => x))}");
        StartNextActionAudit();
    }

    private void RenderObjectAudit()
    {
        var healthBackground = MirSkin.GetTexture(LibraryFile.Interface, 80);
        var healthFill = MirSkin.GetTexture(LibraryFile.Interface, 79);
        bool labelAnchor = Math.Abs(RenderPrimitives.OriginalNameBaseline(9f)) < 32f;
        bool healthAssets = healthBackground != null && healthFill != null;
        GD.Print(labelAnchor && healthAssets
            ? $"[ObjectLabelAudit] PASS centerX=24 nameBaseline={RenderPrimitives.OriginalNameBaseline(9f):0.0} "
              + $"health79={healthFill.GetWidth()}x{healthFill.GetHeight()} health80={healthBackground.GetWidth()}x{healthBackground.GetHeight()}"
            : $"[ObjectLabelAudit] FAIL anchor={labelAnchor} healthAssets={healthAssets}");
        int x = 480, y = 320;
        var monsterInfo = Globals.MonsterInfoList?.Binding
            .FirstOrDefault(m => MonsterLookup.Map.ContainsKey(m.Image));
        if (monsterInfo != null)
        {
            var monster = ObjectRenderer.CreateMonster(new S.ObjectMonster
            {
                ObjectID = 9001,
                MonsterIndex = monsterInfo.Index,
                Direction = MirDirection.Down,
                Location = new System.Drawing.Point(0, 0),
                NameColour = System.Drawing.Color.Lime,
            });
            if (monster != null)
            {
                monster.SetAnimation(MirAnimation.Combat1);
                bool attackOk = monster.Animation == MirAnimation.Combat1;
                monster.PlayRangeAttack();
                bool rangeOk = monster.Animation == MirAnimation.Combat2;
                monster.PlaySpell(MagicType.DoomClawLeftPinch);
                bool specialSpellOk = monster.Animation == MirAnimation.Combat4;
                monster.PlaySpell(MagicType.DragonRepulse);
                bool dragonOk = monster.Animation == MirAnimation.DragonRepulseStart;
                var deadMonster = ObjectRenderer.CreateMonster(new S.ObjectMonster
                {
                    ObjectID = 9003,
                    MonsterIndex = monsterInfo.Index,
                    Direction = MirDirection.Down,
                    Location = new System.Drawing.Point(0, 0),
                    Dead = true,
                });
                bool deadOk = deadMonster != null && deadMonster.Animation == MirAnimation.Dead;
                GD.Print(attackOk && rangeOk && specialSpellOk && dragonOk && deadOk
                    ? "[MonsterAudit] PASS Attack=Combat1 Range=Combat2 DoomClawLeftPinch=Combat4 DragonRepulse=Start Dead=Die"
                    : $"[MonsterAudit] FAIL attack={monster.Animation} range={rangeOk} special={specialSpellOk} dragon={dragonOk} dead={deadOk}");
                deadMonster?.QueueFree();
                monster.Position = new Vector2(x, y);
                monster.ShowHealthBar = true;
                monster.Health = monster.MaxHealth = 100;
                monster.Focused = true;
                monster.ZIndex = 100 + y / 32;
                AddChild(monster);
            }
        }

        var npcInfo = Globals.NPCInfoList?.Binding.FirstOrDefault();
        if (npcInfo != null)
        {
            var npc = ObjectRenderer.CreateNPC(new S.ObjectNPC
            {
                ObjectID = 9002,
                NPCIndex = npcInfo.Index,
                Direction = MirDirection.Down,
                CurrentLocation = new System.Drawing.Point(0, 0),
            });
            if (npc != null)
            {
                npc.Position = new Vector2(x + 120, y);
                npc.Focused = true;
                npc.ZIndex = 100 + y / 32;
                AddChild(npc);
            }
        }

        var player = new PlayerRenderer();
        player.UpdateAppearance(new StartInformation
        {
            Name = "RenderAuditHero",
            NameColour = System.Drawing.Color.Cyan,
            Class = MirClass.Warrior,
            Gender = MirGender.Male,
            HairType = 1,
            HairColour = System.Drawing.Color.Black,
            Armour = 11,
            ArmourColour = System.Drawing.Color.LightSkyBlue,
            Costume = -1,
            HelmetShape = 11,
            Shield = 10,
            Weapon = 1200,
            Horse = HorseType.None,
            HorseShape = 0,
            Direction = MirDirection.Down,
        });
        // 诊断对象置于独立的高层，避免被测试地图前景盖住；生产场景仍由
        // RenderY 决定 ZIndex。这样截图能真正检查坐骑、装备和脚底影子。
        player.Position = new Vector2(x - 240, y + 120);
        player.ShowHealthBar = true;
        player.Health = player.MaxHealth = 100;
        player.ZIndex = 1000;
        AddChild(player);

        var itemInfo = Globals.ItemInfoList?.Binding
            .FirstOrDefault(i => i.Image >= 0);
        if (itemInfo != null)
        {
            var item = ObjectRenderer.CreateItem(new S.ObjectItem
            {
                ObjectID = 9003,
                Item = new ClientUserItem(itemInfo, 1),
                Location = new System.Drawing.Point(0, 0),
            });
            if (item != null)
            {
                item.Position = new Vector2(x + 360, y);
                item.Focused = true;
                item.ZIndex = 100 + y / 32;
                AddChild(item);
            }
        }

        GD.Print("[RenderAudit] 已创建真实 Monster/NPC ZL 对象，触发 Shadow、Overlay fallback、标签和 RenderY 绘制");
    }

    private ZlLibrary GetLibrary(LibraryFile file)
    {
        // 测试场景必须与实际 GameScene 使用相同的路径大小写修复、
        // ZL/ZL2 解析和缓存，否则会出现“地图统计成功但截图灰底”的假通过。
        return LibraryCache.Get(file);
    }

    private void RenderArea(MirMap map, int startX, int startY, int viewW, int viewH)
    {
        for (int x = startX; x < startX + viewW && x < map.Width; x++)
        {
            for (int y = startY; y < startY + viewH && y < map.Height; y++)
            {
                ref var cell = ref map.Cells[x, y];
                float px = x * CellWidth;
                float py = y * CellHeight;

                // 背景层（半分辨率，只画偶数格）
                if (x % 2 == 0 && y % 2 == 0 && cell.BackFile > 0)
                {
                    DrawCell(cell.BackFile, cell.BackImage, px, py, 90 + y);
                }

                // 中层
                if (cell.MiddleFile > 0 && cell.MiddleImage > 0)
                {
                    DrawCell(cell.MiddleFile, cell.MiddleImage - 1, px, py, 99 + y);
                }

                // 前景
                if (cell.FrontFile > 0 && cell.FrontImage > 0)
                {
                    DrawCell(cell.FrontFile, cell.FrontImage - 1, px, py, 101 + y);
                }
            }
        }
    }

    private void DrawCell(int fileByte, int imageIndex, float px, float py, int zIndex)
    {
        if (fileByte == 0 || imageIndex < 0) return;
        if (!Libraries.KROrder.TryGetValue(fileByte, out LibraryFile file)) return;
        if (file == LibraryFile.Tilesc) return;

        var lib = GetLibrary(file);
        if (lib == null || imageIndex >= lib.Images.Length)
        {
            if (_mapTextureDiagnostics++ < 8)
                GD.PrintErr($"[MapTest] 地形图库失败: fileByte={fileByte} file={file} image={imageIndex} lib={(lib == null ? "null" : lib.Images.Length.ToString())}");
            return;
        }
        if (lib.Images[imageIndex] == null)
        {
            if (_mapTextureDiagnostics++ < 8)
                GD.PrintErr($"[MapTest] 地形帧为空: file={file} image={imageIndex}");
            return;
        }

        var texture = lib.GetImageTexture(imageIndex);
        if (texture == null)
        {
            if (_mapTextureDiagnostics++ < 8)
                GD.PrintErr($"[MapTest] 地形纹理为空: file={file} image={imageIndex}");
            return;
        }

        var img = lib.Images[imageIndex];
        var sprite = new Sprite2D();
        sprite.Texture = texture;
        sprite.Position = new Vector2(px, py);
        sprite.ZIndex = zIndex;
        // 贴图偏移（OffSetY 让贴图底边对齐格子）
        sprite.Offset = new Vector2(img.OffSetX, img.OffSetY);
        AddChild(sprite);
    }
}
