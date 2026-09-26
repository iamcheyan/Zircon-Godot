using System;
using Godot;
using Library;

namespace ZirconClient.Controls;

/// <summary>
/// 垂直滚动条 (移植自 Client/Controls/DXVScrollBar.cs)。
/// 上/下箭头 (Interface 44/46) + 可拖动滑块 (Interface 45);
/// Value 钳位在 [MinValue, MaxValue - VisibleSize]。
/// </summary>
public partial class DXVScrollBar : DXControl
{
    private int _value;
    public int Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            OnValueChanged();
        }
    }

    /// <summary>Value 越界时回钳 (照原版 OnValueChanged 首行语义)</summary>
    private void OnValueChanged()
    {
        int previous = _value;
        int clamped = Math.Max(MinValue, Math.Min(MaxValue - VisibleSize, Value));
        if (Value != clamped) _value = clamped;
        UpdateScrollBar();
        // Max/Min/VisibleSize 的动态更新也会经过这里，但列表控件通常在
        // 重建行时才设置 MaxValue。只有实际滚动位置变化才通知订阅者，
        // 避免“重建列表 -> MaxValue -> ValueChanged -> 再次重建”的递归，
        // 以及因此留下已 QueueFree 的控件引用。
        if (previous != _value)
            ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private int _maxValue;
    public int MaxValue
    {
        get => _maxValue;
        set
        {
            if (_maxValue == value) return;
            _maxValue = value;
            OnValueChanged();
            MaxValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int _minValue;
    public int MinValue
    {
        get => _minValue;
        set
        {
            if (_minValue == value) return;
            _minValue = value;
            OnValueChanged();
            MinValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int _visibleSize;
    public int VisibleSize
    {
        get => _visibleSize;
        set
        {
            if (_visibleSize == value) return;
            _visibleSize = value;
            OnValueChanged();
            VisibleSizeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool _hideWhenNoScroll;
    public bool HideWhenNoScroll
    {
        get => _hideWhenNoScroll;
        set
        {
            if (_hideWhenNoScroll == value) return;
            _hideWhenNoScroll = value;
            UpdateScrollBar();
            HideWhenNoScrollChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler<EventArgs> ValueChanged;
    public event EventHandler<EventArgs> MaxValueChanged;
    public event EventHandler<EventArgs> MinValueChanged;
    public event EventHandler<EventArgs> VisibleSizeChanged;
    public event EventHandler<EventArgs> HideWhenNoScrollChanged;

    /// <summary>每步滚动量 (滚轮/箭头)</summary>
    public int Change = 10;

    /// <summary>
    /// 旧版 EI 聊天框滚动条：轨道用 GameInter F68 那条锁链按**原生尺寸**绘制，
    /// 不拉伸。F68 是一整条 12x154 的锁链，滑块圆点（橙红，实测均值 RGB
    /// (172,78,21)）已经烤在原生 y 62..77 处，所以只要把整条锁链按滑块位置做
    /// 纵向偏移，圆点就正好落在滑块上并随拖动一起移动 —— 与原版表现一致。
    /// </summary>
    public bool LegacyChainTrack;

    public LibraryFile LegacyChainLibrary = LibraryFile.GameInter;
    public int LegacyChainIndex = 68;
    /// <summary>圆点在锁链贴图里的原生 y（F68 实测 62）。</summary>
    public int LegacyChainDotY = 62;

    public DXButton UpButton, DownButton, PositionBar;

    private int ScrollHeight => Math.Max(0, (int)Size.Y - 50);

    public DXVScrollBar()
    {
        Border = true;
        BorderColour = new Color(0.8f, 0.6f, 0.2f);
        BackColour = Colors.Black;

        UpButton = new DXButton
        {
            LibraryFile = LibraryFile.Interface,
            Index = 44,
            Location = new Vector2I(1, 1),
            Enabled = false,
        };
        UpButton.MouseClick += (o, e) => Value -= Change;
        UpButton.MouseWheel += DoMouseWheel;

        DownButton = new DXButton
        {
            LibraryFile = LibraryFile.Interface,
            Index = 46,
            Location = new Vector2I(1, 0),
            Enabled = false,
        };
        DownButton.MouseClick += (o, e) => Value += Change;
        DownButton.MouseWheel += DoMouseWheel;

        PositionBar = new DXButton
        {
            LibraryFile = LibraryFile.Interface,
            Index = 45,
            Location = new Vector2I(1, 17),
            Enabled = false,
            Movable = true,
            // DXButton rejects left-button input when CanBePressed=false
            // before DXControl can enter its Movable drag path.
            CanBePressed = true,
        };
        PositionBar.Moving += PositionBarMoving;
        PositionBar.MouseWheel += DoMouseWheel;

        AddControl(UpButton);
        AddControl(DownButton);
        AddControl(PositionBar);

        Size = new Vector2I(14, 100);
    }

    public override void _Ready()
    {
        base._Ready();
        ResizeChildren();
        Resized += OnResized;
    }

    private void OnResized()
    {
        ResizeChildren();
    }

    private void ResizeChildren()
    {
        if (ScrollHeight < 0) return;
        DownButton.Location = new Vector2I(UpButton.Location.X, (int)Size.Y - 13);
        UpdateScrollBar();
    }

    private void UpdateScrollBar()
    {
        UpButton.Enabled = Value > MinValue;
        DownButton.Enabled = Value < MaxValue - VisibleSize;
        PositionBar.Enabled = MaxValue - MinValue > VisibleSize;

        int range = MaxValue - MinValue - VisibleSize;
        // 动态窗口可能暂时只有一行高度，不能让滑块位置计算除以 0；
        // 没有可滚动范围时保留在轨道起点并由 Enabled=false 屏蔽拖动。
        if (range > 0 && ScrollHeight > 0)
            PositionBar.Location = new Vector2I(UpButton.Location.X, 16 + (int)(ScrollHeight * ((Value - MinValue) / (float)range)));
        else
            PositionBar.Location = new Vector2I(UpButton.Location.X, 16);

        if (HideWhenNoScroll)
            Visible = UpButton.Enabled || DownButton.Enabled;
    }

    private void PositionBarMoving(object sender, EventArgs e)
    {
        int range = MaxValue - MinValue - VisibleSize;
        if (range <= 0 || ScrollHeight <= 0) return;
        Value = MinValue + (int)Math.Round((PositionBar.Location.Y - 16) * range / (float)ScrollHeight);
    }

    public void DoMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Value -= e.Delta * Change;
    }

    /// <summary>
    /// 旧版锁链轨道：按原生尺寸绘制 F68，并整体纵向偏移，使烤在图里的圆点
    /// 正好落在滑块 PositionBar 的当前位置上（圆点即滑块，可拖拽）。
    /// 只画落在控件矩形内的那一段，避免溢出到聊天面板其它区域。
    /// </summary>
    protected override void DrawControl()
    {
        if (!LegacyChainTrack) return;

        var tex = MirSkin.GetTexture(LegacyChainLibrary, LegacyChainIndex);
        if (tex == null) return;

        var art = MirSkin.GetSize(LegacyChainLibrary, LegacyChainIndex);
        if (art.X <= 0 || art.Y <= 0) return;

        int destX = (int)Size.X - art.X;
        int destY = PositionBar.Location.Y - LegacyChainDotY;

        int top = Math.Max(0, -destY);
        int bottom = Math.Min(art.Y, (int)Size.Y - destY);
        if (bottom <= top) return;

        var src = new Rect2(0, top, art.X, bottom - top);
        var dst = new Rect2(destX, destY + top, art.X, bottom - top);
        DrawTextureRectRegion(tex, dst, src);
    }
}
