using System;
using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>
/// TrimUI 专用简洁 HUD。它只在 AutoLoginArgs.Handheld 下挂载；桌面和普通 HUD
/// 不共用视觉布局。Start/F10 在两套 HUD 之间切换，状态与当前 GameScene 同步。
/// </summary>
public partial class HandheldHud : Control
{
    private readonly GameScene _game;
    private Label _modeLabel;
    private Label _hintLabel;
    private Label _targetLabel;
    private ColorRect _healthFill;
    private ColorRect _manaFill;
    private ColorRect _focusFill;
    private ColorRect _panel;
    private double _toastUntil;
    private string _toast = "";

    public HandheldHud(GameScene game)
    {
        _game = game;
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(1024, 768);
        ZIndex = 80;
        Build();
        SetProcess(true);
    }

    private void Build()
    {
        _panel = new ColorRect
        {
            Color = new Color(0.02f, 0.03f, 0.06f, 0.78f),
            MouseFilter = MouseFilterEnum.Ignore,
            Position = new Vector2(10, 8),
            Size = new Vector2(190, 48),
        };
        AddChild(_panel);

        _healthFill = AddBar(new Vector2(18, 18), new Color(0.82f, 0.18f, 0.20f, 0.95f));
        _manaFill = AddBar(new Vector2(18, 29), new Color(0.18f, 0.43f, 0.95f, 0.95f));
        _focusFill = AddBar(new Vector2(18, 40), new Color(0.95f, 0.70f, 0.18f, 0.95f));

        _modeLabel = AddLabel(new Vector2(214, 10), "HANDHELD HUD", 13, Colors.White);
        _hintLabel = AddLabel(new Vector2(214, 29), "MENU 手札 · START 界面", 12, new Color(0.85f, 0.88f, 0.95f));
        _targetLabel = AddLabel(new Vector2(10, 62), "", 13, new Color(1f, 0.86f, 0.35f));
    }

    private ColorRect AddBar(Vector2 position, Color color)
    {
        var back = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.75f),
            Position = position,
            Size = new Vector2(150, 6),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(back);
        var fill = new ColorRect
        {
            Color = color,
            Position = position,
            Size = new Vector2(1, 6),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(fill);
        return fill;
    }

    private Label AddLabel(Vector2 position, string text, int size, Color color)
    {
        var label = new Label
        {
            Text = text,
            Position = position,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var font = MirSkin.GetFont();
        if (font != null) label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        AddChild(label);
        return label;
    }

    public void ShowToast(string text)
    {
        _toast = text ?? "";
        _toastUntil = Godot.Time.GetTicksMsec() + 1500;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_game == null) return;
        Scale = Vector2.One / GameScene.UiScale;
        var viewport = _game.GetViewport().GetVisibleRect().Size;
        if (viewport.X <= 0 || viewport.Y <= 0) return;
        _panel.Position = new Vector2(10, 8);
        _modeLabel.Position = new Vector2(214, 10);
        _hintLabel.Position = new Vector2(214, 29);
        _targetLabel.Position = new Vector2(10, viewport.Y - 27);
        _targetLabel.Text = _game.HandheldTargetText;

        float maxHp = Math.Max(1, _game.PlayerStats[Stat.Health]);
        float maxMp = Math.Max(1, _game.PlayerStats[Stat.Mana]);
        float maxFp = Math.Max(1, _game.PlayerStats[Stat.Focus]);
        _healthFill.Size = new Vector2(150 * Mathf.Clamp(_game.CurrentHealth / maxHp, 0, 1), 6);
        _manaFill.Size = new Vector2(150 * Mathf.Clamp(_game.CurrentMana / maxMp, 0, 1), 6);
        _focusFill.Size = new Vector2(150 * Mathf.Clamp(_game.CurrentFocus / maxFp, 0, 1), 6);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_toastUntil > Godot.Time.GetTicksMsec() && !string.IsNullOrWhiteSpace(_toast))
        {
            var viewport = _game.GetViewport().GetVisibleRect().Size;
            var font = MirSkin.GetFont() ?? ThemeDB.FallbackFont;
            var size = font.GetStringSize(_toast, HorizontalAlignment.Left, -1, 14);
            var pos = new Vector2((viewport.X - size.X) / 2f, viewport.Y * 0.22f);
            DrawRect(new Rect2(pos - new Vector2(10, 22), size + new Vector2(20, 32)), new Color(0.02f, 0.03f, 0.06f, 0.88f));
            DrawRect(new Rect2(pos - new Vector2(10, 22), size + new Vector2(20, 32)), new Color(1f, 0.78f, 0.25f, 0.9f), false, 1);
            DrawString(font, pos, _toast, HorizontalAlignment.Left, -1, 14, Colors.White);
        }
    }
}
