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
    public Color PanelBorder { get; set; } = new Color(0.72f, 0.57f, 0.20f, 0.9f);

    protected override void DrawControl()
    {
        if (PanelFill.HasValue)
        {
            var rect = new Rect2(Vector2.Zero, Size);
            DrawRect(rect, PanelFill.Value);
            DrawRect(rect, PanelBorder, false, 1f);
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
        PanelFill = new Color(0f, 0f, 0f, 0.72f);
        PanelBorder = new Color(0.72f, 0.57f, 0.20f, 0.9f);
        AddControl(new DXLabel { Text = title, FontSize = 10, TextColour = new Color(1f, .85f, .3f), DrawOutline = true, Align = HorizontalAlignment.Center, VAlign = VerticalAlignment.Center, Location = new Vector2I(0, 8), Size = new Vector2I(size.X, 18), IsControl = false });

        int inputX = size.X <= 300 ? 85 : 105;
        int inputWidth = Math.Min(190, size.X - inputX - 30);
        for (int i = 0; i < labels.Length; i++)
        {
            int y = 45 + i * 25;
            AddControl(new DXLabel { Text = labels[i], FontSize = 9, TextColour = new Color(1f, .82f, .5f), Location = new Vector2I(10, y + 3), Size = new Vector2I(inputX - 18, 20), IsControl = false });
            var edit = new DXTextInput { Location = new Vector2I(inputX, y), Size = new Vector2I(inputWidth, 20), Secret = secret != null && i < secret.Length && secret[i] };
            AddControl(edit);
            _inputs.Add(edit);
        }

        if (!string.IsNullOrWhiteSpace(secondary))
        {
            var link = new DXButton { Text = secondary, FontSize = 9, TextColour = new Color(1f, .75f, .25f), Size = new Vector2I(inputWidth, 22), Location = new Vector2I(inputX, 70), LibraryFile = LibraryFile.Interface, Index = -1 };
            link.MouseClick += (o, e) => SecondaryClicked?.Invoke();
            AddControl(link);
        }

        var submit = new DXButton { Text = Lang.LegacyLoginsOkLabel, FontSize = 10, Size = new Vector2I(80, 28), Location = new Vector2I(size.X / 2 - 90, size.Y - 43), LibraryFile = LibraryFile.Interface, Index = -1 };
        submit.MouseClick += (o, e) => Submitted?.Invoke(ReadValues());
        AddControl(submit);
        var cancel = new DXButton { Text = Lang.CommonControlCancel, FontSize = 10, Size = new Vector2I(80, 28), Location = new Vector2I(size.X / 2 + 10, size.Y - 43), LibraryFile = LibraryFile.Interface, Index = -1 };
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
