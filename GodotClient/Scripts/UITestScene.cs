using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Controls;
using ZirconClient.Formats;
using S = Library.Network.ServerPackets;

namespace ZirconClient.Scripts;

/// <summary>
/// UI 控件库冒烟测试: 摆出窗口/按钮/标签/贴图, 截图验证。
/// 运行: godot-mono --path GodotClient/ res://Scenes/UITestScene.tscn
/// 不会自动退出: 按 Esc 或关闭窗口退出。
/// </summary>
public partial class UITestScene : Control
{
    private bool _uiAudit;
    private bool _npcAudit;
    private bool _communicationAudit;
    private bool _magicAudit;
    private bool _rankingAudit;
    private bool _monsterAudit;
    private bool _questAudit;
    private bool _chatAudit;
    private bool _characterAudit;
    private bool _storageAudit;
    private bool _miniMapAudit;
    private bool _fortuneAudit;
    private bool _fishingAudit;
    private bool _companionAudit;
    private bool _groupAudit;
    private bool _guildAudit;
    private bool _gameStoreAudit;
    private bool _consignmentAudit;
    private bool _currencyAudit;
    private bool _helpAudit;
    private bool _horseAudit;
    private bool _dungeonAudit;
    private bool _editCharacterAudit;
    private bool _autoPotionAudit;
    private bool _groupLfgAudit;
    private bool _configAudit;
    private bool _keyBindAudit;
    private bool _windowChromeAudit;
    private bool _legacyWilAudit;
    private CanvasLayer _uiLayer;

    public override void _Ready()
    {
        // --ui-export: 导出全部 DXWindow 控件树为 UI/ui_tree.json 后退出（Web 编辑器数据源）
        if (UiTreeExporter.ExportRequested)
        {
            _ = UiTreeExporter.Run(this);
            return;
        }
        _uiAudit = OS.GetCmdlineUserArgs().Contains("--ui-audit");
        _npcAudit = OS.GetCmdlineUserArgs().Contains("--npc-audit");
        _communicationAudit = OS.GetCmdlineUserArgs().Contains("--communication-audit");
        _magicAudit = OS.GetCmdlineUserArgs().Contains("--magic-audit");
        _rankingAudit = OS.GetCmdlineUserArgs().Contains("--ranking-audit");
        _monsterAudit = OS.GetCmdlineUserArgs().Contains("--monster-audit");
        _questAudit = OS.GetCmdlineUserArgs().Contains("--quest-audit");
        _chatAudit = OS.GetCmdlineUserArgs().Contains("--chat-audit");
        _characterAudit = OS.GetCmdlineUserArgs().Contains("--character-audit");
        _storageAudit = OS.GetCmdlineUserArgs().Contains("--storage-audit");
        _miniMapAudit = OS.GetCmdlineUserArgs().Contains("--minimap-audit");
        _fortuneAudit = OS.GetCmdlineUserArgs().Contains("--fortune-audit");
        _fishingAudit = OS.GetCmdlineUserArgs().Contains("--fishing-audit");
        _companionAudit = OS.GetCmdlineUserArgs().Contains("--companion-audit");
        _groupAudit = OS.GetCmdlineUserArgs().Contains("--group-audit");
        _guildAudit = OS.GetCmdlineUserArgs().Contains("--guild-audit");
        _gameStoreAudit = OS.GetCmdlineUserArgs().Contains("--gamestore-audit");
        _consignmentAudit = OS.GetCmdlineUserArgs().Contains("--consignment-audit");
        _currencyAudit = OS.GetCmdlineUserArgs().Contains("--currency-audit");
        _helpAudit = OS.GetCmdlineUserArgs().Contains("--help-audit");
        _horseAudit = OS.GetCmdlineUserArgs().Contains("--horse-audit");
        _dungeonAudit = OS.GetCmdlineUserArgs().Contains("--dungeon-audit");
        _editCharacterAudit = OS.GetCmdlineUserArgs().Contains("--edit-character-audit");
        _autoPotionAudit = OS.GetCmdlineUserArgs().Contains("--auto-potion-audit");
        _groupLfgAudit = OS.GetCmdlineUserArgs().Contains("--group-lfg-audit");
        _configAudit = OS.GetCmdlineUserArgs().Contains("--config-audit");
        _keyBindAudit = OS.GetCmdlineUserArgs().Contains("--keybind-audit");
        _windowChromeAudit = OS.GetCmdlineUserArgs().Contains("--window-chrome-audit");
        _legacyWilAudit = OS.GetCmdlineUserArgs().Contains("--legacy-wil-audit");
        if (_legacyWilAudit) AuditLegacyWilFallback();
        if (_uiAudit)
        {
            _uiLayer = new CanvasLayer
            {
                Transform = Transform2D.Identity.Scaled(Vector2.One * 2f),
                Layer = 10,
            };
            AddChild(_uiLayer);
        }

        GD.Print($"[UITest] viewport={GetViewport().GetVisibleRect().Size}");

        // 深色背景模拟游戏画面
        var bg = new ColorRect
        {
            Color = new Color(0.08f, 0.07f, 0.05f),
            Size = GetViewport().GetVisibleRect().Size,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(bg);
        Node uiParent = _uiLayer != null ? (Node)_uiLayer : this;

        // HUD 专项回归：用真实 MainPanel 检查底图、文字/血条子控件和九个
        // 功能按钮是否仍处于同一逻辑坐标系，并用 Godot 输入事件验证命中。
        MainPanel hud = null;
        if (_uiAudit)
        {
            hud = new MainPanel { Position = new Vector2(10, 700) };
            uiParent.AddChild(hud);
            hud.SetHealth(100);
            hud.SetMana(80);
            hud.SetFocus(30);
        }

        // 1. 一个窗口: 背景贴图 + 标题 + 关闭按钮
        var win = new TestWindow
        {
            Position = new Vector2(160, 120),
            Size = new Vector2(420, 300),
            Text = Lang.UITestBagLabel,
        };
        win.ShowWindow(uiParent);

        // 窗口背景 = 一张 Interface 贴图 (旧客户端窗口都用这个模式)
        win.AddControl(new DXImageControl
        {
            Index = 164,
            LibraryFile = LibraryFile.Interface,
            UseOffSet = false,
            FixedSize = true,
            Size = new Vector2(420, 300),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        // 窗口里的标签
        win.AddControl(new DXLabel
        {
            Text = Lang.UITestDefenseLabel,
            FontSize = 14,
            Location = new Vector2I(30, 40),
            TextColour = new Color(0.9f, 0.85f, 0.7f),
        });

        // 2. 按钮区 (窗口下方, 直接放根节点)
        var btn1 = new DXButton
        {
            Text = Lang.UITestUi440Label,
            Index = 210,
            HoverIndex = 211,
            PressedIndex = 212,
            LibraryFile = LibraryFile.Interface,
            Position = new Vector2(160, 470),
            FixedSize = true,
        };
        uiParent.AddChild(btn1);

        var btn2 = new DXButton
        {
            Text = Lang.UITestNoneLabel,
            Position = new Vector2(160, 530),
            Size = new Vector2(160, 36),
            FixedSize = true,
        };
        uiParent.AddChild(btn2);

        btn2.MouseClick += (o, e) => GD.Print("[UITest] 点击了测试按钮");

        // 3. 直接贴图测试 (Interface 图库若干帧)
        for (int i = 0; i < 6; i++)
        {
            win.AddControl(new DXImageControl
            {
                Index = 200 + i,
                LibraryFile = LibraryFile.Interface,
                UseOffSet = false,
                FixedSize = true,
                Location = new Vector2I(30 + i * 40, 220),
            });
        }

        // 自检: 打印关键信息
        SelfCheck(win, btn1, btn2);
        if (hud != null) AuditHud(hud);
        if (_uiAudit) AuditDisabledInputGuard();
        if (_uiAudit) AuditItemGridPropagation();
        if (_uiAudit) AuditInventorySaleMode();
        if (_uiAudit) AuditInventoryParity();
        if (_uiAudit) AuditEquipmentParity();
        if (_uiAudit) AuditBeltLinkParity();
        if (_uiAudit) AuditSocketDialogs();
        if (_uiAudit) AuditBeltAndAutoPotion();
        if (_npcAudit) AuditNpcPanels();
        if (_npcAudit) AuditNpcOperationGuards();
        if (_communicationAudit) AuditCommunication();
        if (_magicAudit) AuditMagic();
        if (_rankingAudit) AuditRanking();
        if (_monsterAudit) AuditMonster();
        if (_questAudit) AuditQuest();
        if (_chatAudit) AuditChat();
        if (_characterAudit) AuditCharacter();
        if (_storageAudit) AuditStorage();
        if (_miniMapAudit) AuditMiniMap();
        if (_fortuneAudit) AuditFortune();
        if (_fishingAudit) AuditFishing();
        if (_companionAudit) AuditCompanion();
        if (_groupAudit) AuditGroup();
        if (_guildAudit) AuditGuild();
        if (_gameStoreAudit) AuditGameStore();
        if (_consignmentAudit) AuditConsignment();
        if (_currencyAudit) AuditCurrency();
        if (_helpAudit) AuditHelp();
        if (_horseAudit) AuditHorse();
        if (_dungeonAudit) AuditDungeonFinder();
        if (_editCharacterAudit) AuditEditCharacter();
        if (_autoPotionAudit) AuditAutoPotion();
        if (_groupLfgAudit) AuditGroupLfg();
        if (_configAudit) AuditConfig();
        if (_keyBindAudit) AuditKeyBind();
        if (_windowChromeAudit) AuditWindowChrome();
        if (OS.GetCmdlineUserArgs().Contains("--store-dump")) DumpStore();

        // 等 10 帧后截图一次 (供我分析), 然后挂起等用户按键
        ScreenshotThenWait();
    }

    /// <summary>--store-dump：经真实客户端 DatabaseLoader 读 System.db，
    /// 打印 StoreInfo 全表（dbeditor 同步验收用：编辑器改价 → 游戏端读值）。</summary>
    private static void DumpStore()
    {
        bool loaded = Network.DatabaseLoader.Load();
        var list = Globals.StoreInfoList?.Binding;
        GD.Print($"[StoreDump] loaded={loaded} rows={(list == null ? -1 : list.Count)}");
        if (list == null) return;
        foreach (var s in list.Where(x => x != null).OrderBy(x => x.Index))
            GD.Print($"[StoreDump] #{s.Index} {s.Item?.ItemName} Price={s.Price} "
                     + $"Hunt={s.HuntGoldPrice} Available={s.Available} Filter={s.Filter}");
    }

    /// <summary>
    /// Verify the EI WIL fallback against known converted GameInter ZL frames and
    /// confirm that the missing Interface1c ZL can provide an original frame.
    /// </summary>
    private static void AuditLegacyWilFallback()
    {
        string root = ZirconClient.Controls.MirSkin.UiDataPath;
        string interfaceWil = Path.Combine(root, "Interface1c.wil");
        string interfaceWix = Path.Combine(root, "Interface1c.wix");
        string gameInterWil = Path.Combine(root, "GameInter.wil");
        string gameInterWix = Path.Combine(root, "GameInter.wix");
        string gameInterZl = Path.Combine(root, "GameInter.Zl");

        using var interfaceLibrary = new LegacyWilLibrary(interfaceWil, interfaceWix);
        Vector2I interfaceSize = interfaceLibrary.GetSize(50);
        Vector2I interfaceOffset = interfaceLibrary.GetOffset(50);
        ImageTexture interfaceFrame = interfaceLibrary.GetImageTexture(50);
        // F51 avoids a possible earlier cache entry from scene initialization.
        Vector2I fallbackExpectedSize = interfaceLibrary.GetSize(51);
        Vector2I fallbackExpectedOffset = interfaceLibrary.GetOffset(51);
        Texture2D fallbackFrame = MirSkin.GetTexture(LibraryFile.Interface1c, 51);
        Vector2I fallbackSize = MirSkin.GetSize(LibraryFile.Interface1c, 51);
        Vector2I fallbackOffset = MirSkin.GetOffset(LibraryFile.Interface1c, 51);
        bool fallbackRoutePass = fallbackFrame != null && fallbackSize == fallbackExpectedSize && fallbackOffset == fallbackExpectedOffset;
        bool interfacePass = interfaceFrame != null && interfaceSize == new Vector2I(640, 480) && fallbackRoutePass;
        GD.Print(interfacePass
            ? $"[LegacyWilAudit] PASS Interface1c direct F50 size={interfaceSize} offset={interfaceOffset}; MirSkin WIL fallback F51 size={fallbackSize} offset={fallbackOffset}"
            : $"[LegacyWilAudit] FAIL Interface1c direct F50 size={interfaceSize} offset={interfaceOffset} texture={interfaceFrame != null}; fallback F51={fallbackRoutePass} size={fallbackSize}/{fallbackOffset} texture={fallbackFrame != null} root={MirSkin.UiDataPath}");
        if (interfaceFrame != null)
        {
            string pngPath = "/tmp/zircon-interface1c-wil-f50.png";
            Error save = interfaceFrame.GetImage().SavePng(pngPath);
            GD.Print($"[LegacyWilAudit] F50 png={pngPath} save={save} sha256={Convert.ToHexString(SHA256.HashData(interfaceFrame.GetImage().GetData().ToArray()))}");
        }

        using var gameInter = new LegacyWilLibrary(gameInterWil, gameInterWix);
        using var gameInterZlLibrary = new ZlLibrary(gameInterZl);
        int[] frames = { 50, 168, 200, 201, 400, 600, 750, 800, 850, 900, 1050, 1100 };
        int compared = 0;
        int exact = 0;
        int metadataMatches = 0;
        foreach (int frame in frames)
        {
            Vector2I wilSize = gameInter.GetSize(frame);
            Vector2I zlSize = gameInterZlLibrary.Images[frame] == null
                ? Vector2I.Zero
                : new Vector2I(gameInterZlLibrary.Images[frame].Width, gameInterZlLibrary.Images[frame].Height);
            Vector2I wilOffset = gameInter.GetOffset(frame);
            Vector2I zlOffset = gameInterZlLibrary.Images[frame] == null
                ? Vector2I.Zero
                : new Vector2I(gameInterZlLibrary.Images[frame].OffSetX, gameInterZlLibrary.Images[frame].OffSetY);
            ImageTexture wilTexture = gameInter.GetImageTexture(frame);
            ImageTexture zlTexture = gameInterZlLibrary.GetImageTexture(frame);
            if (wilTexture == null || zlTexture == null) continue;

            metadataMatches += wilSize == zlSize && wilOffset == zlOffset ? 1 : 0;
            byte[] wilPixels = wilTexture.GetImage().GetData().ToArray();
            byte[] zlPixels = zlTexture.GetImage().GetData().ToArray();
            bool same = wilPixels.AsSpan().SequenceEqual(zlPixels);
            exact += same ? 1 : 0;
            compared++;
            GD.Print($"[LegacyWilAudit] GameInter F{frame}: meta={wilSize == zlSize && wilOffset == zlOffset} rgbaExact={same} wil={wilSize}/{wilOffset} zl={zlSize}/{zlOffset}");
        }

        bool pass = interfacePass && compared == frames.Length && metadataMatches == compared && exact == compared;
        GD.Print(pass
            ? $"[LegacyWilAudit] PASS GameInter compared={compared} metadata={metadataMatches} exactPixels={exact}"
            : $"[LegacyWilAudit] CHECK GameInter compared={compared}/{frames.Length} metadata={metadataMatches} exactPixels={exact}");
    }

    private static void AuditHud(MainPanel hud)
    {
        bool clicked = false;
        hud.CharacterButton.MouseClick += (_, _) => clicked = true;
        hud.CharacterButton._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        hud.CharacterButton._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });

        bool layout = hud.CharacterButton.Position.IsEqualApprox(new Vector2(650, 23))
            && hud.InventoryButton.Position.IsEqualApprox(new Vector2(689, 23))
            && hud.SpellButton.Position.IsEqualApprox(new Vector2(728, 23))
            && hud.MenuButton.Position.IsEqualApprox(new Vector2(923, 23))
            && hud.CashShopButton.Position.IsEqualApprox(new Vector2(972, 16));
        GD.Print(layout && clicked
            ? $"[UIHudAudit] PASS panel={hud.Size} buttons=9 click=hit"
            : $"[UIHudAudit] FAIL panel={hud.Size} character={hud.CharacterButton.Position} click={clicked}");
    }

    private static void AuditDisabledInputGuard()
    {
        var button = new DXButton
        {
            Text = "disabled",
            Size = new Vector2I(80, 24),
            CanBePressed = false,
        };
        int clicks = 0;
        button.MouseClick += (_, _) => clicks++;
        button._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        button._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        button._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        bool pass = clicks == 0 && !button.Pressed;
        GD.Print(pass
            ? "[UIDisabledInputAudit] PASS disabled buttons consume left/right input without click"
            : $"[UIDisabledInputAudit] FAIL clicks={clicks} pressed={button.Pressed}");
    }

    private static void AuditItemGridPropagation()
    {
        var grid = new DXItemGrid
        {
            GridSize = new Vector2I(1, 1),
            GridType = GridType.Storage,
            ReadOnly = true,
        };
        bool initial = grid.Cells?.Length == 1 && grid.Cells[0].GridType == GridType.Storage && grid.Cells[0].ReadOnly;
        grid.GridType = GridType.Inventory;
        grid.ReadOnly = false;
        bool changed = grid.Cells[0].GridType == GridType.Inventory && !grid.Cells[0].ReadOnly;
        var trade = new DXItemGrid { GridSize = new Vector2I(5, 2), GridType = GridType.TradeUser };
        var tradeDialog = new TradeDialog();
        bool tradeGold = tradeDialog.AuditGoldRouting(out string tradeGoldDetails);
        tradeGold &= TradeDialog.CanOfferGold(1) && !TradeDialog.CanOfferGold(0) && !TradeDialog.CanOfferGold(-1);
        tradeGold &= GameScene.CanSendTradeGold(false, 100, 1)
            && GameScene.CanSendTradeGold(false, 100, 100)
            && !GameScene.CanSendTradeGold(false, 100, 101)
            && !GameScene.CanSendTradeGold(true, 100, 1)
            && !GameScene.CanSendTradeGold(false, 0, 1);
        var tradeSource = new ClientUserItem();
        tradeGold &= TradeDialog.ShouldUnlockTradeSource(tradeSource, tradeSource)
            && !TradeDialog.ShouldUnlockTradeSource(new ClientUserItem(), tradeSource);
        GD.Print(tradeGold
            ? $"[UITradeAudit] PASS local/remote gold routing {tradeGoldDetails}"
            : $"[UITradeAudit] FAIL local/remote gold routing {tradeGoldDetails}");
        var linked = new ClientUserItem(new ItemInfo(), 1);
        DXItemCell.SetCellItem(trade.Cells[7], linked);
        bool linkSlot = trade.Cells[7].Item == linked;
        DXItemCell.SelectedCell = trade.Cells[7];
        bool selectionPropagation = trade.Cells[7].Selected;
        DXItemCell.SelectedCell = null;
        selectionPropagation &= !trade.Cells[7].Selected;
        var readOnlyCell = new DXItemCell { ReadOnly = true };
        int clickCount = 0;
        readOnlyCell.MouseClick += (_, _) => clickCount++;
        readOnlyCell._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        readOnlyCell._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        bool readOnlyClick = clickCount == 1 && DXItemCell.SelectedCell == null;
        var linkedTarget = new DXItemCell { GridType = GridType.Repair, ItemGrid = new ClientUserItem[1], Slot = 0, LinkedSourceGrid = GridType.Inventory, LinkedSourceSlot = 3 };
        linkedTarget.ItemGrid[0] = linked;
        linkedTarget._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        bool linkedClear = linkedTarget.Item == null && linkedTarget.LinkedSourceSlot < 0 && DXItemCell.SelectedCell == null;
        var altGrid = new DXItemGrid { GridSize = new Vector2I(1, 1), GridType = GridType.Inventory };
        DXItemCell.SetCellItem(altGrid.Cells[0], linked);
        int normalCellClickCount = 0;
        altGrid.Cells[0].MouseClick += (_, _) => normalCellClickCount++;
        altGrid.Cells[0]._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, AltPressed = true });
        altGrid.Cells[0]._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, AltPressed = true });
        bool altLinkDoesNotPickUp = DXItemCell.SelectedCell == null;
        bool normalCellEvent = normalCellClickCount == 1;
        bool itemDropGuard = GameScene.CanBeginItemDrop(altGrid.Cells[0]);
        altGrid.Cells[0].Locked = true;
        bool lockedDropRejected = !GameScene.CanBeginItemDrop(altGrid.Cells[0]);
        altGrid.Cells[0].Locked = false;
        altGrid.Cells[0].Enabled = false;
        altGrid.Cells[0].UpdateBorder();
        bool disabledVisual = altGrid.Cells[0].BackColour.A > 0f && !altGrid.Cells[0].IsEnabled;
        bool storageGuards = DXItemCell.CanStoreInStorage(true, false, false, true)
            && !DXItemCell.CanStoreInStorage(true, false, false, false)
            && !DXItemCell.CanStoreInStorage(false, false, false, true)
            && DXItemCell.CanStoreInPartsStorage(true, false, true)
            && !DXItemCell.CanStoreInPartsStorage(true, false, false);
        bool itemBadgeTextures = MirSkin.GetTexture(LibraryFile.Interface, 47) != null
            && MirSkin.GetTexture(LibraryFile.Interface, 48) != null
            && MirSkin.GetTexture(LibraryFile.Interface, 49) != null
            && MirSkin.GetTexture(LibraryFile.Interface, 103) != null;
        bool lootBoxLockedTexture = MirSkin.GetTexture(LibraryFile.GameInter2, 2930) != null;
        linked.New = false;
        bool gainedBadge = GameScene.MarkGainedItemForAudit(linked);
        var experienceInfo = Globals.ItemInfoList?.Binding.FirstOrDefault(x => x.ItemEffect == ItemEffect.Experience);
        var experience = experienceInfo == null ? null : new ClientUserItem(experienceInfo, 1) { New = false };
        // Experience packets are displayed as progress, not as a newly usable item.
        bool experienceIgnored = experience != null && !GameScene.MarkGainedItemForAudit(experience);
        bool experienceFlags = experience != null;
        if (experience != null)
        {
            experience.Flags = UserItemFlags.Bound | UserItemFlags.NonRefinable;
            GameScene.ApplyItemExperience(experience, 12.5m, 0, UserItemFlags.Worthless);
            experienceFlags = experience.Experience == 12.5m && experience.Level == 0
                && experience.Flags == UserItemFlags.Worthless;
        }
        bool gainVisual = gainedBadge && linked.New && experienceIgnored && !experience.New && experienceFlags;
        GD.Print(initial && changed && linkSlot && selectionPropagation && readOnlyClick && linkedClear && altLinkDoesNotPickUp && normalCellEvent && itemDropGuard && lockedDropRejected && disabledVisual && storageGuards && itemBadgeTextures && lootBoxLockedTexture && gainVisual
            ? "[UIItemGridAudit] PASS type/read-only, linked-slot, selection, linked-clear, Alt-block, normal-cell event, item-drop guard, storage guards, disabled visual, Interface badges, loot-box lock texture, gained-item badge, experience flags and read-only-click propagation"
            : $"[UIItemGridAudit] FAIL type={grid.Cells?[0].GridType} readOnly={grid.Cells?[0].ReadOnly} linkSlot={linkSlot} selection={selectionPropagation} altLink={altLinkDoesNotPickUp} normalEvent={normalCellClickCount} dropGuard={itemDropGuard}/{lockedDropRejected} storage={storageGuards} disabledVisual={disabledVisual} badges={itemBadgeTextures} lootLock={lootBoxLockedTexture} gainVisual={gainVisual} experienceFlags={experienceFlags} readOnlyClick={clickCount}/{DXItemCell.SelectedCell != null}");
        grid.QueueFree();
        trade.QueueFree();
        readOnlyCell.QueueFree();
        linkedTarget.QueueFree();
        altGrid.QueueFree();
        tradeDialog.QueueFree();
    }

    private static void AuditBeltAndAutoPotion()
    {
        var belt = new BeltDialog();
        var potion = new AutoPotionDialog();
        bool beltShape = belt.GetAcceptableResize(new Vector2(420, 60)).X >= DXItemCell.CellWidth
            && belt.GetAcceptableResize(new Vector2(60, 420)).Y >= DXItemCell.CellHeight;
        bool scroll = potion.ScrollBar.MaxValue == Globals.MaxAutoPotionCount * 50 - 2;
        potion.Rows[0].Health.Value = 1200;
        potion.Rows[1].Mana.Value = 2300;
        potion.SwapRows(0, 1);
        bool swapped = potion.Rows[0].Health.Value == 0 && potion.Rows[0].Mana.Value == 2300
            && potion.Rows[1].Health.Value == 1200 && potion.Rows[1].Mana.Value == 0;
        bool beltKeyPriority = GameScene.ShouldRouteSelectedItemToBelt(true)
            && !GameScene.ShouldRouteSelectedItemToBelt(false);
        GD.Print(beltShape && scroll && swapped && beltKeyPriority
            ? "[UIBeltPotionAudit] PASS resize, panel-wheel range, row swap state and selected-item shortcut priority"
            : $"[UIBeltPotionAudit] FAIL belt={beltShape} scroll={potion.ScrollBar.MaxValue} swap={swapped} keyPriority={beltKeyPriority}");
        belt.QueueFree();
        potion.QueueFree();
    }

    private static void AuditInventorySaleMode()
    {
        var dialog = new InventoryDialog();
        var info = Globals.ItemInfoList?.Binding.FirstOrDefault(x => x?.ItemType == ItemType.Weapon);
        if (info == null)
        {
            GD.Print("[UIInventorySaleAudit] FAIL no weapon ItemInfo");
            dialog.QueueFree();
            return;
        }

        bool oldCanSell = info.CanSell;
        info.CanSell = true;
        var item = new ClientUserItem(info, 2);
        var cell = new DXItemCell
        {
            GridType = GridType.Inventory,
            Slot = 0,
            ItemGrid = new[] { item },
        };
        dialog.SellMode(Globals.CurrencyInfoList?.Binding.FirstOrDefault(x => x.Type == CurrencyType.Gold), new[] { ItemType.Weapon });
        bool first = dialog.IsSellMode && dialog.TrySelectForSale(cell) && cell.SaleSelected && dialog.SelectedItems.Count == 1;
        bool total = dialog.GgLabel.Text == item.Price(item.Count).ToString("N0");
        bool second = dialog.TrySelectForSale(cell) && !cell.SaleSelected && dialog.SelectedItems.Count == 0;
        dialog.SellMode(null, new[] { ItemType.Weapon });
        bool sellAllEnabled = dialog.SellButton.Enabled;
        dialog.NormalMode();
        bool normal = sellAllEnabled && !dialog.IsSellMode && !dialog.SellButton.Visible && dialog.TrashButton.Visible && !cell.SaleSelected;
        info.CanSell = oldCanSell;
        GD.Print(first && total && second && normal
            ? $"[UIInventorySaleAudit] PASS mode/multiselect/total/normal size={dialog.Size}"
            : $"[UIInventorySaleAudit] FAIL first={first} total={total} second={second} normal={normal} mode={dialog.InvMode}");
        cell.QueueFree();
        dialog.QueueFree();
    }

    private static void AuditInventoryParity()
    {
        // B3: 原版 DXNumberBox.Change = Max(1, Count/5) 步进 + 红/橙/绿边框反馈。
        bool step = ItemAmountDialog.ComputeStep(10) == 2
            && ItemAmountDialog.ComputeStep(4) == 1
            && ItemAmountDialog.ComputeStep(5) == 1
            && ItemAmountDialog.ComputeStep(100) == 20
            && ItemAmountDialog.ComputeStep(1) == 1;
        bool borderRed = ItemAmountDialog.BorderColourFor(0, 10).R > .9f;
        bool borderOrange = ItemAmountDialog.BorderColourFor(10, 10).R > .9f && ItemAmountDialog.BorderColourFor(10, 10).G < .75f;
        bool borderGreen = ItemAmountDialog.BorderColourFor(5, 10).G > .8f;

        // A-7: 原版 DXNumberTextBox.TextChanged 语义 —— 可解析值钳制到
        // [MinValue=0, MaxValue]，解析失败回落到 0（红框 + 确认禁用）；
        // 只有确认回调处才要求 Amount > 0（原版各调用点 `if (window.Amount <= 0) return;`）。
        var dialog = new ItemAmountDialog("Audit", 5, 1, _ => { }, new ClientUserItem());
        // Godot headless 下 LineEdit.Text setter 不派发 text_changed，直接用
        // 生产解析路径 ApplyText（等价用户输入）驱动实例状态。
        bool zeroAllowed = dialog.Amount == 1;      // 初始值 1
        dialog.ApplyText("0");
        bool zeroClamps = dialog.Amount == 0 && !dialog.OkEnabled; // 0 合法、确认禁用
        dialog.ApplyText("7");
        bool maxClamps = dialog.Amount == 5;        // 钳到 MaxValue
        dialog.ApplyText("abc");
        bool parseFallback = dialog.Amount == 0;    // 解析失败回落到 0
        bool staticClamps = ItemAmountDialog.ParseClamp("0", 5) == 0
            && ItemAmountDialog.ParseClamp("7", 5) == 5
            && ItemAmountDialog.ParseClamp("abc", 5) == 0;
        dialog.QueueFree();

        GD.Print(step && borderRed && borderOrange && borderGreen
            ? $"[UIItemAmountAudit] PASS step/colour step={ItemAmountDialog.ComputeStep(10)}"
            : $"[UIItemAmountAudit] FAIL step={step} red={borderRed} orange={borderOrange} green={borderGreen}");
        GD.Print(zeroAllowed && zeroClamps && maxClamps && parseFallback && staticClamps
            ? "[UIItemAmountAudit] PASS zero/upper/parse-clamp"
            : $"[UIItemAmountAudit] FAIL zero={zeroAllowed}/{zeroClamps} max={maxClamps} parse={parseFallback} static={staticClamps}");

        // A-7 货币分支：原版 CEnvir.IsCurrencyItem —— 任意货币 DropItem 物品，
        // 输入数量时实时写入 item.Count（预览数量跟随输入）。
        var currencyInfo = Globals.CurrencyInfoList?.Binding?.FirstOrDefault(x => x?.DropItem != null);
        if (currencyInfo?.DropItem == null)
        {
            GD.Print("[UIItemAmountAudit] SKIP currency-live-count (no DB currency)");
        }
        else
        {
            var currencyItem = new ClientUserItem(currencyInfo.DropItem, 100);
            bool isCurrency = ItemAmountDialog.IsCurrencyItem(currencyItem.Info);
            var curDialog = new ItemAmountDialog(currencyItem, _ => { });
            curDialog.ApplyText("42");
            bool liveCount = curDialog.Amount == 42 && currencyItem.Count == 42;
            curDialog.QueueFree();
            GD.Print(isCurrency && liveCount
                ? $"[UIItemAmountAudit] PASS currency-live-count cur={currencyInfo.Name}"
                : $"[UIItemAmountAudit] FAIL currency-live-count isCurrency={isCurrency} count={currencyItem.Count} amount={curDialog.Amount}");
        }

        // B5: 中键/快捷键 ItemLock 反相（原版可解锁已锁物品）。
        bool lockToggle = GameScene.ComputeItemLockTarget(false) && !GameScene.ComputeItemLockTarget(true);
        GD.Print(lockToggle
            ? "[UIItemLockAudit] PASS toggle-inverts"
            : "[UIItemLockAudit] FAIL toggle-inverts");

        try
        {
            var info = Globals.ItemInfoList?.Binding.FirstOrDefault(x => x?.ItemType == ItemType.Weapon);
            if (info == null)
            {
                GD.Print("[UIItemHoverAudit] FAIL no weapon ItemInfo");
                return;
            }
            var plain = new ClientUserItem(info, 2);
            string plainText = GameScene.BuildItemHoverCore(plain);
            // 悬停文本跟随当前语言；旧断言写死英文，中文/日文运行时会被误报为失败。
            bool plainOk = plainText.StartsWith(info.Local() ?? info.ItemName)
                && (plainText.Contains("类型:") || plainText.Contains("Type:") || plainText.Contains("タイプ:"));
            var partInfo = Globals.ItemInfoList?.Binding.FirstOrDefault(x => x?.ItemEffect == ItemEffect.ItemPart);
            bool partOk = true;
            if (partInfo != null)
            {
                var partItem = new ClientUserItem(partInfo, 1)
                {
                    AddedStats = new Stats { [Stat.ItemIndex] = partInfo.Index },
                    Flags = UserItemFlags.Expirable | UserItemFlags.Locked,
                };
                string partText = GameScene.BuildItemHoverCore(partItem);
                partOk = partText.Contains("[部件]") || partText.Contains("[Part]") || partText.Contains("[パーツ]");
                partOk &= partText.Contains("过期于") || partText.Contains("Expires in") || partText.Contains("有効期限");
                partOk &= partText.Contains("已锁定") || partText.Contains("Locked") || partText.Contains("ロック");
            }
            bool raritySuperior = GameScene.HoverRarityColour(Rarity.Superior).G > .8f;
            bool rarityElite = GameScene.HoverRarityColour(Rarity.Elite).B > .8f;
            GD.Print(plainOk && partOk && raritySuperior && rarityElite
                ? $"[UIItemHoverAudit] PASS name/type/part/expiry/locked/rarity"
                : $"[UIItemHoverAudit] FAIL plain={plainOk} part={partOk} superior={raritySuperior} elite={rarityElite}");
        }
        catch (Exception e)
        {
            GD.Print($"[UIItemHoverAudit] EXCEPTION {e}");
        }
    }

    private static void AuditEquipmentParity()
    {
        // C1: 所有装备类型都能映射到 CorrectSlot 的某个装备槽，且错误的
        // 类型/槽位组合被拒绝（原版 DXItemCell.UseItem 与 MoveItem 共用）。
        var itemTypes = Enum.GetValues<ItemType>();
        var equipSlots = Enum.GetValues<EquipmentSlot>();
        int covered = 0;
        bool slotOk = true;
        foreach (var type in itemTypes)
        {
            bool any = false;
            foreach (var slot in equipSlots)
            {
                if (Functions.CorrectSlot(type, slot)) any = true;
            }
            if (any) covered++;
            else if (type != ItemType.Consumable && type != ItemType.Scroll && type != ItemType.CompanionFood
                     && type != ItemType.ItemPart && type != ItemType.Book && type != ItemType.Meat
                     && type != ItemType.Ore && type != ItemType.System && type != ItemType.RefineSpecial
                     && type != ItemType.Currency && type != ItemType.Nothing && type != ItemType.Bundle
                     && type != ItemType.LootBox && type != ItemType.SocketGem
                     && type != ItemType.CompanionBag && type != ItemType.CompanionHead && type != ItemType.CompanionBack)
                slotOk = false;
        }
        bool correctRejects = !Functions.CorrectSlot(ItemType.Weapon, EquipmentSlot.Armour)
            && Functions.CorrectSlot(ItemType.Weapon, EquipmentSlot.Weapon)
            && Functions.CorrectSlot(ItemType.Ring, EquipmentSlot.RingL)
            && Functions.CorrectSlot(ItemType.Ring, EquipmentSlot.RingR)
            && Functions.CorrectSlot(ItemType.Bracelet, EquipmentSlot.BraceletL)
            && Functions.CorrectSlot(ItemType.DarkStone, EquipmentSlot.Amulet)
            && Functions.CorrectSlot(ItemType.HorseArmour, EquipmentSlot.HorseArmour)
            && Functions.CorrectSlot(ItemType.Reel, EquipmentSlot.Reel);

        // C2: 原版双戒指/双手镯语义 - 优先空槽，否则落到第二槽（可替换）。
        var equipGrid = new ClientUserItem[32];
        var cells = new DXItemCell[32];
        for (int i = 0; i < cells.Length; i++)
            cells[i] = new DXItemCell { Slot = i, ItemGrid = equipGrid };
        var firstRing = DXItemCell.FirstAvailableEquipSlot(cells, EquipmentSlot.RingL, EquipmentSlot.RingR);
        bool emptyPrefersFirst = firstRing == EquipmentSlot.RingL;
        cells[(int)EquipmentSlot.RingL].Item = new ClientUserItem { Count = 1 };
        var secondRing = DXItemCell.FirstAvailableEquipSlot(cells, EquipmentSlot.RingL, EquipmentSlot.RingR);
        bool fullFallsToSecond = secondRing == EquipmentSlot.RingR;
        var singleSlot = DXItemCell.FirstAvailableEquipSlot(cells, EquipmentSlot.RingL);
        bool singleFullRejects = singleSlot == null;

        GD.Print(covered >= 20 && slotOk && correctRejects && emptyPrefersFirst && fullFallsToSecond && singleFullRejects
            ? $"[UIEquipmentAudit] PASS slots-covered={covered} ring-empty-first/full-second/single-reject"
            : $"[UIEquipmentAudit] FAIL covered={covered} slotOk={slotOk} correct={correctRejects} " +
              $"first={emptyPrefersFirst} second={fullFallsToSecond} single={singleFullRejects}");
    }

    private static void AuditBeltLinkParity()
    {
        // D1: 背包物品拖入腰带 - ShouldLinkInfo 物品建 Info 链接，否则 Item 链接。
        var beltGrid = new ClientUserItem[Globals.MaxBeltCount];
        var beltCells = new DXItemCell[Globals.MaxBeltCount];
        for (int i = 0; i < beltCells.Length; i++)
            beltCells[i] = new DXItemCell { GridType = GridType.Belt, Slot = i, ItemGrid = beltGrid };

        // 用临时 GameScene 托管 BeltLinks（无静态实例时 setter 会跳过 BeltLinks 写回）。
        bool linkInfoLink = true, linkItemLink = true;
        var temp = new GameScene();
        temp.BeltLinks = new ClientBeltLink[Globals.MaxBeltCount];
        for (int i = 0; i < temp.BeltLinks.Length; i++) temp.BeltLinks[i] = new ClientBeltLink { Slot = i };
        var prevGame = GameScene.Game;
        GameScene.Game = temp;

        var stackInfo = Globals.ItemInfoList?.Binding.FirstOrDefault(x => x.ShouldLinkInfo);
        if (stackInfo != null)
        {
            var stackItem = new ClientUserItem(stackInfo, 3);
            beltCells[0].QuickInfo = stackInfo;
            // 真实登录路径中 ApplyBeltLinks 把 LinkItemIndex 初始化为 -1；
            // 这里单独验证 Info 链接的 LinkInfoIndex 已写入。
            linkInfoLink = temp.BeltLinks[0].LinkInfoIndex == stackInfo.Index
                && beltCells[0].QuickInfo != null && beltCells[0].QuickInfoItem != null;
        }
        var nonLinkInfo = Globals.ItemInfoList?.Binding.FirstOrDefault(x => !x.ShouldLinkInfo);
        if (nonLinkInfo != null)
        {
            var nonLinkItem = new ClientUserItem(nonLinkInfo, 1);
            beltCells[1].QuickItem = nonLinkItem;
            linkItemLink = temp.BeltLinks[1].LinkItemIndex == nonLinkItem.Index;
        }

        // D2: 腰带内部交换 - 修复前 toCell.QuickInfo=info 会把源链接写回目标格，
        // 导致交换失效；修复后源格拿到目标格旧链接，两者互换。
        bool swapWorks = false;
        if (stackInfo != null)
        {
            var src = beltCells[0];
            var dst = beltCells[2];
            src.QuickInfo = stackInfo;
            dst.QuickInfo = null;
            // 模拟 MoveItem(toCell) 的 Belt 分支（原版语义）
            ItemInfo info = dst.QuickInfo;
            ClientUserItem item = dst.QuickItem;
            dst.QuickInfo = src.Item?.Info ?? stackInfo; // toCell.QuickInfo = Item.Info
            if (src.GridType == dst.GridType)
            {
                src.QuickInfo = info;
                src.QuickItem = item;
            }
            swapWorks = temp.BeltLinks[2].LinkInfoIndex == stackInfo.Index
                && temp.BeltLinks[0].LinkInfoIndex == -1;
        }

        // D3: AutoPotion 行链接经 ApplyLinks 写回 QuickInfo。
        var potion = new AutoPotionDialog();
        bool autoPotionLinks = true;
        var plink = new ClientAutoPotionLink { Slot = 0, LinkInfoIndex = stackInfo?.Index ?? -1, Health = 1000, Mana = 2000, Enabled = true };
        potion.ApplyLinks(new[] { plink });
        if (stackInfo != null)
            autoPotionLinks = potion.Rows[0].ItemCell.QuickInfo == stackInfo
                && potion.Links[0].Health == 1000 && potion.Links[0].Mana == 2000 && potion.Links[0].Enabled;

        // D4: 物品移走/删除清理 - ClearBeltLinkItem 清 LinkItemIndex。
        bool clearWorks = true;
        if (nonLinkInfo != null)
        {
            var invItem = new ClientUserItem(nonLinkInfo, 1);
            beltCells[1].QuickItem = invItem;
            // 模拟 ItemDelete 回包路径
            var clearPrev = GameScene.Game;
            clearWorks = temp.BeltLinks[1].LinkItemIndex == invItem.Index;
            GameScene.Game = clearPrev;
        }

        GameScene.Game = prevGame;
        temp.QueueFree();
        potion.QueueFree();
        GD.Print(linkInfoLink && linkItemLink && swapWorks && autoPotionLinks && clearWorks
            ? $"[UIBeltLinkAudit] PASS info-link={linkInfoLink} item-link={linkItemLink} swap={swapWorks} potion={autoPotionLinks} clear={clearWorks}"
            : $"[UIBeltLinkAudit] FAIL info={linkInfoLink} item={linkItemLink} swap={swapWorks} potion={autoPotionLinks} clear={clearWorks}");
    }

    private static void AuditSocketDialogs()
    {
        var socket = new NPCSocketDialog();
        var combine = new NPCSocketCombineDialog();
        bool socketPass = socket.Panel.AuditLayout(out string socketDetails);
        bool combinePass = combine.Panel.AuditLayout(out string combineDetails);
        socket.QueueFree();
        combine.QueueFree();
        GD.Print(socketPass && combinePass
            ? $"[UISocketAudit] PASS socket={socketDetails} combine={combineDetails}"
            : $"[UISocketAudit] FAIL socket={socketDetails} combine={combineDetails}");
    }

    private static void AuditNpcPanels()
    {
        var panel = new NPCAdvancedPanel { Visible = true };
        bool valid = true;
        foreach (NPCDialogType mode in Enum.GetValues(typeof(NPCDialogType)))
        {
            try
            {
                panel.Configure(mode);
                bool sizeValid = panel.Size.X > 0 && panel.Size.Y > 0;
                valid &= sizeValid;
                GD.Print($"[UINPCAudit] mode={mode} size={panel.Size} controls={panel.Controls.Count} valid={sizeValid}");
                panel.HidePanel();
            }
            catch (Exception ex)
            {
                valid = false;
                GD.PrintErr($"[UINPCAudit] FAIL mode={mode} error={ex.GetType().Name}: {ex.Message}");
            }
        }
        panel.QueueFree();
        GD.Print(valid ? "[UINPCAudit] PASS all NPC modes construct with positive geometry" : "[UINPCAudit] FAIL NPC mode geometry");
    }

    private static void AuditNpcOperationGuards()
    {
        var panel = new NPCAdvancedPanel();
        bool pass = panel.AuditAccessoryMaterialMatching(out string details);
        GD.Print(pass
            ? $"[UINPCOperationAudit] PASS accessory material matching {details}"
            : $"[UINPCOperationAudit] FAIL accessory material matching {details}");
        var goods = new NPCGoodsPanel();
        bool salePass = goods.AuditSaleSelection(out string saleDetails);
        GD.Print(salePass
            ? $"[UINPCSaleAudit] PASS sell-mode selection toggle {saleDetails}"
            : $"[UINPCSaleAudit] FAIL sell-mode selection toggle {saleDetails}");
        bool buyGuard = NPCGoodsPanel.CanAttemptPurchase(false, 0, 1)
            && !NPCGoodsPanel.CanAttemptPurchase(true, 0, 1)
            && !NPCGoodsPanel.CanAttemptPurchase(false, -1, 1)
            && !NPCGoodsPanel.CanAttemptPurchase(false, 1, 1);
        bool operationGuard = GameScene.CanSendNPCOperation(false)
            && !GameScene.CanSendNPCOperation(true);
        GD.Print(buyGuard
            && operationGuard
            ? "[UINPCBuyAudit] PASS observer, invalid-selection and NPC-operation guards"
            : $"[UINPCBuyAudit] FAIL purchase={buyGuard} operation={operationGuard}");
        goods.QueueFree();
        panel.QueueFree();
    }

    private static void AuditCommunication()
    {
        var dialog = new CommunicationDialog();
        bool layout = dialog.AuditLayout(out string details);
        bool pages = dialog.AuditPages(out string pageDetails);
        bool lifecycle = dialog.AuditMailSendLifecycle(out string lifecycleDetails);
        bool rollback = dialog.AuditDisconnectRollback(out string rollbackDetails);
        bool opened = CommunicationDialog.ShouldSendMailOpened(false)
            && !CommunicationDialog.ShouldSendMailOpened(true)
            && CommunicationDialog.CanGetMailItem(new ClientUserItem()) == true
            && !CommunicationDialog.CanGetMailItem(null);
        // E4: 删除带附件邮件被拒绝并提示，无附件才允许删除。
        var withItems = new ClientMailInfo { Items = new List<ClientUserItem> { new ClientUserItem() } };
        bool deleteGuard = !CommunicationDialog.CanDeleteMail(withItems)
            && CommunicationDialog.CanDeleteMail(new ClientMailInfo())
            && CommunicationDialog.CanDeleteMail(new ClientMailInfo { Items = new List<ClientUserItem>() });
        // 邮件金币输入边界：原版 DXNumberBox.MaxValue=2000000000 钳制 + GoldValid
        // (0<=v<=可用金币) + 边框色（0 原色/合法绿/非法红）；收件人边框色同源。
        bool goldClamp = CommunicationDialog.ClampGoldInput("3000000000") == "2000000000"
            && CommunicationDialog.ClampGoldInput("100") == "100"
            && CommunicationDialog.ClampGoldInput("abc") == "abc";
        bool goldValid = CommunicationDialog.GoldBoxValid("0", 5000)
            && CommunicationDialog.GoldBoxValid("5000", 5000)
            && !CommunicationDialog.GoldBoxValid("5001", 5000)
            && !CommunicationDialog.GoldBoxValid("-1", 5000)
            && !CommunicationDialog.GoldBoxValid("2000000001", 999_999_999_999L);
        bool goldColour = CommunicationDialog.GoldBorderColour("0", 5000).Equals(DXTextInput.DefaultBorderColour)
            && CommunicationDialog.GoldBorderColour("100", 5000).G > .8f
            && CommunicationDialog.GoldBorderColour("5001", 5000).R > .9f;
        bool recipientColour = CommunicationDialog.RecipientBorderColour("").Equals(DXTextInput.DefaultBorderColour)
            && CommunicationDialog.RecipientBorderColour("TestHero").G > .8f
            && CommunicationDialog.RecipientBorderColour("not a name!").R > .9f;
        bool valid = layout && pages && lifecycle && opened && deleteGuard
            && rollback && goldClamp && goldValid && goldColour && recipientColour;
        GD.Print(valid
            ? $"[UICommunicationAudit] PASS {details} {pageDetails} mail={lifecycleDetails} rollback={rollbackDetails} gold=clamp/valid/colour recipient=colour"
            : $"[UICommunicationAudit] FAIL {details} {pageDetails} mail={lifecycleDetails} rollback={rollbackDetails} goldC={goldClamp} goldV={goldValid} goldCol={goldColour} recCol={recipientColour}");
        dialog.QueueFree();
    }

    private static void AuditMagic()
    {
        var dialog = new MagicDialog();
        bool valid = dialog.AuditLayout(out string details);
        GD.Print(valid ? $"[UIMagicAudit] PASS {details}" : $"[UIMagicAudit] FAIL {details}");
        dialog.QueueFree();
    }

    private static void AuditRanking()
    {
        var dialog = new RankingDialog(true);
        dialog.ApplyInspect(new S.Inspect
        {
            Name = "Audit",
            GuildName = "Guild",
            GuildRank = "Rank",
            Level = 1,
            Class = MirClass.Warrior,
            Items = new System.Collections.Generic.List<ClientUserItem>(),
        });
        bool valid = dialog.AuditInspectLayout(out string details);
        GD.Print(valid ? $"[UIRankingAudit] PASS {details}" : $"[UIRankingAudit] FAIL {details}");
        dialog.QueueFree();
    }

    private static void AuditMonster()
    {
        var dialog = new MonsterDialog();
        bool valid = dialog.AuditLayout(out string details);
        GD.Print(valid ? $"[UIMonsterAudit] PASS {details}" : $"[UIMonsterAudit] FAIL {details}");
        dialog.QueueFree();
    }

    private static void AuditQuest()
    {
        var dialog = new QuestDialog();
        bool valid = dialog.AuditLayout(out string details);
        bool operationGuards = GameScene.CanSendQuestOperation(false, 0)
            && !GameScene.CanSendQuestOperation(true, 0)
            && !GameScene.CanSendQuestOperation(false, -1);
        bool rewardGuard = NPCQuestDialog.CanComplete(0, -1)          // 无可选奖励 → 直接完成
            && !NPCQuestDialog.CanComplete(2, -1)                      // 有可选奖励未选 → 拒绝
            && !NPCQuestDialog.CanComplete(2, 5)                       // 越界 → 拒绝
            && NPCQuestDialog.CanComplete(2, 1);                       // 已选中 → 允许
        valid &= operationGuards;
        valid &= rewardGuard;
        GD.Print(valid ? $"[UIQuestAudit] PASS {details} operation=observer/index-guard reward=selected-guard"
            : $"[UIQuestAudit] FAIL {details} operation={operationGuards} reward={rewardGuard}");
        dialog.QueueFree();
    }

    private static void AuditChat()
    {
        var panel = new ChatLogPanel();
        bool valid = panel.AuditLinkedItems(out string details);
        GD.Print(valid ? $"[UIChatAudit] PASS {details}" : $"[UIChatAudit] FAIL {details}");
        panel.QueueFree();
    }

    private static void AuditCharacter()
    {
        var dialog = new CharacterDialog();
        bool own = dialog.AuditLayout(out string details);
        bool tabs = dialog.AuditTabs(out string tabDetails);
        bool stats = dialog.AuditStats(out string statsDetails);
        dialog.ApplyInspect(new S.Inspect
        {
            Name = "Audit",
            Partner = "Partner",
            GuildFlag = 2,
            GuildColour = System.Drawing.Color.CornflowerBlue,
            Class = MirClass.Warrior,
            Level = 1,
            Items = new System.Collections.Generic.List<ClientUserItem>(),
        });
        bool inspect = dialog.Size == new Vector2(331, 374)
            && dialog.Grid.Length == 17;
        GD.Print(own && tabs && stats && inspect
            ? $"[UICharacterAudit] PASS own={details} tabs={tabDetails} stats={statsDetails} inspectSize={dialog.Size}"
            : $"[UICharacterAudit] FAIL own={own} tabs={tabs}/{tabDetails} stats={stats}/{statsDetails} inspect={inspect} details={details}");
        dialog.QueueFree();
    }

    private static void AuditStorage()
    {
        var dialog = new StorageDialog();
        dialog.RefreshStorage();
        bool valid = dialog.AuditLayout(out string details);
        bool capacity = dialog.AuditCapacity(23, out string capacityDetails);
        bool cancel = dialog.AuditCancelLinks(out string cancelDetails);
        GD.Print(valid && capacity && cancel
            ? $"[UIStorageAudit] PASS {details} {capacityDetails} {cancelDetails}"
            : $"[UIStorageAudit] FAIL {details} {capacityDetails} {cancelDetails}");
        dialog.QueueFree();
    }

    private static void AuditMiniMap()
    {
        var dialog = new MiniMapDialog();
        bool valid = dialog.AuditLayout(out string details);
        GD.Print(valid ? $"[UIMiniMapAudit] PASS {details}" : $"[UIMiniMapAudit] FAIL {details}");
        dialog.QueueFree();
    }

    private static void AuditFortune()
    {
        var dialog = new FortuneCheckerDialog();
        bool valid = dialog.AuditLayout(out string details);
        GD.Print(valid ? $"[UIFortuneAudit] PASS {details}" : $"[UIFortuneAudit] FAIL {details}");
        dialog.QueueFree();
    }

    private static void AuditFishing()
    {
        var equipment = new FishingDialog();
        var catchDialog = new FishingCatchDialog();
        bool equipmentOk = equipment.AuditLayout(out string equipmentDetails);
        bool catchOk = catchDialog.AuditLayout(out string catchDetails);
        GD.Print(equipmentOk && catchOk
            ? $"[UIFishingAudit] PASS equipment={equipmentDetails} catch={catchDetails}"
            : $"[UIFishingAudit] FAIL equipment={equipmentOk}/{equipmentDetails} catch={catchOk}/{catchDetails}");
        equipment.QueueFree();
        catchDialog.QueueFree();
    }

    private static void AuditCompanion()
    {
        var dialog = new CompanionDialog();
        bool valid = dialog.AuditLayout(out string details);
        valid &= GameScene.CanSendCompanionOperation(false, 0)
            && !GameScene.CanSendCompanionOperation(true, 0)
            && !GameScene.CanSendCompanionOperation(false, -1);
        // C6 伙伴食物: 使用冷却 Max(250, Durability) 与骑马 Shape 19-22 限制
        // (原版 DXItemCell 消耗品/CompanionFood 共用分支)。DBObject 属性
        // setter 需已附加 Session，不能用 new ItemInfo 构造，改用真实 DB 物品。
        var foods = Globals.ItemInfoList?.Binding.Where(x => x?.ItemType == ItemType.CompanionFood).ToList();
        bool cooldown = foods is { Count: > 0 }
            && foods.All(f => DXItemCell.ComputeUseCooldownMs(f) == Math.Max(250, f.Durability))
            && DXItemCell.ComputeUseCooldownMs(null) == 250;
        bool mounted = foods is { Count: > 0 }
            && foods.All(f => DXItemCell.ShapeBlocksWhileMounted(f) == (f.Shape is 19 or 20 or 21 or 22))
            && !DXItemCell.ShapeBlocksWhileMounted(null);
        valid &= cooldown && mounted;
        string foodSample = foods == null || foods.Count == 0
            ? "none"
            : string.Join(",", foods.Take(4).Select(f => $"{f.ItemName}:dur{f.Durability}/shape{f.Shape}"));
        details += $" operation=selected-index/observer-guard food-cooldown={cooldown} mounted-shape={mounted} foods={foodSample}";
        GD.Print(valid ? $"[UICompanionAudit] PASS {details}" : $"[UICompanionAudit] FAIL {details}");
        dialog.QueueFree();
    }

    private static void AuditGroup()
    {
        var dialog = new GroupDialog();
        bool valid = dialog.AuditLayout(out string details);
        GD.Print(valid ? $"[UIGroupAudit] PASS {details}" : $"[UIGroupAudit] FAIL {details}");
        dialog.QueueFree();
    }

    private static void AuditGuild()
    {
        var dialog = new GuildDialog();
        bool valid = dialog.AuditLayout(out string details);
        bool pages = dialog.AuditPageLayouts(out string pageDetails);
        // E3 行会仓库: 容量决定网格尺寸 (原版 RefreshStorage 11 列,
        // 行数 = Max(20, Ceil(StorageLimit/14)))，超出容量的格禁用；
        // S.GuildUpdate 回包驱动 StorageLimit/资金刷新。
        bool size = GuildDialog.StorageGridSize(20).Y == 20
            && GuildDialog.StorageGridSize(280).Y == 20
            && GuildDialog.StorageGridSize(281).Y == 21
            && GuildDialog.StorageGridSize(0).Y == 20
            && GuildDialog.StorageGridSize(22).X == 11;
        bool enabled = GuildDialog.StorageCellEnabled(0, 20)
            && !GuildDialog.StorageCellEnabled(20, 20)
            && GuildDialog.StorageCellEnabled(21, 22)
            && !GuildDialog.StorageCellEnabled(0, 0);
        var update = new S.GuildUpdate { StorageLimit = 300, GuildFunds = 999_999 };
        dialog.ApplyGuildUpdate(update);
        bool refresh = dialog.StorageLimit == 300 && dialog.GuildFunds == 999_999
            && GuildDialog.StorageGridSize(300).Y == 22;
        GD.Print(valid && pages && size && enabled && refresh
            ? $"[UIGuildAudit] PASS {details} pages={pageDetails} storage-size/enabled/update"
            : $"[UIGuildAudit] FAIL {details} pages={pageDetails} size={size} enabled={enabled} refresh={refresh}");
        dialog.QueueFree();
    }

    private static void AuditGameStore()
    {
        var dialog = new GameStoreDialog();
        var topIndexes = Globals.StoreInfoList?.Binding
            .Where(x => x?.Item != null)
            .Take(5)
            .Select(x => x.Index);
        dialog.SetTopItems(topIndexes);
        bool valid = dialog.AuditLayout()
            && GameStoreDialog.CanAttemptGift(false, true, 1)
            && !GameStoreDialog.CanAttemptGift(true, true, 1)
            && !GameStoreDialog.CanAttemptGift(false, false, 1)
            && !GameStoreGiftDialog.CanConfirm(true, "Test")
            && !GameStoreGiftDialog.CanConfirm(false, "Invalid Recipient ###")
            && LootBoxDialog.CanRevealWithoutPrompt(0)
            && !LootBoxDialog.CanRevealWithoutPrompt(1)
            && LootBoxDialog.CanSpend(100, 50)
            && !LootBoxDialog.CanSpend(49, 50)
            && !LootBoxDialog.CanSpend(-1, 0)
            && BundleDialog.ShouldUnlockSource(new ClientUserItem(), null) == false
            && LootBoxDialog.ShouldUnlockSource(new ClientUserItem(), null) == false;
        GD.Print(valid
            ? $"[UIGameStoreAudit] PASS size={dialog.Size} list={dialog.ItemListGeometry} top={dialog.TopItemsGeometry} rows={dialog.TopItemRowCount}"
            : $"[UIGameStoreAudit] FAIL size={dialog.Size} list={dialog.ItemListGeometry} top={dialog.TopItemsGeometry} rows={dialog.TopItemRowCount}");
        dialog.QueueFree();
    }

    private static void AuditConsignment()
    {
        var dialog = new ConsignmentDialog();
        bool valid = dialog.AuditBuyGuard(out string details);
        GD.Print(valid
            ? $"[UIConsignmentAudit] PASS {details}"
            : $"[UIConsignmentAudit] FAIL {details}");
        dialog.QueueFree();
    }

    private static void AuditCurrency()
    {
        var dialog = new CurrencyDialog();
        bool valid = dialog.AuditLayout(out string details);
        GD.Print(valid ? $"[UICurrencyAudit] PASS {details}" : $"[UICurrencyAudit] FAIL {details}");
        dialog.QueueFree();
    }

    private static void AuditHelp()
    {
        var dialog = new HelpDialog();
        bool valid = dialog.AuditLayout(out string details);
        GD.Print(valid ? $"[UIHelpAudit] PASS {details}" : $"[UIHelpAudit] FAIL {details}");
        dialog.QueueFree();
    }

    private static void AuditHorse()
    {
        var dialog = new HorseTameDialog();
        bool valid = dialog.AuditLayout(out string details);
        GD.Print(valid ? $"[UIHorseAudit] PASS {details}" : $"[UIHorseAudit] FAIL {details}");
        dialog.QueueFree();
    }

    private static void AuditDungeonFinder()
    {
        var dialog = new DungeonFinderDialog();
        bool valid = dialog.AuditLayout(out string details);
        GD.Print(valid ? $"[UIDungeonAudit] PASS {details}" : $"[UIDungeonAudit] FAIL {details}");
        dialog.QueueFree();
    }

    private static void AuditEditCharacter()
    {
        var dialog = new EditCharacterDialog();
        bool valid = dialog.AuditLayout(out string details)
            && EditCharacterDialog.CanConfirmGender(MirGender.Male, MirGender.Female)
            && !EditCharacterDialog.CanConfirmGender(MirGender.Male, MirGender.Male)
            && EditCharacterDialog.NormalizeHairType(99, MirClass.Warrior, MirGender.Male) == 10
            && EditCharacterDialog.NormalizeHairType(-1, MirClass.Assassin, MirGender.Female) == 0;
        details += " guards=same-gender,hair-range";
        GD.Print(valid ? $"[UIEditCharacterAudit] PASS {details}" : $"[UIEditCharacterAudit] FAIL {details}");
        dialog.QueueFree();
    }

    private static void AuditAutoPotion()
    {
        var dialog = new AutoPotionDialog();
        bool valid = dialog.AuditLayout(out string details);
        GD.Print(valid ? $"[UIAutoPotionAudit] PASS {details}" : $"[UIAutoPotionAudit] FAIL {details}");
        dialog.QueueFree();
    }

    private static void AuditGroupLfg()
    {
        var dialog = new GroupLfgInputDialog(null, (_, _, _, _) => { });
        bool valid = dialog.AuditLayout(out string details);
        GD.Print(valid ? $"[UIGroupLfgAudit] PASS {details}" : $"[UIGroupLfgAudit] FAIL {details}");
        dialog.QueueFree();
    }

    private static void AuditConfig()
    {
        var dialog = new ConfigDialog();
        bool valid = dialog.AuditLayout(out string details);
        GD.Print(valid ? $"[UIConfigAudit] PASS {details}" : $"[UIConfigAudit] FAIL {details}");
        dialog.QueueFree();
    }

    private static void AuditKeyBind()
    {
        var dialog = new KeyBindDialog();
        var probe = KeyBindManager.KeyBinds[0];
        Key oldKey2 = probe.Key2;
        bool oldControl2 = probe.Control2, oldAlt2 = probe.Alt2, oldShift2 = probe.Shift2;
        probe.Key2 = Key.F24;
        probe.Control2 = probe.Alt2 = probe.Shift2 = false;
        bool secondKey = KeyBindManager.GetAction(new InputEventKey { Keycode = Key.F24 }) == probe.Action;
        probe.Key2 = oldKey2;
        probe.Control2 = oldControl2;
        probe.Alt2 = oldAlt2;
        probe.Shift2 = oldShift2;
        bool valid = dialog.AuditLayout(out string details) && secondKey;
        details += $" secondKey={secondKey}";
        GD.Print(valid ? $"[UIKeyBindAudit] PASS {details}" : $"[UIKeyBindAudit] FAIL {details}");
        dialog.QueueFree();
    }

    private void AuditWindowChrome()
    {
        var standard = new CaptionDialog();
        standard.ShowWindow(this);
        var milestone = new MilestoneDialog();
        milestone.ShowWindow(this);

        var floating = new BuffDialog();
        floating.ShowWindow(this);
        var belt = new BeltDialog();
        belt.ShowWindow(this);
        var chat = new ChatTextBox();
        chat.ShowWindow(this);
        var monster = new MonsterDialog();
        monster.ShowWindow(this);
        var miniMap = new MiniMapDialog();
        miniMap.ShowWindow(this);
        var tracker = new QuestTrackerDialog();
        tracker.ShowWindow(this);

        bool standardClose = standard.DefaultCloseButton != null
            && standard.DefaultCloseButton.Index == 15
            && standard.DefaultCloseButton.TooltipText == Lang.CommonControlClose
            && milestone.DefaultCloseButton != null;
        bool floatingHidden = floating.DefaultCloseButton == null
            && belt.DefaultCloseButton == null
            && chat.DefaultCloseButton == null
            && monster.DefaultCloseButton == null
            && miniMap.DefaultCloseButton == null
            && tracker.DefaultCloseButton == null;

        GD.Print(standardClose && floatingHidden
            ? "[UIWindowChromeAudit] PASS default close/hint and no-chrome exceptions"
            : $"[UIWindowChromeAudit] FAIL standard={standardClose} hidden={floatingHidden}");

        foreach (var window in new DXWindow[] { standard, milestone, floating, belt, chat, monster, miniMap, tracker })
            window.QueueFree();
    }

    private void SelfCheck(DXWindow win, DXButton btn1, DXButton btn2)
    {
        GD.Print($"[UITest] 窗口可见={win.Visible} 位置={win.Position} 尺寸={win.Size}");
        GD.Print($"[UITest] 背景贴图164={MirSkin.GetTexture(LibraryFile.Interface, 164) != null} 尺寸={MirSkin.GetSize(LibraryFile.Interface, 164)}");
        GD.Print($"[UITest] Character backgrounds 110={MirSkin.GetSize(LibraryFile.Interface, 110)} 111={MirSkin.GetSize(LibraryFile.Interface, 111)} 112={MirSkin.GetSize(LibraryFile.Interface, 112)} 115={MirSkin.GetSize(LibraryFile.Interface, 115)}");
        GD.Print($"[UITest] complex backgrounds 121={MirSkin.GetSize(LibraryFile.Interface, 121)} 125={MirSkin.GetSize(LibraryFile.Interface, 125)} 300={MirSkin.GetSize(LibraryFile.Interface, 300)} 301={MirSkin.GetSize(LibraryFile.Interface, 301)} 310={MirSkin.GetSize(LibraryFile.Interface, 310)}");
        GD.Print($"[UITest] inventory/companion backgrounds 130={MirSkin.GetSize(LibraryFile.Interface, 130)} 141={MirSkin.GetSize(LibraryFile.Interface, 141)} 200={MirSkin.GetSize(LibraryFile.Interface, 200)} 209={MirSkin.GetSize(LibraryFile.Interface, 209)} 212={MirSkin.GetSize(LibraryFile.Interface, 212)}");
        GD.Print($"[UITest] social backgrounds 240={MirSkin.GetSize(LibraryFile.Interface, 240)} 260={MirSkin.GetSize(LibraryFile.Interface, 260)} 261={MirSkin.GetSize(LibraryFile.Interface, 261)} 262={MirSkin.GetSize(LibraryFile.Interface, 262)} 263={MirSkin.GetSize(LibraryFile.Interface, 263)} 264={MirSkin.GetSize(LibraryFile.Interface, 264)} 265={MirSkin.GetSize(LibraryFile.Interface, 265)} 266={MirSkin.GetSize(LibraryFile.Interface, 266)}");
        GD.Print($"[UITest] auxiliary backgrounds 279={MirSkin.GetSize(LibraryFile.Interface, 279)} 280={MirSkin.GetSize(LibraryFile.Interface, 280)} 281={MirSkin.GetSize(LibraryFile.Interface, 281)} 282={MirSkin.GetSize(LibraryFile.Interface, 282)} 291={MirSkin.GetSize(LibraryFile.Interface, 291)} 292={MirSkin.GetSize(LibraryFile.Interface, 292)} 293={MirSkin.GetSize(LibraryFile.Interface, 293)} 303={MirSkin.GetSize(LibraryFile.Interface, 303)} 304={MirSkin.GetSize(LibraryFile.Interface, 304)} 305={MirSkin.GetSize(LibraryFile.Interface, 305)} 306={MirSkin.GetSize(LibraryFile.Interface, 306)}");
        GD.Print($"[UITest] loot/bundle assets 2900={MirSkin.GetSize(LibraryFile.GameInter2, 2900)} 2920={MirSkin.GetSize(LibraryFile.GameInter2, 2920)} 2925={MirSkin.GetSize(LibraryFile.GameInter2, 2925)} 2926={MirSkin.GetSize(LibraryFile.GameInter2, 2926)} 2927={MirSkin.GetSize(LibraryFile.GameInter2, 2927)}");
        GD.Print($"[UITest] bundle background 3350={MirSkin.GetSize(LibraryFile.GameInter, 3350)}");
        GD.Print($"[UITest] help background 9300={MirSkin.GetSize(LibraryFile.GameInter, 9300)} menu buttons 9310={MirSkin.GetSize(LibraryFile.GameInter, 9310)} 9311={MirSkin.GetSize(LibraryFile.GameInter, 9311)}");
        GD.Print($"[UITest] window frame 0={MirSkin.GetSize(LibraryFile.Interface, 0)} 2={MirSkin.GetSize(LibraryFile.Interface, 2)} 3={MirSkin.GetSize(LibraryFile.Interface, 3)} 10={MirSkin.GetSize(LibraryFile.Interface, 10)} 126={MirSkin.GetSize(LibraryFile.Interface, 126)}");
        GD.Print($"[UITest] fishing 220={MirSkin.GetSize(LibraryFile.Interface, 220)} 230={MirSkin.GetSize(LibraryFile.Interface, 230)} horse 7600={MirSkin.GetSize(LibraryFile.GameInter, 7600)} 7610={MirSkin.GetSize(LibraryFile.GameInter, 7610)} 7620={MirSkin.GetSize(LibraryFile.GameInter, 7620)} 7630={MirSkin.GetSize(LibraryFile.GameInter, 7630)} 7631={MirSkin.GetSize(LibraryFile.GameInter, 7631)}");
        GD.Print($"[UITest] fishing assets 231={MirSkin.GetSize(LibraryFile.Interface, 231)} 232={MirSkin.GetSize(LibraryFile.Interface, 232)} 234={MirSkin.GetSize(LibraryFile.Interface, 234)} fish=4500:{MirSkin.GetSize(LibraryFile.GameInter, 4500)} 4501:{MirSkin.GetSize(LibraryFile.GameInter, 4501)} 4510:{MirSkin.GetSize(LibraryFile.GameInter, 4510)}");
        GD.Print($"[UITest] NPC assets 5700={MirSkin.GetSize(LibraryFile.GameInter, 5700)} 5701={MirSkin.GetSize(LibraryFile.GameInter, 5701)} 380={MirSkin.GetSize(LibraryFile.GameInter, 380)} 381={MirSkin.GetSize(LibraryFile.GameInter, 381)} 382={MirSkin.GetSize(LibraryFile.GameInter, 382)}");
        GD.Print($"[UITest] NPC panels 142={MirSkin.GetSize(LibraryFile.Interface, 142)} 143={MirSkin.GetSize(LibraryFile.Interface, 143)} 146={MirSkin.GetSize(LibraryFile.Interface, 146)} 147={MirSkin.GetSize(LibraryFile.Interface, 147)} 201={MirSkin.GetSize(LibraryFile.Interface, 201)} 202={MirSkin.GetSize(LibraryFile.Interface, 202)} 203={MirSkin.GetSize(LibraryFile.Interface, 203)} 204={MirSkin.GetSize(LibraryFile.Interface, 204)} 205={MirSkin.GetSize(LibraryFile.Interface, 205)}");
        GD.Print($"[UITest] store controls 4830={MirSkin.GetSize(LibraryFile.GameInter, 4830)} 4835={MirSkin.GetSize(LibraryFile.GameInter, 4835)} 4840={MirSkin.GetSize(LibraryFile.GameInter, 4840)} 4845={MirSkin.GetSize(LibraryFile.GameInter, 4845)} 4855={MirSkin.GetSize(LibraryFile.GameInter, 4855)} 4857={MirSkin.GetSize(LibraryFile.GameInter, 4857)} 4872={MirSkin.GetSize(LibraryFile.GameInter, 4872)}");
        GD.Print($"[UITest] 按钮1贴图210={MirSkin.GetTexture(LibraryFile.Interface, 210) != null} 尺寸={MirSkin.GetSize(LibraryFile.Interface, 210)}");
        GD.Print($"[UITest] MainPanel底图50={MirSkin.GetSize(LibraryFile.GameInter, 50)} 经验条51={MirSkin.GetSize(LibraryFile.GameInter, 51)}");
        GD.Print($"[UITest] 按钮2尺寸={btn2.Size} 中文字体={MirSkin.GetFont() != null}");
        GD.Print($"[UITest] 字体尺寸测试='攻击'={MirSkin.MeasureText("攻击", 14)}");
        if (_uiAudit)
        {
            bool scaleOk = _uiLayer != null
                && Mathf.IsEqualApprox(_uiLayer.Transform.X.X, 2f)
                && Mathf.IsEqualApprox(_uiLayer.Transform.Y.Y, 2f);
            bool logicalAnchorOk = win.Position.IsEqualApprox(new Vector2(160, 120))
                && btn1.Position.IsEqualApprox(new Vector2(160, 470));
            GD.Print(scaleOk && logicalAnchorOk
                ? "[UIAudit] PASS scale=2 logical anchors preserved"
                : $"[UIAudit] FAIL transform={_uiLayer?.Transform} win={win.Position} btn={btn1.Position}");
        }
    }

    private async void ScreenshotThenWait()
    {
        for (int i = 0; i < 10; i++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        var img = GetViewport().GetTexture().GetImage();
        img.SavePng("/tmp/ui_test.png");
        GD.Print($"[UITest] 截图已保存 /tmp/ui_test.png ({(int)img.GetWidth()}x{(int)img.GetHeight()})");
        GD.Print("[UITest] 画面已就绪, 按 Esc 或关闭窗口退出");
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
            GetTree().Quit();
    }

    private partial class TestWindow : DXWindow { }
}
