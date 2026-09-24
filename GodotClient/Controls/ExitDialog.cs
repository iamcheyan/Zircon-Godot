using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>Zircon 双操作退出对话框：返回角色选择或退出客户端。</summary>
public partial class ExitDialog : DXWindow
{
    public ExitDialog()
    {
        HasTitle = false;
        HasFooter = false;
        Size = new Vector2I(252, 128);

        AddControl(new DXImageControl
        {
            LibraryFile = LibraryFile.Interface,
            Index = 281,
            FixedSize = true,
            Size = Size,
            MouseFilter = MouseFilterEnum.Ignore,
        });

        var close = new DXButton
        {
            LibraryFile = LibraryFile.Interface,
            Index = 15,
        };
        close.Location = new Vector2I((int)Size.X - (int)close.Size.X - 3, 3);
        close.MouseClick += (o, e) => WindowManager.Close(this);
        AddControl(close);

        AddControl(new DXLabel
        {
            Text = Lang.ExitDialogExitButtonLabel,
            FontSize = 11,
            TextColour = new Color(1f, 0.85f, 0.3f),
            DrawOutline = true,
            OutlineColour = Colors.Black,
            Align = HorizontalAlignment.Center,
            VAlign = VerticalAlignment.Center,
            AutoSize = false,
            Location = new Vector2I(0, 8),
            Size = new Vector2I((int)Size.X, 18),
            IsControl = false,
        });

        var select = CreateButton(Lang.ExitCharacterLabel, 48);
        select.MouseClick += (o, e) => GameScene.Game?.LeaveGame();
        var exit = CreateButton(Lang.ExitExitLabel, 78);
        exit.MouseClick += (o, e) => GameScene.Game?.ExitClient();
    }

    private DXButton CreateButton(string text, int y)
    {
        var button = new DXButton
        {
            Text = text,
            FontSize = 10,
            TextColour = new Color(1f, 0.85f, 0.3f),
            Size = new Vector2I(130, 25),
            Location = new Vector2I(61, y),
            LibraryFile = LibraryFile.Interface,
            Index = -1,
            Type = DXButton.ButtonType.SmallButton,
        };
        AddControl(button);
        return button;
    }
}
