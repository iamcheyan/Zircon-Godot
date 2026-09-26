using System;
using System.Collections.Generic;
using Godot;
using Library;
using Library.SystemModels;

namespace ZirconClient.Scripts;

// 地图光照层：环境光 + .map 格子光。绘制在所有世界对象之上、UI 之下。
// 采用 Godot 2D 的轻量叠加实现，保持原版 LightSetting 的主要视觉语义。
public partial class MapLightLayer : Node2D
{
    private static float WorldScale => GameScene.WorldScale;
    // 最低黑夜环境光设为 0.25f (25% 柔和月夜亮度，舒适不伤眼)
    private const float NightAmbient = 0.25f;
    private const float TwilightAmbient = 100f / 255f;
    private const int MaxLights = 64;
    // 原版死亡红染: Color.IndianRed (205,92,92) 整层相乘染色
    // (Client/Scenes/Views/MapControl.cs Light.OnClearTexture user.Dead 分支)。
    public static readonly Color DeadTint = new(205f / 255f, 92f / 255f, 92f / 255f);
    private MapInfo _mapInfo;
    private MapView _mapView;
    private float _dayTime = 1f;
    private LightSetting? _auditLightOverride;
    private Func<IEnumerable<LightSource>> _sources;
    private Func<PlayerLightState> _playerState;
    private ShaderMaterial _lightMaterial;

    public readonly struct LightSource
    {
        public readonly Vector2 Position;
        public readonly int Radius;
        public readonly Color Colour;
        public LightSource(Vector2 position, int radius, Color colour)
        { Position = position; Radius = radius; Colour = colour; }
    }

    // 原版特殊光照态: 死亡→全屏红染; 深渊中毒→全黑+玩家微光。
    // 白天也强制渲染 (ShouldRenderLightLayer 对二者直接返回 true)。
    public readonly struct PlayerLightState
    {
        public readonly bool Dead;
        public readonly bool Abyss;
        public readonly Vector2 Position;
        public PlayerLightState(bool dead, bool abyss, Vector2 position)
        { Dead = dead; Abyss = abyss; Position = position; }
    }

    public void SetMap(MapInfo info, MapView view)
    {
        _mapInfo = info;
        _mapView = view;
        QueueRedraw();
    }

    public override void _Ready()
    {
        // SCREEN_TEXTURE lets the light layer darken the already-rendered map and
        // restore it around light sources. A translucent DrawCircle painted over a
        // black rectangle cannot reveal the map underneath.
        var shader = new Shader
        {
            Code = @"
shader_type canvas_item;
render_mode unshaded;
uniform sampler2D screen_texture : hint_screen_texture, filter_nearest;
uniform float ambient = 1.0;
uniform vec3 global_tint = vec3(1.0);
uniform int light_count = 0;
uniform vec2 viewport_size = vec2(1.0);
uniform vec2 light_positions[64];
uniform float light_radii[64];
uniform vec3 light_colours[64];

void fragment() {
    vec4 scene = texture(screen_texture, SCREEN_UV);
    vec2 point = UV * viewport_size;
    float brightness = ambient;
    vec3 tint = vec3(1.0);
    for (int i = 0; i < 64; i++) {
        if (i >= light_count) break;
        float influence = 1.0 - smoothstep(light_radii[i] * 0.35, light_radii[i],
            distance(point, light_positions[i]));
        brightness = max(brightness, ambient + influence * (1.0 - ambient));
        tint = mix(tint, light_colours[i], influence * 0.22);
    }
    COLOR = vec4(scene.rgb * brightness * tint * global_tint, scene.a);
}
"
        };
        _lightMaterial = new ShaderMaterial { Shader = shader };
        Material = _lightMaterial;
    }

    public void SetDayTime(float dayTime)
    {
        _dayTime = dayTime;
        QueueRedraw();
    }

    /// <summary>仅供确定性渲染审计使用，不改变 MapInfo/数据库状态。</summary>
    public void SetAuditLightOverride(LightSetting? setting)
    {
        _auditLightOverride = setting;
        QueueRedraw();
    }

    public void SetObjectSources(Func<IEnumerable<LightSource>> sources)
    {
        _sources = sources;
        QueueRedraw();
    }

    public void SetPlayerState(Func<PlayerLightState> state)
    {
        _playerState = state;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_mapInfo != null) QueueRedraw();
    }

    // Kept as a pure function so the fixed original LightSetting mapping can
    // be regression-tested without requiring a live viewport or map scene.
    public static float AmbientFor(LightSetting setting, float dayTime)
        => setting switch
        {
            LightSetting.Light => 1f,
            LightSetting.Night => NightAmbient,
            LightSetting.Twilight => TwilightAmbient,
            _ => Math.Clamp(Math.Max(NightAmbient, dayTime), 0f, 1f),
        };

    // The legacy light texture is 1024px wide. Its destination is rendered at
    // 2x in the old client, while this shader receives logical (1x) coordinates,
    // hence the 1024 / 2 texture radius divided by WorldScale.
    public static float ObjectLightRadius(int light)
        => 256f * (0.1f + Math.Max(0, light) * 0.04f);

    public static float TileLightRadius(int light)
        => 256f * (0.1f + Math.Max(0, light) * 0.6f);

    public static float EffectLightRadius(int frameLight)
        => ObjectLightRadius(Math.Max(1, frameLight / 5));

    // 原版深渊微光: scale = BaseLightSize + 4 × LightScale = 0.18,
    // 与 ObjectLightRadius 同一 256 基准 (1024 纹理直径 / 2 / WorldScale)。
    public static float AbyssGlowRadius() => 256f * (0.1f + 4 * 0.02f);

    public override void _Draw()
    {
        if (_mapInfo == null || _mapView?.Map == null) return;

        Vector2 viewport = GetViewport().GetVisibleRect().Size / WorldScale;
        var playerState = _playerState?.Invoke() ?? default;

        // 原版 MapControl.Light.OnClearTexture: 死亡 → 整层 IndianRed 红染,
        // 深渊中毒 → 全黑 + 玩家微光。死亡优先于深渊; 二者白天也渲染。
        if (playerState.Dead)
        {
            DrawOverlay(viewport, 1f,
                new Godot.Collections.Array<Vector2>(),
                new Godot.Collections.Array<float>(),
                new Godot.Collections.Array<Color>(),
                new Vector3(DeadTint.R, DeadTint.G, DeadTint.B));
            return;
        }

        if (playerState.Abyss)
        {
            // 原版: Clear 黑 + 玩家位置微光 scale = BaseLightSize + 4×LightScale。
            // Abyss 环绕特效由 GameScene 循环施放 (MagicType.Abyss)。
            DrawOverlay(viewport, 0f,
                new Godot.Collections.Array<Vector2> { playerState.Position },
                new Godot.Collections.Array<float> { AbyssGlowRadius() },
                new Godot.Collections.Array<Color> { new Color(1f, 1f, 1f) },
                new Vector3(1f, 1f, 1f));
            return;
        }

        float ambient = AmbientFor(_auditLightOverride ?? _mapInfo.Light, _dayTime);

        if (ambient >= 0.999f) return;

        var positions = new Godot.Collections.Array<Vector2>();
        var radii = new Godot.Collections.Array<float>();
        var colours = new Godot.Collections.Array<Color>();

        if (_sources != null)
        {
            foreach (var source in _sources())
            {
                if (source.Radius <= 0 || positions.Count >= MaxLights) continue;
                positions.Add(source.Position);
                // 原端使用 1024x768 的径向光纹理；当前的圆形近似此前按
                // 1x 视觉尺寸估算，放到 2x 世界后光圈明显偏小。坐标仍
                // 保持逻辑坐标，只扩大半径，父节点再负责最终 2x 输出。
                // 原版光纹理为 1024x768，物体光的 scale 为
                // 0.1 + Light * 0.02 * 2。这里的 shader 半径使用逻辑坐标，
                // 因此把原版纹理直径除以两倍世界缩放，保持光圈覆盖范围。
                radii.Add(ObjectLightRadius(source.Radius));
                colours.Add(new Color(source.Colour.R, source.Colour.G, source.Colour.B));
            }
        }

        // 格子光使用与旧端相同的 LightScale/TileLightScaleMultiplier 量级。
        int minX = Math.Max(0, _mapView.CenterX - _mapView.ViewRangeX - 15);
        int maxX = Math.Min(_mapView.Map.Width - 1, _mapView.CenterX + _mapView.ViewRangeX + 15);
        int minY = Math.Max(0, _mapView.CenterY - _mapView.ViewRangeY - 15);
        int maxY = Math.Min(_mapView.Map.Height - 1, _mapView.CenterY + _mapView.ViewRangeY + 15);

        for (int x = minX; x <= maxX; x++)
        for (int y = minY; y <= maxY; y++)
        {
            ref var cell = ref _mapView.Map.Cells[x, y];
            if (cell.Light <= 0) continue;

            if (positions.Count >= MaxLights) break;
            Vector2 center = _mapView.CellToScreen(x, y, false) + new Vector2(24, 16);
            positions.Add(center);
            // 原版格子光 scale = 0.1 + Light * 30 * 0.02。
            radii.Add(TileLightRadius(cell.Light));
            colours.Add(Colors.White);
        }

        DrawOverlay(viewport, ambient, positions, radii, colours, new Vector3(1f, 1f, 1f));
    }

    private void DrawOverlay(Vector2 viewport, float ambient,
        Godot.Collections.Array<Vector2> positions, Godot.Collections.Array<float> radii,
        Godot.Collections.Array<Color> colours, Vector3 globalTint)
    {
        _lightMaterial.SetShaderParameter("ambient", ambient);
        _lightMaterial.SetShaderParameter("light_count", positions.Count);
        _lightMaterial.SetShaderParameter("viewport_size", viewport);
        _lightMaterial.SetShaderParameter("light_positions", positions);
        _lightMaterial.SetShaderParameter("light_radii", radii);
        _lightMaterial.SetShaderParameter("light_colours", colours);
        _lightMaterial.SetShaderParameter("global_tint", globalTint);
        DrawRect(new Rect2(Vector2.Zero, viewport), Colors.White);
    }
}
