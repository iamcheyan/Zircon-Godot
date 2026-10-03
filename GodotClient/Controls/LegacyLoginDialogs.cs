using System;
using System.Collections.Generic;
using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>登录页原版辅助窗口的共同布局：输入行、主按钮、取消按钮和可选的次级链接。</summary>
public sealed partial class LegacyLoginDialog : DXWindow
{
    private readonly List<DXTextInput> _inputs = new();
    public IReadOnlyList<DXTextInput> Inputs => _inputs;
    public event Action<IReadOnlyList<string>> Submitted;
    public event Action SecondaryClicked;

    /// <summary>面板底色（半透明黑）。null = 不自绘面板底。</summary>
    public Color? PanelFill { get; set; }
    /// <summary>面板描边色。</summary>
    public Color PanelBorder { get; set; } = new Color(1f, 1f, 1f, 0.10f);

    /// <summary>
    /// 居中到当前视口。构造时场景可能还没挂上树，所以延迟一帧再做；
    /// 每次打开（Visible 变化）也重算一次，适配窗口尺寸变化。
    /// </summary>
    public override void _Ready()
    {
        base._Ready();
        CenterOnViewport();
        VisibilityChanged += CenterOnViewport;
        Resized += CenterOnViewport;
    }

    private void CenterOnViewport()
    {
        Vector2 viewport = GetViewportRect().Size;
        if (viewport.X <= 0 || viewport.Y <= 0) return;
        Position = new Vector2(
            Mathf.RoundToInt((viewport.X - Size.X) / 2f),
            Mathf.RoundToInt((viewport.Y - Size.Y) / 2f));
        GD.Print($"[LegacyLoginDialog] centered={Position}/{Size} chrome={DrawChrome} panel={PanelFill.HasValue}");
    }

    protected override void DrawControl()
    {
        if (PanelFill.HasValue)
        {
            var rect = new Rect2(Vector2.Zero, Size);
            DrawRect(rect, PanelFill.Value);
            if (PanelBorder.A > 0f) DrawRect(rect, PanelBorder, false, 1f);
        }
        base.DrawControl();
    }

    public LegacyLoginDialog(string title, Vector2I size, string[] labels, bool[] secret = null, string secondary = null)
    {
        HasTitle = false;
        HasFooter = false;
        // 不画 DXWindow 默认 chrome：它用 `LibraryFile.Interface` 合成 Zircon 现代
        // 窗口金边与底色（`DXWindow.DrawWindowChrome`），而 legacy 模式下该库在 EI
        // 素材目录不存在、每次回退到 mir2ei/Data/Interface.Zl，于是这些表单在
        // legacy 登录屏上呈现现代风格，与周围格格不入。
        // 原版没有这些窗口 —— 注册/改密是点按钮开浏览器
        // (mir2ei.com/service/new_user_regi 与 /Modify_pwd，见 Mir3.exe
        //  0x4043B0 / 0x40442F)，没有原版外观可抄。改用统一半透明黑底，
        // 让底层登录背景透出来。
        DrawChrome = false;
        DropShadow = false;
        Size = size;
        PanelFill = new Color(0f, 0f, 0f, 0.82f);
        // 纯黑半透明，不加金边：表单只是工具面板，不需要 legacy 装饰语言。
        PanelBorder = new Color(1f, 1f, 1f, 0.08f);
        AddControl(new DXLabel { Text = title, FontSize = 11, TextColour = new Color(0.92f, 0.92f, 0.92f), DrawOutline = true, Align = HorizontalAlignment.Center, VAlign = VerticalAlignment.Center, Location = new Vector2I(0, 8), Size = new Vector2I(size.X, 18), IsControl = false });

        int inputX = size.X <= 300 ? 85 : 105;
        int inputWidth = Math.Min(190, size.X - inputX - 30);
        for (int i = 0; i < labels.Length; i++)
        {
            int y = 45 + i * 25;
            AddControl(new DXLabel { Text = labels[i], FontSize = 9, TextColour = new Color(0.85f, 0.85f, 0.85f), Location = new Vector2I(10, y + 3), Size = new Vector2I(inputX - 18, 20), IsControl = false });
            var edit = new DXTextInput { Location = new Vector2I(inputX, y), Size = new Vector2I(inputWidth, 20), Secret = secret != null && i < secret.Length && secret[i] };
            AddControl(edit);
            _inputs.Add(edit);
        }

        if (!string.IsNullOrWhiteSpace(secondary))
        {
            var link = new DXButton { Text = secondary, FontSize = 9, TextColour = new Color(0.72f, 0.78f, 0.9f), Size = new Vector2I(inputWidth, 22), Location = new Vector2I(inputX, 70), Index = -1 };
            link.MouseClick += (o, e) => SecondaryClicked?.Invoke();
            AddControl(link);
        }

        var submit = new DXButton { Text = Lang.LegacyLoginsOkLabel, FontSize = 10, TextColour = new Color(0.95f, 0.95f, 0.95f), Size = new Vector2I(80, 28), Location = new Vector2I(size.X / 2 - 90, size.Y - 43), Index = -1 };
        submit.MouseClick += (o, e) => Submitted?.Invoke(ReadValues());
        AddControl(submit);
        var cancel = new DXButton { Text = Lang.CommonControlCancel, FontSize = 10, TextColour = new Color(0.78f, 0.78f, 0.78f), Size = new Vector2I(80, 28), Location = new Vector2I(size.X / 2 + 10, size.Y - 43), Index = -1 };
        cancel.MouseClick += (o, e) => WindowManager.Close(this);
        AddControl(cancel);
    }

    public string[] ReadValues()
    {
        var values = new string[_inputs.Count];
        for (int i = 0; i < values.Length; i++) values[i] = _inputs[i].Text.Trim();
        return values;
    }
}
