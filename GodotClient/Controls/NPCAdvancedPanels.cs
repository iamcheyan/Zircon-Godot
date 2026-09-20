using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Formats;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>
/// 原版 NPCDialog.cs 中附属工艺窗口的统一 Godot 版本。
/// 这些窗口都遵循同一个旧客户端交互约定：背包物品拖入链接格，确认时发送
/// CellLinkInfo，而不是把物品真的移动到临时窗口。这样精炼、碎片、制作和饰品
/// 窗口不会再被错误地当成普通背包格。
/// </summary>
public sealed partial class NPCAdvancedPanel : DXControl
{
    private readonly List<DXItemGrid> _grids = new();
    private readonly List<DXItemCell> _cells = new();
    private readonly List<DXButton> _buttons = new();
    private readonly List<CellLinkInfo> _pendingLinks = new();
    private DXLabel _title;
    private DXLabel _hint;
    private NPCDialogType _mode;
    private RefineType _refineType;
    private RefineQuality _quality = RefineQuality.Quick;
    private RequiredClass _class = RequiredClass.None;
    private readonly List<ClientRefineInfo> _refines = new();
    private DXControl _retrieveList;
    private DXVScrollBar _retrieveScroll;
    private DXButton _retrieveButton;
    private int _retrieveSelected = -1;
    private DXLabel _rollResult;
    private DXButton _rollClaim;
    private ObjectRenderer _companionPreview;
    private DXButton _weaponCraftButton;
    private DXImageControl _weaponPreview;
    private DXItemCell _weaponTemplate;

    public NPCAdvancedPanel()
    {
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
    }

    public bool TryRouteItem(DXItemCell source)
    {
        if (source?.Item == null) return false;
        var target = FindRouteTarget(source);
        if (!CanAcceptLink(source, target)) return false;
        source.MoveItem(target);
        return true;
    }

    /// <summary>
    /// 统一验证特殊加工目标格。原版 DXItemCell.CheckLink 在手动投放和
    /// DXItemGrid 的“全选”路径都会执行同一套目标/材料匹配检查；不能只
    /// 在 TryRouteItem 中检查，否则批量 MoveItem(DXItemGrid) 会绕过它。
    /// </summary>
    public bool CanAcceptLink(DXItemCell source, DXItemCell target)
    {
        if (source?.Item == null || target == null || target.Item != null || target.LinkedSourceSlot >= 0)
            return false;
        if (!_cells.Contains(target) || !source.CanLinkToSpecialGrid(target.GridType)) return false;
        return MatchesAccessoryMaterial(source, target);
    }

    public bool AuditAccessoryMaterialMatching(out string details)
    {
        Configure(NPCDialogType.AccessoryRefine);
        var target = _cells.FirstOrDefault(c => c.GridType == GridType.AccessoryRefineCombTarget);
        var material = _cells.FirstOrDefault(c => c.GridType == GridType.AccessoryRefineCombItems);
        // ItemInfo 是 MirDB 对象，直接 new 后写属性没有 DB owner，会在
        // OnChanged 中空引用。审计使用已由 DatabaseLoader 挂接的真实条目。
        var firstInfo = Globals.ItemInfoList?.Binding.FirstOrDefault(x => x?.ItemType == ItemType.Necklace);
        var otherInfo = Globals.ItemInfoList?.Binding.FirstOrDefault(x => x?.ItemType == ItemType.Bracelet);
        if (firstInfo == null || otherInfo == null)
        {
            details = "missing necklace/bracelet fixture";
            HidePanel();
            return false;
        }
        DXItemCell.SetCellItem(target, new ClientUserItem(firstInfo, 1));
        var source = new DXItemCell
        {
            GridType = GridType.Inventory,
            Slot = 0,
            ItemGrid = new ClientUserItem[1],
        };
        DXItemCell.SetCellItem(source, new ClientUserItem(otherInfo, 1));
        bool rejectsDifferentType = !CanAcceptLink(source, material);
        source.ItemGrid[0] = new ClientUserItem(firstInfo, 1);
        bool acceptsMatchingType = CanAcceptLink(source, material);

        // F4: 黑铁矿石格只收 BlackIronOre 且拒绝不可精炼物。
        var oreInfo = Globals.ItemInfoList?.Binding.FirstOrDefault(x => x.ItemEffect == ItemEffect.BlackIronOre);
        var oreCell = _cells.FirstOrDefault(c => c.GridType == GridType.RefineBlackIronOre);
        bool oreOk = oreInfo == null || oreCell == null
            || (CanLink(source, oreCell, oreInfo) && !CanLink(source, oreCell, firstInfo));
        HidePanel();
        source.QueueFree();

        // F4: 饰品重置格（AccessoryReset mode）只收 Necklace/Bracelet/Ring、
        // 非 NonRefinable、等级未满；黑铁矿石不是饰品被拒绝。
        Configure(NPCDialogType.AccessoryReset);
        var resetCell = _cells.FirstOrDefault(c => c.GridType == GridType.AccessoryReset);
        var resetSource = new DXItemCell { GridType = GridType.Inventory, Slot = 0, ItemGrid = new ClientUserItem[1] };
        bool resetOk = resetCell == null || oreInfo == null
            || (CanLink(resetSource, resetCell, firstInfo) && !CanLink(resetSource, resetCell, oreInfo));
        HidePanel();
        resetSource.QueueFree();

        details = $"differentTypeRejected={rejectsDifferentType} matchingTypeAccepted={acceptsMatchingType} blackIronOre={oreOk} accessoryReset={resetOk}";
        return rejectsDifferentType && acceptsMatchingType && oreOk && resetOk;
    }

    private static bool CanLink(DXItemCell source, DXItemCell target, ItemInfo info)
    {
        source.ItemGrid[0] = new ClientUserItem(info, 1);
        return source.CanLinkToSpecialGrid(target.GridType);
    }

    private DXItemCell FindRouteTarget(DXItemCell source)
    {
        bool Empty(DXItemCell cell) => cell?.Item == null && cell.LinkedSourceSlot < 0;
        DXItemCell First(GridType type) => _cells.FirstOrDefault(c => c.GridType == type && Empty(c));

        // Targets are selected before their materials. This mirrors the original
        // dialogs and prevents an accessory from being silently routed into a
        // material slot just because it happens to be the first empty cell.
        switch (_mode)
        {
            case NPCDialogType.AccessoryRefineLevel:
                var levelTarget = First(GridType.AccessoryRefineLevelTarget);
                if (levelTarget != null && source.CanLinkToSpecialGrid(levelTarget.GridType)) return levelTarget;
                break;
            case NPCDialogType.AccessoryRefine:
                var combTarget = First(GridType.AccessoryRefineCombTarget);
                if (combTarget != null && source.CanLinkToSpecialGrid(combTarget.GridType)) return combTarget;
                var ore = First(GridType.RefineCorundumOre);
                if (ore != null && source.CanLinkToSpecialGrid(ore.GridType)) return ore;
                break;
            case NPCDialogType.WeaponCraft:
                var template = First(GridType.WeaponCraftTemplate);
                if (template != null && source.CanLinkToSpecialGrid(template.GridType)) return template;
                break;
        }

        return _cells.FirstOrDefault(c => Empty(c) && source.CanLinkToSpecialGrid(c.GridType));
    }

    private bool MatchesAccessoryMaterial(DXItemCell source, DXItemCell target)
    {
        if (source?.Item == null || target == null ||
            target.GridType is not (GridType.AccessoryRefineLevelItems or GridType.AccessoryRefineCombItems)) return true;

        var linkedTarget = _cells.FirstOrDefault(c => c.GridType is GridType.AccessoryRefineLevelTarget or GridType.AccessoryRefineCombTarget)?.Item;
        if (linkedTarget == null || linkedTarget.Info != source.Item.Info) return false;
        if (source.Item.Flags.HasFlag(UserItemFlags.Bound) != linkedTarget.Flags.HasFlag(UserItemFlags.Bound)) return false;
        if (target.GridType == GridType.AccessoryRefineCombItems)
        {
            if (source.Item.Level > 1 || source.Item.AddedStats.Count != linkedTarget.AddedStats.Count) return false;
            if (source.Item.AddedStats.Count > 0 && !source.Item.AddedStats.Compare(linkedTarget.AddedStats)) return false;
        }
        return true;
    }

    public void Configure(NPCDialogType mode)
    {
        _mode = mode;
        Visible = true;
        ClearControls();
        _refineType = RefineType.None;
        _quality = RefineQuality.Quick;
        _class = RequiredClass.None;

        switch (mode)
        {
            case NPCDialogType.RefinementStone:
                BuildRefinementStone();
                break;
            case NPCDialogType.Refine:
                BuildRefine();
                break;
            case NPCDialogType.MasterRefine:
                BuildMasterRefine();
                break;
            case NPCDialogType.RefineRetrieve:
                BuildRetrieve();
                break;
            case NPCDialogType.ItemFragment:
                BuildItemFragment();
                break;
            case NPCDialogType.AccessoryRefineUpgrade:
                BuildAccessoryUpgrade();
                break;
            case NPCDialogType.AccessoryRefineLevel:
                BuildAccessoryLevel();
                break;
            case NPCDialogType.AccessoryReset:
                BuildAccessoryReset();
                break;
            case NPCDialogType.WeaponCraft:
                BuildWeaponCraft();
                break;
            case NPCDialogType.AccessoryRefine:
                BuildAccessoryRefine();
                break;
            case NPCDialogType.WeddingRing:
                BuildWeddingRing();
                break;
            case NPCDialogType.CompanionManage:
                BuildCompanionManage();
                break;
            case NPCDialogType.RollDie:
            case NPCDialogType.RollYut:
                BuildRoll(mode == NPCDialogType.RollYut);
                break;
            default:
                BuildQuestFallback();
                break;
        }
        QueueRedraw();
    }

    public void HidePanel()
    {
        Visible = false;
        ClearControls();
    }

    private void ClearControls()
    {
        if (_companionPreview != null)
        {
            RemoveChild(_companionPreview);
            _companionPreview.QueueFree();
            _companionPreview = null;
        }
        foreach (var c in Controls.ToArray())
        {
            RemoveControl(c);
            c.QueueFree();
        }
        _grids.Clear();
        _cells.Clear();
        _buttons.Clear();
        _retrieveList = null;
        _retrieveScroll = null;
        _retrieveButton = null;
        _retrieveSelected = -1;
        _weaponCraftButton = null;
        _weaponPreview = null;
        _weaponTemplate = null;
    }

    private void Base(string title, int width, int height, bool hasTitle = true, bool hasFooter = false)
    {
        Size = new Vector2I(width, height);
        BackColour = new Color(0.025f, 0.018f, 0.02f, .97f);
        Border = false;
        Add(new LegacyWindowFrame { Size = Size, HasTitle = hasTitle, HasFooter = hasFooter });
        _title = Add(new DXLabel { Text = title, FontSize = 11, TextColour = new Color(1f, .85f, .3f), Align = HorizontalAlignment.Center, AutoSize = false, Size = new Vector2I(width, 24), IsControl = false });
        // 原版工艺窗口没有额外的操作提示文字，物品链接由格子本身的拖拽/点击行为提供。
        _hint = Add(new DXLabel { Visible = false, Text = string.Empty, FontSize = 9, Location = new Vector2I(8, height - 43), IsControl = false });
    }

    private T Add<T>(T control) where T : DXControl
    {
        AddControl(control);
        return control;
    }

    private DXItemGrid AddGrid(GridType type, int x, int y, int columns, int rows, string label)
    {
        if (!string.IsNullOrEmpty(label))
            Add(new DXLabel { Text = label, FontSize = 8, TextColour = Colors.White, Location = new Vector2I(x, y - 15), IsControl = false });
        var grid = new DXItemGrid
        {
            GridType = type,
            GridSize = new Vector2I(columns, rows),
            Location = new Vector2I(x, y),
            ItemGrid = new ClientUserItem[columns * rows],
            Linked = true,
            AllowLink = true,
            // 原版 DXItemGrid 的默认 GridPadding 为 0；保留 1 会使每列多出 2px。
            GridPadding = 0,
            Border = false,
        };
        Add(grid);
        // DXItemGrid 在设置 GridSize 时会先创建一次格子；此时 ItemGrid
        // 还未写入。原版窗口创建顺序相反，必须在这里重建才能让链接格
        // 正确持有自己的临时数组。
        grid.CreateGrid();
        _grids.Add(grid);
        foreach (var cell in grid.Cells)
        {
            _cells.Add(cell);
            cell.LinkChanged += _ => QueueRedraw();
        }
        return grid;
    }

    private DXButton Button(string text, int x, int y, Action action, bool enabled = true)
    {
        var button = Add(new DXButton
        {
            Text = text,
            FontSize = 9,
            Location = new Vector2I(x, y),
            Size = new Vector2I(80, 25),
            LibraryFile = LibraryFile.Interface,
            Index = -1,
            Type = DXButton.ButtonType.SmallButton,
            Enabled = enabled,
        });
        button.MouseClick += (s, e) => action();
        _buttons.Add(button);
        return button;
    }

    private void BuildRefinementStone()
    {
        Base("Refinement Stone", 509, 176);
        AddGrid(GridType.RefinementStoneIronOre, 35, 58, 4, 1, "Iron Ore");
        AddGrid(GridType.RefinementStoneSilverOre, 186, 58, 4, 1, "Silver Ore");
        AddGrid(GridType.RefinementStoneDiamond, 337, 58, 4, 1, "Diamond");
        AddGrid(GridType.RefinementStoneGoldOre, 35, 125, 2, 1, "Gold Ore");
        AddGrid(GridType.RefinementStoneCrystal, 186, 125, 1, 1, "Crystal");
        var gold = Add(new DXNumberField("", 0, 2_000_000_000) { Location = new Vector2I(338, 125), Size = new Vector2I(139, 19) });
        var submit = Button(Lang.GuildCastlePanelRequestButtonLabel, 399, 150, () =>
        {
            var iron = Links(GridType.RefinementStoneIronOre);
            var silver = Links(GridType.RefinementStoneSilverOre);
            var diamond = Links(GridType.RefinementStoneDiamond);
            var goldOre = Links(GridType.RefinementStoneGoldOre);
            var crystal = Links(GridType.RefinementStoneCrystal);
            if (BeginSubmit(iron, silver, diamond, goldOre, crystal).Count == 0) return;
            GameScene.Game?.SendNPCRefinementStone(iron, silver, diamond, goldOre, crystal, gold.Value);
        }, false);
        void RefreshSubmit()
        {
            long balance = GameScene.Game?.Currencies.FirstOrDefault(x => x.Info?.Type == CurrencyType.Gold)?.Amount ?? 0;
            submit.Enabled = _grids.Where(x => x.GridType is GridType.RefinementStoneIronOre or GridType.RefinementStoneSilverOre or GridType.RefinementStoneDiamond or GridType.RefinementStoneGoldOre or GridType.RefinementStoneCrystal)
                .SelectMany(x => x.Cells).All(x => x.Item != null) && gold.Value <= balance;
        }
        foreach (var grid in _grids.Where(x => x.GridType is GridType.RefinementStoneIronOre or GridType.RefinementStoneSilverOre or GridType.RefinementStoneDiamond or GridType.RefinementStoneGoldOre or GridType.RefinementStoneCrystal))
            foreach (var cell in grid.Cells) cell.LinkChanged += _ => RefreshSubmit();
        gold.ValueChanged += (_, _) => RefreshSubmit();
        RefreshSubmit();
    }

    private void BuildItemFragment()
    {
        Base("Fragment Items", 264, 202);
        var grid = AddGrid(GridType.ItemFragment, 9, 37, 7, 3, null);
        var cost = Add(new DXLabel { Text = Lang.NPCAdvancedPanelsUi232Label, FontSize = 9, Location = new Vector2I(89, 154), Size = new Vector2I(157, 20), IsControl = false });
        var selectAll = Button(Lang.NPCAdvancedPanelsUi233Label, 9, 179, () =>
        {
            foreach (var cell in GameScene.Game?.InventoryCells ?? Array.Empty<DXItemCell>())
                if (cell.Item?.CanFragment() == true) cell.MoveItem(grid);
        });
        var submit = Button(Lang.NPCAdvancedPanelsUi234Label, 175, 179, () =>
        {
            var links = Links(GridType.ItemFragment);
            if (BeginSubmit(links).Count > 0) GameScene.Game?.SendNPCFragment(links);
        }, false);
        void RefreshFragment()
        {
            long total = grid.Cells.Where(x => x.Item != null).Sum(x => (long)x.Item.FragmentCost());
            long balance = GameScene.Game?.Currencies.FirstOrDefault(x => x.Info?.Type == CurrencyType.Gold)?.Amount ?? 0;
            cost.Text = string.Format(Lang.NPCAdvancedPanelsUi235Label, total);
            cost.TextColour = total > balance ? Colors.Red : Colors.White;
            submit.Enabled = total > 0 && total <= balance;
            selectAll.Enabled = GameScene.Game?.InventoryCells.Any(x => x.Item?.CanFragment() == true) == true;
        }
        foreach (var cell in grid.Cells) cell.LinkChanged += _ => RefreshFragment();
        RefreshFragment();
    }

    private void BuildRefine()
    {
        Base("Refine", 509, 176);
        AddGrid(GridType.RefineBlackIronOre, 14, 58, 5, 1, "Black Iron Ore");
        AddGrid(GridType.RefineAccessory, 14, 125, 3, 1, "Accessories");
        AddGrid(GridType.RefineSpecial, 154, 125, 1, 1, "Special");

        var types = new[]
        {
            (RefineType.DC, "DC"), (RefineType.SpellPower, "Spell Power"),
            (RefineType.Fire, "Fire"), (RefineType.Ice, "Ice"),
            (RefineType.Lightning, "Lightning"), (RefineType.Wind, "Wind"),
            (RefineType.Holy, "Holy"), (RefineType.Dark, "Dark"),
            (RefineType.Phantom, "Phantom"),
        };
        var checks = new List<DXCheckButton>();
        DXButton submit = null;
        for (int i = 0; i < types.Length; i++)
        {
            int index = i;
            int[] columns = { 181, 265, 340, 416 };
            int x = columns[i % columns.Length];
            int y = 74 + (i / columns.Length) * 22;
            var check = new DXCheckButton(types[i].Item2) { Location = new Vector2I(x, y), Size = new Vector2I(74, 19), LibraryFile = LibraryFile.Interface, Index = -1 };
            check.Changed += (s, e) =>
            {
                if (!check.Checked) return;
                _refineType = types[index].Item1;
                foreach (var other in checks.Where(c => c != check)) other.Checked = false;
                if (submit != null) submit.Enabled = true;
            };
            Add(check); checks.Add(check);
            Add(new DXLabel { Text = types[i].Item2, FontSize = 8, Location = new Vector2I(x + 18, y + 1), IsControl = false });
        }
        Button(Lang.NPCAdvancedPanelsUi236Label, 250, 58, () => CycleQuality());
        Add(new DXLabel { Text = Lang.NPCAdvancedPanelsUi237Label, FontSize = 9, Location = new Vector2I(250, 80), IsControl = false, Name = "RefineQuality" });
        Add(new DXLabel { Text = string.Format(Lang.NPCAdvancedPanelsUi238Label, FormatRefineTime(_quality)), FontSize = 8, Location = new Vector2I(335, 80), IsControl = false, Name = "RefineDuration" });
        submit = Button(Lang.NPCAdvancedPanelsStartLabel, 420, 153, () =>
        {
            if (_refineType == RefineType.None) return;
            var ores = Links(GridType.RefineBlackIronOre);
            var items = Links(GridType.RefineAccessory);
            var specials = Links(GridType.RefineSpecial);
            if (BeginSubmit(ores, items, specials).Count == 0) return;
            GameScene.Game?.SendNPCRefine(_refineType, _quality, ores, items, specials);
        }, false);
    }

    private void BuildMasterRefine()
    {
        // 原版 NPCMasterRefineDialog：491 宽，左侧五个单格链接位，
        // 右侧九个 RefineType 选项；Fragment I/II 必须各放 10 个。
        Base("Master Refine", 509, 170);
        AddGrid(GridType.MasterRefineFragment1, 14, 58, 1, 1, "Fragment I");
        AddGrid(GridType.MasterRefineFragment2, 64, 58, 1, 1, "Fragment II");
        AddGrid(GridType.MasterRefineFragment3, 114, 58, 1, 1, "Fragment III");
        AddGrid(GridType.MasterRefineStone, 14, 124, 1, 1, "Refinement Stone");
        AddGrid(GridType.MasterRefineSpecial, 114, 124, 1, 1, "Special");

        var types = new[]
        {
            (RefineType.DC, "DC"), (RefineType.SpellPower, "Spell Power"),
            (RefineType.Fire, "Fire"), (RefineType.Ice, "Ice"),
            (RefineType.Lightning, "Lightning"), (RefineType.Wind, "Wind"),
            (RefineType.Holy, "Holy"), (RefineType.Dark, "Dark"),
            (RefineType.Phantom, "Phantom"),
        };
        var checks = new List<DXCheckButton>();
        DXButton evaluate = null, submit = null;
        for (int i = 0; i < types.Length; i++)
        {
            int option = i;
            var check = new DXCheckButton(types[i].Item2)
            {
                Location = new Vector2I(new[] { 181, 265, 340, 416 }[i % 4], 74 + (i / 4) * 22),
                Size = new Vector2I(76, 19), FontSize = 8,
                LibraryFile = LibraryFile.Interface, Index = -1,
            };
            check.Changed += (s, e) =>
            {
                if (!check.Checked) return;
                _refineType = types[option].Item1;
                foreach (var other in checks.Where(x => x != check)) other.Checked = false;
                if (evaluate != null) evaluate.Enabled = true;
                if (submit != null) submit.Enabled = true;
            };
            Add(check); checks.Add(check);
            Add(new DXLabel { Text = types[i].Item2, FontSize = 8, Location = new Vector2I((int)check.Position.X + 17, (int)check.Position.Y + 1), IsControl = false });
        }
        evaluate = Button("Evaluate", 320, 128, () => SubmitMaster(true), false);
        submit = Button("Submit", 408, 128, () => SubmitMaster(false), false);
        Add(new DXLabel { Text = $"费用: {Globals.MasterRefineEvaluateCost:#,##0}", FontSize = 8, Location = new Vector2I(320, 111), IsControl = false });
    }

    private void SubmitMaster(bool evaluate)
    {
        if (_refineType == RefineType.None) return;
        var fragment1 = Links(GridType.MasterRefineFragment1);
        var fragment2 = Links(GridType.MasterRefineFragment2);
        var fragment3 = Links(GridType.MasterRefineFragment3);
        var stone = Links(GridType.MasterRefineStone);
        var special = Links(GridType.MasterRefineSpecial);
        // 原版逐项校验并提示：Fragment I/II 必须恰好 10 个，III 至少 1 个，石头至少 1 个。
        if (fragment1.Count == 0 || fragment1[0].Count != 10)
        {
            GameScene.Game?.ReceiveChat("You need Fragment (I) x10 to Master Refine", MessageType.System);
            return;
        }
        if (fragment2.Count == 0 || fragment2[0].Count != 10)
        {
            GameScene.Game?.ReceiveChat("You need Fragment (II) x10 to Master Refine", MessageType.System);
            return;
        }
        if (fragment3.Count == 0)
        {
            GameScene.Game?.ReceiveChat("You need at least 1x Fragment (III) to Master Refine", MessageType.System);
            return;
        }
        if (stone.Count == 0)
        {
            GameScene.Game?.ReceiveChat("需要精炼石 x1 才能进行大师精炼", MessageType.System);
            return;
        }
        if (BeginSubmit(fragment1, fragment2, fragment3, stone, special).Count == 0) return;
        if (evaluate)
        {
            // 原版 Evaluate 弹确认框后才发包。
            var confirm = new ConfirmDialog("Are you sure you want to pay for an evaluation?", "Evaluation",
                () => GameScene.Game?.SendNPCMasterRefineEvaluate(_refineType, fragment1, fragment2, fragment3, stone, special));
            WindowManager.Open(confirm, GameScene.Game?.UILayer ?? GetParent());
        }
        else
            GameScene.Game?.SendNPCMasterRefine(fragment1, fragment2, fragment3, stone, special);
    }

    private void BuildRetrieve()
    {
        Base("Refines", 509, 402, true, true);
        _retrieveList = Add(new DXControl { Location = new Vector2I(9, 37), Size = new Vector2I(491, 302), Clip = true, PassThrough = false });
        _retrieveScroll = Add(new DXVScrollBar { Location = new Vector2I(484, 38), Size = new Vector2I(14, 300), VisibleSize = 302, Change = 43, HideWhenNoScroll = true });
        _retrieveScroll.ValueChanged += (s, e) => RebuildRetrieveRows();
        Button(Lang.NPCAdvancedPanelsRefreshLabel, 110, 359, () => GameScene.Game?.RequestNPCRefineList());
        _retrieveButton = Button(Lang.NPCAdvancedPanelsUi241Label, 214, 359, RetrieveSelected, _refines.Count > 0);
        RebuildRetrieveRows();
    }

    public void SetRefineList(IEnumerable<ClientRefineInfo> list)
    {
        _refines.Clear();
        if (list != null) _refines.AddRange(list.Where(x => x != null));
        if (_mode == NPCDialogType.RefineRetrieve && Visible) RebuildRetrieveRows();
    }

    public void RemoveRefine(int index)
    {
        _refines.RemoveAll(x => x?.Index == index);
        if (_retrieveSelected >= _refines.Count) _retrieveSelected = _refines.Count - 1;
        if (_mode == NPCDialogType.RefineRetrieve && Visible) RebuildRetrieveRows();
    }

    public void RefreshRefineList() => RebuildRetrieveRows();

    /// <summary>服务端处理完工艺请求后，清除仍显示在临时链接格中的来源引用。</summary>
    public void ClearLinkedItems(IEnumerable<CellLinkInfo> links)
    {
        var keys = new HashSet<(GridType Grid, int Slot)>((links ?? Enumerable.Empty<CellLinkInfo>())
            .Where(x => x != null)
            .Select(x => (x.GridType, x.Slot)));
        foreach (var cell in _cells)
        {
            if (cell.LinkedSourceSlot < 0 || !keys.Contains((cell.LinkedSourceGrid, cell.LinkedSourceSlot))) continue;
            if (cell.ItemGrid != null && cell.Slot >= 0 && cell.Slot < cell.ItemGrid.Length)
                cell.ItemGrid[cell.Slot] = null;
            cell.LinkedSourceGrid = GridType.None;
            cell.LinkedSourceSlot = -1;
            cell.RefreshItem();
        }
    }

    public List<CellLinkInfo> CancelLinks()
    {
        var links = _cells
            .Where(c => c != null && c.LinkedSourceSlot >= 0)
            .Select(c => new CellLinkInfo { GridType = c.LinkedSourceGrid, Slot = c.LinkedSourceSlot })
            .Concat(_pendingLinks)
            .GroupBy(x => (x.GridType, x.Slot))
            .Select(x => x.First())
            .ToList();
        ClearLinkedItems(links);
        _pendingLinks.Clear();
        return links;
    }

    public List<CellLinkInfo> CancelUnsubmittedLinks()
    {
        var pending = new HashSet<(GridType Grid, int Slot)>(_pendingLinks.Select(x => (x.GridType, x.Slot)));
        var links = _cells
            .Where(c => c != null && c.LinkedSourceSlot >= 0 && !pending.Contains((c.LinkedSourceGrid, c.LinkedSourceSlot)))
            .Select(c => new CellLinkInfo { GridType = c.LinkedSourceGrid, Slot = c.LinkedSourceSlot })
            .ToList();
        ClearLinkedItems(links);
        return links;
    }

    private void RebuildRetrieveRows()
    {
        if (_retrieveList == null) return;
        foreach (var child in _retrieveList.GetChildren())
        {
            if (child is not Node node) continue;
            if (node is DXControl control) _retrieveList.RemoveControl(control);
            else _retrieveList.RemoveChild(node);
            node.QueueFree();
        }
        _retrieveScroll.MaxValue = Mathf.Max(_retrieveScroll.VisibleSize, _refines.Count * 43);
        if (_retrieveButton != null) _retrieveButton.Enabled = _retrieveSelected >= 0 && _retrieveSelected < _refines.Count;
        for (int i = 0; i < _refines.Count; i++)
        {
            var refine = _refines[i];
            var row = new DXButton
            {
                Text = $"#{refine.Index}  {refine.Weapon?.Info?.ItemName ?? Lang.NPCAdvancedPanelsItemLabel}  {refine.Type}/{refine.Quality}  成功率 {refine.Chance}/{refine.MaxChance}",
                FontSize = 9,
                TextColour = i == _retrieveSelected ? new Color(1f, .85f, .3f) : Colors.White,
                Location = new Vector2I(2, i * 43 - (int)_retrieveScroll.Value),
                Size = new Vector2I(440, 39),
                LibraryFile = LibraryFile.Interface,
                Index = -1,
            };
            int selected = i;
            row.MouseClick += (s, e) => { _retrieveSelected = selected; RebuildRetrieveRows(); };
            _retrieveList.AddControl(row);
        }
    }

    private void RetrieveSelected()
    {
        if (_retrieveSelected < 0 || _retrieveSelected >= _refines.Count) return;
        GameScene.Game?.SendNPCRefineRetrieve(_refines[_retrieveSelected].Index);
    }

    private void BuildSingleGrid(string title, GridType type, int columns, int rows, string action)
    {
        Base(title, Math.Max(280, columns * 39 + 20), rows > 1 ? 205 : 145);
        AddGrid(type, 10, 48, columns, rows, Lang.ConsignmentItemLabel);
        Button(action, (int)Size.X - 94, (int)Size.Y - 38, () => SubmitSingle(type));
    }

    private void BuildSingleTarget(string title, GridType type, string action)
    {
        Base(title, 180, 150);
        AddGrid(type, 72, 48, 1, 1, Lang.NPCAdvancedPanelsTargetLabel);
        Button(action, 49, 108, () => SubmitSingle(type));
    }

    private void BuildAccessoryUpgrade()
    {
        Base("Accessory Upgrade", 509, 176);
        var target = AddGrid(GridType.AccessoryRefineUpgradeTarget, 62, 55, 1, 1, "Item");
        var options = new[]
        {
            (RefineType.DCPercent, "DC 1%"), (RefineType.SPPercent, "Spell Power 1%"),
            (RefineType.HealthPercent, "Health 1%"), (RefineType.ManaPercent, "Mana 1%"),
            (RefineType.DC, "DC 0-1"), (RefineType.SpellPower, "Spell Power 0-1"),
            (RefineType.Health, "Health +10"), (RefineType.Mana, "Mana +10"),
            (RefineType.AC, "AC 1-1"), (RefineType.MR, "MR 1-1"),
            (RefineType.Accuracy, "Accuracy +1"), (RefineType.Agility, "Agility +1"),
            (RefineType.Fire, "Fire +1"), (RefineType.Ice, "Ice +1"),
            (RefineType.Lightning, "Lightning +1"), (RefineType.Wind, "Wind +1"),
            (RefineType.Holy, "Holy +1"), (RefineType.Dark, "Dark +1"),
            (RefineType.Phantom, "Phantom +1"),
        };
        var checks = new List<DXCheckButton>();
        DXButton submit = null;
        int[] columns = { 181, 265, 350, 420 };
        for (int i = 0; i < options.Length; i++)
        {
            int optionIndex = i;
            int x = columns[i % columns.Length];
            int y = 29 + (i / columns.Length) * 17;
            var check = new DXCheckButton(options[i].Item2) { Location = new Vector2I(x, y), Size = new Vector2I(84, 17), LibraryFile = LibraryFile.Interface, Index = -1 };
            check.Changed += (s, e) =>
            {
                if (!check.Checked) return;
                _refineType = options[optionIndex].Item1;
                foreach (var other in checks.Where(c => c != check)) other.Checked = false;
                if (submit != null) submit.Enabled = target.Cells[0].Item != null;
            };
            Add(check); checks.Add(check);
            Add(new DXLabel { Text = options[i].Item2, FontSize = 8, Location = new Vector2I(x + 17, y + 1), IsControl = false });
        }
        submit = Button(Lang.GuildCastlePanelRequestButtonLabel, 40, 124, () => SubmitSingle(GridType.AccessoryRefineUpgradeTarget), false);
        target.Cells[0].LinkChanged += _ => submit.Enabled = _refineType != RefineType.None && target.Cells[0].Item != null;
    }

    private void BuildAccessoryReset()
    {
        Base("Accessory", 118, 130, false);
        var grid = AddGrid(GridType.AccessoryReset, 41, 41, 1, 1, null);
        Add(new DXLabel { Text = string.Format(Lang.NPCAdvancedPanelsUi245Label, Globals.AccessoryResetCost), FontSize = 9, Align = HorizontalAlignment.Center,
            Size = new Vector2I(100, 20), Location = new Vector2I(9, 77), IsControl = false });
        var button = Add(new DXButton { Text = Lang.ResetPasswordResetButtonLabel, FontSize = 9, Type = DXButton.ButtonType.SmallButton, Size = new Vector2I(50, 25), Location = new Vector2I(34, 102), LibraryFile = LibraryFile.Interface, Index = -1, Enabled = false });
        button.MouseClick += (_, _) => SubmitSingle(GridType.AccessoryReset);
        grid.Cells[0].LinkChanged += cell => button.Enabled = cell.Item != null;
    }

    private void BuildAccessoryLevel()
    {
        Base("Accessory Leveling", 264, 262);
        var materials = AddGrid(GridType.AccessoryRefineLevelItems, 9, 97, 7, 3, null);
        var target = AddGrid(GridType.AccessoryRefineLevelTarget, 114, 58, 1, 1, "Accessory");
        var cost = Add(new DXLabel { Text = Lang.NPCAdvancedPanelsUpgradeLabel, FontSize = 9, Location = new Vector2I(89, 177), Size = new Vector2I(157, 20), IsControl = false });
        var selectAll = Button(Lang.NPCAdvancedPanelsUi233Label, 9, 202, () =>
        {
            foreach (var cell in GameScene.Game?.InventoryCells ?? Array.Empty<DXItemCell>())
                if (cell.Item != null) cell.MoveItem(materials);
        });
        var submit = Button(Lang.NPCAdvancedPanelsUpgradeLabel2, 175, 202, () =>
        {
            var targetLink = Link(GridType.AccessoryRefineLevelTarget);
            var materials = Links(GridType.AccessoryRefineLevelItems);
            if (targetLink != null && BeginSubmit(new[] { targetLink }, materials).Count > 0)
                GameScene.Game?.SendNPCAccessoryLevelUp(targetLink, materials);
        }, false);
        void Refresh()
        {
            int count = materials.Cells.Count(x => x.Item != null);
            long gold = GameScene.Game?.Currencies.FirstOrDefault(x => x.Info?.Type == CurrencyType.Gold)?.Amount ?? 0;
            cost.Text = string.Format(Lang.NPCAdvancedPanelsUpgradeLabel3, count);
            cost.TextColour = count > gold ? Colors.Red : Colors.White;
            submit.Enabled = target.Cells[0].Item != null && count > 0;
            selectAll.Enabled = GameScene.Game?.InventoryCells.Any(x => x.Item != null) == true;
        }
        foreach (var cell in materials.Cells) cell.LinkChanged += _ => Refresh();
        target.Cells[0].LinkChanged += _ => Refresh();
        Refresh();
    }

    private void BuildAccessoryRefine()
    {
        Base("Accessory Refine", 509, 246);
        var target = AddGrid(GridType.AccessoryRefineCombTarget, 20, 58, 1, 1, "Accessory");
        var ore = AddGrid(GridType.RefineCorundumOre, 92, 58, 1, 1, "Ore");
        var copies = AddGrid(GridType.AccessoryRefineCombItems, 20, 115, 2, 1, "Copies Of Accessory");
        var options = new[]
        {
            (RefineType.DC, "DC"), (RefineType.SpellPower, "Spell Power"),
            (RefineType.Fire, "Fire"), (RefineType.Ice, "Ice"),
            (RefineType.Lightning, "Lightning"), (RefineType.Wind, "Wind"),
            (RefineType.Holy, "Holy"), (RefineType.Dark, "Dark"),
            (RefineType.Phantom, "Phantom"), (RefineType.Health, "Health"),
            (RefineType.Mana, "Mana"), (RefineType.AC, "AC"),
            (RefineType.MR, "MR"), (RefineType.Accuracy, "Accuracy"),
            (RefineType.Agility, "Agility"),
        };
        var checks = new List<DXCheckButton>();
        DXButton submit = null;
        var cost = Add(new DXLabel { Text = Lang.NPCAdvancedPanelsRefineLabel, FontSize = 9, Location = new Vector2I(89, 164), Size = new Vector2I(157, 20), IsControl = false });
        int[] columns = { 181, 265, 350, 420 };
        for (int i = 0; i < options.Length; i++)
        {
            int option = i;
            int x = columns[i % columns.Length];
            int y = 46 + (i / columns.Length) * 18;
            var check = new DXCheckButton(options[i].Item2) { Location = new Vector2I(x, y), Size = new Vector2I(84, 17), LibraryFile = LibraryFile.Interface, Index = -1 };
            check.Changed += (_, _) =>
            {
                if (!check.Checked) return;
                _refineType = options[option].Item1;
                foreach (var other in checks.Where(x => x != check)) other.Checked = false;
                Refresh();
            };
            Add(check); checks.Add(check);
            Add(new DXLabel { Text = options[i].Item2, FontSize = 8, Location = new Vector2I(x + 17, y + 1), IsControl = false });
        }
        submit = Button(Lang.NPCAdvancedPanelsRefineLabel2, 420, 189, () =>
        {
            if (_refineType == RefineType.None) return;
            var targetLink = Link(GridType.AccessoryRefineCombTarget);
            var oreLink = Link(GridType.RefineCorundumOre);
            var copyLinks = Links(GridType.AccessoryRefineCombItems);
            if (targetLink != null && oreLink != null && copies.Cells.Count(x => x.Item != null) == 2 &&
                BeginSubmit(new[] { targetLink }, new[] { oreLink }, copyLinks).Count > 0)
                GameScene.Game?.SendNPCAccessoryRefine(targetLink, oreLink, copyLinks, _refineType);
        }, false);
        void Refresh()
        {
            long gold = GameScene.Game?.Currencies.FirstOrDefault(x => x.Info?.Type == CurrencyType.Gold)?.Amount ?? 0;
            cost.TextColour = gold < 50_000 ? Colors.Red : Colors.White;
            submit.Enabled = _refineType != RefineType.None && target.Cells[0].Item != null && ore.Cells[0].Item != null && copies.Cells.Count(x => x.Item != null) == 2 && gold >= 50_000;
        }
        foreach (var cell in target.Cells.Concat(ore.Cells).Concat(copies.Cells)) cell.LinkChanged += _ => Refresh();
        Refresh();
    }

    private void BuildWeddingRing()
    {
        Base("Ring", 78, 110, false);
        var grid = AddGrid(GridType.WeddingRing, 30, 41, 1, 1, null);
        var button = Add(new DXButton { Text = Lang.NPCAdvancedPanelsUi252Label, FontSize = 9, Type = DXButton.ButtonType.SmallButton, Size = new Vector2I(50, 25), Location = new Vector2I(14, 85), LibraryFile = LibraryFile.Interface, Index = -1, Enabled = false });
        button.MouseClick += (_, _) => SubmitSingle(GridType.WeddingRing);
        grid.Cells[0].LinkChanged += cell => button.Enabled = cell.Item != null;
    }

    private void BuildTargetAndMaterials(string title, GridType target, GridType materials, string action)
    {
        Base(title, 360, 235);
        AddGrid(target, 162, 43, 1, 1, Lang.NPCAdvancedPanelsTargetLabel2);
        AddGrid(materials, 18, 118, 8, 1, Lang.NPCAdvancedPanelsMaterialLabel);
        Button(action, 258, 171, () =>
        {
            var targetLink = Links(target);
            var materialLinks = Links(materials);
            if (targetLink.Count == 0 || BeginSubmit(targetLink, materialLinks).Count == 0) return;
            if (target == GridType.AccessoryRefineLevelTarget)
                GameScene.Game?.SendNPCAccessoryLevelUp(targetLink[0], materialLinks);
            else
                GameScene.Game?.SendNPCAccessoryRefine(targetLink[0], null, materialLinks);
        });
    }

    private void BuildWeaponCraft()
    {
        Base("Weapon Craft", 268, 326);
        _weaponTemplate = AddGrid(GridType.WeaponCraftTemplate, 107, 40, 1, 1, "Template / Weapon").Cells[0];
        AddGrid(GridType.WeaponCraftYellow, 18, 104, 1, 1, "Yellow");
        AddGrid(GridType.WeaponCraftBlue, 57, 104, 1, 1, "Blue");
        AddGrid(GridType.WeaponCraftRed, 96, 104, 1, 1, "Red");
        AddGrid(GridType.WeaponCraftPurple, 18, 164, 1, 1, "Purple");
        AddGrid(GridType.WeaponCraftGreen, 57, 164, 1, 1, "Green");
        AddGrid(GridType.WeaponCraftGrey, 96, 164, 1, 1, "Grey");
        _weaponPreview = Add(new DXImageControl { LibraryFile = LibraryFile.Equip, Index = 1110, Location = new Vector2I(20, 88), Border = true, Size = new Vector2I(60, 60) });
        Button(Lang.MainPanelClassHint, 18, 209, CycleClass);
        Add(new DXLabel { Text = Lang.NPCAdvancedPanelsClassLabel, FontSize = 9, Location = new Vector2I(18, 236), IsControl = false, Name = "CraftClass" });
        Add(new DXLabel { Text = string.Format(Lang.NPCAdvancedPanelsUi256Label, Globals.CraftWeaponPercentCost), FontSize = 8, Location = new Vector2I(18, 259), IsControl = false, Name = "CraftCost" });
        _weaponCraftButton = Button(Lang.NPCAdvancedPanelsCraftLabel, 154, 284, () =>
        {
            var template = Link(GridType.WeaponCraftTemplate);
            var yellow = Link(GridType.WeaponCraftYellow);
            var blue = Link(GridType.WeaponCraftBlue);
            var red = Link(GridType.WeaponCraftRed);
            var purple = Link(GridType.WeaponCraftPurple);
            var green = Link(GridType.WeaponCraftGreen);
            var grey = Link(GridType.WeaponCraftGrey);
            if (template == null || BeginSubmit(new[] { template, yellow, blue, red, purple, green, grey }.Where(x => x != null)).Count == 0) return;
            GameScene.Game?.SendNPCWeaponCraft(_class, template, yellow, blue, red, purple, green, grey);
        }, false);
        _weaponTemplate.LinkChanged += _ => UpdateWeaponCraftState();
        UpdateWeaponCraftState();
    }

    private void BuildCompanionManage()
    {
        var background = Add(new DXImageControl { LibraryFile = LibraryFile.Interface, Index = 146, MouseFilter = MouseFilterEnum.Ignore });
        Size = (Vector2I)background.Size;
        if (Size.X < 240 || Size.Y < 250) Size = new Vector2I(250, 350);

        var close = Add(new DXButton { LibraryFile = LibraryFile.Interface, Index = 15 });
        close.Location = new Vector2I((int)Size.X - (int)close.Size.X - 3, 3);
        close
            .MouseClick += (s, e) => GameScene.Game?.CloseNPCDialog();
        Add(new DXLabel { Text = Lang.NPCAdvancedPanelsCompanionLabel, FontSize = 11, TextColour = new Color(1f, .85f, .3f), DrawOutline = true,
            Align = HorizontalAlignment.Center, VAlign = VerticalAlignment.Center, Location = new Vector2I(0, 8), Size = new Vector2I((int)Size.X, 18), IsControl = false });

        var available = (Globals.CompanionInfoList?.Binding?.AsEnumerable() ?? Enumerable.Empty<CompanionInfo>())
            .Where(x => x?.MonsterInfo != null).ToList();
        int selected = 0;
        Add(new DXLabel { Text = Lang.ConsignmentDialogNameLabel, FontSize = 9, Location = new Vector2I(38, 190), IsControl = false });
        var nameLabel = Add(new DXLabel { FontSize = 11, TextColour = Colors.White, DrawOutline = true,
            Align = HorizontalAlignment.Center, Size = new Vector2I(150, 24), Location = new Vector2I(70, 192), IsControl = false });
        Add(new DXLabel { Text = Lang.ConsignmentDialogPriceLabel, FontSize = 9, Location = new Vector2I(38, 214), IsControl = false });
        var description = Add(new DXLabel { FontSize = 9, TextColour = Colors.White, Size = new Vector2I(195, 63), Location = new Vector2I(30, 242), IsControl = false });
        var price = Add(new DXLabel { FontSize = 10, TextColour = Colors.White, Align = HorizontalAlignment.Center,
            Size = new Vector2I(150, 22), Location = new Vector2I(70, 214), IsControl = false });
        var index = Add(new DXTextInput { Text = "0", Location = new Vector2I(26, (int)Size.Y - 71), Size = new Vector2I(55, 22) });
        var name = Add(new DXTextInput { Location = new Vector2I(90, (int)Size.Y - 71), Size = new Vector2I(Math.Max(90, (int)Size.X - 180), 22) });

        void RefreshCompanion()
        {
            var info = available.ElementAtOrDefault(selected);
            if (_companionPreview != null)
            {
                RemoveChild(_companionPreview);
                _companionPreview.QueueFree();
                _companionPreview = null;
            }
            if (info?.MonsterInfo != null && MonsterLookup.Map.TryGetValue(info.MonsterInfo.Image, out var lookup))
            {
                _companionPreview = new ObjectRenderer
                {
                    Type = ObjectRenderer.Kind.Monster,
                    MonsterImage = info.MonsterInfo.Image,
                    MonsterInfo = info.MonsterInfo,
                    BodyLibrary = LibraryCache.Get(lookup.File),
                    BodyShape = lookup.Shape,
                    BodyOffSet = 1000,
                    DisplayName = string.Empty,
                    NameColour = Colors.Transparent,
                    DrawColour = Colors.White,
                    Stats = info.MonsterInfo.Stats,
                    Level = info.MonsterInfo.Level,
                    Position = new Vector2(105, 130),
                };
                _companionPreview.SetAnimation(MirAnimation.Standing);
                AddChild(_companionPreview);
            }
            nameLabel.Text = info?.MonsterInfo?.Local() ?? Lang.NPCCompanionStorageCompanionLabel;
            description.Text = info?.Description ?? string.Empty;
            price.Text = info == null ? string.Empty : string.Format(Lang.NPCAdvancedPanelsPriceLabel, info.Price, info.Currency?.Abbreviation ?? string.Empty);
            if (info != null) index.Text = info.Index.ToString();
        }

        var left = Add(new DXButton { LibraryFile = LibraryFile.GameInter, Index = 4112, Location = new Vector2I(20, 135) });
        var right = Add(new DXButton { LibraryFile = LibraryFile.GameInter, Index = 4117, Location = new Vector2I(200, 135) });
        left.MouseClick += (s, e) => { if (selected > 0) { selected--; RefreshCompanion(); } };
        right.MouseClick += (s, e) => { if (selected + 1 < available.Count) { selected++; RefreshCompanion(); } };
        Button(Lang.NPCAdvancedPanelsUi261Label, 28, (int)Size.Y - 43, () => { if (int.TryParse(index.Text, out var value)) GameScene.Game?.SendCompanionUnlock(Math.Max(0, value)); });
        Button(Lang.NPCAdvancedPanelsUi262Label, (int)Size.X - 108, (int)Size.Y - 43, () => { if (int.TryParse(index.Text, out var value) && !string.IsNullOrWhiteSpace(name.Text)) GameScene.Game?.SendCompanionAdopt(Math.Max(0, value), name.Text.Trim()); });
        left.Enabled = selected > 0;
        right.Enabled = selected + 1 < available.Count;
        RefreshCompanion();
    }

    private void BuildRoll(bool yut)
    {
        Base(yut ? "Yut Game" : "Dice Game", 280, 190);
        Add(new DXLabel { Text = yut ? "Yut" : "Dice", FontSize = 22, Align = HorizontalAlignment.Center, AutoSize = false, Size = new Vector2I(280, 70), Location = new Vector2I(0, 48), IsControl = false });
        Button(Lang.SocketDialogStartButtonLabel, 99, 133, () => GameScene.Game?.SendNPCRoll(yut ? 1 : 0));
    }

    public void ShowRollResult(int type, int result)
    {
        if (_mode is not (NPCDialogType.RollDie or NPCDialogType.RollYut)) return;
        _rollResult ??= Add(new DXLabel { FontSize = 14, TextColour = Colors.Yellow, Location = new Vector2I(80, 105), Size = new Vector2I(160, 25), IsControl = false });
        _rollResult.Text = string.Format(Lang.NPCAdvancedPanelsUi263Label, result);
        if (_rollClaim == null)
        {
            _rollClaim = Button(Lang.NPCAdvancedPanelsUi264Label, 99, 158, () => GameScene.Game?.SendNPCRollResult());
        }
        _rollClaim.Visible = true;
    }

    private void BuildQuestFallback()
    {
        Base("Quest List", 360, 150);
        Add(new DXLabel { Text = Lang.NPCAdvancedPanelsQuestLabel, FontSize = 10, Location = new Vector2I(18, 52), IsControl = false });
    }

    private void CycleRefineType()
    {
        var values = (RefineType[])Enum.GetValues(typeof(RefineType));
        _refineType = values[(Array.IndexOf(values, _refineType) + 1) % values.Length];
        foreach (var c in Controls)
            if (c.Name == "RefineType") c.Text = string.Format(Lang.NPCAdvancedPanelsUi266Label, _refineType);
    }

    private void CycleQuality()
    {
        var values = (RefineQuality[])Enum.GetValues(typeof(RefineQuality));
        _quality = values[(Array.IndexOf(values, _quality) + 1) % values.Length];
        foreach (var c in Controls)
        {
            if (c.Name == "RefineQuality") c.Text = string.Format(Lang.NPCAdvancedPanelsUi267Label, _quality);
            if (c.Name == "RefineDuration") c.Text = string.Format(Lang.NPCAdvancedPanelsUi238Label, FormatRefineTime(_quality));
        }
    }

    private static string FormatRefineTime(RefineQuality quality)
    {
        if (quality == RefineQuality.Rush) return string.Empty;
        if (!Globals.RefineTimes.TryGetValue(quality, out var time)) return string.Empty;
        if (time.TotalDays >= 1) return string.Format(Lang.NPCAdvancedPanelsUi269Label, time.TotalDays);
        if (time.TotalHours >= 1) return string.Format(Lang.NPCAdvancedPanelsUi270Label, time.TotalHours);
        return string.Format(Lang.NPCAdvancedPanelsUi271Label, time.TotalMinutes);
    }

    private void CycleClass()
    {
        var values = new[] { RequiredClass.None, RequiredClass.Warrior, RequiredClass.Wizard, RequiredClass.Taoist, RequiredClass.Assassin };
        _class = values[(Array.IndexOf(values, _class) + 1) % values.Length];
        foreach (var c in Controls)
            if (c.Name == "CraftClass") c.Text = string.Format(Lang.NPCAdvancedPanelsClassLabel2, _class);
        UpdateWeaponCraftState();
    }

    private void UpdateWeaponCraftState()
    {
        if (_mode != NPCDialogType.WeaponCraft || _weaponTemplate == null) return;
        var item = _weaponTemplate.Item;
        long cost = Globals.CraftWeaponPercentCost;
        if (item?.Info != null && item.Info.ItemEffect != ItemEffect.WeaponTemplate)
        {
            cost = item.Info.Rarity switch
            {
                Rarity.Common => Globals.CommonCraftWeaponPercentCost,
                Rarity.Superior => Globals.SuperiorCraftWeaponPercentCost,
                Rarity.Elite => Globals.EliteCraftWeaponPercentCost,
                _ => cost,
            };
        }
        var gold = GameScene.Game?.Currencies.FirstOrDefault(x => x.Info?.Type == CurrencyType.Gold)?.Amount ?? 0;
        _weaponCraftButton.Enabled = item != null && _class != RequiredClass.None && cost <= gold;
        if (_weaponPreview != null)
        {
            _weaponPreview.Index = item?.Info?.ItemEffect == ItemEffect.WeaponTemplate ? _class switch
            {
                RequiredClass.Warrior => 1111,
                RequiredClass.Wizard => 1112,
                RequiredClass.Taoist => 1113,
                RequiredClass.Assassin => 1114,
                _ => 1110,
            } : item?.Info?.Image ?? 1110;
        }
        foreach (var c in Controls)
            if (c.Name == "CraftCost") c.Text = string.Format(Lang.NPCAdvancedPanelsUi273Label, cost);
    }

    private void SubmitSingle(GridType type)
    {
        var link = Link(type);
        if (link == null) return;
        var linkedCell = _cells.FirstOrDefault(x => x.GridType == type && x.LinkedSourceSlot >= 0);
        if (_mode == NPCDialogType.AccessoryReset && linkedCell?.Item?.Info?.ItemType is not (ItemType.Ring or ItemType.Bracelet or ItemType.Necklace)) return;
        if (_mode == NPCDialogType.WeddingRing && linkedCell?.Item?.Info?.ItemType != ItemType.Ring) return;
        switch (_mode)
        {
            case NPCDialogType.ItemFragment:
                var links = Links(type);
                if (BeginSubmit(links).Count > 0) GameScene.Game?.SendNPCFragment(links);
                break;
            case NPCDialogType.AccessoryRefineUpgrade:
                if (BeginSubmit(new[] { link }).Count > 0) GameScene.Game?.SendNPCAccessoryUpgrade(link, _refineType);
                break;
            case NPCDialogType.AccessoryReset:
                if (BeginSubmit(new[] { link }).Count > 0) GameScene.Game?.SendNPCAccessoryReset(link);
                break;
            case NPCDialogType.WeddingRing:
                if (BeginSubmit(new[] { link }).Count > 0) GameScene.Game?.SendMarriageMakeRing(link.Slot);
                break;
        }
    }

    private CellLinkInfo Link(GridType type)
    {
        foreach (var cell in _cells)
            if (cell.GridType == type && cell.LinkedSourceSlot >= 0)
                return new CellLinkInfo { GridType = cell.LinkedSourceGrid, Slot = cell.LinkedSourceSlot, Count = cell.Item?.Count ?? 1 };
        return null;
    }

    private List<CellLinkInfo> Links(GridType type)
    {
        var result = new List<CellLinkInfo>();
        foreach (var cell in _cells)
            if (cell.GridType == type && cell.LinkedSourceSlot >= 0)
                result.Add(new CellLinkInfo { GridType = cell.LinkedSourceGrid, Slot = cell.LinkedSourceSlot, Count = cell.Item?.Count ?? 1 });
        return result;
    }

    private List<CellLinkInfo> BeginSubmit(params IEnumerable<CellLinkInfo>[] groups)
    {
        if (_pendingLinks.Count > 0) return new List<CellLinkInfo>();
        var links = (groups ?? Array.Empty<IEnumerable<CellLinkInfo>>())
            .Where(x => x != null)
            .SelectMany(x => x)
            .Where(x => x != null)
            .GroupBy(x => (x.GridType, x.Slot))
            .Select(x => x.First())
            .Select(x => new CellLinkInfo { GridType = x.GridType, Slot = x.Slot, Count = x.Count })
            .ToList();
        if (links.Count == 0) return links;

        _pendingLinks.AddRange(links);
        foreach (var link in links)
        {
            var source = FindSourceCell(link);
            if (source == null) continue;
            source.Locked = true;
            source.UpdateBorder();
        }
        return links;
    }

    public void CompleteLinks(IEnumerable<CellLinkInfo> links)
    {
        var keys = new HashSet<(GridType Grid, int Slot)>((links ?? Enumerable.Empty<CellLinkInfo>())
            .Where(x => x != null).Select(x => (x.GridType, x.Slot)));
        _pendingLinks.RemoveAll(x => keys.Contains((x.GridType, x.Slot)));
        ClearLinkedItems(links);
    }

    private DXItemCell FindSourceCell(CellLinkInfo link)
    {
        var game = GameScene.Game;
        if (game == null || link == null) return null;
        var cells = link.GridType switch
        {
            GridType.Inventory => game.InventoryCells,
            GridType.Equipment => game.EquipmentCells,
            GridType.Storage => game.StorageCells,
            GridType.PartsStorage => game.PartsStorageCells,
            GridType.GuildStorage => game.GuildStorageItemCells,
            GridType.CompanionInventory => game.CompanionInventoryCells,
            GridType.CompanionEquipment => game.CompanionEquipmentCells,
            _ => Array.Empty<DXItemCell>(),
        };
        return link.Slot >= 0 && link.Slot < cells.Length ? cells[link.Slot] : null;
    }
}
