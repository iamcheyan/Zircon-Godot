using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>
/// EI 坐骑窗口（GameInter F850）。这是坐骑窗口，不是快捷腰带窗口；
/// 动作按钮的可用状态由旧版 0..3 坐骑状态门控。
/// </summary>
public partial class HorseDialog : DXWindow
{
    private readonly DXImageControl _background;
    private readonly DXButton _mount;
    private readonly DXButton _lead;
    private readonly DXButton _hide;
    private readonly DXButton _show;
    private int _state;

    public HorseDialog()
    {
        HasTitle = false;
        HasFooter = false;
        Movable = true;
        Clip = true;
        Size = new Vector2I(296, 332);

        _background = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 850,
            FixedSize = true,
            // F850's 512×512 canvas has transparent margins. Its alpha bbox is
            // (118,94,275,323); offset the canvas so the visible frame begins
            // at the EI window origin and the baked action labels sit beneath
            // their independently hittable child buttons.
            Location = new Vector2I(-118, -94),
            Size = MirSkin.GetSize(LibraryFile.GameInter, 850),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddControl(_background);

        _mount = Action(860, 861, new Vector2I(28, 244), new Vector2I(44, 20), "@上马");
        _lead = Action(862, 863, new Vector2I(74, 244), new Vector2I(60, 20), "@遛马");
        _hide = Action(864, 865, new Vector2I(133, 244), new Vector2I(60, 20), "@收马");
        _show = Action(866, 867, new Vector2I(192, 244), new Vector2I(56, 20), "@遛马");

        var close = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 161,
            HoverIndex = 162,
            PressedIndex = 162,
            Location = new Vector2I(252, 293),
            Size = new Vector2I(28, 26),
        };
        close.MouseClick += (_, _) => WindowManager.Close(this);
        AddControl(close);
        SetMountState(0);
    }

    private DXButton Action(int normal, int hover, Vector2I location, Vector2I size, string command)
    {
        var button = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = normal,
            HoverIndex = hover,
            PressedIndex = hover,
            Location = location,
            Size = size,
            TooltipText = command,
        };
        button.MouseClick += (_, _) => GameScene.Game?.SendChat(command);
        AddControl(button);
        return button;
    }

    public void SetMountState(int state)
    {
        _state = Mathf.Clamp(state, 0, 3);
        _mount.Enabled = _state == 0;
        _lead.Enabled = _state != 0;
        _hide.Enabled = true;
        _show.Enabled = true;
    }

    public bool AuditLegacyEiLayout(out string details)
    {
        bool ok = Size == new Vector2I(296, 332)
            && Clip
            && _background.Index == 850
            && _background.Location == new Vector2I(-118, -94)
            && _background.Size == new Vector2I(512, 512)
            && _mount.Location == new Vector2I(28, 244)
            && _lead.Location == new Vector2I(74, 244)
            && _hide.Location == new Vector2I(133, 244)
            && _show.Location == new Vector2I(192, 244);
        details = $"size={Size} clip={Clip} background=F{_background.Index}@{_background.Location}/{_background.Size} buttons={_mount.Location},{_lead.Location},{_hide.Location},{_show.Location} state={_state}";
        return ok;
    }
}
