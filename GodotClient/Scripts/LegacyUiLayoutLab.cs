using System;
using System.Collections.Generic;
using Godot;
using Library;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

/// <summary>
/// 独立的旧版 UI 布局实验场。它只读取 legacy_ui.json 并绘制旧版窗口
/// 参考层，不创建任何正式窗口，也不接管正式业务逻辑。
/// </summary>
public partial class LegacyUiLayoutLab : Control
{
    private const int LogicalWidth = 1024;
    private const int LogicalHeight = 768;
    private readonly List<LegacyUiPreviewWindow> _windows = new();
    private Label _status;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Pass;
        BuildToolbar();
        BuildDesktop();
        QueueRedraw();
    }

    private void BuildToolbar()
    {
        var toolbar = new ColorRect
        {
            Color = new Color("241b13"),
            Position = Vector2.Zero,
            Size = new Vector2(LogicalWidth, 42),
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 1000,
        };
        AddChild(toolbar);

        var title = new Label
        {
            Text = "旧版 UI Layout Lab  ·  800×600 reference",
            Position = new Vector2(14, 7),
            Size = new Vector2(360, 26),
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 1001,
        };
        title.AddThemeColorOverride("font_color", LegacyUiSkin.TitleText);
        toolbar.AddChild(title);

        _status = new Label
        {
            Text = "拖动窗口标题栏；候选窗口以半透明显示",
            Position = new Vector2(395, 9),
            Size = new Vector2(500, 22),
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 1001,
        };
        _status.AddThemeColorOverride("font_color", new Color("b8a581"));
        toolbar.AddChild(_status);
    }

    private void BuildDesktop()
    {
        foreach (var profile in LegacyUiSkin.Windows.Values)
        {
            var preview = new LegacyUiPreviewWindow(profile)
            {
                Position = LegacyUiSkin.ToGodotLocation(profile.Location) + new Vector2(0, 42),
                Size = LegacyUiSkin.ToGodotSize(profile.Size),
                ZIndex = profile.Candidate ? 10 : 20,
            };
            preview.Selected += SelectWindow;
            _windows.Add(preview);
            AddChild(preview);
        }
    }

    private void SelectWindow(LegacyUiPreviewWindow selected)
    {
        foreach (var window in _windows)
            window.ZIndex = window == selected ? 500 : (window.Profile.Candidate ? 10 : 20);
        _status.Text = $"{selected.Profile.Name}  ·  legacy={selected.Profile.Location} {selected.Profile.Size}  ·  frame={selected.Profile.BackgroundFrame}";
    }
}

internal sealed partial class LegacyUiPreviewWindow : Control
{
    public LegacyUiSkin.WindowProfile Profile { get; }
    public event Action<LegacyUiPreviewWindow> Selected;

    private bool _dragging;
    private Vector2 _dragOffset;

    public LegacyUiPreviewWindow(LegacyUiSkin.WindowProfile profile)
    {
        Profile = profile;
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = profile.Name;
    }

    public override void _Draw()
    {
        var texture = MirSkin.GetTexture(Profile.BackgroundLibrary, Profile.BackgroundFrame);
        if (texture != null)
            DrawTextureRect(texture, new Rect2(Vector2.Zero, Size), false,
                Profile.Candidate ? new Color(1f, 1f, 1f, .52f) : Colors.White);
        else
            DrawRect(new Rect2(Vector2.Zero, Size), new Color("32251a"));

        DrawRect(new Rect2(Vector2.Zero, Size), Profile.Candidate
            ? new Color(0.75f, 0.58f, 0.25f, .65f)
            : new Color(0.9f, 0.75f, 0.35f, .95f), false, 2f);
        DrawRect(new Rect2(0, 0, Size.X, 24), new Color(0.05f, 0.03f, 0.02f, .78f));
        DrawString(ThemeDB.FallbackFont, new Vector2(8, 17), Profile.Name,
            HorizontalAlignment.Left, -1, 12, Profile.Candidate ? new Color("cfb47d") : LegacyUiSkin.TitleText);
        DrawString(ThemeDB.FallbackFont, new Vector2(8, Size.Y - 8),
            $"{Profile.BackgroundLibrary}[{Profile.BackgroundFrame}]", HorizontalAlignment.Left, -1, 10,
            new Color(1f, 1f, 1f, .68f));
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton button || button.ButtonIndex != MouseButton.Left) return;
        if (button.Pressed)
        {
            Selected?.Invoke(this);
            _dragging = true;
            _dragOffset = button.Position;
            AcceptEvent();
        }
        else
        {
            _dragging = false;
            AcceptEvent();
        }
    }

    public override void _Process(double delta)
    {
        if (!_dragging) return;
        if (!Input.IsMouseButtonPressed(MouseButton.Left))
        {
            _dragging = false;
            return;
        }
        Position = GetParent<Control>().GetLocalMousePosition() - _dragOffset;
        QueueRedraw();
    }
}
