using System;
using System.Linq;
using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>
/// 腰带窗口 (移植自 Client/Scenes/Views/BeltDialog.cs):
/// 无标题无边框, 10 格横向 (Globals.MaxBeltCount=10), 每格右上角数字 1-9,0。
/// 格内 QuickInfo/QuickItem 由 UpdateLinks 从 BeltLinks 恢复。
/// </summary>
public partial class BeltDialog : DXWindow
{
    private const int LegacyEiBeltSlots = 6;
    private static readonly Vector2I LegacyEiBeltSize = new(248, 46);
    public ClientBeltLink[] Links;
    public DXItemGrid Grid;
    private DXControl _dragHandle;
    private bool _resizing;
    private bool _draggingHandle;
    private Vector2 _dragStartMouse;
    private Vector2 _dragStartPosition;
    private DXImageControl _background;
    private bool _legacyEiPotionBeltLayout;

    /// <summary>玩家是否拖动过腰带栏; LayoutHud 不能覆盖已自定义的位置。</summary>
    public bool UserMoved { get; private set; }

    /// <summary>
    /// 旧版 EI 的腰带是**固定行**：原版 id12 腰带窗由 hero+0x518E0 槽创建，
    /// ctor `0x427B24`（caption「腰带(Ctrl+Z, Z)」，帧 0x9F/0x9F）与 F51 背景
    /// 都没有任何移动/缩放入口；切换显示靠那个黄色圆钮（GameInter[159]，
    /// HUD 内 hud+393/+13）。用户实机截图亦确认腰带固定贴在底栏上、点黄钮显隐。
    /// → legacy 下禁掉拖动、缩放与位置记忆，每帧强制回默认锚点。
    /// </summary>
    private bool PositionLocked => _legacyEiPotionBeltLayout;

    public BeltDialog()
    {
        Movable = true;
        HasTitle = false;
        HasFooter = false;
        HasTopBorder = false;
        ShowCloseButton = false;
        Size = new Vector2I(10 * (DXItemCell.CellWidth - 1) + 19, DXItemCell.CellHeight - 1 + 13);

        // HasTitle=false 的 DXWindow 没有可拖拽标题栏；专用手柄不覆盖任何格子，
        // 并把移动操作转发到窗口本身。旧版下整条禁用（原版腰带不可拖）。
        _dragHandle = new DXControl
        {
            Name = "BeltDragHandle",
            Location = Vector2I.Zero,
            Size = new Vector2(Size.X, 6),
            IsControl = true,
            BackColour = Colors.Transparent,
            ZIndex = 20,
        };
        _dragHandle.MouseDown += (_, _) =>
        {
            if (PositionLocked) return;
            _draggingHandle = true;
            _dragStartMouse = GetViewport().GetMousePosition() / GameScene.UiScale;
            _dragStartPosition = Position;
            UserMoved = true;
        };
        _dragHandle.MouseUp += (_, _) => FinishHandleDrag();
        _dragHandle.MouseMove += (_, _) => ApplyHandleDrag();
        AddControl(_dragHandle);

        // 恢复上一次拖动后的位置; (-1,-1) 表示首次使用, 由 LayoutHud 给默认锚点。
        // 旧版腰带位置固定，绝不读存档——否则历史误写的坐标会一直把它钉在别处。
        if (!PositionLocked)
        {
            Vector2I saved = ClientSettings.BeltDialogLocation;
            if (saved.X >= 0 && saved.Y >= 0)
            {
                Position = saved;
                UserMoved = true;
            }
        }

        // DXControl.Movable 拖动时触发 Moving; 松开时 MouseUp 持久化。
        Moving += (_, _) => UserMoved = true;
        MouseUp += (_, _) =>
        {
            if (PositionLocked || !UserMoved) return;
            ClientSettings.BeltDialogLocation = new Vector2I((int)Position.X, (int)Position.Y);
            ClientSettings.Save();
        };
        Links = new ClientBeltLink[Globals.MaxBeltCount];
        for (int i = 0; i < Globals.MaxBeltCount; i++)
            Links[i] = new ClientBeltLink { Slot = i };

        Grid = new DXItemGrid
        {
            GridSize = new Vector2I(10, 1),
            Location = new Vector2I(9, 6),
            GridType = GridType.Belt,
            GridPadding = 0,
            BackColour = new Color(0.094f, 0.047f, 0.047f, 1f),
            Border = true,
            BorderColour = new Color(0.39f, 0.325f, 0.196f, 1f),
            ShowCellDividers = true,
        };
        AddControl(Grid);
        RefreshGridLayout();
    }

    /// <summary>旧版坐骑/腰带窗的 GameInter F850 资源布局。</summary>
    public void ApplyLegacyEiLayout()
    {
        Size = new Vector2I(296, 332);
        _background ??= new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 850,
            FixedSize = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        if (_background.GetParent() == null) AddControl(_background);
        _background.LibraryFile = LibraryFile.GameInter;
        _background.Index = 850;
        // 与其余 10 个 legacy 窗相同的约定：把该帧 alpha 可见区原点对齐到窗口 (0,0)。
        // 素材实测 F850 的 alpha bbox 原点 (118,94)，故取 -(118,94)。
        // 原值 (-7,44) 用的是 WIL **header offset**（-headerOffset），不是 bbox 原点 ——
        // 系统性核查（逐个比对代码锚点与素材 alpha bbox）发现全仓只有本处与 NPCDialog 例外。
        _background.Location = new Vector2I(-118, -94);
        _background.Size = MirSkin.GetSize(LibraryFile.GameInter, 850);
        _background.StretchImage = false;
        _dragHandle.Size = new Vector2(Size.X, 6);
        Grid.Visible = false;
        UpdateClientAreaForLegacySkin();
    }

    /// <summary>
    /// EI potion belt layout: native-size GameInter[51] backdrop and six visible
    /// slots. Runtime/server link storage remains unchanged.
    /// </summary>
    public void ApplyLegacyEiPotionBeltLayout()
    {
        _legacyEiPotionBeltLayout = true;
        // 原版腰带不可拖动、不可缩放：Movable/AllowResize 都要断掉，
        // 拖动手柄在 MouseDown 里被 PositionLocked 拦下。
        Movable = false;
        AllowResize = false;
        UserMoved = false;
        if (_dragHandle != null) _dragHandle.MouseFilter = MouseFilterEnum.Ignore;
        DrawChrome = false;
        DropShadow = false;
        Size = LegacyEiBeltSize;
        Grid.GridPadding = 1.5f;
        _background ??= new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 51,
            FixedSize = true,
            StretchImage = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        if (_background.GetParent() == null)
        {
            AddControl(_background);
            MoveChild(_background, 0);
            Controls.Remove(_background);
            Controls.Insert(0, _background);
        }

        _background.LibraryFile = LibraryFile.GameInter;
        _background.Index = 51;
        _background.Location = Vector2I.Zero;
        _background.Size = MirSkin.GetSize(LibraryFile.GameInter, 51);
        _background.StretchImage = false;
        Grid.BackColour = Colors.Transparent;
        Grid.Border = false;
        Grid.ShowCellDividers = false;
        RefreshGridLayout();
        GD.Print($"[LegacyBelt] frame=GameInter[51] art={_background.Size} window={Size} visibleSlots={Grid.Cells?.Length ?? 0} links={Links.Length}");
        QueueRedraw();
    }

    public override void _Ready()
    {
        base._Ready();
        Resized += OnResized;
        RefreshGridLayout();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_draggingHandle && !Input.IsMouseButtonPressed(MouseButton.Left))
            FinishHandleDrag();
    }

    private void ApplyHandleDrag()
    {
        if (!_draggingHandle || PositionLocked) return;
        Vector2 mouse = GetViewport().GetMousePosition() / GameScene.UiScale;
        Vector2 target = _dragStartPosition + mouse - _dragStartMouse;
        Vector2 viewport = GetViewportRect().Size / GameScene.UiScale;
        Position = new Vector2(
            Mathf.Clamp(target.X, 0, Mathf.Max(0, viewport.X - Size.X)),
            Mathf.Clamp(target.Y, 0, Mathf.Max(0, viewport.Y - Size.Y)));
    }
    private void FinishHandleDrag()
    {
        if (!_draggingHandle) return;
        _draggingHandle = false;
        if (PositionLocked) return;
        ClientSettings.BeltDialogLocation = new Vector2I((int)Position.X, (int)Position.Y);
        ClientSettings.Save();
    }


    private void OnResized() => RefreshGridLayout();

    /// <summary>
    /// 未拖动时由 LayoutHud 调用。两种布局的锚点不同：
    /// · EI 复古（--legacy-ui）：**固定在聊天窗正上方**，水平居中于聊天区，
    ///   底边贴 HUD 上沿 —— 原版就是这么放的（不可拖动，永远在那个位置）。
    ///   聊天区取逆向值 LegacyHudLayout.ChatLogLocation/ChatLogSize
    ///   （= EI 的 chat_region (224,492)-(578,566)，相对 HUD 原点 (0,465)）；
    ///   聊天区上沿正中的金色圆钮正在中心，所以"居中"即"跟黄色按钮贴在一起"。
    /// · 现代 Zircon：贴主面板右边缘、右下角对齐（原版 Client/Scenes/GameScene.cs:996）。
    /// </summary>
    public void ApplyDefaultAnchor(Vector2 logicalViewport, Vector2I mainPanelLocation, Vector2 mainPanelSize)
    {
        // 旧版位置固定：忽略 UserMoved，每帧都拉回默认锚点，
        // 这样即便有残留的拖动状态或外部改过 Position 也回得去。
        if (UserMoved && !PositionLocked) return;

        float x;
        if (PositionLocked && AutoLoginArgs.LegacyUi)
        {
            float chatCenter = mainPanelLocation.X + LegacyHudLayout.ChatLogLocation.X
                               + LegacyHudLayout.ChatLogSize.X / 2f;
            x = chatCenter - Size.X / 2f;
        }
        else
        {
            x = mainPanelLocation.X + mainPanelSize.X - Size.X;
        }
        float y = mainPanelLocation.Y - Size.Y;
        // 原版里腰带与 HUD 底板是**紧贴**的（用户 2026-10-16 复报「还有缝」）。
        // 缝不是摆放错位，而是两件素材各自的黑色边距叠在一起：
        //   实测 GameInter[51]（腰带 248x46）：内容到 y=43，最后 2 行纯黑；
        //   实测 GameInter[50]（底板 800x136）：聊天区顶部前 **11 行纯黑**。
        // 直接 bottom 贴 panel.top 会把这两段黑边叠成一道 ~13px 的缝。
        // 故让腰带内容底边去接底板内容顶边：下移 (11+2)=13 逻辑像素。
        // 校验方式：改后在实机截图上量中央列，腰带木纹底边与底板亮边应无黑带。
        if (PositionLocked && AutoLoginArgs.LegacyUi)
            y += LegacyEiBeltDropIntoPanel;
        Position = new Vector2(Mathf.Max(0, x), Mathf.Max(0, y));
    }

    /// <summary>
    /// legacy EI 腰带相对"贴底板顶"要额外下移的像素 = 底板顶部黑带 + 腰带底部黑边。
    /// 依据见 <see cref="ApplyDefaultAnchor"/> 注释（素材逐行实测）。
    /// </summary>
    public const int LegacyEiBeltDropIntoPanel = 13;

    /// <summary>原版 AllowResize：按格子吸附尺寸，并在横向/纵向之间自动切换。</summary>
    public override Vector2I GetAcceptableResize(Vector2 requested)
    {
        int width = Math.Max(1, (int)requested.X);
        int height = Math.Max(1, (int)requested.Y);
        int columns = Math.Max(1, Math.Min(Globals.MaxBeltCount, (int)Math.Ceiling((width - 28) / (double)DXItemCell.CellWidth)));
        int rows = Math.Max(1, Math.Min(Globals.MaxBeltCount, (int)Math.Ceiling((height - 22) / (double)DXItemCell.CellHeight)));

        if (height > width)
            columns = 1;
        else
            rows = 1;

        return new Vector2I(columns * (DXItemCell.CellWidth - 1) + 19,
            rows * (DXItemCell.CellHeight - 1) + 13);
    }

    private void RefreshGridLayout()
    {
        if (Grid == null) return;

        if (_legacyEiPotionBeltLayout)
        {
            Size = LegacyEiBeltSize;
            if (_dragHandle != null) _dragHandle.Size = new Vector2(Size.X, 6);
            Grid.Location = new Vector2I(3, 2);
            bool rebuild = Grid.GridSize != new Vector2I(LegacyEiBeltSlots, 1)
                || Grid.Cells?.Length != LegacyEiBeltSlots;
            Grid.GridSize = new Vector2I(LegacyEiBeltSlots, 1);
            if (!rebuild) return;

            AddSlotLabels();
            UpdateLinks();
            return;
        }

        if (_dragHandle != null) _dragHandle.Size = new Vector2(Size.X, 6);
        Grid.Location = new Vector2I(9, 6);

        int columns = Math.Max(1, Math.Min(Globals.MaxBeltCount,
            (int)Math.Ceiling((Size.X - 28) / (double)DXItemCell.CellWidth)));
        int rows = Math.Max(1, Math.Min(Globals.MaxBeltCount,
            (int)Math.Ceiling((Size.Y - 22) / (double)DXItemCell.CellHeight)));
        if (Size.Y > Size.X) columns = 1;
        else rows = 1;

        int count = columns * rows;
        if (Grid.GridSize == new Vector2I(columns, rows) && Grid.Cells?.Length == count)
            return;

        Grid.GridSize = new Vector2I(columns, rows);
        AddSlotLabels();
        UpdateLinks();
    }

    private void AddSlotLabels()
    {
        if (Grid?.Cells == null) return;
        for (int i = 0; i < Grid.Cells.Length; i++)
        {
            int slot = i;
            var label = new DXLabel
            {
                Text = ((slot + 1) % 10).ToString(),
                FontSize = 8,
                TextColour = new Color(1f, 0.9f, 0.5f),
                DrawOutline = true,
                OutlineColour = Colors.Black,
                Location = new Vector2I(-2, -1),
                IsControl = false,
            };
            Grid.Cells[slot].AddControl(label);
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (!IsEnabled)
        {
            if (e is InputEventMouseButton or InputEventMouseMotion) AcceptEvent();
            return;
        }
        if (PositionLocked)
        {
            // 旧版腰带尺寸固定：右下角不设缩放热区，也不跑 GetAcceptableResize。
            base._GuiInput(e);
            return;
        }
        if (e is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left)
        {
            if (button.Pressed && button.Position.X >= Size.X - 10 && button.Position.Y >= Size.Y - 10)
            {
                _resizing = true;
                AcceptEvent();
                return;
            }

            if (!button.Pressed && _resizing)
            {
                _resizing = false;
                AcceptEvent();
                return;
            }
        }

        if (e is InputEventMouseMotion motion && _resizing)
        {
            Size = GetAcceptableResize(motion.Position);
            RefreshGridLayout();
            AcceptEvent();
            return;
        }

        base._GuiInput(e);
    }

    /// <summary>从 BeltLinks 恢复格子链接 (登录时调用)</summary>
    public void UpdateLinks()
    {
        if (GameScene.Game == null) return;

        // 全量回包可能包含空槽；先清掉旧的 QuickInfo/QuickItem，避免
        // 重连、角色切换或服务端清空腰带后仍显示上一轮链接。
        foreach (var cell in Grid.Cells ?? Array.Empty<DXItemCell>())
        {
            cell.QuickInfo = null;
            cell.QuickItem = null;
        }

        foreach (var link in Links)
        {
            if (link.Slot < 0 || link.Slot >= Grid.Cells.Length) continue;

            if (link.LinkInfoIndex > 0)
            {
                var info = Globals.ItemInfoList.Binding.FirstOrDefault(x => x.Index == link.LinkInfoIndex);
                if (info != null) Grid.Cells[link.Slot].QuickInfo = info;
            }
            else if (link.LinkItemIndex > 0)
            {
                var item = GameScene.Game.Inventory.FirstOrDefault(x => x?.Index == link.LinkItemIndex);
                if (item != null) Grid.Cells[link.Slot].QuickItem = item;
            }
        }
        Grid.RefreshGrid();
    }
}
