using System;
using System.Linq;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Scripts;
using ZirconClient.Formats;

namespace ZirconClient.Controls;

/// <summary>
/// 物品格子 (移植自 Client/Controls/DXItemCell.cs 的核心行为):
/// Item getter/setter 直读直写底层数组 (GameScene.Inventory/Equipment/Storage),
/// setter 触发 RefreshItem 刷新图标/数量/边框。左键拿起/放下/移动,
/// 右键/双击使用, 中键锁定。腰带格用 QuickInfo/QuickItem 存"链接"而非真实物品。
/// </summary>
public partial class DXItemCell : DXControl
{
    public bool ShowCountLabel = true;
    public const int CellWidth = 36;
    public const int CellHeight = 36;

    // 原版大目标格（例如 NPCSocketDialog.TargetCell）会在窗口自己的
    // BeforeChildrenDraw 中使用 Inventory 图库；普通背包/交易格仍使用
    // StoreItem。保留可选图源，避免把两种显示语义混成一个固定图库。
    public LibraryFile ItemLibraryFile = LibraryFile.StoreItem;

    /// <summary>拿起状态: 记录源格子, 点另一格完成移动 (原版静态 SelectedCell)</summary>
    private static DXItemCell _selectedCell;
    public static DXItemCell SelectedCell
    {
        get => _selectedCell;
        set
        {
            if (_selectedCell == value) return;

            var previous = _selectedCell;
            _selectedCell = null;
            if (previous != null && previous._selected)
            {
                previous._selected = false;
                previous.UpdateBorder();
            }

            _selectedCell = value;
            if (value != null && !value._selected)
            {
                value._selected = true;
                value.UpdateBorder();
            }
        }
    }

    public ClientUserItem[] ItemGrid;
    public int Slot;

    public DXItemGrid HostGrid;
    public bool Locked;
    public bool ReadOnly;
    public GridType LinkedSourceGrid = GridType.None;
    public int LinkedSourceSlot = -1;
    public Action<DXItemCell> LinkChanged;
    public long LinkedCount => Item?.Count ?? 0;

    /// <summary>装备槽空时隐藏 (不画边框/底)</summary>
    public new bool Hidden;

    /// <summary>物品图标在格内水平/垂直居中显示</summary>
    public bool CenterImage = true;

    private GridType _gridType;
    public GridType GridType
    {
        get => _gridType;
        set { _gridType = value; QueueRedraw(); }
    }

    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            if (_selected && SelectedCell != this) SelectedCell = this;
            if (!_selected && SelectedCell == this) SelectedCell = null;
            UpdateBorder();
        }
    }

    /// <summary>
    /// NPC 批量出售使用的独立选中状态。原版出售模式的 SelectedItems
    /// 可以同时选中多个格子，不应复用拿起物品用的全局 SelectedCell。
    /// </summary>
    private bool _saleSelected;
    public bool SaleSelected
    {
        get => _saleSelected;
        set
        {
            if (_saleSelected == value) return;
            _saleSelected = value;
            UpdateBorder();
        }
    }

    // ---- 腰带: 链接物品 (非真实持有) ----
    private ItemInfo _quickInfo;
    public ItemInfo QuickInfo
    {
        get => _quickInfo;
        set
        {
            if (_quickInfo == value) return;
            _quickInfo = value;

            if (value == null)
            {
                QuickInfoItem = null;
                if (GridType == GridType.Belt && GameScene.Game != null && Slot < GameScene.Game.BeltLinks.Length)
                    GameScene.Game.BeltLinks[Slot].LinkInfoIndex = -1;
            }
            else
            {
                QuickInfoItem = new ClientUserItem(value, 1);
                QuickItem = null;
                if (GridType == GridType.Belt && GameScene.Game != null && Slot < GameScene.Game.BeltLinks.Length)
                    GameScene.Game.BeltLinks[Slot].LinkInfoIndex = value.Index;
            }
            RefreshItem();
        }
    }

    public ClientUserItem QuickInfoItem { get; private set; }

    private ClientUserItem _quickItem;
    public ClientUserItem QuickItem
    {
        get => _quickItem;
        set
        {
            if (_quickItem == value) return;
            _quickItem = value;
            if (GridType == GridType.Belt && GameScene.Game != null && Slot < GameScene.Game.BeltLinks.Length)
                GameScene.Game.BeltLinks[Slot].LinkItemIndex = value?.Index ?? -1;
            RefreshItem();
        }
    }

    // ---- 物品: 直读直写底层数组 ----
    public ClientUserItem Item
    {
        get
        {
            if (GridType == GridType.Belt || GridType == GridType.AutoPotion)
            {
                if (QuickInfo != null) return QuickInfoItem;
                return QuickItem;
            }
            if (ItemGrid == null || Slot >= ItemGrid.Length) return null;
            return ItemGrid[Slot];
        }
        set
        {
            if (ItemGrid == null || Slot >= ItemGrid.Length || ItemGrid[Slot] == value) return;
            ItemGrid[Slot] = value;
            if (value != null) value.Slot = Slot;
            RefreshItem();
            ItemChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler<EventArgs> ItemChanged;

    private DXLabel _countLabel;

    public DXItemCell()
    {
        Size = new Vector2(CellWidth, CellHeight);
    }

    public override void _Ready()
    {
        base._Ready();

        _countLabel = new DXLabel
        {
            FontSize = 10,
            TextColour = Colors.White,
            DrawOutline = true,
            OutlineColour = Colors.Black,
            Align = HorizontalAlignment.Right,
            VAlign = VerticalAlignment.Bottom,
            IsControl = false,
        };
        _countLabel.Size = new Vector2(CellWidth - 2, CellHeight - 2);
        _countLabel.Location = new Vector2I(1, 1);
        AddControl(_countLabel);
        MouseEnter += OnHoverEnter;
        MouseLeave += OnHoverLeave;
        UpdateBorder();
        RefreshItem();
    }

    private void OnHoverEnter(object sender, EventArgs e)
    {
        GameScene.Game?.SetHoverItem(Item);
        UpdateBorder();
    }

    private void OnHoverLeave(object sender, EventArgs e)
    {
        GameScene.Game?.SetHoverItem(null);
        UpdateBorder();
    }

    // ---- 绘制 ----

    protected override void DrawControl()
    {
        // 装备栏的武器/衣服/头盔/盾牌由 PaperDoll 绘制；
        // 格子仍保留鼠标命中区域用于卸下，但不重复绘制背包图标。
        if (Hidden) return;
        // 装备槽: 空槽图由面板画 (BeforeDraw), 有物品时这里画图标
        var item = Item;
        // 原版 DXItemCell.LootBoxLocked：未揭示的宝箱格不显示普通物品图标，
        // 而显示 GameInter2 2930 的专用锁定图。
        if (GridType == GridType.LootBox && Locked && item != null)
        {
            var lockedTexture = MirSkin.GetTexture(LibraryFile.GameInter2, 2930);
            if (lockedTexture != null)
            {
                var size = lockedTexture.GetSize();
                DrawTextureRect(lockedTexture,
                    new Rect2((Size.X - size.X) / 2f, (Size.Y - size.Y) / 2f, size.X, size.Y),
                    false, item.Count > 0 ? Colors.White : Colors.Gray);
                return;
            }
        }
        if (item != null)
            DrawItemIcon(item);
    }

    private void DrawItemIcon(ClientUserItem item)
    {
        ItemInfo info = item.Info;
        int drawIndex;

        if (IsCurrencyItem(info))
            drawIndex = CurrencyImage(info, item.Count);
        else
        {
            if (info.ItemEffect == ItemEffect.ItemPart && item.AddedStats != null && item.AddedStats[Stat.ItemIndex] > 0)
            {
                var partInfo = Globals.ItemInfoList?.Binding.FirstOrDefault(x => x.Index == item.AddedStats[Stat.ItemIndex]);
                if (partInfo != null) info = partInfo;
            }
            drawIndex = info.Image;
        }

        var tex = MirSkin.GetTexture(ItemLibraryFile, drawIndex);
        if (tex == null) return;

        var imgSize = tex.GetSize();
        float x = CenterImage ? (Size.X - imgSize.X) / 2f : 0;
        float y = CenterImage ? (Size.Y - imgSize.Y) / 2f : 0;
        bool itemPartReady = item.Info.ItemEffect != ItemEffect.ItemPart
            || item.AddedStats == null
            || item.AddedStats[Stat.ItemIndex] <= 0
            || item.Count >= Math.Max(1, info.PartCount);
        var colour = itemPartReady && item.Count > 0
            ? Colors.White : new Color(0.5f, 0.5f, 0.5f, 1f);
        DrawTextureRect(tex, new Rect2(x, y, imgSize.X, imgSize.Y), false, colour);

        // 角标: New=47, Lock=48, 不可用=49, ItemPart=103 (Interface)
        if (item.New)
            DrawBadge(47, colour);
        if (item.Flags.HasFlag(UserItemFlags.Locked) && !Hidden && GridType != GridType.Inspect)
            DrawBadge(48, colour);
        if (GameScene.Game != null && !GameScene.Game.CanUseItem(item) && !Hidden && GridType != GridType.Inspect)
            DrawBadge(49, colour);
        if (item.Info.ItemEffect == ItemEffect.ItemPart)
            DrawBadge(103, colour);

        DrawSpecialItemEffect(item);
    }

    private void DrawSpecialItemEffect(ClientUserItem item)
    {
        if (item?.Info?.ItemType != ItemType.DarkStone) return;
        int start = item.Info.Shape switch
        {
            1 => 2020,
            2 => 2030,
            3 => 2040,
            4 => 2050,
            _ => 0,
        };
        if (start == 0) return;
        var library = LibraryCache.Get(LibraryFile.GameInter);
        int index = start + (int)(Godot.Time.GetTicksMsec() / 100) % 10;
        if (library == null || index >= library.Images.Length || library.Images[index] == null) return;
        var image = library.Images[index];
        var texture = library.GetImageTexture(index);
        if (texture == null) return;
        DrawTextureRect(texture, new Rect2(-5 + image.OffSetX, 20 + image.OffSetY,
            image.Width, image.Height), false, new Color(1f, 1f, 1f, 0.8f));
    }

    private void DrawBadge(int index, Color colour)
    {
        var tex = MirSkin.GetTexture(LibraryFile.Interface, index);
        if (tex == null) return;
        var imgSize = tex.GetSize();
        float x = index == 49 || index == 103 ? Size.X - imgSize.X - 1 : 1;
        DrawTextureRect(tex, new Rect2(x, 1, imgSize.X, imgSize.Y), false, colour);
    }

    public void UpdateBorder()
    {
        if (Hidden)
        {
            BackColour = Colors.Transparent;
            Border = false;
            return;
        }

        bool active = IsHovered || Selected || SaleSelected || Locked || LinkedSourceSlot >= 0;
        BackColour = !Enabled
            ? new Color(0f, 0f, 0f, 0.49f)
            : active
                ? new Color(1f, 0.49f, 0.49f, 0.49f)
                : Colors.Transparent;
        Border = active;
        BorderColour = active ? Colors.Lime : new Color(0.45f, 0.45f, 0.45f);
        QueueRedraw();
    }

    public void UnlockForTrade()
    {
        Locked = false;
        UpdateBorder();
    }

    /// <summary>数组/数量/链接变化后刷新显示 (原版 RefreshItem)</summary>
    public void RefreshItem()
    {
        var item = Item;

        // 原版在背包/伙伴背包发生变化时，会同步刷新腰带和自动喝药槽；
        // 否则链接槽里的总数量会在拾取、使用、分堆后滞后显示。
        if (GridType is GridType.Inventory or GridType.CompanionInventory && GameScene.Game != null)
        {
            foreach (var cell in GameScene.Game.BeltCells)
                cell?.RefreshItem();
            var potionRows = GameScene.Game.AutoPotionBox?.Rows;
            if (potionRows != null)
                foreach (var row in potionRows)
                    row?.ItemCell?.RefreshItem();
        }

        // 腰带格: 数量 = 背包同类物品合计
        if ((GridType is GridType.Belt or GridType.AutoPotion) && QuickInfo != null && QuickInfoItem != null && GameScene.Game != null)
        {
            long sum = GameScene.Game.Inventory.Where(x => x?.Info == QuickInfo).Sum(x => x.Count);
            sum += GameScene.Game.CompanionInventory.Where(x => x?.Info == QuickInfo).Sum(x => x.Count);
            QuickInfoItem.Count = sum;
        }

        bool showCount = ShowCountLabel && !Hidden && item != null
            && !IsCurrencyItem(item.Info) && item.Info.ItemEffect != ItemEffect.Experience
            && (item.Info.StackSize > 1 || item.Count > 1);

        // 窗口构造阶段可能还没有进入场景树，原版控件此时已经有 Handle，
        // Godot 的 _Ready 尚未创建数量标签；先保留数据，进入树后 _Ready 会重绘。
        if (_countLabel != null)
        {
            _countLabel.Visible = showCount;
            _countLabel.Text = item?.Count.ToString() ?? "";
        }

        UpdateBorder();
        QueueRedraw();
    }

    private static bool IsCurrencyItem(ItemInfo info)
    {
        return Globals.CurrencyInfoList?.Binding.FirstOrDefault(x => x.DropItem == info) != null;
    }

    // 与原版一致：只比较影响堆叠的标记，客户端 New/Locked 状态不阻止合并。
    public static bool CanMergeItems(ClientUserItem left, ClientUserItem right)
    {
        return left?.Info != null && right?.Info == left.Info &&
               left.Flags.HasFlag(UserItemFlags.Bound) == right.Flags.HasFlag(UserItemFlags.Bound) &&
               left.Flags.HasFlag(UserItemFlags.Worthless) == right.Flags.HasFlag(UserItemFlags.Worthless) &&
               left.Flags.HasFlag(UserItemFlags.NonRefinable) == right.Flags.HasFlag(UserItemFlags.NonRefinable) &&
               left.Flags.HasFlag(UserItemFlags.Expirable) == right.Flags.HasFlag(UserItemFlags.Expirable) &&
               left.AddedStats.Compare(right.AddedStats) && left.ExpireTime == right.ExpireTime;
    }

    public static bool CanStoreInStorage(bool inSafeZone, bool marriage, bool itemPart, bool canStore)
        => inSafeZone && !marriage && !itemPart && canStore;

    public static bool CanStoreInPartsStorage(bool inSafeZone, bool marriage, bool itemPart)
        => inSafeZone && !marriage && itemPart;

    public static void SetCellItem(DXItemCell cell, ClientUserItem item)
    {
        if (cell == null) return;
        int index = Math.Max(0, cell.Slot);
        if (cell.ItemGrid == null)
            cell.ItemGrid = new ClientUserItem[index + 1];
        else if (cell.ItemGrid.Length <= index)
            Array.Resize(ref cell.ItemGrid, index + 1);
        cell.ItemGrid[index] = item;
    }

    private static int CurrencyImage(ItemInfo info, long count)
    {
        var currency = Globals.CurrencyInfoList?.Binding.FirstOrDefault(x => x.DropItem == info);
        if (currency == null) return info.Image;

        var image = currency.Images.OrderByDescending(x => x.Amount).FirstOrDefault(x => x.Amount <= count);
        return image?.Image ?? currency.DropItem.Image;
    }

    /// <summary>计算物品在图库中的绘制索引（供鼠标跟随图标等外部使用）。</summary>
    public static int GetItemDrawIndex(ClientUserItem item)
    {
        ItemInfo info = item.Info;
        if (IsCurrencyItem(info))
            return CurrencyImage(info, item.Count);
        if (info.ItemEffect == ItemEffect.ItemPart && item.AddedStats != null && item.AddedStats[Stat.ItemIndex] > 0)
        {
            var partInfo = Globals.ItemInfoList?.Binding.FirstOrDefault(x => x.Index == item.AddedStats[Stat.ItemIndex]);
            if (partInfo != null) return partInfo.Image;
        }
        return info.Image;
    }

    /// <summary>物品图标使用的图库文件（与 DXItemCell.ItemLibraryFile 一致）。</summary>
    public static LibraryFile ItemIconLibraryFile => LibraryFile.StoreItem;

    // ---- 交互 ----

    public override void _GuiInput(InputEvent e)
    {
        if (!IsEnabled)
        {
            if (e is InputEventMouseButton or InputEventMouseMotion) AcceptEvent();
            return;
        }

        if (e is InputEventMouseButton mb)
        {
            // 原版 DXItemCell.OnMouseClick 的前置顺序：货币已拿起时，
            // 物品格不能抢走地图的“丢弃数量”点击；观察和检查格也完全不操作。
            if (Locked || GameScene.Game?.CurrencyPickedUp == true || GameScene.Game?.IsObserver == true ||
                GridType == GridType.Inspect)
            {
                AcceptEvent();
                return;
            }
            // 原版先由 DXControl 派发 MouseDown/MouseClick，再执行物品格自己的
            // 移动/使用逻辑。礼包、宝箱、寄售和邮件附件都依赖这个事件。
            // 按下时基类只派发 MouseDown，抬起时才派发一次 MouseClick，
            // 因此不会与下面仅在 Pressed 分支执行的操作重复。
            base._GuiInput(e);
            // 原版先让 DXControl.OnMouseClick 派发事件，再由 ReadOnly
            // 阻止 MoveItem；邮件只读附件格依赖这个事件发送 MailGetItem。
            if (ReadOnly)
            {
                return;
            }
            if (!mb.Pressed) return;
            if (LinkedSourceSlot >= 0)
            {
                ClearLinkedItem();
                if (SelectedCell == null)
                {
                    AcceptEvent();
                    return;
                }
            }
            if (mb.ButtonIndex == MouseButton.Left)
            {
                // 原版 Alt+左键只阻止普通拿起/移动，不发送聊天链接；聊天链接仅由 Ctrl+中键触发。
                // 必须在双击和 Shift 分堆之前截获，否则第二次点击会误发 ItemMove。
                if (mb.Pressed && (mb.AltPressed || Input.IsKeyPressed(Key.Alt)))
                {
                    AcceptEvent();
                    return;
                }
                if (mb.Pressed && mb.DoubleClick)
                {
                    // Godot 的 DoubleClick 只在第二次按下时为 true (release 恒为 false)。
                    // 双击 = 拿起(第一次按下) + 再次按下; 先放回拿起状态再使用,
                    // 与旧版 (DX WinForms: Click 拿起/放下后 DoubleClick 使用) 行为一致。
                    if (SelectedCell == this) SelectedCell = null;
                    // 原版双击没有 GuildStorage 分支；装备格也只处理婚戒传送，
                    // 不能落入通用 UseItem，否则会把行会仓库物品当成可使用物品，
                    // 或把装备再次伪装成背包→装备操作。
                    if (GridType == GridType.Equipment)
                    {
                        if (Item?.Flags.HasFlag(UserItemFlags.Marriage) == true)
                            GameScene.Game?.SendMarriageTeleport();
                    }
                    else if (GridType is GridType.Belt or GridType.AutoPotion or GridType.Inventory or
                             GridType.CompanionInventory or GridType.CompanionEquipment or
                             GridType.Storage or GridType.PartsStorage)
                    {
                        UseItem();
                    }
                }
                else if (mb.Pressed)
                {
                    FocusControl = this;
                    if (Input.IsKeyPressed(Key.Shift) && Item != null && Item.Count > 1 &&
                        GridType is GridType.Inventory or GridType.Storage or GridType.PartsStorage or GridType.GuildStorage or GridType.CompanionInventory)
                    {
                        GameScene.Game?.OpenItemSplitDialog(Item, GridType, Slot);
                    }
                    else if (!Locked) MoveItem();
                }
            }
            else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
            {
                // 右键使用: 若本格处于"拿起"状态 (SelectedCell == this) 先放回,
                // 否则 UseItem 首行的 SelectedCell == this 检查会吞掉右键。
                if (!Locked)
                {
                    if (SelectedCell == this) SelectedCell = null;
                    if (Item != null && GridType is (GridType.Equipment or GridType.CompanionEquipment))
                    {
                        // 原版装备格右键的优先级：维修/加工窗口先接管，不能先被
                        // “卸下到背包”分支截断。否则装备在维修、镶嵌、升级等
                        // 面板中右键会错误地回到背包。
                        if (GameScene.Game?.TryRouteItemToNpc(this) == true)
                            return;

                        // 钓鱼/驯兽期间原版禁止卸下当前装备。
                        if (GameScene.Game?.IsFishingActive == true || GameScene.Game?.IsTamingActive == true)
                            return;

                        if (GridType == GridType.Equipment && Item.Flags.HasFlag(UserItemFlags.Marriage))
                        {
                            GameScene.Game?.SendMarriageTeleport();
                        }
                        else if (GameScene.Game?.InventoryCells is { Length: > 0 } cells && cells[0]?.HostGrid != null)
                        {
                            MoveItem(cells[0].HostGrid);
                        }
                        return;
                    }
                    // 旧版右键在维修、镶嵌、精炼、制作等窗口中优先把
                    // 背包物品放入目标格，而不是直接使用物品。
                    bool routed = GameScene.Game?.TryRouteItemToNpc(this) ?? false;
                    // 原版 DXItemCell 右键分支：维修/加工窗口之后，背包出售
                    // 模式下右键切换待售选中（InventoryDialog.SellMode），
                    // 不可卖/锁定的物品给出系统提示或静默返回。
                    if (!routed && GameScene.Game?.TrySelectItemForNpcSale(this) == true)
                        return;
                    // 原版装备/伙伴装备右键只允许修理、婚戒传送或回背包；
                    // 交易、邮件、寄售、行会仓库的右键入口来自可持有物品格，
                    // 不会把装备格直接投放到这些窗口。
                    if (!routed && GridType is not (GridType.Equipment or GridType.CompanionEquipment))
                        routed = GameScene.Game?.TryRouteItemToTradeOrConsign(this) ?? false;
                    if (routed) return;

                    if (GameScene.Game?.TryRouteItemToCompanion(this) == true) return;

                    // 原版仓库/部件仓库/行会仓库右键是取回背包；只有背包和伙伴背包
                    // 在没有特殊窗口路由时进入 UseItem。
                    if (GridType is GridType.Storage or GridType.PartsStorage or GridType.GuildStorage)
                    {
                        if (GameScene.Game?.InventoryCells is { Length: > 0 } inventory && inventory[0]?.HostGrid != null)
                            MoveItem(inventory[0].HostGrid);
                        return;
                    }
                    UseItem();
                }
            }
            else if (mb.ButtonIndex == MouseButton.Middle && mb.Pressed)
            {
                if (Input.IsKeyPressed(Key.Ctrl))
                {
                    if (Item != null)
                        GameScene.Game?.LinkItemToChat(Item);
                }
                else if (Item != null && GameScene.Game != null)
                {
                    // 原版中键分支无 Locked/ReadOnly/链接源守卫，直接反相发包
                    // （已锁物品可解锁）；入口处已拦截货币/观察者/检查格。
                    GameScene.Game.SendItemLock(GridType, Slot,
                        GameScene.ComputeItemLockTarget(Item.Flags.HasFlag(UserItemFlags.Locked)));
                }
            }
            else if (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown)
            {
                // 基类调用已在本方法开头完成，滚轮事件只派发一次。
            }
            AcceptEvent();
            return;
        }

        if (e is InputEventMouseMotion)
        {
            // 悬停刷新物品提示 (原版 MouseControl == this -> MouseItem)
            GameScene.Game?.SetHoverItem(Item);
            base._GuiInput(e);
        }
    }

    private void ClearLinkedItem()
    {
        if (LinkedSourceSlot < 0) return;
        var sourceLink = new CellLinkInfo
        {
            GridType = LinkedSourceGrid,
            Slot = LinkedSourceSlot,
        };
        if (ItemGrid != null && Slot >= 0 && Slot < ItemGrid.Length)
            ItemGrid[Slot] = null;
        LinkedSourceGrid = GridType.None;
        LinkedSourceSlot = -1;
        LinkChanged?.Invoke(this);
        RefreshItem();
        // 临时链接被用户取消时，恢复原版 Link=null 对来源格的解锁语义。
        // 交易/寄售/NPC 临时格不能让来源继续保持锁定状态。
        GameScene.Game?.UnlockItemLink(sourceLink);
    }

    /// <summary>拿起/放下/移动 (原版无参 MoveItem)</summary>
    public void MoveItem()
    {
        if (Locked || ReadOnly || GameScene.Game == null || GameScene.Game.IsObserver) return;

        if (SelectedCell == null)
        {
            if (Item == null) return;

            // 特殊窗口中的物品格不是实际背包格。原版点击已经链接的目标格
            // 会解除该链接，而不是把“复制出来的物品”再次拿起进入移动状态。
            // Godot 版用 LinkedSourceGrid/Slot 表示同一关系。
            if (LinkedSourceSlot >= 0)
            {
                ClearLinkedItem();
                return;
            }

            SelectedCell = this;
            return;
        }

        // 放回原位
        if (SelectedCell == this || SelectedCell.Item == null)
        {
            SelectedCell = null;
            return;
        }

        var from = SelectedCell;

        switch (from.GridType)
        {
            case GridType.Equipment:
                // 装备身上的物品不互移
                if (GridType == GridType.Equipment) return;
                // 原版从人物装备格卸下前禁止钓鱼/驯兽状态；否则左键拿起后
                // 点击背包会绕过右键和 ToEquipment 的状态保护发出 ItemMove。
                if (GameScene.Game.IsFishingActive || GameScene.Game.IsTamingActive) return;
                if (Item == null || (from.Item.Info == Item.Info && from.Item.Count < Item.Info.StackSize))
                    from.MoveItem(this);
                else if (HostGrid != null)
                    from.MoveItem(HostGrid);
                SelectedCell = null;
                return;
            case GridType.CompanionEquipment:
                if (GridType == GridType.CompanionEquipment) return;
                if (Item == null || (from.Item.Info == Item.Info && Item.Count < from.Item.Info.StackSize))
                    from.MoveItem(this);
                else if (HostGrid != null)
                    from.MoveItem(HostGrid);
                SelectedCell = null;
                return;
        }

        switch (GridType)
        {
            case GridType.Storage:
                if (from.Item.Info.ItemEffect == ItemEffect.ItemPart) return;
                break;
            case GridType.PartsStorage:
                if (from.Item.Info.ItemEffect != ItemEffect.ItemPart) return;
                break;
            case GridType.Equipment:
                if (!Functions.CorrectSlot(from.Item.Info.ItemType, (EquipmentSlot)Slot) || from.GridType == GridType.Belt) return;
                ToEquipment(from);
                return;
            case GridType.CompanionEquipment:
                if (!Functions.CorrectSlot(from.Item.Info.ItemType, (CompanionSlot)Slot) || from.GridType == GridType.Belt) return;
                ToCompanionEquipment(from);
                return;
        }

        from.MoveItem(this);
    }

    /// <summary>原版消耗品分支的冷却时长：Max(250, Durability) 毫秒。</summary>
    public static int ComputeUseCooldownMs(ItemInfo info) => Math.Max(250, info?.Durability ?? 0);

    /// <summary>原版骑马限制：Shape 19-22 的消耗品（坐骑类食物/道具）骑马时不可使用。</summary>
    public static bool ShapeBlocksWhileMounted(ItemInfo info) => info != null && info.Shape is 19 or 20 or 21 or 22;

    /// <summary>原版 UseItem 的 Ring/Bracelet 语义：优先空槽，否则落到第二槽（可替换）。</summary>
    public static EquipmentSlot? FirstAvailableEquipSlot(DXItemCell[] equipmentCells, params EquipmentSlot[] slots)
    {
        if (slots == null || slots.Length == 0) return null;
        var first = equipmentCells.ElementAtOrDefault((int)slots[0]);
        if (first?.Item == null) return slots[0];
        return slots.Length > 1 ? slots[1] : null;
    }

    private static bool EquipFirstAvailable(DXItemCell fromCell, params EquipmentSlot[] slots)
    {
        var slot = FirstAvailableEquipSlot(GameScene.Game?.EquipmentCells, slots);
        if (slot == null) return false;
        return GameScene.Game.EquipmentCells[(int)slot.Value].ToEquipment(fromCell);
    }

    /// <summary>穿戴到本装备槽 (原版 ToEquipment)</summary>
    public bool ToEquipment(DXItemCell fromCell)
    {
        if (fromCell?.Item == null || Locked || ReadOnly || GameScene.Game == null || GameScene.Game.IsObserver) return false;
        if (Item?.Flags.HasFlag(UserItemFlags.Marriage) == true) return false;
        if (GameScene.Game.IsFishingActive || GameScene.Game.IsTamingActive) return false;
        if (!GameScene.Game.CanWearItem(fromCell.Item, (EquipmentSlot)Slot)) return false;

        if (fromCell == SelectedCell) SelectedCell = null;

        bool merge = Item != null && Item.Count < Item.Info.StackSize && CanMergeItems(Item, fromCell.Item);

        Locked = true;
        fromCell.Locked = true;
        UpdateBorder();
        fromCell.UpdateBorder();
        GameScene.Game.SendItemMove(fromCell.GridType, GridType, fromCell.Slot, Slot, merge);
        return true;
    }

    /// <summary>伙伴装备槽使用 CompanionSlot 校验，不能复用人物 EquipmentSlot。</summary>
    public bool ToCompanionEquipment(DXItemCell fromCell)
    {
        if (Locked || ReadOnly || GameScene.Game == null || GameScene.Game.IsObserver || fromCell?.Item == null) return false;
        if (!GameScene.Game.CanCompanionWearItem(fromCell.Item, (CompanionSlot)Slot)) return false;
        if (fromCell == SelectedCell) SelectedCell = null;

        bool merge = Item != null && Item.Count < Item.Info.StackSize && CanMergeItems(Item, fromCell.Item);

        Locked = true;
        fromCell.Locked = true;
        UpdateBorder();
        fromCell.UpdateBorder();
        GameScene.Game.SendItemMove(fromCell.GridType, GridType, fromCell.Slot, Slot, merge);
        return true;
    }

    /// <summary>移动到指定格 (原版 MoveItem(DXItemCell), 含腰带链接分支)</summary>
    public void MoveItem(DXItemCell toCell)
    {
        if (toCell == null || !toCell.IsEnabled || toCell.Locked ||
            Locked || ReadOnly || GameScene.Game == null || GameScene.Game.IsObserver) return;

        // 目标是腰带: 建立链接
        if (toCell.GridType == GridType.Belt)
        {
            ItemInfo info = null;
            ClientUserItem item = null;

            if (GridType == toCell.GridType)
            {
                info = toCell.QuickInfo;
                item = toCell.QuickItem;
            }

            if (Item != null && Item.Info.ShouldLinkInfo)
                toCell.QuickInfo = Item.Info;
            else if (Item != null)
                toCell.QuickItem = Item;

            if (GridType == toCell.GridType)
            {
                // 原版 MoveItem: 腰带内部交换时把目标格原链接还给源格 (this)。
                // Godot 的 QuickInfo/QuickItem setter 会同步 BeltLinks[Slot]，
                // 因此这里必须设置 this 而不是 toCell，否则交换不会生效。
                QuickInfo = info;
                QuickItem = item;
                var link = GameScene.Game.BeltLinks[Slot];
                GameScene.Game.SendBeltLinkChanged(link.Slot, link.LinkInfoIndex, link.LinkItemIndex);
            }

            if (Selected) SelectedCell = null;

            var newLink = GameScene.Game.BeltLinks[toCell.Slot];
            GameScene.Game.SendBeltLinkChanged(newLink.Slot, newLink.LinkInfoIndex, newLink.LinkItemIndex);
            return;
        }

        // 自动喝药只保存物品类型，不移动背包本体；这是原版 AllowLink 行为。
        if (toCell.GridType == GridType.AutoPotion)
        {
            if (Item == null || Item.Info == null || GridType == toCell.GridType || !Item.Info.CanAutoPot) return;
            toCell.QuickInfo = Item.Info;
            if (Selected) SelectedCell = null;
            GameScene.Game.AutoPotionBox?.SendRowUpdate(toCell.Slot);
            return;
        }

        // 寄售窗口只建立链接，不把背包物品真的移动出去；确认按钮再发原版寄售包。
        if (toCell.GridType == GridType.Consign)
        {
            if (Item == null || GridType is not (GridType.Inventory or GridType.Storage or GridType.PartsStorage) ||
                (GridType == GridType.Inventory && !GameScene.Game.InSafeZone) ||
                Item.Flags.HasFlag(UserItemFlags.Marriage) || Item.Flags.HasFlag(UserItemFlags.NonRefinable) ||
                Item.Flags.HasFlag(UserItemFlags.Bound) || Item.Info?.CanTrade != true) return;
            void LinkConsignmentAmount(long amount)
            {
                SetCellItem(toCell, new ClientUserItem(Item, (int)Math.Clamp(amount, 1L, (long)Item.Count)));
                toCell.LinkedSourceGrid = GridType;
                toCell.LinkedSourceSlot = Slot;
                LockAsLinkedSource();
                toCell.RefreshItem();
                SelectedCell = null;
            }

            if (Item.Count > 1)
                WindowManager.Open(new ItemAmountDialog(Item, LinkConsignmentAmount), GameScene.Game.UILayer);
            else
                LinkConsignmentAmount(Item.Count);
            return;
        }

        if (toCell.GridType == GridType.SocketTarget || toCell.GridType == GridType.SocketGem ||
            toCell.GridType == GridType.SocketCombine1 || toCell.GridType == GridType.SocketCombine2 ||
            toCell.GridType == GridType.SocketCombine3 ||
            toCell.GridType == GridType.RefinementStoneIronOre ||
            toCell.GridType == GridType.RefinementStoneSilverOre ||
            toCell.GridType == GridType.RefinementStoneDiamond ||
            toCell.GridType == GridType.RefinementStoneGoldOre ||
            toCell.GridType == GridType.RefinementStoneCrystal ||
            toCell.GridType == GridType.RefineBlackIronOre ||
            toCell.GridType == GridType.RefineCorundumOre ||
            toCell.GridType == GridType.RefineAccessory ||
            toCell.GridType == GridType.RefineSpecial ||
            toCell.GridType == GridType.ItemFragment ||
            toCell.GridType == GridType.AccessoryRefineUpgradeTarget ||
            toCell.GridType == GridType.AccessoryRefineLevelTarget ||
            toCell.GridType == GridType.AccessoryRefineLevelItems ||
            toCell.GridType == GridType.MasterRefineFragment1 ||
            toCell.GridType == GridType.MasterRefineFragment2 ||
            toCell.GridType == GridType.MasterRefineFragment3 ||
            toCell.GridType == GridType.MasterRefineStone ||
            toCell.GridType == GridType.MasterRefineSpecial ||
            toCell.GridType == GridType.AccessoryReset ||
            toCell.GridType == GridType.WeaponCraftTemplate ||
            toCell.GridType == GridType.WeaponCraftYellow ||
            toCell.GridType == GridType.WeaponCraftBlue ||
            toCell.GridType == GridType.WeaponCraftRed ||
            toCell.GridType == GridType.WeaponCraftPurple ||
            toCell.GridType == GridType.WeaponCraftGreen ||
            toCell.GridType == GridType.WeaponCraftGrey ||
            toCell.GridType == GridType.AccessoryRefineCombTarget ||
            toCell.GridType == GridType.AccessoryRefineCombItems ||
            toCell.GridType == GridType.Repair ||
            toCell.GridType == GridType.TradeUser ||
            toCell.GridType == GridType.SendMail ||
            toCell.GridType == GridType.WeddingRing)
        {
            if (Item == null) return;
            if (toCell.GridType == GridType.Repair &&
                !(GameScene.Game?.CanRouteRepairItem(this) ?? true)) return;
            if (!CanLinkToSpecialGrid(toCell.GridType)) return;
            if (toCell.GridType is GridType.AccessoryRefineLevelItems or GridType.AccessoryRefineCombItems &&
                !(GameScene.Game?.CanRouteAdvancedItem(this, toCell) ?? true)) return;
            if (toCell.GridType == GridType.Repair &&
                (Item.Info == null || !Item.Info.CanRepair ||
                 Item.CurrentDurability >= Item.MaxDurability)) return;
            void LinkAmount(long amount)
            {
                SetCellItem(toCell, new ClientUserItem(Item, (int)Math.Clamp(amount, 1L, (long)Item.Count)));
                toCell.LinkedSourceGrid = GridType;
                toCell.LinkedSourceSlot = Slot;
                LockAsLinkedSource();
                toCell.RefreshItem();
                toCell.LinkChanged?.Invoke(toCell);
                if (toCell.GridType == GridType.TradeUser)
                {
                    Locked = true;
                    UpdateBorder();
                }
                SelectedCell = null;
            }
            if (Selected) SelectedCell = null;
            int fixedCount = SpecialLinkFixedCount(toCell.GridType);
            if (fixedCount > 0)
            {
                if (Item.Count < fixedCount) return;
                LinkAmount(fixedCount);
            }
            else if (Item.Count > 1)
            {
                var amount = new ItemAmountDialog(Item, LinkAmount);
                WindowManager.Open(amount, GameScene.Game.UILayer);
            }
            else
                LinkAmount(Item.Count);
            return;
        }

        // 源是腰带: 清除链接
        if (GridType == GridType.Belt)
        {
            QuickInfo = null;
            QuickItem = null;
            var link = GameScene.Game.BeltLinks[Slot];
            GameScene.Game.SendBeltLinkChanged(link.Slot, link.LinkInfoIndex, link.LinkItemIndex);
            if (Selected) SelectedCell = null;
            return;
        }

        // 自动喝药槽同样是配置链接，不是可发送给服务器的物品网格。
        // 原版从该槽拖出时清空当前槽并提交当前行配置；不能落入普通 ItemMove。
        if (GridType == GridType.AutoPotion)
        {
            QuickInfo = null;
            GameScene.Game.AutoPotionBox?.SendRowUpdate(Slot);
            if (Selected) SelectedCell = null;
            return;
        }

        if (toCell.GridType == GridType.Storage &&
            (GridType is not (GridType.Inventory or GridType.Equipment or GridType.GuildStorage or GridType.CompanionInventory or GridType.CompanionEquipment) ||
             !CanStoreInStorage(GameScene.Game.InSafeZone, Item.Flags.HasFlag(UserItemFlags.Marriage),
                 Item.Info.ItemEffect == ItemEffect.ItemPart, Item.Info.CanStore))) return;
        if (toCell.GridType == GridType.PartsStorage &&
            (GridType is not (GridType.Inventory or GridType.GuildStorage or GridType.CompanionInventory or GridType.CompanionEquipment) || GameScene.Game.InSafeZone == false ||
             Item.Flags.HasFlag(UserItemFlags.Marriage) || Item.Info?.ItemEffect != ItemEffect.ItemPart)) return;
        if (toCell.GridType == GridType.GuildStorage &&
            (GridType is not (GridType.Inventory or GridType.Storage or GridType.PartsStorage or GridType.Equipment or GridType.CompanionInventory or GridType.CompanionEquipment) ||
             !GameScene.Game.InSafeZone ||
             Item.Flags.HasFlag(UserItemFlags.Marriage) || Item.Flags.HasFlag(UserItemFlags.Bound) ||
             Item.Info?.CanTrade != true)) return;
        if (GridType == GridType.GuildStorage && !GameScene.Game.InSafeZone &&
            toCell.GridType is not (GridType.Storage or GridType.PartsStorage)) return;

        bool merge = toCell.Item != null && toCell.Item.Count < toCell.Item.Info.StackSize && CanMergeItems(Item, toCell.Item);

        if (Selected) SelectedCell = null;

        Locked = true;
        toCell.Locked = true;
        UpdateBorder();
        toCell.UpdateBorder();
        GameScene.Game.SendItemMove(GridType, toCell.GridType, Slot, toCell.Slot, merge);
    }

    public bool CanLinkToSpecialGrid(GridType target)
    {
        if (Item?.Info == null) return false;
        bool inventorySource = GridType is GridType.Inventory or GridType.CompanionInventory;
        bool notRefinable = Item.Flags.HasFlag(UserItemFlags.NonRefinable);
        bool married = Item.Flags.HasFlag(UserItemFlags.Marriage);
        bool accessory = Item.Info.ItemType is ItemType.Necklace or ItemType.Bracelet or ItemType.Ring;
        bool tradeSource = GridType is GridType.Inventory or GridType.Storage or GridType.PartsStorage or GridType.Equipment;
        bool repairSource = GridType is GridType.Inventory or GridType.Equipment or GridType.Storage or GridType.GuildStorage or GridType.CompanionInventory;
        bool materialSource = GridType is GridType.Inventory or GridType.Storage or GridType.CompanionInventory;
        return target switch
        {
            GridType.Repair => repairSource && !married && Item.Info.CanRepair && Item.CurrentDurability < Item.MaxDurability,
            GridType.SocketTarget => GridType == GridType.Inventory && (Item.Info.ItemType is ItemType.Weapon or ItemType.Armour),
            GridType.SocketGem or GridType.SocketCombine1 or GridType.SocketCombine2 or GridType.SocketCombine3 => GridType == GridType.Inventory && Item.Info.ItemType == ItemType.SocketGem,
            GridType.ItemFragment => inventorySource && !married && !notRefinable && !Item.Flags.HasFlag(UserItemFlags.Locked) && Item.CanFragment(),
            GridType.RefinementStoneIronOre => materialSource && !married && !notRefinable && Item.Info.ItemEffect == ItemEffect.IronOre,
            GridType.RefinementStoneSilverOre => materialSource && !married && !notRefinable && Item.Info.ItemEffect == ItemEffect.SilverOre,
            GridType.RefinementStoneDiamond => materialSource && !married && !notRefinable && Item.Info.ItemEffect == ItemEffect.Diamond,
            GridType.RefinementStoneGoldOre => materialSource && !married && !notRefinable && Item.Info.ItemEffect == ItemEffect.GoldOre,
            GridType.RefinementStoneCrystal => materialSource && !married && !notRefinable && Item.Info.ItemEffect == ItemEffect.Crystal,
            GridType.RefineBlackIronOre => materialSource && !married && !notRefinable && Item.Info.ItemEffect == ItemEffect.BlackIronOre,
            GridType.RefineCorundumOre => materialSource && !married && !notRefinable && Item.Info.ItemEffect == ItemEffect.Corundum,
            GridType.RefineAccessory => materialSource && !married && !notRefinable && accessory,
            GridType.RefineSpecial => materialSource && !married && !notRefinable && Item.Info.ItemType == ItemType.RefineSpecial && Item.Info.Shape == 1,
            GridType.MasterRefineFragment1 => materialSource && !married && !notRefinable && Item.Info.ItemEffect == ItemEffect.Fragment1,
            GridType.MasterRefineFragment2 => materialSource && !married && !notRefinable && Item.Info.ItemEffect == ItemEffect.Fragment2,
            GridType.MasterRefineFragment3 => materialSource && !married && !notRefinable && Item.Info.ItemEffect == ItemEffect.Fragment3,
            GridType.MasterRefineStone => materialSource && !married && !notRefinable && Item.Info.ItemEffect == ItemEffect.RefinementStone,
            GridType.MasterRefineSpecial => materialSource && !married && !notRefinable && Item.Info.ItemType == ItemType.RefineSpecial && Item.Info.Shape == 5,
            GridType.WeaponCraftTemplate => GridType == GridType.Inventory && (Item.Info.ItemType == ItemType.Weapon || Item.Info.ItemEffect == ItemEffect.WeaponTemplate),
            GridType.WeaponCraftYellow => GridType == GridType.Inventory && Item.Info.ItemEffect == ItemEffect.YellowSlot,
            GridType.WeaponCraftBlue => GridType == GridType.Inventory && Item.Info.ItemEffect == ItemEffect.BlueSlot,
            GridType.WeaponCraftRed => GridType == GridType.Inventory && Item.Info.ItemEffect == ItemEffect.RedSlot,
            GridType.WeaponCraftPurple => GridType == GridType.Inventory && Item.Info.ItemEffect == ItemEffect.PurpleSlot,
            GridType.WeaponCraftGreen => GridType == GridType.Inventory && Item.Info.ItemEffect == ItemEffect.GreenSlot,
            GridType.WeaponCraftGrey => GridType == GridType.Inventory && Item.Info.ItemEffect == ItemEffect.GreySlot,
            GridType.TradeUser => tradeSource && !married,
            GridType.SendMail => tradeSource && GridType != GridType.Equipment && !married &&
                (GridType is not (GridType.Inventory or GridType.CompanionInventory) || GameScene.Game.InSafeZone),
            GridType.WeddingRing => GridType == GridType.Inventory && Item.Info.ItemType == ItemType.Ring &&
                (GameScene.Game.CanWearItem(Item, EquipmentSlot.RingL) || GameScene.Game.CanWearItem(Item, EquipmentSlot.RingR)),
            GridType.AccessoryReset => GridType is GridType.Inventory or GridType.Equipment or GridType.CompanionInventory or GridType.Storage &&
                accessory && !notRefinable && Item.Level < Globals.AccessoryExperienceList.Count,
            GridType.AccessoryRefineUpgradeTarget =>
                GridType is GridType.Inventory or GridType.Equipment or GridType.CompanionInventory or GridType.Storage &&
                accessory && !notRefinable && Item.Flags.HasFlag(UserItemFlags.Refinable),
            GridType.AccessoryRefineLevelTarget =>
                GridType is GridType.Inventory or GridType.Equipment or GridType.CompanionInventory or GridType.Storage &&
                accessory && !notRefinable && !Item.Flags.HasFlag(UserItemFlags.Refinable) && Item.Level < Globals.AccessoryExperienceList.Count,
            GridType.AccessoryRefineLevelItems or GridType.AccessoryRefineCombItems =>
                GridType is GridType.Inventory or GridType.CompanionInventory or GridType.Storage &&
                !married && !notRefinable && !Item.Flags.HasFlag(UserItemFlags.Locked) && accessory,
            GridType.AccessoryRefineCombTarget =>
                GridType is GridType.Inventory or GridType.Equipment or GridType.CompanionInventory or GridType.Storage &&
                accessory && !notRefinable && !Item.Flags.HasFlag(UserItemFlags.Refinable) && Item.Level <= 1,
            _ => true,
        };
    }

    /// <summary>移动到网格的空位/可堆叠格 (原版 MoveItem(DXItemGrid) 语义)</summary>
    public bool MoveItem(DXItemGrid toGrid)
    {
        if (Item == null || toGrid == null) return false;
        if (toGrid.GridType == GridType.Belt || toGrid.GridType == GridType.AutoPotion) return false;
        // 原版 (!Linked && Link != null) 闸门：特殊窗口中的复制物只可由
        // 点击解除链接，不能再次作为真实来源参与交易/加工/ItemMove。
        if (Locked || LinkedSourceSlot >= 0 || GameScene.Game == null || GameScene.Game.IsObserver) return false;
        // 腰带/自动喝药格保存的是快捷配置，不是可参与 ItemMove 的持有物品。
        if (GridType is GridType.Belt or GridType.AutoPotion) return false;
        if (toGrid.GridType == GridType.Storage &&
            !CanStoreInStorage(GameScene.Game.InSafeZone, Item.Flags.HasFlag(UserItemFlags.Marriage),
                Item.Info?.ItemEffect == ItemEffect.ItemPart, Item.Info?.CanStore == true)) return false;
        if (toGrid.GridType == GridType.PartsStorage &&
            !CanStoreInPartsStorage(GameScene.Game.InSafeZone, Item.Flags.HasFlag(UserItemFlags.Marriage),
                Item.Info?.ItemEffect == ItemEffect.ItemPart)) return false;

        if (IsSpecialLinkGrid(toGrid.GridType))
        {
            if (!CanLinkToSpecialGrid(toGrid.GridType)) return false;
            var linkCell = toGrid.Cells.FirstOrDefault(x => !x.Locked && x.Enabled && x.Item == null && x.LinkedSourceSlot < 0);
            if (linkCell == null) return false;
            if (linkCell.GridType is GridType.AccessoryRefineLevelItems or GridType.AccessoryRefineCombItems &&
                !(GameScene.Game?.CanRouteAdvancedItem(this, linkCell) ?? true)) return false;
            int fixedCount = SpecialLinkFixedCount(toGrid.GridType);
            if (fixedCount > 0)
            {
                if (Item.Count < fixedCount) return false;
                SetCellItem(linkCell, new ClientUserItem(Item, fixedCount));
                linkCell.LinkedSourceGrid = GridType;
                linkCell.LinkedSourceSlot = Slot;
                LockAsLinkedSource();
                linkCell.RefreshItem();
                linkCell.LinkChanged?.Invoke(linkCell);
                SelectedCell = null;
                return true;
            }
            MoveItem(linkCell);
            return true;
        }

        DXItemCell toCell = null;
        bool merge = false;

        foreach (var cell in toGrid.Cells)
        {
            if (cell.Locked || !cell.Enabled) continue;

            var toItem = cell.Item;
            if (toItem == null)
            {
                if (toCell == null) toCell = cell;
                continue;
            }

            if (toItem.Info != Item.Info || toItem.Count >= toItem.Info.StackSize || !CanMergeItems(Item, toItem)) continue;

            toCell = cell;
            merge = true;
            break;
        }

        if (toCell == null) return false;

        if (Selected) SelectedCell = null;
        Locked = true;
        toCell.Locked = true;
        UpdateBorder();
        toCell.UpdateBorder();
        GameScene.Game.SendItemMove(GridType, toCell.GridType, Slot, toCell.Slot, merge);
        return true;
    }

    private static bool IsSpecialLinkGrid(GridType type) => type is
        GridType.SocketTarget or GridType.SocketGem or GridType.SocketCombine1 or GridType.SocketCombine2 or GridType.SocketCombine3 or
        GridType.RefinementStoneIronOre or GridType.RefinementStoneSilverOre or GridType.RefinementStoneDiamond or GridType.RefinementStoneGoldOre or GridType.RefinementStoneCrystal or
        GridType.RefineBlackIronOre or GridType.RefineCorundumOre or GridType.ItemFragment or GridType.AccessoryRefineUpgradeTarget or GridType.AccessoryRefineLevelTarget or GridType.AccessoryRefineLevelItems or
        GridType.RefineAccessory or GridType.RefineSpecial or
        GridType.MasterRefineFragment1 or GridType.MasterRefineFragment2 or GridType.MasterRefineFragment3 or GridType.MasterRefineStone or GridType.MasterRefineSpecial or GridType.AccessoryReset or
        GridType.WeaponCraftTemplate or GridType.WeaponCraftYellow or GridType.WeaponCraftBlue or GridType.WeaponCraftRed or GridType.WeaponCraftPurple or GridType.WeaponCraftGreen or GridType.WeaponCraftGrey or
        GridType.AccessoryRefineCombTarget or GridType.AccessoryRefineCombItems or GridType.Repair or GridType.TradeUser or GridType.SendMail or GridType.WeddingRing;

    private static int SpecialLinkFixedCount(GridType type) => type switch
    {
        GridType.RefineSpecial or GridType.RefinementStoneCrystal or
        GridType.MasterRefineSpecial or GridType.MasterRefineStone or GridType.WeaponCraftTemplate or
        GridType.WeaponCraftYellow or GridType.WeaponCraftBlue or GridType.WeaponCraftRed or
        GridType.WeaponCraftPurple or GridType.WeaponCraftGreen or GridType.WeaponCraftGrey => 1,
        GridType.MasterRefineFragment1 or GridType.MasterRefineFragment2 => 10,
        _ => 0,
    };

    /// <summary>
    /// 原版把临时 Link 写回来源格的 Linked 状态；来源格在 Link 存在期间
    /// 不能再次拿起/移动/删除。Godot 用 Locked 表达同一输入闸门，解除链接
    /// 时由 UnlockItemLink 统一恢复。
    /// </summary>
    private void LockAsLinkedSource()
    {
        Locked = true;
        UpdateBorder();
    }

    /// <summary>使用物品: 可穿戴->穿戴, 消耗品/卷轴->C.ItemUse，并处理系统物品/礼包/宝箱。</summary>
    public bool UseItem()
    {
        if (AutoLoginArgs.OperationAuditExt && GridType == GridType.CompanionInventory && Item?.Info?.ItemType == ItemType.CompanionFood)
            GD.Print($"[OperationAuditExt] S17b gate itemNull={Item == null} locked={Locked} readOnly={ReadOnly} linked={LinkedSourceSlot} selected={SelectedCell == this} grid={GridType}");
        if (Item == null || Locked || ReadOnly || LinkedSourceSlot >= 0 || SelectedCell == this ||
            GameScene.Game == null || GameScene.Game.IsObserver) return false;
        if (!GameScene.Game.CanUseItem(Item)) return false;
        if (GameScene.Game.IsFishingActive || GameScene.Game.IsTamingActive) return false;
        GameScene.Game.PlaySound(GetItemSound());

        // 腰带格: 找到背包里的本体使用
        if (GridType is GridType.Belt or GridType.AutoPotion)
        {
            DXItemCell cell;
            if (QuickInfo != null)
                cell = GameScene.Game.InventoryCells.FirstOrDefault(x => x?.Item?.Info == QuickInfo) ??
                       GameScene.Game.CompanionInventoryCells.FirstOrDefault(x => x?.Item?.Info == QuickInfo);
            else
                cell = GameScene.Game.InventoryCells.FirstOrDefault(x => x?.Item == QuickItem) ??
                       GameScene.Game.CompanionInventoryCells.FirstOrDefault(x => x?.Item == QuickItem);
            return cell?.UseItem() == true;
        }

        switch (Item.Info.ItemType)
        {
            case ItemType.Weapon:
                return GameScene.Game.EquipmentCells[(int)EquipmentSlot.Weapon].ToEquipment(this);
            case ItemType.Armour:
                return GameScene.Game.EquipmentCells[(int)EquipmentSlot.Armour].ToEquipment(this);
            case ItemType.Torch:
                return GameScene.Game.EquipmentCells[(int)EquipmentSlot.Torch].ToEquipment(this);
            case ItemType.Helmet:
                return GameScene.Game.EquipmentCells[(int)EquipmentSlot.Helmet].ToEquipment(this);
            case ItemType.Necklace:
                return GameScene.Game.EquipmentCells[(int)EquipmentSlot.Necklace].ToEquipment(this);
            case ItemType.Bracelet:
                return EquipFirstAvailable(this, EquipmentSlot.BraceletL, EquipmentSlot.BraceletR);
            case ItemType.Ring:
                return EquipFirstAvailable(this, EquipmentSlot.RingL, EquipmentSlot.RingR);
            case ItemType.Shoes:
                return GameScene.Game.EquipmentCells[(int)EquipmentSlot.Shoes].ToEquipment(this);
            case ItemType.Poison:
                return GameScene.Game.EquipmentCells[(int)EquipmentSlot.Poison].ToEquipment(this);
            case ItemType.Amulet:
            case ItemType.DarkStone:
                return GameScene.Game.EquipmentCells[(int)EquipmentSlot.Amulet].ToEquipment(this);
            case ItemType.Flower:
                return GameScene.Game.EquipmentCells[(int)EquipmentSlot.Flower].ToEquipment(this);
            case ItemType.Emblem:
                return GameScene.Game.EquipmentCells[(int)EquipmentSlot.Emblem].ToEquipment(this);
            case ItemType.Shield:
                return GameScene.Game.EquipmentCells[(int)EquipmentSlot.Shield].ToEquipment(this);
            case ItemType.Costume:
                return GameScene.Game.EquipmentCells[(int)EquipmentSlot.Costume].ToEquipment(this);
            case ItemType.HorseArmour:
                return GameScene.Game.EquipmentCells[(int)EquipmentSlot.HorseArmour].ToEquipment(this);
            case ItemType.Hook:
                return GameScene.Game.EquipmentCells[(int)EquipmentSlot.Hook].ToEquipment(this);
            case ItemType.Float:
                return GameScene.Game.EquipmentCells[(int)EquipmentSlot.Float].ToEquipment(this);
            case ItemType.Bait:
                return GameScene.Game.EquipmentCells[(int)EquipmentSlot.Bait].ToEquipment(this);
            case ItemType.Finder:
                return GameScene.Game.EquipmentCells[(int)EquipmentSlot.Finder].ToEquipment(this);
            case ItemType.Reel:
                return GameScene.Game.EquipmentCells[(int)EquipmentSlot.Reel].ToEquipment(this);
            case ItemType.CompanionBag:
            case ItemType.CompanionHead:
            case ItemType.CompanionBack:
                // 伙伴装备格由伙伴窗口注入；没有伙伴或窗口时保持旧版的无操作结果。
                if (GameScene.Game.Companion == null || GameScene.Game.CompanionEquipmentCells.Length == 0) return false;
                int companionSlot = Item.Info.ItemType switch
                {
                    ItemType.CompanionBag => (int)CompanionSlot.Bag,
                    ItemType.CompanionHead => (int)CompanionSlot.Head,
                    _ => (int)CompanionSlot.Back
                };
                return GameScene.Game.CompanionEquipmentCells[companionSlot].ToCompanionEquipment(this);
            case ItemType.Consumable:
            case ItemType.Scroll:
            case ItemType.CompanionFood:
            case ItemType.ItemPart:
                // 原版在所有可消耗物品分支都先走 CanUseItem；否则性别、职业、等级、属性和伙伴等级限制会被绕过。
                if (AutoLoginArgs.OperationAuditExt && Item.Info.ItemType == ItemType.CompanionFood)
                    GD.Print($"[OperationAuditExt] S17b branch canUse={GameScene.Game.CanUseItem(Item)} grid={GridType} mounted={GameScene.Game.IsMounted} cooldown={GameScene.Game.IsUseItemOnCooldown(Item)} remain={GameScene.Game.UseItemTime - Godot.Time.GetTicksMsec()} dur={Item.Info.Durability} effect={Item.Info.ItemEffect}");
                if (!GameScene.Game.CanUseItem(Item)) return false;
                if (GridType != GridType.Inventory && GridType != GridType.PartsStorage &&
                    GridType != GridType.CompanionInventory && GridType != GridType.CompanionEquipment) return false;
                if (GameScene.Game.IsMounted && ShapeBlocksWhileMounted(Item.Info)) return false;
                if (GameScene.Game.IsUseItemOnCooldown(Item) &&
                    Item.Info.ItemEffect != ItemEffect.ElixirOfPurification) return false;

                GameScene.Game.SetUseItemCooldown(ComputeUseCooldownMs(Item.Info));
                Locked = true;
                UpdateBorder();
                GameScene.Game.SendItemUse(GridType, Slot);
                return true;
            case ItemType.Book:
                // 原版书籍只能从背包使用，并通过 ItemUse 交给服务端学习技能。
                if (!GameScene.Game.CanUseItem(Item) || GridType != GridType.Inventory || GameScene.Game.IsMounted ||
                    GameScene.Game.IsUseItemOnCooldown(Item)) return false;
                GameScene.Game.SetUseItemCooldown(250);
                Locked = true;
                UpdateBorder();
                GameScene.Game.SendItemUse(GridType, Slot);
                return true;
            case ItemType.System:
                if (GridType != GridType.Inventory) return false;
                switch (Item.Info.ItemEffect)
                {
                    case ItemEffect.GenderChange:
                        if (GameScene.Game.EquipmentCells[(int)EquipmentSlot.Armour].Item != null)
                        {
                            GameScene.Game.ReceiveChat(Lang.CannotChangeGenderWhileWearingArmour, MessageType.System);
                            return false;
                        }
                        GameScene.Game.OpenEditCharacterDialog(EditCharacterChange.Gender);
                        return true;
                    case ItemEffect.HairChange:
                        GameScene.Game.OpenEditCharacterDialog(EditCharacterChange.Hair);
                        return true;
                    case ItemEffect.ArmourDye:
                        if (GameScene.Game.EquipmentCells[(int)EquipmentSlot.Armour].Item == null)
                        {
                            GameScene.Game.ReceiveChat("必须先穿戴护甲才能使用染色。", MessageType.System);
                            return false;
                        }
                        GameScene.Game.OpenEditCharacterDialog(EditCharacterChange.Armour);
                        return true;
                    case ItemEffect.NameChange:
                        GameScene.Game.OpenEditCharacterDialog(EditCharacterChange.Name);
                        return true;
                    case ItemEffect.FortuneChecker:
                        GameScene.Game.OpenFortuneCheckerDialog();
                        return true;
                    case ItemEffect.Caption:
                        GameScene.Game.OpenCaptionDialog();
                        return true;
                    default:
                        return false;
                }
            case ItemType.Bundle:
                if (GridType != GridType.Inventory) return false;
                if (GameScene.Game.IsMounted || GameScene.Game.IsUseItemOnCooldown(Item) || GameScene.Game.BundleBoxVisible) return false;
                GameScene.Game.SetUseItemCooldown(250);
                Locked = true;
                UpdateBorder();
                GameScene.Game.SendBundleOpen(Slot);
                return true;
            case ItemType.LootBox:
                if (GridType != GridType.Inventory) return false;
                if (GameScene.Game.IsMounted || GameScene.Game.IsUseItemOnCooldown(Item) || GameScene.Game.LootBoxVisible) return false;
                GameScene.Game.SetUseItemCooldown(250);
                Locked = true;
                UpdateBorder();
                GameScene.Game.SendLootBoxOpen(Slot);
                return true;
            default:
                return false;
        }
    }

    private SoundIndex GetItemSound()
    {
        return Item?.Info?.ItemType switch
        {
            ItemType.Weapon => SoundIndex.ItemWeapon,
            ItemType.Armour => SoundIndex.ItemArmour,
            ItemType.Helmet => SoundIndex.ItemHelmet,
            ItemType.Necklace => SoundIndex.ItemNecklace,
            ItemType.Bracelet => SoundIndex.ItemBracelet,
            ItemType.Ring => SoundIndex.ItemRing,
            ItemType.Shoes => SoundIndex.ItemShoes,
            ItemType.Consumable => Item.Info.Shape > 0 ? SoundIndex.ItemDefault : SoundIndex.ItemPotion,
            _ => SoundIndex.ItemDefault,
        };
    }

    /// <summary>中键: 锁定切换 (原版 Ctrl+中键为聊天链接, 略)</summary>
    public void ToggleLock()
    {
        // 旧版 DXItemCell.OnKeyDown 前置校验: 已锁定/持有货币/链接源格/观察者/只读 均不处理。
        if (Item == null || GameScene.Game == null || GameScene.Game.CurrencyPickedUp || GameScene.Game.IsObserver) return;
        if (Locked || LinkedSourceSlot >= 0 || ReadOnly) return;
        bool locked = GameScene.ComputeItemLockTarget(Item.Flags.HasFlag(UserItemFlags.Locked));
        GameScene.Game.SendItemLock(GridType, Slot, locked);
    }
}
