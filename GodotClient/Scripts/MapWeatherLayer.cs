using System;
using System.Collections.Generic;
using Godot;
using Library;
using ZirconClient.Formats;

namespace ZirconClient.Scripts;

// 按 Client/Models/Particles/Weather 参数移植的真实 ProgUse.Zl 天气粒子。
// 雨509(水花510-514)、雪500、雾550、闪电540。
public partial class MapWeatherLayer : Node2D
{
    private const float WorldScale = 1f;
    private readonly List<WeatherParticle> _particles = new();
    private readonly RandomNumberGenerator _rng = new();
    private ZlLibrary _library;
    private Weather _weather;
    private bool _enabled = true;
    private double _rainSpawn;
    private double _snowSpawn;
    private double _lightningSpawn;

    private sealed class WeatherParticle
    {
        public int TextureIndex;
        public Vector2 Position;
        public Vector2 Velocity;
        public float Scale;
        public float Opacity = 1f;
        public float FadeRate;
        public float Rotation;
        public float AngularVelocity;
        public Color Colour = Colors.White;
        public double AgeMs;
        public double LifeMs;
        public bool Grounded;
        public bool Fade;
        public bool Fading;
    }

    public void SetWeather(Weather weather)
    {
        _weather = weather;
        _particles.Clear();
        _rainSpawn = _snowSpawn = _lightningSpawn = 0;
        _library = LibraryCache.Get(LibraryFile.ProgUse);
        if (_library == null)
        {
            GD.PrintErr("[Weather] ProgUse.Zl 加载失败");
            return;
        }
        _rng.Seed = (ulong)(uint)weather + 0x5EEDUL;
        if (Has(Weather.Fog)) SpawnFog();
        QueueRedraw();
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!_enabled) _particles.Clear();
        else if (_weather != Weather.None) SetWeather(_weather);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (!_enabled || !ClientSettings.DrawParticles || _weather == Weather.None || _library == null) return;
        double ms = delta * 1000.0;
        Vector2 size = LogicalViewport();

        if (Has(Weather.Rain))
        {
            _rainSpawn += ms;
            while (_rainSpawn >= 10 && _particles.Count < 600)
            {
                _rainSpawn -= 10;
                SpawnRain(size);
            }
        }
        if (Has(Weather.Snow))
        {
            _snowSpawn += ms;
            while (_snowSpawn >= 20 && CountKind(500) < 500)
            {
                _snowSpawn -= 20;
                SpawnSnow(size);
            }
        }
        if (Has(Weather.Lightning))
        {
            _lightningSpawn -= ms;
            if (_lightningSpawn <= 0 && CountKind(540) < 3)
            {
                SpawnLightning(size);
                _lightningSpawn = _rng.RandfRange(1000, 5000);
            }
        }

        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.AgeMs += ms;
            // Legacy Particle.Update is driven by a 10ms UpdateSpeed.  Its
            // Velocity, AngularVelocity and ScaleRate are applied once per
            // such tick, so the equivalent continuous rate is 100 ticks/s,
            // not the display refresh rate (60Hz).
            const float LegacyTicksPerSecond = 100f;
            if (!p.Grounded) p.Position += p.Velocity * (float)delta * LegacyTicksPerSecond;
            p.Rotation += p.AngularVelocity * (float)delta * LegacyTicksPerSecond;

            if (p.TextureIndex == 509 && p.AgeMs >= p.LifeMs && !p.Grounded)
            {
                p.Grounded = true;
                p.TextureIndex = 510;
                p.AgeMs = 0;
                // MirRainParticle switches 509 -> 510 on the next 10ms tick;
                // every splash frame thereafter is 100ms.
                p.LifeMs = 100;
                p.Velocity = Vector2.Zero;
            }
            else if (p.Grounded && p.TextureIndex >= 510 && p.TextureIndex < 514 && p.AgeMs >= 100)
            {
                // RainParticle 在旧端会依次播放 510..514，每帧 100ms。
                p.TextureIndex++;
                p.AgeMs = 0;
                p.LifeMs = p.TextureIndex == 514 ? 100 : 100;
            }
            else if (p.TextureIndex == 500 && p.AgeMs >= p.LifeMs && !p.Grounded)
            {
                // 旧端 SnowParticle 到期后停在落点，再以 ScaleRate=-0.01
                // 和 FadeRate=0.01 消融；不能继续按原速度飘走。
                p.Grounded = true;
                p.Velocity = Vector2.Zero;
                // Client/Models/Particles/Weather/Snow.cs stops rotation when
                // the snowflake lands; it then only fades and shrinks.
                p.AngularVelocity = 0f;
                p.Fading = true;
                p.AgeMs = 0;
                p.LifeMs = 1000;
            }
            else if (p.Fade && p.AgeMs >= p.LifeMs)
            {
                p.Fading = true;
                p.AgeMs = 0;
                p.LifeMs = 100;
            }
            if (p.Fading)
            {
                if (p.TextureIndex == 540)
                    p.Opacity -= p.FadeRate * (float)delta * LegacyTicksPerSecond;
                else
                    p.Scale -= 0.01f * (float)delta * LegacyTicksPerSecond;
            }

            if ((p.Grounded && p.TextureIndex >= 514 && p.AgeMs >= p.LifeMs) ||
                (p.TextureIndex == 500 && p.Scale <= 0) ||
                (p.TextureIndex == 540 && p.Opacity <= 0f) ||
                (p.Fading && p.TextureIndex != 500 && p.TextureIndex != 540 && p.AgeMs >= p.LifeMs))
                _particles.RemoveAt(i);
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_enabled || !ClientSettings.DrawParticles || _library == null) return;
        foreach (var p in _particles) DrawParticle(p);
    }

    private void DrawParticle(WeatherParticle p)
    {
        if (p.TextureIndex < 0 || p.TextureIndex >= _library.Images.Length) return;
        var img = _library.Images[p.TextureIndex];
        if (img == null || img.Width <= 0 || img.Height <= 0) return;
        // 旧版 Particle.Draw 最终调用 MirLibrary.DrawBlendCentered(...,
        // ImageType.Image)。它只使用 ZL/DXT1 自身的 Alpha，不额外抠除
        // 黑色颜色键；因此天气也必须走普通图像缓存，保持旧版的黑/灰像素
        // 和 DXT1 Alpha 完全一致。Weather/Fog 专用 keyed cache 仅供审计
        // 与素材诊断使用，不能用于正式绘制，否则会比旧版少画一批像素。
        var tex = _library.GetImageTexture(p.TextureIndex);
        if (tex == null) return;

        float opacity = Math.Clamp(p.Opacity, 0f, 1f);
        DrawSetTransform(p.Position, p.Rotation, Vector2.One * Math.Max(0.01f, p.Scale));
        DrawTextureRectRegion(tex,
            // Particle.DrawBlendCentered(..., useOffSet:false) places the
            // texture center at Position; weather must not use sprite offsets.
            new Rect2(-img.Width / 2f, -img.Height / 2f, img.Width, img.Height),
            new Rect2(0, 0, img.Width, img.Height), new Color(p.Colour, opacity));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    private void SpawnRain(Vector2 size)
    {
        bool top = _rng.Randf() < 0.8f;
        var position = top
            ? new Vector2(_rng.RandfRange(0, size.X), 1)
            : new Vector2(size.X, _rng.RandfRange(0, size.Y));
        _particles.Add(new WeatherParticle
        {
            TextureIndex = 509, Position = position, Velocity = new Vector2(-1, 5),
            Scale = _rng.RandiRange(1, 2), Rotation = 0.4f,
            LifeMs = _rng.RandiRange(500, 2000)
        });
    }

    private void SpawnSnow(Vector2 size)
    {
        _particles.Add(new WeatherParticle
        {
            TextureIndex = 500,
            Position = new Vector2(_rng.RandfRange(0, size.X), 0),
            Velocity = new Vector2(_rng.RandiRange(-1, 0), 1),
            // old SnowParticle: random.NextDouble() * 1.5F
            Scale = _rng.RandfRange(0f, 1.5f), AngularVelocity = 0.1f,
            LifeMs = _rng.RandiRange(4000, 10000), Fade = true
        });
    }

    private void SpawnFog()
    {
        Vector2 size = LogicalViewport();
        int fogWidth = _library.Images[550]?.Width ?? 128;
        for (int i = 0; i < 4; i++)
            _particles.Add(new WeatherParticle
            {
                TextureIndex = 550,
                Position = new Vector2(size.X / 2f - i * fogWidth * 4f, size.Y / 2f),
                Velocity = new Vector2(1, 0), Scale = 4f, Colour = Colors.DarkGray,
                LifeMs = 3600000
            });
    }

    private void SpawnLightning(Vector2 size)
    {
        _particles.Add(new WeatherParticle
        {
            TextureIndex = 540,
            Position = new Vector2(_rng.RandfRange(0, size.X), 0),
            Velocity = Vector2.Zero, Scale = _rng.RandiRange(1, 3),
            LifeMs = _rng.RandiRange(100, 200), Fade = true, FadeRate = 0.1f
        });
    }

    private int CountKind(int texture) { int n = 0; foreach (var p in _particles) if (p.TextureIndex == texture) n++; return n; }
    private bool Has(Weather value) => ((int)_weather & (int)value) != 0;
    private Vector2 LogicalViewport() => GetViewport().GetVisibleRect().Size / WorldScale;
}
