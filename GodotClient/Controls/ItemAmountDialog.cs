using System;
using System.Linq;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>旧版 DXItemAmountWindow：拆分堆叠物品时选择实际数量。</summary>
public sealed partial class ItemAmountDialog : DXWindow
{
    private readonly DXTextInput _amount;
    private readonly DXItemCell _itemCell;
    private readonly DXButton _ok;
    public long Amount { get; private set; }
    public DXTextInput AmountBox => _amount;
    public bool OkEnabled => _ok?.Enabled ?? (_handheldAmount != null && Amount > 0);
    private readonly long _max;
    private readonly ClientUserItem _item;
    private readonly Action<long> _confirm;
    private Control _handheldContent;
    private Label _handheldAmount;
    private Panel _handheldField;
    private Button _handheldOk;
    private bool _finished;
    internal Func<bool> ContextValid { get; set; }

    public ItemAmountDialog(ClientUserItem item, Action<long> confirm)
        // 原版 DXItemAmountWindow 在打开时统一从 1 开始，而不是默认半堆。
        : this(item?.Info?.Local() ?? Lang.ConsignmentItemLabel, Math.Max(1, item?.Count ?? 1), 1, confirm, item)
    {
    }

    public ItemAmountDialog(string title, long max, long initial, Action<long> confirm)
        : this(title, max, initial, confirm, null)
    {
    }

    public ItemAmountDialog(string title, long max, long initial, Action<long> confirm, ClientUserItem item)
    {
        Text = Lang.ItemAmountSelectLabel;
        _confirm = confirm;
        _max = Math.Max(1, max);
        // 原版 DXNumberBox 未设置 MinValue（默认 0）：键入 0 合法，红框并
        // 禁用确认；只有确认回调处才要求 Amount > 0。
        Amount = Math.Clamp(initial, 0, _max);
        _item = item;
        if (AutoLoginArgs.Handheld)
        {
            BuildHandheld(title);
            return;
        }

        HasFooter = true;
        BackColour = new Color(0.035f, 0.022f, 0.012f, 1f);
        // 原版 DXItemAmountWindow 的客户区为 200x46；这里保留物品格和数量
        // 控件的原始相对关系，窗口总尺寸按标题/底栏框架计算。
        Size = new Vector2I(218, 146);
        AddControl(new LegacyWindowFrame { Size = Size, HasTitle = true, HasFooter = true });
        _itemCell = null;
        if (item != null)
        {
            _itemCell = new DXItemCell { Location = new Vector2I(18, 38), ReadOnly = true, Border = true };
            _itemCell.ItemGrid = new[] { item };
            _itemCell.Slot = 0;
            AddControl(_itemCell);
        }
        _amount = new DXTextInput { Text = Amount.ToString(), Location = new Vector2I(64, 43), Size = new Vector2I(78, 22), MaxLength = 12 };
        _amount.TextChanged += value => UpdateAmount(value);
        AddControl(_amount);
        // 原版 DXNumberBox.Change = Max(1, Count/5)：上下按钮按总量的
        // 五分之一步进，而不是固定 ±1。
        long step = ComputeStep(_max);
        var up = new DXButton { Text = "▲", FontSize = 8, Type = DXButton.ButtonType.SmallButton, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(144, 42), Size = new Vector2I(20, 12) };
        up.MouseClick += (s, e) => UpdateAmount((Amount + step).ToString());
        AddControl(up);
        var down = new DXButton { Text = "▼", FontSize = 8, Type = DXButton.ButtonType.SmallButton, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(144, 55), Size = new Vector2I(20, 12) };
        down.MouseClick += (s, e) => UpdateAmount((Amount - step).ToString());
        AddControl(down);
        // 原版 ConfirmButton.Enabled = Amount > 0：键入 0 时按钮灰置，
        // DXControl._GuiInput 对禁用控件直接 return，点击不会派发。
        _ok = new DXButton { Text = Lang.LegacyLoginsOkLabel, Type = DXButton.ButtonType.Default, Location = new Vector2I(128, 103), Size = new Vector2I(80, 25), LibraryFile = LibraryFile.Interface, Index = -1, Enabled = Amount > 0 };
        _ok.MouseClick += (s, e) => Confirm();
        AddControl(_ok);
        // 原版 AmountBox_KeyPress：Enter 确认（各调用点还有 window.Amount<=0
        // 防护，这里同样不发包、窗口保留供修正）、Escape 关闭。
        _amount.TextSubmitted += _ => Confirm();
        _amount.Canceled += Cancel;
        _amount.GrabFocus();
        // 原版构造末尾 AmountBox.Value = 1 触发 ValueChanged：边框绿、货币
        // 预览数量立即同步为输入值。
        UpdateAmount(Amount.ToString());
    }

    public override void _Ready()
    {
        base._Ready();
        if (_handheldAmount != null)
        {
            HandheldPromptStyle.Center(this, _handheldContent);
            return;
        }
        Vector2 logicalViewport = GetViewportRect().Size / GameScene.UiScale;
        Position = new Vector2(
            Mathf.Max(0f, (logicalViewport.X - Size.X) / 2f),
            Mathf.Max(0f, (logicalViewport.Y - Size.Y) / 2f));
    }

    /// <summary>Handle semantic input from the parent: A = Enter, B = Escape.</summary>
    internal bool HandleHandheldKey(Key key)
    {
        if (_handheldAmount == null || !IsVisibleInTree()) return false;
        long delta;
        switch (key)
        {
            case Key.Left: delta = -1; break;
            case Key.Right: delta = 1; break;
            case Key.Up: delta = ComputeStep(_max); break;
            case Key.Down: delta = -ComputeStep(_max); break;
            case Key.Enter:
            case Key.KpEnter:
                Confirm();
                return true;
            case Key.Escape:
                Cancel();
                return true;
            default:
                return false;
        }
        // Clamp the delta before adding so a max near long.MaxValue cannot wrap.
        if (!_finished)
            UpdateAmount((Amount + Math.Clamp(delta, -Amount, _max - Amount)).ToString());
        return true;
    }

    private void BuildHandheld(string title)
    {
        _handheldContent = HandheldPromptStyle.BuildFrame(this, 208, Text);
        var subject = HandheldPromptStyle.AddLabel(_handheldContent, title ?? Lang.ConsignmentItemLabel,
            new Rect2(20, 46, 480, 26), 16, HandheldPromptStyle.Paper);
        subject.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _handheldField = new Panel
        {
            Position = new Vector2(20, 76), Size = new Vector2(480, 48), MouseFilter = MouseFilterEnum.Ignore,
        };
        _handheldContent.AddChild(_handheldField);
        _handheldAmount = HandheldPromptStyle.AddLabel(_handheldContent, "", new Rect2(32, 76, 456, 48), 22, HandheldPromptStyle.Paper);
        _handheldAmount.HorizontalAlignment = HorizontalAlignment.Center;
        _handheldAmount.ClipText = true;
        var hint = HandheldPromptStyle.AddLabel(_handheldContent, $"← / →  ±1     ↑ / ↓  ±{ComputeStep(_max)}",
            new Rect2(20, 126, 480, 24), 14, HandheldPromptStyle.Muted);
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        _handheldOk = HandheldPromptStyle.AddButton(_handheldContent, $"A  {Lang.LegacyLoginsOkLabel}", 20, Confirm, bottom: 16);
        HandheldPromptStyle.StyleButton(_handheldOk, selected: true);
        var cancel = HandheldPromptStyle.AddButton(_handheldContent, $"B  {Lang.CommonControlCancel}", 266, Cancel, bottom: 16);
        HandheldPromptStyle.StyleButton(cancel, selected: false);
        UpdateAmount(Amount.ToString());
    }

    private void Cancel()
    {
        if (_finished) return;
        _finished = true;
        WindowManager.Close(this);
    }

    public override void Close()
    {
        _finished = true;
        base.Close();
    }

    private void Confirm()
    {
        if (_finished || Amount <= 0) return;
        _finished = true;
        try { if (ContextValid?.Invoke() != false) _confirm?.Invoke(Amount); }
        finally { WindowManager.Close(this); }
    }

    /// <summary>原版 DXNumberTextBox.TextChanged 的解析钳制：失败回落到 0，
    /// 可解析值钳到 [0, max]。</summary>
    public static long ParseClamp(string value, long max)
    {
        long amount = 0;
        if (long.TryParse(value, out long parsed)) amount = parsed;
        return Math.Clamp(amount, 0, Math.Max(1, max));
    }

    /// <summary>审计/测试入口：等价于用户键入触发 TextChanged（headless 下
    /// LineEdit.text_changed 不派发，生产路径仍由 TextChanged 驱动）。</summary>
    internal void ApplyText(string value) => UpdateAmount(value);

    private void UpdateAmount(string value)
    {
        // 原版 DXNumberTextBox.TextChanged：解析失败回落到 MinValue=0，
        // 可解析值钳制到 [MinValue=0, MaxValue]。
        Amount = ParseClamp(value, _max);
        if (_amount != null && _amount.Text != Amount.ToString()) _amount.Text = Amount.ToString();
        // 原版 DXItemAmountWindow.ValueChanged 边框反馈：<=0 红、等于上限橙、其余绿。
        Color border = BorderColourFor(Amount, _max);
        if (_amount != null) _amount.BorderColour = border;
        if (_handheldAmount != null)
        {
            _handheldAmount.Text = $"{Amount} / {_max}";
            _handheldField.AddThemeStyleboxOverride("panel", HandheldPromptStyle.Box(HandheldPromptStyle.Tile, border));
            _handheldOk.Disabled = Amount <= 0;
        }
        if (_ok != null) _ok.Enabled = Amount > 0;
        if (_item != null && IsCurrencyItem(_item.Info))
        {
            // 原版货币分支：item.Count = Amount 后 RefreshItem 实时刷新预览数量。
            _item.Count = Amount;
            _itemCell?.RefreshItem();
        }
    }

    /// <summary>原版 CEnvir.IsCurrencyItem：物品是任意货币的 DropItem。</summary>
    public static bool IsCurrencyItem(ItemInfo info) => info != null
        && Globals.CurrencyInfoList?.Binding?.FirstOrDefault(x => x.DropItem == info) != null;

    /// <summary>原版 DXNumberBox.Change = Max(1, Count/5) 的步进值。</summary>
    public static long ComputeStep(long max) => Math.Max(1, max / 5);

    /// <summary>原版 DXItemAmountWindow 边框反馈颜色：<=0 红、等于上限橙、其余绿。</summary>
    public static Color BorderColourFor(long amount, long max) => amount <= 0
        ? new Color(1f, .25f, .25f)
        : amount == max
            ? new Color(1f, .65f, .1f)
            : new Color(.3f, .9f, .35f);
}
