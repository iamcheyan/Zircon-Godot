using System;
using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>原版 Client/Scenes/Views/MenuDialog.cs 的菜单窗口。</summary>
public partial class MenuDialog : DXWindow
{
    public DXButton SettingsButton, HelpButton, GuildButton, StorageButton,
        RankingButton, LeaveButton;
    private DXImageControl _background;
    private DXButton _closeButton;
    private DXLabel _titleLabel;

    public MenuDialog()
    {
        HasTitle = false;
        Movable = true;
        HasFooter = false;
        Size = new Vector2I(152, 230);

        _background = new DXImageControl
        {
            LibraryFile = LibraryFile.Interface,
            Index = 279,
            FixedSize = true,
            Size = Size,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddControl(_background);

        _closeButton = new DXButton
        {
            LibraryFile = LibraryFile.Interface,
            Index = 15,
            Location = new Vector2I(Mathf.RoundToInt(Size.X) - 30, 3),
        };
        _closeButton.MouseClick += (o, e) => WindowManager.Close(this);
        AddControl(_closeButton);

        _titleLabel = new DXLabel
        {
            Text = Lang.MenuDialogTitle,
            FontSize = 11,
            TextColour = new Color(1f, 0.85f, 0.3f),
            DrawOutline = true,
            OutlineColour = Colors.Black,
            Align = HorizontalAlignment.Center,
            VAlign = VerticalAlignment.Center,
            AutoSize = false,
            Size = new Vector2I(Mathf.RoundToInt(Size.X), 24),
            IsControl = false,
        };
        AddControl(_titleLabel);

        SettingsButton = AddMenuButton(Lang.MenuDialogSettingsButtonLabel, 40);
        HelpButton = AddMenuButton(Lang.MenuHelpLabel, 70);
        GuildButton = AddMenuButton(Lang.MenuDialogGuildButtonLabel, 100);
        StorageButton = AddMenuButton(Lang.MenuDialogStorageButtonLabel, 130);
        RankingButton = AddMenuButton(Lang.MenuDialogRankingButtonLabel, 160);
        LeaveButton = AddMenuButton(Lang.MenuDialogLeaveButtonLabel, 190);

        StorageButton.MouseClick += (o, e) => GameScene.Game?.ToggleStorageWindow();
        SettingsButton.MouseClick += (o, e) => GameScene.Game?.OpenConfigDialog();
        HelpButton.MouseClick += (o, e) => GameScene.Game?.OpenHelpDialog();
        GuildButton.MouseClick += (o, e) => GameScene.Game?.OpenGuildDialog();
        RankingButton.MouseClick += (o, e) => GameScene.Game?.OpenRankingDialog();
        LeaveButton.MouseClick += (o, e) => GameScene.Game?.OpenExitDialog();
    }

    public void ApplyLegacyEiLayout()
    {
        Size = new Vector2I(248, 264);
        _background.LibraryFile = LibraryFile.GameInter;
        _background.Index = 750;
        _background.Location = new Vector2I(-4, -119);
        _background.Size = MirSkin.GetSize(LibraryFile.GameInter, 750);
        _background.StretchImage = false;
        _titleLabel.Visible = false;

        _closeButton.LibraryFile = LibraryFile.GameInter;
        _closeButton.Index = 161;
        _closeButton.HoverIndex = 162;
        _closeButton.PressedIndex = 162;
        _closeButton.Location = new Vector2I(216, 238);
        _closeButton.Size = new Vector2I(28, 26);
        DXButton[] buttons = { SettingsButton, HelpButton, GuildButton, StorageButton, RankingButton, LeaveButton };
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].Location = new Vector2I(28, 44 + i * 30);
            buttons[i].Size = new Vector2I(192, 25);
            buttons[i].Modulate = new Color(1, 1, 1, 0);
        }
        UpdateClientAreaForLegacySkin();
    }

    private DXButton AddMenuButton(string text, int y)
    {
        var button = new DXButton
        {
            Text = text,
            FontSize = 10,
            TextColour = new Color(1f, 0.85f, 0.3f),
            Size = new Vector2I(100, 25),
            Location = new Vector2I(26, y),
            LibraryFile = LibraryFile.Interface,
            Index = -1,
        };
        AddControl(button);
        return button;
    }
}
