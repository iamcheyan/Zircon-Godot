using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>原版 NPCRepairDialog：11×5 维修格、费用、背包导入和维修提交。</summary>
public partial class NPCRepairPanel : DXControl
{
    private readonly ClientUserItem[] _items = new ClientUserItem[55];
    private readonly List<CellLinkInfo> _links = new();
    private readonly DXItemGrid _grid;
    private readonly DXLabel _cost;
    private readonly DXLabel _title;
    private readonly DXLabel _costLabel;
    private readonly DXButton _repair;
    private readonly DXCheckButton _special;
    private readonly DXCheckButton _guildFunds;
    private readonly LegacyWindowFrame _frame;
    private readonly DXImageControl _legacyTop;
    private readonly List<DXImageControl> _legacyMidTiles = new();
    private readonly RepairSlotBackdrop _legacySlots;

    /// <summary>
    /// 维修格的"暗槽 + 金线"自绘底。为什么需要它：`DXItemCell` 只在激活/悬停时才画边框
    /// （`UpdateBorder() → Border = active`），而原版背包/商店的格子是**烤进面板贴图**里的；
    /// 素材库里没有 11x5 格的修理面板整帧（F250 是 3x4 大格、F1001 是 3x3），所以这里自绘。
    /// </summary>
    private sealed partial class RepairSlotBackdrop : DXControl
    {
        public int Columns = 11, Rows = 5, Pitch = 35, SlotWidth = 33, SlotHeight = 33;

        protected override void DrawControl()
        {
            for (int row = 0; row < Rows; row++)
                for (int col = 0; col < Columns; col++)
                {
                    var rect = new Rect2(col * Pitch, row * Pitch, SlotWidth, SlotHeight);
                    DrawRect(rect, new Color(0f, 0f, 0f, 0.45f), true);
                    DrawRect(rect, new Color(0.62f, 0.51f, 0.29f, 0.85f), false, 1f);
                }
        }
    }
    private readonly List<DXButton> _sourceButtons = new();
    private bool _legacyEiLayout;
    private readonly List<CellLinkInfo> _pendingLinks = new();
    private HashSet<ItemType> _allowedTypes;

    public IEnumerable<ItemType> AllowedTypes
    {
        set => _allowedTypes = value == null ? null : new HashSet<ItemType>(value);
    }

    public NPCRepairPanel()
    {
        // 原版 DXItemGrid 默认 GridPadding=0：11x5 为 386x176。
        // SetClientSize(Grid.Width, Grid.Height + 70) 后总窗口为 404x292，且没有 Footer。
        Size = new Vector2I(404, 292);
        _frame = new LegacyWindowFrame { Size = Size, HasTitle = true, HasFooter = false };
        AddControl(_frame);
        // legacy 三段式木质框架（默认隐藏；ApplyLegacyEiLayout 时替换掉 _frame 的现代外框）：
        // F1100 顶段 + F1101 中段平铺，与 NPC 对话框同一套素材。
        _legacyTop = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 1100,
            FixedSize = true,
            StretchImage = false,
            Size = new Vector2I(552, 138),
            Location = new Vector2I(-64, -59),
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddControl(_legacyTop);
        for (int i = 0; i < 16; i++)
        {
            var tile = new DXImageControl
            {
                LibraryFile = LibraryFile.GameInter,
                Index = 1101,
                FixedSize = true,
                StretchImage = false,
                Size = new Vector2I(552, 18),
                Location = new Vector2I(-64, 128 - 7 + i * 18),
                Visible = false,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _legacyMidTiles.Add(tile);
            AddControl(tile);
        }
        // 维修格自绘底（默认隐藏；legacy 布局时显示在真实格子下方）
        _legacySlots = new RepairSlotBackdrop
        {
            Location = new Vector2I(0, 146),
            Size = new Vector2I(11 * 35, 5 * 35),
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddControl(_legacySlots);
        _title = new DXLabel { Text = "修理物品", FontSize = 10, TextColour = new Color(1f, .85f, .3f), DrawOutline = true, OutlineColour = Colors.Black, Align = HorizontalAlignment.Center, VAlign = VerticalAlignment.Center, AutoSize = false, Location = new Vector2I(0, 8), Size = new Vector2I(404, 18), IsControl = false };
        AddControl(_title);
        _grid = new DXItemGrid { GridSize = new Vector2I(11, 5), Location = new Vector2I(9, 37), GridType = GridType.Repair, ItemGrid = _items, GridPadding = 0, Border = false }; AddControl(_grid);
        foreach (var cell in _grid.Cells)
            cell.LinkChanged += (o) => RebuildLinks();

        int bottom = 224;
        _costLabel = new DXLabel { Text = "修理费用：", FontSize = 9, TextColour = Colors.White, Location = new Vector2I(9, bottom), Size = new Vector2I(79, 20), IsControl = false, Border = true, BorderColour = new Color(1f, .75f, .25f) };
        AddControl(_costLabel);
        _cost = new DXLabel { Text = "0", FontSize = 10, TextColour = Colors.White, Location = new Vector2I(88, bottom), Size = new Vector2I(248, 20), IsControl = false, Border = true, BorderColour = new Color(1f, .75f, .25f) }; AddControl(_cost);
        _special = new DXCheckButton("特殊修理") { Location = new Vector2I(210, bottom + 25), Size = new Vector2I(100, 19), FontSize = 9 };
        _special.Changed += (o, e) => { if (_special.Checked) ClearUnavailableSpecialItems(); RebuildLinks(); }; AddControl(_special);
        _guildFunds = new DXCheckButton("使用行会资金") { Location = new Vector2I(200, bottom + 47), Size = new Vector2I(110, 19), FontSize = 9, Enabled = false }; AddControl(_guildFunds);

        int buttonY = bottom + 25;
        AddSourceButton("背包", 9, buttonY, () => ImportCells(GameScene.Game?.InventoryCells));
        AddSourceButton("装备", 93, buttonY, () => ImportCells(GameScene.Game?.EquipmentCells));
        _repair = new DXButton { Text = "修理", Type = DXButton.ButtonType.SmallButton, FontSize = 10, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(315, buttonY), Size = new Vector2I(79, 25), Enabled = false };
        _repair.MouseClick += (o, e) => Submit(); AddControl(_repair);
        AddSourceButton("仓库", 9, buttonY + 30, () => ImportCells(GameScene.Game?.StorageCells));
        AddSourceButton("行会仓库", 93, buttonY + 30, () => ImportCells(GameScene.Game?.GuildStorageCells));
    }

    public void Clear()
    {
        for (int i = 0; i < _items.Length; i++)
        {
            _items[i] = null;
            if (_grid.Cells != null && i < _grid.Cells.Length)
            {
                _grid.Cells[i].LinkedSourceGrid = GridType.None;
                _grid.Cells[i].LinkedSourceSlot = -1;
            }
        }
        _links.Clear(); _grid.RefreshGrid(); _cost.Text = Lang.NPCRepairPanelUi129Label; _repair.Enabled = false;
    }

    public List<CellLinkInfo> CancelDisplayedLinks()
    {
        var links = new List<CellLinkInfo>(_links);
        foreach (var cell in _grid.Cells ?? Array.Empty<DXItemCell>())
        {
            if (cell.LinkedSourceSlot >= 0)
                links.Add(new CellLinkInfo { GridType = cell.LinkedSourceGrid, Slot = cell.LinkedSourceSlot });
        }
        Clear();
        return links;
    }

    public List<CellLinkInfo> CancelLinks()
    {
        var links = CancelDisplayedLinks();
        links.AddRange(_pendingLinks);
        _pendingLinks.Clear();
        return links;
    }

    public void RepairResult(Library.Network.ServerPackets.NPCRepair packet)
    {
        if (packet == null) return;

        var resultLinks = packet.Links != null && packet.Links.Count > 0
            ? packet.Links
            : _pendingLinks;
        foreach (var link in resultLinks)
        {
            var source = FindSourceCell(link);
            if (source == null) continue;

            source.Locked = false;
            source.UpdateBorder();
            if (packet.Success && source.Item != null)
            {
                if (packet.Special)
                {
                    source.Item.CurrentDurability = source.Item.MaxDurability;
                    if (source.Item.Info.ItemType != ItemType.Weapon && packet.SpecialRepairDelay > TimeSpan.Zero)
                        source.Item.NextSpecialRepair = Library.Time.Now.Add(packet.SpecialRepairDelay);
                }
                else
                {
                    source.Item.MaxDurability = Math.Max(0,
                        source.Item.MaxDurability - (source.Item.MaxDurability - source.Item.CurrentDurability) / Globals.DuraLossRate);
                    source.Item.CurrentDurability = source.Item.MaxDurability;
                }
                source.RefreshItem();
            }
        }

        _pendingLinks.Clear();
        _repair.Enabled = false;
    }

    public bool TryRouteItem(DXItemCell source)
    {
        if (!CanAcceptSource(source))
            return false;
        var target = _grid.Cells?.FirstOrDefault(c => c.Item == null && c.LinkedSourceSlot < 0);
        if (target == null) return false;
        source.MoveItem(target);
        RebuildLinks();
        return true;
    }

    /// <summary>
    /// 原版 DXItemCell.CheckLink(Repair) 的页面级过滤，供直接拖到维修格
    /// 的路径复用；右键导入和底部批量导入也必须走同一套规则。
    /// </summary>
    public bool CanAcceptSource(DXItemCell source)
    {
        return source?.Item?.Info != null
            && (_allowedTypes == null || _allowedTypes.Contains(source.Item.Info.ItemType))
            && !source.Item.Flags.HasFlag(UserItemFlags.Marriage)
            && source.GridType is not (GridType.PartsStorage or GridType.CompanionEquipment)
            && source.Item.Info.CanRepair
            && source.Item.CurrentDurability < source.Item.MaxDurability
            && (!_special.Checked || source.Item.NextSpecialRepair <= Library.Time.Now);
    }

    private void AddSourceButton(string text, int x, int y, Action action)
    {
        var button = new DXButton { Text = text, Type = DXButton.ButtonType.SmallButton, FontSize = 9, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(x, y), Size = new Vector2I(79, 25) };
        button.MouseClick += (o, e) => action();
        _sourceButtons.Add(button);
        AddControl(button);
    }

    /// <summary>
    /// 旧版 EI 皮肤下的修理面板。原来这一态**没有任何 legacy 处理**：主体是半透明
    /// 现代外框（能透出背后的地图/NPC）、按钮是无美术的扁平矩形，与旁边的 EI 商店面板、
    /// NPC 对话窗完全不是一套皮（2026-10-18 看图诊断确认）。
    ///
    /// 为什么不直接套某一帧：素材库里没有"11x5 维修格 + 费用行 + 四容器钮"的整帧
    /// （F250 是 3x4 大格的修理框、F1002 是"商品列表 + 小信息板"组合帧、F1001 是 3x3 格），
    /// 所以采用与 NPC 对话框**同一套三段式木质框架**：F1100 顶段 + F1101 纵向平铺中段
    /// （实测 F1101 就是窗口中段的黑底身体 + 左右木栏，见 LEGACY_DIALOG_GEOMETRY §8.1），
    /// 这样面板不透明、且与玩家看惯的对话框同风格。
    /// 修理钮用素材里唯一的修理钮 F263/264/265（64x19，韩文「수리」烤在图内，原版即如此）。
    /// 面板内部落位是首版排布，按实机截图微调（与 goods 面板当初的做法一致）。
    /// </summary>
    public void ApplyLegacyEiLayout()
    {
        _legacyEiLayout = true;
        // 关键：F1100/F1101 的 alpha 可见区宽 **384**（画布 512 宽是含透明边），
        // 所以面板与所有控件都必须落在 0..384 内 —— 上一版用 552 宽，控件直接落到木框外的草地上
        // （2026-10-18 视觉复核发现"小长条跨右边框""按钮浮在草地上"）。
        const int width = 384;
        Size = new Vector2I(width, 410);
        _frame.Visible = false;

        // 顶段：F1100（alpha 可见区 384x138，bbox 原点 (64,59) -> 锚点 -(64,59)）
        _legacyTop.Visible = true;
        _legacyTop.Location = new Vector2I(-64, -59);
        _legacyTop.Size = new Vector2I(width, 138);

        // 中段：F1101（可见 383x18）自 y=128 每 18px 平铺到面板底部
        for (int i = 0; i < _legacyMidTiles.Count; i++)
        {
            int y = 128 + i * 18;
            DXImageControl tile = _legacyMidTiles[i];
            if (y + 18 <= Size.Y)
            {
                tile.Visible = true;
                tile.Location = new Vector2I(-64, y - 7);
                tile.Size = new Vector2I(width, 18);
            }
            else tile.Visible = false;
        }

        _title.Location = new Vector2I(0, 10);
        _title.Size = new Vector2I(width, 18);

        // 11x5 维修格 = 385x175（格距 35），放面板上部；先铺自绘槽底，真实格子叠在上面
        _legacySlots.Visible = true;
        _legacySlots.Location = new Vector2I(0, 146);
        _grid.Location = new Vector2I(0, 146);

        // 费用行：格子正下方（数值已由 Lang 模板带标签，不再重复前置"修理费用："）
        _costLabel.Visible = false;
        _cost.Location = new Vector2I(10, 328);
        _cost.Size = new Vector2I(300, 20);

        // 两个复选框
        _special.Location = new Vector2I(175, 352);
        _special.Size = new Vector2I(100, 19);
        _guildFunds.Location = new Vector2I(175, 374);
        _guildFunds.Size = new Vector2I(110, 19);

        // 修理钮：素材唯一的修理钮（F263 普通 / F264 悬停 / F265 按下）
        _repair.Location = new Vector2I(312, 356);
        _repair.Size = new Vector2I(64, 19);
        _repair.FontSize = 9;
        _repair.Text = "";
        _repair.LibraryFile = LibraryFile.GameInter;
        _repair.Index = 263;
        _repair.HoverIndex = 264;
        _repair.PressedIndex = 265;

        // 四个来源钮：素材里没有对应按钮帧（只有 修理/出售/保管/交换 四类），
        // 暂留文字钮，位置排在左下，待人工确认后再换素材。
        for (int i = 0; i < _sourceButtons.Count; i++)
        {
            DXButton b = _sourceButtons[i];
            b.Size = new Vector2I(34, 34);
            b.Location = new Vector2I(10 + i * 40, 352);
        }
    }

    /// <summary>审计用：legacy EI 修理面板的关键常量是否自洽（视觉正确性靠截图判断）。</summary>
    public bool AuditLegacyEiLayout(out string details)
    {
        bool ok = _legacyEiLayout
            && Size == new Vector2I(384, 410)
            && _legacyTop.Visible && !_frame.Visible
            && _legacyTop.LibraryFile == LibraryFile.GameInter && _legacyTop.Index == 1100
            && _legacySlots.Visible && _legacySlots.Location == new Vector2I(0, 146)
            && _grid.Location == new Vector2I(0, 146)
            && _repair.LibraryFile == LibraryFile.GameInter
            && _repair.Index == 263 && _repair.HoverIndex == 264 && _repair.PressedIndex == 265
            && _cost.Location == new Vector2I(10, 328)
            && !_costLabel.Visible
            && _sourceButtons.Count == 4;
        details = $"size={Size} top={_legacyTop.Index}@{_legacyTop.Location} tiles={_legacyMidTiles.Count} "
            + $"slots={_legacySlots.Location} grid={_grid.Location} cost={_cost.Location} "
            + $"repair={_repair.Index}/{_repair.HoverIndex}/{_repair.PressedIndex}@{_repair.Location}";
        return ok;
    }

    private void ImportCells(IEnumerable<DXItemCell> cells)
    {
        Clear();
        int target = 0;
        foreach (var source in cells ?? Enumerable.Empty<DXItemCell>())
        {
            var item = source?.Item;
            if (item == null || item.Info == null || !item.Info.CanRepair ||
                source.GridType is GridType.PartsStorage or GridType.CompanionEquipment ||
                (_allowedTypes != null && !_allowedTypes.Contains(item.Info.ItemType)) ||
                item.CurrentDurability >= item.MaxDurability || item.Flags.HasFlag(UserItemFlags.Marriage) ||
                (_special.Checked && item.NextSpecialRepair > Library.Time.Now) || target >= _items.Length) continue;
            _items[target] = new ClientUserItem(item, item.Count);
            _grid.Cells[target].LinkedSourceGrid = source.GridType;
            _grid.Cells[target].LinkedSourceSlot = source.Slot;
            target++;
        }
        _grid.RefreshGrid();
        RebuildLinks();
    }

    private void Submit()
    {
        if (_pendingLinks.Count > 0) return;
        RebuildLinks();
        if (_links.Count == 0) return;

        _pendingLinks.Clear();
        _pendingLinks.AddRange(_links);
        foreach (var link in _pendingLinks)
        {
            var source = FindSourceCell(link);
            if (source != null)
            {
                source.Locked = true;
                source.UpdateBorder();
            }
        }

        // 原版在发送前解除维修槽的 Link；来源格保持 Locked，直到 NPCRepair 回包。
        for (int i = 0; i < _grid.Cells.Length; i++)
        {
            _items[i] = null;
            _grid.Cells[i].LinkedSourceGrid = GridType.None;
            _grid.Cells[i].LinkedSourceSlot = -1;
        }
        _links.Clear();
        _grid.RefreshGrid();
        _cost.Text = Lang.NPCRepairPanelUi129Label;
        _repair.Enabled = false;
        // 原版提交后复位 GuildCheckBox（Special 不复位）。
        _guildFunds.Checked = false;
        _guildFunds.Enabled = GameScene.Game?.HasGuild == true;
        GameScene.Game?.SendNPCRepair(new List<CellLinkInfo>(_pendingLinks), _special.Checked, _guildFunds.Checked);
    }

    private DXItemCell FindSourceCell(CellLinkInfo link)
    {
        if (link == null || GameScene.Game == null) return null;
        var cells = link.GridType switch
        {
            GridType.Inventory => GameScene.Game.InventoryCells,
            GridType.Equipment => GameScene.Game.EquipmentCells,
            GridType.Storage => GameScene.Game.StorageCells,
            GridType.PartsStorage => GameScene.Game.PartsStorageCells,
            GridType.GuildStorage => GameScene.Game.GuildStorageItemCells,
            GridType.CompanionInventory => GameScene.Game.CompanionInventoryCells,
            GridType.CompanionEquipment => GameScene.Game.CompanionEquipmentCells,
            _ => Array.Empty<DXItemCell>(),
        };
        return link.Slot >= 0 && link.Slot < cells.Length ? cells[link.Slot] : null;
    }

    private void RebuildLinks()
    {
        _links.Clear();
        long cost = 0;
        if (_grid.Cells != null)
            foreach (var cell in _grid.Cells)
            {
                if (cell?.Item == null || cell.LinkedSourceSlot < 0) continue;
                _links.Add(new CellLinkInfo { GridType = cell.LinkedSourceGrid, Slot = cell.LinkedSourceSlot, Count = cell.Item.Count });
                cost += cell.Item.RepairCost(_special.Checked);
            }
        _cost.Text = string.Format(Lang.NPCRepairPanelUi131Label, cost);
        _guildFunds.Enabled = GameScene.Game?.HasGuild == true;
        if (_guildFunds.Checked && cost > (GameScene.Game?.GuildFunds ?? 0))
            _cost.TextColour = Colors.Red;
        else
            _cost.TextColour = Colors.White;
        _repair.Enabled = _links.Count > 0;
    }

    private void ClearUnavailableSpecialItems()
    {
        if (_grid.Cells == null) return;
        foreach (var cell in _grid.Cells)
        {
            if (cell?.Item == null || cell.Item.NextSpecialRepair <= Library.Time.Now) continue;
            cell.ItemGrid[cell.Slot] = null;
            cell.LinkedSourceGrid = GridType.None;
            cell.LinkedSourceSlot = -1;
        }
        _grid.RefreshGrid();
    }
}
