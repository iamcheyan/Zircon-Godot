using Godot;
using Library;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

/// <summary>
/// 旧版 EI 主 HUD 的独立测试场。
/// 画布固定采用原版 800x600，运行时只做等比缩放，不改变旧版坐标。
/// </summary>
public partial class LegacyHudLayoutLab : Control
{
    private const float LegacyWidth = 800f;
    private const float LegacyHeight = 600f;
    private const float MainPanelY = 465f;
    private CanvasLayer _canvas;

    public override void _Ready()
    {
        _canvas = new CanvasLayer { Layer = 10 };
        AddChild(_canvas);

        var background = new ColorRect
        {
            Color = new Color("11161b"),
            Size = new Vector2(LegacyWidth, LegacyHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _canvas.AddChild(background);

        var hud = new MainPanel
        {
            Location = new Vector2I(0, (int)MainPanelY),
        };
        _canvas.AddChild(hud);
        hud.SetHealth(100);
        hud.SetMana(80);
        hud.SetFocus(30);

        var caption = new Label
        {
            Text = "Legacy EI HUD Layout Lab · 800×600 · GameInter[50]",
            Position = new Vector2(12, 10),
            Size = new Vector2(500, 22),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        caption.AddThemeColorOverride("font_color", new Color("d8bb76"));
        _canvas.AddChild(caption);

        UpdateScale();
        GetViewport().SizeChanged += UpdateScale;
    }

    private void UpdateScale()
    {
        if (_canvas == null) return;
        Vector2 viewport = GetViewportRect().Size;
        if (viewport.X <= 0 || viewport.Y <= 0) return;
        float scale = Mathf.Min(viewport.X / LegacyWidth, viewport.Y / LegacyHeight);
        _canvas.Transform = Transform2D.Identity
            .Translated((viewport - new Vector2(LegacyWidth, LegacyHeight) * scale) / 2f)
            .Scaled(Vector2.One * scale);
    }
}
