using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>
/// 背包窗口 (移植自 Client/Scenes/Views/InventoryDialog.cs):
/// Interface 130 底图, 6x8 物品格 + 权重条 + 金币标签 + 排序/移除按钮。
/// </summary>
public partial class InventoryDialog : DXWindow
{
    public DXItemGrid Grid;
    public DXButton SortButton, TrashButton, SellButton, CloseButton;
    public DXControl WalletButton;
    public DXControl WeightBar;
    public DXLabel WeightLabel, GoldLabel, GgLabel;
    public readonly List<DXItemCell> SelectedItems = new();
    public readonly List<ItemType> SellableItemTypes = new();

    public InventoryMode InvMode { get; private set; } = InventoryMode.Normal;
    public bool IsSellMode => InvMode == InventoryMode.Sell;
    public bool IsRepairMode => InvMode == InventoryMode.Repair;
    public bool IsStorageMode => InvMode == InventoryMode.Storage;

    private bool _weightInit;
    private CurrencyInfo _primaryCurrency;
    private DXImageControl _background;
    private DXButton _legacyActionButton;
    // 原版背包的三个子控件（关闭钮 F161/162、动作钮 F264/265、模式位 F267/268）
    // 都属共享按钮类（类构造 0x404690，release 处理 0x4177F0），点击命中时**只播音**：
    // 0x41780C-0x417819 `push 0x69; mov ecx,0x8AB130; call 0x45AFC0` → 音效索引 0x69=105。
    // 端口 sounds.json 中 105 对应 `SoundIndex.ButtonC`（ButtonA=103.wav、ButtonB=104.wav）。
    // 模式位（原版 F267/268 为**空帧**，故默认模式两边都不画美术）在端口是 DXImageControl，
    // 无点击能力，这里补一个透明热区以还原「点击播音」。
    private DXButton _legacyModeTabHotspot;
    private DXLabel _legacyModeLabel;
    private DXLabel _legacyWeightValue;
    private DXImageControl _legacyModeArt;
    private DXLabel _titleLabel, _goldTitle, _ggTitle;
    private DXImageControl _legacyScrollTrack;
    private DXControl _legacyScrollClip;
    private DXVScrollBar _legacyScrollBar;
    private LegacyGaugeDragSurface _legacyGaugeDrag;

    private readonly List<CellLinkInfo> _pendingSellLinks = new();
    private bool _legacyEiLayout;

    public InventoryDialog()
    {
        // 原版 InventoryDialog 直接使用 Interface 130 背景图。
        HasTitle = false;
        Movable = true;
        Text = Lang.InventoryDialogTitle;
        Size = new Vector2I(264, 436);

        _background = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 250,
            FixedSize = true,
            StretchImage = true,
            Size = Size,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddControl(_background);

        // 原版虽然没有 DXWindow 标题栏，但背景图内部仍有独立的标题文字层。
        // 不能只给 DXWindow.Text 赋值，否则 HasTitle=false 时不会绘制标题。
        _titleLabel = new DXLabel
        {
            Text = Lang.InventoryDialogTitle,
            FontSize = 10,
            TextColour = new Color(1f, .85f, .3f),
            DrawOutline = true,
            OutlineColour = Colors.Black,
            Align = HorizontalAlignment.Center,
            VAlign = VerticalAlignment.Center,
            AutoSize = false,
            Location = new Vector2I(52, 8),
            Size = new Vector2I(160, 18),
            IsControl = false,
        };
        AddControl(_titleLabel);

        CloseButton = new DXButton
        {
            LibraryFile = LibraryFile.Interface,
            Index = 15,
        };
        CloseButton.Location = new Vector2I((int)Size.X - (int)CloseButton.Size.X - 3, 3);
        CloseButton.MouseClick += (o, e) => WindowManager.Close(this);
        AddControl(CloseButton);

        Grid = new DXItemGrid
        {
            GridSize = new Vector2I(6, 8),
            Location = new Vector2I(20, 39),
            GridPadding = 1,
            GridType = GridType.Inventory,
            ItemGrid = null, // GameScene 注入
        };
        AddControl(Grid);

        WeightBar = new DXControl
        {
            Location = new Vector2I(53, 355),
            Size = MirSkin.GetSize(LibraryFile.GameInter, 360),
        };
        WeightBar.BeforeDraw += DrawWeightFill;
        AddControl(WeightBar);

        WeightLabel = new DXLabel
        {
            TextColour = Colors.White,
            DrawOutline = true,
            OutlineColour = Colors.Black,
            Align = HorizontalAlignment.Center,
            VAlign = VerticalAlignment.Center,
            AutoSize = false,
            Size = new Vector2I(200, 18),
            IsControl = false,
        };
        AddControl(WeightLabel);

        _goldTitle = new DXLabel
        {
            Text = Lang.InventoryDialogPrimaryCurrencyTitle,
            TextColour = new Color(0.85f, 0.68f, 0.2f),
            Location = new Vector2I(55, 381),
            AutoSize = false,
            Size = new Vector2I(97, 20),
            FontSize = 8,
            VAlign = VerticalAlignment.Center,
            IsControl = false,
        };
        AddControl(_goldTitle);

        GoldLabel = new DXLabel
        {
            Text = "0",
            TextColour = Colors.White,
            Location = new Vector2I(80, 381),
            AutoSize = false,
            Size = new Vector2I(97, 20),
            FontSize = 8,
            Align = HorizontalAlignment.Right,
            VAlign = VerticalAlignment.Center,
        };
        GoldLabel.MouseClick += (o, e) => GameScene.Game?.SelectCurrency(
            GameScene.Game.Currencies.FirstOrDefault(x => x.Info?.Type == CurrencyType.Gold));
        AddControl(GoldLabel);

        _ggTitle = new DXLabel
        {
            Text = "GG",
            TextColour = new Color(1f, 0.55f, 0.2f),
            Location = new Vector2I(55, 400),
            AutoSize = false,
            Size = new Vector2I(97, 20),
            FontSize = 8,
            VAlign = VerticalAlignment.Center,
            IsControl = false,
        };
        AddControl(_ggTitle);

        GgLabel = new DXLabel
        {
            Text = "0",
            TextColour = Colors.White,
            Location = new Vector2I(80, 400),
            AutoSize = false,
            Size = new Vector2I(97, 20),
            FontSize = 8,
            Align = HorizontalAlignment.Right,
            VAlign = VerticalAlignment.Center,
        };
        GgLabel.MouseClick += (o, e) => GameScene.Game?.SelectCurrency(
            GameScene.Game.Currencies.FirstOrDefault(x => x.Info?.Type == CurrencyType.GameGold));
        AddControl(GgLabel);

        WalletButton = new DXControl
        {
            Location = new Vector2I(8, 380),
            Size = new Vector2I(45, 40),
            // 原版 WalletLabel 没有可见文字或按钮底图，只提供点击热区。
            BackColour = Colors.Transparent,
            Border = false,
        };
        WalletButton.MouseClick += (o, e) => GameScene.Game?.ToggleCurrencyWindow();
        AddControl(WalletButton);

        SortButton = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 364,
            Location = new Vector2I(180, 384),
        };
        SortButton.MouseClick += (o, e) => GameScene.Game?.SendItemSort(GridType.Inventory);
        AddControl(SortButton);

        TrashButton = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 358,
            Location = new Vector2I(218, 384),
        };
        TrashButton.MouseClick += (o, e) => TrashItem();
        AddControl(TrashButton);

        SellButton = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 354,
            Location = new Vector2I(218, 384),
            Visible = false,
            Enabled = false,
            TooltipText = "出售选中物品",
        };
        SellButton.MouseClick += (o, e) => SellSelected();
        AddControl(SellButton);
    }

    /// <summary>按最早 EI 客户端 GameInter F250 的原始坐标重排背包。</summary>
    public void ApplyLegacyEiLayout()
    {
        _legacyEiLayout = true;
        Size = new Vector2I(284, 324);
        _background.LibraryFile = LibraryFile.GameInter;
        _background.Index = 250;
        _background.Location = new Vector2I(-114, -94);
        _background.Size = MirSkin.GetSize(LibraryFile.GameInter, 250);
        _background.StretchImage = false;
        // EI F280 is a complete 16×424 vertical gauge. Six rows are visible;
        // the record array is placed into the separate six-column identity
        // table so multi-cell items reserve every occupied cell.
        Grid.UseLegacyFootprints = true;
        Grid.ItemLibraryFile = LibraryFile.Inventory;
        Grid.GridPadding = 0;
        Grid.GridSize = new Vector2I(6, 6);
        // EI 视口固定 6 行；占用网格总行数由 ConfigureLegacyInventoryGrid()
        // 按 footprint first-fit 重算（可 >= 6）。这里先给一个自洽初值，
        // 否则审计/截图在 Configure 之前会看到 VisibleHeight 的默认 int.MaxValue。
        Grid.VisibleHeight = 6;
        Grid.Location = new Vector2I(25, 41);
        Grid.Clip = true;

        // 锁链 GameInter F280（16x424，贴图里 y≈208 烤了一颗圆点=滑块）：
        // 原版 F250 背包右侧黑槽范围：X=248..264，Y=40..256（高度 216），完全对齐左侧 6 行物品格子（Y=41..257）。
        // 顶部是银帽 (Y≈20..35)，底部是银扣 (Y≈260)。裁剪框精准限定在黑槽内，不遮挡两端金属装饰。
        _legacyScrollClip ??= new DXControl
        {
            Location = new Vector2I(248, 40),
            Size = new Vector2I(16, 216),
            Clip = true,
            IsControl = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        if (_legacyScrollClip.GetParent() == null) AddControl(_legacyScrollClip);

        _legacyScrollTrack ??= new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 280,
            FixedSize = true,
            StretchImage = false,
            Size = new Vector2I(16, 424),
            Location = new Vector2I(0, -198),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        if (_legacyScrollTrack.GetParent() == null) _legacyScrollClip.AddControl(_legacyScrollTrack);

        _legacyScrollBar ??= new DXVScrollBar
        {
            Size = new Vector2I(16, 216),
            Location = new Vector2I(248, 40),
            Border = false,
            BackColour = Colors.Transparent,
            Change = 1,
            Visible = false, // 视觉完全由 F280 锁链呈现，DXVScrollBar 仅作为纯逻辑状态存储
        };
        if (_legacyScrollBar.GetParent() == null) AddControl(_legacyScrollBar);
        _legacyScrollBar.UpButton.Visible = false;
        _legacyScrollBar.DownButton.Visible = false;
        _legacyScrollBar.PositionBar.Visible = false;
        _legacyScrollBar.MouseWheel += _legacyScrollBar.DoMouseWheel;
        _legacyScrollBar.ValueChanged -= LegacyScrollChanged;
        _legacyScrollBar.ValueChanged += LegacyScrollChanged;

        // 交互命中面覆盖整条黑槽（Y=40..256），点击/拖动/滚轮都直接驱动滚动
        _legacyGaugeDrag ??= new LegacyGaugeDragSurface
        {
            Location = new Vector2I(248, 40),
            Size = new Vector2I(16, 216),
            MouseFilter = MouseFilterEnum.Stop,
        };
        _legacyGaugeDrag.Target = _legacyScrollBar;
        if (_legacyGaugeDrag.GetParent() == null) AddControl(_legacyGaugeDrag);

        // 全窗滚轮兜底：格子/锁链各自转发滚轮；其余区域（顶部负重栏、底部区）
        // 的滚轮不会被其它子控件消费，会冒泡到窗口本身。这里在窗口上统一转发，
        // 像普通窗口一样到处都能滚，且不新增控件、不影响按钮命中。
        MouseWheel -= OnLegacyWindowWheel;
        MouseWheel += OnLegacyWindowWheel;

        _titleLabel.Visible = false;
        _legacyModeLabel ??= new DXLabel
        {
            DrawOutline = true,
            OutlineColour = Colors.Black,
            FontSize = 10,
            AutoSize = false,
            Align = HorizontalAlignment.Center,
            VAlign = VerticalAlignment.Center,
            IsControl = false,
        };
        if (_legacyModeLabel.GetParent() == null) AddControl(_legacyModeLabel);
        // 证据：mode 0 的 [包袱] (0x47BE10) 画在 0x42EF5B，色 0xF8DCFA；
        // mode 1-3 [修补]/[变卖]/[储存] 色 0xF8C8C8。绘制矩形未记录在 VA 里，
        // 但原版截图里这行字就在顶部左侧那个黑框内（F250 实测框体
        // 窗口 x25..127 / y22..38），所以居中放在框里。
        _legacyModeLabel.Location = new Vector2I(25, 22);
        _legacyModeLabel.Size = new Vector2I(102, 16);
        ApplyLegacyModeLabel();

        // 证据里的第三个子控件：位置 (176,286)、尺寸 64x20、**随模式换帧** ——
        // 服务器模式分支给它换的是 263/264/265（수리 修理）、270/271/272
        // （판매 变卖）、273/274/275（보관 储存），都是 GameInter 的 64x20
        // 韩文模式字美术。我方此前**完全没有这个控件**。
        if (_legacyModeArt == null)
        {
            _legacyModeArt = new DXImageControl
            {
                LibraryFile = LibraryFile.GameInter,
                FixedSize = true,
                Location = new Vector2I(176, 286),
                Size = new Vector2I(64, 20),
                IsControl = false,
            };
            AddControl(_legacyModeArt);
        }
        _legacyActionButton ??= new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 264,
            HoverIndex = 265,
            PressedIndex = 265,
            Location = new Vector2I(176, 262),
            Size = new Vector2I(64, 20),
        };
        if (_legacyActionButton.GetParent() == null) AddControl(_legacyActionButton);
        _legacyActionButton.Location = new Vector2I(176, 262);
        _legacyActionButton.Size = new Vector2I(64, 20);
        // 原版该钮属共享按钮类 → 点击音效 105（端口 ButtonC），非 DXButton 默认的 ButtonA。
        _legacyActionButton.Sound = SoundIndex.ButtonC;

        if (_legacyModeTabHotspot == null)
        {
            _legacyModeTabHotspot = new DXButton
            {
                LibraryFile = LibraryFile.GameInter,
                Index = -1,          // 不绘制：美术由 _legacyModeArt 负责
                HoverIndex = -1,
                PressedIndex = -1,
                Location = new Vector2I(176, 286),
                Size = new Vector2I(64, 20),
                Sound = SoundIndex.ButtonC,
            };
            AddControl(_legacyModeTabHotspot);
        }
        _legacyModeTabHotspot.Location = new Vector2I(176, 286);
        _legacyModeTabHotspot.Size = new Vector2I(64, 20);
        ApplyLegacyModeArt();
        CloseButton.LibraryFile = LibraryFile.GameInter;
                // 原版关闭钮实参 (arg2,arg3,arg8) = (161,162,-1)、arg9=0：
        // 普通态与悬停态都不画帧（✕ 美术已烘焙进该窗口背景帧，见报告 §10/§10.1 的模板搜索证据），
        // 只有按下态画 arg3=162。
        CloseButton.Index = -1;
        CloseButton.HoverIndex = -1;
        CloseButton.PressedIndex = 162;
        CloseButton.Modulate = new Color(1, 1, 1, 0);  // 不绘制（含 fallback 底色框）
        CloseButton.Location = new Vector2I(249, 288);
        CloseButton.Size = new Vector2I(28, 26);
        // 同属共享按钮类：点击音效 105（端口 ButtonC）。
        CloseButton.Sound = SoundIndex.ButtonC;

        // EI 负重/记录区使用 F280 垂直 gauge；旧版包袱模式的文本仍
        // 单独绘制在根相对 (0x86,0x18)-(0xF0,0x26)。
        // 现代 F360 横向填充不能冒充该资源，因此保持隐藏。


        WeightBar.Visible = false;
        // 证据整块是 (0x86,0x18)-(0xF0,0x26)；标签盒高 14 且文字垂直居中，
        // 因此把盒顶收到 0x17 才能让字面中心正好落在黑框中线（框实测 y22..38）。
        WeightLabel.Location = new Vector2I(0x86, 0x17);
        WeightLabel.Size = new Vector2I(0xF0 - 0x86, 0x26 - 0x18);
        WeightLabel.FontSize = 10;
        WeightLabel.Align = HorizontalAlignment.Left;
        WeightLabel.TextColour = new Color(0xA0 / 255f, 0xA0 / 255f, 0xA0 / 255f);
        // 第二段：/ 总量:%d，色 0xF8C8C8（证据 paint_geometry[4] 的第二个 rect）。
        if (_legacyWeightValue == null)
        {
            _legacyWeightValue = new DXLabel
            {
                FontSize = 10,
                Align = HorizontalAlignment.Left,
                AutoSize = false,
                Size = new Vector2I(120, 14),
                TextColour = new Color(0xF8 / 255f, 0xC8 / 255f, 0xC8 / 255f),
                IsControl = false,
            };
            AddControl(_legacyWeightValue);
        }
        _legacyWeightValue.Location = new Vector2I(0x86, 0x17);
        _legacyWeightValue.Visible = true;
        _goldTitle.Visible = false;
        // 原版实机截图里这个数是**金橙色、右对齐**，落在底部左椭圆的中右部；
        // 不是 EI 反汇编里那个恒为 0 的 0x64C8F8 蓝框（0x41,0x11A 是死字段）。
        // 实测：数字占窗口 x84..129、y270..283，核心色约 (247,199,108)。
        GoldLabel.Location = new Vector2I(0x41, 0x10E);
        GoldLabel.Size = new Vector2I(0x81 - 0x41, 13);
        GoldLabel.FontSize = 10;
        GoldLabel.Align = HorizontalAlignment.Right;
        GoldLabel.TextColour = new Color(0xF8 / 255f, 0xC8 / 255f, 0x78 / 255f);
        _ggTitle.Visible = false;
        GgLabel.Visible = false;
        WalletButton.Visible = false;
        WalletButton.Location = new Vector2I(176, 262);
        WalletButton.Size = new Vector2I(64, 20);

        // 新版排序/丢弃/出售按钮没有旧版 F250 中的等价固定入口。
        SortButton.Visible = false;
        TrashButton.Visible = false;
        SellButton.Visible = false;
        UpdateClientAreaForLegacySkin();
    }
    /// <summary>
    /// 将服务器记录数组映射到 EI 的六列可视窗口。记录索引仍是
    /// ItemGrid 的槽位，格子位置由 footprint first-fit 计算，不按
    /// 46 条记录硬编码行数。
    /// </summary>
    public void ConfigureLegacyInventoryGrid()
    {
        if (!_legacyEiLayout || Grid == null) return;

        // 暗黑式：可视 6 行之外，网格要向下留出**空行**，玩家才能滚下去把物品
        // 放到空位。内容不足时也保留至少 30 行（24 行空位）。
        const int MinimumLegacyRows = 30;
        int rows = Math.Max(MinimumLegacyRows, Grid.GetLegacyRequiredRows(6));
        Grid.GridSize = new Vector2I(6, rows);
        Grid.VisibleHeight = 6;
        Grid.ScrollValue = Math.Min(Grid.ScrollValue, Math.Max(0, rows - 6));
        // 行数不变时 GridSize setter 不会重建，必须显式重建占用表，否则
        // 物品移动后仍用旧锚点（大件被截成单格、高亮缩成 1x1）。
        Grid.RefreshLegacyFootprints();

        // GridSize 行数变化会触发 CreateGrid() 重建 Cells，滚轮绑定必须在
        // 重建之后重挂，否则滚到新格子上就没反应。
        BindLegacyInventoryScrollInputs();

        if (_legacyScrollBar == null) return;
        _legacyScrollBar.MinValue = 0;
        _legacyScrollBar.VisibleSize = 6;
        _legacyScrollBar.MaxValue = rows;
        // 滚轮步长必须与可视行数同量级。DXVScrollBar 默认 `Change = 10`，
        // 而 legacy 背包 VisibleSize 只有 6 —— 一次滚轮跳 10 行，`Value -= 10`
        // 从 0 直接变 -10 被 OnValueChanged 钳回 0，于是**上滚在前 6 下完全
        // 没反应**（死区），且中间 4 行永远看不到。原版 F280 锁链是逐行滚的，
        // 故这里取 1。
        _legacyScrollBar.Change = 1;
        _legacyScrollBar.Value = Grid.ScrollValue;
        _legacyGaugeDrag?.SetTarget(_legacyScrollBar);
        RefreshLegacyChainPosition();
    }

    /// <summary>
    /// 按滚动值滑动整条锁链，让贴图里烤的圆点落在拇指位置（原版 F280 做法：
    /// 锁链固定长度、只在窗口内滑动并裁掉多余部分）。
    /// </summary>
    private void RefreshLegacyChainPosition()
    {
        if (_legacyScrollBar == null || _legacyScrollTrack == null || _legacyScrollClip == null) return;
        int range = _legacyScrollBar.MaxValue - _legacyScrollBar.MinValue - _legacyScrollBar.VisibleSize;
        float t = range > 0
            ? Mathf.Clamp((_legacyScrollBar.Value - _legacyScrollBar.MinValue) / (float)range, 0f, 1f)
            : 0f;
        const int bakedDotY = 208; // F280 圆点中心
        const float pad = 10f;
        float trackH = (float)_legacyScrollClip.Size.Y;
        float travel = Mathf.Max(1f, trackH - pad * 2f);
        float thumbY = pad + t * travel;
        _legacyScrollTrack.Location = new Vector2I(0, (int)Math.Round(thumbY - bakedDotY));
    }

    /// <summary>
    /// legacy 背包：绑定格子与网格区域滚轮，转发给 F280 滚动条。Cells 每次重建后都要重绑。
    /// </summary>
    public void BindLegacyInventoryScrollInputs()
    {
        if (!_legacyEiLayout || Grid == null || _legacyScrollBar == null) return;
        Grid.MouseWheel -= _legacyScrollBar.DoMouseWheel;
        Grid.MouseWheel += _legacyScrollBar.DoMouseWheel;
        if (Grid.Cells != null)
        {
            foreach (var cell in Grid.Cells)
            {
                if (cell == null) continue;
                cell.MouseWheel -= _legacyScrollBar.DoMouseWheel;
                cell.MouseWheel += _legacyScrollBar.DoMouseWheel;
            }
        }
    }

    /// <summary>窗口级滚轮兜底：未被格子/锁链消费的滚轮也驱动同一条滚动值。</summary>
    private void OnLegacyWindowWheel(object sender, MouseWheelEventArgs e)
    {
        if (_legacyEiLayout) _legacyScrollBar?.DoMouseWheel(sender, e);
    }

    private void LegacyScrollChanged(object sender, EventArgs e)
    {
        if (Grid == null || _legacyScrollBar == null) return;
        Grid.ScrollValue = _legacyScrollBar.Value;
        // 值变化时滑动锁链，让烤在图里的圆点跟着走到位。
        RefreshLegacyChainPosition();
    }

    private void TrashItem()
    {
        if (DXItemCell.SelectedCell == null) return;
        var cell = DXItemCell.SelectedCell;
        if (cell.Item == null) return;
        if (cell.GridType != GridType.Inventory) return;
        if (cell.Item.Flags.HasFlag(UserItemFlags.Locked) || cell.Item.Flags.HasFlag(UserItemFlags.Marriage)) return;

        cell.Locked = true;
        cell.UpdateBorder();
        DXItemCell.SelectedCell = null;
        GameScene.Game?.SendItemDelete(cell.GridType, cell.Slot);
    }

    public override void _Ready()
    {
        base._Ready();
        CenterWeightLabel();
        GameScene.Game?.RefreshInventoryWeights();
    }

    private void DrawWeightFill(object sender, EventArgs e)
    {
        var game = GameScene.Game;
        if (game == null) return;
        if (game.BagWeight <= 0) return;

        var stats = game.PlayerStats;
        if (stats == null || stats[Stat.BagWeight] <= 0) return;

        float percent = Math.Clamp(game.BagWeight / (float)stats[Stat.BagWeight], 0f, 1f);
        if (percent <= 0) return;

        var tex = MirSkin.GetTexture(LibraryFile.GameInter, 360);
        if (tex == null) return;
        var imgSize = tex.GetSize();
        WeightBar.DrawTextureRect(tex, new Rect2(0, 0, imgSize.X * percent, imgSize.Y), false);
    }

    public void CenterWeightLabel()
    {
        if (WeightLabel == null || WeightBar == null) return;
        if (_legacyEiLayout) return;
        var size = MirSkin.MeasureText(WeightLabel.Text, WeightLabel.FontSize);
        WeightLabel.Location = new Vector2I(
            WeightBar.Location.X + (int)((WeightBar.Size.X - size.X) / 2),
            WeightBar.Location.Y + (int)((WeightBar.Size.Y - size.Y) / 2));
    }

    /// <summary>GameScene 数据注入</summary>
    public void SetWeight(int bagWeight)
    {
        int capacity = GameScene.Game?.PlayerStats?[Stat.BagWeight] ?? 0;
        // inventory-window-render-evidence.json paint_geometry[4]：负重是**两段
        // 两色**绘制（0xA0A0A0 与 0xF8C8C8，同一行两个 rect），不是单个标签。
        // 此前合成一行单色 0xA0A0A0，且 106px 宽在 FontSize 10 下会折行。
        bool showWeight = InvMode == InventoryMode.Normal;
        string weightText = $"负重:{bagWeight}";
        string capacityText = $"/ 总量:{(capacity > 0 ? capacity : 0)}";
        WeightLabel.Text = showWeight ? weightText : string.Empty;
        if (_legacyWeightValue != null)
        {
            _legacyWeightValue.Text = showWeight ? capacityText : string.Empty;
            if (showWeight && _legacyEiLayout)
            {
                float w = MirSkin.MeasureText(weightText, WeightLabel.FontSize).X;
                _legacyWeightValue.Location = new Vector2I(
                    WeightLabel.Location.X + (int)w, WeightLabel.Location.Y);
            }
        }
        CenterWeightLabel();
        WeightBar.QueueRedraw();
    }

    public void SetCurrency(long gold, long gg)
    {
        if (_legacyEiLayout)
        {
            GoldLabel.Text = gold.ToString();
            return;
        }

        if (!IsSellMode)
        {
            GoldLabel.Text = gold.ToString("N0");
            GgLabel.Text = gg.ToString("N0");
            return;
        }

        var current = GameScene.Game?.Currencies?.FirstOrDefault(x => x.Info == _primaryCurrency);
        GoldLabel.Text = (current?.Amount ?? 0).ToString("N0");
        GgLabel.Text = SaleTotal().ToString("N0");
    }

    /// <summary>自测/审计用：legacy F280 滚动条的可滚动跨度 (Max-Min-VisibleSize)。</summary>
    public int LegacyScrollRange => _legacyScrollBar == null
        ? -1
        : _legacyScrollBar.MaxValue - _legacyScrollBar.MinValue - _legacyScrollBar.VisibleSize;

    /// <summary>自测/审计用：legacy F280 滚动条是否处于可拖动状态。</summary>
    public bool LegacyScrollEnabled => _legacyScrollBar?.PositionBar?.Enabled == true;

    public bool AuditLegacyEiLayout(out string details)
    {
        // 占用网格行数随 footprint first-fit 长高（>= 6），审计不能把
        // GridSize 锁死在 6x6；可视视口恒为 6 行、六列、原点 (25,41) 才是契约。
        bool ok = Size == new Vector2I(284, 324)
            && _background.Index == 250
            && Grid.GridSize.X == 6
            && Grid.GridSize.Y >= 6
            && Grid.VisibleHeight == 6
            && Grid.Location == new Vector2I(25, 41)
            && CloseButton.Location == new Vector2I(249, 288)
            && _legacyActionButton?.Location == new Vector2I(176, 262)
            // 原版三控件点击音效=105（共享按钮类 release 0x4177F0），端口映射为 ButtonC
            && _legacyActionButton?.Sound == SoundIndex.ButtonC
            && CloseButton.Sound == SoundIndex.ButtonC
            && _legacyModeTabHotspot?.Sound == SoundIndex.ButtonC
            && _legacyModeTabHotspot?.Location == new Vector2I(176, 286);
        details = $"size={Size} background=F{_background.Index} grid={Grid.GridSize}@{Grid.Location} close={CloseButton.Location} action={_legacyActionButton?.Location}";
        return ok;
    }

    /// <summary>
    /// 旧版顶部模式文字：mode 0「[包袱]」色 0xF8DCFA，mode 1-3 色 0xF8C8C8
    /// （证据 status_selector：0x42EF52 [包袱] / 0x42F02E [修补] / 0x42F068
    /// [变卖] / 0x42F0EB [储存]）。
    /// </summary>
    private void ApplyLegacyModeLabel()
    {
        if (_legacyModeLabel == null) return;
        _legacyModeLabel.Text = InvMode switch
        {
            InventoryMode.Repair => "[修补]",
            InventoryMode.Sell => "[变卖]",
            InventoryMode.Storage => "[储存]",
            _ => "[包袱]",
        };
        _legacyModeLabel.TextColour = InvMode == InventoryMode.Normal
            ? new Color(0xF8 / 255f, 0xDC / 255f, 0xFA / 255f)
            : new Color(0xF8 / 255f, 0xC8 / 255f, 0xC8 / 255f);
    }

    /// <summary>
    /// 右下角随模式换帧的 64x20 控件（原版只有一个：263/264/265 수리、270/271/272
    /// 판매、273/274/275 보관）。mode 0 原版该处是空槽，标题帧/按钮帧一律不画，
    /// 否则会出现 mode 0 也显示「수 리」和灰底方块的问题。
    /// </summary>
    private void ApplyLegacyModeArt()
    {
        if (_legacyModeArt != null)
        {
            _legacyModeArt.Index = InvMode switch
            {
                InventoryMode.Repair => 263,
                InventoryMode.Sell => 270,
                InventoryMode.Storage => 273,
                _ => -1,
            };
            _legacyModeArt.Visible = _legacyModeArt.Index >= 0;
        }
        if (_legacyActionButton != null)
        {
            int normal = InvMode switch
            {
                InventoryMode.Repair => 264,
                InventoryMode.Sell => 271,
                InventoryMode.Storage => 274,
                _ => -1,
            };
            int hover = InvMode switch
            {
                InventoryMode.Repair => 265,
                InventoryMode.Sell => 272,
                InventoryMode.Storage => 275,
                _ => -1,
            };
            _legacyActionButton.Index = normal;
            _legacyActionButton.HoverIndex = hover;
            _legacyActionButton.PressedIndex = hover;
            bool npcMode = normal >= 0;
            _legacyActionButton.Visible = npcMode;
        }
        // Index<0 的 DXButton 会画「生成按钮/兜底灰框」，mode 0 那格在原版是空的，
        // 所以直接隐藏热点（NPC 模式下才需要它承接点击）。
        if (_legacyModeTabHotspot != null)
            _legacyModeTabHotspot.Visible = InvMode != InventoryMode.Normal;
    }

    /// <summary>仅供布局测试台预览负重/总量文字（不走 PlayerStats 查询）。</summary>
    public void SetLegacyWeightPreview(int bagWeight, int capacity)
    {
        WeightLabel.Text = $"负重:{bagWeight}";
        if (_legacyWeightValue != null)
        {
            _legacyWeightValue.Text = $"/ 总量:{capacity}";
            float w = MirSkin.MeasureText(WeightLabel.Text, WeightLabel.FontSize).X;
            _legacyWeightValue.Location = new Vector2I(
                WeightLabel.Location.X + (int)w, WeightLabel.Location.Y);
        }
    }

    /// <summary>
    /// 旧版模式由服务端消息切换，而不是点击装饰性页签。保留一个明确的
    /// 业务入口，供 NPC 修补/变卖/储存回包直接切换旧版文字和操作状态。
    /// </summary>
    public void SetLegacyMode(InventoryMode mode)
    {
        InvMode = mode;
        ApplyLegacyModeLabel();
        ApplyLegacyModeArt();
        if (mode != InventoryMode.Sell)
        {
            ClearSaleSelection();
            SellButton.Visible = false;
        }
        WeightLabel.Text = mode == InventoryMode.Normal ? WeightLabel.Text : string.Empty;
        CenterWeightLabel();
    }

    /// <summary>原版 InventoryDialog.SellMode：背包负责多选物品和提交 NPCSell。</summary>
    public void SellMode(CurrencyInfo currency, IEnumerable<ItemType> sellableTypes)
    {
        _primaryCurrency = currency ?? Globals.CurrencyInfoList?.Binding.FirstOrDefault(x => x.Type == CurrencyType.Gold);
        SellableItemTypes.Clear();
        if (sellableTypes != null) SellableItemTypes.AddRange(sellableTypes);
        ClearSaleSelection();
        InvMode = InventoryMode.Sell;
        _titleLabel.Text = "背包 [出售]";
        _goldTitle.Text = _primaryCurrency?.Abbreviation ?? Lang.InventoryDialogPrimaryCurrencyTitle;
        _ggTitle.Text = Lang.InventoryDialogSecondaryCurrencyTitle;
        _ggTitle.TextColour = new Color(.4f, .65f, 1f);
        GgLabel.Text = "0";
        TrashButton.Visible = false;
        SellButton.Visible = true;
        // 原版按钮始终可用：有选中项时出售选中项，没有选中项时出售全部可售物品。
        SellButton.Enabled = true;
        ApplyLegacyModeLabel();
        ApplyLegacyModeArt();
        SetCurrency(0, 0);
    }

    /// <summary>原版 InventoryDialog.NormalMode：离开 NPC 出售状态并恢复普通按钮。</summary>
    public void NormalMode()
    {
        ClearSaleSelection();
        SellableItemTypes.Clear();
        _primaryCurrency = null;
        InvMode = InventoryMode.Normal;
        _titleLabel.Text = Lang.InventoryDialogTitle;
        _goldTitle.Text = Lang.InventoryDialogPrimaryCurrencyTitle;
        _goldTitle.TextColour = new Color(.85f, .68f, .2f);
        _ggTitle.Text = "GG";
        _ggTitle.TextColour = new Color(1f, .55f, .2f);
        TrashButton.Visible = true;
        SellButton.Visible = false;
        SellButton.Enabled = false;
        ApplyLegacyModeLabel();
        ApplyLegacyModeArt();
        SetCurrency(
            GameScene.Game?.Currencies?.FirstOrDefault(x => x.Info?.Type == CurrencyType.Gold)?.Amount ?? 0,
            GameScene.Game?.Currencies?.FirstOrDefault(x => x.Info?.Type == CurrencyType.GameGold)?.Amount ?? 0);
    }

    /// <summary>出售模式下由背包格调用；返回 true 表示已消费这次点击。</summary>
    public bool TrySelectForSale(DXItemCell cell)
    {
        if (!IsSellMode || cell?.Item == null || cell.GridType != GridType.Inventory) return false;
        // 原版 DXItemCell 右键 SellMode：婚戒不可出售、静默返回；不可卖物品
        // 先给出系统提示（UnableToSellHereCannotSold）再拒绝选中。
        if (cell.Item.Flags.HasFlag(UserItemFlags.Marriage))
            return true;
        if (cell.Locked || cell.Item.Flags.HasFlag(UserItemFlags.Locked) ||
            cell.Item.Flags.HasFlag(UserItemFlags.Worthless) ||
            cell.Item.Info?.CanSell != true)
        {
            GameScene.Game?.ReceiveChat($"无法出售 {cell.Item.Info?.Local()}, 该物品不可出售。");
            return true;
        }
        // 原版 InventoryDialog.Cell_SelectedChanged：类型不在商店可售列表时
        // 提示 UnableToSellHere 并取消选中。
        if (SellableItemTypes.Count > 0 && !SellableItemTypes.Contains(cell.Item.Info.ItemType))
        {
            GameScene.Game?.ReceiveChat($"无法在 {cell.Item.Info?.Local()} 这里进行售卖。");
            return true;
        }

        if (SelectedItems.Contains(cell))
        {
            SelectedItems.Remove(cell);
            cell.SaleSelected = false;
        }
        else
        {
            SelectedItems.Add(cell);
            cell.SaleSelected = true;
        }

        DXItemCell.SelectedCell = null;
        GgLabel.Text = SaleTotal().ToString("N0");
        SellButton.Enabled = true;
        SellButton.TooltipText = SelectedItems.Count == 1 ? "出售" : "全部出售";
        return true;
    }

    private long SaleTotal()
    {
        decimal rate = _primaryCurrency?.ExchangeRate > 0M ? _primaryCurrency.ExchangeRate : 1M;
        return SelectedItems.Where(x => x?.Item != null)
            .Sum(x => (long)(x.Item.Price(x.Item.Count) / rate));
    }

    private void SellSelected()
    {
        if (!IsSellMode || GameScene.Game?.IsObserver == true) return;
        var candidates = SelectedItems.Count > 0
            ? SelectedItems
            : (Grid?.Cells ?? Array.Empty<DXItemCell>())
                .Where(x => x?.Item != null && (SellableItemTypes.Count == 0 || SellableItemTypes.Contains(x.Item.Info.ItemType)));
        var links = candidates.Where(x => x?.Item != null && !x.Locked &&
                !x.Item.Flags.HasFlag(UserItemFlags.Locked) &&
                !x.Item.Flags.HasFlag(UserItemFlags.Marriage) &&
                !x.Item.Flags.HasFlag(UserItemFlags.Worthless) &&
                x.Item.Info?.CanSell == true)
            .Select(x => new CellLinkInfo { GridType = GridType.Inventory, Slot = x.Slot, Count = x.Item.Count })
            .ToList();
        if (links.Count == 0) return;

        foreach (var link in links)
        {
            var cell = Grid?.Cells?.FirstOrDefault(x => x.Slot == link.Slot);
            if (cell == null) continue;
            cell.Locked = true;
            cell.UpdateBorder();
            _pendingSellLinks.Add(link);
        }
        foreach (var cell in SelectedItems) cell.SaleSelected = false;
        SelectedItems.Clear();
        SellButton.Enabled = true;
        GameScene.Game?.SendNPCSell(links);
    }

    public void ItemsChanged(IEnumerable<CellLinkInfo> links, bool success)
    {
        var changed = new HashSet<int>((links ?? Enumerable.Empty<CellLinkInfo>())
            .Where(x => x?.GridType == GridType.Inventory).Select(x => x.Slot));
        if (changed.Count == 0) return;
        foreach (var cell in Grid?.Cells ?? Array.Empty<DXItemCell>())
        {
            if (cell != null && changed.Contains(cell.Slot))
            {
                cell.SaleSelected = false;
                if (!success) cell.Locked = false;
                cell.UpdateBorder();
            }
        }
        _pendingSellLinks.RemoveAll(x => changed.Contains(x.Slot));
    }

    private void ClearSaleSelection()
    {
        foreach (var cell in SelectedItems) if (cell != null) cell.SaleSelected = false;
        SelectedItems.Clear();
        GgLabel.Text = "0";
        DXItemCell.SelectedCell = null;
    }

    /// <summary>
    /// F280 竖轨窗口内的命中面。EI 的 gauge 命中（0x42FFD0 → F707 0x417D00）
    /// 把 pointer Y 的比例写回滚动值；这里照做：按下/拖动/滚轮都作用在同一条
    /// DXVScrollBar 上，拖动方向 = 轨道方向（向下拖 → 行窗下移）。
    /// </summary>
    private sealed partial class LegacyGaugeDragSurface : DXControl
    {
        public DXVScrollBar Target;
        private bool _gaugeDragging;

        public void SetTarget(DXVScrollBar target) => Target = target;

        public LegacyGaugeDragSurface()
        {
            MouseWheel += (s, e) => Target?.DoMouseWheel(s, e);
        }

        public override void _Process(double delta)
        {
            base._Process(delta);
            if (_gaugeDragging)
            {
                if (!Input.IsMouseButtonPressed(MouseButton.Left))
                {
                    _gaugeDragging = false;
                }
                else
                {
                    Vector2 localMouse = GetGlobalTransformWithCanvas().AffineInverse() * GetViewport().GetMousePosition();
                    ApplyGaugeY(localMouse.Y);
                }
            }
        }

        public override void _GuiInput(InputEvent e)
        {
            base._GuiInput(e);
            if (Target == null) return;

            if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
            {
                _gaugeDragging = mb.Pressed;
                if (mb.Pressed)
                {
                    ApplyGaugeY((float)mb.Position.Y);
                    AcceptEvent();
                }
            }
        }

        private void ApplyGaugeY(float y)
        {
            if (Target == null) return;
            int range = Target.MaxValue - Target.MinValue - Target.VisibleSize;
            if (range <= 0) return;
            const float pad = 10f;
            float travel = Mathf.Max(1f, Size.Y - pad * 2f);
            float t = Mathf.Clamp((y - pad) / travel, 0f, 1f);
            Target.Value = Target.MinValue + (int)Math.Round(t * range);
        }
    }
}
