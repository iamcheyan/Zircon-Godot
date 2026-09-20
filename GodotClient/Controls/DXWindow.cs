using System;
using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>
/// 窗口 (移植自 Client/Controls/DXWindow.cs 的核心行为):
/// 标题栏 + 子控件容器 + 拖拽移动。背景是子控件贴图 (旧窗口都用
/// DXImageControl { Index = xxx, LibraryFile = Interface } 做背景, 抄窗口时照搬)。
/// </summary>
public abstract partial class DXWindow : DXControl
{
    public static readonly System.Collections.Generic.List<DXWindow> Windows = new();

    public bool HasTitle = true;
    public bool HasTopBorder = true;
    public bool HasFooter;
    public bool SlimFooter;
    /// <summary>
    /// 原版 DXWindow 构造时总会创建关闭按钮；无标题的浮动窗口再显式关闭它。
    /// 已经由具体窗口创建的 Interface[15] 不会重复创建。
    /// </summary>
    public bool ShowCloseButton = true;

    public DXButton DefaultCloseButton { get; private set; }
    public DXLabel TitleLabel => _titleLabel;

    private bool _dropShadow = true;
    private StyleBoxFlat _shadowStyle;

    /// <summary>
    /// 旧版 DXWindow 的 DropShadow 后处理。窗口背景是子控件贴图，
    /// 因此阴影必须在子控件之前绘制，不能直接压在背景图上。
    /// </summary>
    public bool DropShadow
    {
        get => _dropShadow;
        set
        {
            if (_dropShadow == value) return;
            _dropShadow = value;
            QueueRedraw();
        }
    }

    private DXLabel _titleLabel;

    /// <summary>客户区 (相对窗口左上角; 标题栏下方)</summary>
    public Rect2 ClientArea;

    public const int TitleHeight = 24;
    public const int FooterHeight = 20;

    private bool _moving;
    private Vector2 _moveGrabOffset;

    // ---- 原版 AllowResize 边缘缩放 (DXControl.cs:1586-1627) ----
    public bool AllowResize;
    public bool CanResizeWidth = true;
    public bool CanResizeHeight = true;
    private const int ResizeBuffer = 6;
    private const int MinResize = 12;
    private bool _resizing;
    private int _resizeEdges; // 1=left 2=right 4=up 8=down
    private Vector2 _resizeStartMouse;
    private Vector2I _resizeStartSize;
    private Vector2I _resizeStartPos;

    /// <summary>原版 DXControl.GetAcceptableResize: 子类可做格子吸附 (BeltDialog 已实现)。</summary>
    public virtual Vector2I GetAcceptableResize(Vector2 requested)
    {
        return new Vector2I((int)Mathf.Max(MinResize, requested.X), (int)Mathf.Max(MinResize, requested.Y));
    }

    protected DXWindow()
    {
        Windows.Add(this);
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Ready()
    {
        base._Ready();

        if (ShowCloseButton && DefaultCloseButton == null)
        {
            foreach (var control in Controls)
            {
                if (control is DXButton button && button.LibraryFile == LibraryFile.Interface && button.Index == 15)
                {
                    DefaultCloseButton = button;
                    break;
                }
            }

            if (DefaultCloseButton == null)
            {
                DefaultCloseButton = new DXButton
                {
                    Name = "CloseButton",
                    LibraryFile = LibraryFile.Interface,
                    Index = 15,
                    TooltipText = Lang.CommonControlClose,
                    Location = new Vector2I(Mathf.Max(0, (int)Size.X - 30), 3),
                };
                DefaultCloseButton.MouseClick += (_, _) => Visible = false;
                AddControl(DefaultCloseButton);
            }
        }

        if (_titleLabel == null && HasTitle)
        {
            _titleLabel = new DXLabel
            {
                Name = "TitleLabel",
                Text = Text,
                FontSize = 13,
                TextColour = new Color(1f, 0.95f, 0.7f),
                Align = HorizontalAlignment.Center,
                VAlign = VerticalAlignment.Center,
                Location = new Vector2I(30, 4),
                Size = new Vector2(Size.X - 60, TitleHeight - 4),
                MouseFilter = MouseFilterEnum.Ignore,
                ZIndex = 100, // 必须盖在背景贴图之上 (背景是后添加的子控件)
            };
            AddChild(_titleLabel);
        }


        // 布局完成后应用 Web 编辑器 overlay（deferred：等子类 _Ready 尾部的重排
        // 如 InventoryDialog.CenterWeightLabel 跑完再覆盖）。无 overlay 时零开销。
        if (UiOverlay.HasOverrides)
            CallDeferred(nameof(ApplyUiOverlay));
        UpdateClientArea();
    }

    /// <summary>UiOverlay 应用入口（CallDeferred 目标，见 _Ready 尾部）。</summary>
    public void ApplyUiOverlay() => UiOverlay.ApplyWindow(this);

    public override void _Draw()
    {
        if (DropShadow && Size.X > 0 && Size.Y > 0)
        {
            _shadowStyle ??= new StyleBoxFlat
            {
                BgColor = Colors.Transparent,
                ShadowColor = new Color(0f, 0f, 0f, 0.5f),
                ShadowSize = 8,
                ShadowOffset = new Vector2(0f, 2f),
            };
            DrawStyleBox(_shadowStyle, new Rect2(Vector2.Zero, Size));
        }
        base._Draw();
    }

    public new string Text
    {
        get => base.Text;
        set
        {
            base.Text = value;
            if (_titleLabel != null) _titleLabel.Text = value;
        }
    }

    protected void UpdateClientArea()
    {
        float top = HasTitle ? TitleHeight : 0;
        float bottom = Size.Y - (HasFooter ? FooterHeight : 0);
        ClientArea = new Rect2(0, top, Size.X, bottom - top);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        // 标题栏拖动中, 鼠标在窗口外松开时 release 事件不会到达本窗口 -> 补复位。
        if (_moving && !Input.IsMouseButtonPressed(MouseButton.Left)) _moving = false;
        // 边缘缩放同理; 拖动过程用全局鼠标轮询, 避免 _GuiInput motion 在窗口外中断。
        if (_resizing && !Input.IsMouseButtonPressed(MouseButton.Left))
        {
            _resizing = false;
            _resizeEdges = 0;
            return;
        }
        if (_resizing) ApplyResize();
    }

    private void ApplyResize()
    {
        // GetGlobalMousePosition 返回视口坐标，而窗口 Size/Position 使用 UI
        // 逻辑坐标；高 DPI 或 CanvasLayer 缩放时必须换回逻辑位移。
        Vector2 delta = (GetGlobalMousePosition() - _resizeStartMouse) / GameScene.UiScale;
        Vector2I nPos = _resizeStartPos;
        Vector2I nSize = _resizeStartSize;
        // HUD 窗口挂在 _uiLayer (CanvasLayer) 上, Position/Size 是逻辑坐标,
        // 必须除以 UiScale 换算成逻辑画布再钳制; 直接用物理视口会把窗口
        // 放到画布之外 (右/下缘溢出屏幕)。
        Vector2 viewport = GetViewportRect().Size / GameScene.UiScale;

        if ((_resizeEdges & 2) != 0) // right
            nSize.X = _resizeStartSize.X + (int)delta.X;
        if ((_resizeEdges & 8) != 0) // down
            nSize.Y = _resizeStartSize.Y + (int)delta.Y;
        if ((_resizeEdges & 1) != 0) // left
        {
            nPos.X = _resizeStartPos.X + (int)delta.X;
            nSize.X = _resizeStartSize.X - (int)delta.X;
        }
        if ((_resizeEdges & 4) != 0) // up
        {
            nPos.Y = _resizeStartPos.Y + (int)delta.Y;
            nSize.Y = _resizeStartSize.Y - (int)delta.Y;
        }

        Vector2I accept = GetAcceptableResize(new Vector2(nSize.X, nSize.Y));
        nSize = accept;
        // 左侧/上侧拖动时尺寸被吸附 -> 反向修正位置以固定右/下缘
        if ((_resizeEdges & 1) != 0) nPos.X = _resizeStartPos.X + (_resizeStartSize.X - nSize.X);
        if ((_resizeEdges & 4) != 0) nPos.Y = _resizeStartPos.Y + (_resizeStartSize.Y - nSize.Y);

        // 钳制到逻辑视口 (绝不让窗口越过屏幕边缘)
        nPos.X = Mathf.Clamp(nPos.X, 0, Mathf.Max(0, (int)viewport.X - nSize.X));
        nPos.Y = Mathf.Clamp(nPos.Y, 0, Mathf.Max(0, (int)viewport.Y - nSize.Y));

        Position = nPos;
        Size = nSize;
        UpdateClientArea();
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent e)
    {
        if (!IsEnabled)
        {
            if (e is InputEventMouseButton or InputEventMouseMotion) AcceptEvent();
            return;
        }

        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                // 点击窗口任意处置顶 (原版: 可见窗口 BringToFront + 点击激活)
                WindowManager.BringToFront(this);
                // 原版 DXControl.OnMouseDown: AllowResize 时优先判定边缘缩放
                if (AllowResize && !_moving)
                {
                    int edges = 0;
                    if (CanResizeWidth)
                    {
                        if (mb.Position.X < ResizeBuffer) edges |= 1;
                        else if (mb.Position.X > Size.X - ResizeBuffer) edges |= 2;
                    }
                    if (CanResizeHeight)
                    {
                        if (mb.Position.Y < ResizeBuffer) edges |= 4;
                        else if (mb.Position.Y > Size.Y - ResizeBuffer) edges |= 8;
                    }
                    if (edges != 0)
                    {
                        _resizing = true;
                        _resizeEdges = edges;
                        _resizeStartMouse = GetGlobalMousePosition();
                        _resizeStartSize = (Vector2I)Size;
                        _resizeStartPos = (Vector2I)Position;
                        AcceptEvent();
                        return;
                    }
                }
                // 只允许在标题栏区域拖动
                if (HasTitle && mb.Position.Y < TitleHeight)
                {
                    _moving = true;
                    _moveGrabOffset = mb.Position;
                    AcceptEvent();
                    return;
                }
            }
            else
            {
                _moving = false;
                _resizing = false;
                _resizeEdges = 0;
            }
        }
        else if (e is InputEventMouseMotion mm && _moving)
        {
            Vector2 target = Position + mm.Relative;
            Vector2 vp = GetViewport().GetVisibleRect().Size / GameScene.UiScale;
            target.X = Mathf.Clamp(target.X, 0, Mathf.Max(0, vp.X - Size.X));
            target.Y = Mathf.Clamp(target.Y, 0, Mathf.Max(0, vp.Y - Size.Y));
            Position = target;
            AcceptEvent();
            return;
        }

        base._GuiInput(e);
    }

    /// <summary>把窗口挂到场景并显示 (旧客户端由 ActiveScene 管理, Godot 里显式挂载)</summary>
    public void ShowWindow(Node parent)
    {
        if (GetParent() == null)
        {
            parent.AddChild(this);
        }
        Visible = true;
        BringToFront();
        QueueRedraw();
    }

    public virtual void Close()
    {
        _moving = false;
        Visible = false;
        if (GetParent() != null)
        {
            GetParent().RemoveChild(this);
        }
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        _moving = false;
    }
}
