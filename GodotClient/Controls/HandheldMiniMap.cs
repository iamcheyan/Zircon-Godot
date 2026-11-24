using Godot;
using Library;

namespace ZirconClient.Controls;

/// <summary>只读的掌机 HUD 小地图。地图保持北朝上，以玩家为中心按纹理原尺寸取景。</summary>
public partial class HandheldMiniMap : Control
{
    public MiniMapDialog Source { get; set; }
    public MirDirection PlayerDirection { get; set; }

    private static readonly Color RimColor = new(0.85f, 0.72f, 0.48f);
    private readonly TextureRect _map;
    private readonly Control _overlay;
    private readonly ShaderMaterial _mapMaterial, _overlayMaterial;
    private readonly Vector2[] _arrow = new Vector2[3];
    private MiniMapDialog _textureSource;
    private (LibraryFile Library, int Frame) _textureKey;
    private Vector2 _mapCenter;
    private bool _hasPlayer;

    public HandheldMiniMap()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
        _mapMaterial = new ShaderMaterial { Shader = new Shader { Code = """
            shader_type canvas_item;
            render_mode unshaded;
            uniform vec2 view_size;
            uniform vec2 map_center;
            varying vec4 tint;

            void vertex() {
                tint = COLOR;
            }

            void fragment() {
                vec2 delta = (UV - vec2(0.5)) * view_size;
                vec2 map_uv = (map_center + delta) * TEXTURE_PIXEL_SIZE;
                float radius = min(view_size.x, view_size.y) * 0.5;
                if (dot(delta, delta) > radius * radius ||
                    any(lessThan(map_uv, vec2(0.0))) || any(greaterThanEqual(map_uv, vec2(1.0)))) {
                    discard;
                }
                COLOR = tint * texture(TEXTURE, map_uv);
            }
            """ } };
        _map = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            TextureFilter = TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore,
            Material = _mapMaterial,
            Visible = false,
        };
        AddChild(_map);

        // Use local positions, not UVs: the draw list also contains sprites and text.
        _overlayMaterial = new ShaderMaterial { Shader = new Shader { Code = """
            shader_type canvas_item;
            render_mode unshaded;
            uniform vec2 view_size;
            varying vec2 local_position;

            void vertex() {
                local_position = VERTEX;
            }

            void fragment() {
                vec2 delta = local_position - view_size * 0.5;
                float radius = min(view_size.x, view_size.y) * 0.5;
                if (dot(delta, delta) > radius * radius) {
                    discard;
                }
            }
            """ } };
        _overlay = new Control { MouseFilter = MouseFilterEnum.Ignore, Material = _overlayMaterial };
        _overlay.Draw += DrawOverlay;
        AddChild(_overlay);

        Resized += UpdateGeometry;
        Size = new Vector2(82, 82);
    }

    private void UpdateGeometry()
    {
        _map.Size = _overlay.Size = Size;
        _mapMaterial.SetShaderParameter("view_size", Size);
        _overlayMaterial.SetShaderParameter("view_size", Size);
        _overlay.QueueRedraw();
    }

    public override void _Ready()
    {
        UpdateGeometry();
        RefreshMap();
    }

    public override void _Process(double delta)
    {
        if (IsVisibleInTree()) RefreshMap();
    }

    private void RefreshMap()
    {
        var source = IsInstanceValid(Source) && IsInstanceValid(Source.Image) ? Source : null;
        var image = source?.Image;
        var key = image is { DrawImage: true, Index: > 0 } ? (image.LibraryFile, image.Index) : default;
        if (_textureSource != source || _textureKey != key)
        {
            _textureSource = source;
            _textureKey = key;
            _map.Texture = source?.HandheldTexture;
            _map.Visible = _map.Texture != null;
            _overlay.QueueRedraw();
        }

        if (!_map.Visible) return;
        var playerCenter = source.HandheldPlayerCenter;
        _hasPlayer = playerCenter.HasValue;
        var center = playerCenter ?? _map.Texture.GetSize() * 0.5f;
        if (_mapCenter != center)
        {
            _mapCenter = center;
            _mapMaterial.SetShaderParameter("map_center", _mapCenter);
        }
        _overlay.QueueRedraw();
    }

    private void DrawOverlay()
    {
        var center = Size * 0.5f;
        float radius = Mathf.Min(center.X, center.Y);
        if (radius <= 1f) return;

        if (_map.Visible && IsInstanceValid(_textureSource))
        {
            var origin = center - _mapCenter;
            foreach (var pair in _textureSource.HandheldObjectMarkers)
                if (pair.Key != _textureSource.HandheldPlayerObjectID)
                    DrawMarker(pair.Value, origin, center, radius);
            foreach (var marker in _textureSource.HandheldStaticMarkers)
                if (marker is DXControl control)
                    DrawMarker(control, origin, center, radius);

            const int fontSize = 10;
            var font = ThemeDB.FallbackFont;
            float halfWidth = font.GetStringSize("N", fontSize: fontSize).X * 0.5f;
            var north = new Vector2(center.X - halfWidth, center.Y - radius + 3f + font.GetAscent(fontSize));
            _overlay.DrawStringOutline(font, north, "N", fontSize: fontSize, size: 2, modulate: Colors.Black);
            _overlay.DrawString(font, north, "N", fontSize: fontSize, modulate: RimColor);

            if (_hasPlayer) DrawPlayer(center);
        }
        else
        {
            _overlay.DrawLine(center - new Vector2(3, 0), center + new Vector2(3, 0), RimColor, 1.5f, true);
        }

        _overlay.DrawArc(center, radius - 1f, 0f, Mathf.Tau, 64, RimColor, 1.5f, true);
    }

    private void DrawPlayer(Vector2 center)
    {
        var forward = Vector2.Up.Rotated((int)PlayerDirection * (Mathf.Pi / 4f));
        var side = forward.Orthogonal();
        _arrow[0] = center + forward * 8f;
        _arrow[1] = center - forward * 4f + side * 4.5f;
        _arrow[2] = center - forward * 4f - side * 4.5f;
        _overlay.DrawColoredPolygon(_arrow, Colors.Black);
        // The triangle's centroid is at the player position; inset the fill around it.
        for (int i = 0; i < _arrow.Length; i++)
            _arrow[i] = center + (_arrow[i] - center) * 0.75f;
        _overlay.DrawColoredPolygon(_arrow, Colors.White);
    }

    private void DrawMarker(DXControl marker, Vector2 origin, Vector2 center, float radius)
    {
        // Visible (not IsVisibleInTree): the HUD can be active while the desktop dialog is hidden.
        if (!IsInstanceValid(marker) || !marker.Visible) return;
        var pos = origin + marker.Position + marker.Size * 0.5f;
        if (pos.DistanceSquaredTo(center) > (radius + 6f) * (radius + 6f)) return;

        switch (marker)
        {
            case DXImageControl icon when icon.DrawImage:
                var tex = MirSkin.GetTexture(icon.LibraryFile, icon.Index);
                if (tex != null)
                {
                    var size = tex.GetSize();
                    size *= Mathf.Min(1f, 8f / Mathf.Max(size.X, size.Y));
                    _overlay.DrawTextureRect(tex, new Rect2(pos - size * 0.5f, size), false, icon.ForeColour);
                }
                else
                    _overlay.DrawCircle(pos, 2f, icon.ForeColour, antialiased: true);
                break;
            case DXLabel label:
                const int fontSize = 10;
                var font = ThemeDB.FallbackFont;
                var textSize = font.GetStringSize(label.Text, fontSize: fontSize);
                var baseline = pos + new Vector2(-textSize.X * 0.5f, (font.GetAscent(fontSize) - font.GetDescent(fontSize)) * 0.5f);
                _overlay.DrawStringOutline(font, baseline, label.Text, fontSize: fontSize, size: 2, modulate: Colors.Black);
                _overlay.DrawString(font, baseline, label.Text, fontSize: fontSize, modulate: label.TextColour);
                break;
            case DXMapInfoControl dot:
                _overlay.DrawCircle(pos, 1.5f, dot.BackColour, antialiased: true);
                break;
        }
    }
}
