using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Scripts;
using ZirconClient.Formats;

namespace ZirconClient.Controls;

/// <summary>原版 CompanionDialog(Interface 141)：伙伴资料、加成、过滤和伙伴背包。</summary>
public partial class CompanionDialog : DXWindow
{
    private readonly DXControl _body;
    private DXButton _companionTab;
    private readonly DXLabel _name, _level, _experience, _hunger, _health;
    private readonly DXControl _healthFill, _experienceFill, _hungerFill;
    private DXControl _weightFill;
    private readonly DXControl _bonusPanel, _filterPanel, _bagPanel;
    private readonly DXVScrollBar _bonusScroll;
    private readonly DXLabel _bagWeightLabel;
    private readonly List<DXLabel> _bonusRows = new();
    private readonly Dictionary<MirClass, DXCheckButton> _classFilters = new();
    private readonly Dictionary<Rarity, DXCheckButton> _rarityFilters = new();
    private readonly Dictionary<ItemType, DXCheckButton> _typeFilters = new();
    private readonly DXButton _bonusButton, _filterButton, _bagButton, _saveFilter;
    public DXItemCell[] EquipmentCells { get; private set; } = Array.Empty<DXItemCell>();
    public DXItemGrid InventoryGrid { get; private set; }
    private ClientUserCompanion _companion;
    private ObjectRenderer _preview;
    private int _bagWeight;
    private int _maxBagWeight;
    private int _inventorySize;
    private int _page;
    public bool BagVisible => Visible && _page == 3 && InventoryGrid?.Visible == true;

    public CompanionDialog()
    {
        HasTitle = false; HasFooter = false; Movable = true; Size = new Vector2I(464, 372);
        AddControl(new DXImageControl { LibraryFile = LibraryFile.Interface, Index = 141, FixedSize = true, Size = Size, MouseFilter = MouseFilterEnum.Ignore });
        var close = new DXButton { LibraryFile = LibraryFile.Interface, Index = 15 };
        close.Location = new Vector2I((int)Size.X - (int)close.Size.X - 3, 3);
        close.MouseClick += (o, e) => WindowManager.Close(this); AddControl(close);
        AddControl(new DXLabel { Text = Lang.CompanionDialogTitle, FontSize = 10, TextColour = new Color(1f, .85f, .3f), DrawOutline = true, OutlineColour = Colors.Black, Align = HorizontalAlignment.Center, VAlign = VerticalAlignment.Center, AutoSize = false, Location = new Vector2I(0, 8), Size = new Vector2I(464, 18), IsControl = false });
        // 原版 DXTabControl 这里只注册一个 CompanionTab；加成/筛选/背包
        // 是主页签底部的视图切换按钮，不是顶部四个页签。
        _companionTab = new DXButton
        {
            Text = Lang.CompanionDialogTitle,
            FontSize = 10,
            TextColour = new Color(1f, .85f, .3f),
            LibraryFile = LibraryFile.Interface,
            Index = -1,
            Type = DXButton.ButtonType.SelectedTab,
            Location = new Vector2I(15, 38),
            Size = new Vector2I(68, 25),
        };
        AddControl(_companionTab);

        _body = new DXControl { Location = new Vector2I(0, 62), Size = new Vector2I(464, 300), Clip = true }; AddControl(_body);
        AddMainLabel(Lang.CompanionDialogCompanionTabNameLabel, 10, 156, new Color(1f, .85f, .3f), HorizontalAlignment.Left, 60);
        AddMainLabel(Lang.CompanionDialogCompanionTabLevelLabel, 10, 178, new Color(1f, .85f, .3f), HorizontalAlignment.Left, 60);
        AddMainLabel(Lang.CompanionDialogCompanionTabExpLabel, 10, 200, new Color(1f, .85f, .3f), HorizontalAlignment.Left, 60);
        AddMainLabel(Lang.CompanionDialogCompanionTabHungerLabel, 10, 222, new Color(1f, .85f, .3f), HorizontalAlignment.Left, 60);
        _name = AddMainLabel(Lang.CompanionNotSummonedLabel, 73, 156, Colors.White, HorizontalAlignment.Center, 152);
        _level = AddMainLabel("0", 73, 178, Colors.White, HorizontalAlignment.Center, 152);
        _experience = AddMainLabel("0%", 73, 200, Colors.White, HorizontalAlignment.Center, 152);
        _hunger = AddMainLabel("0 / 0", 73, 222, Colors.White, HorizontalAlignment.Center, 152);
        _health = AddMainLabel("0%", 60, 117, Colors.White, HorizontalAlignment.Center, 128);
        _healthFill = AddBar(_body, 60, 123, 4375);
        _experienceFill = AddBar(_body, 73, 202, 4310);
        _hungerFill = AddBar(_body, 73, 224, 4311);

        _bonusPanel = CreateSidePanel(142, false);
        _filterPanel = CreateSidePanel(143, false);
        _bagPanel = CreateSidePanel(142, false);
        _bonusScroll = BuildBonusPanel();
        BuildFilterPanel();
        _bagWeightLabel = BuildBagPanel();

        _bonusButton = AddBottomButton(Lang.CompanionBonusLabel, 10, () => ShowPage(1));
        _filterButton = AddBottomButton(Lang.CompanionFilterLabel, 90, () => ShowPage(2));
        _bagButton = AddBottomButton(Lang.CompanionDialogCompanionTabBagButtonLabel, 170, () => ShowPage(3));
        _saveFilter = new DXButton { Text = Lang.FilterDialogSaveButtonLabel, FontSize = 9, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(370, 40), Size = new Vector2I(80, 24), Visible = false };
        _saveFilter.MouseClick += (o, e) => SaveFilters(); AddControl(_saveFilter);
        DrawEquipment();
        ShowPage(0);
    }

    private DXButton AddBottomButton(string text, int x, Action click)
    {
        var button = new DXButton { Text = text, FontSize = 10, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(x, 325), Size = new Vector2I(70, 25) };
        button.MouseClick += (o, e) => click(); AddControl(button); return button;
    }

    private DXLabel AddMainLabel(string text, int x, int y, Color colour, HorizontalAlignment align, int width)
    {
        var label = new DXLabel { Text = text, FontSize = 9, TextColour = colour, DrawOutline = true, OutlineColour = Colors.Black, Align = align, AutoSize = false, Size = new Vector2I(width, 17), Location = new Vector2I(x, y), IsControl = false };
        _body.AddControl(label); return label;
    }

    private DXControl AddBar(DXControl parent, int x, int y, int index)
    {
        var size = MirSkin.GetSize(LibraryFile.GameInter, index);
        if (size.X <= 0 || size.Y <= 0) size = new Vector2I(1, 1);
        var bar = new DXControl { Location = new Vector2I(x, y), Size = size, Clip = true, MouseFilter = MouseFilterEnum.Ignore };
        bar.AddControl(new DXImageControl { LibraryFile = LibraryFile.GameInter, Index = index, FixedSize = true, Size = size, IsControl = false, MouseFilter = MouseFilterEnum.Ignore });
        parent.AddControl(bar);
        return bar;
    }

    // 兼容当前构造函数使用的原版 GameInter 图库索引调用。
    // 图库尺寸是实际进度条尺寸，不能把索引值误当作宽度。
    private DXControl AddBar(int x, int y, int index)
    {
        return AddBar(_body, x, y, index);
    }

    private static void SetBar(DXControl fill, int width, float percent)
    {
        if (fill == null) return;
        fill.Size = new Vector2I(Math.Max(1, (int)(width * Mathf.Clamp(percent, 0, 1))), Math.Max(1, (int)fill.Size.Y));
    }

    private DXControl CreateSidePanel(int index, bool visible)
    {
        var panel = new DXControl { Location = new Vector2I(252, 0), Size = new Vector2I(208, 300), Clip = true, Visible = visible };
        panel.AddControl(new DXImageControl { LibraryFile = LibraryFile.Interface, Index = index, FixedSize = true, Size = new Vector2I(208, 300), MouseFilter = MouseFilterEnum.Ignore });
        _body.AddControl(panel); return panel;
    }

    private DXVScrollBar BuildBonusPanel()
    {
        var scroll = new DXVScrollBar { Location = new Vector2I(194, 1), Size = new Vector2I(14, 298), VisibleSize = 298, Change = 57, HideWhenNoScroll = true };
        _bonusPanel.AddControl(scroll);
        foreach (int level in new[] { 3, 5, 7, 10, 11, 13, 15 })
        {
            var row = new DXLabel { Text = string.Format(Lang.CompanionNotObtainedLabel, level), FontSize = 9, TextColour = Colors.White, DrawOutline = true, OutlineColour = Colors.Black, Size = new Vector2I(185, 52), Location = new Vector2I(4, 5 + _bonusRows.Count * 57), IsControl = false };
            row.SetMeta("level", level); _bonusPanel.AddControl(row); _bonusRows.Add(row);
        }
        // MaxValue 会立即校正 Value 并发出 ValueChanged；先完成行和滚动范围
        // 初始化，再连接回调，避免构造阶段通过尚未赋值的 _bonusScroll 刷新。
        scroll.MaxValue = _bonusRows.Count * 57 + 15;
        scroll.ValueChanged += (o, e) => RefreshBonusRows();
        return scroll;
    }

    private void RefreshBonusRows()
    {
        foreach (var row in _bonusRows)
        {
            int level = row.GetMeta("level").AsInt32();
            row.Position = new Vector2(4, 5 + _bonusRows.IndexOf(row) * 57 - _bonusScroll.Value);
            var stats = level switch { 3 => _companion?.Level3, 5 => _companion?.Level5, 7 => _companion?.Level7, 10 => _companion?.Level10, 11 => _companion?.Level11, 13 => _companion?.Level13, 15 => _companion?.Level15, _ => null };
            string text = stats == null || stats.Values.Count == 0 ? Lang.CompanionNotObtainedLabel2 : string.Join("\n", stats.Values.Keys.Select(s => stats.GetDisplay(s)).Where(s => !string.IsNullOrWhiteSpace(s)).Take(2));
            row.Text = $"Lv. {level}\n{text}";
        }
    }

    private void BuildFilterPanel()
    {
        AddFilterGroup(Lang.MainPanelClassHint, Enum.GetValues<MirClass>(), _classFilters, 10);
        AddFilterGroup(Lang.CompanionRarityLabel, Enum.GetValues<Rarity>(), _rarityFilters, 70);
        var excluded = new HashSet<ItemType> { ItemType.Nothing, ItemType.Consumable, ItemType.Torch, ItemType.Poison, ItemType.Amulet, ItemType.Meat, ItemType.Ore, ItemType.Currency, ItemType.DarkStone, ItemType.RefineSpecial, ItemType.HorseArmour, ItemType.CompanionFood, ItemType.System, ItemType.ItemPart, ItemType.Hook, ItemType.Float, ItemType.Bait, ItemType.Finder, ItemType.Reel };
        AddFilterGroup(Lang.ConsignmentDialogItemTypesLabel, Enum.GetValues<ItemType>().Where(t => !excluded.Contains(t) && !t.ToString().Contains("Companion")), _typeFilters, 130);
    }

    private void AddFilterGroup<T>(string title, IEnumerable<T> values, Dictionary<T, DXCheckButton> target, int y) where T : struct, Enum
    {
        var header = new DXLabel { Text = title, FontSize = 9, TextColour = new Color(1f, .85f, .3f), DrawOutline = true, OutlineColour = Colors.Black, Location = new Vector2I(10, y), Size = new Vector2I(180, 17), IsControl = false }; _filterPanel.AddControl(header);
        int i = 0;
        foreach (var value in values)
        {
            int column = i % 2, row = i / 2;
            var check = new DXCheckButton(string.Empty) { Location = new Vector2I(10 + column * 110, y + 20 + row * 18), Size = new Vector2I(18, 18) };
            Color labelColour = value is Rarity.Elite ? new Color(.75f, .55f, 1f) : value is Rarity.Superior ? new Color(.65f, 1f, .7f) : Colors.AntiqueWhite;
            var label = new DXLabel { Text = value.ToString(), FontSize = 8, TextColour = labelColour, DrawOutline = true, OutlineColour = Colors.Black, Location = new Vector2I(28 + column * 110, y + 20 + row * 18), Size = new Vector2I(82, 18), IsControl = false };
            _filterPanel.AddControl(check); _filterPanel.AddControl(label); target[value] = check; i++;
        }
    }

    private DXLabel BuildBagPanel()
    {
        _weightFill = AddBar(_bagPanel, 8, 266, 4312);
        var label = new DXLabel { Text = "0 / 0", FontSize = 9, TextColour = Colors.White, DrawOutline = true, OutlineColour = Colors.Black, Align = HorizontalAlignment.Center, AutoSize = false, Size = new Vector2I(80, 16), Location = new Vector2I(7, 264), IsControl = false };
        _bagPanel.AddControl(label); return label;
    }

    // 轻量统计刷新 (对应原版 CompanionBox.Refresh): 只更新标签/进度条/预览,
    // 绝不触碰 GameScene 的 InventoryArray/EquipmentArray —— 那些数组是
    // ItemMove/ItemChanged/AddCompanionItems 的唯一数据源, 每次
    // CompanionUpdate/CompanionItemsGained 都做 ApplyCompanion 的全量
    // Clear+Copy 会抹掉刚写入的协议状态 (S17b 食物 count=9 被置空即此因)。
    public void RefreshCompanionStats(ClientUserCompanion companion)
    {
        if (companion == null) return;
        _name.Text = string.IsNullOrWhiteSpace(companion.Name) ? companion.CompanionInfo?.MonsterInfo?.Local() ?? string.Format(Lang.CompanionCompanionLabel, companion.CompanionIndex) : companion.Name;
        _level.Text = $"Lv. {companion.Level}";
        var info = Globals.CompanionLevelInfoList?.Binding?.FirstOrDefault(x => x.Level == companion.Level);
        int maxExperience = Math.Max(1, info?.MaxExperience ?? 1), maxHunger = Math.Max(1, info?.MaxHunger ?? 100);
        _experience.Text = $"{companion.Experience / (float)maxExperience:P0}"; _hunger.Text = $"{companion.Hunger} / {maxHunger}"; _health.Text = "100%";
        SetBar(_healthFill, 128, 1); SetBar(_experienceFill, 152, companion.Experience / (float)maxExperience); SetBar(_hungerFill, 152, companion.Hunger / (float)maxHunger);
        RefreshPreview(companion);
        RefreshBonusRows(); RefreshBagWeight();
    }

    public void ApplyCompanion(ClientUserCompanion companion)
    {
        var previous = _companion;
        _companion = companion;
        if (companion == null)
        {
            if (GameScene.Game != null)
            {
                // 当前活动伙伴的数组此时可能正引用 GameScene 的工作数组。
                // 先复制成独立快照，才能在 CompanionStore 后清空界面而不
                // 破坏随后 CompanionRetrieve 要恢复的伙伴物品。
                if (previous != null && ReferenceEquals(previous.InventoryArray, GameScene.Game.CompanionInventory))
                    previous.InventoryArray = (ClientUserItem[])GameScene.Game.CompanionInventory.Clone();
                if (previous != null && ReferenceEquals(previous.EquipmentArray, GameScene.Game.CompanionEquipment))
                    previous.EquipmentArray = (ClientUserItem[])GameScene.Game.CompanionEquipment.Clone();
                Array.Clear(GameScene.Game.CompanionInventory, 0, GameScene.Game.CompanionInventory.Length);
                Array.Clear(GameScene.Game.CompanionEquipment, 0, GameScene.Game.CompanionEquipment.Length);
                GameScene.Game.RefreshItemGrids();
            }
            RemovePreview();
            _name.Text = Lang.CompanionNotSummonedLabel; _level.Text = "0"; _experience.Text = "0%"; _hunger.Text = "0 / 0"; _health.Text = "0%";
            SetBar(_healthFill, 128, 0); SetBar(_experienceFill, 152, 0); SetBar(_hungerFill, 152, 0); RefreshBonusRows(); return;
        }
        RefreshCompanionStats(companion);
        // GameScene 的数组是所有 ItemMove/ItemChanged/腰带汇总的唯一数据源。
        // 伙伴模型数组只作为切换伙伴时的快照复制进来，不能让 UI 继续引用
        // 另一份数组，否则操作成功后显示与协议状态会分叉。
        var game = GameScene.Game;
        if (game != null)
        {
            Array.Clear(game.CompanionInventory, 0, game.CompanionInventory.Length);
            Array.Copy(companion.InventoryArray, game.CompanionInventory,
                Math.Min(companion.InventoryArray.Length, game.CompanionInventory.Length));
            Array.Clear(game.CompanionEquipment, 0, game.CompanionEquipment.Length);
            Array.Copy(companion.EquipmentArray, game.CompanionEquipment,
                Math.Min(companion.EquipmentArray.Length, game.CompanionEquipment.Length));
            companion.InventoryArray = game.CompanionInventory;
            companion.EquipmentArray = game.CompanionEquipment;
        }
        if (EquipmentCells != null) for (int i = 0; i < EquipmentCells.Length && i < companion.EquipmentArray.Length; i++) EquipmentCells[i].ItemGrid = companion.EquipmentArray;
        if (InventoryGrid != null)
        {
            InventoryGrid.ItemGrid = companion.InventoryArray;
            InventoryGrid.CreateGrid();
            ApplyInventoryCapacity();
        }
        RefreshBonusRows(); RefreshBagWeight();
    }

    public void ApplyWeight(int weight, int maxWeight, int inventorySize)
    {
        _bagWeight = weight; _maxBagWeight = maxWeight; _inventorySize = inventorySize; RefreshBagWeight();
        ApplyInventoryCapacity();
    }

    public void ApplySkills(Stats level3, Stats level5, Stats level7, Stats level10, Stats level11, Stats level13, Stats level15)
    {
        if (_companion == null) return;
        _companion.Level3 = level3; _companion.Level5 = level5; _companion.Level7 = level7; _companion.Level10 = level10; _companion.Level11 = level11; _companion.Level13 = level13; _companion.Level15 = level15; RefreshBonusRows();
    }

    private void RefreshBagWeight()
    {
        _bagWeightLabel.Text = $"{_bagWeight} / {_maxBagWeight}";
        _bagWeightLabel.TextColour = _bagWeight >= _maxBagWeight && _maxBagWeight > 0 ? Colors.Red : Colors.White;
        SetBar(_weightFill, MirSkin.GetSize(LibraryFile.GameInter, 4312).X, _maxBagWeight <= 0 ? 0 : _bagWeight / (float)_maxBagWeight);
    }

    private void ShowPage(int page)
    {
        _page = page;
        _bonusPanel.Visible = page == 1; _filterPanel.Visible = page == 2; _bagPanel.Visible = page == 3; _saveFilter.Visible = page == 2;
        _bonusButton.Enabled = page != 1; _filterButton.Enabled = page != 2; _bagButton.Enabled = page != 3;
        if (InventoryGrid != null) InventoryGrid.Visible = page == 3;
    }

    private void SaveFilters()
    {
        GameScene.Game?.SendCompanionFilters(_classFilters.Where(x => x.Value.Checked).Select(x => x.Key).ToList(), _rarityFilters.Where(x => x.Value.Checked).Select(x => x.Key).ToList(), _typeFilters.Where(x => x.Value.Checked).Select(x => x.Key).ToList());
    }

    private void ApplyInventoryCapacity()
    {
        if (InventoryGrid?.Cells == null) return;
        for (int i = 0; i < InventoryGrid.Cells.Length; i++)
            InventoryGrid.Cells[i].Enabled = i < _inventorySize;
    }

    private void DrawEquipment()
    {
        EquipmentCells = new DXItemCell[4]; int[] x = { 198, 198, 198, 24 }, y = { 17, 59, 103, 17 };
        int[] empty = { 99, 100, 101, 102 };
        for (int i = 0; i < 4; i++)
        {
            var cell = new DXItemCell { Location = new Vector2I(x[i], y[i]), Size = new Vector2I(36, 36), ItemGrid = GameScene.Game?.CompanionEquipment, Slot = i, GridType = GridType.CompanionEquipment };
            var placeholder = new DXImageControl { LibraryFile = LibraryFile.Interface, Index = empty[i], FixedSize = true, Size = new Vector2I(36, 36), Location = new Vector2I(x[i], y[i]), IsControl = false, MouseFilter = MouseFilterEnum.Ignore };
            _body.AddControl(placeholder);
            cell.BeforeDraw += (o, e) => placeholder.Visible = cell.Item == null;
            _body.AddControl(cell); EquipmentCells[i] = cell;
        }
        InventoryGrid = new DXItemGrid { Location = new Vector2I(10, 14), GridSize = new Vector2I(5, 6), GridPadding = 1, GridType = GridType.CompanionInventory, ItemGrid = GameScene.Game?.CompanionInventory, Visible = false };
        _bagPanel.AddControl(InventoryGrid);
    }

    private void RefreshPreview(ClientUserCompanion companion)
    {
        RemovePreview();
        var info = companion?.CompanionInfo?.MonsterInfo;
        if (info == null || !MonsterLookup.Map.TryGetValue(info.Image, out var lookup)) return;
        _preview = new ObjectRenderer
        {
            Type = ObjectRenderer.Kind.Monster,
            MonsterImage = info.Image,
            MonsterInfo = info,
            BodyLibrary = LibraryCache.Get(lookup.File),
            BodyShape = lookup.Shape,
            BodyOffSet = 1000,
            DisplayName = string.Empty,
            NameColour = Colors.Transparent,
            DrawColour = Colors.White,
            Stats = info.Stats,
            Level = info.Level,
            Position = new Vector2(90, 140),
        };
        _preview.SetAnimation(MirAnimation.Standing);
        _body.AddChild(_preview);
    }

    private void RemovePreview()
    {
        if (_preview == null) return;
        if (_preview.GetParent() != null) _preview.GetParent().RemoveChild(_preview);
        _preview.QueueFree();
        _preview = null;
    }

    public bool AuditLayout(out string details)
    {
        bool equipment = EquipmentCells.Length == 4
            && EquipmentCells[0].Location == new Vector2I(198, 17)
            && EquipmentCells[1].Location == new Vector2I(198, 59)
            && EquipmentCells[2].Location == new Vector2I(198, 103)
            && EquipmentCells[3].Location == new Vector2I(24, 17);
        bool panels = _bonusPanel.Size == new Vector2I(208, 300)
            && _filterPanel.Size == new Vector2I(208, 300)
            && _bagPanel.Size == new Vector2I(208, 300)
            && _bonusRows.Count == 7
            && _bonusScroll.Change == 57;
        bool bars = MirSkin.GetSize(LibraryFile.GameInter, 4310).X > 0
            && MirSkin.GetSize(LibraryFile.GameInter, 4311).X > 0
            && MirSkin.GetSize(LibraryFile.GameInter, 4312).X > 0;
        bool tabs = _companionTab?.Type == DXButton.ButtonType.SelectedTab
            && _companionTab.Position == new Vector2I(15, 38)
            && _bonusButton.Position == new Vector2I(10, 325)
            && _filterButton.Position == new Vector2I(90, 325)
            && _bagButton.Position == new Vector2I(170, 325);
        details = $"size={Size} tabs=1 bottomButtons={tabs} equipment={EquipmentCells.Length} panels=208x300 bonusRows={_bonusRows.Count} bars=4375/4310/4311/4312";
        return Size == new Vector2I(464, 372) && equipment && panels && bars && tabs;
    }
}
