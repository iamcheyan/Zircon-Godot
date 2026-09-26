using System;
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

        _mount = Action(860, 861, new Vector2I(28, 244), new Vector2I(44, 20), "@上马", () => _state == 0);
        _lead = Action(862, 863, new Vector2I(74, 244), new Vector2I(60, 20), "@遛马", () => _state != 0);
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

    private DXButton Action(int normal, int hover, Vector2I location, Vector2I size, string command,
        Func<bool> allowed = null)
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
        // 原版是点击时判状态、条件不满足则无动作（美术不变、无灰态）。
        button.MouseClick += (_, _) =>
        {
            if (allowed != null && !allowed()) return;
            GameScene.Game?.SendChat(command);
        };
        AddControl(button);
        return button;
    }

    public void SetMountState(int state)
    {
        _state = Mathf.Clamp(state, 0, 3);
        // horse-window-render-evidence.json：原版是**每次点击时判状态** ——
        // 按钮美术始终正常，条件不满足则无动作，**不画任何状态叠加**
        // （state_field_xref.overlay_search 明确「无状态叠加」）。
        // 此前用 DXButton.Enabled 门控，而 DXButton 对禁用态会把整体调暗到
        // 0.32 灰，产生原版没有的灰态。改为按钮恒 Enabled，在点击回调里判。
        _mount.Enabled = true;
        _lead.Enabled = true;
        _hide.Enabled = true;
        _show.Enabled = true;
    }

    public bool AuditLegacyEiLayout(out string details)
    {
        bool ok = Size == new Vector2I(296, 332)
            // G2：原版窗口不按窗口矩形裁子控件、只被 800x600 屏幕裁，
            // ApplyLegacyTestWindow 已统一改为 Clip=false，断言同步更新。
            && !Clip
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
