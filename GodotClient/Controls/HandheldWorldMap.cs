using System;
using System.Collections.Generic;
using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>Read-only, north-up map and player status for the handheld Select shortcut.</summary>
internal partial class HandheldWorldMap : Control
{
    private const int StatusHeight = 64;
    private const int FontSize = 12;
    private static readonly Color Background = new("101a15"), Ink = new("14221b"), Paper = new("f1eddb"),
        Muted = new("aebaa6"), Gold = new("dfc57e"), Health = new("bb695d"), Mana = new("619eb4");
    private readonly GameScene _game;
    private readonly Font _font;
    private readonly Control _markers;
    private readonly Vector2[] _arrow = new Vector2[3];
    private readonly List<int> _buffIcons = new();
    private Texture2D _texture;
    private MiniMapDialog _source;
    private Rect2 _terrain;

    internal HandheldWorldMap(GameScene game)
    {
        _game = game;
        _font = MirSkin.GetFont() ?? ThemeDB.FallbackFont;
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _markers = new Control { ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
        _markers.Draw += DrawMarkers;
        AddChild(_markers);
    }

    internal static Rect2 FitMap(Vector2 available, Vector2 texture)
    {
        if (available.X <= 0 || available.Y <= 0 || texture.X <= 0 || texture.Y <= 0) return default;
        // Use the whole area and the whole texture. Project markers with the same
        // horizontal and vertical scale, even when the map and screen ratios differ.
        return new Rect2(Vector2.Zero, available);
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        _source = _game.HandheldMiniMapSource;
        _texture = _source?.HandheldTexture;
        _terrain = _texture == null ? default : FitMap(new Vector2(Size.X, Size.Y - StatusHeight), _texture.GetSize());
        _markers.Position = _terrain.Position;
        _markers.Size = _terrain.Size;
        _markers.QueueRedraw();
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Background);
        if (_texture != null && _terrain.HasArea()) DrawTextureRect(_texture, _terrain, false);
        else Write(new Vector2(12, 24), "此地图暂无地图图像", Gold);
        DrawStatus(Mathf.Max(0, Size.Y - StatusHeight));
    }

    private void DrawMarkers()
    {
        if (_texture == null || !_terrain.HasArea() || !IsInstanceValid(_source)) return;
        var scale = _terrain.Size / _texture.GetSize();
        foreach (var pair in _source.HandheldObjectMarkers)
            if (pair.Key != _source.HandheldPlayerObjectID) DrawMarker(pair.Value);
        foreach (var marker in _source.HandheldStaticMarkers)
            if (marker is DXControl control) DrawMarker(control);
        if (_source.HandheldPlayerCenter is { } center)
        {
            var inset = _terrain.Size.Min(new Vector2(24, 24)) * .5f;
            var position = (center * scale).Clamp(inset, _terrain.Size - inset);
            HandheldMiniMap.DrawPlayerArrow(_markers, _arrow, position, _game.HandheldPlayerDirection, 1.35f);
        }

        void DrawMarker(DXControl marker)
        {
            if (IsInstanceValid(marker) && marker.Visible)
                HandheldMiniMap.DrawMapMarker(_markers, marker, (marker.Position + marker.Size * .5f) * scale);
        }
    }

    private void DrawStatus(float top)
    {
        var player = _game.HandheldMapStatus();
        float y = top + 15;
        DrawRect(new Rect2(0, top, Size.X, StatusHeight), Ink);
        DrawLine(new Vector2(0, top), new Vector2(Size.X, top), new Color("657560"));
        Write(new Vector2(12, y), player.Map, Gold, Size.X * .24f - 16);
        Write(new Vector2(Size.X * .25f, y), $"{player.X}, {player.Y}", Paper);
        Write(new Vector2(Size.X * .42f, y), player.Character, Paper, Size.X * .35f);
        const string close = "SELECT / B 返回";
        Write(new Vector2(Size.X - 12 - _font.GetStringSize(close, fontSize: FontSize).X, y), close, Muted);

        float width = Mathf.Floor(Size.X * .21f);
        DrawResource(new Vector2(12, top + 23), width, "生命", _game.CurrentHealth, _game.PlayerStats[Stat.Health], Health);
        DrawResource(new Vector2(22 + width, top + 23), width, "法力", _game.CurrentMana, _game.PlayerStats[Stat.Mana], Mana);
        float statusX = 32 + width * 2;
        if (_game.PlayerStats[Stat.Focus] > 0)
        {
            DrawResource(new Vector2(statusX, top + 23), width * .72f, "专注", _game.CurrentFocus, _game.PlayerStats[Stat.Focus], Gold);
            statusX += width * .72f + 10;
        }
        Write(new Vector2(statusX, top + 35), player.State, player.Warning ? Health : Muted, Size.X - statusX - 12);
        DrawBuffs(12, top + 45);
    }

    private void DrawBuffs(float x, float y)
    {
        _game.FillHandheldHudBuffs(_buffIcons);
        var buffs = _buffIcons;
        if (buffs.Count == 0) return;
        Write(new Vector2(x, y + 12), "效果", Muted);
        x += 34;
        int slots = Mathf.Max(0, Mathf.FloorToInt((Size.X - x - 12) / 17));
        int shown = buffs.Count <= slots ? buffs.Count : Math.Max(0, slots - 2);
        for (int i = 0; i < shown; i++)
        {
            var icon = MirSkin.GetTexture(LibraryFile.CBIcon, buffs[i]);
            var bounds = new Rect2(x + i * 17, y, 14, 14);
            if (icon != null) DrawTextureRect(icon, bounds, false);
            else DrawCircle(bounds.GetCenter(), 3, Gold);
        }
        if (shown < buffs.Count && slots >= 2)
            Write(new Vector2(x + shown * 17, y + 12), $"+{buffs.Count - shown}", Gold);
    }

    private void DrawResource(Vector2 at, float width, string name, int current, int capacity, Color color)
    {
        string value = capacity > 0 ? $"{name} {current}/{capacity}" : $"{name} {current}";
        Write(at + new Vector2(0, 12), value, Paper, width);
        var bar = new Rect2(at + new Vector2(0, 16), new Vector2(width, 3));
        DrawRect(bar, new Color("374639"));
        bar.Size = new Vector2(Mathf.Floor(width * HandheldHud.ResourceFraction(current, capacity)), 3);
        if (bar.HasArea()) DrawRect(bar, color);
    }

    private void Write(Vector2 position, string text, Color color, float available = -1)
    {
        if (string.IsNullOrEmpty(text) || (available != -1 && available < 12)) return;
        if (available > 0 && _font.GetStringSize(text, fontSize: FontSize).X > available)
        {
            const string ellipsis = "…";
            while (text.Length > 0 && _font.GetStringSize(text + ellipsis, fontSize: FontSize).X > available)
                text = text[..^1];
            text += ellipsis;
        }
        DrawString(_font, position, text, fontSize: FontSize, modulate: color);
    }
}
