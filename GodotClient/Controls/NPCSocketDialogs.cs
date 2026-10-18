using Godot;
using Library;

namespace ZirconClient.Controls;

/// <summary>原版独立 NPCSocketBox（GameInter 5700），不再把开孔面板塞进 NPC 对话框。</summary>
public sealed partial class NPCSocketDialog : DXWindow
{
    public NPCSocketPanel Panel { get; }

    public NPCSocketDialog()
    {
        HasTitle = false;
        HasFooter = false;
        Movable = true;
        Size = new Vector2I(188, 320);
        AddControl(new DXImageControl { LibraryFile = LibraryFile.GameInter, Index = 5700, FixedSize = true, Size = Size, MouseFilter = MouseFilterEnum.Ignore });
        var close = new DXButton { LibraryFile = LibraryFile.Interface, Index = 15 };
        close.Location = new Vector2I(Mathf.RoundToInt(Size.X - close.Size.X - 3), 3);
        close.MouseClick += (o, e) => WindowManager.Close(this);
        AddControl(close);
        var closeDialog = new DXButton { Text = "关闭", Type = DXButton.ButtonType.Default, FontSize = 9, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(102, 280), Size = new Vector2I(70, 24) };
        closeDialog.MouseClick += (o, e) => WindowManager.Close(this);
        AddControl(closeDialog);
        AddControl(new DXLabel { Text = "镶嵌", FontSize = 10, TextColour = new Color(1f, .85f, .3f), DrawOutline = true, OutlineColour = Colors.Black, Align = HorizontalAlignment.Center, VAlign = VerticalAlignment.Center, Location = new Vector2I(0, 8), Size = new Vector2I(188, 18), IsControl = false });
        Panel = new NPCSocketPanel { Location = Vector2I.Zero };
        AddControl(Panel);

        // 2026-10-18：Panel 是最后加入的，会盖住上面的 ✕ 与「关闭」按钮
        // （--hud-ui-audit 悬停自校验实测这两个按钮 unreachable、鼠标命中的是 Panel 自身）
        // → 面板加完再把本窗按钮提到最前。
        foreach (DXControl c in Controls)
            if (c is DXButton b) b.BringToFront();
    }

    public void Result(Library.Network.ServerPackets.NPCSocketItem packet) => Panel.Result(packet);
    public bool TryRouteItem(DXItemCell source) => Panel.TryRouteItem(source);

    public override void Close()
    {
        Panel.Reset();
        base.Close();
    }
}

/// <summary>原版独立 NPCSocketCombineBox（GameInter 5701）。</summary>
public sealed partial class NPCSocketCombineDialog : DXWindow
{
    public NPCSocketCombinePanel Panel { get; }

    public NPCSocketCombineDialog()
    {
        HasTitle = false;
        HasFooter = false;
        Movable = true;
        Size = new Vector2I(192, 326);
        AddControl(new DXImageControl { LibraryFile = LibraryFile.GameInter, Index = 5701, FixedSize = true, Size = Size, MouseFilter = MouseFilterEnum.Ignore });
        var close = new DXButton { LibraryFile = LibraryFile.Interface, Index = 15 };
        close.Location = new Vector2I(Mathf.RoundToInt(Size.X - close.Size.X - 3), 3);
        close.MouseClick += (o, e) => WindowManager.Close(this);
        AddControl(close);
        var closeDialog = new DXButton { Text = "关闭", Type = DXButton.ButtonType.Default, FontSize = 9, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(105, 285), Size = new Vector2I(70, 24) };
        closeDialog.MouseClick += (o, e) => WindowManager.Close(this);
        AddControl(closeDialog);
        AddControl(new DXLabel { Text = "镶嵌合成", FontSize = 10, TextColour = new Color(1f, .85f, .3f), DrawOutline = true, OutlineColour = Colors.Black, Align = HorizontalAlignment.Center, VAlign = VerticalAlignment.Center, Location = new Vector2I(0, 8), Size = new Vector2I(192, 18), IsControl = false });
        Panel = new NPCSocketCombinePanel { Location = Vector2I.Zero };
        AddControl(Panel);
    }

    public void Result(Library.Network.ServerPackets.NPCSocketCombine packet) => Panel.Result(packet);
    public bool TryRouteItem(DXItemCell source) => Panel.TryRouteItem(source);

    public override void Close()
    {
        Panel.Reset();
        base.Close();
    }
}
