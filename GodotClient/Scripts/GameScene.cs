using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using Godot;
using Library;
using Library.Network;
using Library.SystemModels;
using G = Library.Network.GeneralPackets;
using S = Library.Network.ServerPackets;
using C = Library.Network.ClientPackets;
using ZirconClient.Controls;
using ZirconClient.Formats;

namespace ZirconClient.Scripts;

public partial class GameScene : Control
{
    // 开发阶段暂时关闭商城及充值入口，避免测试时误触发商城/支付流程。
    // 当前 EI 三职业兼容范围：商城、寄售、副本和宠物入口关闭；
    // 自动巡路、自动战斗、套装、宝石、钓鱼、声望和称号保持可用。
    private static readonly bool GameStoreEnabled = false;
    private static readonly bool ConsignmentEnabled = false;
    private static readonly bool DungeonEnabled = false;
    private static readonly bool CompanionEnabled = false;
    public static bool IsCompanionEnabled => CompanionEnabled;
    public static bool IsConsignmentEnabled => ConsignmentEnabled;
    /// <summary>
    /// UI 缩放系数：跟随窗口高度保持逻辑视口高恒定（原版 1024x768 基准）。
    /// 窗口 1536 高 → 2x（旧行为）；更高窗口等比放大，UI/字体占屏比例不变。
    /// </summary>
    internal static float UiScale { get; private set; } = 2f;
    private const float UiScaleBaseHeight = 768f;
    private const float WorldScale = 1f;
    private const string UiAuditArgument = "--ui-layout-audit";
    private Vector2 _lastHudViewport;
    private float _lastHudScale;
    public float DayTime { get; private set; } = 1f;
    public TimeOfDay TimeOfDay { get; private set; } = TimeOfDay.Day;
    public bool DrawWeather { get; private set; } = true;
    /// <summary>玩家当前"隐藏头盔"状态（设置页初始勾选用，服务端回包同步）。</summary>
    public bool PlayerHideHead => _player?.HideHead ?? false;

    private Network.NetworkManager _net;
    private MapView _mapView;
    private readonly Dictionary<int, string> _castleOwners = new();
    private readonly Dictionary<int, DateTime> _castleWarDates = new();
    private readonly Dictionary<int, ClientUserQuest> _userQuests = new();
    public IReadOnlyDictionary<int, string> CastleOwners => _castleOwners;
    public DateTime? GetCastleWarDate(int index) => _castleWarDates.TryGetValue(index, out var date) ? date : null;
    private Label _statusLabel;
    private Label _debugLabel;
    private PlayerRenderer _player;
    private readonly Dictionary<SoundIndex, AudioStream> _actionSounds = new();
    private readonly Dictionary<SoundIndex, AudioStreamPlayer> _loopingSounds = new();
    private readonly Dictionary<uint, SoundIndex> _durationSoundByObject = new();
    private SoundIndex _mapMusic = SoundIndex.None;
    private bool _leavingGame;
    private List<SelectInfo> _pendingLogoutCharacters;
    private CombatController _combatController;
    private MapLightLayer _lightLayer;
    private MapWeatherLayer _weatherLayer;
    private MouseWalker _mouseWalker;

    // M11: UI 窗口层 (与 2D 世界分层) + 窗口管理器
    private CanvasLayer _uiLayer;
    public Node UILayer => _uiLayer;
    private StatusWindow _statusWindow;
    private MenuDialog _menuDialog;
    private ExitDialog _exitDialog;
    private NoticeDialog _noticeDialog;
    private ChatLogPanel _chatLog;
    private ChatTextBox _chatTextBox;
    private HelpDialog _helpDialog;
    private ConfigDialog _configDialog;
    private ChatOptionsDialog _chatOptionsDialog;
    private GuildDialog _guildDialog;
    private GuildMemberDialog _guildMemberDialog;
    private MilestoneDialog _milestoneDialog;
    private readonly Dictionary<int, ClientUserMilestone> _milestones = new();
    private CompanionDialog _companionDialog;
    private NPCCompanionStorageDialog _npcCompanionStorageDialog;
    private RankingDialog _rankingDialog;
    private CommunicationDialog _communicationDialog;
    private GameStoreDialog _gameStoreDialog;
    private ConsignmentDialog _consignmentDialog;
    private MarketHistoryDialog _marketHistoryDialog;
    private FishingDialog _fishingDialog;
    private FishingCatchDialog _fishingCatchDialog;
    private HorseTameDialog _horseTameDialog;
    private HorseDialog _horseDialog;
    private uint _tamingTargetObjectID;
    // 原版 MapControl 挖矿状态机：左键点矿点后 Mining=true，
    // 每帧满足条件（矿点在界内/Flag/相邻/武器槽 PickAxe/无马/冷却到）
    // 就重复 AttemptAction(Mining)，否则 Mining=false。
    private bool _mining;
    private System.Drawing.Point _miningPoint;
    private double _nextMiningMs;
    // 原版 AttemptAction(Harvest) 的 Globals.HarvestTime(600ms) 冷却。
    private double _nextHarvestMs;
    private MonsterDialog _monsterDialog;
    private TradeDialog _tradeDialog;
    private NPCDialog _npcDialog;
    private NPCSocketDialog _npcSocketDialog;
    private NPCSocketCombineDialog _npcSocketCombineDialog;
    private NPCQuestListDialog _npcQuestListDialog;
    private NPCQuestDialog _npcQuestDialog;
    private readonly Dictionary<uint, NPCInfo> _npcInfos = new();
    private uint _npcObjectId;
    public uint NPCObjectId => _npcObjectId;
    private GroupDialog _groupDialog;
    private GroupHealthPanel _groupHealthPanel;
    private double _statusRefreshMs;

    // 进图过渡遮罩
    private CanvasLayer _coverLayer;
    private ColorRect _startupCoverRect;

    // M12: HUD + 键位
    private MainPanel _mainPanel;
    private MiniMapDialog _miniMap;
    private BigMapDialog _bigMap;
    private BuffDialog _buffDialog;
    private QuestTrackerDialog _questTracker;
    private QuestDialog _questDialog;
    private readonly System.Collections.Generic.Dictionary<int, ClientBuffInfo> _buffs = new();
    private MagicBar _magicBar;
    private MagicDialog _magicDialog;
    private Stats _playerStats = new Stats();
    private int _playerLevel;
    public int PlayerLevel => _playerLevel;
    private decimal _playerExperience, _playerMaxExperience;
    private int _currentHP, _currentMP, _currentFP;
    private AttackMode _attackMode;
    private MagicType _attackMagic;
    private PetMode _petMode;

    // ---- M9 物品系统: 数据模型 (数组即底层格, DXItemCell 直读直写) ----
    public static GameScene Game;
    public IEnumerable<ClientUserMilestone> Milestones => _milestones.Values;

    /// <summary>旧版 HasUnclaimedMilestoneReward：存在完成但未领取且有奖励的里程碑。</summary>
    public bool HasUnclaimedMilestoneReward()
        => _milestones.Values.Any(x => x.IsComplete && !x.Claimed && x.Info?.Reward != null);
    public bool QuestTrackerVisible { get; private set; } = true;
    public ClientUserQuest GetUserQuest(int index) => _userQuests.TryGetValue(index, out var quest) ? quest : null;

    public void SetQuestTrackerVisible(bool visible)
    {
        QuestTrackerVisible = visible;
        ClientSettings.QuestTrackerVisible = visible;
        ClientSettings.Save();
        if (_questTracker == null) return;
        _questTracker.TrackingEnabled = visible;
        if (visible) _questTracker.PopulateQuests(_userQuests.Values);
        else _questTracker.Visible = false;
    }

    public bool CanAcceptQuest(QuestInfo quest)
    {
        if (quest?.StartNPC == null || quest.FinishNPC == null) return false;
        if (_userQuests.ContainsKey(quest.Index)) return false;
        foreach (var requirement in quest.Requirements ?? Enumerable.Empty<QuestRequirement>())
        {
            switch (requirement.Requirement)
            {
                case QuestRequirementType.MinLevel when PlayerLevel < requirement.IntParameter1:
                case QuestRequirementType.MaxLevel when PlayerLevel > requirement.IntParameter1:
                    return false;
                case QuestRequirementType.NotAccepted when requirement.QuestParameter != null &&
                    _userQuests.ContainsKey(requirement.QuestParameter.Index):
                    return false;
                case QuestRequirementType.HaveCompleted:
                    if (requirement.QuestParameter == null || !_userQuests.TryGetValue(requirement.QuestParameter.Index, out var completed) || !completed.Completed)
                        return false;
                    break;
                case QuestRequirementType.HaveNotCompleted when requirement.QuestParameter != null &&
                    _userQuests.TryGetValue(requirement.QuestParameter.Index, out var existing) && existing.Completed:
                    return false;
                case QuestRequirementType.Class:
                    var required = StartInfo?.Class switch
                    {
                        MirClass.Warrior => RequiredClass.Warrior,
                        MirClass.Wizard => RequiredClass.Wizard,
                        MirClass.Taoist => RequiredClass.Taoist,
                        MirClass.Assassin => RequiredClass.Assassin,
                        _ => RequiredClass.None,
                    };
                    if (required != RequiredClass.None && (requirement.Class & required) != required) return false;
                    break;
            }
        }
        return true;
    }

    // 与原版 GameScene.GetQuestText/GetTaskText 同一套文本选择和进度格式，
    // NPC 任务详情、任务日志和右侧追踪器都共用，避免详情页只显示 QuestTask.ToString()。
    public string GetQuestText(QuestInfo questInfo, ClientUserQuest userQuest, bool isLog = false)
    {
        if (questInfo == null) return string.Empty;
        string text = userQuest == null ? questInfo.AcceptText :
            userQuest.Completed ? questInfo.ArchiveText :
            userQuest.IsComplete && !isLog ? questInfo.CompletedText : questInfo.ProgressText;
        text ??= string.Empty;
        text = text.Replace("[PLAYERNAME]", StartInfo?.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        text = text.Replace("[STARTNAME]", questInfo.StartNPC?.Local() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        text = text.Replace("[FINISHNAME]", questInfo.FinishNPC?.Local() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        return text;
    }

    public string GetTaskText(QuestInfo questInfo, ClientUserQuest userQuest)
        => questInfo?.Tasks == null ? string.Empty : string.Join("\n", questInfo.Tasks.Select(x => GetTaskText(x, userQuest)));

    public string GetTaskText(QuestTask task, ClientUserQuest userQuest)
    {
        if (task == null) return string.Empty;
        var parts = new List<string>();
        switch (task.Task)
        {
            case QuestTaskType.KillMonster:
                parts.Add($"Kill {task.Amount}");
                break;
            case QuestTaskType.GainItem:
                parts.Add($"Collect {task.Amount} {task.ItemParameter?.ItemName}");
                break;
            case QuestTaskType.VisitRegion:
                parts.Add($"Goto {task.RegionParameter?.Description} in {task.RegionParameter?.Map?.PlayerDescription}");
                break;
        }

        if (string.IsNullOrEmpty(task.MobDescription))
        {
            if (task.Task == QuestTaskType.GainItem && task.MonsterDetails?.Count > 0) parts.Add("from");
            var monsters = new List<string>();
            foreach (var detail in task.MonsterDetails ?? Enumerable.Empty<QuestTaskMonsterDetails>())
            {
                if (detail?.Monster == null) continue;
                if (monsters.Count >= 3) { monsters.Add("..."); break; }
                monsters.Add(detail.Monster.MonsterName +
                    (detail.Map == null ? string.Empty : $" in {detail.Map.PlayerDescription}"));
            }
            if (monsters.Count > 0) parts.Add(string.Join(" or ", monsters));
        }
        else
        {
            if (task.Task == QuestTaskType.GainItem && task.MonsterDetails?.Count > 0) parts.Add("from");
            parts.Add(task.MobDescription);
        }

        var userTask = userQuest?.Tasks?.FirstOrDefault(x => x?.Task == task || x?.TaskIndex == task.Index);
        if (userQuest != null)
            parts.Add(userTask?.Completed == true ? "(Completed)" :
                task.Task == QuestTaskType.VisitRegion ? string.Empty : $"({userTask?.Amount ?? 0}/{task.Amount})");
        return string.Join(" ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    public bool IsFishingActive => _fishingCatchDialog?.IsActive == true;
    public bool IsTamingActive => _horseTameDialog?.Visible == true;
    public bool IsMounted => _playerHorse != HorseType.None;

    public void ToggleStorageWindow()
    {
        if (_storageDialog != null)
            WindowManager.Toggle(_storageDialog, _uiLayer);
    }

    public void LeaveGame()
    {
        if (_leavingGame) return;
        _leavingGame = true;
        // 原版这里仅发送 C.Logout，等待服务端返回 S.GameLogout 后再切回角色选择。
        // 连接必须保留在 Select 阶段，否则角色列表回包会在客户端切场景前丢失。
        _net?.Connection?.SendLogout();
        WindowManager.Close(_exitDialog);
    }

    public void ExitClient()
    {
        // “退出客户端”和“返回角色选择”是两个不同的原版操作：前者关闭进程，
        // 后者等待 GameLogout 回包并复用当前登录连接。
        _net?.Connection?.SendLogout();
        _net?.Disconnect();
        GetTree().Quit();
    }

    public void OpenRechargePage()
    {
        if (!GameStoreEnabled) return;
        if (_net == null || string.IsNullOrWhiteSpace(_net.BuyAddress))
        {
            ReceiveChat(Lang.GameUi542Label, MessageType.System);
            return;
        }
        var address = _net.BuyAddress + Uri.EscapeDataString(StartInfo?.Name ?? string.Empty);
        OS.ShellOpen(address);
    }

    public void SendChat(string text)
        => SendChat(text, new List<int>());

    public void SendChat(string text, List<int> linkedItemIndexes)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        // 客户端本地调试命令 @uiReload：重读 UI/ui_overlay.json 并刷新全部窗口
        //（与 F12 等价；服务器没有对应命令，直接拦截不发送）
        if (text.Trim() == "@uiReload")
        {
            UiOverlay.ReloadAll();
            ReceiveChat("[UiOverlay] 已热重载 ui_overlay.json", MessageType.System);
            return;
        }

        _net?.Connection?.Enqueue(new C.Chat
        {
            Text = text.Trim(),
            LinkedItemIndexes = linkedItemIndexes ?? new List<int>(),
        });
    }

    public void SendGameStoreBuy(int index, long count, bool useHuntGold)
    {
        if (!GameStoreEnabled) return;
        if (IsObserver || index < 0 || count <= 0) return;
        _net?.Connection?.SendGameStoreBuy(index, count, useHuntGold);
    }
    public void SendGameStoreGift(int index, long count, bool useHuntGold, string recipient)
    {
        if (!GameStoreEnabled) return;
        if (IsObserver || index < 0 || count <= 0 || string.IsNullOrWhiteSpace(recipient)) return;
        _net?.Connection?.SendGameStoreGift(index, count, useHuntGold, recipient.Trim());
    }
    public void SendGameStoreFavourite(int index)
    {
        if (!GameStoreEnabled) return;
        if (IsObserver || index < 0) return;
        _net?.Connection?.SendGameStoreFavourite(index);
    }
    public void ReceiveChat(string text, MessageType type = MessageType.System, List<ClientUserItem> linkedItems = null)
    {
        _chatLog?.AddMessage(text, type, Colors.Yellow, linkedItems);
        if (type == MessageType.Announcement && AutoLoginArgs.LegacyUi)
            ShowLegacyNotice(text);
    }

    public void ShowLegacyNotice(string text)
    {
        if (_noticeDialog == null) return;
        _noticeDialog.SetNotice(text);
        WindowManager.Open(_noticeDialog, _uiLayer);
    }

    public void StartPrivateMessage(string name) => _chatTextBox?.StartPM(name);

    public void OpenExitDialog()
    {
        if (_exitDialog == null) return;
        WindowManager.Open(_exitDialog, _uiLayer);
    }

    public void OpenHelpDialog()
    {
        if (_helpDialog != null) WindowManager.Open(_helpDialog, _uiLayer);
    }

    public void OpenConfigDialog()
    {
        if (_configDialog != null) WindowManager.Open(_configDialog, _uiLayer);
    }

    public void SetDrawWeather(bool enabled)
    {
        DrawWeather = enabled;
        ClientSettings.DrawWeather = enabled;
        ClientSettings.Save();
        _weatherLayer?.SetEnabled(enabled);
    }

    public void SetHideChatBar(bool hidden)
    {
        ClientSettings.HideChatBar = hidden;
        ClientSettings.Save();
        if (_chatLog != null) _chatLog.Visible = !hidden;
        if (_chatTextBox != null) _chatTextBox.Visible = !hidden;
    }

    public void OpenChatOptionsDialog()
    {
        if (_chatOptionsDialog != null) WindowManager.Open(_chatOptionsDialog, _uiLayer);
    }

    public bool IsChatTypeEnabled(MessageType type) => _chatLog?.IsTypeEnabled(type) ?? true;
    public void SetChatFilter(MessageType type, bool enabled) => _chatLog?.SetTypeEnabled(type, enabled);
    public void AddChatTab(string title) => _chatLog?.AddTab(title);
    public void ResetChatTabs() => _chatLog?.ResetTabs();
    public void SelectChatTab(int index) => _chatLog?.SelectTab(index);
    public int ChatTabCount => _chatLog?.TabCount ?? 0;
    public int SelectedChatTab => _chatLog?.SelectedTabIndex ?? 0;
    public string GetChatTabTitle(int index) => _chatLog?.GetTabTitle(index) ?? string.Empty;
    public void RenameChatTab(int index, string title) => _chatLog?.RenameTab(index, title);
    public void RemoveChatTab(int index) => _chatLog?.RemoveTab(index);
    public bool GetChatOption(string option) => _chatLog?.GetOption(option) ?? false;
    public void SetChatOption(string option, bool enabled) => _chatLog?.SetOption(option, enabled);
    public void SaveChatTabs() => _chatLog?.SaveTabs();
    public void LoadChatTabs() => _chatLog?.LoadTabs();

    public void OpenGuildDialog() { if (_guildDialog != null) WindowManager.Open(_guildDialog, _uiLayer); }
    public bool HasGuild => _guildDialog?.HasGuild == true;
    public long GuildFunds => _guildDialog?.GuildFunds ?? 0;
    public int GuildFlag => _guildDialog?.GuildFlag ?? -1;
    public System.Drawing.Color GuildColour => _guildDialog?.GuildColour ?? System.Drawing.Color.White;
    public IEnumerable<DXItemCell> GuildStorageCells => _guildDialog?.GuildStorageCells ?? Array.Empty<DXItemCell>();
    public void OpenGuildMemberDialog(int index, string name, string rank, GuildPermission permission)
    {
        if (_guildMemberDialog == null) return;
        _guildMemberDialog.OpenMember(index, name, rank, permission);
        WindowManager.Open(_guildMemberDialog, _uiLayer);
    }
    public void OpenRankingDialog() { if (_rankingDialog != null) { WindowManager.Open(_rankingDialog, _uiLayer); RequestRankings(0, false); } }
    public void RequestRankings(int startIndex, bool onlineOnly, RequiredClass classFilter = RequiredClass.None)
        => _net?.Connection?.Enqueue(new C.RankRequest { Class = classFilter, OnlineOnly = onlineOnly, StartIndex = startIndex });
    public static bool CanSendQuestOperation(bool observer, int index)
        => !observer && index >= 0;

    public void SendQuestAccept(int index)
    {
        if (!CanSendQuestOperation(IsObserver, index)) return;
        PlaySound(SoundIndex.QuestTake);
        _net?.Connection?.Enqueue(new C.QuestAccept { Index = index });
    }
    public void SendQuestComplete(int index, int choiceIndex = 0)
    {
        if (!CanSendQuestOperation(IsObserver, index)) return;
        PlaySound(SoundIndex.QuestComplete);
        _net?.Connection?.Enqueue(new C.QuestComplete { Index = index, ChoiceIndex = choiceIndex });
    }
    public void SendQuestTrack(int index, bool track)
        => _net?.Connection?.Enqueue(new C.QuestTrack { Index = index, Track = track });
    public void SendQuestAbandon(int index)
    {
        if (!CanSendQuestOperation(IsObserver, index)) return;
        _net?.Connection?.Enqueue(new C.QuestAbandon { Index = index });
    }
    public void SendFriendAdd(string name)
        => _net?.Connection?.Enqueue(new C.FriendAdd { Name = name ?? string.Empty });
    public void SendFriendRemove(int index)
        => _net?.Connection?.Enqueue(new C.FriendRemove { Index = index });
    public void SendBlockAdd(string name) => _net?.Connection?.SendBlockAdd(name);
    public void SendBlockRemove(int index) => _net?.Connection?.SendBlockRemove(index);
    // 修炼功能暂时关闭：即使未来有隐藏控件或旧 UI 回调，也不向服务端发请求。
    public void SendIncreaseDiscipline() { }
    public void SendGuildTax(long tax) => _net?.Connection?.SendGuildTax(tax);
    public void SendMarriageResponse(bool accept)
    {
        if (IsObserver) return;
        _net?.Connection?.SendMarriageResponse(accept);
    }
    public void SendMarriageMakeRing(int slot)
    {
        if (IsObserver || slot < 0) return;
        _net?.Connection?.SendMarriageMakeRing(slot);
    }
    public void SendTeleportRing(int x, int y, int mapIndex)
    {
        if (IsObserver || mapIndex < 0 || x < 0 || y < 0) return;
        _net?.Connection?.SendTeleportRing(new System.Drawing.Point(x, y), mapIndex);
    }
    public void SendGroupLfg(bool enabled, string name, string type, int maxCount) => _net?.Connection?.SendGroupLfg(enabled, name, type, maxCount);
    public void SendGroupNotify(bool receive) => _net?.Connection?.SendGroupNotify(receive);
    public void SendMagicToggle(MagicType magic, bool canUse) => _net?.Connection?.SendMagicToggle(magic, canUse);
    // 隐士功能暂时关闭：保留方法签名以兼容现有控件代码，但不执行加点。
    public void SendHermit(Stat stat) { }
    public void SendObservable(bool allow) => _net?.Connection?.SendObservable(allow);
    public void SendTownRevive() => _net?.Connection?.SendTownRevive();
    private ClientUserCurrency _selectedCurrency;
    /// <summary>原版 GameScene.CurrencyPickedUp：选中货币后，物品格点击只能继续处理丢弃数量。</summary>
    public bool CurrencyPickedUp => _selectedCurrency != null;
    public void SelectCurrency(ClientUserCurrency currency)
    {
        // 原版 InventoryDialog：拿起任意货币后再次点击货币标签只取消，
        // 不会在一次操作中把选中货币偷偷切换到另一种；拿起物品时货币标签
        // 也不抢占 SelectedCell。CanPickup 同时校验 DropItem.CanDrop。
        if (DXItemCell.SelectedCell != null) return;
        if (_selectedCurrency != null)
        {
            _selectedCurrency = null;
            return;
        }
        if (currency?.CanPickup == true && currency.Amount > 0)
            _selectedCurrency = currency;
    }

    public void ToggleCurrencyWindow()
    {
        WindowManager.Toggle(_currencyDialog, _uiLayer);
        _currencyDialog?.RefreshCurrencies(Currencies);
    }
    public void SendObserverRequest(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        _net?.Connection?.Enqueue(new C.ObserverRequest { Name = name.Trim() });
    }
    public void SendRankingInspect(int index)
        => _net?.Connection?.Enqueue(new C.Inspect { Index = index, Ranking = true });
    public void OpenCompanionDialog() { if (!CompanionEnabled) return; if (_companionDialog != null) WindowManager.Open(_companionDialog, _uiLayer); }
    public void OpenNPCCompanionStorage()
    {
        if (!CompanionEnabled) return;
        if (_npcCompanionStorageDialog == null) return;
        _npcCompanionStorageDialog.SetCompanions(Companions);
        WindowManager.Open(_npcCompanionStorageDialog, _uiLayer);
    }
    public void CloseNPCCompanionStorage()
    {
        if (_npcCompanionStorageDialog != null) WindowManager.Close(_npcCompanionStorageDialog);
    }
    public bool TryRouteItemToNpc(DXItemCell source)
    {
        if (_inventoryDialog?.IsSellMode == true && source?.GridType == GridType.Inventory)
            return _inventoryDialog.TrySelectForSale(source);
        return TryRouteItemToSocket(source) || _npcDialog?.TryRouteItem(source) == true;
    }

    public bool TrySelectItemForNpcSale(DXItemCell source)
        => _inventoryDialog?.IsSellMode == true && _inventoryDialog.Visible && _inventoryDialog.TrySelectForSale(source);

    public void ShowInventoryForNpcSale(CurrencyInfo currency, IEnumerable<ItemType> sellableTypes)
    {
        if (_inventoryDialog == null) return;
        _inventoryDialog.SellMode(currency, sellableTypes);
        _inventoryDialog.Visible = true;
    }

    public void EndInventoryNpcSale() => _inventoryDialog?.NormalMode();

    public void SetInventoryLegacyMode(InventoryMode mode)
        => _inventoryDialog?.SetLegacyMode(mode);

    public bool CanRouteAdvancedItem(DXItemCell source, DXItemCell target)
        => _npcDialog?.CanAcceptAdvancedLink(source, target) ?? true;

    public bool CanRouteRepairItem(DXItemCell source)
        => _npcDialog?.CanAcceptRepairLink(source) ?? true;

    /// <summary>原版 CompanionBox 可见时，背包右键把物品投放到伙伴背包。</summary>
    public bool TryRouteItemToCompanion(DXItemCell source)
    {
        if (_companionDialog?.BagVisible != true || source?.GridType != GridType.Inventory || source.Item == null)
            return false;
        return _companionDialog.InventoryGrid != null && source.MoveItem(_companionDialog.InventoryGrid);
    }

    public void UnlockItemLink(CellLinkInfo link)
    {
        if (link == null) return;
        var cells = link.GridType switch
        {
            GridType.Inventory => InventoryCells,
            GridType.Equipment => EquipmentCells,
            GridType.Storage => StorageCells,
            GridType.PartsStorage => PartsStorageCells,
            GridType.GuildStorage => GuildStorageItemCells,
            GridType.CompanionInventory => CompanionInventoryCells,
            GridType.CompanionEquipment => CompanionEquipmentCells,
            _ => Array.Empty<DXItemCell>(),
        };
        if (link.Slot < 0 || link.Slot >= cells.Length) return;
        cells[link.Slot].UnlockForTrade();
    }

    public bool TryRouteItemToSocket(DXItemCell source)
    {
        if (source?.Item == null) return false;
        if (_npcSocketDialog?.Visible == true && _npcSocketDialog.TryRouteItem(source)) return true;
        if (_npcSocketCombineDialog?.Visible == true && _npcSocketCombineDialog.TryRouteItem(source)) return true;
        return false;
    }

    public void OpenNPCSocketDialog()
    {
        if (_npcSocketDialog == null) return;
        WindowManager.Close(_npcSocketCombineDialog);
        WindowManager.Open(_npcSocketDialog, _uiLayer);
    }

    public void OpenNPCSocketCombineDialog()
    {
        if (_npcSocketCombineDialog == null) return;
        WindowManager.Close(_npcSocketDialog);
        WindowManager.Open(_npcSocketCombineDialog, _uiLayer);
    }

    public void CloseNPCSocketDialogs()
    {
        _npcSocketDialog?.Panel.Reset();
        _npcSocketCombineDialog?.Panel.Reset();
        if (_npcSocketDialog != null) WindowManager.Close(_npcSocketDialog);
        if (_npcSocketCombineDialog != null) WindowManager.Close(_npcSocketCombineDialog);
    }

    public void CloseNPCDialog()
    {
        _inventoryDialog?.NormalMode();
        _npcDialog?.CancelUnsubmittedLinks();
        CloseNPCSocketDialogs();
        if (_npcQuestListDialog != null) WindowManager.Close(_npcQuestListDialog);
        if (_npcQuestDialog != null) WindowManager.Close(_npcQuestDialog);
        if (_npcCompanionStorageDialog != null) WindowManager.Close(_npcCompanionStorageDialog);
        if (_npcDialog != null) WindowManager.Close(_npcDialog);
    }

    public void OpenNPCQuestList(uint objectId)
    {
        if (_npcQuestListDialog == null || !_npcInfos.TryGetValue(objectId, out var npc)) return;
        _npcQuestListDialog.Location = new Vector2I(_npcDialog?.Location.X ?? 0, (_npcDialog?.Location.Y ?? 0) + (int)(_npcDialog?.Size.Y ?? 204));
        _npcQuestListDialog.OpenFor(npc);
    }

    public void OpenNPCQuestDialog(QuestInfo quest)
    {
        if (_npcQuestDialog == null || quest == null) return;
        var listLocation = _npcQuestListDialog?.Location ?? Vector2I.Zero;
        _npcQuestDialog.Location = new Vector2I(listLocation.X + (int)(_npcQuestListDialog?.Size.X ?? 240), listLocation.Y);
        _npcQuestDialog.OpenFor(quest);
    }

    public void CloseNPCQuestDialogs()
    {
        if (_npcQuestListDialog != null) WindowManager.Close(_npcQuestListDialog);
        if (_npcQuestDialog != null) WindowManager.Close(_npcQuestDialog);
    }
    public bool BundleBoxVisible => _bundleDialog?.Visible == true;
    public bool LootBoxVisible => _lootBoxDialog?.Visible == true;
    public bool TryRouteItemToTradeOrConsign(DXItemCell source)
    {
        if (_consignmentDialog?.Visible == true && _consignmentDialog.TryRouteItem(source)) return true;
        if (_communicationDialog?.Visible == true && _communicationDialog.TryRouteItem(source)) return true;
        // 原版 DXItemCell.OnMouseClick 的背包分支先尝试仓库，
        // 再尝试交易/行会仓库；多个窗口同时可见时不能让交易抢走仓库存取。
        if (source?.Item != null && source.GridType == GridType.Inventory &&
            _storageDialog?.Visible == true)
        {
            if (source.Item.Info?.ItemEffect == ItemEffect.ItemPart)
            {
                if (_storageDialog.PartGrid?.Visible == true && source.MoveItem(_storageDialog.PartGrid))
                    return true;
            }
            else if (_storageDialog.Grid?.Visible == true && source.MoveItem(_storageDialog.Grid))
                return true;
        }
        if (_tradeDialog?.Visible == true && _tradeDialog.TryRouteItem(source)) return true;
        if (_guildDialog?.Visible == true && _guildDialog.TryRouteItem(source)) return true;
        return false;
    }
    public void OpenCommunicationDialog() { if (_communicationDialog != null) WindowManager.Open(_communicationDialog, _uiLayer); }
    public void OpenGroupDialog() { if (_groupDialog != null) { _net?.Connection?.SendGroupNotify(true); WindowManager.Open(_groupDialog, _uiLayer); } }
    public void CloseGroupDialog() { if (_groupDialog != null) WindowManager.Close(_groupDialog); }
    public void OpenGameStoreDialog()
    {
        if (!GameStoreEnabled) return;
        if (_gameStoreDialog != null) WindowManager.Open(_gameStoreDialog, _uiLayer);
    }
    public void OpenConsignmentDialog() { if (!ConsignmentEnabled) return; if (_consignmentDialog != null) WindowManager.Open(_consignmentDialog, _uiLayer); }
    public void OpenMarketHistory(ClientUserItem item) { if (_marketHistoryDialog != null) _marketHistoryDialog.ShowFor(item); }
    public void OpenFishingDialog() { if (_fishingDialog != null) WindowManager.Open(_fishingDialog, _uiLayer); }
    public void OpenEditCharacterDialog()
    {
        _editCharacterDialog?.ResetForCurrent();
        if (_editCharacterDialog != null) WindowManager.Open(_editCharacterDialog, _uiLayer);
    }

    public void OpenEditCharacterDialog(EditCharacterChange change)
    {
        OpenEditCharacterDialog();
        _editCharacterDialog?.SelectChange(change);
    }

    public void StartFishing()
    {
        OpenFishingDialog();
        SendFishingCast(FishingState.Cast);
    }
    public void OpenCaptionDialog() { if (_captionDialog != null) WindowManager.Open(_captionDialog, _uiLayer); }
    public void OpenFortuneCheckerDialog() { if (_fortuneDialog != null) WindowManager.Open(_fortuneDialog, _uiLayer); }

    public ClientUserItem[] Inventory = new ClientUserItem[Globals.InventorySize];
    public ClientUserItem[] Equipment = new ClientUserItem[Globals.EquipmentSize];
    public ClientUserItem[] Storage = new ClientUserItem[Globals.StorageSize];
    public ClientUserItem[] PartsStorage = new ClientUserItem[Globals.StorageSize];
    public ClientUserItem[] CompanionInventory = new ClientUserItem[Globals.InventorySize];
    public ClientUserItem[] CompanionEquipment = new ClientUserItem[4];
    public ClientUserCompanion Companion;
    public readonly List<ClientUserCompanion> Companions = new();
    public List<ClientUserCurrency> Currencies = new();
    public ClientBeltLink[] BeltLinks = new ClientBeltLink[Globals.MaxBeltCount];
    public int StorageSize = Globals.StorageSize;
    public int BagWeight, WearWeight, HandWeight;

    public DXItemCell[] InventoryCells = Array.Empty<DXItemCell>();
    public DXItemCell[] EquipmentCells = Array.Empty<DXItemCell>();
    public DXItemCell[] CompanionEquipmentCells = Array.Empty<DXItemCell>();
    public DXItemCell[] CompanionInventoryCells => _companionDialog?.InventoryGrid?.Cells ?? Array.Empty<DXItemCell>();
    public DXItemCell[] GuildStorageItemCells => _guildDialog?.GuildStorageCells ?? Array.Empty<DXItemCell>();
    public DXItemCell[] StorageCells => _storageDialog?.StorageCells ?? Array.Empty<DXItemCell>();
    public DXItemCell[] PartsStorageCells => _storageDialog?.PartGrid?.Cells ?? Array.Empty<DXItemCell>();

    // 物品交互状态
    public double UseItemTime;          // 服务端 S.ItemUseDelay 给的下次可用时间 (绝对 ms)
    private double _pickUpNextMs;       // Tab 拾取节流 (250ms)
    private DXImageControl _mouseItemIcon;  // 拿起物品跟随鼠标的图标
    private DXLabel _mouseItemLabel;    // 拿起物品跟随鼠标的文字
    private DXLabel _hoverLabel;        // 物品悬浮提示
    private ClientUserItem _hoverItem;
    // 每个地面物品都有经典白色闪烁；稀有物品还会叠加其蓝/绿/紫光效。
    private readonly System.Collections.Generic.Dictionary<uint, List<MirEffectNode>> _itemGlows = new();
    private readonly System.Collections.Generic.Dictionary<int, MirEffectNode> _buffEffects = new();
    private readonly System.Collections.Generic.Dictionary<uint, MirEffectNode> _spellEffects = new();
    private readonly System.Collections.Generic.Dictionary<(uint, BuffType), MirEffectNode> _objectBuffEffects = new();
    private readonly System.Collections.Generic.Dictionary<uint, MirEffectNode> _movementEffects = new();
    private readonly System.Collections.Generic.Dictionary<uint, MirEffectNode> _objectPoisonEffects = new();

    private InventoryDialog _inventoryDialog;
    private CharacterDialog _characterDialog;
    private EditCharacterDialog _editCharacterDialog;
    private StorageDialog _storageDialog;
    private BeltDialog _beltDialog;
    public AutoPotionDialog AutoPotionBox { get; private set; }
    public DXItemCell[] BeltCells => _beltDialog?.Grid?.Cells ?? Array.Empty<DXItemCell>();
    private CurrencyDialog _currencyDialog;
    private FilterDropDialog _filterDropDialog;
    private BundleDialog _bundleDialog;
    private FortuneCheckerDialog _fortuneDialog;
    private LootBoxDialog _lootBoxDialog;
    private DungeonFinderDialog _dungeonFinderDialog;
    private TimerDialog _timerDialog;
    private CaptionDialog _captionDialog;
    private readonly Dictionary<int, ClientFortuneInfo> _fortunes = new();
    public string[] DropFilters { get; private set; } = Array.Empty<string>();

    public Stats PlayerStats => _playerStats;

    // 周围物体 (怪物/NPC/物品): ObjectID -> 渲染节点
    private readonly System.Collections.Generic.Dictionary<uint, ObjectRenderer> _objects = new();
    private readonly System.Collections.Generic.Dictionary<uint, PlayerRenderer> _otherPlayers = new();
    private readonly System.Collections.Generic.Dictionary<uint, MirRopeEffectNode> _tamingRopes = new();

    // SelectScene 传入的进游戏信息(StartGame 回包在场景创建前已处理完)
    public StartInformation StartInfo { get; set; }

    private uint _playerObjectID;
    private int _playerMapIndex;
    private int _playerInstanceIndex = -1;
    public int CurrentInstanceIndex => _playerInstanceIndex;
    public InstanceInfo CurrentInstanceInfo => Globals.InstanceInfoList?.Binding
        .FirstOrDefault(x => x.Index == _playerInstanceIndex);
    private readonly List<AutoPathRoute> _autoPathRoutes = new();
    private int _autoPathProgressMap = -1;
    private int _autoPathProgressPoint = -1;
    private bool _autoPathCancelPending;
    private PendingAutoPathMove _pendingAutoPathMove;

    private sealed class PendingAutoPathMove
    {
        public MirDirection Direction;
        public System.Drawing.Point Location;
        public int Distance;
        public TimeSpan Slow;
    }

    /// <summary>与原版 TryQueueAutoPathMove 一致：切图移动立即过渡，其余移动暂存。</summary>
    public static bool ShouldQueueAutoPathMove(bool autoPathActive, bool mapChanged)
        => autoPathActive && !mapChanged;

    public static bool ShouldCancelMapRightClick(bool hasSelectedItem, bool hasSelectedCurrency)
        => hasSelectedItem || hasSelectedCurrency;

    public static bool ShouldCancelGatheringForMapClick(bool altPressed, bool fishingActive, bool tamingActive)
        => !altPressed && (fishingActive || tamingActive);

    /// <summary>原版 MapControl.ProcessInput 的拾取前置状态闸门。</summary>
    public static bool CanSendMapPickup(bool observer, bool dead, bool paralyzed,
        bool contained, bool dragonRepulsed)
        => !observer && !dead && !paralyzed && !contained && !dragonRepulsed;

    /// <summary>原版拾取 250ms 节流（PickUpTime = Now + 250ms，鼠标与 Tab 共用）。</summary>
    public static bool CanSendPickUp(double nowMs, double nextMs) => nowMs >= nextMs;

    public static bool CanBeginItemDrop(DXItemCell source)
        => source?.Item != null && !source.Locked
            && source.GridType is GridType.Inventory or GridType.CompanionInventory;

    /// <summary>
    /// 物品交互期间地图不能继续驱动人物移动：包括背包中拿起物品、货币
    /// 选择状态，以及物品数量确认窗口已经打开但 SelectedCell 已被清掉的阶段。
    /// </summary>
    public static bool IsMovementBlockedByItemInteraction(bool selectedItem,
        bool selectedCurrency, bool itemWindowVisible)
        => selectedItem || selectedCurrency || itemWindowVisible;

    public static bool CanDropCurrency(bool observer, long selectedAmount, long amount)
        => !observer && selectedAmount > 0 && amount > 0 && amount <= selectedAmount;

    /// <summary>原版 Alt 采集：武器槽必须 FishingRod 且护甲槽必须 FishingRobe。</summary>
    public static bool IsFishingRig(ItemEffect? toolEffect, ItemEffect? armourEffect)
        => toolEffect == ItemEffect.FishingRod && armourEffect == ItemEffect.FishingRobe;

    /// <summary>
    /// 原版 Mining 块条件（MapControl.ProcessInput 1045-1071）：地图可挖矿、
    /// **武器槽** PickAxe、耐久 >0 或天生无耐久、矿点在界内且 Flag、
    /// 矿点与玩家相邻、未骑马。
    /// </summary>
    public static bool CanMineNow(bool canMine, ItemEffect? weaponEffect,
        int durability, int itemDurability, bool inBounds, bool cellFlag,
        bool adjacent, bool mounted)
        => canMine && weaponEffect == ItemEffect.PickAxe
            && (durability > 0 || itemDurability == 0)
            && inBounds && cellFlag && adjacent && !mounted;
    private System.Drawing.Point _playerLocation;
    private MirDirection _playerDirection;
    private Library.HorseType _playerHorse = Library.HorseType.None;
    private double _runCooldownUntilMs;
    private double _nextNpcCallMs;
    private uint _pendingNpcClickObjectId;
    private uint _groundItemPickupTargetId;
    private double _nextInspectMs;
    private PoisonType _playerPoison;
    private bool _abyssVisionActive;
    private double _abyssEffectTimer;
    private bool _observer;
    public bool IsObserver => _observer;
    public bool InSafeZone { get; private set; }
    public double CombatUntilMs { get; private set; }
    public double ItemReviveUntilMs { get; private set; }
    public double ReincarnationPillUntilMs { get; private set; }
    private readonly HashSet<string> _guildWars = new();

    // 玩家已学技能: MagicInfo -> ClientUserMagic (S.NewMagic 维护)
    public readonly System.Collections.Generic.Dictionary<MagicInfo, ClientUserMagic> UserMagics = new();
    private readonly HashSet<MagicType> _enabledToggleMagics = new();
    // 原版 GameScene.ToggleTime：切换/蓄力技能共用的防连点时间。
    public DateTime ToggleTime { get; private set; } = DateTime.MinValue;
    // 原版 GameScene.OutputTime：技能超距提示的防刷屏时间。
    private DateTime _magicTooFarAt = DateTime.MinValue;
    // 原版 MagicObject 的客户端目标记忆：单体魔法第一次命中后，
    // 后续施法优先复用该对象，不要求鼠标继续悬停在目标上。
    private uint _magicLockTargetObjectId;
    public int MagicBarSpellSet = 1;  // F1~F8 当前栏组 (1~4, 原版 Ctrl+1~4 切)
    public bool ShowMagicBarFrames { get; private set; } = true;
    private bool _autoRun;  // D 键切换自动跑步 (原版 AutoRun)
    private bool _rightClickDeTarget = true;
    public bool RightClickDeTarget => _rightClickDeTarget;
    private bool _escapeCloseAll;
    public bool EscapeCloseAll => _escapeCloseAll;

    // 原版 UserObject.MagicAction：移动中按下技能只入队，等动作结束
    // （NextActionTime 到期且 ActionQueue 清空）才在 ProcessInput 第二步
    // 真正发包。直接发包会被服务端 DelayedAction 队列挂到走完，效果等同
    // 但本地状态（MP/NextCast）提前扣减，且攻击分支不会被暂停。
    private C.Magic _pendingMagicPacket;
    private double _pendingMagicCastAtMs;
    // 与原版 GameScene.CanRun 一致：站立后第一次移动先走，收到移动回包后
    // 才允许下一次右键移动使用跑步距离/动作。
    private bool _canRun;
    private long _nextObjectHitOrder;

    // CallDeferred 缓冲
    private StartGameResult _pendingStartResult;
    private StartInformation _pendingStartInfo;
    private int _pendingMapIndex;
    private int _pendingInstanceIndex = -1;
    private bool _startGameShown;
    private bool _waitingStartupMap;
    private bool _hasPendingMapChanged;
    private MirDirection _pendingDir;
    private int _pendingX, _pendingY;
    private int _pendingDistance = 1;
    // 移动插值状态
    private System.Drawing.Point _moveFrom;
    private double _moveStartMs;
    // 每一段移动开始时固定的位移时间。位移不能读取随时变化的动画帧状态，
    // 否则旧配置或动作切换会把连续插值退化成按帧跳格。
    private double _moveDurationMs = 600.0;
    private int _moveFrameCount = 1;
    // 原版 UserObject.ServerTime 门控: 发完一个移动请求后锁住, 等服务端回包
    // (S.ObjectMove 确认 或 S.UserLocation 纠正)才解锁, 一次只发一个 C.Move。
    // 0 = 未锁定(可发包); >0 = 锁定到该时刻。用 double 而非 DateTime 避免
    // 每帧分配。锁定期内 MouseWalker 不再发新移动, 消除预判与回包重叠。
    private double _moveServerLockUntilMs;
    // 服务器回包通常早于贴图移动完成。不同外观的走路帧表有 600/800/1000ms
    // 等时长，不能只等回包，否则下一步会重置上一段插值，表现为回拉、飘移。
    private double _moveVisualLockUntilMs;
    private bool _runningTestStarted;
    private bool _interactionAuditStarted;
    private int _interactionInspectSent;
    private int _interactionInspectLeftSent;
    private int _interactionInspectReceived;
    private bool _interactionNpcSent;
    private double _interactionAuditDeadline;
    private bool _operationAuditStarted;
    private bool _operationAuditResponsePending;
    private bool _operationAuditLastSuccess;
    private int _operationAuditStage;
    private int _operationAuditSourceSlot = -1;
    private int _operationAuditTargetSlot = -1;
    private int _operationAuditEquipmentSlot = -1;
    private ClientUserItem _operationAuditOriginalEquipment;
    // --operation-audit-ext: 真实服务器矩阵 (B2 使用解锁 / B5 锁定 / C2 双戒指双手镯 / D1 腰带 / D3 自动药水 / E4 邮件)
    private bool _operationAuditExtStarted;
    private bool _operationAuditExtResponsePending;
    private bool _operationAuditExtLastSuccess;
    private int _operationAuditExtStage;
    private int _operationAuditExtSlotA = -1;
    private int _operationAuditExtSlotB = -1;
    private int _operationAuditExtFromSlot = -1;
    private int _operationAuditExtToSlot = -1;
    private ClientUserItem _operationAuditExtOriginalA;
    private ClientUserItem _operationAuditExtOriginalB;
    private int _auditMailCountBefore = -1;
    private int _auditMailIndex = -1;
    private bool _auditMailNewReceived;
    private int _auditMailSendCount = -1;
    // S17 伙伴食物 / S18 行会仓库 (C6/E3 实服端到端)
    private int _operationAuditExtCompanionSubStage;
    private int _operationAuditExtCompanionFoodCount = -1;
    private int _operationAuditExtCompanionHungerBefore = -1;
    private int _operationAuditExtCompanionHungerAfter = -1;
    private bool _operationAuditExtCompanionItemChanged;
    private int _operationAuditExtS17bRetries;
    private bool _operationAuditExtCompanionPass;
    private bool _operationAuditExtGuildPass;
    private int _operationAuditExtGuildSubStage;
    private long _auditGuildGoldBefore = -1;
    // S16 战斗在线实测: 真实 C.Attack 发包节拍与死亡目标保留 (D15)
    private ObjectRenderer _operationAuditExtCombatTarget;
    private uint _operationAuditExtCombatTargetId;
    private readonly List<double> _operationAuditExtAttackTimes = new();
    private bool _operationAuditExtCombatDied;
    private bool _operationAuditExtCombatKeptTarget;
    private bool _operationAuditExtCombatSelected;
    private bool _operationAuditExtCombatSecond;
    private bool _operationAuditExtSpawnAttempted;
    private int _operationAuditExtWalkSteps;
    private System.Drawing.Point _operationAuditExtLastWalkPos;
    // 删除回包不带物品 Index；记录发包时的 Index，防止旧回包删除同一槽位后来
    // 放入的新物品。没有本地待确认请求的删除仍按服务端权威事件处理。
    private readonly Dictionary<(GridType Grid, int Slot), long> _pendingItemDeletes = new();
    private readonly Dictionary<(GridType Grid, int Slot), long> _pendingItemUses = new();
    private bool _runningTestRightHeld;
    private readonly List<Action> _trackedEventUnsubscribers = new();
    // S.StartGame 后原版通常还会发送 S.MapChanged；不能用过短的固定延迟
    // 先把 StartInformation 中可能过期的地图画出来，造成错误场景闪现。
    private const double StartupMapFallbackDelaySeconds = 2.0;

    private void TrackEvent<T>(Action<Action<T>> subscribe, Action<Action<T>> unsubscribe,
        Action<T> handler)
    {
        subscribe(handler);
        _trackedEventUnsubscribers.Add(() => unsubscribe(handler));
    }

    private void TrackEvent(Action<Action> subscribe, Action<Action> unsubscribe, Action handler)
    {
        subscribe(handler);
        _trackedEventUnsubscribers.Add(() => unsubscribe(handler));
    }

    public override void _Ready()
    {
        ClientSettings.Load();
        KeyBindManager.Load();
        // Web 编辑器 overlay（UI/ui_overlay.json）：启动加载，之后 CreateHud 布局
        // 完成、每个窗口 _Ready（deferred）和 F12 热重载时应用。
        UiOverlay.Load();
        ClientSettings.ApplyDisplaySettings();
        ClientSettings.UpdateWindowTitle();
        ClientSettings.BindWindowTitle(GetViewport());
        ClientSettings.ApplyAudioSettings();
        SoundPlayback.Stop(SoundIndex.LoginScene);
        SoundPlayback.Stop(SoundIndex.SelectScene);
        Game = this;
        // GameScene 是铺满视口的 Control，默认 MouseFilter.Stop 会在 GUI 阶段
        // 捕获并 accept 所有鼠标事件，导致 _UnhandledInput 收不到地图点击
        // (Ctrl+右键观察、拾取、NPC、采矿全部静默失效)。地图交互全部在
        // _UnhandledInput 手动处理，不需要 GUI 命中；UI 窗口挂在独立 _uiLayer
        // 下，按各自 MouseFilter 参与 GUI。故本 Control 设为 Ignore 让鼠标穿透。
        MouseFilter = Control.MouseFilterEnum.Ignore;
        ShowMagicBarFrames = ClientSettings.ShowMagicBarFrames;
        _rightClickDeTarget = ClientSettings.RightClickDeTarget;
        _escapeCloseAll = ClientSettings.EscapeCloseAll;
        DrawWeather = ClientSettings.DrawWeather;
        QuestTrackerVisible = ClientSettings.QuestTrackerVisible;

        // 世界坐标使用原版 48x32 逻辑格，最终整体按 2 倍输出。
        // UI CanvasLayer 有独立缩放，不会被这里重复缩放。
        Scale = Vector2.One * WorldScale;

        _net = GetNodeOrNull<Network.NetworkManager>("/root/NetworkManager");

        _mapView = new MapView();
        AddChild(_mapView);
        // 原版 LLayer 在 DrawObjects()(地形+对象+天气粒子)之后最后绘制全屏
        // 光纹理: 夜晚环境光盖住包括动物/怪物/树在内的所有世界内容, 光源
        // 光斑再恢复亮度。光照层挂在独立 CanvasLayer(Layer=1, UI=10 之下),
        // 其 Transform 用 2x 与世界根节点 Scale 一致, 层内保持逻辑坐标。
        // CanvasLayer 按层索引排序、每层独立渲染: 整个世界(默认画布)先完整
        // 绘制, 该层再触发一次全新的 hint_screen_texture 整屏拷贝, 采样必然
        // 包含全部对象/特效。不能放在世界层末尾: Godot 只在第一个使用
        // screen_texture 的节点绘制前自动整屏拷贝, 地形 Blend 行或施法特效
        // (低 ZIndex)会劫持该拷贝点, 光照层随后采样到残缺画面并覆盖全屏,
        // 表现为移动/施法时所有贴图消失。
        var lightCanvas = new CanvasLayer
        {
            Layer = 1,
            Transform = new Transform2D(0f, Vector2.One * WorldScale, 0f, Vector2.Zero),
        };
        _lightLayer = new MapLightLayer { ZIndex = RenderOrder.LightOverlay };
        lightCanvas.AddChild(_lightLayer);
        AddChild(lightCanvas);
        _lightLayer.SetObjectSources(GetObjectLightSources);
        _lightLayer.SetPlayerState(() => new MapLightLayer.PlayerLightState(
            _player != null && _player.Dead,
            _playerPoison.HasFlag(PoisonType.Abyss),
            _player != null ? _player.Position + new Vector2(24f, 0f) : Vector2.Zero));
        // 旧端天气在 LLayer 环境光之前绘制，夜间天气也必须一起变暗。
        _weatherLayer = new MapWeatherLayer { ZIndex = RenderOrder.Particles };
        AddChild(_weatherLayer);
        _mouseWalker = new MouseWalker(_mapView, SendMouseMove,
        () => _combatController?.MouseObject != null
            && (_combatController.MouseObject.Type == ObjectRenderer.Kind.Item
                || (!_combatController.MouseObject.Dead
                    && !(_combatController.MouseObject.Type == ObjectRenderer.Kind.Monster
                        && !string.IsNullOrWhiteSpace(_combatController.MouseObject.PetOwner)))),
        GetRunSteps,
        SendTurn,
        () => IsMouseOverUi(),
        IsMovementCellBlocked,
        CanPlayerMove,
        CanPlayerTurn,
        BlockLeftMouseMovement,
        () => Input.IsKeyPressed(Key.Ctrl) && _combatController?.MouseObject?.Type == ObjectRenderer.Kind.Player,
        // ServerTime 门控: 锁定期内 MouseWalker 不发新移动, 等服务端回包。
        () => Godot.Time.GetTicksMsec() < _moveServerLockUntilMs
            || Godot.Time.GetTicksMsec() < _moveVisualLockUntilMs,
        // CanMove 阻挡判定的权威起点 = _playerLocation(原版 User.CurrentLocation)。
        () => _playerLocation);
        AddChild(_mouseWalker);
        _combatController = new CombatController(_mapView,
            () => _objects,
            () => _playerLocation,
            (dir, action, magic) =>
            {
                if (_net?.Connection?.Connected != true) return;
                if (!CanPlayerTurn()) return;
                if (_playerHorse != HorseType.None) return;
                if (_player != null)
                {
                    _player.Direction = dir;
                    if (action == MirAction.Attack) _player.PlayCombat(magic);
                }
                _canRun = false;
                MagicType attackMagic = magic != MagicType.None ? magic : _attackMagic;
                GD.Print($"[Combat] enqueue C.Attack action={action} magic={attackMagic} direction={dir}");
                if (AutoLoginArgs.OperationAuditExt && _operationAuditExtStage == 16)
                    _operationAuditExtAttackTimes.Add(Godot.Time.GetTicksMsec());
                _net.Connection.Enqueue(new C.Attack { Direction = dir, Action = action, AttackMagic = attackMagic });
            },
            (dir, distance) =>
            {
                if (_net?.Connection?.Connected != true) return;
                if (!CanPlayerMove()) return;
                // 点击目标后的追击必须和普通鼠标移动共用同一条预测/插值管线。
                // 旧实现这里只调用 BeginMove 后直接发 C.Move，服务端回包前
                // 角色只有走路贴图却没有移动时间轴，回包到达后又重新跳起插值，
                // 于是表现为卡顿、回拉和“飘”向目标。
                SendMouseMove(dir, distance, distance >= 2);
            },
            () => ComputeAttackIntervalMs(Globals.AttackDelay, PlayerStats[Stat.AttackSpeed],
                Globals.ASpeedRate, BagWeight > _playerStats[Stat.BagWeight],
                _playerPoison.HasFlag(PoisonType.Neutralize)),
            target => _player?.LibraryWeaponShape == Globals.ShurikenLibraryWeaponShape && _playerHorse == HorseType.None,
            (direction, target) =>
            {
                if (CanPlayerTurn()) _net?.Connection?.SendRangeAttack(direction, target);
            },
            IsMovementCellBlocked,
            () => _rightClickDeTarget,
            IsMouseOverUi,
            () => !IsFishingActive && !IsTamingActive,
            () => ReceiveChat("目标太远，无法投掷飞镖。", MessageType.Hint),
            () => _player?.LibraryWeaponShape == Globals.ShurikenLibraryWeaponShape,
            () => _playerHorse != HorseType.None,
            () => _player?.ElementalHurricane == true,
            () => _mouseWalker?.AutoRun == true,
            () => _pendingMagicPacket != null,
            SendTurn,
            ClearMagicLock,
            () => _moveFrameCount > 1,
            GetRunSteps,
            GetTargetRunSteps,
            StopPlayerActionForInput);
        AddChild(_combatController);
        _combatController.ZIndex = 200;  // 高亮框画在物体之上
        UpdateViewRange();

        _player = new PlayerRenderer();
        // 本地玩家和其他对象一样按脚底所在行排序，允许同一行的前景树木
        // 与悬崖前沿盖住人物，而普通中层地面仍在人物下面。
        _player.ZIndex = RenderOrder.ObjectAtFoot(_player.RenderY);
        _player.FrameChanged = (animation, frame, magic) => OnPlayerFrameChanged(_player, animation, frame, magic);
            _player.SoundCue = PlaySound;
        AddChild(_player);

        // M11: 窗口层 CanvasLayer, 所有窗口挂这里 (独立于 2D 世界, 永远最顶层)
        _uiLayer = new CanvasLayer();
        _uiLayer.Layer = 10;
        RefreshUiScale();
        AddChild(_uiLayer);

        // 坐标/状态文本: 原版无此常驻文本 (Godot 移植端调试用)。
        // 必须挂 _uiLayer (逻辑坐标, 随 UiScale 缩放), 否则挂在世界层
        // (根节点 Scale=2 且随相机滚动) 会漂移出屏并被视口裁切。
        // 正式 UI 默认隐藏，避免盖住 Buff/小地图；仅 DebugLabel 时显示。
        _statusLabel = new Label();
        _statusLabel.Position = new Vector2(10, 10);
        _statusLabel.Size = new Vector2(500, 80);
        _statusLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _statusLabel.Visible = ClientSettings.DebugLabel;
        _uiLayer.AddChild(_statusLabel);
        _debugLabel = new Label
        {
            Position = new Vector2(5, 5),
            Size = new Vector2(430, 36),
            ZIndex = 100,
            Visible = ClientSettings.DebugLabel,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _uiLayer.AddChild(_debugLabel);

        _statusWindow = new StatusWindow(); // 初始隐藏, F2 打开
        CreateHud();
        Resized += OnGameResized;
        LayoutHud(); // 立即执行首次同步布局

        // 黑幕遮罩层 (Layer 100 永远最顶层，防止首帧地图未载入、实体在 (0,0) 坍塌闪烁)
        _coverLayer = new CanvasLayer { Layer = 100 };
        _startupCoverRect = new ColorRect
        {
            Color = Colors.Black,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _startupCoverRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _coverLayer.AddChild(_startupCoverRect);
        AddChild(_coverLayer);

        CallDeferred(nameof(LayoutHud));
        if (OS.GetCmdlineUserArgs().Contains(UiAuditArgument))
            CallDeferred(nameof(RunUiLayoutAudit));

        if (_net?.Connection != null)
        {
        _net.Connection.StartGameResultEvent += OnStartGameResult;
        _net.Connection.GameLogoutEvent += OnGameLogout;
        _net.Connection.MapChangedEvent += OnMapChanged;
        _net.Connection.UserLocationEvent += OnUserLocation;
        _net.Connection.ObjectMoveEvent += OnObjectMove;
        _net.Connection.ObjectIdleEvent += OnObjectIdle;
        _net.Connection.ObjectShowEvent += OnObjectShow;
        _net.Connection.ObjectHideEvent += OnObjectHide;
        _net.Connection.ObjectNameColourEvent += OnObjectNameColour;
        _net.Connection.ObjectPetOwnerChangedEvent += OnObjectPetOwnerChanged;
        _net.Connection.ObjectLeveledEvent += OnObjectLeveled;
        _net.Connection.ObjectReviveEvent += OnObjectRevive;
        _net.Connection.ObjectStatsEvent += OnObjectStats;
        _net.Connection.ObjectHarvestedEvent += OnObjectHarvested;
        _net.Connection.CompanionShapeUpdateEvent += OnCompanionShapeUpdate;
        _net.Connection.SafeZoneChangedEvent += OnSafeZoneChanged;
        _net.Connection.CombatTimeEvent += OnCombatTime;
        _net.Connection.GuildChangedEvent += OnGuildChanged;
        _net.Connection.GuildWarStartedEvent += OnGuildWarStarted;
        _net.Connection.GuildWarFinishedEvent += OnGuildWarFinished;
        _net.Connection.GuildWarEvent += OnGuildWar;
        _net.Connection.MarriageInfoEvent += OnMarriageInfo;
        _net.Connection.MarriageRemoveRingEvent += OnMarriageRemoveRing;
        _net.Connection.MarriageMakeRingEvent += OnMarriageMakeRing;
        _net.Connection.MarriageOnlineChangedEvent += OnMarriageOnlineChanged;
        _net.Connection.MailSendEvent += OnMailSend;
        _net.Connection.MarketPlaceStoreBuyEvent += OnMarketPlaceStoreBuy;
        _net.Connection.MountFailedEvent += OnMountFailed;
        _net.Connection.TradeAddItemEvent += OnTradeAddItem;
        _net.Connection.TradeAddGoldEvent += OnTradeAddGold;
        _net.Connection.DataObjectLocationEvent += OnDataObjectLocation;
        _net.Connection.DataObjectRemoveEvent += OnDataObjectRemove;
        _net.Connection.DataObjectPlayerEvent += OnDataObjectPlayer;
        _net.Connection.DataObjectItemEvent += OnDataObjectItem;
        _net.Connection.StartObserverEvent += OnStartObserver;
        _net.Connection.NPCRefinementStoneEvent += OnNPCRefinementStone;
        _net.Connection.NPCRefineEvent += OnNPCRefine;
        _net.Connection.NPCMasterRefineEvent += OnNPCMasterRefine;
        _net.Connection.NPCAccessoryLevelUpEvent += OnNPCAccessoryLevelUp;
        _net.Connection.NPCAccessoryUpgradeEvent += OnNPCAccessoryUpgrade;
        _net.Connection.NPCAccessoryRefineEvent += OnNPCAccessoryRefine;
        _net.Connection.NPCWeaponCraftEvent += OnNPCWeaponCraft;
        _net.Connection.NPCRefineRetrieveEvent += OnNPCRefineRetrieve;
        _net.Connection.ItemAcessoryRefinedEvent += OnItemAcessoryRefined;
        _net.Connection.ReviveTimersEvent += OnReviveTimers;
        _net.Connection.ObservableSwitchEvent += OnObservableSwitch;
        _net.Connection.GuildCreateEvent += OnGuildCreate;
        _net.Connection.GuildKickEvent += OnGuildKick;
        _net.Connection.GuildTaxEvent += OnGuildTax;
        _net.Connection.GuildIncreaseMemberEvent += OnGuildIncreaseMember;
        _net.Connection.GuildIncreaseStorageEvent += OnGuildIncreaseStorage;
        _net.Connection.GuildInviteMemberEvent += OnGuildInviteMember;
        _net.Connection.GuildDayResetEvent += OnGuildDayReset;
        _net.Connection.InspectEvent += OnInspect;
        _net.Connection.ObjectMonsterEvent += OnObjectMonster;
        _net.Connection.ObjectPlayerEvent += OnObjectPlayer;
        _net.Connection.PlayerUpdateEvent += OnPlayerUpdate;
        _net.Connection.PlayerChangeUpdateEvent += OnPlayerChangeUpdate;
        _net.Connection.HelmetToggleEvent += OnHelmetToggle;
        _net.Connection.ObjectNPCEvent += OnObjectNPC;
        _net.Connection.ObjectItemEvent += OnObjectItem;
        _net.Connection.ChatEvent += OnChat;
        _net.Connection.GroupMemberEvent += OnGroupMember;
        _net.Connection.GroupRemoveEvent += OnGroupRemove;
        _net.Connection.GroupLFGEvent += OnGroupLfg;
        _net.Connection.GroupInviteEvent += OnGroupInvite;
        _net.Connection.GroupRequestEvent += OnGroupRequest;
        _net.Connection.GroupUpdateEvent += OnGroupUpdate;
        _net.Connection.GroupSwitchEvent += OnGroupSwitch;
        _net.Connection.MailListEvent += OnMailList;
        _net.Connection.MailNewEvent += OnMailNew;
        _net.Connection.MailDeleteEvent += OnMailDelete;
        _net.Connection.MailItemDeleteEvent += OnMailItemDelete;
        _net.Connection.FriendUpdateEvent += OnFriendUpdate;
        _net.Connection.FriendAddEvent += OnFriendAdd;
        _net.Connection.FriendRemoveEvent += OnFriendRemove;
        _net.Connection.BlockListEvent += OnBlockList;
        _net.Connection.BlockAddedEvent += OnBlockAdded;
        _net.Connection.BlockRemovedEvent += OnBlockRemoved;
        _net.Connection.DisciplineUpdateEvent += OnDisciplineUpdate;
        _net.Connection.DisciplineExperienceChangedEvent += OnDisciplineExperienceChanged;
        _net.Connection.MarriageInviteEvent += OnMarriageInvite;
        TrackEvent<S.TradeOpen>(h => _net.Connection.TradeOpenEvent += h,
            h => _net.Connection.TradeOpenEvent -= h,
            p => { if (p != null) _tradeDialog?.OpenTrade(p.Name); });
        TrackEvent<S.TradeRequest>(h => _net.Connection.TradeRequestEvent += h,
            h => _net.Connection.TradeRequestEvent -= h,
            p => _tradeDialog?.ShowRequest(p?.Name));
        TrackEvent<S.NPCRoll>(h => _net.Connection.NPCRollEvent += h,
            h => _net.Connection.NPCRollEvent -= h,
            p => _npcDialog?.ShowRollResult(p?.Type ?? 0, p?.Result ?? 0));
        TrackEvent(h => _net.Connection.TradeCloseEvent += h,
            h => _net.Connection.TradeCloseEvent -= h,
            () => _tradeDialog?.ClearTrade());
        TrackEvent(h => _net.Connection.DisconnectedEvent += h,
            h => _net.Connection.DisconnectedEvent -= h,
            () =>
        {
            _communicationDialog?.CancelPendingMailLinks();
            _communicationDialog?.MailSendResult();
            _npcDialog?.CancelPendingLinks();
            _npcSocketDialog?.Panel.CancelPending();
            _npcSocketCombineDialog?.Panel.CancelPending();
            _consignmentDialog?.CancelPendingLinks();
            _tradeDialog?.ClearTrade();
            _pendingItemDeletes.Clear();
            _pendingItemUses.Clear();
            _pendingNpcClickObjectId = 0;
            DXItemCell.SelectedCell = null;
            while (WindowManager.CloseTop()) { }
        });
        TrackEvent<ClientUserItem>(h => _net.Connection.TradeItemAddedEvent += h,
            h => _net.Connection.TradeItemAddedEvent -= h,
            item => _tradeDialog?.SetOtherItem(item));
        TrackEvent<long>(h => _net.Connection.TradeGoldAddedEvent += h,
            h => _net.Connection.TradeGoldAddedEvent -= h,
            gold => _tradeDialog?.SetOtherGold(gold));
        TrackEvent(h => _net.Connection.TradeUnlockEvent += h,
            h => _net.Connection.TradeUnlockEvent -= h,
            () => _tradeDialog?.Unlock());
        TrackEvent<S.Rankings>(h => _net.Connection.RankingsEvent += h,
            h => _net.Connection.RankingsEvent -= h,
            p => _rankingDialog?.ApplyRankings(p));
        _net.Connection.NPCResponseEvent += OnNPCResponse;
        TrackEvent(h => _net.Connection.NPCClosedEvent += h,
            h => _net.Connection.NPCClosedEvent -= h,
            () =>
        {
            CloseNPCSocketDialogs();
            if (_npcQuestListDialog != null) WindowManager.Close(_npcQuestListDialog);
            if (_npcQuestDialog != null) WindowManager.Close(_npcQuestDialog);
            if (_npcDialog != null) WindowManager.Close(_npcDialog);
        });
        TrackEvent<S.NPCRepair>(h => _net.Connection.NPCRepairEvent += h,
            h => _net.Connection.NPCRepairEvent -= h,
            packet => _npcDialog?.RepairResult(packet));
        TrackEvent<S.BundleOpen>(h => _net.Connection.BundleOpenEvent += h,
            h => _net.Connection.BundleOpenEvent -= h,
            p => _bundleDialog?.Open(p.Slot, p.Items));
        TrackEvent(h => _net.Connection.BundleCloseEvent += h,
            h => _net.Connection.BundleCloseEvent -= h,
            () => { if (_bundleDialog != null) WindowManager.Close(_bundleDialog); });
        _net.Connection.FortuneUpdateEvent += OnFortuneUpdate;
        TrackEvent<S.LootBoxOpen>(h => _net.Connection.LootBoxOpenEvent += h,
            h => _net.Connection.LootBoxOpenEvent -= h,
            p => _lootBoxDialog?.Open(p.Slot, p.Items));
        TrackEvent(h => _net.Connection.LootBoxCloseEvent += h,
            h => _net.Connection.LootBoxCloseEvent -= h,
            () => { if (_lootBoxDialog != null) WindowManager.Close(_lootBoxDialog); });
        TrackEvent<S.NPCSocketItem>(h => _net.Connection.NPCSocketItemEvent += h,
            h => _net.Connection.NPCSocketItemEvent -= h,
            p => _npcSocketDialog?.Result(p));
        TrackEvent<S.NPCSocketCombine>(h => _net.Connection.NPCSocketCombineEvent += h,
            h => _net.Connection.NPCSocketCombineEvent -= h,
            p => _npcSocketCombineDialog?.Result(p));
        TrackEvent<S.SetTimer>(h => _net.Connection.SetTimerEvent += h,
            h => _net.Connection.SetTimerEvent -= h,
            p => _timerDialog?.AddTimer(p));
        TrackEvent<S.MarketPlaceConsign>(h => _net.Connection.MarketPlaceConsignEvent += h,
            h => _net.Connection.MarketPlaceConsignEvent -= h,
            p => _consignmentDialog?.AddConsignments(p?.Consignments));
        TrackEvent<S.MarketPlaceSearch>(h => _net.Connection.MarketPlaceSearchEvent += h,
            h => _net.Connection.MarketPlaceSearchEvent -= h,
            p => _consignmentDialog?.ApplySearch(p?.Count ?? 0, p?.Results));
        TrackEvent<S.MarketPlaceSearchCount>(h => _net.Connection.MarketPlaceSearchCountEvent += h,
            h => _net.Connection.MarketPlaceSearchCountEvent -= h,
            p => _consignmentDialog?.ApplySearchCount(p?.Count ?? 0));
        TrackEvent<S.MarketPlaceSearchIndex>(h => _net.Connection.MarketPlaceSearchIndexEvent += h,
            h => _net.Connection.MarketPlaceSearchIndexEvent -= h,
            p => _consignmentDialog?.ApplySearchIndex(p?.Index ?? -1, p?.Result));
        TrackEvent<S.MarketPlaceBuy>(h => _net.Connection.MarketPlaceBuyEvent += h,
            h => _net.Connection.MarketPlaceBuyEvent -= h,
            p => _consignmentDialog?.ApplyBuy(p?.Index ?? -1, p?.Count ?? 0, p?.Success == true));
        TrackEvent<S.MarketPlaceConsignChanged>(h => _net.Connection.MarketPlaceConsignChangedEvent += h,
            h => _net.Connection.MarketPlaceConsignChangedEvent -= h,
            p => _consignmentDialog?.ApplyConsignChanged(p?.Index ?? -1, p?.Count ?? 0));
        TrackEvent<S.MarketPlaceHistory>(h => _net.Connection.MarketPlaceHistoryEvent += h,
            h => _net.Connection.MarketPlaceHistoryEvent -= h,
            p => _marketHistoryDialog?.Apply(p?.Index ?? -1, p?.Display ?? -1, p?.SaleCount ?? 0, p?.LastPrice ?? 0, p?.AveragePrice ?? 0));
        _net.Connection.ObjectFishingEvent += OnObjectFishing;
        TrackEvent<S.FishingStats>(h => _net.Connection.FishingStatsEvent += h,
            h => _net.Connection.FishingStatsEvent -= h,
            p => _fishingCatchDialog?.UpdateStats(p));
        _net.Connection.GameStoreDataEvent += OnGameStoreData;
        _net.Connection.GameStoreTopItemsEvent += OnGameStoreTopItems;
        _net.Connection.GameStoreFavouriteChangedEvent += OnGameStoreFavouriteChanged;
        _net.Connection.GameStoreGiftEvent += OnGameStoreGift;
        _net.Connection.AutoPathChangedEvent += OnAutoPathChanged;
        _net.Connection.ObjectTamingEvent += OnObjectTaming;
        _net.Connection.JoinInstanceEvent += OnJoinInstance;
        _net.Connection.NewMagicEvent += OnNewMagic;
        _net.Connection.MagicLeveledEvent += OnMagicLeveled;
        _net.Connection.MagicCooldownEvent += OnMagicCooldown;
        _net.Connection.MagicToggleEvent += OnMagicToggle;
        _net.Connection.ObjectRemoveEvent += OnObjectRemove;
        _net.Connection.ObjectTurnEvent += OnObjectTurn;
        _net.Connection.ObjectHarvestEvent += OnObjectHarvest;
        _net.Connection.ObjectMountEvent += OnObjectMount;
        _net.Connection.ObjectDashEvent += OnObjectDash;
        _net.Connection.ObjectPushedEvent += OnObjectPushed;
        _net.Connection.ObjectMiningEvent += OnObjectMining;
        _net.Connection.ObjectAttackEvent += OnObjectAttack;
        _net.Connection.ObjectRangeAttackEvent += OnObjectRangeAttack;
        _net.Connection.ObjectMagicEvent += OnObjectMagic;
        _net.Connection.ObjectProjectileEvent += OnObjectProjectile;
        _net.Connection.ObjectSpellEvent += OnObjectSpell;
        _net.Connection.ObjectSpellChangedEvent += OnObjectSpellChanged;
        _net.Connection.ObjectEffectEvent += OnObjectEffect;
        _net.Connection.MapEffectEvent += OnMapEffect;
        _net.Connection.ObjectBuffAddEvent += OnObjectBuffAdd;
        _net.Connection.ObjectBuffRemoveEvent += OnObjectBuffRemove;
        _net.Connection.ObjectPoisonEvent += OnObjectPoison;
        _net.Connection.HealthChangedEvent += OnHealthChanged;
        _net.Connection.DataObjectHealthManaEvent += OnDataObjectHealthMana;
        _net.Connection.DataObjectMaxHealthManaEvent += OnDataObjectMaxHealthMana;
        _net.Connection.DataObjectMonsterEvent += OnDataObjectMonsterInfo;
        _net.Connection.ObjectDiedEvent += OnObjectDied;
        _net.Connection.ObjectStruckEvent += OnObjectStruck;
        _net.Connection.StatsUpdateEvent += OnStatsUpdate;
        _net.Connection.DayTimeChangedEvent += OnDayTimeChanged;
        _net.Connection.TimeOfDayChangedEvent += OnTimeOfDayChanged;
        _net.Connection.LevelChangedEvent += OnLevelChanged;
        _net.Connection.GainedExperienceEvent += OnGainedExperience;
        _net.Connection.InformMaxExperienceEvent += OnInformMaxExperience;
        _net.Connection.ManaChangedEvent += OnManaChanged;
        _net.Connection.FocusChangedEvent += OnFocusChanged;
        _net.Connection.BuffAddEvent += OnBuffAdd;
        _net.Connection.BuffRemoveEvent += OnBuffRemove;
        _net.Connection.BuffChangedEvent += OnBuffChanged;
        _net.Connection.BuffTimeEvent += OnBuffTime;
        _net.Connection.BuffPausedEvent += OnBuffPaused;
        _net.Connection.AttackModeChangedEvent += OnAttackModeChanged;
        _net.Connection.PetModeChangedEvent += OnPetModeChanged;
        // M9 物品系统
        _net.Connection.ItemsGainedEvent += OnItemsGained;
        _net.Connection.ItemMoveEvent += OnItemMove;
        _net.Connection.ItemSortEvent += OnItemSort;
        _net.Connection.ItemSplitEvent += OnItemSplit;
        _net.Connection.ItemDeleteEvent += OnItemDelete;
        _net.Connection.ItemLockEvent += OnItemLock;
        _net.Connection.ItemUseDelayEvent += OnItemUseDelay;
        _net.Connection.ItemChangedEvent += OnItemChanged;
        _net.Connection.ItemStatsChangedEvent += OnItemStatsChanged;
        _net.Connection.ItemStatsRefreshedEvent += OnItemStatsRefreshed;
        _net.Connection.ItemDurabilityEvent += OnItemDurability;
        _net.Connection.ItemExperienceEvent += OnItemExperience;
        _net.Connection.ItemsChangedEvent += OnItemsChanged;
        TrackEvent<S.CompanionAdopt>(h => _net.Connection.CompanionAdoptEvent += h,
            h => _net.Connection.CompanionAdoptEvent -= h,
            p => { Companion = p?.UserCompanion; if (Companion != null && !Companions.Exists(x => x.Index == Companion.Index)) Companions.Add(Companion); _companionDialog?.ApplyCompanion(Companion); _npcCompanionStorageDialog?.AddCompanion(Companion); });
        TrackEvent<S.CompanionUpdate>(h => _net.Connection.CompanionUpdateEvent += h,
            h => _net.Connection.CompanionUpdateEvent -= h,
            p =>
            {
                // 原版 CConnection.Process(S.CompanionUpdate) 只刷新 CompanionBox 标签,
                // 不做数组同步; ApplyCompanion 的 Clear+Copy 会抹掉同帧已写入的协议状态。
                if (Companion != null) { Companion.Level = p.Level; Companion.Experience = p.Experience; Companion.Hunger = p.Hunger; _companionDialog?.RefreshCompanionStats(Companion); }
                // S17b: S.ItemChanged 先入队 (服务端 6298), S.CompanionUpdate 后发 -> 双包齐后 Continue
                if (AutoLoginArgs.OperationAuditExt && _operationAuditExtStage == 17)
                {
                    GD.Print($"[OperationAuditExt] S17b raw CompanionUpdate level={p.Level} hunger={p.Hunger} itemChanged={_operationAuditExtCompanionItemChanged}");
                    _operationAuditExtCompanionHungerAfter = Companion?.Hunger ?? -1;
                    if (_operationAuditExtCompanionItemChanged)
                    {
                        _operationAuditExtResponsePending = true;
                        CallDeferred(nameof(ContinueOperationAuditExt));
                    }
                }
            });
        TrackEvent<S.CompanionItemsGained>(h => _net.Connection.CompanionItemsGainedEvent += h,
            h => _net.Connection.CompanionItemsGainedEvent -= h,
            p =>
            {
                var items = p?.Items ?? new List<ClientUserItem>();
                MarkGainedItems(items, true);
                AddCompanionItems(items);
            });
        TrackEvent<S.CompanionWeightUpdate>(h => _net.Connection.CompanionWeightUpdateEvent += h,
            h => _net.Connection.CompanionWeightUpdateEvent -= h,
            p => _companionDialog?.ApplyWeight(p.BagWeight, p.MaxBagWeight, p.InventorySize));
        TrackEvent<S.CompanionSkillUpdate>(h => _net.Connection.CompanionSkillUpdateEvent += h,
            h => _net.Connection.CompanionSkillUpdateEvent -= h,
            p => _companionDialog?.ApplySkills(p.Level3, p.Level5, p.Level7, p.Level10, p.Level11, p.Level13, p.Level15));
        _net.Connection.CompanionRetrieveEvent += OnCompanionRetrieve;
        _net.Connection.CompanionReleaseEvent += OnCompanionRelease;
        TrackEvent<S.CompanionStore>(h => _net.Connection.CompanionStoreEvent += h,
            h => _net.Connection.CompanionStoreEvent -= h,
            p =>
            {
                if (Companion != null)
                {
                    SyncCompanionItemList();
                    Companion.CharacterName = null;
                }
                Companion = null;
                _companionDialog?.ApplyCompanion(null);
                _npcCompanionStorageDialog?.Refresh();
                ReceiveChat(Lang.GameCompanionLabel, MessageType.System);
            });
        TrackEvent<S.CompanionUnlock>(h => _net.Connection.CompanionUnlockEvent += h,
            h => _net.Connection.CompanionUnlockEvent -= h,
            p => ReceiveChat(string.Format(Lang.GameCompanionLabel2, p?.Index ?? 0), MessageType.System));
        TrackEvent<S.GuildNewItem>(h => _net.Connection.GuildNewItemEvent += h,
            h => _net.Connection.GuildNewItemEvent -= h,
            p => _guildDialog?.SetGuildItem(p.Slot, p.Item));
        TrackEvent<S.GuildGetItem>(h => _net.Connection.GuildGetItemEvent += h,
            h => _net.Connection.GuildGetItemEvent -= h,
            p => _guildDialog?.SetGuildItem(p.Slot, p.Item));
        _net.Connection.GuildInfoEvent += OnGuildInfo;
        _net.Connection.GuildNoticeChangedEvent += OnGuildNoticeChanged;
        _net.Connection.GuildUpdateEvent += OnGuildUpdate;
        _net.Connection.GuildMemberOfflineEvent += OnGuildMemberOffline;
        _net.Connection.GuildMemberOnlineEvent += OnGuildMemberOnline;
        _net.Connection.GuildMemberContributionEvent += OnGuildMemberContribution;
        _net.Connection.GuildFundsChangedEvent += OnGuildFundsChanged;
        _net.Connection.GuildInviteEvent += OnGuildInvite;
        _net.Connection.QuestChangedEvent += OnQuestChanged;
        _net.Connection.QuestCancelledEvent += OnQuestCancelled;
        _net.Connection.GuildCastleInfoEvent += OnGuildCastleInfo;
        _net.Connection.GuildConquestDateEvent += OnGuildConquestDate;
        _net.Connection.GuildConquestStartedEvent += OnGuildConquestStarted;
        _net.Connection.GuildConquestFinishedEvent += OnGuildConquestFinished;
        _net.Connection.RefineListEvent += OnRefineList;
        _net.Connection.CurrencyChangedEvent += OnCurrencyChanged;
        _net.Connection.WeightUpdateEvent += OnWeightUpdate;
        _net.Connection.StorageSizeEvent += OnStorageSize;
        }

        // M9: 鼠标跟随物品图标 + 悬浮提示 (窗口层最顶)
        // M9: 鼠标跟随物品图标 (原版拖物品时图标跟随鼠标)
        _mouseItemIcon = new DXImageControl
        {
            FixedSize = true,
            Size = new Vector2I(32, 32),
            ZIndex = 501,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _uiLayer.AddChild(_mouseItemIcon);

        _mouseItemLabel = new DXLabel
        {
            TextColour = Colors.White,
            DrawOutline = true,
            OutlineColour = Colors.Black,
            AutoSize = true,
            ZIndex = 500,
            Visible = false,
        };
        _uiLayer.AddChild(_mouseItemLabel);

        _hoverLabel = new DXLabel
        {
            TextColour = new Color(0.9f, 0.9f, 0.5f),
            DrawOutline = true,
            OutlineColour = Colors.Black,
            AutoSize = true,
            ZIndex = 500,
            Visible = false,
            // 旧版 ItemLabelBuilder.CreateLabel 的悬浮框：深棕半透明底 + 金棕边框。
            // 之前只有文字、没有框，地图/UI 背景下文字看不清。
            BackColour = new Color(18f / 255f, 15f / 255f, 8f / 255f, 230f / 255f),
            Border = true,
            BorderColour = new Color(105f / 255f, 95f / 255f, 62f / 255f),
            TextPadding = new Vector2I(6, 4),
        };
        _uiLayer.AddChild(_hoverLabel);

        // StartGame 突发包在 _Ready 前已被 Process 处理(订阅未生效), 一次性排空积压队列
        DrainPendingObjects();
        _net?.Connection?.StopPendingPacketBuffering();

        if (StartInfo != null)
        {
            // 主线程内直接处理, 无需 CallDeferred
            _pendingStartResult = StartGameResult.Success;
            _pendingStartInfo = StartInfo;
            ShowStartGameResult();
        }
        else
        {
            _statusLabel.Text = Lang.GameGameLabel;
        }
    }

    /// <summary>
    /// 原版 AttemptAction(Standing) 的 C.Turn 出口（MouseWalker 转向 +
    /// CombatController 追击被阻挡时的原地转向共用）。
    /// 原版 C.Turn 对本地玩家无回包：PlayerObject.Turn 只向其他玩家
    /// Broadcast(S.ObjectTurn)（拒绝才回 S.UserLocation）。面向因此必须
    /// 在发包同时本地应用，否则服务端从不校正本地朝向（原版在
    /// AttemptAction(Standing) 里同步设置 Direction）。
    /// </summary>
    private void SendTurn(MirDirection dir)
    {
        if (!CanPlayerTurn() || _player == null) return;
        if (!ShouldSendTurn(_player.Direction, dir)) return;
        _playerDirection = dir;
        _player.Direction = dir;
        if (AutoLoginArgs.OfflineMovementTest)
        {
            GD.Print($"[OfflineMove] TURN direction={dir}");
            return;
        }
        if (_net?.Connection?.Connected != true) return;
        _net.Connection.Enqueue(new C.Turn { Direction = dir });
    }

    /// <summary>
    /// 原版 UserObject.SetAction Attack/RangeAttack 间隔（UserObject.cs:638-653）：
    /// base = max(800, AttackDelay - AttackSpeed*ASpeedRate)；超重或 Neutralize
    /// 时 AttackTime 再叠加一次 base（等效间隔 x2）。Godot 之前的
    /// max(250, ...) 既没有 800 地板也没有超重翻倍，高攻速下最多快 ~3 倍。
    /// </summary>
    public static double ComputeAttackIntervalMs(double attackDelayBase, int attackSpeed, int aspeedRate,
        bool overweight, bool neutralize)
    {
        double delay = Math.Max(800.0, attackDelayBase - attackSpeed * aspeedRate);
        if (overweight || neutralize) delay *= 2.0;
        return delay;
    }

    /// <summary>
    /// 原版 UserObject.SetAction Mining 间隔：超重时再额外叠加一次 base
    /// （等效 x3），Neutralize 叠加一次（x2）；两者同时按超重 x3。
    /// </summary>
    public static double ComputeMiningIntervalMs(double attackDelayBase, int attackSpeed, int aspeedRate,
        bool overweight, bool neutralize)
    {
        double delay = Math.Max(800.0, attackDelayBase - attackSpeed * aspeedRate);
        if (overweight) delay *= 3.0;
        else if (neutralize) delay *= 2.0;
        return delay;
    }

    public static bool ShouldSendTurn(MirDirection current, MirDirection requested)
        => current != requested;

    /// <summary>当前移动动画集合（原版 AttemptAction(Moving) 期间）。
    /// 用于把 C.Magic 延迟到走完（原版 MagicAction 队列）。</summary>
    public static bool IsWalkAnimation(MirAnimation animation)
        => animation is MirAnimation.Walking or MirAnimation.Running
            or MirAnimation.HorseWalking or MirAnimation.HorseRunning
            or MirAnimation.CreepWalkSlow or MirAnimation.CreepWalkFast;

    public override void _ExitTree()
    {
        if (_net?.Connection != null)
        {
            foreach (var unsubscribe in _trackedEventUnsubscribers)
                unsubscribe();
            _trackedEventUnsubscribers.Clear();

            _net.Connection.StartGameResultEvent -= OnStartGameResult;
            _net.Connection.GameLogoutEvent -= OnGameLogout;
            _net.Connection.MapChangedEvent -= OnMapChanged;
            _net.Connection.UserLocationEvent -= OnUserLocation;
            _net.Connection.ObjectMoveEvent -= OnObjectMove;
        _net.Connection.ObjectMonsterEvent -= OnObjectMonster;
        _net.Connection.ObjectPlayerEvent -= OnObjectPlayer;
            _net.Connection.ObjectNPCEvent -= OnObjectNPC;
            _net.Connection.NPCResponseEvent -= OnNPCResponse;
            _net.Connection.ChatEvent -= OnChat;
            _net.Connection.ObjectItemEvent -= OnObjectItem;
            _net.Connection.ObjectRemoveEvent -= OnObjectRemove;
        _net.Connection.ObjectTurnEvent -= OnObjectTurn;
            _net.Connection.ObjectHarvestEvent -= OnObjectHarvest;
            _net.Connection.ObjectMountEvent -= OnObjectMount;
            _net.Connection.ObjectDashEvent -= OnObjectDash;
            _net.Connection.ObjectPushedEvent -= OnObjectPushed;
            _net.Connection.ObjectMiningEvent -= OnObjectMining;
        _net.Connection.ObjectAttackEvent -= OnObjectAttack;
            _net.Connection.ObjectRangeAttackEvent -= OnObjectRangeAttack;
        _net.Connection.ObjectMagicEvent -= OnObjectMagic;
            _net.Connection.ObjectProjectileEvent -= OnObjectProjectile;
            _net.Connection.ObjectSpellEvent -= OnObjectSpell;
            _net.Connection.ObjectSpellChangedEvent -= OnObjectSpellChanged;
            _net.Connection.ObjectEffectEvent -= OnObjectEffect;
            _net.Connection.MapEffectEvent -= OnMapEffect;
            _net.Connection.ObjectBuffAddEvent -= OnObjectBuffAdd;
            _net.Connection.ObjectBuffRemoveEvent -= OnObjectBuffRemove;
            _net.Connection.ObjectPoisonEvent -= OnObjectPoison;
            _net.Connection.HealthChangedEvent -= OnHealthChanged;
            _net.Connection.DataObjectHealthManaEvent -= OnDataObjectHealthMana;
            _net.Connection.DataObjectMaxHealthManaEvent -= OnDataObjectMaxHealthMana;
            _net.Connection.DataObjectMonsterEvent -= OnDataObjectMonsterInfo;
            _net.Connection.ObjectDiedEvent -= OnObjectDied;
            _net.Connection.ObjectStruckEvent -= OnObjectStruck;
            _net.Connection.StatsUpdateEvent -= OnStatsUpdate;
            _net.Connection.DayTimeChangedEvent -= OnDayTimeChanged;
            _net.Connection.TimeOfDayChangedEvent -= OnTimeOfDayChanged;
            _net.Connection.LevelChangedEvent -= OnLevelChanged;
            _net.Connection.GainedExperienceEvent -= OnGainedExperience;
            _net.Connection.InformMaxExperienceEvent -= OnInformMaxExperience;
            _net.Connection.ManaChangedEvent -= OnManaChanged;
            _net.Connection.FocusChangedEvent -= OnFocusChanged;
            _net.Connection.BuffAddEvent -= OnBuffAdd;
            _net.Connection.BuffRemoveEvent -= OnBuffRemove;
            _net.Connection.BuffChangedEvent -= OnBuffChanged;
            _net.Connection.BuffTimeEvent -= OnBuffTime;
            _net.Connection.BuffPausedEvent -= OnBuffPaused;
            _net.Connection.AttackModeChangedEvent -= OnAttackModeChanged;
            _net.Connection.PetModeChangedEvent -= OnPetModeChanged;
            // M9 物品系统
            _net.Connection.ItemsGainedEvent -= OnItemsGained;
            _net.Connection.ItemMoveEvent -= OnItemMove;
            _net.Connection.ItemSortEvent -= OnItemSort;
            _net.Connection.ItemSplitEvent -= OnItemSplit;
            _net.Connection.ItemDeleteEvent -= OnItemDelete;
            _net.Connection.ItemLockEvent -= OnItemLock;
            _net.Connection.ItemUseDelayEvent -= OnItemUseDelay;
            _net.Connection.ItemChangedEvent -= OnItemChanged;
            _net.Connection.ItemStatsChangedEvent -= OnItemStatsChanged;
            _net.Connection.ItemStatsRefreshedEvent -= OnItemStatsRefreshed;
            _net.Connection.ItemDurabilityEvent -= OnItemDurability;
            _net.Connection.ItemExperienceEvent -= OnItemExperience;
            _net.Connection.ItemsChangedEvent -= OnItemsChanged;
            _net.Connection.CurrencyChangedEvent -= OnCurrencyChanged;
            _net.Connection.WeightUpdateEvent -= OnWeightUpdate;
            _net.Connection.StorageSizeEvent -= OnStorageSize;
            _net.Connection.GuildInfoEvent -= OnGuildInfo;
            _net.Connection.GuildNoticeChangedEvent -= OnGuildNoticeChanged;
            _net.Connection.GuildUpdateEvent -= OnGuildUpdate;
            _net.Connection.GuildMemberOfflineEvent -= OnGuildMemberOffline;
            _net.Connection.GuildMemberOnlineEvent -= OnGuildMemberOnline;
            _net.Connection.GuildMemberContributionEvent -= OnGuildMemberContribution;
            _net.Connection.GuildFundsChangedEvent -= OnGuildFundsChanged;
            _net.Connection.GuildInviteEvent -= OnGuildInvite;
            _net.Connection.GuildCreateEvent -= OnGuildCreate;
            _net.Connection.GuildKickEvent -= OnGuildKick;
            _net.Connection.GuildTaxEvent -= OnGuildTax;
            _net.Connection.GuildIncreaseMemberEvent -= OnGuildIncreaseMember;
            _net.Connection.GuildIncreaseStorageEvent -= OnGuildIncreaseStorage;
            _net.Connection.GuildInviteMemberEvent -= OnGuildInviteMember;
            _net.Connection.GuildDayResetEvent -= OnGuildDayReset;
            _net.Connection.QuestChangedEvent -= OnQuestChanged;
            _net.Connection.QuestCancelledEvent -= OnQuestCancelled;
            _net.Connection.GuildCastleInfoEvent -= OnGuildCastleInfo;
            _net.Connection.GuildConquestDateEvent -= OnGuildConquestDate;
            _net.Connection.GuildConquestStartedEvent -= OnGuildConquestStarted;
            _net.Connection.GuildConquestFinishedEvent -= OnGuildConquestFinished;
            _net.Connection.RefineListEvent -= OnRefineList;
            _net.Connection.GroupMemberEvent -= OnGroupMember;
            _net.Connection.GroupRemoveEvent -= OnGroupRemove;
            _net.Connection.GroupLFGEvent -= OnGroupLfg;
            _net.Connection.GroupInviteEvent -= OnGroupInvite;
            _net.Connection.GroupRequestEvent -= OnGroupRequest;
            _net.Connection.GroupUpdateEvent -= OnGroupUpdate;
            _net.Connection.GroupSwitchEvent -= OnGroupSwitch;
            _net.Connection.MailListEvent -= OnMailList;
            _net.Connection.MailNewEvent -= OnMailNew;
            _net.Connection.MailDeleteEvent -= OnMailDelete;
            _net.Connection.FriendUpdateEvent -= OnFriendUpdate;
            _net.Connection.FriendAddEvent -= OnFriendAdd;
            _net.Connection.FriendRemoveEvent -= OnFriendRemove;
            _net.Connection.BlockListEvent -= OnBlockList;
            _net.Connection.MailItemDeleteEvent -= OnMailItemDelete;
            _net.Connection.BlockAddedEvent -= OnBlockAdded;
            _net.Connection.BlockRemovedEvent -= OnBlockRemoved;
            _net.Connection.DisciplineUpdateEvent -= OnDisciplineUpdate;
            _net.Connection.DisciplineExperienceChangedEvent -= OnDisciplineExperienceChanged;
            _net.Connection.MarriageInviteEvent -= OnMarriageInvite;
            _net.Connection.GameStoreDataEvent -= OnGameStoreData;
            _net.Connection.GameStoreTopItemsEvent -= OnGameStoreTopItems;
            _net.Connection.GameStoreFavouriteChangedEvent -= OnGameStoreFavouriteChanged;
            _net.Connection.GameStoreGiftEvent -= OnGameStoreGift;
            _net.Connection.FortuneUpdateEvent -= OnFortuneUpdate;
            _net.Connection.ObjectFishingEvent -= OnObjectFishing;
            _net.Connection.ObjectTamingEvent -= OnObjectTaming;
            _net.Connection.AutoPathChangedEvent -= OnAutoPathChanged;
            _net.Connection.NewMagicEvent -= OnNewMagic;
            _net.Connection.CompanionRetrieveEvent -= OnCompanionRetrieve;
            _net.Connection.CompanionReleaseEvent -= OnCompanionRelease;
            _net.Connection.JoinInstanceEvent -= OnJoinInstance;
            _net.Connection.UserMilestonesEvent -= OnUserMilestones;
            _net.Connection.MilestoneEarnedEvent -= OnMilestoneEarned;
            _net.Connection.ObjectIdleEvent -= OnObjectIdle;
            _net.Connection.ObjectShowEvent -= OnObjectShow;
            _net.Connection.ObjectHideEvent -= OnObjectHide;
            _net.Connection.ObjectNameColourEvent -= OnObjectNameColour;
            _net.Connection.ObjectPetOwnerChangedEvent -= OnObjectPetOwnerChanged;
            _net.Connection.ObjectLeveledEvent -= OnObjectLeveled;
            _net.Connection.ObjectReviveEvent -= OnObjectRevive;
            _net.Connection.ObjectStatsEvent -= OnObjectStats;
            _net.Connection.ObjectHarvestedEvent -= OnObjectHarvested;
            _net.Connection.CompanionShapeUpdateEvent -= OnCompanionShapeUpdate;
            _net.Connection.SafeZoneChangedEvent -= OnSafeZoneChanged;
            _net.Connection.CombatTimeEvent -= OnCombatTime;
            _net.Connection.GuildChangedEvent -= OnGuildChanged;
            _net.Connection.GuildWarStartedEvent -= OnGuildWarStarted;
            _net.Connection.GuildWarFinishedEvent -= OnGuildWarFinished;
            _net.Connection.GuildWarEvent -= OnGuildWar;
            _net.Connection.MarriageInfoEvent -= OnMarriageInfo;
            _net.Connection.MarriageRemoveRingEvent -= OnMarriageRemoveRing;
            _net.Connection.MarriageMakeRingEvent -= OnMarriageMakeRing;
            _net.Connection.MarriageOnlineChangedEvent -= OnMarriageOnlineChanged;
            _net.Connection.MailSendEvent -= OnMailSend;
            _net.Connection.MarketPlaceStoreBuyEvent -= OnMarketPlaceStoreBuy;
            _net.Connection.MountFailedEvent -= OnMountFailed;
            _net.Connection.TradeAddItemEvent -= OnTradeAddItem;
            _net.Connection.TradeAddGoldEvent -= OnTradeAddGold;
            _net.Connection.DataObjectLocationEvent -= OnDataObjectLocation;
            _net.Connection.DataObjectRemoveEvent -= OnDataObjectRemove;
            _net.Connection.DataObjectPlayerEvent -= OnDataObjectPlayer;
            _net.Connection.DataObjectItemEvent -= OnDataObjectItem;
            _net.Connection.StartObserverEvent -= OnStartObserver;
            _net.Connection.NPCRefinementStoneEvent -= OnNPCRefinementStone;
            _net.Connection.NPCRefineEvent -= OnNPCRefine;
            _net.Connection.NPCMasterRefineEvent -= OnNPCMasterRefine;
            _net.Connection.NPCAccessoryLevelUpEvent -= OnNPCAccessoryLevelUp;
            _net.Connection.NPCAccessoryUpgradeEvent -= OnNPCAccessoryUpgrade;
            _net.Connection.NPCAccessoryRefineEvent -= OnNPCAccessoryRefine;
            _net.Connection.NPCWeaponCraftEvent -= OnNPCWeaponCraft;
            _net.Connection.NPCRefineRetrieveEvent -= OnNPCRefineRetrieve;
            _net.Connection.ItemAcessoryRefinedEvent -= OnItemAcessoryRefined;
            _net.Connection.ReviveTimersEvent -= OnReviveTimers;
            _net.Connection.ObservableSwitchEvent -= OnObservableSwitch;
            _net.Connection.PlayerUpdateEvent -= OnPlayerUpdate;
            _net.Connection.PlayerChangeUpdateEvent -= OnPlayerChangeUpdate;
            _net.Connection.HelmetToggleEvent -= OnHelmetToggle;
            _net.Connection.InspectEvent -= OnInspect;
            _net.Connection.MagicLeveledEvent -= OnMagicLeveled;
            _net.Connection.MagicCooldownEvent -= OnMagicCooldown;
            _net.Connection.MagicToggleEvent -= OnMagicToggle;
        }

        if (Game == this) Game = null;

        // 进程退出前释放静态资源缓存, 消除 Godot 退出时的 RID 泄漏警告。
        // 注意: 回登录界面/切场景也会触发 _ExitTree, 但 MirSkin 缓存是惰性
        // 重建的, 重新进入时 GetTexture/GetFont 会重新加载, 功能不受影响。
        MirSkin.DisposeAll();
    }

    private void OnStartGameResult(StartGameResult result, StartInformation info)
    {
        _pendingStartResult = result;
        _pendingStartInfo = info;
        CallDeferred(nameof(ShowStartGameResult));
    }

    private void OnGameLogout(S.GameLogout packet)
    {
        if (!_leavingGame) return;
        _leavingGame = false;
        _pendingLogoutCharacters = packet?.Characters ?? new List<SelectInfo>();
        CallDeferred(nameof(ReturnToCharacterSelect));
    }

    private void ReturnToCharacterSelect()
    {
        if (!IsInsideTree()) return;

        SoundPlayback.Stop(SoundIndex.LoginScene);
        SoundPlayback.Stop(SoundIndex.SelectScene);
        while (WindowManager.CloseTop()) { }

        var selectScene = ResourceLoader.Load<PackedScene>("res://Scenes/SelectScene.tscn");
        if (selectScene == null) return;

        var select = selectScene.Instantiate<SelectScene>();
        select.SetCharacters(_pendingLogoutCharacters ?? new List<SelectInfo>());
        _pendingLogoutCharacters = null;
        GetTree().Root.AddChild(select);
        QueueFree();
    }

    private void ShowStartGameResult()
    {
        if (_pendingStartResult == StartGameResult.Success && _pendingStartInfo != null)
        {
            StartInfo = _pendingStartInfo;
            _playerObjectID = _pendingStartInfo.ObjectID;
            _playerMapIndex = _pendingStartInfo.MapIndex;
            _playerInstanceIndex = _pendingStartInfo.InstanceIndex;
            _playerLocation = _pendingStartInfo.Location;
            _playerDirection = _pendingStartInfo.Direction;
            _pendingX = _playerLocation.X;
            _pendingY = _playerLocation.Y;
            _pendingDir = _playerDirection;
            _playerHorse = _pendingStartInfo.Horse;
            // 原版进入地图后 CanRun 已可用；右键按下的第一段就必须是跑步，
            // 不能先被客户端错误降级成一格走路。
            _canRun = true;
            DayTime = _pendingStartInfo.DayTime;
            TimeOfDay = _pendingStartInfo.TimeOfDay;

            GD.Print($"[Game] 进入游戏! 玩家: {_pendingStartInfo.Name}, 位置: ({_pendingStartInfo.Location.X},{_pendingStartInfo.Location.Y}), 方向: {_pendingStartInfo.Direction}, 地图: {_pendingStartInfo.MapIndex}");
            _statusLabel.Text = string.Format(Lang.GameLocationLabel, _pendingStartInfo.Name, _pendingStartInfo.Location.X, _pendingStartInfo.Location.Y, _pendingStartInfo.Direction);

            InitHudData(_pendingStartInfo);
            _startGameShown = true;

            // StartInformation 中的地图可能仍是进入流程前的旧值；
            // 启动阶段以随后到达的 S.MapChanged 为准，避免先闪现错误地图。
            if (_hasPendingMapChanged)
            {
                _hasPendingMapChanged = false;
                _playerMapIndex = _pendingMapIndex;
                _playerInstanceIndex = _pendingInstanceIndex;
                LoadPlayerMap(clearObjects: false);
            }
            else
            {
                _waitingStartupMap = true;
                GetTree().CreateTimer(StartupMapFallbackDelaySeconds).Timeout += FinalizeStartupMap;
            }

        }
        else
        {
            _statusLabel.Text = string.Format(Lang.SelectGameLabel3, _pendingStartResult);
            GD.Print($"[Game] StartGame 失败: {_pendingStartResult}");
        }
    }

    private void OnMapChanged(int mapIndex, int instanceIndex)
    {
        _pendingNpcClickObjectId = 0;
        _pendingMapIndex = mapIndex;
        _pendingInstanceIndex = instanceIndex;
        _hasPendingMapChanged = true;
        if (_startGameShown)
        {
            _waitingStartupMap = false;
            CallDeferred(nameof(ShowMapChanged));
        }
    }

    private void FinalizeStartupMap()
    {
        if (!_waitingStartupMap || !IsInsideTree() || _pendingStartInfo == null) return;
        _waitingStartupMap = false;
        _playerMapIndex = _pendingStartInfo.MapIndex;
        _playerInstanceIndex = _pendingStartInfo.InstanceIndex;
        LoadPlayerMap(clearObjects: false);
        UpdateMapMusic();
    }

    private void OnDayTimeChanged(float dayTime)
    {
        DayTime = Math.Clamp(dayTime, 0f, 1f);
        _lightLayer?.SetDayTime(DayTime);
    }

    private void OnTimeOfDayChanged(TimeOfDay timeOfDay, string label)
    {
        TimeOfDay = timeOfDay;
        GD.Print($"[Game] 时间阶段: {timeOfDay} {label}");
        _lightLayer?.QueueRedraw();
    }

    private void ShowMapChanged()
    {
        _hasPendingMapChanged = false;
        _playerMapIndex = _pendingMapIndex;
        _playerInstanceIndex = _pendingInstanceIndex;
        GD.Print($"[Game] 地图切换: MapIndex={_pendingMapIndex} InstanceIndex={_pendingInstanceIndex}");
        DXItemCell.SelectedCell = null;
        _pendingNpcClickObjectId = 0;
        LoadPlayerMap();
        UpdateMapMusic();
        UpdateAutoPathProgress();
    }

    private void UpdateMapMusic()
    {
        var map = Globals.MapInfoList?.Binding.FirstOrDefault(m => m.Index == _playerMapIndex);
        var next = map?.Music ?? SoundIndex.None;
        if (next == _mapMusic) return;
        if (_mapMusic != SoundIndex.None) StopSound(_mapMusic);
        _mapMusic = next;
        PlaySound(_mapMusic);
    }

    private void OnUserLocation(MirDirection dir, System.Drawing.Point loc)
    {
        if (AutoLoginArgs.OfflineMovementTest)
        {
            GD.Print($"[OfflineMove] IGNORE UserLocation location=({loc.X},{loc.Y})");
            return;
        }
        // S.UserLocation 是服务端对非法/过早移动的纠正，不是 S.ObjectMove。
        // 原客户端收到它会校正格子并停止当前移动，不能再播放一次 Walking。
        if (_player == null) return;
        _moveServerLockUntilMs = 0;  // 服务端纠正(拒绝移动), 同样解除门控
        _playerDirection = dir;
        _player.Direction = dir;
        ApplyAuthoritativePlayerLocation(loc);
        _player.PlayStandingForState();
        _canRun = IsRunInputHeld();
    }

    private void HandleKeyBind(KeyBindAction action)
    {
        if (action >= KeyBindAction.SpellUse01 && action <= KeyBindAction.SpellUse24)
        {
            UseMagicKey((int)(action - KeyBindAction.SpellUse01));
            return;
        }
        if (action >= KeyBindAction.SpellSet01 && action <= KeyBindAction.SpellSet04)
        {
            MagicBarSpellSet = (int)(action - KeyBindAction.SpellSet01) + 1;
            _magicBar?.Refresh();
            return;
        }
        switch (action)
        {
            case KeyBindAction.MapMiniWindow:
                // 原版是三态：显示不透明 -> 半透明 -> 隐藏 -> 显示不透明。
                if (!_miniMap.Visible)
                {
                    _miniMap.ResetTransparencyForKeyBind();
                    _miniMap.Visible = true;
                }
                else if (Mathf.IsEqualApprox(_miniMap.Opacity, 1f))
                {
                    _miniMap.ToggleTransparencyForKeyBind();
                }
                else
                {
                    _miniMap.Visible = false;
                    _miniMap.ResetTransparencyForKeyBind();
                }
                break;
            case KeyBindAction.MapBigWindow:
                if (_bigMap.Visible) _bigMap.Visible = false;
                else OpenBigMap();
                break;
            case KeyBindAction.QuestTrackerWindow:
                _questTracker.Visible = !_questTracker.Visible;
                break;
            case KeyBindAction.ChangeAttackMode:
                CycleAttackMode();
                break;
            case KeyBindAction.ChangePetMode:
                CyclePetMode();
                break;
            case KeyBindAction.CharacterWindow:
                ToggleCharacterWindow();
                break;
            case KeyBindAction.InventoryWindow:
                WindowManager.Toggle(_inventoryDialog, _uiLayer);
                break;
            case KeyBindAction.StorageWindow:
                WindowManager.Toggle(_storageDialog, _uiLayer);
                break;
            case KeyBindAction.BeltWindow:
                WindowManager.Toggle(_beltDialog, _uiLayer);
                break;
            case KeyBindAction.AutoPotionWindow:
                WindowManager.Toggle(AutoPotionBox, _uiLayer);
                break;
            case KeyBindAction.CurrencyWindow:
                WindowManager.Toggle(_currencyDialog, _uiLayer);
                _currencyDialog?.RefreshCurrencies(Currencies);
                break;
            case KeyBindAction.FilterDropWindow:
                WindowManager.Toggle(_filterDropDialog, _uiLayer);
                _filterDropDialog?.LoadFilters(DropFilters);
                break;
            case KeyBindAction.FortuneWindow:
                WindowManager.Toggle(_fortuneDialog, _uiLayer);
                if (_fortuneDialog.Visible) _fortuneDialog.Search();
                break;
            case KeyBindAction.MagicWindow:
                if (_magicDialog is not null)
                {
                    WindowManager.Toggle(_magicDialog, _uiLayer);
                    _magicDialog.Refresh();
                }
                break;
            case KeyBindAction.MagicBarWindow:
                if (_magicBar != null) _magicBar.Visible = !_magicBar.Visible;
                break;
            case KeyBindAction.DungeonFinderWindow:
                if (DungeonEnabled) WindowManager.Toggle(_dungeonFinderDialog, _uiLayer);
                break;
            case KeyBindAction.BlockListWindow:
                WindowManager.Toggle(_communicationDialog, _uiLayer);
                if (_communicationDialog.Visible) _communicationDialog.ShowBlockPage();
                break;
            case KeyBindAction.QuestLogWindow:
                WindowManager.Toggle(_questDialog, _uiLayer);
                break;
            case KeyBindAction.GroupAllowSwitch:
                _groupDialog?.ToggleAllow();
                break;
            case KeyBindAction.MountToggle:
                _net?.Connection?.Enqueue(new C.Mount());
                break;
            case KeyBindAction.AutoRunToggle:
                _autoRun = !_autoRun;
                if (_mouseWalker != null) _mouseWalker.AutoRun = _autoRun;
                break;
            case KeyBindAction.ToggleItemLock:
                // 旧版 DXItemCell.OnKeyDown: 悬停格 (MouseControl==this) 按 Scroll Lock 锁定,
                // 不是 SelectedCell (拿起) 格。
                (DXControl.MouseControl as DXItemCell)?.ToggleLock();
                break;
            case KeyBindAction.TradeRequest:
                _net?.Connection?.Enqueue(new C.TradeRequest());
                break;
            case KeyBindAction.GroupTarget:
                if (_combatController?.MouseObject?.Type == ObjectRenderer.Kind.Player)
                    _net?.Connection?.Enqueue(new C.GroupInvite { Name = _combatController.MouseObject.DisplayName });
                break;
            case KeyBindAction.TradeAllowSwitch:
                SendChat("@AllowTrade");
                break;
            case KeyBindAction.ChangeChatMode:
                _chatTextBox?.CycleMode();
                break;
            case KeyBindAction.ItemPickUp:
                SendPickUp();
                break;
            case KeyBindAction.PartnerTeleport:
                SendMarriageTeleport();
                break;
            case KeyBindAction.MenuWindow:
                WindowManager.Toggle(_menuDialog, _uiLayer);
                break;
            case KeyBindAction.RankingWindow:
                WindowManager.Toggle(_rankingDialog, _uiLayer);
                break;
            case KeyBindAction.GameStoreWindow:
                if (GameStoreEnabled)
                    WindowManager.Toggle(_gameStoreDialog, _uiLayer);
                break;
            case KeyBindAction.CompanionWindow:
                if (CompanionEnabled) WindowManager.Toggle(_companionDialog, _uiLayer);
                break;
            case KeyBindAction.GroupWindow:
                WindowManager.Toggle(_groupDialog, _uiLayer);
                break;
            case KeyBindAction.GuildWindow:
                WindowManager.Toggle(_guildDialog, _uiLayer);
                break;
            case KeyBindAction.MailBoxWindow:
                WindowManager.Toggle(_communicationDialog, _uiLayer);
                break;
            case KeyBindAction.MailSendWindow:
                WindowManager.Toggle(_communicationDialog, _uiLayer);
                if (_communicationDialog.Visible) _communicationDialog.ShowSendPage();
                break;
            case KeyBindAction.ChatOptionsWindow:
                WindowManager.Toggle(_chatOptionsDialog, _uiLayer);
                break;
            case KeyBindAction.ExitGameWindow:
                OpenExitDialog();
                break;
            case >= KeyBindAction.UseBelt01 and <= KeyBindAction.UseBelt10:
                UseBeltKey((int)(action - KeyBindAction.UseBelt01));
                break;
            case KeyBindAction.HelpWindow:
                OpenHelpDialog();
                break;
            case KeyBindAction.ConfigWindow:
                OpenConfigDialog();
                break;
            default:
                GD.Print($"[Game] 键位 {action} 尚无对应动作");
                break;
        }
    }

    private void ToggleCharacterWindow()
    {
        if (_characterDialog == null) return;
        if (_characterDialog.Visible)
        {
            WindowManager.Close(_characterDialog);
            return;
        }

        _characterDialog.ShowOwn();
        WindowManager.Open(_characterDialog, _uiLayer);
    }

    /// <summary>旧版 EI Ctrl+S 坐骑窗口入口；坐骑动作仍由窗口按钮发送。</summary>
    public void ToggleHorseWindow()
    {
        if (_horseDialog == null) return;
        WindowManager.Toggle(_horseDialog, _uiLayer);
    }

    private void OnObjectMove(uint objectID, MirDirection dir, System.Drawing.Point loc, int distance,
        TimeSpan slow = default, bool mapChanged = false)
    {
        ClearMovementEffect(objectID);
        if (objectID == _playerObjectID)
        {
            if (AutoLoginArgs.OfflineMovementTest)
            {
                GD.Print($"[OfflineMove] IGNORE ObjectMove location=({loc.X},{loc.Y}) distance={distance}");
                return;
            }
            // 不要在这里提前解除 ServerTime 门控。这里的网络事件会把
            // ShowUserLocation 延迟到主线程帧末执行；如果此时立即解锁，
            // MouseWalker 可能先发出下一步，而旧回包随后会把预测坐标
            // 误判为“需要纠正”，表现为走路回拉/瞬移。
            bool autoPathActive = _autoPathRoutes.Count > 0 || _autoPathCancelPending;
            if (autoPathActive)
            {
                if (mapChanged)
                {
                    // 原版 ApplyAutoPathMapTransition：跨地图时丢弃旧步进，
                    // 让随后的 MapChanged 成为唯一的地图切换来源。
                    _pendingAutoPathMove = null;
                    ApplyAuthoritativePlayerLocation(loc, slow);
                    return;
                }

                _pendingAutoPathMove = new PendingAutoPathMove
                {
                    Direction = dir,
                    Location = loc,
                    Distance = Math.Max(1, distance),
                    Slow = slow,
                };
                return;
            }
            _mouseWalker?.AddMoveDelay(slow);
            CallDeferred(nameof(ShowUserLocation), (int)dir, loc.X, loc.Y, Math.Max(1, distance));
            return;
        }

        // 其他玩家/怪物移动 (M4)。
        // 其他玩家同时注册在 _otherPlayers(可见 PlayerRenderer) 与
        // _objects(隐藏命中代理)。渲染必须走 PlayerRenderer 的平滑补间,
        // 代理坐标由 UpdateOtherPlayerPosition 每帧同步, 这里只需更新其
        // 点击优先级; 怪物/NPC/物品没有 PlayerRenderer, 走 _objects 队列。
        if (_otherPlayers.TryGetValue(objectID, out var player))
        {
            if (_objects.TryGetValue(objectID, out var proxy)) proxy.HitOrder = ++_nextObjectHitOrder;
            player.StartMove(new System.Drawing.Point(loc.X, loc.Y), dir,
                Math.Max(1, distance), player.Horse != HorseType.None);
            UpdateOtherPlayerPosition(player);
        }
        else if (_objects.TryGetValue(objectID, out var ob))
        {
            // 原版移动会把对象从旧 Cell.Objects 移除后追加到新格末尾，
            // 使其在 CheckCursor 的逆序扫描中成为最新优先项。
            ob.HitOrder = ++_nextObjectHitOrder;
            ob.QueueMove(loc, dir, Math.Max(1, distance));
        }
    }

    private void ProcessPendingAutoPathMove()
    {
        if (_pendingAutoPathMove == null || _player == null || !_startGameShown) return;

        var move = _pendingAutoPathMove;
        _pendingAutoPathMove = null;
        _canRun = true;
        _mouseWalker?.AddMoveDelay(move.Slow);
        CallDeferred(nameof(ShowUserLocation), (int)move.Direction,
            move.Location.X, move.Location.Y, move.Distance);
    }

    private void OnObjectIdle(S.ObjectIdle packet)
    {
        if (packet == null) return;
        ClearMovementEffect(packet.ObjectID);
        if (_otherPlayers.TryGetValue(packet.ObjectID, out var player))
        {
            player.StopRemoteMovement();
            player.Direction = packet.Direction;
            player.CellX = packet.Location.X;
            player.CellY = packet.Location.Y;
            player.PlayStandingForState();
            UpdateOtherPlayerPosition(player);
            return;
        }
        if (_objects.TryGetValue(packet.ObjectID, out var objectNode))
        {
            objectNode.Direction = packet.Direction;
            objectNode.CellX = packet.Location.X;
            objectNode.CellY = packet.Location.Y;
            objectNode.SetAnimation(MirAnimation.Standing);
            UpdateObjectPositions();
        }
    }

    private void OnObjectShow(S.ObjectShow packet) => SetObjectVisibility(packet?.ObjectID ?? 0, true, packet?.Direction, packet?.Location);
    private void OnObjectHide(S.ObjectHide packet) => SetObjectVisibility(packet?.ObjectID ?? 0, false, packet?.Direction, packet?.Location);

    private void SetObjectVisibility(uint objectID, bool visible, MirDirection? direction, System.Drawing.Point? location)
    {
        if (_objects.TryGetValue(objectID, out var objectNode))
        {
            if (direction.HasValue) objectNode.Direction = direction.Value;
            if (location.HasValue) { objectNode.CellX = location.Value.X; objectNode.CellY = location.Value.Y; }
            objectNode.Visible = visible;
            UpdateObjectPositions();
        }
        if (_otherPlayers.TryGetValue(objectID, out var player))
        {
            if (direction.HasValue) player.Direction = direction.Value;
            if (location.HasValue) { player.CellX = location.Value.X; player.CellY = location.Value.Y; }
            player.Visible = visible;
            UpdateOtherPlayerPosition(player);
        }
    }

    private void OnObjectNameColour(S.ObjectNameColour packet)
    {
        if (packet == null) return;
        var colour = ToGodotColour(packet.Colour);
        if (_objects.TryGetValue(packet.ObjectID, out var objectNode))
        {
            objectNode.NameColour = colour;
            objectNode.QueueRedraw();
        }
        if (_otherPlayers.TryGetValue(packet.ObjectID, out var player))
        {
            player.NameColour = colour;
            player.QueueRedraw();
        }
    }

    private void OnObjectPetOwnerChanged(S.ObjectPetOwnerChanged packet)
    {
        if (packet == null || !_objects.TryGetValue(packet.ObjectID, out var objectNode)) return;
        objectNode.PetOwner = packet.PetOwner;
        objectNode.QueueRedraw();
    }

    private void OnObjectLeveled(S.ObjectLeveled packet)
    {
        if (packet == null) return;
        if (_objects.TryGetValue(packet.ObjectID, out var objectNode)) objectNode.QueueRedraw();
        if (_otherPlayers.TryGetValue(packet.ObjectID, out var player)) player.QueueRedraw();
    }

    private void OnObjectRevive(S.ObjectRevive packet)
    {
        if (packet == null) return;
        if (_objects.TryGetValue(packet.ObjectID, out var objectNode))
        {
            objectNode.Dead = false;
            objectNode.CellX = packet.Location.X;
            objectNode.CellY = packet.Location.Y;
            objectNode.Visible = true;
            objectNode.SetAnimation(MirAnimation.Standing);
        }
        if (_otherPlayers.TryGetValue(packet.ObjectID, out var player))
        {
            player.Dead = false;
            player.CellX = packet.Location.X;
            player.CellY = packet.Location.Y;
            player.Visible = true;
            player.PlayStandingForState();
        }
        UpdateObjectPositions();
        foreach (var remotePlayer in _otherPlayers.Values) UpdateOtherPlayerPosition(remotePlayer);
    }

    private void OnObjectStats(S.ObjectStats packet)
    {
        if (packet == null) return;
        int maxHealth = packet.Stats?[Stat.Health] ?? 0;
        int light = packet.Stats?[Stat.Light] ?? 0;
        if (_otherPlayers.TryGetValue(packet.ObjectID, out var player))
        {
            player.MaxHealth = maxHealth;
            player.Light = light;
            player.QueueRedraw();
        }
        if (_objects.TryGetValue(packet.ObjectID, out var objectNode))
        {
            objectNode.Stats = packet.Stats;
            objectNode.MaxHealth = maxHealth;
            objectNode.Light = light;
            objectNode.QueueRedraw();
        }
    }

    private void OnObjectHarvested(S.ObjectHarvested packet)
    {
        if (packet == null || !_objects.TryGetValue(packet.ObjectID, out var objectNode)) return;
        objectNode.CellX = packet.Location.X;
        objectNode.CellY = packet.Location.Y;
        objectNode.Direction = packet.Direction;
        objectNode.Dead = true;
        objectNode.SetAnimation(MirAnimation.Dead);
        UpdateObjectPositions();
    }

    private void OnCompanionShapeUpdate(S.CompanionShapeUpdate packet)
    {
        if (packet == null) return;
        // 伙伴的独立头/背图层由下一次 ObjectMonster 外观包重新建立；保留包处理，
        // 同时让当前对象立即重绘，避免服务端变更后仍停留在旧帧。
        if (_objects.TryGetValue(packet.ObjectID, out var objectNode)) objectNode.QueueRedraw();
    }

    private void OnSafeZoneChanged(S.SafeZoneChanged packet)
    {
        InSafeZone = packet?.InSafeZone == true;
        if (StartInfo != null) StartInfo.InSafeZone = InSafeZone;
        ReceiveChat(InSafeZone ? Lang.GameSafeZoneLabel : Lang.GameAwayLabel, MessageType.System);
    }

    private void OnCombatTime(S.CombatTime packet)
    {
        CombatUntilMs = Godot.Time.GetTicksMsec() + 10_000;
    }

    private void OnGuildChanged(S.GuildChanged packet)
    {
        if (packet == null) return;
        if (packet.ObjectID == _playerObjectID)
        {
            if (StartInfo != null) { StartInfo.GuildName = packet.GuildName; StartInfo.GuildRank = packet.GuildRank; }
            _characterDialog?.QueueRedraw();
        }
        if (_otherPlayers.TryGetValue(packet.ObjectID, out var player))
        {
            player.GuildName = packet.GuildName;
            player.QueueRedraw();
        }
        if (_objects.TryGetValue(packet.ObjectID, out var hitProxy))
        {
            hitProxy.GuildName = packet.GuildName;
            hitProxy.QueueRedraw();
        }
    }

    private void OnGuildWarStarted(S.GuildWarStarted packet)
    {
        if (packet == null) return;
        _guildWars.Add(packet.GuildName ?? string.Empty);
        ReceiveChat(string.Format(Lang.GameGuildLabel, packet.GuildName), MessageType.System);
    }

    private void OnGuildWarFinished(S.GuildWarFinished packet)
    {
        if (packet == null) return;
        _guildWars.Remove(packet.GuildName ?? string.Empty);
        ReceiveChat(string.Format(Lang.GameGuildLabel2, packet.GuildName), MessageType.System);
    }

    private void OnGuildWar(S.GuildWar packet)
    {
        if (packet != null) ReceiveChat(packet.Success ? Lang.GameGuildLabel3 : Lang.GameGuildLabel4, MessageType.System);
    }

    private void OnMarriageInfo(S.MarriageInfo packet)
    {
        _characterDialog?.SetPartner(packet?.Partner?.Name);
        if (packet?.Partner != null) ReceiveChat(string.Format(Lang.GameUi554Label, packet.Partner.Name), MessageType.System);
    }

    private void OnMarriageRemoveRing(S.MarriageRemoveRing packet)
    {
        var ring = Equipment.ElementAtOrDefault((int)EquipmentSlot.RingL);
        if (ring != null) ring.Flags &= ~UserItemFlags.Marriage;
        _characterDialog?.QueueRedraw();
    }

    private void OnMarriageMakeRing(S.MarriageMakeRing packet)
    {
        var ring = Equipment.ElementAtOrDefault((int)EquipmentSlot.RingL);
        if (ring != null) ring.Flags |= UserItemFlags.Marriage;
        _characterDialog?.QueueRedraw();
    }

    private void OnMarriageOnlineChanged(S.MarriageOnlineChanged packet)
    {
        if (packet != null) ReceiveChat(packet.ObjectID == 0 ? Lang.GameOfflineLabel : Lang.GameUi556Label, MessageType.System);
    }

    private void OnMailSend(S.MailSend packet)
    {
        _communicationDialog?.MailSendResult();
        // 原版 S.MailSend 只是请求阶段回包，真正结果随后由
        // ItemsChanged.Success 表示；不能在这里提前提示“发送完成”。
    }
    private void OnMarketPlaceStoreBuy(S.MarketPlaceStoreBuy packet) => ReceiveChat(Lang.GameBuyLabel, MessageType.System);
    private void OnMountFailed(S.MountFailed packet) => ReceiveChat(Lang.GameNoneLabel, MessageType.System);
    private void OnTradeAddItem(S.TradeAddItem packet)
    {
        _tradeDialog?.ApplyTradeAddItem(packet);
        if (packet != null && !packet.Success) ReceiveChat(Lang.GameAddLabel, MessageType.System);
    }
    private void OnTradeAddGold(S.TradeAddGold packet)
    {
        if (packet == null) return;
        _tradeDialog?.SetPlayerGold(packet.Gold);
    }

    private void OnDataObjectLocation(S.DataObjectLocation packet)
    {
        if (packet == null) return;
        if (_objects.TryGetValue(packet.ObjectID, out var objectNode))
        {
            objectNode.CellX = packet.CurrentLocation.X;
            objectNode.CellY = packet.CurrentLocation.Y;
            UpdateObjectPositions();
        }
        if (_otherPlayers.TryGetValue(packet.ObjectID, out var player))
        {
            player.CellX = packet.CurrentLocation.X;
            player.CellY = packet.CurrentLocation.Y;
            UpdateOtherPlayerPosition(player);
        }
    }

    private void OnDataObjectRemove(S.DataObjectRemove packet)
    {
        if (packet == null) return;
        _miniMap?.RemoveObject(packet.ObjectID);
        _bigMap?.RemoveObject(packet.ObjectID);
        OnObjectRemove(packet.ObjectID);
    }

    private void OnDataObjectPlayer(S.DataObjectPlayer packet)
    {
        if (packet == null) return;
        _miniMap?.UpdateObject(packet.ObjectID, packet.CurrentLocation.X, packet.CurrentLocation.Y, ObjectRenderer.Kind.Player);
        _bigMap?.UpdateObject(packet.ObjectID, packet.CurrentLocation.X, packet.CurrentLocation.Y, ObjectRenderer.Kind.Player);
        if (_otherPlayers.TryGetValue(packet.ObjectID, out var player))
        {
            player.Health = packet.Health;
            player.MaxHealth = packet.MaxHealth;
            player.Dead = packet.Dead;
            player.CellX = packet.CurrentLocation.X;
            player.CellY = packet.CurrentLocation.Y;
            UpdateOtherPlayerPosition(player);
        }
    }

    private void OnDataObjectItem(S.DataObjectItem packet)
    {
        if (packet == null) return;
        _miniMap?.UpdateObject(packet.ObjectID, packet.CurrentLocation.X, packet.CurrentLocation.Y, ObjectRenderer.Kind.Item);
        _bigMap?.UpdateObject(packet.ObjectID, packet.CurrentLocation.X, packet.CurrentLocation.Y, ObjectRenderer.Kind.Item);
    }

    private void OnStartObserver(S.StartObserver packet)
    {
        if (packet?.StartInformation == null) return;
        _observer = true;
        StartInfo = packet.StartInformation;
        FillItems(packet.Items);
        ReceiveChat(Lang.GameObserverLabel, MessageType.System);
    }

    private static Color ToGodotColour(System.Drawing.Color colour) =>
        colour.A <= 0 ? Colors.White : new Color(colour.R / 255f, colour.G / 255f, colour.B / 255f, colour.A / 255f);

    private void OnInspect(S.Inspect packet)
    {
        if (packet == null) return;
        if (AutoLoginArgs.InteractionAudit)
        {
            _interactionInspectReceived++;
            GD.Print($"[InteractionAudit] INSPECT_RESPONSE name={packet.Name} items={packet.Items?.Count ?? 0}");
        }
        if (packet.Ranking)
        {
            _rankingDialog?.ApplyInspect(packet);
            return;
        }
        if (_characterDialog == null) return;
        _characterDialog.ApplyInspect(packet);
        WindowManager.Open(_characterDialog, _uiLayer);
    }

    private void OnObjectPlayer(S.ObjectPlayer p)
    {
        if (p == null || p.ObjectID == _playerObjectID) return;
        if (_otherPlayers.TryGetValue(p.ObjectID, out var existing))
        {
            existing.UpdateAppearance(p);
            existing.GuildName = p.GuildName;
            existing.CellX = p.Location.X;
            existing.CellY = p.Location.Y;
            UpdateOtherPlayerPosition(existing);
            return;
        }
        var player = new PlayerRenderer { CellX = p.Location.X, CellY = p.Location.Y };
        player.CharacterIndex = p.Index;
        player.FrameChanged = (animation, frame, magic) => OnPlayerFrameChanged(player, animation, frame, magic);
        player.SoundCue = PlaySound;
        player.UpdateAppearance(p);
        AddChild(player);
        _otherPlayers[p.ObjectID] = player;
        // 命中代理：玩家外观仍由 PlayerRenderer 绘制，代理只参与地图拾取/点击命中。
        var hit = new ObjectRenderer
        {
            Type = ObjectRenderer.Kind.Player,
            ObjectID = p.ObjectID,
            DisplayName = p.Name,
            GuildName = p.GuildName,
            CharacterIndex = p.Index,
            CellX = p.Location.X,
            CellY = p.Location.Y,
            Visible = false,
            HitOrder = ++_nextObjectHitOrder,
        };
        AddChild(hit);
        _objects[p.ObjectID] = hit;
        UpdateOtherPlayerPosition(player);
        _miniMap?.UpdateObject(p.ObjectID, p.Location.X, p.Location.Y, ObjectRenderer.Kind.Player);
        _bigMap?.UpdateObject(p.ObjectID, p.Location.X, p.Location.Y, ObjectRenderer.Kind.Player);
    }

    private void OnPlayerUpdate(S.PlayerUpdate packet)
    {
        if (packet == null) return;
        if (_playerObjectID != 0 && _player != null && packet.ObjectID == _playerObjectID)
        {
            _player.ApplyUpdate(packet);
            if (StartInfo != null)
            {
                StartInfo.Weapon = packet.Weapon;
                StartInfo.Shield = packet.Shield;
                StartInfo.Armour = packet.Armour;
                StartInfo.Costume = packet.Costume;
                StartInfo.ArmourColour = packet.ArmourColour;
                StartInfo.HelmetShape = packet.Helmet;
                StartInfo.HideHead = packet.HideHead;
            }
            _characterDialog?.QueueRedraw();
            return;
        }
        if (_otherPlayers.TryGetValue(packet.ObjectID, out var player))
            player.ApplyUpdate(packet);
    }

    private void OnPlayerChangeUpdate(S.PlayerChangeUpdate packet)
    {
        if (packet == null) return;
        if (_playerObjectID != 0 && _player != null && packet.ObjectID == _playerObjectID)
        {
            _player.ApplyCharacterUpdate(packet);
            if (StartInfo != null)
            {
                StartInfo.Name = packet.Name;
                StartInfo.Gender = packet.Gender;
                StartInfo.HairType = packet.HairType;
                StartInfo.HairColour = packet.HairColour;
                StartInfo.ArmourColour = packet.ArmourColour;
            }
            return;
        }
        if (_otherPlayers.TryGetValue(packet.ObjectID, out var player))
            player.ApplyCharacterUpdate(packet);
    }

    private void OnHelmetToggle(S.HelmetToggle packet)
    {
        if (packet == null || _player == null) return;
        _player.HideHead = packet.HideHelmet;
        if (StartInfo != null) StartInfo.HideHead = packet.HideHelmet;
        _player.RefreshAppearanceLibraries();
        _characterDialog?.QueueRedraw();
        _player.QueueRedraw();
    }

    private void OnObjectMonster(S.ObjectMonster p)
    {
        GD.Print($"[Game] OnObjectMonster: ObjectID={p.ObjectID} MonsterIndex={p.MonsterIndex} Dead={p.Dead}");
        if (_objects.ContainsKey(p.ObjectID)) return; // 已有(重复包)
        var ob = ObjectRenderer.CreateMonster(p);
        if (ob == null) return;
        AddObject(ob, p.ObjectID, zIndex: 40);
    }

    private void OnObjectNPC(S.ObjectNPC p)
    {
        var npcInfo = Globals.NPCInfoList?.Binding.FirstOrDefault(x => x.Index == p.NPCIndex);
        if (npcInfo != null) _npcInfos[p.ObjectID] = npcInfo;
        if (_objects.ContainsKey(p.ObjectID)) return;
        var ob = ObjectRenderer.CreateNPC(p);
        if (ob == null) return;
        AddObject(ob, p.ObjectID, zIndex: 40);
    }

    private void OnObjectItem(S.ObjectItem p)
    {
        // 服务端重发/复用掉落物 ObjectID 时，旧客户端会先收到 Remove 或
        // 直接收到新的 ObjectItem。不能因为缓存里还有旧节点就静默丢弃新物品，
        // 否则物品只会在重进地图重建对象后出现。
        if (_objects.TryGetValue(p.ObjectID, out var oldObject))
        {
            if (oldObject.Type != ObjectRenderer.Kind.Item) return;
            OnObjectRemove(p.ObjectID);
        }
        var ob = ObjectRenderer.CreateItem(p);
        if (ob == null) return;
        AddObject(ob, p.ObjectID, zIndex: 30);
        // 新掉落物不能等到下一次窗口/地图重建才获得标签布局；同格物品
        // 的 slot 也必须立即重排，否则旧节点会显示、新节点却不重绘。
        RefreshGroundItemLabels(forceRedraw: true);
        SpawnItemGlow(ob, p.Item);
    }

    private void OnChat(S.Chat p)
    {
        if (p == null || string.IsNullOrWhiteSpace(p.Text)) return;
        string sender = p.ObjectID == _playerObjectID ? (StartInfo?.Name ?? Lang.GameUi561Label) :
            (_objects.TryGetValue(p.ObjectID, out var chatObject) ? chatObject.DisplayName : Lang.GameSystemLabel);
        _chatLog?.AddMessage($"[{p.Type}] {sender}: {p.Text}", p.Type, ChatColour(p.Type), p.LinkedItems);
        if (p.ObjectID == _playerObjectID) _player?.SetChat(p.Text);
        else if (_otherPlayers.TryGetValue(p.ObjectID, out var player)) player.SetChat(p.Text);
        else if (_objects.TryGetValue(p.ObjectID, out var ob)) ob.SetChat(p.Text);
    }

    private static Color ChatColour(MessageType type) => ClientSettings.ChatForeColour(type);

    private void OnGroupMember(uint objectId, string name)
    {
        _groupDialog?.AddMember(objectId, name);
        _groupHealthPanel?.AddMember(objectId, name);
    }
    private void OnGroupRemove(uint objectId)
    {
        _groupDialog?.RemoveMember(objectId);
        _groupHealthPanel?.RemoveMember(objectId);
    }
    private void OnGroupLfg(S.GroupLFG packet) => _groupDialog?.SetLfg(packet?.List);
    private void OnGroupInvite(S.GroupInvite packet) => _groupDialog?.ShowInvite(packet?.Name);
    private void OnGroupRequest(S.GroupRequest packet) => ReceiveChat($"{packet?.Name ?? Lang.GroupUnknownLabel} 请求加入队伍（请在队伍界面处理）", MessageType.System);
    private void OnGroupUpdate(S.GroupUpdate packet) => _groupDialog?.SetOwnLfg(packet?.Group);
    private void OnGroupSwitch(S.GroupSwitch packet) => _groupDialog?.SetAllow(packet?.Allow == true);

    private void OnGameStoreData(S.GameStoreData packet)
    {
        if (packet == null) return;
        // 原版一个 GameStoreData 同时携带收藏和热销商品，不能只应用前者。
        _gameStoreDialog?.SetFavourites(packet.Favourites);
        _gameStoreDialog?.SetTopItems(packet.TopItems);
    }

    private void OnGameStoreTopItems(S.GameStoreTopItems packet)
    {
        _gameStoreDialog?.SetTopItems(packet?.Items);
    }

    private void OnGameStoreFavouriteChanged(S.GameStoreFavouriteChanged packet)
    {
        if (packet != null) _gameStoreDialog?.SetFavourite(packet.Index, packet.Favourited);
    }

    private void OnGameStoreGift(S.GameStoreGift packet)
    {
        if (packet == null) return;
        ReceiveChat(string.Format(Lang.GameMarketLabel, packet.Result), packet.Result == GameStoreGiftResult.Success ? MessageType.Announcement : MessageType.System);
    }

    private void OnGuildInfo(S.GuildInfo packet)
    {
        _guildDialog?.ApplyGuild(packet?.Guild);
        // S18a: 建会响应是 S.GuildInfo (服务端 SendGuildInfo, 非 S.GuildUpdate)
        if (AutoLoginArgs.OperationAuditExt && _operationAuditExtStage == 18 && _operationAuditExtGuildSubStage == 0)
        {
            _operationAuditExtLastSuccess = true;
            _operationAuditExtResponsePending = true;
            CallDeferred(nameof(ContinueOperationAuditExt));
        }
    }
    private void OnGuildNoticeChanged(S.GuildNoticeChanged packet) => _guildDialog?.SetGuildNotice(packet?.Notice);
    private void OnGuildUpdate(S.GuildUpdate packet) => _guildDialog?.ApplyGuildUpdate(packet);
    private void OnGuildMemberOffline(S.GuildMemberOffline packet) => _guildDialog?.SetMemberOnline(packet?.Index ?? -1, false, string.Empty);
    private void OnGuildMemberOnline(S.GuildMemberOnline packet) => _guildDialog?.SetMemberOnline(packet?.Index ?? -1, true, packet?.Name);
    private void OnGuildMemberContribution(S.GuildMemberContribution packet) => _guildDialog?.SetMemberContribution(packet?.Index ?? -1, packet?.Contribution ?? 0);
    private void OnGuildFundsChanged(S.GuildFundsChanged packet) => _guildDialog?.ChangeGuildFunds(packet?.Change ?? 0);
    private void OnGuildInvite(S.GuildInvite packet)
    {
        if (packet == null) return;
        OpenGuildDialog();
        _guildDialog?.ShowInvite(packet.Name, packet.GuildName);
    }
    private void OnCompanionRetrieve(S.CompanionRetrieve packet)
    {
        if (packet == null) return;
        Companion = Companions.FirstOrDefault(x => x?.Index == packet.Index);
        if (Companion == null) return;
        _companionDialog?.ApplyCompanion(Companion);
        _npcCompanionStorageDialog?.Refresh();
    }
    private void OnCompanionRelease(S.CompanionRelease packet)
    {
        if (packet == null) return;
        Companions.RemoveAll(x => x?.Index == packet.Index);
        Companion = null;
        _companionDialog?.ApplyCompanion(null);
        _npcCompanionStorageDialog?.RemoveCompanion(packet.Index);
    }
    private void OnQuestChanged(S.QuestChanged packet)
    {
        var quest = packet?.Quest;
        if (quest == null) return;
        if (quest.Quest == null) quest.Complete();
        _userQuests[quest.Index] = quest;
        RefreshQuestUi();
    }
    private void OnQuestCancelled(S.QuestCancelled packet)
    {
        if (packet == null) return;
        _userQuests.Remove(packet.Index);
        RefreshQuestUi();
    }
    private void RefreshQuestUi()
    {
        var quests = _userQuests.Values.Where(x => x?.Quest != null).ToList();
        _questTracker?.PopulateQuests(quests);
        _questDialog?.SetQuests(quests);
        _mainPanel?.SetQuestIndicators(quests.Any(x => !x.IsComplete), quests.Any(x => x.IsComplete));
    }
    private void OnGuildCastleInfo(S.GuildCastleInfo packet)
    {
        if (packet == null) return;
        _castleOwners[packet.Index] = packet.Owner ?? string.Empty;
        _guildDialog?.RefreshWarPage();
    }
    private void OnGuildConquestDate(S.GuildConquestDate packet)
    {
        if (packet == null) return;
        _castleWarDates[packet.Index] = packet.WarTime == TimeSpan.MinValue ? DateTime.MinValue : DateTime.Now + packet.WarTime;
        _guildDialog?.RefreshWarPage();
    }
    private void OnGuildConquestStarted(S.GuildConquestStarted packet)
    {
        if (packet == null) return;
        _castleWarDates[packet.Index] = DateTime.Now;
        _guildDialog?.RefreshWarPage();
    }
    private void OnGuildConquestFinished(S.GuildConquestFinished packet)
    {
        if (packet == null) return;
        _castleWarDates[packet.Index] = DateTime.MinValue;
        _guildDialog?.RefreshWarPage();
    }

    private void ConsumeNpcLinks(bool success, params IEnumerable<CellLinkInfo>[] groups)
    {
        var links = groups.Where(x => x != null).SelectMany(x => x).Where(x => x != null).ToList();
        if (links.Count == 0) return;
        _npcDialog?.ClearAdvancedLinks(links);
        OnItemsChanged(new S.ItemsChanged { Links = links, Success = success });
    }

    private void ReleaseNpcLinksWithoutConsuming(params IEnumerable<CellLinkInfo>[] groups)
    {
        var links = groups.Where(x => x != null).SelectMany(x => x).Where(x => x != null).ToList();
        if (links.Count == 0) return;
        _npcDialog?.ClearAdvancedLinks(links);
        foreach (var link in links) UnlockCell(link.GridType, link.Slot);
        DXItemCell.SelectedCell = null;
    }

    private void OnNPCRefinementStone(S.NPCRefinementStone packet)
    {
        if (packet == null) return;
        // 该包本身没有 Success；现行服务端通过后续 ItemsChanged 回包表达成功/失败。
        ConsumeNpcLinks(true, packet.IronOres, packet.SilverOres, packet.DiamondOres, packet.GoldOres, packet.Crystal);
        ReceiveChat(Lang.GameRefineLabel, MessageType.System);
    }

    private void OnNPCRefine(S.NPCRefine packet)
    {
        if (packet == null) return;
        ConsumeNpcLinks(packet.Success, packet.Ores, packet.Items, packet.Specials);
        ReceiveChat(packet.Success ? Lang.GameRefineLabel2 : Lang.GameRefineLabel3, MessageType.System);
    }

    private void OnNPCMasterRefine(S.NPCMasterRefine packet)
    {
        if (packet == null) return;
        ConsumeNpcLinks(packet.Success, packet.Fragment1s, packet.Fragment2s, packet.Fragment3s, packet.Stones, packet.Specials);
        ReceiveChat(packet.Success ? Lang.GameRefineLabel4 : Lang.GameRefineLabel5, MessageType.System);
    }

    private void OnNPCAccessoryLevelUp(S.NPCAccessoryLevelUp packet)
    {
        if (packet == null) return;
        var links = packet.Links?.ToList() ?? new List<CellLinkInfo>();
        if (packet.Target != null) links.Add(packet.Target);
        ReleaseNpcLinksWithoutConsuming(links);
    }

    private void OnNPCAccessoryUpgrade(S.NPCAccessoryUpgrade packet)
        => ReceiveChat(packet?.Success == true ? Lang.GameUpgradeLabel : Lang.GameUpgradeLabel2, MessageType.System);

    private void OnNPCAccessoryRefine(S.NPCAccessoryRefine packet)
    {
        if (packet == null) return;
        var links = packet.Links?.ToList() ?? new List<CellLinkInfo>();
        if (packet.Target != null) links.Add(packet.Target);
        if (packet.OreTarget != null) links.Add(packet.OreTarget);
        ReleaseNpcLinksWithoutConsuming(links);
        ReceiveChat(packet.Success ? Lang.GameRefineLabel6 : Lang.GameRefineLabel7, MessageType.System);
    }

    private void OnNPCWeaponCraft(S.NPCWeaponCraft packet)
    {
        if (packet == null) return;
        ConsumeNpcLinks(packet.Success, new[] { packet.Template, packet.Yellow, packet.Blue, packet.Red, packet.Purple, packet.Green, packet.Grey });
        ReceiveChat(packet.Success ? Lang.GameWeaponLabel : Lang.GameWeaponLabel2, MessageType.System);
    }

    private void OnNPCRefineRetrieve(S.NPCRefineRetrieve packet)
    {
        if (packet == null) return;
        _npcDialog?.RemoveRefine(packet.Index);
        ReceiveChat(string.Format(Lang.GameRefineLabel8, packet.Index), MessageType.System);
    }

    private void OnItemAcessoryRefined(S.ItemAcessoryRefined packet)
    {
        if (packet == null) return;
        var item = ItemAt(packet.GridType, packet.Slot);
        if (item != null) item.AddedStats = packet.NewStats;
        RefreshItemGrids();
    }

    private void OnReviveTimers(S.ReviveTimers packet)
    {
        if (packet == null) return;
        double now = Godot.Time.GetTicksMsec();
        ItemReviveUntilMs = now + packet.ItemReviveTime.TotalMilliseconds;
        ReincarnationPillUntilMs = now + packet.ReincarnationPillTime.TotalMilliseconds;
    }

    private void OnObservableSwitch(S.ObservableSwitch packet)
    {
        if (StartInfo != null) StartInfo.Observable = packet?.Allow == true;
    }

    private void OnGuildCreate(S.GuildCreate packet) => ReceiveChat(Lang.GameGuildLabel5, MessageType.System);
    private void OnGuildKick(S.GuildKick packet) => ReceiveChat(Lang.GameGuildLabel6, MessageType.System);
    private void OnGuildTax(S.GuildTax packet) => ReceiveChat(Lang.GameSettingsLabel, MessageType.System);
    private void OnGuildIncreaseMember(S.GuildIncreaseMember packet) => ReceiveChat(Lang.GameGuildLabel7, MessageType.System);
    private void OnGuildIncreaseStorage(S.GuildIncreaseStorage packet) => ReceiveChat(Lang.GameGuildLabel8, MessageType.System);
    private void OnGuildInviteMember(S.GuildInviteMember packet) => ReceiveChat(Lang.GameGuildLabel9, MessageType.System);
    private void OnGuildDayReset(S.GuildDayReset packet) => ReceiveChat(Lang.GameGuildLabel10, MessageType.System);
    private void OnRefineList(S.RefineList packet) => _npcDialog?.SetRefineList(packet?.List);
    private void OnMailList(List<ClientMailInfo> mails)
    {
        _communicationDialog?.SetMails(mails);
        _mainPanel?.SetMailIndicator(_communicationDialog?.HasUnread == true);
    }
    private void OnMailNew(ClientMailInfo mail)
    {
        _communicationDialog?.AddMail(mail);
        _mainPanel?.SetMailIndicator(true);
        if (AutoLoginArgs.OperationAuditExt && _operationAuditExtStage == 13)
        {
            _auditMailNewReceived = true;
            if (_operationAuditExtLastSuccess)
            {
                _operationAuditExtResponsePending = true;
                CallDeferred(nameof(ContinueOperationAuditExt));
            }
        }
    }
    private void OnMailDelete(int index)
    {
        _communicationDialog?.RemoveMail(index);
        _mainPanel?.SetMailIndicator(_communicationDialog?.HasUnread == true);
        if (AutoLoginArgs.OperationAuditExt && _operationAuditExtStage == 15)
        {
            _operationAuditExtLastSuccess = true;
            _operationAuditExtResponsePending = true;
            CallDeferred(nameof(ContinueOperationAuditExt));
        }
    }
    private void OnMailItemDelete(int index, int slot)
    {
        _communicationDialog?.RemoveMailItem(index, slot);
        if (AutoLoginArgs.OperationAuditExt && _operationAuditExtStage == 14)
        {
            _operationAuditExtLastSuccess = true;
            _operationAuditExtResponsePending = true;
            CallDeferred(nameof(ContinueOperationAuditExt));
        }
    }
    private void OnFriendUpdate(S.FriendUpdate packet) => _communicationDialog?.ApplyFriend(packet?.Info);
    private void OnFriendAdd(S.FriendAdd packet) => _communicationDialog?.ApplyFriend(packet?.Info);
    private void OnFriendRemove(S.FriendRemove packet) => _communicationDialog?.RemoveFriend(packet?.Index ?? -1);
    private void OnBlockList(IList<ClientBlockInfo> list) => _communicationDialog?.SetBlocks(list);
    private void OnBlockAdded(ClientBlockInfo info) => _communicationDialog?.ApplyBlock(info);
    private void OnBlockRemoved(int index) => _communicationDialog?.RemoveBlock(index);
    private void OnDisciplineUpdate(S.DisciplineUpdate packet) { if (StartInfo != null) StartInfo.Discipline = packet?.Discipline; _characterDialog?.RefreshDiscipline(); }
    private void OnDisciplineExperienceChanged(long experience) { if (StartInfo?.Discipline != null) StartInfo.Discipline.Experience = experience; _characterDialog?.RefreshDiscipline(); }
    private void OnMarriageInvite(S.MarriageInvite packet) => _guildDialog?.ShowMarriageInvite(packet?.Name);

    // 地面物品光效：所有掉落使用经典白色十字闪烁；稀有度光效沿用
    // 原版 ItemObject 的 Common+AddedStats / Superior / Elite 规则并叠加。
    private void SpawnItemGlow(ObjectRenderer ob, ClientUserItem item)
    {
        if (ob == null) return;
        var effects = new List<MirEffectNode>();
        _itemGlows[ob.ObjectID] = effects;

    // ProgUse 20..29：资源内的经典白色星芒/十字闪烁，10 帧循环。
    // 旧版实际显示的是小型星芒，而不是把 128 像素画布原尺寸铺开。
    // 0.5 倍约为旧版截图中的 25~35 像素可见尺寸；普通掉落使用更慢、更
    // 柔和的呼吸节奏，避免 100ms 快速循环造成“乱闪”；同时叠加平滑
    // 淡入淡出，让星芒不是单纯切帧闪烁。
        var sparkle = new MirEffectNode();
        AddChild(sparkle);
    sparkle.Setup(LibraryFile.ProgUse, 20, 10, 280, ob, ob.CellX, ob.CellY, null);
    sparkle.Loop = true;
    sparkle.Blend = true;
    sparkle.BlendRate = 0.25f;
    sparkle.SpriteScale = 0.5f;
    // Ground 图库物品本体以节点左上角 + (24,16) 居中绘制；ProgUse
    // 的资源偏移属于另一套特效锚点，不能直接套到地面物品上。
    sparkle.UseOffSet = false;
    sparkle.AdditionalOffX = 24;
    sparkle.AdditionalOffY = 16;
    sparkle.PulseFade = true;
    sparkle.PulseMinOpacity = 0.12f;
    sparkle.PulseMaxOpacity = 0.48f;
        effects.Add(sparkle);

        if (item?.Info == null) return;
        var info = item.Info;

        int fxIndex;
        Color lightColour;
        switch (info.Rarity)
        {
            case Rarity.Superior:
                fxIndex = 100; lightColour = Colors.PaleGreen; break;
            case Rarity.Elite:
                fxIndex = 120; lightColour = Colors.MediumPurple; break;
            default:
                // Common: 带附加属性且非零件才有光效
                if (item.AddedStats?.Count > 0 && info.ItemEffect != ItemEffect.ItemPart)
                {
                    fxIndex = 110; lightColour = Colors.DeepSkyBlue; break;
                }
                return;
        }

        var fx = new MirEffectNode();
        AddChild(fx);
        fx.Setup(LibraryFile.ProgUse, fxIndex, 10, 100, ob, ob.CellX, ob.CellY, null);
        fx.Loop = true;
        fx.Blend = true;
        fx.BlendRate = 0.5f;
        // 原版 ItemObject 的 MirEffect 构造参数为 (60, 60, colour)：
        // colour 是随帧光源颜色，不是序列帧贴图的染色。贴图本身始终白色
        // Blend 绘制，并在物品脚底所在行的物体特效层显示。
        fx.FrameLight = 60;
        fx.FrameLightColour = lightColour;
        effects.Add(fx);
    }

    private void OnObjectRemove(uint objectID)
    {
        // 与原版 CConnection.Process(S.ObjectRemove) 一致：先断开所有
        // 目标/悬停引用，再释放节点，避免自动攻击或悬停框访问迟到对象。
        _combatController?.RemoveObjectReference(objectID);
        if (_magicLockTargetObjectId == objectID)
            ClearMagicLock();
        ClearMovementEffect(objectID);
        if (_tamingRopes.Remove(objectID, out var rope)) rope.QueueFree();
        if (_spellEffects.Remove(objectID, out var spellFx)) spellFx.QueueFree();
        if (_durationSoundByObject.Remove(objectID, out var durationSound) &&
            !_durationSoundByObject.Values.Contains(durationSound))
            StopSound(durationSound);
        foreach (var key in _objectBuffEffects.Keys.Where(k => k.Item1 == objectID).ToList())
        {
            if (_objectBuffEffects.Remove(key, out var buffFx)) buffFx.QueueFree();
        }
        if (_objectPoisonEffects.Remove(objectID, out var poisonFx)) poisonFx.QueueFree();
        if (_itemGlows.Remove(objectID, out var glows))
            foreach (var glow in glows) glow.QueueFree();
        if (_otherPlayers.Remove(objectID, out var player)) player.QueueFree();
        bool removedGroundItem = _objects.Remove(objectID, out var ob) && ob.Type == ObjectRenderer.Kind.Item;
        ob?.QueueFree();
        if (removedGroundItem)
            RefreshGroundItemLabels(forceRedraw: true);
        _npcInfos.Remove(objectID);
        GD.Print($"[Game] 移除物体: ObjectID={objectID}");
        _miniMap?.RemoveObject(objectID);
        _bigMap?.RemoveObject(objectID);
    }

    private void OnObjectTurn(uint objectID, MirDirection dir, System.Drawing.Point loc, TimeSpan slow)
    {
        if (objectID == _playerObjectID && _player != null)
        {
            _playerDirection = dir;
            _player.Direction = dir;
            _mouseWalker?.AddMoveDelay(slow);
            if (_playerLocation != loc)
            {
                ApplyAuthoritativePlayerLocation(loc);
                _player.PlayStandingForState();
            }
            return;
        }

        if (_otherPlayers.TryGetValue(objectID, out var player))
        {
            player.StopRemoteMovement();
            player.Direction = dir;
            player.CellX = loc.X;
            player.CellY = loc.Y;
            UpdateOtherPlayerPosition(player);
            player.QueueRedraw();
        }
        else if (_objects.TryGetValue(objectID, out var ob))
        {
            ob.Direction = dir;
            ob.CellX = loc.X;
            ob.CellY = loc.Y;
            UpdateObjectPositions();
            ob.QueueRedraw();
        }
    }

    private void OnObjectHarvest(S.ObjectHarvest p)
    {
        if (p == null) return;
        if (p.ObjectID == _playerObjectID && _player != null)
        {
            _player.Direction = p.Direction;
            _player.PlayHarvest();
            ApplyAuthoritativePlayerLocation(p.Location, p.Slow);
        }
        else if (_otherPlayers.TryGetValue(p.ObjectID, out var player))
        {
            player.StopRemoteMovement();
            player.Direction = p.Direction;
            player.PlayHarvest();
        }
    }

    private void OnObjectMount(S.ObjectMount p)
    {
        if (p == null) return;
        if (p.ObjectID == _playerObjectID && _player != null)
        {
            _player.Horse = p.Horse;
            _player.RefreshAppearanceLibraries();
            _player.PlayStandingForState();
            _playerHorse = p.Horse;
            _horseDialog?.SetMountState(p.Horse == HorseType.None ? 0 : 1);
        }
        else if (_otherPlayers.TryGetValue(p.ObjectID, out var player))
        {
            player.Horse = p.Horse;
            player.RefreshAppearanceLibraries();
            player.PlayStandingForState();
        }
    }

    private void OnObjectDash(S.ObjectDash p)
    {
        if (p == null) return;
        ClearMovementEffect(p.ObjectID);
        if (p.ObjectID == _playerObjectID && _player != null)
        {
            _player.Direction = p.Direction;
            _player.BeginMove(p.Direction, Math.Max(1, p.Distance), _player.Horse != HorseType.None, false);
            ApplyAuthoritativePlayerLocation(p.Location);
            _player.PlayDash(p.Magic);
            SetMovementEffect(p.ObjectID, p.Magic, _player);
            if (p.Magic == MagicType.Assault) PlaySound(SoundIndex.AssaultStart);
        }
        else if (_otherPlayers.TryGetValue(p.ObjectID, out var player))
        {
            player.StopRemoteMovement();
            player.Direction = p.Direction;
            player.BeginMove(p.Direction, Math.Max(1, p.Distance), player.Horse != HorseType.None, false);
            player.PlayDash(p.Magic);
            SetMovementEffect(p.ObjectID, p.Magic, player);
            if (p.Magic == MagicType.Assault) PlaySound(SoundIndex.AssaultStart);
        }
    }

    private void OnObjectPushed(S.ObjectPushed p)
    {
        if (p == null) return;
        if (p.ObjectID == _playerObjectID && _player != null)
        {
            _player.Direction = p.Direction;
            _player.PlayPushed();
            ApplyAuthoritativePlayerLocation(p.Location);
        }
        else if (_otherPlayers.TryGetValue(p.ObjectID, out var player))
        {
            player.StopRemoteMovement();
            player.Direction = p.Direction;
            player.PlayPushed();
        }
    }

    private void OnObjectMining(S.ObjectMining p)
    {
        if (p == null) return;
        if (p.ObjectID == _playerObjectID && _player != null)
        {
            _player.Direction = p.Direction;
            _player.PlayMining();
            ApplyAuthoritativePlayerLocation(p.Location, p.Slow);
        }
        else if (_otherPlayers.TryGetValue(p.ObjectID, out var player))
        {
            player.StopRemoteMovement();
            player.Direction = p.Direction;
            player.PlayMining();
        }
    }

    // ---- M5 战斗 ----

    // 攻击: 攻击者播攻击动画, 被攻击者播 Struck
    private void OnObjectAttack(S.ObjectAttack p)
    {
        uint objectID = p.ObjectID;
        MirDirection dir = p.Direction;
        System.Drawing.Point loc = p.Location;
        MagicType magic = p.AttackMagic;
        uint targetID = p.TargetID;
        ClearMovementEffect(objectID);
        if (objectID == _playerObjectID)
        {
            if (_player != null)
            {
                _player.Direction = dir;
                _player.PlayCombat(magic);
                if (magic != MagicType.DanceOfSwallow)
                    ApplyAuthoritativePlayerLocation(loc, p.Slow);
            }
        }
        else if (_otherPlayers.TryGetValue(objectID, out var player))
        {
            player.StopRemoteMovement();
            player.CellX = loc.X;
            player.CellY = loc.Y;
            player.Direction = dir; player.PlayCombat(magic);
            UpdateOtherPlayerPosition(player);
        }
        else if (_objects.TryGetValue(objectID, out var ob))
            {
                ob.Direction = dir;
                ob.PlayRangeAttack();
        }

        if (targetID != 0)
        {
            if (targetID == _playerObjectID)
            {
                if (_player != null) _player.PlayStruck();
            }
            else if (_otherPlayers.TryGetValue(targetID, out var targetPlayer)) targetPlayer.PlayStruck();
            else if (_objects.TryGetValue(targetID, out var tgt))
            {
                tgt.SetAnimation(MirAnimation.Struck);
                tgt.PlayStruckSound();
            }

            var attackEffect = MagicEffectTable.GetAttack(magic);
            var attackSource = GetMagicTargetNode(objectID);
            var attackTarget = GetMagicTargetNode(targetID);
            if (attackEffect != null && attackSource != null)
                SpawnImpactTarget(attackEffect, attackSource, dir);
            if (magic == MagicType.Chain && attackTarget != null)
            {
                var sourceTarget = GetMagicTargetNode(objectID);
                if (sourceTarget != null)
                {
                    var line = new MirLineEffectNode();
                    AddChild(line);
                    line.Setup(sourceTarget, attackTarget, LibraryFile.MagicEx7, 80, 1f, 3000);
                }
            }
        }
    }

    private void OnObjectRangeAttack(S.ObjectRangeAttack p)
    {
        uint objectID = p.ObjectID;
        MirDirection dir = p.Direction;
        System.Drawing.Point loc = p.Location;
        MagicType magic = p.AttackMagic;
        List<uint> targets = p.Targets;
        ClearMovementEffect(objectID);
        if (objectID == _playerObjectID)
        {
            _player.Direction = dir;
            _player.PlayRangeAttack();
            ApplyAuthoritativePlayerLocation(loc);
        }
        else if (_otherPlayers.TryGetValue(objectID, out var player))
        {
            player.StopRemoteMovement();
            player.CellX = loc.X;
            player.CellY = loc.Y;
            player.Direction = dir; player.PlayRangeAttack();
            UpdateOtherPlayerPosition(player);
        }
        else if (_objects.TryGetValue(objectID, out var ob))
        {
            ob.Direction = dir;
            ob.SetAnimation(MirAnimation.Combat1);
        }

        foreach (uint targetID in targets ?? Enumerable.Empty<uint>())
        {
            if (targetID == _playerObjectID) _player.PlayStruck();
            else if (_otherPlayers.TryGetValue(targetID, out var targetPlayer)) targetPlayer.PlayStruck();
            else if (_objects.TryGetValue(targetID, out var target))
            {
                target.SetAnimation(MirAnimation.Struck);
                target.PlayStruckSound();
            }
        }

        // 原版 RangeAttack 的 Shuriken: MirProjectile(1270,3,100ms,MagicEx,
        // light 1,5,NoneColour, 施法者格){ Blend=false, Explode=true,
        // Delay=2 (2 倍慢), Has16Directions=true, MapTarget=目标格 }。
        // 旧端遗漏了这枚飞行物，只播了挥击/受击动画。
        if (magic == MagicType.Shuriken)
        {
            foreach (uint targetID in targets ?? Enumerable.Empty<uint>())
            {
                System.Drawing.Point targetCell;
                if (targetID == _playerObjectID) targetCell = _playerLocation;
                else if (_otherPlayers.TryGetValue(targetID, out var targetPlayer))
                    targetCell = new System.Drawing.Point(targetPlayer.CellX, targetPlayer.CellY);
                else if (_objects.TryGetValue(targetID, out var target))
                    targetCell = new System.Drawing.Point(target.CellX, target.CellY);
                else continue;
                SpawnProjectileDefinition(new MagicEffectTable.ProjectileDef
                {
                    File = LibraryFile.MagicEx, StartIndex = 1270, FrameCount = 3,
                    Colour = MagicEffectTable.None, Explode = true, Has16Directions = true,
                    FrameLight = 1,
                }, loc.X, loc.Y, targetCell.X, targetCell.Y, null, blend: false, delay: 2);
            }
        }
    }

    private void ApplyAuthoritativePlayerLocation(System.Drawing.Point loc, TimeSpan slow = default)
    {
        if (_player == null) return;
        _moveServerLockUntilMs = 0;  // 权威位置应用即解锁, 覆盖所有纠正路径
        _moveVisualLockUntilMs = 0;
        _playerLocation = loc;
        _pendingDistance = 1;
        _moveFrameCount = 1;
        _player.OffsetX = 0f;
        _player.OffsetY = 0f;
        _player.CellX = loc.X;
        _player.CellY = loc.Y;
        UpdatePlayerPosition();
        if (slow > TimeSpan.Zero)
            _mouseWalker?.AddMoveDelay(slow);
    }

    // 魔法施放：先进入旧端对应的抬手/施法动作，释放表现等待动作的
    // 释放关键帧后再创建投射物、命中和地面特效。这样不会在抬手第一帧
    // 就把整套魔法结果一次性显示出来。
    private void OnObjectMagic(uint objectID, MirDirection dir, System.Drawing.Point loc, MagicType type, List<uint> targets, List<System.Drawing.Point> locations, bool cast)
    {
        GD.Print($"[Magic] OnObjectMagic type={type} cast={cast} targets={targets?.Count ?? 0} locs={locations?.Count ?? 0} loc=({loc.X},{loc.Y})");
        Node2D renderer = null;
        int spellInstance = 0;
        if (objectID == _playerObjectID)
        {
            if (_player != null)
            {
                _player.Direction = dir;
                renderer = _player;
                spellInstance = _player.PlaySpell(type);
            }
        }
        else if (_otherPlayers.TryGetValue(objectID, out var player))
        {
            player.Direction = dir;
            renderer = player;
            spellInstance = player.PlaySpell(type);
        }
        else if (_objects.TryGetValue(objectID, out var ob))
        {
            ob.Direction = dir;
            renderer = ob;
            spellInstance = ob.PlaySpell(type);
        }

        // 原版 SetFrame 的 start switch (MapObject.cs:3627+) 不检查 MagicCast:
        // 抬手特效与音效无条件播放；!cast 只跳过 release switch。
        RenderObjectMagicStart(objectID, dir, loc, type, locations);

        if (!cast) return;  // 原版: !MagicCast 时不播释放特效

        if (renderer != null && spellInstance != 0)
        {
            // 原版释放 = Spell 动作结束 (SetAction 进入下一个动作时执行 release
            // switch)。用施法实例号过滤，避免快速连放/动画排队时旧释放被误触发。
            void Handler(int instance, MagicType ended)
            {
                if (instance != spellInstance || ended != type) return;
                if (renderer is PlayerRenderer p) p.SpellAnimEnded -= Handler;
                else if (renderer is MapObjectNode m) m.SpellAnimEnded -= Handler;
                RenderObjectMagic(objectID, dir, loc, type, targets, locations);
            }
            if (renderer is PlayerRenderer rp) rp.SpellAnimEnded += Handler;
            else if (renderer is MapObjectNode rm) rm.SpellAnimEnded += Handler;
        }
        else
        {
            // 无渲染器 (施法者已消失): 退化为固定延迟后播放释放。
            GetTree().CreateTimer(0.3).Timeout += () =>
                RenderObjectMagic(objectID, dir, loc, type, targets, locations);
        }
    }

    private void RenderObjectMagicStart(uint objectID, MirDirection dir, System.Drawing.Point loc,
        MagicType type, List<System.Drawing.Point> locations)
    {
        // 原版 MapObject.SetAction Spell 的 start switch (MapObject.cs:3627+) 不检查
        // MagicCast —— 抬手特效与音效无条件播放；!cast 只跳过 release switch。
        var def = MagicEffectTable.Get(type);

        // 旧端 SetAction 使用动作包中的 CurrentLocation 作为施法者当前格。
        int sourceX = loc.X;
        int sourceY = loc.Y;

        // 原版 start switch 的种族门控: Race != ObjectType.Monster 才进
        // ScortchedEarth 的抬手特效与 LavaStrikeStart (怪物不播抬手)。
        if (objectID != _playerObjectID && !_otherPlayers.ContainsKey(objectID)
            && type == MagicType.ScortchedEarth) return;

        // Start 音效: 无条件播放 (含 ThunderKick 的双音效 start 表)。
        foreach (var sound in MagicSoundCatalog.ResolveAll(type, MagicSoundPhase.Start))
            PlaySound(sound);

        if (def == null) return;  // 纯音效 case (WaningMoon/CalamityOfFullMoon 等)

        // MapLocations 和 AttackTargets 在旧端是两条不同的锚定路径：
        // 前者是 MapTarget 地面坐标，后者是实时 Target 对象坐标。
        var destCells = new List<(int x, int y)>();
        foreach (var lp in locations) destCells.Add((lp.X, lp.Y));

        if (def.CastAtSource)
        {
            var sourceNode = GetMagicTargetNode(objectID);
            if (sourceNode != null)
                SpawnCastEffectTarget(def, sourceNode, def.DirectionFromCast ? dir : MirDirection.Up);
            else SpawnCastEffect(def, sourceX, sourceY, int.MinValue, int.MinValue, dir);
        }
        if (def.Source != null)
        {
            var sourceNode = GetMagicTargetNode(objectID);
            if (sourceNode != null)
                SpawnImpactTarget(def.Source, sourceNode, def.DirectionFromCast ? dir : MirDirection.Up);
            else
                SpawnImpact(def.Source, sourceX, sourceY);
            foreach (var extra in def.SourceAdditional)
            {
                if (sourceNode != null)
                    SpawnImpactTarget(extra, sourceNode, def.DirectionFromCast ? dir : MirDirection.Up);
                else
                    SpawnImpact(extra, sourceX, sourceY);
            }
        }

        // 旧端按 MagicLocations/AttackTargets 分别挂载特效：
        // MapTarget 使用地面格坐标，Target 使用对象坐标；不能先在施法者
        // 位置统一生成一个 CastEffect 再把它当作所有技能的表现。
        if (def.SourcePerLocation.Count > 0)
        {
            // 原版 LightningBeam: 每个 MagicLocation 在施法者身上各播一次
            // MirEffect(1180,4,...){ Target=this, Direction=DirectionFromPoint(施法者, 格) }。
            // 必须挂在施法者节点 (光束从施法者指向格子)，不能播在地面格上。
            var sourceNode = GetMagicTargetNode(objectID);
            if (sourceNode != null)
            {
                var sourceCell = GetTargetCell(sourceNode);
                foreach (var (x, y) in destCells)
                {
                    foreach (var perLoc in def.SourcePerLocation)
                    {
                        var fx = new MirEffectNode();
                        AddChild(fx);
                        // 原版是 Target=this + Direction*Skip 选帧 (MirEffect.Draw)，
                        // 不是按方向分组帧，因此用 StartIndex 原值，方向交给 fx.Direction。
                        fx.SetupTarget(perLoc.File, perLoc.StartIndex, perLoc.FrameCount,
            perLoc.DelayMs, sourceNode, () => GetTargetRenderY(sourceNode));
                        fx.Blend = true;
                        fx.DrawType = perLoc.DrawType;
                        fx.BlendRate = perLoc.BlendRate;
                        fx.Opacity = perLoc.Opacity;
                        fx.Skip = perLoc.Skip;
                        fx.SetStartDelay(perLoc.StartDelayMs);
                        fx.Direction = Functions.DirectionFromPoint(
                            new System.Drawing.Point(sourceCell.X, sourceCell.Y), new System.Drawing.Point(x, y));
                        fx.FrameLight = perLoc.FrameLight;
                        fx.FrameLightColour = perLoc.Colour;
                    }
                }
            }
        }

    }

    // 原版 SetAction 离开 Spell 动作 (动画播完/被打断) 时执行 release switch。
    // 名字与签名保持不变: DeadTargetAudit 直接反射调用它。
    private void RenderObjectMagic(uint objectID, MirDirection dir, System.Drawing.Point loc,
        MagicType type, List<uint> targets, List<System.Drawing.Point> locations)
    {
        var def = MagicEffectTable.Get(type);

        // 旧端 SetAction 使用动作包中的 CurrentLocation 作为施法者当前格。
        int sourceX = loc.X;
        int sourceY = loc.Y;

        var destCells = new List<(int x, int y)>();
        foreach (var lp in locations) destCells.Add((lp.X, lp.Y));

        // 目标受击动画
        foreach (uint tid in targets)
            if (_otherPlayers.TryGetValue(tid, out var targetPlayer)) targetPlayer.PlayStruck();
        foreach (uint tid in targets)
            if (_objects.TryGetValue(tid, out var tgt)) tgt.SetAnimation(MirAnimation.Struck);

        // 旧端没有对应 case 时不生成伪造的通用爆炸；否则一个未覆盖的
        // MagicType 会被错误地表现成“所有技能都是火球落地”。
        if (def == null && !MagicEffectTable.IsNoVisualSpellCase(type))
        {
            GD.PrintErr($"[Magic] 未迁移技能轨迹: type={type} source=({sourceX},{sourceY}) " +
                $"targets={targets?.Count ?? 0} locations={destCells.Count}; " +
                Lang.GameSkillLabel);
            return;
        }

        if (def == null) return;

        // ==== 地面格 (MagicLocations) 特效 ====
        int destinationIndex = 0;
        foreach (var (x, y) in destCells)
        {
            double projectileDelay = destinationIndex * def.ProjectileDelayStepMs;
            if (!def.NoLocationVisual)
            {
                if (def.Projectile != null)
                {
                    // 原版地面格弹道 + CompleteAction 落点特效 (MapImpact)。
                    // BlowEarth 的弹道只到最后一个 MagicLocation (逐点后移)。
                    if (!def.ProjectileLastLocationOnly || destinationIndex == destCells.Count - 1)
                        SpawnProjectileDefinition(def.Projectile, sourceX, sourceY, x, y, def.MapImpact, projectileDelay);
                }
                else if (def.MapImpact != null)
                {
                    SpawnImpact(def.MapImpact, x, y, sourceX, sourceY);
                }
                else if (def.Source == null && !def.CastAtSource)
                {
                    SpawnCastEffect(def, x, y, sourceX, sourceY, dir);
                }
                foreach (var extraProjectile in def.AdditionalProjectiles)
                    SpawnProjectileDefinition(extraProjectile, sourceX, sourceY, x, y, null, projectileDelay);
                foreach (var extra in def.Additional) SpawnImpact(extra, x, y, sourceX, sourceY);
                foreach (var extra in def.AdditionalMapEffects)
                    SpawnImpact(extra, x + extra.OffsetX, y + extra.OffsetY, sourceX, sourceY);
            }
            destinationIndex++;
        }

        // ==== 目标 (AttackTargets) 特效 ====
        int targetIndex = 0;
        foreach (uint tid in targets)
        {
            var targetNode = GetMagicTargetNode(tid);
            if (targetNode != null)
            {
                var targetCell = GetTargetCell(targetNode);
                var targetDirection = def.TargetEffect?.DirectionFromCast == true || def.Impact?.DirectionFromCast == true
                    ? dir
                    : def.DirectionFromSource
                    ? Functions.DirectionFromPoint(new System.Drawing.Point(sourceX, sourceY), targetCell)
                    : MirDirection.Up;
                if (!def.NoTargetVisual)
                {
                    if (def.TargetEffect != null)
                        SpawnImpactTarget(def.TargetEffect, targetNode, targetDirection);
                    else if (def.TargetProjectile != null)
                        SpawnProjectileTarget(def, sourceX, sourceY, targetNode);
                    else if (def.Projectile != null)
                        SpawnProjectileTarget(def, sourceX, sourceY, targetNode);
                    else if (def.Impact != null)
                        SpawnImpactTarget(def.Impact, targetNode, targetDirection);
                    // 原版对 AttackTargets 无特效的 spell (纯 per-loc/per-caster,
                    // 如怪物风暴/Sama 系) 不在这里伪造 SpawnCastEffect。
                    var targetAdditionalProjectiles = def.TargetAdditionalProjectiles.Count > 0
                        ? def.TargetAdditionalProjectiles : def.AdditionalProjectiles;
                    foreach (var extraProjectile in targetAdditionalProjectiles)
                        SpawnProjectileDefinitionTarget(extraProjectile, sourceX, sourceY, targetNode, null);
                    foreach (var extra in def.Additional) SpawnImpactTarget(extra, targetNode);
                }
            }
            else if (_objects.TryGetValue(tid, out var tgt))
            {
                var targetCell = new System.Drawing.Point(tgt.CellX, tgt.CellY);
                var targetDirection = def.TargetEffect?.DirectionFromCast == true || def.Impact?.DirectionFromCast == true
                    ? dir
                    : def.DirectionFromSource
                    ? Functions.DirectionFromPoint(new System.Drawing.Point(sourceX, sourceY), targetCell)
                    : MirDirection.Up;
                if (!def.NoTargetVisual)
                {
                    if (def.TargetEffect != null)
                        SpawnImpactTarget(def.TargetEffect, tgt, targetDirection);
                    else if (def.TargetProjectile != null) SpawnProjectileTarget(def, sourceX, sourceY, tgt);
                    else if (def.Projectile != null) SpawnProjectile(def, sourceX, sourceY, tgt.CellX, tgt.CellY);
                    else if (def.Impact != null) SpawnImpactTarget(def.Impact, tgt, targetDirection);
                    var targetAdditionalProjectiles = def.TargetAdditionalProjectiles.Count > 0
                        ? def.TargetAdditionalProjectiles : def.AdditionalProjectiles;
                    foreach (var extraProjectile in targetAdditionalProjectiles)
                        SpawnProjectileDefinitionTarget(extraProjectile, sourceX, sourceY, tgt, null);
                    foreach (var extra in def.Additional) SpawnImpact(extra, tgt.CellX, tgt.CellY, sourceX, sourceY);
                }
                foreach (var extra in def.AdditionalMapEffects)
                    SpawnImpact(extra, tgt.CellX + extra.OffsetX, tgt.CellY + extra.OffsetY, sourceX, sourceY);
            }
            else
            {
                // 目标已被完全移除 (不在场景节点也不在 _objects 缓存)，
                // 退回到对应格子保底播放 Impact/Projectile，避免命中特效凭空消失。
                if (!def.NoTargetVisual && destCells.Count > 0)
                {
                    int cellIndex = Math.Min(targetIndex, destCells.Count - 1);
                    var (fallbackX, fallbackY) = destCells[cellIndex];
                    if (def.TargetEffect != null)
                        SpawnImpact(def.TargetEffect, fallbackX, fallbackY, sourceX, sourceY);
                    else if (def.Projectile != null)
                        SpawnProjectile(def, sourceX, sourceY, fallbackX, fallbackY);
                    else if (def.Impact != null)
                        SpawnImpact(def.Impact, fallbackX, fallbackY, sourceX, sourceY);
                    else
                        SpawnCastEffect(def, fallbackX, fallbackY, sourceX, sourceY, dir);
                }
            }
            targetIndex++;
        }

        // 释放阶段挂在施法者自身 (原版 release 的 Target=this, 如 DarkSoulPrison 600,9)。
        if (def.ReleaseAtCaster)
        {
            SpawnCastEffect(def, sourceX, sourceY, sourceX, sourceY, dir);
        }
        // 没有目标/地点的站桩类技能才挂在施法者当前位置。
        else if (destCells.Count == 0 && def.Projectile == null && !def.CastAtSource && def.Source == null)
        {
            SpawnCastEffect(def, sourceX, sourceY, sourceX, sourceY, dir);
            foreach (var extra in def.AdditionalMapEffects)
                SpawnImpact(extra, sourceX + extra.OffsetX, sourceY + extra.OffsetY, sourceX, sourceY);
        }

        // 释放结束音效 (原版 release switch 末尾的 Play, 按 Locations/Targets 门控)。
        foreach (var spec in MagicSoundCatalog.ResolveSpecs(type, MagicSoundPhase.End))
            if (MagicSoundCatalog.GateSatisfied(spec.Gate, locations?.Count > 0, targets?.Count > 0))
                PlaySound(spec.Sound);
    }

    // 原版 PlayerObject.FrameIndexChanged 中由动作关键帧触发的本地表现。
    // 网络魔法包负责目标/轨迹；这里仅补必须与人物挥击帧同步的本地事件。
    private void OnPlayerFrameChanged(PlayerRenderer source, MirAnimation animation, int frame, MagicType magic)
    {
        if (source == null) return;
        if (animation == MirAnimation.TamingCast && frame == 5 &&
            source.TamingObjectID != 0 && _objects.TryGetValue(source.TamingObjectID, out var tamingTarget))
        {
            if (_tamingRopes.Remove(source.TamingObjectID, out var oldRope)) oldRope.QueueFree();
            var rope = new MirRopeEffectNode();
            AddChild(rope);
            rope.Setup(source, tamingTarget);
            _tamingRopes[source.TamingObjectID] = rope;
        }
        if (animation == MirAnimation.FishingCast && frame == 1)
        {
            SpawnCastEffect(new MagicEffectTable.CastEffect
            {
                File = LibraryFile.MagicEx5, StartIndex = 1400, FrameCount = 6,
                DelayMs = 120, Blend = true, BlendRate = 0.8f,
                Colour = MagicEffectTable.None
            }, source.FishingLocation.X, source.FishingLocation.Y);
        }
        if (animation == MirAnimation.FishingWait && frame == 1)
        {
            SpawnCastEffect(new MagicEffectTable.CastEffect
            {
                File = LibraryFile.MagicEx5,
                StartIndex = source.FishFound ? 1400 : 1420,
                FrameCount = 6, DelayMs = 120, Blend = true,
                BlendRate = 0.8f, Colour = MagicEffectTable.None
            }, source.FishingLocation.X, source.FishingLocation.Y);
        }
        if (magic == MagicType.SeismicSlam && frame == 4)
        {
            var def = new MagicEffectTable.ImpactDef
            {
                File = LibraryFile.MonMagicEx7, StartIndex = 700, FrameCount = 7,
                DelayMs = 120, Colour = MagicEffectTable.Lightning, BlendRate = 0.8f
            };
            var point = Functions.Move(new System.Drawing.Point(source.CellX, source.CellY),
                source.Direction, 2);
            SpawnImpact(def, point.X, point.Y);
        }
        if (magic == MagicType.CrushingWave && frame == 4)
        {
            var projectile = new MagicEffectTable.ProjectileDef
            {
                File = LibraryFile.MagicEx6, StartIndex = 200, FrameCount = 8,
                DelayMs = 100, Colour = MagicEffectTable.Lightning,
                Has16Directions = false, FrameLight = 35
            };
            SpawnProjectileDefinition(projectile, source.CellX, source.CellY,
                Functions.Move(new System.Drawing.Point(source.CellX, source.CellY),
                    source.Direction, Globals.MagicRange).X,
                Functions.Move(new System.Drawing.Point(source.CellX, source.CellY),
                    source.Direction, Globals.MagicRange).Y, null);
            var wave = new MagicEffectTable.ImpactDef
            {
                File = LibraryFile.MagicEx6, StartIndex = 300, FrameCount = 9,
                DelayMs = 150, Colour = MagicEffectTable.Lightning,
                DirectionFromSource = true
            };
            var near = Functions.Move(new System.Drawing.Point(source.CellX, source.CellY), source.Direction, 1);
            SpawnImpact(wave, near.X, near.Y, source.CellX, source.CellY);
        }
        if (magic == MagicType.OffensiveBlow && frame == 3)
        {
            SpawnImpact(new MagicEffectTable.ImpactDef
            {
                File = LibraryFile.MagicEx5, StartIndex = 2305, FrameCount = 5,
                DelayMs = 100, Colour = MagicEffectTable.Fire, Skip = 10
            }, source.CellX, source.CellY, source.CellX, source.CellY);
        }
    }

    public void PlaySound(SoundIndex sound)
    {
        if (sound == SoundIndex.None) return;
        if (!SoundCatalog.TryGet(sound, out var entry))
        {
            GD.PrintErr($"[Sound] 原版音效索引没有迁移映射: {sound}");
            return;
        }
        if (!_actionSounds.TryGetValue(sound, out var stream))
        {
            string resourcePath = "res://../Debug/Client/Sound/" + entry.FileName;
            string filePath = ProjectSettings.GlobalizePath(resourcePath);
            // Prefer the original WAV on disk; asking ResourceLoader to resolve
            // an outside-res:// path emits a noisy loader error before fallback.
            stream = File.Exists(filePath) ? AudioStreamWav.LoadFromFile(filePath) : null;
            if (stream == null && !File.Exists(filePath))
                stream = ResourceLoader.Load<AudioStream>(resourcePath);
            // Debug/Client/Sound is deliberately outside the Godot project and
            // therefore may not be importable as res:// in a packaged client.
            // Load the original WAV from its absolute workspace path as a
            // fallback, preserving the old client's numbered sound assets.
            if (stream == null)
            {
                stream = AudioStreamWav.LoadFromFile(filePath);
            }
            if (stream == null)
            {
                GD.PrintErr($"[Sound] 无法加载音效 {sound} ({entry.FileName}): {filePath}");
                return;
            }
            _actionSounds[sound] = stream;
        }
        if (entry.Loop && _loopingSounds.TryGetValue(sound, out var activeLoop) && IsInstanceValid(activeLoop))
            return;
        if (entry.Loop && stream is AudioStreamWav loopWav)
            loopWav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        // 按音效分类走对应总线（设置页 5 类音量/静音的消费端）
        var player = new AudioStreamPlayer { Stream = stream, Bus = ClientSettings.BusFor(entry.Category) };
        AddChild(player);
        if (entry.Loop)
        {
            _loopingSounds[sound] = player;
            player.Finished += () =>
            {
                _loopingSounds.Remove(sound);
                player.QueueFree();
            };
        }
        else
            player.Finished += player.QueueFree;
        player.Play();
    }

    public void StopSound(SoundIndex sound)
    {
        if (!_loopingSounds.Remove(sound, out var player)) return;
        if (IsInstanceValid(player)) player.QueueFree();
    }

    // 原版 CConnection.Process(S.ObjectProjectile) 只处理这 4 个 MagicType，
    // 其余类型静默忽略；不再用 def.Projectile 泛化渲染 (会漏掉 ChainLightning
    // 的逐点落雷和 ElementalSwords 的两段式起手)。
    private void OnObjectProjectile(S.ObjectProjectile packet)
    {
        if (!_objects.ContainsKey(packet.ObjectID) && packet.ObjectID != _playerObjectID) return;
        int sourceX = packet.CurrentLocation.X;
        int sourceY = packet.CurrentLocation.Y;
        var def = MagicEffectTable.Get(packet.Type);

        switch (packet.Type)
        {
            case MagicType.ChainLightning:
                // 每个 MagicLocation 一个 MirEffect(470,10) + ChainLightningEnd。
                if (def?.MapImpact != null)
                    foreach (var p in packet.Locations)
                        SpawnImpact(def.MapImpact, p.X, p.Y, sourceX, sourceY);
                if (packet.Locations.Count > 0) PlaySound(SoundIndex.ChainLightningEnd);
                break;
            case MagicType.LightningStrike:
                // 每个 AttackTarget 一个 MirProjectile(500,8 Skip0)，落地特效由
                // release 包承担 (LightningStrikeEnd 已随 release 播过)。
                if (def != null)
                    foreach (uint id in packet.Targets)
                    {
                        var target = GetMagicTargetNode(id);
                        if (target != null)
                            SpawnProjectileDefinitionTarget(def.TargetProjectile ?? def.Projectile,
                                sourceX, sourceY, target, null, SoundIndex.LightningBeamEnd);
                    }
                if (packet.Targets.Count > 0) PlaySound(SoundIndex.LightningBeamEnd);
                break;
            case MagicType.FireBounce:
                // 每个 AttackTarget 一个 MirProjectile(1640,6) + 落地
                // MirEffect(1800,10) + GreaterFireBallEnd；有目标时再播 Travel。
                if (def != null)
                    foreach (uint id in packet.Targets)
                    {
                        var target = GetMagicTargetNode(id);
                        if (target != null)
                            SpawnProjectileTarget(def, sourceX, sourceY, target, SoundIndex.GreaterFireBallEnd);
                    }
                if (packet.Targets.Count > 0) PlaySound(SoundIndex.GreaterFireBallTravel);
                break;
            case MagicType.ElementalSwords:
                // 每个 AttackTarget: MirEffect(300,5 MagicEx10 0,0 Skip10,
                // Direction=施法者朝向, MapTarget=当前格) 完成后再朝目标发
                // MirProjectile(0,3 MagicEx10 Has16Directions) + ElementalSwordsEnd。
                foreach (uint id in packet.Targets)
                {
                    var target = GetMagicTargetNode(id);
                    if (target != null)
                        SpawnElementalSwords(packet, target, sourceX, sourceY);
                    PlaySound(SoundIndex.ElementalSwordsEnd);
                }
                break;
        }
    }

    private void OnObjectSpell(S.ObjectSpell packet)
    {
        var durationSound = packet.Effect switch
        {
            SpellEffect.FireWall => SoundIndex.FireWallDuration,
            SpellEffect.Tempest => SoundIndex.TempestDuration,
            SpellEffect.PoisonousCloud => SoundIndex.PoisonousCloudStart,
            SpellEffect.DarkSoulPrison => SoundIndex.DarkSoulPrison,
            SpellEffect.MonsterDeathCloud => SoundIndex.JinchonDevilAttack3,
            SpellEffect.Rubble => SoundIndex.MiningStruck,
            _ => SoundIndex.None,
        };
        PlaySound(durationSound);
        if (_durationSoundByObject.Remove(packet.ObjectID, out var oldDuration) &&
            oldDuration != durationSound && !_durationSoundByObject.Values.Contains(oldDuration))
            StopSound(oldDuration);
        if (durationSound != SoundIndex.None)
            _durationSoundByObject[packet.ObjectID] = durationSound;
        if (_spellEffects.Remove(packet.ObjectID, out var oldFx)) oldFx.QueueFree();
        var config = packet.Effect switch
        {
            SpellEffect.SafeZone => (LibraryFile.Magic, 649, 1, 365000, 0.3f, MagicEffectTable.None),
            SpellEffect.FireWall => (LibraryFile.Magic, 920, 5, 150, 0.55f, MagicEffectTable.Fire),
            SpellEffect.Tempest => (LibraryFile.MagicEx2, 920, 10, 150, 0.55f, MagicEffectTable.Wind),
            SpellEffect.IceAura => (LibraryFile.MagicEx5, 2600, 10, 150, 0.55f, MagicEffectTable.Ice),
            SpellEffect.TrapOctagon => (LibraryFile.Magic, 200, 6, 100, 0.7f, MagicEffectTable.Dark),
            SpellEffect.DarkSoulPrison => (LibraryFile.MagicEx6, 700, 10, 100, 0.7f, MagicEffectTable.Dark),
            SpellEffect.PoisonousCloud => (LibraryFile.MagicEx4, 400, 15, 100, 0.7f, MagicEffectTable.Dark),
            SpellEffect.BurningFire => (LibraryFile.MagicEx6, 1000, 8, 100, 1f, MagicEffectTable.Fire),
            SpellEffect.Rubble => (LibraryFile.ProgUse, 230, 1, 100, 1f, MagicEffectTable.None),
            SpellEffect.MonsterDeathCloud => (LibraryFile.MonMagicEx2, 850, 10, 100, 1f, MagicEffectTable.Dark),
            SpellEffect.ZombieHole => (LibraryFile.ProgUse, 240 + (int)packet.Direction, 1, 100, 1f, MagicEffectTable.None),
            _ => (LibraryFile.Magic, -1, 0, 0, 0f, Colors.White),
        };
        if (config.Item2 < 0) return;
        var fx = new MirEffectNode();
        AddChild(fx);
        fx.Setup(config.Item1, config.Item2, config.Item3, config.Item4, null,
            packet.Location.X, packet.Location.Y,
            () => ComputeEffectScreenPos(packet.Location.X, packet.Location.Y));
        fx.Direction = packet.Direction;
        fx.Loop = true;
        fx.Blend = config.Item5 < 1f;
        fx.BlendRate = config.Item5;
        fx.DrawType = EffectLayerFloor(packet.Effect);
        fx.FrameLight = 10;
        fx.FrameLightColour = config.Item6;
        _spellEffects[packet.ObjectID] = fx;
    }

    private void OnObjectSpellChanged(S.ObjectSpellChanged packet)
    {
        // 旧端按 Power 更新 SpellObject 的伤害/素材，帧段不随威力改变；
        // 保留包处理以免被误报为未处理网络包。
        if (_spellEffects.TryGetValue(packet.ObjectID, out var fx)) fx.QueueRedraw();
    }

    private static MirEffectNode.EffectLayer EffectLayerFloor(SpellEffect effect)
    {
        return effect is SpellEffect.FireWall or SpellEffect.Tempest or SpellEffect.IceAura
            or SpellEffect.TrapOctagon or SpellEffect.PoisonousCloud or SpellEffect.BurningFire
            or SpellEffect.Rubble or SpellEffect.ZombieHole
            ? MirEffectNode.EffectLayer.Floor : MirEffectNode.EffectLayer.Object;
    }

    private void OnObjectEffect(uint objectID, Effect effect)
    {
        var target = GetMagicTargetNode(objectID);
        if (target == null) return;
        PlayEffectSound(effect);
        var def = effect switch
        {
            Effect.TeleportOut => new MagicEffectTable.ImpactDef { File = LibraryFile.Magic, StartIndex = 110, FrameCount = 10, Colour = Colors.White, BlendRate = 0.6f },
            Effect.TeleportIn => new MagicEffectTable.ImpactDef { File = LibraryFile.Magic, StartIndex = 110, FrameCount = 10, Colour = Colors.White, BlendRate = 0.6f },
            Effect.FullBloom => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx4, StartIndex = 1700, FrameCount = 4, Colour = Colors.White, BlendRate = 0.6f },
            Effect.WhiteLotus => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx4, StartIndex = 1600, FrameCount = 12, Colour = Colors.White, BlendRate = 0.6f },
            Effect.RedLotus => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx4, StartIndex = 1700, FrameCount = 12, Colour = Colors.White, BlendRate = 0.6f },
            Effect.SweetBrier => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx4, StartIndex = 1900, FrameCount = 10, Colour = Colors.White, BlendRate = 0.6f },
            Effect.Karma => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx4, StartIndex = 1800, FrameCount = 10, Colour = Colors.White, BlendRate = 0.6f },
            Effect.Puppet => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx4, StartIndex = 820, FrameCount = 8, Colour = MagicEffectTable.Fire, BlendRate = 0.6f },
            Effect.PuppetFire => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx4, StartIndex = 1546, FrameCount = 8, Colour = MagicEffectTable.Fire, BlendRate = 0.6f },
            Effect.PuppetIce => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx4, StartIndex = 2700, FrameCount = 10, Colour = MagicEffectTable.Ice, BlendRate = 0.6f },
            Effect.PuppetLightning => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx4, StartIndex = 2800, FrameCount = 10, Colour = MagicEffectTable.Lightning, BlendRate = 0.6f },
            Effect.PuppetWind => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx4, StartIndex = 2900, FrameCount = 10, Colour = MagicEffectTable.Wind, BlendRate = 0.6f },
            Effect.ThunderBolt => new MagicEffectTable.ImpactDef { File = LibraryFile.Magic, StartIndex = 1450, FrameCount = 3, DelayMs = 150, Colour = MagicEffectTable.Lightning, BlendRate = 0.7f },
            Effect.FrostBiteEnd => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx5, StartIndex = 700, FrameCount = 7, Colour = MagicEffectTable.Ice, BlendRate = 0.6f },
            Effect.DanceOfSwallow => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx4, StartIndex = 1300, FrameCount = 8, Colour = Colors.White, BlendRate = 0.7f },
            Effect.FlashOfLight => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx4, StartIndex = 2400, FrameCount = 5, Colour = Colors.White, BlendRate = 0.7f },
            Effect.DemonExplosion => new MagicEffectTable.ImpactDef { File = LibraryFile.MonMagicEx8, StartIndex = 3300, FrameCount = 10, Colour = MagicEffectTable.Phantom, BlendRate = 0.6f },
            Effect.ParasiteExplode => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx5, StartIndex = 700, FrameCount = 7, Colour = Colors.White },
            Effect.BurningFireExplode => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx6, StartIndex = 1100, FrameCount = 10, Colour = MagicEffectTable.Fire },
            Effect.ChainOfFireExplode => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx10, StartIndex = 600, FrameCount = 12, Colour = MagicEffectTable.Fire },
            Effect.HundredFist => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx5, StartIndex = 2100, FrameCount = 5, DelayMs = 200, Colour = MagicEffectTable.Fire },
            Effect.HundredFistStruck => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx5, StartIndex = 2200, FrameCount = 6, DelayMs = 150, Colour = MagicEffectTable.Fire },
            Effect.IceAuraEnd => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx5, StartIndex = 2700, FrameCount = 11, Colour = MagicEffectTable.Ice },
            _ => null,
        };
        if (def == null) return;
        if (effect != Effect.TeleportOut)
        {
            if (effect == Effect.ChainOfFireExplode)
                SpawnImpactTarget(def, target, MirDirection.Up, SoundIndex.ChainofFireExplode, 8);
            else
                SpawnImpactTarget(def, target);
        }
        if (effect == Effect.TeleportOut)
        {
            // 旧端 TeleportOut 倒放，保持同一目标锚点。
            var reverse = new MirEffectNode();
            AddChild(reverse);
            reverse.SetupTarget(def.File, def.StartIndex, def.FrameCount, def.DelayMs, target, () => GetTargetRenderY(target));
            reverse.Reversed = true;
            reverse.Blend = true;
            reverse.BlendRate = def.BlendRate;
        }
    }

    private void PlayEffectSound(Effect effect)
    {
        var sound = effect switch
        {
            Effect.TeleportOut => SoundIndex.TeleportOut,
            Effect.TeleportIn => SoundIndex.TeleportIn,
            Effect.ThunderBolt => SoundIndex.LightningStrikeEnd,
            Effect.FullBloom => SoundIndex.FullBloom,
            Effect.WhiteLotus => SoundIndex.WhiteLotus,
            Effect.RedLotus => SoundIndex.RedLotus,
            Effect.SweetBrier => SoundIndex.SweetBrier,
            Effect.Karma => SoundIndex.Karma,
            Effect.DanceOfSwallow => SoundIndex.DanceOfSwallowsEnd,
            Effect.FlashOfLight => SoundIndex.FlashOfLightEnd,
            Effect.ParasiteExplode => SoundIndex.ParasiteExplode,
            Effect.FrostBiteEnd => SoundIndex.FireStormEnd,
            Effect.ChainOfFireExplode => SoundIndex.None,
            Effect.MirrorImage => SoundIndex.SummonSkeletonEnd,
            Effect.HundredFist => SoundIndex.HundredFist,
            Effect.IceAuraEnd => SoundIndex.GreaterIceBoltEnd,
            _ => SoundIndex.None,
        };
        PlaySound(sound);
    }

    private void OnMapEffect(System.Drawing.Point location, Effect effect, MirDirection direction)
    {
        PlayMapEffectSound(effect);
        var def = effect switch
        {
            Effect.SummonSkeleton => new MagicEffectTable.CastEffect { File = LibraryFile.Magic, StartIndex = 750, FrameCount = 10, Colour = MagicEffectTable.Phantom },
            Effect.SummonShinsu => new MagicEffectTable.CastEffect { File = LibraryFile.Mon_9, StartIndex = 9640, FrameCount = 10, Colour = MagicEffectTable.Phantom, Skip = 10 },
            Effect.CursedDoll => new MagicEffectTable.CastEffect { File = LibraryFile.MagicEx3, StartIndex = 700, FrameCount = 13, Colour = MagicEffectTable.None },
            Effect.UndeadSoul => new MagicEffectTable.CastEffect { File = LibraryFile.MonMagicEx20, StartIndex = 3300, FrameCount = 10, Colour = MagicEffectTable.None },
            Effect.MirrorImage => new MagicEffectTable.CastEffect { File = LibraryFile.MagicEx2, StartIndex = 1280, FrameCount = 10, Colour = MagicEffectTable.None },
            Effect.FireWallSmoke => new MagicEffectTable.CastEffect { File = LibraryFile.ProgUse, StartIndex = 220, FrameCount = 1, DelayMs = 3500, Colour = MagicEffectTable.None, DrawType = MirEffectNode.EffectLayer.Floor, Opacity = 0.8f },
            _ => null,
        };
        if (def == null) return;
        SpawnCastEffect(def, location.X, location.Y);
        if (effect == Effect.SummonShinsu)
        {
            var fx = GetChildren().OfType<MirEffectNode>().LastOrDefault();
            if (fx != null) fx.Direction = direction;
        }
    }

    private void PlayMapEffectSound(Effect effect)
    {
        var sound = effect switch
        {
            Effect.SummonSkeleton => SoundIndex.SummonSkeletonEnd,
            Effect.SummonShinsu => SoundIndex.SummonShinsuEnd,
            Effect.CursedDoll => SoundIndex.CursedDollEnd,
            Effect.UndeadSoul => SoundIndex.SummonDeadEnd,
            Effect.BurningFireExplode => SoundIndex.FireStormEnd,
            Effect.HundredFist => SoundIndex.HundredFist,
            Effect.IceAuraEnd => SoundIndex.GreaterIceBoltEnd,
            _ => SoundIndex.None,
        };
        PlaySound(sound);
    }

    private void SpawnCastEffect(MagicEffectTable.CastEffect def, int x, int y, int sourceX = int.MinValue, int sourceY = int.MinValue, MirDirection castDirection = MirDirection.Up)
    {
        var fx = new MirEffectNode();
        AddChild(fx);
        fx.Setup(def.File, def.StartIndex, def.FrameCount, def.DelayMs, null, x, y, () => ComputeEffectScreenPos(x, y));
        fx.Blend = def.Blend;
        fx.DrawType = def.DrawType;
        fx.BlendRate = def.BlendRate;
        fx.Opacity = def.Opacity;
        fx.Skip = def.Skip;
        double distanceDelay = sourceX == int.MinValue ? 0 : Functions.Distance(new System.Drawing.Point(sourceX, sourceY), new System.Drawing.Point(x, y)) * def.DistanceDelayMs;
        fx.SetStartDelay(def.StartDelayMs + distanceDelay);
        if (def.DirectionFromSource && sourceX != int.MinValue)
            fx.Direction = Functions.DirectionFromPoint(new System.Drawing.Point(sourceX, sourceY), new System.Drawing.Point(x, y));
        else if (def.DirectionFromCast)
            fx.Direction = castDirection;
        fx.FrameLight = def.FrameLight;
        fx.FrameLightColour = def.Colour;
        fx.UseEffectTransparency = !def.NoColourKey;
    }

    private void SpawnCastEffectTarget(MagicEffectTable.CastEffect def, Node2D target)
        => SpawnCastEffectTarget(def, target, MirDirection.Up);

    private void SpawnCastEffectTarget(MagicEffectTable.CastEffect def, Node2D target, MirDirection direction)
    {
        var fx = new MirEffectNode();
        AddChild(fx);
        fx.SetupTarget(def.File, def.StartIndex, def.FrameCount, def.DelayMs, target,
            () => GetTargetRenderY(target));
        fx.Blend = def.Blend;
        fx.DrawType = def.DrawType;
        fx.BlendRate = def.BlendRate;
        fx.Opacity = def.Opacity;
        fx.Skip = def.Skip;
        fx.SetStartDelay(def.StartDelayMs);
        fx.Direction = direction;
        fx.FrameLightColour = def.Colour;
        fx.UseEffectTransparency = !def.NoColourKey;
    }

    private void SpawnImpact(MagicEffectTable.ImpactDef imp, int x, int y, int sourceX = int.MinValue, int sourceY = int.MinValue)
    {
        MirDirection direction = MirDirection.Up;
        if (imp.DirectionFromSource && sourceX != int.MinValue)
            direction = Functions.DirectionFromPoint(new System.Drawing.Point(sourceX, sourceY), new System.Drawing.Point(x, y));

        var fx = new MirEffectNode();
        AddChild(fx);
        fx.Setup(imp.File, imp.ResolveStartIndex(direction), imp.FrameCount, imp.DelayMs, null, x, y, () => ComputeEffectScreenPos(x, y));
        fx.Blend = true;
        fx.DrawType = imp.DrawType;
        fx.BlendRate = imp.BlendRate;
        fx.Opacity = imp.Opacity;
        fx.Skip = imp.Skip;
        double distanceDelay = sourceX == int.MinValue ? 0 : Functions.Distance(new System.Drawing.Point(sourceX, sourceY), new System.Drawing.Point(x, y)) * imp.DistanceDelayMs;
        fx.SetStartDelay(imp.StartDelayMs + distanceDelay);
        // ResolveStartIndex 已经选取了方向分组帧；不要再次叠加
        // Direction*Skip，否则会把分组偏移重复计算。
        fx.Direction = imp.DirectionStartIndices != null ? MirDirection.Up : direction;
        fx.FrameLight = imp.FrameLight;
        fx.FrameLightColour = imp.Colour;
        fx.UseEffectTransparency = !imp.NoColourKey;
    }

    private void SpawnImpactTarget(MagicEffectTable.ImpactDef imp, Node2D target)
        => SpawnImpactTarget(imp, target, MirDirection.Up);

    private void SpawnImpactTarget(MagicEffectTable.ImpactDef imp, Node2D target, MirDirection direction,
        SoundIndex frameSound = SoundIndex.None, int soundFrame = -1)
    {
        var fx = new MirEffectNode();
        AddChild(fx);
        fx.SetupTarget(imp.File, imp.ResolveStartIndex(direction), imp.FrameCount, imp.DelayMs, target,
            () => GetTargetRenderY(target));
        fx.Blend = true;
        fx.DrawType = imp.DrawType;
        fx.BlendRate = imp.BlendRate;
        fx.Opacity = imp.Opacity;
        fx.Skip = imp.Skip;
        fx.SetStartDelay(imp.StartDelayMs);
        // Rake 的旧端 StartIndex 已经选到了对应方向组，不能再叠加 Direction*Skip。
        fx.Direction = imp.DirectionStartIndices != null ? MirDirection.Up : direction;
        fx.FrameLight = imp.FrameLight;
        fx.FrameLightColour = imp.Colour;
        fx.UseEffectTransparency = !imp.NoColourKey;
        if (frameSound != SoundIndex.None && soundFrame >= 0)
        {
            bool played = false;
            fx.FrameIndexChanged = frame =>
            {
                if (!played && frame == soundFrame)
                {
                    played = true;
                    PlaySound(frameSound);
                }
            };
        }
    }

    private void SpawnProjectile(MagicEffectTable.CastEffect def, int fromX, int fromY, int toX, int toY, double additionalStartDelay = 0)
    {
        var proj = def.Projectile;
        // 原版地面落点弹道无 CompleteAction（MirProjectile 不设 CompleteAction），
        // 投射物飞出屏幕后才消失。只在 MapImpact 显式非空时才设着弹回调，
        // 不能用 Impact 回退——否则地面弹道会在落点停下并播放本不该有的爆炸。
        SpawnProjectileDefinition(proj, fromX, fromY, toX, toY, def.MapImpact, additionalStartDelay);
    }

    private void SpawnProjectileDefinition(MagicEffectTable.ProjectileDef proj, int fromX, int fromY, int toX, int toY, MagicEffectTable.ImpactDef impact, double additionalStartDelay = 0, bool blend = true, int delay = 0)
    {
        if (proj == null) return;
        var pn = new MirProjectileNode();
        AddChild(pn);
        int originX = proj.OriginFromTarget ? toX : fromX;
        int originY = proj.OriginFromTarget ? toY : fromY;
        pn.SetupProjectile(proj.File, proj.StartIndex, proj.FrameCount, proj.DelayMs, null, toX, toY,
            new System.Drawing.Point(originX + proj.OriginOffsetX, originY + proj.OriginOffsetY), (cx, cy) => ComputeEffectScreenPos(cx, cy));
        pn.Blend = blend;
        pn.Delay = delay;
        pn.Skip = proj.Skip;
        pn.Has16Directions = proj.Has16Directions;
        pn.Explode = proj.Explode;
        pn.DrawType = proj.DrawType;
        pn.BlendRate = proj.BlendRate;
        pn.Opacity = proj.Opacity;
        pn.FrameLight = proj.FrameLight;
        pn.FrameLightColour = proj.Colour;
        pn.SetStartDelay(proj.StartDelayMs + additionalStartDelay);
        // 到达后播落地特效 + 逐点到达音效 (原版 MirProjectile CompleteAction)。
        if (impact != null || proj.Arrival != null || proj.ArrivalSound != SoundIndex.None
            || proj.CompletionSound != SoundIndex.None)
            pn.CompleteAction = () =>
            {
                if (proj.ArrivalSound != SoundIndex.None) PlaySound(proj.ArrivalSound);
                if (proj.CompletionSound != SoundIndex.None) PlaySound(proj.CompletionSound);
                if (impact != null) SpawnImpact(impact, toX, toY);
                else if (proj.Arrival != null) SpawnImpact(proj.Arrival, toX, toY);
            };
    }

    private void SpawnProjectileTarget(MagicEffectTable.CastEffect def, int fromX, int fromY, Node2D target,
        SoundIndex completionSound = SoundIndex.None)
    {
        var proj = def.TargetProjectile ?? def.Projectile;
        SpawnProjectileDefinitionTarget(proj, fromX, fromY, target, def.Impact, completionSound);
    }

    private void SpawnProjectileDefinitionTarget(MagicEffectTable.ProjectileDef proj, int fromX, int fromY, Node2D target,
        MagicEffectTable.ImpactDef impact, SoundIndex completionSound = SoundIndex.None)
    {
        if (proj == null) return;
        var pn = new MirProjectileNode();
        AddChild(pn);
        pn.SetupProjectileTarget(proj.File, proj.StartIndex, proj.FrameCount, proj.DelayMs,
            target, () => GetTargetRenderY(target), new System.Drawing.Point(fromX, fromY),
            (cx, cy) => ComputeEffectScreenPos(cx, cy));
        pn.Blend = true;
        pn.Skip = proj.Skip;
        pn.Has16Directions = proj.Has16Directions;
        pn.Explode = proj.Explode;
        pn.DrawType = proj.DrawType;
        pn.BlendRate = proj.BlendRate;
        pn.Opacity = proj.Opacity;
        pn.FrameLight = proj.FrameLight;
        pn.FrameLightColour = proj.Colour;
        pn.SetStartDelay(proj.StartDelayMs);
        var arrivalSound = completionSound != SoundIndex.None ? completionSound : proj.CompletionSound;
        if (impact != null || proj.Arrival != null || proj.ArrivalSound != SoundIndex.None
            || arrivalSound != SoundIndex.None)
            pn.CompleteAction = () =>
            {
                if (proj.ArrivalSound != SoundIndex.None) PlaySound(proj.ArrivalSound);
                if (arrivalSound != SoundIndex.None) PlaySound(arrivalSound);
                if (impact != null) SpawnImpactTarget(impact, target);
                else if (proj.Arrival != null) SpawnImpactTarget(proj.Arrival, target);
            };
    }

    // 原版 ElementalSwords (CConnection.cs:1424-1448): 每个目标先在施法者当前格
    // 播 MirEffect(300,5 MagicEx10 0,0 Skip10 Direction=施法者朝向)，完成后
    // 再朝目标发 MirProjectile(0,3 MagicEx10 Has16Directions)。
    private void SpawnElementalSwords(S.ObjectProjectile packet, Node2D target, int sourceX, int sourceY)
    {
        var fx = new MirEffectNode();
        AddChild(fx);
        fx.Setup(LibraryFile.MagicEx10, 300, 5, 100, null, sourceX, sourceY,
            () => ComputeEffectScreenPos(sourceX, sourceY));
        fx.Skip = 10;
        fx.Direction = packet.Direction;
        fx.Blend = true;
        fx.CompleteAction = () =>
        {
            var pn = new MirProjectileNode();
            AddChild(pn);
            pn.SetupProjectileTarget(LibraryFile.MagicEx10, 0, 3, 100, target,
                () => GetTargetRenderY(target), new System.Drawing.Point(sourceX, sourceY),
                (cx, cy) => ComputeEffectScreenPos(cx, cy));
            pn.Blend = true;
            pn.Has16Directions = true;
        };
    }

    private Node2D GetMagicTargetNode(uint objectID)
    {
        if (objectID == _playerObjectID) return _player;
        if (_otherPlayers.TryGetValue(objectID, out var player)) return player;
        if (_objects.TryGetValue(objectID, out var ob)) return ob;
        return null;
    }

    private static System.Drawing.Point GetTargetCell(Node2D target) => target switch
    {
        MapObjectNode ob => new System.Drawing.Point(ob.CellX, ob.CellY),
        PlayerRenderer player => new System.Drawing.Point(player.CellX, player.CellY),
        _ => System.Drawing.Point.Empty,
    };

    private int GetTargetRenderY(Node2D target)
    {
        return target switch
        {
            MapObjectNode ob => ob.RenderY,
            PlayerRenderer player => player.CellY,
            _ => 0,
        };
    }

    private void SpawnGenericExplosion(int cellX, int cellY)
    {
        var fx = new MirEffectNode();
        AddChild(fx);
        fx.Setup(LibraryFile.Magic, 580, 10, 100, null, cellX, cellY, () => ComputeEffectScreenPos(cellX, cellY));
        fx.Blend = true;
        fx.FrameLight = 10;
        fx.FrameLightColour = new Color(1f, 0.62f, 0.25f);
    }

    // 血量变化: 受伤扣血并显示血条 (Miss/Block 只播动画不扣)
    private void OnHealthChanged(uint objectID, int change, bool miss, bool block, bool critical, bool resist)
    {
        bool applyDamage = !miss && !block;
        if (objectID == _playerObjectID)
        {
            if (_player == null) return;
            if (applyDamage)
            {
                _player.Health += change;
                _currentHP = _player.Health;
                _mainPanel?.SetHealth(_currentHP);
            }
            if (ClientSettings.ShowDamageNumbers) SpawnDamagePopup(_player, change, miss, block, critical, resist);
            _player.ShowHealthBar = true;
            _player.DrawHealthUntilMs = Godot.Time.GetTicksMsec() + 5000;
            if (applyDamage) _player.PlayStruck();
            return;
        }
        if (_objects.TryGetValue(objectID, out var ob))
        {
            ob.ShowHealthBar = true;
            ob.DrawHealthUntilMs = Godot.Time.GetTicksMsec() + 5000;
            if (applyDamage)
            {
                ob.Health += change;
                ob.SetAnimation(MirAnimation.Struck);
            }
            if (ClientSettings.ShowDamageNumbers) SpawnDamagePopup(ob, change, miss, block, critical, resist);
            _groupHealthPanel?.UpdateMember(objectID, ob.Health, ob.MaxHealth);
            return;
        }
        if (_otherPlayers.TryGetValue(objectID, out var player))
        {
            player.ShowHealthBar = true;
            player.DrawHealthUntilMs = Godot.Time.GetTicksMsec() + 5000;
            if (applyDamage)
            {
                player.Health += change;
                player.PlayStruck();
            }
            if (ClientSettings.ShowDamageNumbers) SpawnDamagePopup(player, change, miss, block, critical, resist);
            _groupHealthPanel?.UpdateMember(objectID, player.Health, player.MaxHealth);
        }
    }

    private void SpawnDamagePopup(Node2D target, int value, bool miss, bool block, bool critical, bool resist)
    {
        if (target == null) return;
        // 纯伤害且无任何标志且数值为 0 时不飘字; miss/block/resist 即使 change=0 也要显示反馈
        if (value == 0 && !miss && !block && !resist) return;
        var popup = new DamagePopupNode { Position = target.Position + new Vector2(0f, -62f) };
        AddChild(popup);
        popup.Setup(value, miss, block, critical, resist);
    }

    private void OnDataObjectHealthMana(uint objectID, int health, int mana, bool dead)
    {
        if (objectID == _playerObjectID)
        {
            if (_player == null) return;
            _player.Health = health;
            _currentHP = health;
            _mainPanel?.SetHealth(_currentHP);
            return;
        }
        if (_otherPlayers.TryGetValue(objectID, out var player))
        {
            player.Health = health;
            player.Dead = dead;
            _groupHealthPanel?.UpdateMember(objectID, player.Health, player.MaxHealth);
        }
        else if (_objects.TryGetValue(objectID, out var ob))
        {
            ob.Health = health;
            ob.Dead = dead;
            _groupHealthPanel?.UpdateMember(objectID, ob.Health, ob.MaxHealth);
        }
    }

    private void OnDataObjectMaxHealthMana(uint objectID, int maxHealth, int maxMana)
    {
        if (objectID == _playerObjectID)
        {
            if (_player == null) return;
            _player.MaxHealth = maxHealth;
            return;
        }
        if (_otherPlayers.TryGetValue(objectID, out var player))
        {
            player.MaxHealth = maxHealth;
            player.MaxMana = maxMana;
            _groupHealthPanel?.UpdateMember(objectID, player.Health, player.MaxHealth);
        }
        else if (_objects.TryGetValue(objectID, out var ob))
        {
            ob.MaxHealth = maxHealth;
            _groupHealthPanel?.UpdateMember(objectID, ob.Health, ob.MaxHealth);
        }
    }

    // DataObjectMonster: 视野内怪物的权威血量 (进游戏时批量发, 血条数据源)
    private void OnDataObjectMonsterInfo(uint objectID, int health, int maxHealth, int light, int monsterIndex, bool dead)
    {
        if (!_objects.TryGetValue(objectID, out var ob)) return;
        ob.Health = health;
        ob.MaxHealth = maxHealth;
        ob.Light = light;
        ob.Dead = dead;
        _groupHealthPanel?.UpdateMember(objectID, ob.Health, ob.MaxHealth);
    }

    // 玩家属性: MaxHealth/MaxMana 来源
    private void OnStatsUpdate(int maxHealth, int maxMana)
    {
        if (_player == null) return;
        _player.MaxHealth = maxHealth;
        _player.MaxMana = maxMana;
        if (_player.Health <= 0) _player.Health = maxHealth;
    }

    // 受击: 被击退到新位置 + 播 Struck 动画
    private void OnObjectStruck(uint objectID, MirDirection dir, System.Drawing.Point loc, uint attackerID, Element element)
    {
        SpawnStruckEffect(objectID, element);
        if (objectID == _playerObjectID)
        {
            if (_player == null) return;
            _playerLocation = loc;
            _player.CellX = loc.X;
            _player.CellY = loc.Y;
            _player.Direction = dir;
            _player.PlayStruck();
            _canRun = false;
            _runCooldownUntilMs = Godot.Time.GetTicksMsec() + 600.0;
            _moveFrameCount = 1;
            _player.OffsetX = 0f;
            _player.OffsetY = 0f;
            UpdatePlayerPosition();
            _miniMap?.UpdatePlayer(_player.CellX, _player.CellY);
            _bigMap?.UpdatePlayer(_player.CellX, _player.CellY);
            return;
        }
        if (_otherPlayers.TryGetValue(objectID, out var player))
        {
            player.CellX = loc.X;
            player.CellY = loc.Y;
            player.Direction = dir;
            player.PlayStruck();
            UpdateOtherPlayerPosition(player);
        }
        else if (_objects.TryGetValue(objectID, out var ob))
        {
            ob.CellX = loc.X;
            ob.CellY = loc.Y;
            ob.Direction = dir;
            ob.SetAnimation(MirAnimation.Struck);
            ob.PlayStruckSound();
            ob.Position = ComputeObjectScreenPos(loc.X, loc.Y);
        }
    }

    private void SpawnStruckEffect(uint objectID, Element element)
    {
        var target = GetMagicTargetNode(objectID);
        if (target == null) return;
        int start = element switch
        {
            Element.Fire => 790,
            Element.Ice => 810,
            Element.Lightning => 830,
            Element.Wind => 850,
            Element.Holy => 870,
            Element.Dark => 890,
            Element.Phantom => 910,
            _ => 930,
        };
        var fx = new MirEffectNode();
        AddChild(fx);
        fx.SetupTarget(LibraryFile.MagicEx, start, 6, 100, target,
            () => GetTargetRenderY(target));
        fx.Blend = true;
        fx.BlendRate = 0.7f;
        fx.FrameLight = 10;
        fx.FrameLightColour = element switch
        {
            Element.Fire => MagicEffectTable.Fire,
            Element.Ice => MagicEffectTable.Ice,
            Element.Lightning => MagicEffectTable.Lightning,
            Element.Wind => MagicEffectTable.Wind,
            Element.Holy => MagicEffectTable.Holy,
            Element.Dark => MagicEffectTable.Dark,
            Element.Phantom => MagicEffectTable.Phantom,
            _ => MagicEffectTable.None,
        };
    }

    // ---- M12 HUD ----

    // 挂载坐标照原版 GameScene (Size = 视口): MainPanel 底中, MiniMap 右上,
    // QuestTracker 小地图下方, Buff 小地图左侧, BigMap 居中
    private void CreateHud()
    {
        _mainPanel = new MainPanel();
        _uiLayer.AddChild(_mainPanel);

        _chatLog = new ChatLogPanel();
        _uiLayer.AddChild(_chatLog);
        _chatLog.Visible = !ClientSettings.HideChatBar;
        _chatTextBox = new ChatTextBox();
        _uiLayer.AddChild(_chatTextBox);
        _chatTextBox.Visible = !ClientSettings.HideChatBar;

        _miniMap = new MiniMapDialog();
        _uiLayer.AddChild(_miniMap);
        _miniMap.Visible = true; // DXWindow 默认隐藏, HUD 常驻
        _miniMap.SetBigMapRequestHandler(OpenBigMap);
        _miniMap.LayoutChanged += LayoutHud;

        _questTracker = new QuestTrackerDialog();
        _uiLayer.AddChild(_questTracker);
        _questTracker.Visible = ClientSettings.QuestTrackerVisible;

        _questDialog = new QuestDialog();
        _uiLayer.AddChild(_questDialog);

        _buffDialog = new BuffDialog();
        _uiLayer.AddChild(_buffDialog);
        // 无 buff 时隐藏；有内容时 BuffsChanged 会打开并请求重新锚点。
        _buffDialog.Visible = false;
        _buffDialog.LayoutNeeded += LayoutBuffDialog;

        _bigMap = new BigMapDialog();
        _bigMap.SetRecenterMapProvider(() => GetMapInfo(_playerMapIndex), OpenBigMapForMap);
        _uiLayer.AddChild(_bigMap);
        // 原版进入地图时只显示右上角小地图；大地图只能由 M 键/小地图按钮主动打开。
        // DXWindow 的默认隐藏状态不能作为 HUD 初始化后的唯一不变量，显式复位避免
        // 子控件 Ready/布局或网络初始化过程中把窗口带回可见状态。
        _bigMap.Visible = false;

        // ---- M9: 背包/角色/仓库/腰带对话框 ----
        _inventoryDialog = new InventoryDialog();
        _uiLayer.AddChild(_inventoryDialog);

        _characterDialog = new CharacterDialog();
        _characterDialog.Location = Vector2I.Zero;
        _uiLayer.AddChild(_characterDialog);
        _editCharacterDialog = new EditCharacterDialog();
        _uiLayer.AddChild(_editCharacterDialog);

        _storageDialog = new StorageDialog();
        _uiLayer.AddChild(_storageDialog);

        _beltDialog = new BeltDialog();
        _uiLayer.AddChild(_beltDialog);
        _beltDialog.Visible = true; // 腰带常驻 (原版主面板上方)

        AutoPotionBox = new AutoPotionDialog();
        _uiLayer.AddChild(AutoPotionBox);

        _currencyDialog = new CurrencyDialog();
        _uiLayer.AddChild(_currencyDialog);
        _filterDropDialog = new FilterDropDialog();
        _uiLayer.AddChild(_filterDropDialog);
        _bundleDialog = new BundleDialog();
        _uiLayer.AddChild(_bundleDialog);
        _fortuneDialog = new FortuneCheckerDialog();
        _uiLayer.AddChild(_fortuneDialog);
        _lootBoxDialog = new LootBoxDialog();
        _uiLayer.AddChild(_lootBoxDialog);
        _dungeonFinderDialog = new DungeonFinderDialog();
        _uiLayer.AddChild(_dungeonFinderDialog);
        _timerDialog = new TimerDialog { Location = new Vector2I(20, 100) };
        _uiLayer.AddChild(_timerDialog);
        _captionDialog = new CaptionDialog();
        _uiLayer.AddChild(_captionDialog);

        _magicBar = new MagicBar(this);
        _uiLayer.AddChild(_magicBar);
        _magicBar.Visible = true;
        // MagicBar 在 _Draw 内按绑定行数改 Size (1 行 46 -> 2 行 97);
        // 尺寸变化后必须重新锚定, 否则 2 行底边会压进主面板顶缘。
        _magicBar.Resized += OnMagicBarResized;

        _magicDialog = new MagicDialog();
        _uiLayer.AddChild(_magicDialog);

        _menuDialog = new MenuDialog();
        _menuDialog.Location = new Vector2I(40, 80);
        _uiLayer.AddChild(_menuDialog);

        _exitDialog = new ExitDialog();
        _exitDialog.Location = new Vector2I(40, 80);
        _uiLayer.AddChild(_exitDialog);

        _noticeDialog = new NoticeDialog { Location = new Vector2I(107, 110) };
        _uiLayer.AddChild(_noticeDialog);

        _helpDialog = new HelpDialog();
        _uiLayer.AddChild(_helpDialog);

        _configDialog = new ConfigDialog();
        _uiLayer.AddChild(_configDialog);
        _chatOptionsDialog = new ChatOptionsDialog();
        _uiLayer.AddChild(_chatOptionsDialog);
        _guildDialog = new GuildDialog();
        _uiLayer.AddChild(_guildDialog);
        _guildMemberDialog = new GuildMemberDialog();
        _uiLayer.AddChild(_guildMemberDialog);
        _milestoneDialog = new MilestoneDialog();
        _uiLayer.AddChild(_milestoneDialog);
        _rankingDialog = new RankingDialog(true);
        _uiLayer.AddChild(_rankingDialog);
        _companionDialog = new CompanionDialog();
        _uiLayer.AddChild(_companionDialog);
        CompanionEquipmentCells = _companionDialog.EquipmentCells;
        _npcCompanionStorageDialog = new NPCCompanionStorageDialog();
        _uiLayer.AddChild(_npcCompanionStorageDialog);
        _communicationDialog = new CommunicationDialog();
        _uiLayer.AddChild(_communicationDialog);
        _communicationDialog.UnreadChanged += unread => _mainPanel?.SetMailIndicator(unread);
        _groupDialog = new GroupDialog();
        _uiLayer.AddChild(_groupDialog);
        _groupHealthPanel = new GroupHealthPanel();
        _uiLayer.AddChild(_groupHealthPanel);
        _gameStoreDialog = new GameStoreDialog();
        _uiLayer.AddChild(_gameStoreDialog);
        _consignmentDialog = new ConsignmentDialog();
        _uiLayer.AddChild(_consignmentDialog);
        _marketHistoryDialog = new MarketHistoryDialog();
        _uiLayer.AddChild(_marketHistoryDialog);
        _fishingDialog = new FishingDialog();
        _uiLayer.AddChild(_fishingDialog);
        _fishingCatchDialog = new FishingCatchDialog();
        _uiLayer.AddChild(_fishingCatchDialog);
        _horseTameDialog = new HorseTameDialog();
        _uiLayer.AddChild(_horseTameDialog);
        _horseDialog = new HorseDialog();
        _uiLayer.AddChild(_horseDialog);
        _monsterDialog = new MonsterDialog();
        _uiLayer.AddChild(_monsterDialog);
        _tradeDialog = new TradeDialog();
        _uiLayer.AddChild(_tradeDialog);
        _npcDialog = new NPCDialog();
        _uiLayer.AddChild(_npcDialog);
        _npcSocketDialog = new NPCSocketDialog();
        _uiLayer.AddChild(_npcSocketDialog);
        _npcSocketCombineDialog = new NPCSocketCombineDialog();
        _uiLayer.AddChild(_npcSocketCombineDialog);
        _npcQuestListDialog = new NPCQuestListDialog();
        _uiLayer.AddChild(_npcQuestListDialog);
        _npcQuestDialog = new NPCQuestDialog();
        _uiLayer.AddChild(_npcQuestDialog);

        // 核心窗口统一经过旧版 profile 适配器；默认只应用旧版公共视觉，
        // 不覆盖各窗口自身的动态尺寸和业务布局。确认过的几何迁移由
        // LegacyUiSkin.ApplyWindowProfile(window, geometry: true) 显式开启。
        ApplyLegacyCoreWindowProfiles();

        // 数组注入: 先设 ItemGrid 再 CreateGrid (格子建立时快照 ItemGrid)
        _inventoryDialog.Grid.ItemGrid = Inventory;
        _inventoryDialog.Grid.CreateGrid();
        InventoryCells = _inventoryDialog.Grid.Cells;

        foreach (var cell in _characterDialog.Grid)
            cell.ItemGrid = Equipment;
        EquipmentCells = _characterDialog.Grid;

        _storageDialog.Grid.ItemGrid = Storage;
        _storageDialog.Grid.CreateGrid();
        _storageDialog.PartGrid.ItemGrid = PartsStorage;
        _storageDialog.PartGrid.CreateGrid();
        _storageDialog.RefreshStorage(); // 行数 = StorageSize/10, 重建格 + 滚轮重绑

        BeltLinks = _beltDialog.Links; // 与对话框共享同一数组 (QuickInfo/QuickItem 写回)

        if (AutoLoginArgs.LegacyUi)
            ApplyLegacyCoreTestLayouts();
        OpenLegacyRequestedWindow();

        // M9: 主面板功能按钮 -> 对话框开关
        _mainPanel.CharacterButton.MouseClick += (o, e) =>
        {
            ToggleCharacterWindow();
        };
        _mainPanel.InventoryButton.MouseClick += (o, e) => WindowManager.Toggle(_inventoryDialog, _uiLayer);
        _inventoryDialog.WalletButton.MouseClick += (o, e) =>
        {
            WindowManager.Toggle(_currencyDialog, _uiLayer);
            _currencyDialog.RefreshCurrencies(Currencies);
        };
        LayoutHud();
        AuditLegacyHudIfRequested();
        // HUD 锚定（LayoutHud 设定各窗口根位置）之后应用 overlay 覆盖，
        // 保证窗口级 location 覆盖不会被默认布局冲掉。
        UiOverlay.ApplyAll();
        _mainPanel.BeltButton.MouseClick += (o, e) => WindowManager.Toggle(_beltDialog, _uiLayer);
        _mainPanel.SpellButton.MouseClick += (o, e) =>
        {
            WindowManager.Toggle(_magicDialog, _uiLayer);
            _magicDialog.Refresh();
        };
        _mainPanel.QuestButton.MouseClick += (o, e) =>
        {
            WindowManager.Toggle(_questDialog, _uiLayer);
        };
        _mainPanel.MenuButton.MouseClick += (o, e) =>
        {
            // 旧版没有当前客户端的通用 MenuDialog；菜单键对应 F750
            // 选项窗口。新版模式继续保留原菜单入口。
            if (AutoLoginArgs.LegacyUi)
                OpenConfigDialog();
            else
                WindowManager.Toggle(_menuDialog, _uiLayer);
        };
        _mainPanel.MailButton.MouseClick += (o, e) => OpenCommunicationDialog();
        _mainPanel.GroupButton.MouseClick += (o, e) => OpenGroupDialog();
        _mainPanel.CashShopButton.MouseClick += (o, e) => OpenGameStoreDialog();
        _mainPanel.CashShopButton.Visible = GameStoreEnabled;
        // 旧版 HUD 左上/两侧的小按钮也保留原版的入口语义。
        _mainPanel.SkillEntryButton.MouseClick += (o, e) =>
        {
            WindowManager.Toggle(_magicDialog, _uiLayer);
            _magicDialog.Refresh();
        };
        _mainPanel.MiniMapButton.MouseClick += (o, e) =>
        {
            if (_miniMap != null) _miniMap.Visible = !_miniMap.Visible;
        };
        _mainPanel.ExitButton.MouseClick += (o, e) => OpenExitDialog();
        _mainPanel.PartyButton.MouseClick += (o, e) => OpenGroupDialog();
        _mainPanel.GuildButton.MouseClick += (o, e) => OpenGuildDialog();
        _mainPanel.ExchangeButton.MouseClick += (o, e) => _tradeDialog?.OpenTrade("交易");
        _mainPanel.LogoutButton.MouseClick += (o, e) => OpenExitDialog();

        if (AutoLoginArgs.UiDiagnosticBorders)
            DXControl.DiagnosticBorders = true;
        LayoutHud();
    }

    private void ApplyLegacyCoreWindowProfiles()
    {
        LegacyUiSkin.ApplyWindowProfile(_inventoryDialog);
        LegacyUiSkin.ApplyWindowProfile(_characterDialog);
        LegacyUiSkin.ApplyWindowProfile(_questDialog);
        LegacyUiSkin.ApplyWindowProfile(_configDialog);
        LegacyUiSkin.ApplyWindowProfile(_groupDialog);
        LegacyUiSkin.ApplyWindowProfile(_npcDialog);
        LegacyUiSkin.ApplyWindowProfile(_magicDialog);
        LegacyUiSkin.ApplyWindowProfile(_communicationDialog);
        LegacyUiSkin.ApplyWindowProfile(_storageDialog);
        LegacyUiSkin.ApplyWindowProfile(_gameStoreDialog);
        LegacyUiSkin.ApplyWindowProfile(_tradeDialog);
        LegacyUiSkin.ApplyWindowProfile(_guildDialog);
        LegacyUiSkin.ApplyWindowProfile(_horseDialog);
        _mainPanel?.SetPetModeEnabled(CompanionEnabled);
    }

    private void ApplyLegacyCoreTestLayouts()
    {
        // 仅由 --legacy-ui 显式开启，方便在真实登录场景验证同一套旧版
        // 控件树；默认正式布局暂不隐藏尚未完成旧版数据绑定的现代内容。
        LegacyUiSkin.ApplyLegacyTestWindow(_inventoryDialog, _inventoryDialog.Location);
        LegacyUiSkin.ApplyLegacyTestWindow(_characterDialog, _characterDialog.Location);
        LegacyUiSkin.ApplyLegacyTestWindow(_magicDialog, _magicDialog.Location);
        LegacyUiSkin.ApplyLegacyTestWindow(_groupDialog, _groupDialog.Location);
        LegacyUiSkin.ApplyLegacyTestWindow(_questDialog, _questDialog.Location);
        LegacyUiSkin.ApplyLegacyTestWindow(_communicationDialog, _communicationDialog.Location);
        LegacyUiSkin.ApplyLegacyTestWindow(_tradeDialog, _tradeDialog.Location);
        LegacyUiSkin.ApplyLegacyTestWindow(_guildDialog, _guildDialog.Location);
        LegacyUiSkin.ApplyLegacyTestWindow(_storageDialog, _storageDialog.Location);
        LegacyUiSkin.ApplyLegacyTestWindow(_configDialog, _configDialog.Location);
        LegacyUiSkin.ApplyLegacyTestWindow(_noticeDialog, new Vector2I(107, 110));
    }

    /// <summary>真实登录场景的旧版窗口直达入口，便于逐窗截图和人工验收。</summary>
    private void OpenLegacyRequestedWindow()
    {
        if (!AutoLoginArgs.LegacyUi) return;
        string name = OS.GetCmdlineUserArgs()
            .FirstOrDefault(arg => arg.StartsWith("--legacy-open=", StringComparison.OrdinalIgnoreCase))?
            ["--legacy-open=".Length..].ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(name)) return;

        DXWindow window = name switch
        {
            "character" or "character-expanded" => _characterDialog,
            "inventory" => _inventoryDialog,
            "magic" => _magicDialog,
            "quest" => _questDialog,
            "chat" => _communicationDialog,
            "group" => _groupDialog,
            "menu" => AutoLoginArgs.LegacyUi ? _configDialog : _menuDialog,
            "horse" => _horseDialog,
            "npc" => _npcDialog,
            "trade" => _tradeDialog,
            "guild" => _guildDialog,
            "storage" => _storageDialog,
            "config" or "settings" => _configDialog,
            "notice" or "prompt" => _noticeDialog,
            _ => null,
        };
        if (window == _noticeDialog)
            _noticeDialog?.SetNotice("真实登录旧版 F602 窗口验收");
        if (window == _npcDialog && AutoLoginArgs.LegacyUi)
            _npcDialog.ApplyLegacyEiLayout();
        if (string.Equals(name, "character-expanded", StringComparison.OrdinalIgnoreCase))
            _characterDialog.SetLegacyExpandedForAudit();
        if (window != null)
        {
            WindowManager.Open(window, _uiLayer);
            GD.Print($"[LegacyOpen] requested={name} type={window.GetType().Name} visible={window.Visible} size={window.Size} location={window.Location}");
        }
        else
        {
            GD.PushWarning($"[LegacyOpen] unknown window: {name}");
        }
    }

    private void OnGameResized()
    {
        RefreshUiScale();
        LayoutHud();
        UpdateViewRange();
        UpdatePlayerPosition();
    }

    /// <summary>
    /// HUD 唯一使用的画布尺寸。GetVisibleRect() 可能受可见区域/相机裁剪影响，
    /// 但常驻 HUD 必须贴整个窗口 viewport 的四条边，不能贴到裁剪后的区域。
    /// </summary>
    private Vector2 GetHudViewportSize()
    {
        Vector2 size = GetViewportRect().Size;
        return size.X > 0 && size.Y > 0 ? size : GetViewport().GetVisibleRect().Size;
    }

    /// <summary>
    /// HUD 逻辑画布基于原版 1024x768 设计尺寸缩放。取高/宽两个方向中较小的缩放
    /// 倍率 (限制因素), 保证逻辑画布「至少」1024x768 —— 固定 HUD (主面板宽 1024)
    /// 在任何窗口比例下都装得下, 不会越过右/下屏幕边缘。常规 16:9/16:10 屏幕高度是
    /// 限制因素, 倍率与原来按高度计算完全一致; 只有竖向/接近 4:3 的窄窗口才
    /// 由宽度接管, 避免主面板溢出右边。
    /// </summary>
    private void RefreshUiScale()
    {
        Vector2 viewport = GetHudViewportSize();
        if (viewport.X <= 0 || viewport.Y <= 0)
            UiScale = 2f;
        else
        {
            float byHeight = viewport.Y / UiScaleBaseHeight;
            float byWidth = viewport.X / 1024f;
            UiScale = Mathf.Clamp(Mathf.Min(byHeight, byWidth), 1f, 2f);
        }
        if (_uiLayer != null && IsInstanceValid(_uiLayer))
            _uiLayer.Transform = Transform2D.Identity.Scaled(Vector2.One * UiScale);
        MirSkin.SetUiScale(UiScale);
    }

    /// <summary>
    /// 2 倍 UI 的回归审计。所有 HUD 控件都以旧客户端的逻辑像素布局，
    /// CanvasLayer 负责最终放大；这里同时检查视觉锚点、按钮命中开关和
    /// MagicBar 的输入层，防止“看起来在这里、实际点不到”的回归。
    /// </summary>
    private void RunUiLayoutAudit()
    {
        Vector2 viewport = GetHudViewportSize();
        Vector2 logicalViewport = viewport / UiScale;
        bool pass = _uiLayer != null
            && Mathf.IsEqualApprox(_uiLayer.Transform.X.X, UiScale)
            && Mathf.IsEqualApprox(_uiLayer.Transform.Y.Y, UiScale)
            && _mainPanel != null && _magicBar != null;

        if (pass)
        {
            pass &= _mainPanel.CharacterButton.Position.IsEqualApprox(new Vector2(650, 23));
            pass &= _mainPanel.InventoryButton.Position.IsEqualApprox(new Vector2(689, 23));
            pass &= _mainPanel.SpellButton.Position.IsEqualApprox(new Vector2(728, 23));
            pass &= _mainPanel.MenuButton.Position.IsEqualApprox(new Vector2(923, 23));
            pass &= _mainPanel.CashShopButton.Position.IsEqualApprox(new Vector2(972, 16));
            pass &= _mainPanel.CharacterButton.MouseFilter == Control.MouseFilterEnum.Stop;
            pass &= _mainPanel.InventoryButton.MouseFilter == Control.MouseFilterEnum.Stop;
            pass &= _mainPanel.SpellButton.MouseFilter == Control.MouseFilterEnum.Stop;
            pass &= _magicBar.MouseFilter == Control.MouseFilterEnum.Stop;

            // headless Godot 使用 64x64 的虚拟视口，无法验证真实窗口边界；
            // 真实窗口审计仍检查完整的锚点和命中区域。
            if (viewport.X > 128 && viewport.Y > 128)
            {
                Rect2 panel = _mainPanel.GetRect();
                pass &= panel.Position.X >= 0 && panel.End.X <= logicalViewport.X + 1
                    && panel.Position.Y >= 0 && panel.End.Y <= logicalViewport.Y + 1;
            }
            foreach (var button in new[]
                     {
                         _mainPanel.CharacterButton, _mainPanel.InventoryButton,
                         _mainPanel.SpellButton, _mainPanel.QuestButton,
                         _mainPanel.MailButton, _mainPanel.BeltButton,
                         _mainPanel.GroupButton, _mainPanel.MenuButton,
                         _mainPanel.CashShopButton,
                     })
            {
                Rect2 local = button.GetRect();
                pass &= local.Position.X >= 0 && local.Position.Y >= 0
                    && local.End.X <= _mainPanel.Size.X + 1
                    && local.End.Y <= _mainPanel.Size.Y + 1;
            }

            // 常驻 HUD 的两个历史偏移回归：技能栏必须落在主底栏左上方，
            // 透明且无可见聊天内容时不能留下中央悬浮滚动条。
            if (_magicBar != null && !_magicBar.UserMoved)
            {
                var expectedMagic = new Vector2(
                    Math.Max(0, _mainPanel.Position.X - _magicBar.Size.X - 5),
                    Math.Max(0, _mainPanel.Position.Y - _magicBar.Size.Y - 5));
                pass &= _magicBar.Position.DistanceTo(expectedMagic) <= 1f;
            }
            if (_chatLog != null)
                pass &= !_chatLog.IsScrollChromeVisible;
        }

        string layout = $"viewport={viewport} logical={logicalViewport} "
            + $"panel={_mainPanel?.Position}/{_mainPanel?.Size} "
            + $"magic={_magicBar?.Position}/{_magicBar?.Size} userMoved={_magicBar?.UserMoved} "
            + $"chatScroll={_chatLog?.IsScrollChromeVisible}";
        GD.Print(pass
            ? $"[UILayoutAudit] PASS scale={UiScale} {layout}"
            : $"[UILayoutAudit] FAIL scale={_uiLayer?.Transform} {layout}");
    }

    /// <summary>
    /// 鼠标是否悬停在游戏 UI 上 (所有窗口/面板都在 _uiLayer 下)。
    /// UI 上的左/右键是操作界面, 不是移动角色 —— MouseWalker 据此屏蔽移动。
    /// 等价原版 MapControl.ProcessInput 的 MouseControl == this 判断。
    /// </summary>
    private bool IsMouseOverUi()
    {
        var hovered = GetViewport().GuiGetHoveredControl();
        for (Node n = hovered; n != null; n = n.GetParent())
        {
            if (n == _uiLayer) return true;
        }
        return false;
    }

    private bool CanPlayerTurn()
    {
        if (_observer || _player == null || _player.Dead || _player.DragonRepulsed)
            return false;
        return !_playerPoison.HasFlag(PoisonType.Paralysis)
            && !_playerPoison.HasFlag(PoisonType.Containment);
    }

    private bool CanPlayerMove()
    {
        if (IsMovementBlockedByItemInteraction(
                DXItemCell.SelectedCell != null,
                _selectedCurrency != null,
                WindowManager.OpenWindows.Any(window => window is ItemAmountDialog && window.Visible)))
            return false;
        return CanPlayerTurn()
            && !_player.ElementalHurricane
            && !_playerPoison.HasFlag(PoisonType.WraithGrip)
            && _pendingMagicPacket == null
            && !_player.IsSpellAnimation;
    }

    private void SuspendMovementForMagic()
    {
        // 技能开始时清除自动跑步，并要求鼠标释放后重新按下。
        // 这样技能释放完成不会继承施法前残留的移动意图。
        _autoRun = false;
        if (_mouseWalker != null)
        {
            _mouseWalker.AutoRun = false;
            _mouseWalker.SuspendUntilInputRelease();
        }
    }

    private bool BlockLeftMouseMovement()
    {
        // 原版 MapControl.OnMouseDown 优先处理已拾起的货币并打开数量窗口；
        // MouseWalker 独立运行时也必须屏蔽同一帧的普通移动请求。
        if (IsMovementBlockedByItemInteraction(
                DXItemCell.SelectedCell != null,
                _selectedCurrency != null,
                WindowManager.OpenWindows.Any(window => window is ItemAmountDialog && window.Visible)))
            return true;
        var mouseObject = _combatController?.MouseObject;
        if (IsFishingActive || IsTamingActive)
            return true;
        if (mouseObject != null)
        {
            // 原版只有鼠标所在格就是玩家当前格时才拾取；远处的掉落物
            // 仍允许左键持续走近，否则点击远处物品会立即发一个必失败的
            // PickUp，同时 MouseWalker 又被拦截，表现为“不能捡东西”。
            if (mouseObject.Type == ObjectRenderer.Kind.Item)
            {
                if (mouseObject.CellX == _playerLocation.X && mouseObject.CellY == _playerLocation.Y)
                    return true;
                // 原版采矿分支允许矿点上有掉落物；继续执行下方的矿点判断。
            }
            else if (!mouseObject.Dead)
                return true;
        }

        // 只有满足原版采矿条件时才拦截移动；普通相邻空地点击仍然是走路。
        // 原版矿点是鼠标方向的**第一格**（Functions.Move(玩家, 方向)），
        // 与挖矿分支共用同一判定。
        var target = _combatController?.MouseCell() ?? _playerLocation;
        var miningDirection = Functions.DirectionFromPoint(_playerLocation, target);
        var miningPoint = Functions.Move(_playerLocation, miningDirection);
        var mapInfo = Globals.MapInfoList?.Binding.FirstOrDefault(m => m.Index == _playerMapIndex);
        var pickaxe = Equipment.ElementAtOrDefault((int)EquipmentSlot.Weapon);
        bool inBounds = _mapView?.Map != null
            && miningPoint.X >= 0 && miningPoint.Y >= 0
            && miningPoint.X < _mapView.Map.Width && miningPoint.Y < _mapView.Map.Height;
        bool cellFlag = inBounds && _mapView.Map.Cells[miningPoint.X, miningPoint.Y].Flag;
        bool adjacent = Math.Max(Math.Abs(miningPoint.X - _playerLocation.X),
            Math.Abs(miningPoint.Y - _playerLocation.Y)) == 1;
        return CanMineNow(mapInfo?.CanMine == true, pickaxe?.Info?.ItemEffect,
            pickaxe?.CurrentDurability ?? 0, pickaxe?.Info?.Durability ?? 0,
            inBounds, cellFlag, adjacent, IsMounted);
    }

    // 原版 Cell.Blocking() 不只看地图 Flag，还看当前格上的动态 MapObject。
    // 地面物品和法术不挡路；活着的怪物、NPC、其他玩家会挡路。
    private bool IsMovementCellBlocked(int x, int y)
    {
        foreach (var ob in _objects.Values)
        {
            if (ob.CellX != x || ob.CellY != y || ob.Dead) continue;
            if (ob.Type == ObjectRenderer.Kind.Item) continue;
            if (ob.Type == ObjectRenderer.Kind.Monster)
            {
                // Client.Models.MonsterObject.Blocking：宠物不挡路；
                // 城堡防御对象朝向 7 时也不挡路。
                if (!string.IsNullOrWhiteSpace(ob.PetOwner)) continue;
                if (ob.MonsterInfo?.Flag == MonsterFlag.CastleDefense
                    && ob.Direction == MirDirection.UpLeft) continue;
            }
            return true;
        }

        foreach (var player in _otherPlayers.Values)
        {
            if (player.CellX == x && player.CellY == y && !player.Dead)
                return true;
        }

        return false;
    }

    // 所有常驻 HUD 都基于当前 viewport 重新锚定。不能只在 _Ready 中
    // 计算一次：Linux/Windows 高 DPI 下 Godot 可能在场景创建后才完成
    // 窗口尺寸调整，旧坐标会把底栏留在屏幕中间。
    private void LayoutHud()
    {
        if (_uiLayer == null || !IsInstanceValid(_uiLayer)) return;
        Vector2 vp = GetHudViewportSize() / UiScale;
        if (vp.X <= 0 || vp.Y <= 0) return;

        void Center(DXControl control, int yOffset = 0)
        {
            if (control == null) return;
            control.Location = new Vector2I(
                Math.Max(0, (int)((vp.X - control.Size.X) / 2f)),
                Math.Max(0, (int)((vp.Y - control.Size.Y) / 2f) + yOffset));
        }

        if (_mainPanel != null)
            _mainPanel.Location = new Vector2I(
                Math.Max(0, (int)((vp.X - _mainPanel.Size.X) / 2f)),
                Math.Max(0, (int)(vp.Y - _mainPanel.Size.Y)));

        if (_beltDialog != null && _mainPanel != null)
            _beltDialog.ApplyDefaultAnchor(vp, _mainPanel.Location, _mainPanel.Size);

        if (_chatLog != null && _mainPanel != null)
            _chatLog.Position = new Vector2(
                Math.Max(0, _mainPanel.Position.X),
                Math.Max(0, _mainPanel.Position.Y - _chatLog.Size.Y - 29));
        if (_chatTextBox != null && _mainPanel != null)
            _chatTextBox.Location = new Vector2I(
                Math.Max(0, (int)_mainPanel.Position.X),
                Math.Max(0, (int)(_mainPanel.Position.Y - _chatTextBox.Size.Y - 2)));
        if (_miniMap != null)
            _miniMap.Location = new Vector2I(
                Math.Max(0, (int)(vp.X - _miniMap.Size.X)), 0);

        if (_questTracker != null)
            _questTracker.Location = new Vector2I(
                Math.Max(0, (int)(vp.X - _questTracker.Size.X)),
                (int)_miniMap.Size.Y + 5);

        if (_questDialog != null)
            Center(_questDialog);

        LayoutBuffDialog();

        if (_groupHealthPanel != null)
            _groupHealthPanel.Location = new Vector2I(12, 48);

        if (_inventoryDialog != null)
            _inventoryDialog.Location = new Vector2I(
                Math.Max(0, (int)(vp.X - _inventoryDialog.Size.X)),
                (int)_miniMap.Size.Y);

        if (_storageDialog != null)
            _storageDialog.Location = new Vector2I(
                Math.Max(0, (int)(vp.X - _storageDialog.Size.X - _inventoryDialog.Size.X)), 0);

        if (_magicDialog != null)
            _magicDialog.Location = new Vector2I(
                Math.Max(0, (int)(vp.X - _magicDialog.Size.X)), 0);

        if (AutoPotionBox != null)
            AutoPotionBox.Location = new Vector2I(
                Math.Max(0, (int)((vp.X - AutoPotionBox.Size.X) / 2f)),
                Math.Max(0, (int)((vp.Y - AutoPotionBox.Size.Y) / 2f)));

        if (_currencyDialog != null)
            Center(_currencyDialog);

        if (_filterDropDialog != null)
            Center(_filterDropDialog);

        if (_consignmentDialog != null)
            Center(_consignmentDialog);
        if (_marketHistoryDialog != null)
            Center(_marketHistoryDialog);

        if (_fishingDialog != null)
        {
            var characterLocation = _characterDialog?.Location ?? Vector2I.Zero;
            var characterSize = _characterDialog?.Size ?? Vector2.Zero;
            _fishingDialog.Location = new Vector2I(
                (int)(characterLocation.X + characterSize.X), characterLocation.Y);
        }
        if (_fishingCatchDialog != null)
            Center(_fishingCatchDialog, 200);

        if (_bundleDialog != null)
            Center(_bundleDialog);

        if (_fortuneDialog != null)
            Center(_fortuneDialog);

        if (_lootBoxDialog != null)
            Center(_lootBoxDialog);

        if (_milestoneDialog != null)
            Center(_milestoneDialog, 100);

        if (_guildMemberDialog != null)
            Center(_guildMemberDialog);

        if (_dungeonFinderDialog != null)
            Center(_dungeonFinderDialog);

        if (_beltDialog != null && _mainPanel != null)
            _beltDialog.ApplyDefaultAnchor(vp, _mainPanel.Location, _mainPanel.Size);

        if (_magicBar != null && _mainPanel != null)
        {
            // Mir3-Research/docs/UI_GLOBAL_OFFSET_ANALYSIS.md：清掉贴顶脏配置后再锚底。
            _magicBar.ClearInvalidPersistedPosition();
            _magicBar.ApplyDefaultAnchor(vp, _mainPanel.Location, _mainPanel.Size);
        }

        // 原版 GameScene.SetDefaultLocations 的其余窗口位置。
        if (_menuDialog != null)
            _menuDialog.Location = new Vector2I(Math.Max(0, (int)(vp.X - _menuDialog.Size.X)),
                Math.Max(0, (int)(vp.Y - _menuDialog.Size.Y - _mainPanel.Size.Y)));
        Center(_configDialog);
        Center(_chatOptionsDialog);
        Center(_exitDialog);
        Center(_tradeDialog);
        Center(_guildDialog);
        Center(_rankingDialog);
        Center(_companionDialog);
        Center(_npcCompanionStorageDialog);
        Center(_communicationDialog);
        Center(_groupDialog);
        Center(_gameStoreDialog);
        Center(_editCharacterDialog);
        Center(_helpDialog);
        if (_captionDialog != null)
            _captionDialog.Location = Vector2I.Zero;
        if (_npcDialog != null)
            _npcDialog.Location = Vector2I.Zero;
        Center(_npcSocketDialog);
        Center(_npcSocketCombineDialog);
        Center(_npcQuestDialog);
        if (_monsterDialog != null)
            _monsterDialog.Location = new Vector2I(Math.Max(0, (int)((vp.X - _monsterDialog.Size.X) / 2f)), 50);
        if (_timerDialog != null && _mainPanel != null)
            _timerDialog.Location = new Vector2I((int)(_mainPanel.Position.X + _mainPanel.Size.X - 115),
                Math.Max(0, (int)(vp.Y - 170)));

        // 最后统一执行一次边界约束。上面的角落/居中布局负责“应该在哪里”，
        // 这里负责“绝不能在哪里”：旧配置、窗口尺寸瞬变、窗口拖动都不能让
        // 任何常驻 UI 控件越过当前逻辑画布。
        ClampHudControlsToViewport(vp);
    }

    private void AuditLegacyHudIfRequested()
    {
        if (!AutoLoginArgs.LegacyHud || _mainPanel == null) return;
        bool pass = _mainPanel.AuditLegacyHud(out string details);
        GD.Print($"[LegacyHud] {(pass ? "PASS" : "FAIL")} viewport={GetHudViewportSize()} location={_mainPanel.Location} {details}");
    }

    private void ClampHudControlsToViewport(Vector2 logicalViewport)
    {
        if (_uiLayer == null || !IsInstanceValid(_uiLayer)) return;
        foreach (Node child in _uiLayer.GetChildren())
        {
            if (child is not Control control || !IsInstanceValid(control) || !control.Visible)
                continue;
            if (control.Size.X <= 0 || control.Size.Y <= 0) continue;

            float maxX = Mathf.Max(0, logicalViewport.X - control.Size.X);
            float maxY = Mathf.Max(0, logicalViewport.Y - control.Size.Y);
            control.Position = new Vector2(
                Mathf.Clamp(control.Position.X, 0, maxX),
                Mathf.Clamp(control.Position.Y, 0, maxY));
        }
    }

    private MapInfo GetMapInfo(int mapIndex)
    {
        return Globals.MapInfoList?.Binding.FirstOrDefault(m => m.Index == mapIndex);
    }

    /// <summary>MagicBar 尺寸变化后重新锚定 (仅未拖拽时), 公式同 LayoutHud。</summary>
    private void OnMagicBarResized()
    {
        if (_magicBar == null || !IsInstanceValid(_magicBar) || _magicBar.UserMoved) return;
        if (_mainPanel == null || !IsInstanceValid(_mainPanel)) return;
        Vector2 vp = GetHudViewportSize() / UiScale;
        if (vp.X <= 0 || vp.Y <= 0) return;
        _magicBar.ApplyDefaultAnchor(vp, _mainPanel.Location, _mainPanel.Size);
    }

    /// <summary>
    /// Buff 栏贴小地图左侧。Size 随图标数变化后必须重算，否则会往右顶进小地图像「飘出去」。
    /// 对齐原版 GameScene.SetDefaultLocations: Size.Width - MiniMap - Buff - 5, Y=0。
    /// </summary>
    private void LayoutBuffDialog()
    {
        if (_buffDialog == null || !IsInstanceValid(_buffDialog)) return;
        if (_miniMap == null || !IsInstanceValid(_miniMap)) return;
        Vector2 vp = GetHudViewportSize() / UiScale;
        if (vp.X <= 0 || vp.Y <= 0) return;
        int miniW = (int)_miniMap.Size.X;
        _buffDialog.Location = new Vector2I(
            Math.Max(0, (int)(vp.X - miniW - _buffDialog.Size.X - 5)), 0);
    }

    private void OpenBigMap()
    {
        OpenBigMapForMap(GetMapInfo(_playerMapIndex));
    }

    private void OpenBigMapForMap(MapInfo map)
    {
        if (map == null) return;
        var vp = GetHudViewportSize() / UiScale;
        bool isCurrent = map.Index == _playerMapIndex;
        int mapWidth = _mapView.Map?.Width ?? 0;
        int mapHeight = _mapView.Map?.Height ?? 0;
        string mapPath = ProjectSettings.GlobalizePath($"res://../Debug/Client/Map/{map.FileName}.map");
        if (!MirMap.TryGetDimensions(mapPath, out int selectedWidth, out int selectedHeight))
        {
            selectedWidth = mapWidth;
            selectedHeight = mapHeight;
        }
        _bigMap.SetMap(map, selectedWidth, selectedHeight, _playerObjectID, isCurrent);
        _bigMap.Location = new Vector2I(
            Math.Max(0, (int)((vp.X - _bigMap.Size.X) / 2f)),
            Math.Max(0, (int)((vp.Y - _bigMap.Size.Y) / 2f)));
        _bigMap.Visible = true;
    }

    public void OpenQuestMap(NPCInfo npc)
    {
        if (npc?.Region?.Map != null)
            OpenBigMapForMap(npc.Region.Map);
    }

    // StartGame 数据 -> HUD (等级/职业/属性/血蓝/经验/Buff/任务/攻击宠物模式)
    private void InitHudData(StartInformation info)
    {
        // StatsUpdate 可能在 StartGame 的延迟初始化之前到达。此前这里无条件
        // new Stats() 会把已经收到的最大负重、攻击速度等属性清空，随后
        // WeightUpdate 仍保留当前重量，于是出现 bag=225/0、wear=42/0，
        // 跑步判断永远失败。只有尚未收到有效属性时才初始化为空集合。
        if (_playerStats == null || _playerStats.Count == 0)
            _playerStats = new Stats();
        _observer = false;
        _playerPoison = info.Poison;
        UpdateAbyssVision(_playerPoison.HasFlag(PoisonType.Abyss));
        _playerLevel = info.Level;
        _playerExperience = info.Experience;
        InSafeZone = info.InSafeZone;
        CombatUntilMs = 0;
        // MaxExperience 由 S.InformMaxExperience / S.LevelChanged 填充 (排空可能已先到)
        _currentHP = info.CurrentHP;
        _currentMP = info.CurrentMP;
        _currentFP = info.CurrentFP;
        _attackMode = info.AttackMode;
        _petMode = default;

        _mainPanel?.SetLevel(_playerLevel);
        _mainPanel?.SetClass(info.Class);
        _mainPanel?.SetExperience(_playerExperience, _playerMaxExperience);
        _mainPanel?.SetAttackMode(_attackMode);
        _mainPanel?.SetPetModeEnabled(CompanionEnabled);
        RefreshPlayerBars();

        _buffs.Clear();
        if (info.Buffs != null)
            foreach (var b in info.Buffs)
            {
                _buffs[b.Index] = b;
                if (_player != null)
                {
                    if (b.Type == BuffType.Cloak) _player.Cloaked = true;
                    if (b.Type == BuffType.GhostWalk) _player.GhostWalking = true;
                    if (b.Type == BuffType.DragonRepulse) _player.DragonRepulsed = true;
                    if (b.Type == BuffType.ElementalHurricane) _player.ElementalHurricane = true;
                }
            }
        _buffDialog?.BuffsChanged(_buffs);

        var quests = info.Quests ?? Enumerable.Empty<ClientUserQuest>();
        _userQuests.Clear();
        foreach (var quest in quests)
            if (quest != null) { if (quest.Quest == null) quest.Complete(); _userQuests[quest.Index] = quest; }
        quests = _userQuests.Values;
        _questTracker?.PopulateQuests(quests);
        _questDialog?.SetQuests(quests);
        _mainPanel?.SetQuestIndicators(
            quests.Any(q => q != null && !q.IsComplete),
            quests.Any(q => q != null && q.IsComplete));
        _communicationDialog?.SetFriends(info.Friends);
        Companions.Clear();
        if (info.Companions != null) Companions.AddRange(info.Companions.Where(x => x != null));
        Companion = Companions.FirstOrDefault(x => x?.Index == info.Companion);
        _npcCompanionStorageDialog?.SetCompanions(Companions);
        _companionDialog?.ApplyCompanion(Companion);

        // ---- M9: 物品数据 (StartInformation + 登录仓库包) ----
        FillItems(info.Items);
        ApplyBeltLinks(info.BeltLinks);
        AutoPotionBox?.ApplyLinks(info.AutoPotionLinks);

        // 已学技能 (StartInformation.Magics 一次性下发, S.NewMagic 只在学新技能时发)
        UserMagics.Clear();
        if (info.Magics != null)
        {
            foreach (var m in info.Magics)
            {
                if (m != null) GD.Print($"[Magic][raw] {m.Info?.Name ?? m.InfoIndex.ToString()} Set1={m.Set1Key} Set2={m.Set2Key}");
                if (m == null) continue;
                // 网络反序列化只保证 InfoIndex；不要依赖 CompleteObject 是否在
                // 当前进程执行过，否则整个技能栏会看起来像没有技能。
                if (m.Info == null)
                    m.Complete();
                if (m.Info == null)
                {
                    GD.PrintErr($"[Magic] 技能信息解析失败 InfoIndex={m.InfoIndex}");
                    continue;
                }
                UserMagics[m.Info] = m;
            }
            GD.Print($"[Magic] 加载已有技能 {UserMagics.Count} 个; 已绑定: {UserMagics.Values.Count(m => m != null && (m.Set1Key != Library.SpellKey.None || m.Set2Key != Library.SpellKey.None || m.Set3Key != Library.SpellKey.None || m.Set4Key != Library.SpellKey.None))}");
        }
        _magicBar?.Refresh();
        _magicDialog?.Refresh();
        Currencies.Clear();
        if (info.Currencies != null) Currencies.AddRange(info.Currencies);
        RefreshCurrency();

        if (info.StorageSize > 0) StorageSize = info.StorageSize;
        FillStorage(_net.Connection.PendingStorageItems);
    }

    // 血/蓝/专注条刷新 (Stats 就绪后与增量变化后调用)
    private void RefreshPlayerBars()
    {
        _mainPanel?.SetHealth(_currentHP);
        _mainPanel?.SetMana(_currentMP);
        _mainPanel?.SetFocus(_currentFP);
    }

    private void OnStatsUpdate(S.StatsUpdate p)
    {
        _playerStats = p.Stats ?? new Stats();
        _mainPanel?.SetStats(_playerStats);
        if (_player == null) return;
        _player.MaxHealth = _playerStats[Stat.Health];
        _player.MaxMana = _playerStats[Stat.Mana];
        // 无照明物时保留蜡烛级基础光；装备蜡烛/火把后在原始 Stat.Light
        // 基础上额外增强约 20%，让照明物的差异仍然保留但夜间不至于过暗。
        _player.Light = EffectivePlayerLight(_playerStats[Stat.Light]);
        if (_player.Health <= 0) _player.Health = _playerStats[Stat.Health];
        RefreshPlayerBars();
    }

    // 原版深渊黑视: 光照层全黑+玩家微光由 MapLightLayer.PlayerLightState 处理;
    // 这里循环施放 Abyss 环绕特效——原版每次光照层重建都重画
    // user.CreateMagicEffect(MagicEffect.Abyss), 对应 14帧×70ms≈980ms。
    private const double AbyssEffectIntervalSeconds = 0.98;

    private static int EffectivePlayerLight(int statLight)
    {
        if (statLight <= 0) return 3;
        return Math.Max(3, (int)Math.Ceiling(statLight * 1.2));
    }

    private void UpdateAbyssVision(bool active)
    {
        if (active == _abyssVisionActive) return;
        _abyssVisionActive = active;
        _abyssEffectTimer = 0; // 置零后下一帧立即出特效
    }

    private void SpawnAbyssVisionEffect()
    {
        var def = MagicEffectTable.Get(MagicType.Abyss);
        if (def == null) return;
        SpawnCastEffect(def, _playerLocation.X, _playerLocation.Y);
    }

    private IEnumerable<MapLightLayer.LightSource> GetObjectLightSources()
    {
        if (_player != null && _player.Light > 0)
            yield return new MapLightLayer.LightSource(_player.Position + new Vector2(24f, 0f), _player.Light,
                _playerStats[Stat.Light] == 0
                    ? new Color(1f, 1f, 1f, 0.47f)
                    : new Color(1f, 0.86f, 0.55f));
        foreach (var ob in _objects.Values)
            if (ob.Light > 0 && !ob.Dead)
                yield return new MapLightLayer.LightSource(ob.Position + new Vector2(24f, 0f), ob.Light,
                    new Color(1f, 0.86f, 0.55f));
        foreach (var player in _otherPlayers.Values)
            if (!player.Dead)
            {
                int light = EffectivePlayerLight(player.Light);
                yield return new MapLightLayer.LightSource(player.Position + new Vector2(24f, 0f), light,
                    player.Light > 0 ? new Color(1f, 0.86f, 0.55f) : new Color(1f, 1f, 1f, 0.47f));
            }
        foreach (Node child in GetChildren())
            if (child is MirEffectNode fx && fx.FrameLight > 0)
                // 旧端 MapControl.Light 对效果光使用 EffectLightScaleDivisor=5，
                // 不能和人物/物体 Light 共用同一半径，否则技能光圈会放大五倍。
                yield return new MapLightLayer.LightSource(fx.Position + new Vector2(24f, 16f),
                    Math.Max(1, fx.FrameLight / 5), fx.FrameLightColour);
    }

    private void OnLevelChanged(S.LevelChanged p)
    {
        _playerLevel = p.Level;
        _playerExperience = p.Experience;
        _playerMaxExperience = p.MaxExperience;
        _mainPanel?.SetLevel(_playerLevel);
        _mainPanel?.SetExperience(_playerExperience, _playerMaxExperience);
    }

    private void OnGainedExperience(decimal amount)
    {
        _playerExperience += amount;
        _mainPanel?.SetExperience(_playerExperience, _playerMaxExperience);
    }

    private void OnInformMaxExperience(decimal maxExperience)
    {
        _playerMaxExperience = maxExperience;
        _mainPanel?.SetExperience(_playerExperience, _playerMaxExperience);
    }

    // 蓝/专注: 照原版按 ObjectID 累加 Change (只有玩家的会到 HUD)
    private void OnManaChanged(uint objectID, int change)
    {
        if (objectID != _playerObjectID) return;
        _currentMP += change;
        RefreshPlayerBars();
    }

    private void OnFocusChanged(uint objectID, int change)
    {
        if (objectID != _playerObjectID) return;
        _currentFP += change;
        RefreshPlayerBars();
    }

    private void OnBuffAdd(S.BuffAdd p)
    {
        if (p.Buff == null) return;
        _buffs[p.Buff.Index] = p.Buff;
        if (_player != null)
        {
            switch (p.Buff.Type)
            {
                case BuffType.Cloak: _player.Cloaked = true; break;
                case BuffType.GhostWalk: _player.GhostWalking = true; break;
                case BuffType.DragonRepulse: _player.DragonRepulsed = true; break;
                case BuffType.ElementalHurricane: _player.ElementalHurricane = true; break;
            }
        }
        if (_buffEffects.Remove(p.Buff.Index, out var oldFx)) oldFx.QueueFree();
        if (_player != null && TryGetBuffEffect(p.Buff.Type, out var def))
        {
            var fx = new MirEffectNode();
            AddChild(fx);
            fx.SetupTarget(def.File, def.StartIndex, def.FrameCount, def.DelayMs, _player,
                () => _player.CellY);
            fx.Loop = true;
            fx.Blend = true;
            fx.BlendRate = def.BlendRate;
            fx.FrameLight = 10;
            fx.FrameLightColour = def.Colour;
            _buffEffects[p.Buff.Index] = fx;
        }
        _buffDialog?.BuffsChanged(_buffs);
    }

    private void OnBuffRemove(int index)
    {
        if (_buffEffects.Remove(index, out var fx)) fx.QueueFree();
        if (_buffs.Remove(index, out var removed))
        {
            if (_player != null)
            {
                switch (removed.Type)
                {
                    case BuffType.Cloak: _player.Cloaked = false; break;
                    case BuffType.GhostWalk: _player.GhostWalking = false; break;
                    case BuffType.DragonRepulse:
                        if (_player.Animation == MirAnimation.DragonRepulseMiddle)
                            _player.PlayDragonRepulseEnd();
                        _player.DragonRepulsed = false;
                        break;
                    case BuffType.ElementalHurricane: _player.ElementalHurricane = false; break;
                }
            }
            _buffDialog?.BuffsChanged(_buffs);
        }
    }

    private void OnAutoPathChanged(S.AutoPathChanged packet)
    {
        _autoPathRoutes.Clear();
        if (packet?.Routes != null) _autoPathRoutes.AddRange(packet.Routes);
        _autoPathCancelPending = false;
        UpdateAutoPathProgress();
    }

    private void UpdateAutoPathProgress()
    {
        _autoPathProgressMap = _autoPathRoutes.Count == 0 ? -1 : _playerMapIndex;
        _autoPathProgressPoint = -1;
        if (_autoPathProgressMap >= 0)
        {
            var leg = _autoPathRoutes.SelectMany(r => r?.Legs ?? new List<AutoPathRouteLeg>())
                .FirstOrDefault(x => x.MapIndex == _autoPathProgressMap);
            if (leg?.Points != null)
            {
                _autoPathProgressPoint = 0;
                while (_autoPathProgressPoint < leg.Points.Count
                    && Functions.InRange(_playerLocation, leg.Points[_autoPathProgressPoint], 1))
                    _autoPathProgressPoint++;
            }
        }

        _miniMap?.UpdateAutoPathRoutes(_autoPathRoutes, _playerMapIndex,
            _autoPathProgressMap, _autoPathProgressPoint);
        _bigMap?.UpdateAutoPathRoutes(_autoPathRoutes,
            _autoPathProgressMap, _autoPathProgressPoint);
    }

    private bool TryGetBuffEffect(BuffType type, out MagicEffectTable.ImpactDef def)
    {
        def = type switch
        {
            BuffType.MagicShield => new MagicEffectTable.ImpactDef { File = LibraryFile.Magic, StartIndex = 850, FrameCount = 3, DelayMs = 200, Colour = MagicEffectTable.Wind, BlendRate = 0.7f },
            BuffType.SuperiorMagicShield => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx2, StartIndex = 1920, FrameCount = 3, DelayMs = 200, Colour = MagicEffectTable.Fire, BlendRate = 0.7f },
            BuffType.CelestialLight => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx2, StartIndex = 300, FrameCount = 3, DelayMs = 200, Colour = MagicEffectTable.Holy, BlendRate = 0.7f },
            BuffType.DefensiveBlow => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx7, StartIndex = 880, FrameCount = 6, DelayMs = 100, Colour = MagicEffectTable.None },
            BuffType.ReflectDamage => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx2, StartIndex = 1240, FrameCount = 3, DelayMs = 100, Colour = MagicEffectTable.None },
            BuffType.LifeSteal => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx2, StartIndex = 1260, FrameCount = 6, DelayMs = 150, Colour = MagicEffectTable.Dark },
            BuffType.FrostBite => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx5, StartIndex = 600, FrameCount = 7, DelayMs = 150, Colour = MagicEffectTable.Ice },
            BuffType.PoisonousCloud => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx4, StartIndex = 400, FrameCount = 15, DelayMs = 100, Colour = MagicEffectTable.Dark },
            BuffType.Evasion => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx4, StartIndex = 2500, FrameCount = 12, DelayMs = 70, Colour = MagicEffectTable.None, DrawType = MirEffectNode.EffectLayer.Floor },
            BuffType.RagingWind => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx4, StartIndex = 2600, FrameCount = 12, DelayMs = 70, Colour = MagicEffectTable.None, DrawType = MirEffectNode.EffectLayer.Floor },
            BuffType.Concentration => new MagicEffectTable.ImpactDef { File = LibraryFile.MagicEx5, StartIndex = 300, FrameCount = 15, DelayMs = 100, Colour = MagicEffectTable.None },
            _ => null,
        };
        return def != null;
    }

    private void OnObjectBuffAdd(uint objectID, BuffType type, int extra)
    {
        var target = GetMagicTargetNode(objectID);
        if (target == null || !TryGetBuffEffect(type, out var def)) return;
        var key = (objectID, type);
        if (_objectBuffEffects.Remove(key, out var oldFx)) oldFx.QueueFree();
        var fx = new MirEffectNode();
        AddChild(fx);
        fx.SetupTarget(def.File, def.StartIndex, def.FrameCount, def.DelayMs, target,
            () => GetTargetRenderY(target));
        fx.Loop = true;
        fx.Blend = true;
        fx.BlendRate = def.BlendRate;
        fx.DrawType = def.DrawType;
        fx.FrameLight = 10;
        fx.FrameLightColour = def.Colour;
        _objectBuffEffects[key] = fx;
    }

    private void SetMovementEffect(uint objectID, MagicType magic, Node2D target)
    {
        if (target == null || magic is not (MagicType.Assault or MagicType.HundredFist)) return;
        var (file, start, count, delay, colour) = magic == MagicType.Assault
            ? (LibraryFile.MagicEx2, 740, 3, 100, MagicEffectTable.None)
            : (LibraryFile.MagicEx5, 2100, 5, 200, MagicEffectTable.Fire);
        var fx = new MirEffectNode();
        AddChild(fx);
        fx.SetupTarget(file, start, count, delay, target, () => GetTargetRenderY(target));
        fx.Loop = true;
        fx.Blend = true;
        fx.BlendRate = 0.7f;
        fx.Direction = target is PlayerRenderer player ? player.Direction : MirDirection.Up;
        fx.FrameLight = 10;
        fx.FrameLightColour = colour;
        _movementEffects[objectID] = fx;
    }

    private void ClearMovementEffect(uint objectID)
    {
        if (_movementEffects.Remove(objectID, out var fx)) fx.QueueFree();
    }

    private void OnObjectBuffRemove(uint objectID, BuffType type)
    {
        if (_objectBuffEffects.Remove((objectID, type), out var fx)) fx.QueueFree();
    }

    private void OnObjectPoison(uint objectID, PoisonType poison)
    {
        if (objectID == _playerObjectID)
        {
            _playerPoison = poison;
            UpdateAbyssVision(_playerPoison.HasFlag(PoisonType.Abyss));
        }
        var target = GetMagicTargetNode(objectID);
        if (target == null) return;
        if (_objectPoisonEffects.Remove(objectID, out var oldFx)) oldFx.QueueFree();

        var config = poison.HasFlag(PoisonType.WraithGrip)
            ? (LibraryFile.MagicEx4, 1424, 10, 100, 0.4f, MagicEffectTable.None)
            : poison.HasFlag(PoisonType.HellFire) || poison.HasFlag(PoisonType.Burn)
                ? (LibraryFile.MagicEx, 790, 6, 100, 0.7f, MagicEffectTable.Fire)
                : poison.HasFlag(PoisonType.Silenced) || poison.HasFlag(PoisonType.Abyss)
                    ? (LibraryFile.ProgUse, 680, 6, 150, 0.8f, MagicEffectTable.None)
                    : poison.HasFlag(PoisonType.Parasite)
                        ? (LibraryFile.MagicEx5, 900, 7, 100, 0.8f, MagicEffectTable.None)
                        : poison.HasFlag(PoisonType.Neutralize)
                            ? (LibraryFile.MagicEx7, 470, 6, 120, 0.8f, MagicEffectTable.None)
                            : poison.HasFlag(PoisonType.Fear)
                                ? (LibraryFile.ProgUse, 700, 15, 100, 0.7f, MagicEffectTable.None)
                                : poison.HasFlag(PoisonType.Containment)
                                    ? (LibraryFile.MagicEx2, 2040, 10, 100, 0.7f, MagicEffectTable.None)
                                    : poison.HasFlag(PoisonType.Chain)
                                        ? (LibraryFile.MagicEx7, 27, 4, 100, 0.7f, MagicEffectTable.None)
                                        : poison.HasFlag(PoisonType.Hemorrhage)
                                            ? (LibraryFile.MagicEx7, 1290, 1, 100, 0.7f, MagicEffectTable.None)
                                            : poison.HasFlag(PoisonType.Binding)
                                                ? (LibraryFile.MagicEx5, 3100, 14, 100, 0.7f, MagicEffectTable.None)
                                                : (LibraryFile.Magic, -1, 0, 0, 0f, Colors.White);
        if (config.Item2 < 0) return;
        var fx = new MirEffectNode();
        AddChild(fx);
        fx.SetupTarget(config.Item1, config.Item2, config.Item3, config.Item4, target,
            () => GetTargetRenderY(target));
        fx.Loop = true;
        fx.Blend = true;
        fx.BlendRate = config.Item5;
        fx.FrameLight = 10;
        fx.FrameLightColour = config.Item6;
        _objectPoisonEffects[objectID] = fx;
    }

    private void OnBuffChanged(S.BuffChanged p)
    {
        if (_buffs.TryGetValue(p.Index, out var buff))
            buff.Stats = p.Stats;
        _buffDialog?.BuffsChanged(_buffs);
    }

    private void OnBuffTime(S.BuffTime p)
    {
        if (_buffs.TryGetValue(p.Index, out var buff))
            buff.RemainingTime = p.Time;
        _buffDialog?.BuffsChanged(_buffs);
    }

    private void OnBuffPaused(int index, bool paused)
    {
        if (_buffs.TryGetValue(index, out var buff))
            buff.Pause = paused;
        _buffDialog?.BuffsChanged(_buffs);
    }

    // 攻击/宠物模式循环 (照原版 (Mode+1)%5, 仅本地切 + 发包)
    private void CycleAttackMode()
    {
        _attackMode = (AttackMode)(((int)_attackMode + 1) % 5);
        _mainPanel?.SetAttackMode(_attackMode);
        _net.Connection.Enqueue(new C.ChangeAttackMode { Mode = _attackMode });
    }

    private void CyclePetMode()
    {
        if (!CompanionEnabled) return;
        _petMode = (PetMode)(((int)_petMode + 1) % 5);
        _mainPanel?.SetPetMode(_petMode);
        _net.Connection.Enqueue(new C.ChangePetMode { Mode = _petMode });
    }

    // 服务端回显 (权威): 与本地循环一致, 双保险
    // 原版 CConnection.Process(S.ChangeAttackMode): 回显时把模式描述打到聊天。
    private void OnAttackModeChanged(AttackMode mode)
    {
        _attackMode = mode;
        _mainPanel?.SetAttackMode(mode);
        if (_mainPanel != null && _chatLog != null)
            ReceiveChat(_mainPanel.AttackModeLabel.Text, MessageType.System);
    }

    private void OnPetModeChanged(PetMode mode)
    {
        if (!CompanionEnabled) return;
        _petMode = mode;
        _mainPanel?.SetPetMode(mode);
        if (_mainPanel != null && _chatLog != null)
            ReceiveChat(_mainPanel.PetModeLabel.Text, MessageType.System);
    }

    // ==================== M9 物品系统 ====================

    private ClientUserItem[] GetGrid(GridType type)
    {
        switch (type)
        {
            case GridType.Inventory: return Inventory;
            case GridType.Equipment: return Equipment;
            case GridType.Storage: return Storage;
            case GridType.PartsStorage: return PartsStorage;
            case GridType.GuildStorage: return _guildDialog?.GuildStorageItems;
            case GridType.CompanionInventory: return CompanionInventory;
            case GridType.CompanionEquipment: return CompanionEquipment;
            default: return null;
        }
    }

    private ClientUserItem ItemAt(GridType type, int slot)
    {
        var arr = GetGrid(type);
        if (arr == null || slot < 0 || slot >= arr.Length) return null;
        return arr[slot];
    }

    // 解锁源/目标格 (服务端回包后解除 Locked, 允许后续操作)
    private void UnlockCell(GridType type, int slot)
    {
        DXItemCell[] cells = type switch
        {
            GridType.Inventory => InventoryCells,
            GridType.Equipment => EquipmentCells,
            GridType.Belt => _beltDialog?.Grid?.Cells,
            GridType.Storage => _storageDialog?.Grid?.Cells,
            GridType.PartsStorage => _storageDialog?.PartGrid?.Cells,
            GridType.GuildStorage => GuildStorageItemCells,
            GridType.CompanionInventory => CompanionInventoryCells,
            GridType.CompanionEquipment => CompanionEquipmentCells,
            _ => null,
        };
        if (cells != null && slot >= 0 && slot < cells.Length && cells[slot] != null)
        {
            cells[slot].Locked = false;
            cells[slot].Selected = false;
            cells[slot].UpdateBorder();
        }
    }

    // 批量变更后刷新所有可见格
    public void RefreshItemGrids()
    {
        foreach (var c in InventoryCells) c?.RefreshItem();
        foreach (var c in EquipmentCells) c?.RefreshItem();
        _beltDialog?.Grid?.RefreshGrid();
        _storageDialog?.Grid?.RefreshGrid();
        _storageDialog?.PartGrid?.RefreshGrid();
        _companionDialog?.InventoryGrid?.RefreshGrid();
        foreach (var c in CompanionEquipmentCells) c?.RefreshItem();
        foreach (var c in GuildStorageItemCells) c?.RefreshItem();
    }

    // 背包权重重算 (服务端未发 WeightUpdate 时的本地兜底)
    public void RefreshInventoryWeights()
    {
        int bag = 0;
        foreach (var item in Inventory)
            if (item != null) bag += item.Weight;
        BagWeight = bag;
        _inventoryDialog?.SetWeight(bag);
    }

    public void RefreshCurrency()
    {
        long gold = Currencies.FirstOrDefault(x => x.Info?.Type == CurrencyType.Gold)?.Amount ?? 0;
        long gg = Currencies.FirstOrDefault(x => x.Info?.Type == CurrencyType.GameGold)?.Amount ?? 0;
        _inventoryDialog?.SetCurrency(gold, gg);

        // 主面板 FP/CP (原版 CurrencyChanged 语义)
        if (_mainPanel != null)
        {
            _mainPanel.FPLabel.Text = Currencies.FirstOrDefault(x => x.Info?.Type == CurrencyType.FP)?.Amount.ToString() ?? "0";
            _mainPanel.CPLabel.Text = Currencies.FirstOrDefault(x => x.Info?.Type == CurrencyType.CP)?.Amount.ToString() ?? "0";
        }
    }

    // 拿起物品跟随鼠标 + 悬浮提示
    private void UpdateMouseItem()
    {
        if (_mouseItemLabel == null || _hoverLabel == null) return;
        var dragged = DXItemCell.SelectedCell?.Item;
        if (dragged != null)
        {
            // 物品图标
            if (_mouseItemIcon != null)
            {
                int drawIndex = DXItemCell.GetItemDrawIndex(dragged);
                _mouseItemIcon.LibraryFile = DXItemCell.ItemIconLibraryFile;
                _mouseItemIcon.Index = drawIndex;
                _mouseItemIcon.Visible = true;
            }
            // 物品名称
            _mouseItemLabel.Visible = true;
            _mouseItemLabel.Text = $"{dragged.Info?.Local()}" + (dragged.Count > 1 ? $" x{dragged.Count}" : "");
        }
        else
        {
            if (_mouseItemIcon != null) _mouseItemIcon.Visible = false;
            _mouseItemLabel.Visible = false;
        }

        if (_hoverItem != null && dragged == null)
        {
            _hoverLabel.Visible = true;
            _hoverLabel.Text = BuildItemHoverText(_hoverItem);
            FitHoverLabelSize();
        }
        else
        {
            _hoverLabel.Visible = false;
        }

        if (_mouseItemIcon?.Visible == true || _mouseItemLabel.Visible || _hoverLabel.Visible)
        {
            var p = GetGlobalMousePosition() / UiScale;
            // 图标在鼠标右下方，文字在图标右侧
            if (_mouseItemIcon != null && _mouseItemIcon.Visible)
                _mouseItemIcon.Position = new Vector2(p.X + 8, p.Y + 8);
            _mouseItemLabel.Position = new Vector2(p.X + 42, p.Y + 14);
            _hoverLabel.Position = new Vector2(p.X + 14, p.Y + 10);
        }
    }

    public void SetHoverItem(ClientUserItem item)
    {
        _hoverItem = item;
    }

    /// <summary>
    /// 按当前文本重算悬浮框 Size（DXLabel 不自动调整；背景/边框按 Size 绘制）。
    /// 旧版 ItemLabelBuilder.Complete：文本宽 + 边距。
    /// </summary>
    private void FitHoverLabelSize()
    {
        if (_hoverLabel == null) return;
        const float padding = 6f;
        var lines = _hoverLabel.Text.Split('\n');
        float maxW = 0f;
        foreach (var line in lines)
        {
            var w = MirSkin.MeasureText(line, _hoverLabel.FontSize).X;
            if (w > maxW) maxW = w;
        }
        float lineH = lines.Length == 0 ? 0f : MirSkin.MeasureText(Lang.ChatLogPanelUi114Label, _hoverLabel.FontSize).Y;
        _hoverLabel.Size = new Vector2I(Mathf.RoundToInt(maxW + padding * 2f), Mathf.RoundToInt(lineH * lines.Length + padding * 2f));
    }

    /// <summary>
    /// 原版 GameScene.CreateItemLabel 的物品悬停信息。新版单色标签无法复刻
    /// 旧版每行颜色，但内容与顺序对齐旧版 ItemLabelBuilder。
    /// </summary>
    private string BuildItemHoverText(ClientUserItem item)
    {
        if (item?.Info == null) return string.Empty;
        _hoverLabel.TextColour = HoverRarityColour(item.Info.Rarity);
        return BuildItemHoverFull(item);
    }

    /// <summary>原版 GetItemLabelRarityColour 的稀有度颜色。</summary>
    public static Color HoverRarityColour(Rarity rarity) => rarity switch
    {
        Rarity.Superior => new Color(0.55f, 0.95f, 0.6f),
        Rarity.Elite => new Color(0.7f, 0.6f, 0.95f),
        _ => new Color(0.9f, 0.9f, 0.5f),
    };

    /// <summary>悬停文本核心（静态可测；原版 ItemLabelBuilder 的多行信息）。</summary>
    public static string BuildItemHoverCore(ClientUserItem item)
    {
        if (item?.Info == null) return string.Empty;

        ItemInfo displayInfo = item.Info;
        if (item.Info.ItemEffect == ItemEffect.ItemPart && item.AddedStats != null && item.AddedStats[Stat.ItemIndex] > 0)
        {
            var partInfo = Globals.ItemInfoList?.Binding.FirstOrDefault(x => x.Index == item.AddedStats[Stat.ItemIndex]);
            if (partInfo != null) displayInfo = partInfo;
        }

        var sb = new System.Text.StringBuilder(displayInfo.Local() ?? item.Info.Local() ?? string.Empty);
        if (item.Info.ItemEffect == ItemEffect.ItemPart)
            sb.Append(" - [部件]");

        // 原版 AddItemLabelMetadata 的 Type 行。
        var typeMember = typeof(ItemType).GetMember(displayInfo.ItemType.ToString()).FirstOrDefault();
        var description = typeMember?.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description;
        sb.Append('\n').Append($"类型: {description ?? displayInfo.ItemType.ToString()}");

        // 原版 Expirable 标记的过期时间行。
        if ((item.Flags & UserItemFlags.Expirable) == UserItemFlags.Expirable)
            sb.Append('\n').Append($"过期于 {Functions.ToString(item.ExpireTime, true)}");

        // 原版 Locked 标记的提示行。
        if ((item.Flags & UserItemFlags.Locked) == UserItemFlags.Locked)
            sb.Append('\n').Append("已锁定: 防止误售或误扔");

        return sb.ToString();
    }

    /// <summary>
    /// 完整版物品悬停信息：对齐旧版 CreateItemLabel 的全部分区
    /// （元数据/属性/需求/插槽/交易状态/描述/修理/套装等）。
    /// 单色标签，行序与旧版一致；玩家相关判断用当前实例状态。
    /// </summary>
    private string BuildItemHoverFull(ClientUserItem item)
    {
        if (item?.Info == null) return string.Empty;

        ItemInfo displayInfo = item.Info;
        if (item.Info.ItemEffect == ItemEffect.ItemPart && item.AddedStats != null && item.AddedStats[Stat.ItemIndex] > 0)
        {
            var partInfo = Globals.ItemInfoList?.Binding.FirstOrDefault(x => x.Index == item.AddedStats[Stat.ItemIndex]);
            if (partInfo != null) displayInfo = partInfo;
        }

        var sb = new System.Text.StringBuilder();

        // ---- Header: 名称 + [Part] ----
        sb.Append(displayInfo.Local() ?? item.Info.Local() ?? string.Empty);
        if (item.Info.ItemEffect == ItemEffect.ItemPart)
            sb.Append(" - [部件]");

        // ---- Metadata ----
        if (displayInfo.ItemType != ItemType.Nothing)
        {
            var typeMember = typeof(ItemType).GetMember(displayInfo.ItemType.ToString()).FirstOrDefault();
            var typeDesc = typeMember?.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description;
            sb.Append('\n').Append($"类型: {typeDesc ?? displayInfo.ItemType.ToString()}");
        }

        if (item.Info.Durability > 0)
        {
            switch (displayInfo.ItemType)
            {
                case ItemType.Book:
                    sb.Append('\n').Append($"页数: {item.CurrentDurability / 1000}/{item.MaxDurability / 1000}");
                    break;
                case ItemType.Meat:
                    sb.Append('\n').Append($"品质: {Math.Round(item.CurrentDurability / 1000M)}/{Math.Round(item.MaxDurability / 1000M)}");
                    break;
                case ItemType.Ore:
                    sb.Append('\n').Append($"纯度: {Math.Round(item.CurrentDurability / 1000M)}");
                    break;
                case ItemType.SocketGem:
                    sb.Append('\n').Append($"宝石类型: {GemTypeName(item.Info.Shape)}");
                    sb.Append('\n').Append($"纯度: {GemPurityText(item)}");
                    break;
                default:
                    if (item.Info.StackSize == 1)
                        sb.Append('\n').Append($"耐久: {Math.Round(item.CurrentDurability / 1000M)}/{Math.Round(item.MaxDurability / 1000M)}");
                    break;
            }
        }

        if (IsCurrencyItem(item.Info) || item.Info.ItemEffect == ItemEffect.Experience)
            sb.Append('\n').Append($"数量: {item.Count:#,##0}");
        else if (item.Info.ItemEffect == ItemEffect.ItemPart)
            sb.Append('\n').Append($"部件: {item.Count}/{displayInfo.PartCount}。");
        else if (item.Info.StackSize > 1)
            sb.Append('\n').Append($"数量: {item.Count}/{item.Info.StackSize}");

        if (item.Info.Weight > 0)
            sb.Append('\n').Append($"重量: {item.Info.Weight}");

        // ---- 货币/经验物品：直接返回（旧版只显示描述）----
        if (IsCurrencyItem(item.Info) || item.Info.ItemEffect == ItemEffect.Experience)
        {
            AppendDescription(sb, displayInfo);
            return sb.ToString();
        }

        // ---- 装备属性 ----
        switch (displayInfo.ItemType)
        {
            case ItemType.Consumable:
            case ItemType.Scroll:
                if (displayInfo.ItemEffect == ItemEffect.StatExtractor || displayInfo.ItemEffect == ItemEffect.RefineExtractor)
                    AppendEquipmentStats(sb, item, displayInfo);
                else
                    AppendPotionStats(sb, item);
                break;
            default:
                AppendEquipmentStats(sb, item, displayInfo);
                break;
        }

        // ---- 训练信息（武器/饰品等级）----
        AppendTrainingInfo(sb, item, displayInfo);

        // ---- 需求 ----
        AppendRequirements(sb, item, displayInfo);

        // ---- 插槽 ----
        AppendSocketInfo(sb, item, displayInfo);

        // ---- 交易状态 ----
        AppendTradeState(sb, item, displayInfo);

        // ---- 描述 ----
        AppendDescription(sb, displayInfo);

        // ---- 特殊修理 ----
        if (item.Info.Durability > 0 && item.Info.CanRepair && item.Info.StackSize == 1)
        {
            switch (item.Info.ItemType)
            {
                case ItemType.Weapon:
                case ItemType.Armour:
                case ItemType.Helmet:
                case ItemType.Necklace:
                case ItemType.Bracelet:
                case ItemType.Ring:
                case ItemType.Shoes:
                case ItemType.Shield:
                    sb.Append('\n');
                    if (Library.Time.Now >= item.NextSpecialRepair)
                        sb.Append("可特殊修理");
                    else
                        sb.Append($"特殊修理将于 {Functions.ToString(item.NextSpecialRepair - Library.Time.Now, true)}");
                    break;
            }
        }

        // ---- 过期 / 复活 ----
        if ((item.Flags & UserItemFlags.Expirable) == UserItemFlags.Expirable)
            sb.Append('\n').Append($"过期于 {Functions.ToString(item.ExpireTime, true)}");

        if (item.AddedStats != null && item.AddedStats[Stat.ItemReviveTime] > 0)
        {
            DateTime value = item.Info.ItemEffect == ItemEffect.PillOfReincarnation
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)ReincarnationPillUntilMs).LocalDateTime
                : DateTimeOffset.FromUnixTimeMilliseconds((long)ItemReviveUntilMs).LocalDateTime;
            sb.Append('\n');
            sb.Append(Library.Time.Now >= value
                ? "Revival ready"
                : $"Revival ready in {Functions.ToString(value - Library.Time.Now, true)}");
        }

        // ---- 套装 ----
        if (item.Info.Set != null)
            AppendSetInfo(sb, item, item.Info.Set);

        // ---- 结婚 / GM ----
        if ((item.Flags & UserItemFlags.Marriage) == UserItemFlags.Marriage)
            sb.Append('\n').Append("婚戒。");
        if ((item.Flags & UserItemFlags.GameMaster) == UserItemFlags.GameMaster)
            sb.Append('\n').Append("由管理员创建。");

        // ---- 碎片 / 重置 / 锁定 ----
        if (item.CanFragment())
        {
            sb.Append('\n').Append($"碎片费用: {item.FragmentCost():#,##0}");
            sb.Append('\n').Append($"碎片: {(item.Info.Rarity == Rarity.Common ? "Fragment" : "Fragment (II)")} x{item.FragmentCount():#,##0}");
        }

        if (Library.Time.Now < item.NextReset)
            sb.Append('\n').Append($"重置可用时间: {Functions.ToString(item.NextReset - Library.Time.Now, true)}");

        if ((item.Flags & UserItemFlags.Locked) == UserItemFlags.Locked)
            sb.Append('\n').Append("已锁定: 防止误售或误扔\n[鼠标中键] 或 [Scroll Lock] 解锁ck.");

        return sb.ToString();
    }

    private static string GemTypeName(int shape) => shape switch
    {
        0 => "Piercing",
        1 => "Weapon",
        2 => "Armour",
        3 => "Curse",
        4 => "Reset",
        _ => "Unknown",
    };

    private static string GemPurityText(ClientUserItem item)
    {
        decimal purity = item.CurrentDurability / 1000M;
        decimal max = Math.Max(0, Globals.MaxGemPurity);
        string level = purity <= max * 0.2M ? "Lowest"
            : purity <= max * 0.4M ? "Low"
            : purity <= max * 0.6M ? "Medium"
            : purity <= max * 0.8M ? "High"
            : "Supreme";
        return level;
    }

    private static bool IsCurrencyItem(ItemInfo info)
        => Globals.CurrencyInfoList?.Binding.FirstOrDefault(x => x.DropItem == info) != null;

    private void AppendEquipmentStats(System.Text.StringBuilder sb, ClientUserItem item, ItemInfo displayInfo)
    {
        Stats stats = new Stats();
        stats.Add(displayInfo.Stats, displayInfo.ItemType != ItemType.Weapon);
        stats.Add(item.AddedStats, item.Info.ItemType != ItemType.Weapon);

        if (displayInfo.ItemType == ItemType.Weapon)
        {
            Stat element = item.AddedStats.GetWeaponElement();
            if (element == Stat.None)
                element = displayInfo.Stats.GetWeaponElement();

            if (element != Stat.None)
                stats[element] += item.AddedStats.GetWeaponElementValue() + displayInfo.Stats.GetWeaponElementValue();
        }

        foreach (KeyValuePair<Stat, int> pair in stats.Values)
        {
            string text = stats.GetDisplay(pair.Key);
            if (text == null) continue;
            string added = item.AddedStats.GetFormat(pair.Key);

            switch (pair.Key)
            {
                case Stat.DropRate:
                case Stat.ExperienceRate:
                case Stat.SkillRate:
                case Stat.GoldRate:
                    if (added != null) text += $" ({added})";
                    break;
                default:
                    if (item.AddedStats[pair.Key] != 0)
                        text += $"   ({added})";
                    break;
            }

            sb.Append('\n').Append(text);
        }
    }

    private void AppendPotionStats(System.Text.StringBuilder sb, ClientUserItem item)
    {
        Stats stats = new Stats();
        stats.Add(item.Info.Stats);

        foreach (KeyValuePair<Stat, int> pair in stats.Values)
        {
            string text = stats.GetDisplay(pair.Key);
            if (text == null) continue;
            sb.Append('\n').Append(text);
        }

        if (item.Info.Durability > 0)
            sb.Append('\n').Append($"冷却: {Functions.ToString(TimeSpan.FromMilliseconds(item.Info.Durability), true)}");
    }

    private void AppendTrainingInfo(System.Text.StringBuilder sb, ClientUserItem item, ItemInfo displayInfo)
    {
        switch (displayInfo.ItemType)
        {
            case ItemType.Weapon:
                if ((item.Flags & UserItemFlags.NonRefinable) == UserItemFlags.NonRefinable) return;
                sb.Append('\n').Append($"{displayInfo.ItemType} Level: " + (item.Level < Globals.WeaponExperienceList.Count ? item.Level.ToString() : "Max"));
                if (item.Level >= Globals.WeaponExperienceList.Count) return;
                sb.Append('\n').Append((item.Flags & UserItemFlags.Refinable) == UserItemFlags.Refinable
                    ? "Ready for Refine"
                    : $"{displayInfo.ItemType} Training Points: {item.Experience / Globals.WeaponExperienceList[item.Level]:0.##%}");
                break;
            case ItemType.Necklace:
            case ItemType.Bracelet:
            case ItemType.Ring:
                if ((item.Flags & UserItemFlags.NonRefinable) == UserItemFlags.NonRefinable) return;
                sb.Append('\n').Append($"{displayInfo.ItemType} Level: " + (item.Level < Globals.AccessoryExperienceList.Count ? item.Level.ToString() : "Max"));
                if (item.Level >= Globals.AccessoryExperienceList.Count) return;
                sb.Append('\n').Append((item.Flags & UserItemFlags.Refinable) == UserItemFlags.Refinable
                    ? "Ready for Refine"
                    : $"{displayInfo.ItemType} Training Points: {item.Experience / Globals.AccessoryExperienceList[item.Level]:0.##%}");
                break;
        }
    }

    private void AppendRequirements(System.Text.StringBuilder sb, ClientUserItem item, ItemInfo displayInfo)
    {
        if (displayInfo.RequiredGender != RequiredGender.None)
            sb.Append('\n').Append($"所需性别: {displayInfo.RequiredGender}");

        if (displayInfo.RequiredClass != RequiredClass.All)
        {
            var clsMember = typeof(RequiredClass).GetMember(displayInfo.RequiredClass.ToString()).FirstOrDefault();
            var clsDesc = clsMember?.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description;
            sb.Append('\n').Append($"所需职业: {displayInfo.RequiredClass.Local()}");
        }

        if (displayInfo.RequiredAmount <= 0) return;

        string text;
        switch (displayInfo.RequiredType)
        {
            case RequiredType.Level: text = $"所需等级: {displayInfo.RequiredAmount}"; break;
            case RequiredType.MaxLevel: text = $"最大等级: {displayInfo.RequiredAmount}"; break;
            case RequiredType.AC: text = $"所需防御: {displayInfo.RequiredAmount}"; break;
            case RequiredType.MR: text = $"所需魔抗: {displayInfo.RequiredAmount}"; break;
            case RequiredType.DC: text = $"所需攻击: {displayInfo.RequiredAmount}"; break;
            case RequiredType.MC: text = $"所需魔法: {displayInfo.RequiredAmount}"; break;
            case RequiredType.SC: text = $"所需道术: {displayInfo.RequiredAmount}"; break;
            case RequiredType.Health: text = $"所需生命: {displayInfo.RequiredAmount}"; break;
            case RequiredType.Mana: text = $"所需魔法值: {displayInfo.RequiredAmount}"; break;
            case RequiredType.CompanionLevel: text = $"伙伴等级: {displayInfo.RequiredAmount}"; break;
            case RequiredType.MaxCompanionLevel: text = $"Max 伙伴等级: {displayInfo.RequiredAmount}"; break;
            case RequiredType.RebirthLevel: text = $"转生等级: {displayInfo.RequiredAmount}"; break;
            case RequiredType.MaxRebirthLevel: text = $"Max 转生等级: {displayInfo.RequiredAmount}"; break;
            default: text = "Unknown Type Required"; break;
        }
        sb.Append('\n').Append(text);
    }

    private void AppendSocketInfo(System.Text.StringBuilder sb, ClientUserItem item, ItemInfo displayInfo)
    {
        if (displayInfo.ItemType != ItemType.Weapon && displayInfo.ItemType != ItemType.Armour) return;

        foreach (ClientUserItemSocket socket in (item.Sockets ?? Enumerable.Empty<ClientUserItemSocket>()).OrderBy(x => x.Slot))
        {
            ClientUserItem gemItem = socket.Gem;
            if (gemItem?.Info == null)
            {
                sb.Append('\n').Append("Empty Socket");
                continue;
            }

            Stats gemStats = new Stats(gemItem.Info.Stats);
            gemStats.Add(gemItem.AddedStats);
            foreach (KeyValuePair<Stat, int> pair in gemStats.Values)
            {
                string text = gemStats.GetDisplay(pair.Key);
                if (text == null) continue;
                sb.Append('\n').Append(text);
            }
        }
    }

    private void AppendTradeState(System.Text.StringBuilder sb, ClientUserItem item, ItemInfo displayInfo)
    {
        long sale = item.Price(Math.Max(1, item.Count));
        bool any = false;

        if (sale > 0)
        {
            sb.Append('\n').Append($"售价: {sale:#,##0}");
            any = true;
        }

        if (item.Info.Durability > 0 && !item.Info.CanRepair && item.Info.StackSize == 1)
        {
            switch (item.Info.ItemType)
            {
                case ItemType.Weapon:
                case ItemType.Armour:
                case ItemType.Helmet:
                case ItemType.Necklace:
                case ItemType.Bracelet:
                case ItemType.Ring:
                case ItemType.Shoes:
                case ItemType.Shield:
                    sb.Append('\n').Append("Cannot be repaired.");
                    any = true;
                    break;
            }
        }

        if (!item.Info.CanSell || (item.Flags & UserItemFlags.Worthless) == UserItemFlags.Worthless)
        {
            sb.Append('\n').Append("Cannot be sold.");
            any = true;
        }
        if (!item.Info.CanStore)
        {
            sb.Append('\n').Append("Cannot be stored.");
            any = true;
        }
        if (!item.Info.CanTrade || (item.Flags & UserItemFlags.Bound) == UserItemFlags.Bound)
        {
            sb.Append('\n').Append("Cannot be traded.");
            any = true;
        }
        if (!item.Info.CanDrop)
        {
            sb.Append('\n').Append("Cannot be dropped.");
            any = true;
        }
        if (!item.Info.CanDeathDrop || (item.Flags & UserItemFlags.Worthless) == UserItemFlags.Worthless || (item.Flags & UserItemFlags.Bound) == UserItemFlags.Bound)
        {
            sb.Append('\n').Append("Cannot be dropped on death.");
            any = true;
        }
        if ((item.Flags & UserItemFlags.Bound) == UserItemFlags.Bound)
        {
            sb.Append('\n').Append("Bound Item.");
            any = true;
        }
        if ((item.Flags & UserItemFlags.NonRefinable) == UserItemFlags.NonRefinable)
        {
            sb.Append('\n').Append(item.Info.ItemType == ItemType.Book
                ? "Does not contain Level 4 Pages."
                : "Cannot be Refined or Upgraded.");
            any = true;
        }
        else if (item.Info.ItemType == ItemType.Book)
        {
            sb.Append('\n').Append("Contains high level Pages.");
            any = true;
        }
    }

    private static void AppendDescription(System.Text.StringBuilder sb, ItemInfo displayInfo)
    {
        if (string.IsNullOrEmpty(displayInfo.Description)) return;
        string desc = displayInfo.Description
            .Replace("\\r\\n", "\n")
            .Replace("\\n", "\n")
            .Replace("\\r", "\r");
        sb.Append('\n').Append(desc);
    }

    private void AppendSetInfo(System.Text.StringBuilder sb, ClientUserItem item, SetInfo set)
    {
        sb.Append('\n').Append("Item Set:");
        sb.Append('\n').Append($"    {set.SetName}");
        sb.Append('\n').Append("Parts:");

        bool hasFullSet = true;
        var counted = new List<int>();
        Stats setBonus = new Stats();
        int level = PlayerLevel;
        MirClass userClass = StartInfo?.Class ?? MirClass.Warrior;

        foreach (ItemInfo info in set.Items ?? Enumerable.Empty<ItemInfo>())
        {
            bool hasPart = false;
            for (int i = 0; i < Equipment.Length; i++)
            {
                if (counted.Contains(i)) continue;
                if (Equipment[i] == null || Equipment[i].Info != info) continue;
                if (Equipment[i].CurrentDurability == 0 && Equipment[i].Info.Durability > 0) continue;
                counted.Add(i);
                hasPart = true;
                break;
            }

            if (!hasPart)
                hasFullSet = false;

            sb.Append('\n').Append("    " + info.ItemName);
        }

        sb.Append('\n').Append("Set Bonus:");

        foreach (SetInfoStat stat in set.SetStats ?? Enumerable.Empty<SetInfoStat>())
        {
            if (level < stat.Level) continue;
            if (!ClassMatches(stat.Class, userClass)) continue;
            setBonus[stat.Stat] += stat.Amount;
        }

        foreach (KeyValuePair<Stat, int> pair in setBonus.Values)
        {
            string text = setBonus.GetDisplay(pair.Key);
            if (text == null) continue;
            sb.Append('\n').Append("    " + text);
        }
    }

    private static bool ClassMatches(RequiredClass required, MirClass cls)
    {
        return cls switch
        {
            MirClass.Warrior => (required & RequiredClass.Warrior) == RequiredClass.Warrior,
            MirClass.Wizard => (required & RequiredClass.Wizard) == RequiredClass.Wizard,
            MirClass.Taoist => (required & RequiredClass.Taoist) == RequiredClass.Taoist,
            MirClass.Assassin => (required & RequiredClass.Assassin) == RequiredClass.Assassin,
            _ => true,
        };
    }

    /// <summary>原版 DXItemCell 中键/快捷键 ItemLock 的目标状态（反相当前锁定）。</summary>
    public static bool ComputeItemLockTarget(bool currentlyLocked) => !currentlyLocked;

    // ---- 发包转发 (DXItemCell/对话框调用) ----
    public void SendItemMove(GridType fromGrid, GridType toGrid, int fromSlot, int toSlot, bool mergeItem)
    {
        if (IsObserver || fromSlot < 0 || toSlot < 0) return;
        _net.Connection.SendItemMove(fromGrid, toGrid, fromSlot, toSlot, mergeItem);
    }
    public void SendItemSplit(GridType grid, int slot, long count)
    {
        if (IsObserver || count <= 0) return;
        DXItemCell[] cells = grid switch
        {
            GridType.Inventory => InventoryCells,
            GridType.Storage => StorageCells,
            GridType.PartsStorage => PartsStorageCells,
            GridType.GuildStorage => GuildStorageItemCells,
            GridType.CompanionInventory => CompanionInventoryCells,
            _ => null,
        };
        if (cells == null || slot < 0 || slot >= cells.Length || cells[slot] == null || cells[slot].Item == null)
            return;
        if (cells[slot].Locked) return;
        cells[slot].Locked = true;
        cells[slot].UpdateBorder();
        _net.Connection.SendItemSplit(grid, slot, count);
    }
    public void OpenItemSplitDialog(ClientUserItem item, GridType grid, int slot)
    {
        var dialog = new ItemAmountDialog(item, count => SendItemSplit(grid, slot, count));
        WindowManager.Open(dialog, _uiLayer);
    }

    public void SendItemUse(GridType grid, int slot)
    {
        if (IsObserver || slot < 0) return;
        var item = ItemAt(grid, slot);
        if (item == null || item.Index == 0) return;
        var key = (grid, slot);
        if (_pendingItemUses.ContainsKey(key)) return;
        _pendingItemUses[key] = item.Index;
        _net.Connection.SendItemUse(grid, slot);
    }

    public void SendItemLock(GridType grid, int slot, bool locked)
    {
        if (IsObserver || slot < 0) return;
        _net.Connection.SendItemLock(grid, slot, locked);
    }

    public void SendItemSort(GridType grid)
    {
        if (IsObserver) return;
        _net.Connection.SendItemSort(grid);
    }

    public void SendItemDelete(GridType grid, int slot)
    {
        if (IsObserver || slot < 0) return;
        var item = ItemAt(grid, slot);
        if (item == null || item.Index == 0) return;
        var key = (grid, slot);
        if (_pendingItemDeletes.ContainsKey(key)) return;
        _pendingItemDeletes[key] = item.Index;
        _net.Connection.SendItemDelete(grid, slot);
    }
    public void SendItemDrop(CellLinkInfo link)
    {
        if (IsObserver || link == null || link.Slot < 0) return;
        _net.Connection.SendItemDrop(link);
    }
    public void SendCurrencyDrop(int currencyIndex, long amount)
    {
        var currency = Currencies.FirstOrDefault(x => x?.CurrencyIndex == currencyIndex);
        if (currency == null || !CanDropCurrency(IsObserver, currency.Amount, amount)) return;
        _net?.Connection?.SendCurrencyDrop(currencyIndex, amount);
    }
    public void SendMarriageTeleport()
    {
        if (IsObserver) return;
        _net?.Connection?.Enqueue(new C.MarriageTeleport());
    }
    public void LinkItemToChat(ClientUserItem item) => _chatTextBox?.LinkItem(item);

    public void SendAutoPathWaypoint(int mapIndex, int x, int y)
    {
        _net?.Connection?.SendAutoPathWaypoint(mapIndex, new System.Drawing.Point(x, y));
        _net?.Connection?.SendAutoPathMoveStarted();
    }

    /// <summary>旧版大图双击 NPC 图标: 自动寻路到该 NPC。</summary>
    public void SendAutoPathStart(int npcIndex)
    {
        _net?.Connection?.SendAutoPathStart(npcIndex);
        _net?.Connection?.SendAutoPathMoveStarted();
    }
    public void CancelAutoPath()
    {
        _autoPathCancelPending = true;
        _autoPathRoutes.Clear();
        _pendingAutoPathMove = null;
        UpdateAutoPathProgress();
        _net?.Connection?.SendAutoPathCancel();
    }

    public void SendBeltLinkChanged(int slot, int linkInfoIndex, int linkItemIndex)
    {
        if (IsObserver || slot < 0) return;
        _net.Connection.SendBeltLinkChanged(slot, linkInfoIndex, linkItemIndex);
    }

    public void SendAutoPotionLinkChanged(int slot, ClientAutoPotionLink link)
    {
        if (IsObserver || _net?.Connection == null || link == null || slot < 0) return;
        _net.Connection.Enqueue(new C.AutoPotionLinkChanged
        {
            Slot = slot,
            LinkIndex = link.LinkInfoIndex,
            Health = link.Health,
            Mana = link.Mana,
            Enabled = link.Enabled,
        });
    }

    public void SendBundleOpen(int slot) { if (!IsObserver && slot >= 0) _net?.Connection?.Enqueue(new C.BundleOpen { Slot = slot }); }
    public void SendBundleConfirm(int slot, int choice) { if (!IsObserver && slot >= 0) _net?.Connection?.Enqueue(new C.BundleConfirm { Slot = slot, Choice = choice }); }
    public void SendFortuneCheck(int itemIndex) { if (!IsObserver && itemIndex > 0) _net?.Connection?.Enqueue(new C.FortuneCheck { ItemIndex = itemIndex }); }
    public void SendLootBoxOpen(int slot) { if (!IsObserver && slot >= 0) _net?.Connection?.Enqueue(new C.LootBoxOpen { Slot = slot }); }
    public void SendLootBoxReroll(int slot) { if (!IsObserver && slot >= 0) _net?.Connection?.Enqueue(new C.LootBoxReroll { Slot = slot }); }
    public void SendLootBoxConfirm(int slot) { if (!IsObserver && slot >= 0) _net?.Connection?.Enqueue(new C.LootBoxConfirmSelection { Slot = slot }); }
    public void SendLootBoxReveal(int slot, int choice) { if (!IsObserver && slot >= 0) _net?.Connection?.Enqueue(new C.LootBoxReveal { Slot = slot, Choice = choice }); }
    public void SendLootBoxTake(int slot, int choice) { if (!IsObserver && slot >= 0) _net?.Connection?.Enqueue(new C.LootBoxTakeItems { Slot = slot, Choice = choice }); }
    public void SendCaptionChange(string caption)
    {
        if (IsObserver || string.IsNullOrWhiteSpace(caption)) return;
        _net?.Connection?.Enqueue(new C.CaptionChange { Caption = caption.Trim() });
    }

    public void SendMarketSearch(string name, MarketPlaceSort sort, bool itemTypeFilter = false, ItemType itemType = ItemType.Nothing)
        => _net?.Connection?.SendMarketSearch(name, sort, itemTypeFilter, itemType);
    public void SendMarketSearchIndex(int index) => _net?.Connection?.SendMarketSearchIndex(index);
    public void SendMarketBuy(long index, long count, bool guildFunds = false)
    {
        if (IsObserver || index < 0 || count <= 0) return;
        _net?.Connection?.SendMarketBuy(index, count, guildFunds);
    }
    public void SendMarketCancel(int index, long count)
    {
        if (IsObserver || index < 0 || count <= 0) return;
        _net?.Connection?.SendMarketCancel(index, count);
    }
    public void SendMarketConsign(GridType grid, int slot, long count, int price, bool guildFunds = false)
    {
        if (IsObserver || slot < 0 || count <= 0 || price <= 0) return;
        _net?.Connection?.SendMarketConsign(grid, slot, count, price, guildFunds);
    }
    public void SendMarketHistory(int index, int partIndex, int display) => _net?.Connection?.SendMarketHistory(index, partIndex, display);
    public void SendFishingCast(FishingState state) => _net?.Connection?.SendFishingCast(state, _playerDirection, new System.Drawing.Point(_playerLocation.X, _playerLocation.Y));
    public void SendFishingCast(FishingState state, bool caughtFish)
        => _net?.Connection?.SendFishingCast(state, _playerDirection, new System.Drawing.Point(_playerLocation.X, _playerLocation.Y), caughtFish);
    public void SendTaming(uint objectID) => _net?.Connection?.SendTaming(objectID, TamingState.Cast, _playerDirection);
    public void CancelTaming() => _net?.Connection?.SendTaming(_tamingTargetObjectID, TamingState.Cancel, _playerDirection);
    public void SendTamingSuccess(uint objectID) => _net?.Connection?.SendTamingSuccess(objectID);
    public void SendMilestoneClaim(int index) => _net?.Connection?.SendMilestoneClaim(index);
    public void ClaimMilestone(int index) => SendMilestoneClaim(index);
    public void SendMilestoneNotify(bool receive) => _net?.Connection?.SendMilestoneNotify(receive);
    public void SendMilestoneActive(int index, bool active) => _net?.Connection?.SendMilestoneActive(index, active);
    public void SendSelectLanguage(string language) => _net?.Connection?.SendSelectLanguage(language);
    public void SendGenderChange(MirGender gender, int hairType)
        => SendGenderChange(gender, hairType, StartInfo?.HairColour ?? System.Drawing.Color.Black);
    public void SendGenderChange(MirGender gender, int hairType, System.Drawing.Color hairColour)
    {
        if (IsObserver) return;
        _net?.Connection?.SendGenderChange(gender, hairType, hairColour);
    }
    public void SendHairChange(int hairType)
        => SendHairChange(hairType, StartInfo?.HairColour ?? System.Drawing.Color.Black);
    public void SendHairChange(int hairType, System.Drawing.Color hairColour)
    {
        if (IsObserver) return;
        _net?.Connection?.SendHairChange(hairType, hairColour);
    }
    public void SendArmourDye()
        => SendArmourDye(StartInfo?.ArmourColour ?? System.Drawing.Color.White);
    public void SendArmourDye(System.Drawing.Color colour)
    {
        if (IsObserver) return;
        _net?.Connection?.SendArmourDye(colour);
    }
    public void SendNameChange(string name)
    {
        if (IsObserver || string.IsNullOrWhiteSpace(name)) return;
        _net?.Connection?.SendNameChange(name.Trim());
    }

    public void SendJoinInstance(int index) => _net?.Connection?.SendJoinInstance(index);

    private void OnJoinInstance(S.JoinInstance p)
    {
        if (p == null) return;
        _statusLabel.Text = p.Success ? Lang.GameUi586Label : string.Format(Lang.GameUi587Label, p.Result);
    }

    private void ShowServerActionResult(string action, object packet)
    {
        if (packet != null) _statusLabel.Text = $"{action}：{packet}";
    }

    private void OnUserMilestones(S.UserMilestones packet)
    {
        _milestones.Clear();
        foreach (var milestone in packet?.Milestones ?? new List<ClientUserMilestone>())
            if (milestone != null) _milestones[milestone.Index] = milestone;
        // 里程碑数据变化后刷新任务窗口页签提醒。
        _questDialog?.RefreshAlerts();
    }

    private void OnMilestoneEarned(S.MilestoneEarned packet)
    {
        if (packet != null && _milestones.TryGetValue(packet.Index, out var milestone))
            _milestoneDialog?.ShowMilestone(milestone);
        // 新里程碑完成后页签提醒立即出现（旧版 UpdateAlertIcons 同路径）。
        _questDialog?.RefreshAlerts();
    }

    private void OnObjectTaming(S.ObjectTaming p)
    {
        if (p == null || p.ObjectID != _playerObjectID) return;
        _player?.PlayTaming(p.State, p.TamingObjectID);
        if (_player != null) _player.Direction = p.Direction;
        _horseTameDialog?.SetState(p.State);
        if (p.State == TamingState.Cast && _objects.TryGetValue(p.TamingObjectID, out var target))
        {
            _tamingTargetObjectID = p.TamingObjectID;
            _horseTameDialog?.SetTarget(p.TamingObjectID, target.Position / UiScale);
        }
        else if (p.State == TamingState.Cancel || p.State == TamingState.None)
        {
            _tamingTargetObjectID = 0;
            if (_tamingRopes.Remove(p.TamingObjectID, out var rope)) rope.QueueFree();
        }
    }

    private void OnObjectFishing(S.ObjectFishing p)
    {
        if (p == null || p.ObjectID != _playerObjectID) return;
        _player?.PlayFishing(p.State, p.FishFound, p.FloatLocation);
        if (_player != null) _player.Direction = p.Direction;
        _fishingCatchDialog?.SetState(p.State, p.FishFound);
        if (p.State == FishingState.None || p.State == FishingState.Cancel)
            WindowManager.Close(_fishingDialog);
    }
    public static bool CanSendNPCOperation(bool observer) => !observer;
    public void SendNPCSocketItem(CellLinkInfo target, CellLinkInfo gem)
    {
        if (!CanSendNPCOperation(IsObserver)) return;
        _net?.Connection?.Enqueue(new C.NPCSocketItem { Target = target, Gem = gem });
    }
    public void SendNPCSocketCombine(CellLinkInfo gem1, CellLinkInfo gem2, CellLinkInfo gem3)
    {
        if (!CanSendNPCOperation(IsObserver)) return;
        _net?.Connection?.Enqueue(new C.NPCSocketCombine { Gem1 = gem1, Gem2 = gem2, Gem3 = gem3 });
    }

    public void SendNPCFragment(List<CellLinkInfo> links)
    {
        if (!CanSendNPCOperation(IsObserver)) return;
        _net?.Connection?.Enqueue(new C.NPCFragment { Links = links ?? new List<CellLinkInfo>() });
    }

    public void SendNPCRefinementStone(List<CellLinkInfo> iron, List<CellLinkInfo> silver,
        List<CellLinkInfo> diamond, List<CellLinkInfo> gold, List<CellLinkInfo> crystal, long goldAmount = 0)
    {
        if (!CanSendNPCOperation(IsObserver)) return;
        _net?.Connection?.Enqueue(new C.NPCRefinementStone
        {
            IronOres = iron ?? new List<CellLinkInfo>(), SilverOres = silver ?? new List<CellLinkInfo>(),
            DiamondOres = diamond ?? new List<CellLinkInfo>(), GoldOres = gold ?? new List<CellLinkInfo>(),
            Crystal = crystal ?? new List<CellLinkInfo>(), Gold = goldAmount,
        });
    }

    public void SendNPCRefine(RefineType type, RefineQuality quality, List<CellLinkInfo> ores,
        List<CellLinkInfo> items, List<CellLinkInfo> specials)
    {
        if (!CanSendNPCOperation(IsObserver)) return;
        _net?.Connection?.Enqueue(new C.NPCRefine
        {
            RefineType = type, RefineQuality = quality,
            Ores = ores ?? new List<CellLinkInfo>(), Items = items ?? new List<CellLinkInfo>(),
            Specials = specials ?? new List<CellLinkInfo>(),
        });
    }

    public void SendNPCMasterRefine(List<CellLinkInfo> fragment1, List<CellLinkInfo> fragment2,
        List<CellLinkInfo> fragment3, List<CellLinkInfo> stones, List<CellLinkInfo> specials)
    {
        if (!CanSendNPCOperation(IsObserver)) return;
        _net?.Connection?.Enqueue(new C.NPCMasterRefine
        {
            RefineType = RefineType.None,
            Fragment1s = fragment1 ?? new List<CellLinkInfo>(), Fragment2s = fragment2 ?? new List<CellLinkInfo>(),
            Fragment3s = fragment3 ?? new List<CellLinkInfo>(), Stones = stones ?? new List<CellLinkInfo>(),
            Specials = specials ?? new List<CellLinkInfo>(),
        });
    }
    public void SendNPCMasterRefineEvaluate(RefineType type, List<CellLinkInfo> fragment1, List<CellLinkInfo> fragment2, List<CellLinkInfo> fragment3, List<CellLinkInfo> stones, List<CellLinkInfo> specials)
    {
        if (!CanSendNPCOperation(IsObserver)) return;
        _net?.Connection?.Enqueue(new C.NPCMasterRefineEvaluate { RefineType = type, Fragment1s = fragment1 ?? new(), Fragment2s = fragment2 ?? new(), Fragment3s = fragment3 ?? new(), Stones = stones ?? new(), Specials = specials ?? new() });
    }

    public void RequestNPCRefineList() => _npcDialog?.RefreshRefineList();
    public void SendNPCRefineRetrieve(int index)
    {
        if (!CanSendNPCOperation(IsObserver)) return;
        _net?.Connection?.Enqueue(new C.NPCRefineRetrieve { Index = index });
    }
    public void SendNPCAccessoryUpgrade(CellLinkInfo target, RefineType type)
    {
        if (!CanSendNPCOperation(IsObserver)) return;
        _net?.Connection?.Enqueue(new C.NPCAccessoryUpgrade { Target = target, RefineType = type });
    }
    public void SendNPCAccessoryLevelUp(CellLinkInfo target, List<CellLinkInfo> links)
    {
        if (!CanSendNPCOperation(IsObserver)) return;
        _net?.Connection?.Enqueue(new C.NPCAccessoryLevelUp { Target = target, Links = links ?? new List<CellLinkInfo>() });
    }
    public void SendNPCAccessoryReset(CellLinkInfo target)
    {
        if (!CanSendNPCOperation(IsObserver)) return;
        _net?.Connection?.Enqueue(new C.NPCAccessoryReset { Cell = target });
    }
    public void SendNPCAccessoryRefine(CellLinkInfo target, CellLinkInfo oreTarget, List<CellLinkInfo> links, RefineType refineType = RefineType.None)
    {
        if (!CanSendNPCOperation(IsObserver)) return;
        _net?.Connection?.Enqueue(new C.NPCAccessoryRefine
        {
            Target = target, OreTarget = oreTarget, Links = links ?? new List<CellLinkInfo>(), RefineType = refineType,
        });
    }
    public void SendNPCWeaponCraft(RequiredClass @class, CellLinkInfo template, CellLinkInfo yellow,
        CellLinkInfo blue, CellLinkInfo red, CellLinkInfo purple, CellLinkInfo green, CellLinkInfo grey)
    {
        if (!CanSendNPCOperation(IsObserver)) return;
        _net?.Connection?.Enqueue(new C.NPCWeaponCraft
        {
            Class = @class, Template = template, Yellow = yellow, Blue = blue, Red = red,
            Purple = purple, Green = green, Grey = grey,
        });
    }
    public void SendNPCRoll(int type)
    {
        if (!CanSendNPCOperation(IsObserver) || type is < 0 or > 1) return;
        _net?.Connection?.Enqueue(new C.NPCRoll { Type = type });
    }
    public void SendNPCRollResult()
    {
        if (!CanSendNPCOperation(IsObserver)) return;
        _net?.Connection?.SendNPCRollResult();
    }
    public void SendCompanionFilters(List<MirClass> classes = null, List<Rarity> rarities = null, List<ItemType> itemTypes = null)
        => _net?.Connection?.Enqueue(new C.SendCompanionFilters
        {
            FilterClass = classes ?? new List<MirClass>(), FilterRarity = rarities ?? new List<Rarity>(), FilterItemType = itemTypes ?? new List<ItemType>(),
        });
    public static bool CanSendCompanionOperation(bool observer, int index)
        => !observer && index >= 0;
    public void SendCompanionStore(int index)
    {
        if (!CanSendCompanionOperation(IsObserver, index)) return;
        _net?.Connection?.SendCompanionStore(index);
    }
    public void SendCompanionStore() => SendCompanionStore(Companion?.Index ?? -1);
    public void SendCompanionRetrieve(int index)
    {
        if (CanSendCompanionOperation(IsObserver, index)) _net?.Connection?.SendCompanionRetrieve(index);
    }
    public void SendCompanionRelease(int index)
    {
        if (CanSendCompanionOperation(IsObserver, index)) _net?.Connection?.SendCompanionRelease(index);
    }
    public void SendCompanionUnlock(int index)
    {
        if (!CanSendCompanionOperation(IsObserver, index)) return;
        _net?.Connection?.SendCompanionUnlock(index);
    }
    public void SendCompanionAdopt(int index, string name)
    {
        if (!CanSendCompanionOperation(IsObserver, index) || string.IsNullOrWhiteSpace(name)) return;
        _net?.Connection?.SendCompanionAdopt(index, name.Trim());
    }
    public void SendGuildEditMember(int index, string rank, GuildPermission permission)
        => _net?.Connection?.Enqueue(new C.GuildEditMember { Index = index, Rank = rank ?? string.Empty, Permission = permission });
    public void SendGuildKickMember(int index)
        => _net?.Connection?.Enqueue(new C.GuildKickMember { Index = index });
    public void SendGuildInviteMember(string name) => _net?.Connection?.SendGuildInviteMember(name);
    public void SendGuildEditNotice(string notice) => _net?.Connection?.SendGuildEditNotice(notice);
    public void SendGuildIncreaseMember() => _net?.Connection?.SendGuildIncreaseMember();
    public void SendGuildIncreaseStorage() => _net?.Connection?.SendGuildIncreaseStorage();
    public void SendGuildCreate(string name, bool useGold, int members, int storage) => _net?.Connection?.SendGuildCreate(name, useGold, members, storage);
    public void SendJoinStarterGuild() => _net?.Connection?.SendJoinStarterGuild();
    public void SendGuildColour(Color colour) => _net?.Connection?.SendGuildColour(System.Drawing.Color.FromArgb((int)(colour.A * 255), (int)(colour.R * 255), (int)(colour.G * 255), (int)(colour.B * 255)));
    public void SendGuildFlag(int flag) => _net?.Connection?.SendGuildFlag(flag);
    public void SendRankSearch(string name) => _net?.Connection?.SendRankSearch(name);
    public void SendOnlineState(OnlineState state) => _net?.Connection?.SendOnlineState(state);
    public void SendHelmetToggle(bool hide) => _net?.Connection?.SendHelmetToggle(hide);
    private OnlineState _onlineState = OnlineState.Online;
    public void CycleOnlineState()
    {
        _onlineState = _onlineState switch { OnlineState.Online => OnlineState.Busy, OnlineState.Busy => OnlineState.Away, _ => OnlineState.Online };
        SendOnlineState(_onlineState);
        // 旧版 UpdateStateLabel：切换后立刻刷新好友面板状态按钮。
        _communicationDialog?.RefreshOwnState(_onlineState);
    }
    public void SendGuildWar(string guildName) => _net?.Connection?.SendGuildWar(guildName);
    public void SendGuildRequestConquest(int index) => _net?.Connection?.SendGuildRequestConquest(index);
    public void SendGuildResponse(string guildName, bool accept) => _net?.Connection?.SendGuildResponse(guildName, accept);
    public void SendGuildToggleCastleGates() => _net?.Connection?.SendGuildToggleCastleGates();
    public void SendGuildRepairCastleGates() => _net?.Connection?.SendGuildRepairCastleGates();
    public void SendGuildRepairCastleGuards() => _net?.Connection?.SendGuildRepairCastleGuards();
    public ClientFortuneInfo GetFortune(int itemIndex) => _fortunes.TryGetValue(itemIndex, out var value) ? value : null;

    private void OnFortuneUpdate(S.FortuneUpdate packet)
    {
        foreach (var fortune in packet?.Fortunes ?? new List<ClientFortuneInfo>())
        {
            if (fortune?.ItemInfo == null) fortune?.OnComplete();
            if (fortune?.ItemInfo != null) _fortunes[fortune.ItemInfo.Index] = fortune;
        }
        _fortuneDialog?.Search();
    }

    /// <summary>原版 MapControl 的拾取节流同时适用于鼠标点击和 Tab。</summary>
    public void SendPickUp()
    {
        double now = Godot.Time.GetTicksMsec();
        if (!CanSendPickUp(now, _pickUpNextMs)) return;
        _pickUpNextMs = now + 250.0;
        _net?.Connection?.SendPickUp();
    }

    public void SendGroupSwitch(bool allow)
        => _net.Connection.Enqueue(new C.GroupSwitch { Allow = allow });
    public void SendGroupInvite(string name)
        => _net?.Connection?.Enqueue(new C.GroupInvite { Name = name });

    /// <summary>旧版 GuildDialog 成员行右键: 在大地图上定位成员 (仅同地图在线成员有本地数据;
    /// 旧版靠 DataDictionary 全服位置缓存, Godot 仅维护当前地图对象)。</summary>
    public void ShowGuildMemberOnMap(ClientGuildMemberInfo member)
    {
        if (_bigMap == null || member == null) return;
        if (_objects.TryGetValue(member.ObjectID, out var ob) && ob.Type != ObjectRenderer.Kind.Item)
        {
            OpenBigMap();
            _bigMap.SetPlayerLocation(ob.CellX, ob.CellY);
        }
    }
    public void SendGroupRemove(uint objectId)
    {
        if (_objects.TryGetValue(objectId, out var member))
            _net.Connection.Enqueue(new C.GroupRemove { Name = member.DisplayName });
    }
    public void SendGroupRequest(string name)
        => _net.Connection.Enqueue(new C.GroupRequest { Name = name });
    public void SendGroupResponse(string name, bool accept)
        => _net?.Connection?.SendGroupResponse(name, accept);
    public void SendMailOpened(int index)
        => _net.Connection.Enqueue(new C.MailOpened { Index = index });
    public void SendMailGetItem(int index, int slot)
    {
        if (IsObserver || index < 0 || slot < 0) return;
        _net.Connection.SendMailGetItem(index, slot);
    }
    public void SendMailDelete(int index)
    {
        if (IsObserver || index < 0) return;
        _net.Connection.SendMailDelete(index);
    }
    public void SendMail(string recipient, string subject, string message, List<CellLinkInfo> links = null, long gold = 0)
    {
        if (IsObserver || string.IsNullOrWhiteSpace(recipient) || gold < 0) return;
        _net.Connection.Enqueue(new C.MailSend
        {
            Recipient = recipient.Trim(),
            Subject = subject ?? string.Empty,
            Message = message ?? string.Empty,
            Gold = gold,
            Links = links ?? new List<CellLinkInfo>(),
        });
    }
    public void SendTradeClose() => _net.Connection.Enqueue(new C.TradeClose());
    public void SendTradeRequestResponse(bool accept)
    {
        if (IsObserver) return;
        _net.Connection.SendTradeRequestResponse(accept);
    }
    public void SendTradeConfirm()
    {
        if (IsObserver) return;
        _net.Connection.Enqueue(new C.TradeConfirm());
    }
    public static bool CanSendTradeGold(bool observer, long balance, long amount)
        => !observer && balance > 0 && amount > 0 && amount <= balance;
    public void SendTradeGold(long gold)
    {
        long balance = Currencies.FirstOrDefault(x => x?.Info?.Type == CurrencyType.Gold)?.Amount ?? 0;
        if (!CanSendTradeGold(IsObserver, balance, gold)) return;
        _net.Connection.Enqueue(new C.TradeAddGold { Gold = gold });
    }
    public void SendTradeItem(CellLinkInfo cell)
    {
        if (IsObserver || cell == null || cell.Slot < 0 || cell.Count <= 0) return;
        _net.Connection.SendTradeItem(cell);
    }
    public void SendNPCButton(int buttonId) => _net.Connection.Enqueue(new C.NPCButton { ButtonID = buttonId });
    public void SendNPCClose() => _net.Connection.Enqueue(new C.NPCClose());
    public void SendNPCBuy(int index, long amount, bool guildFunds = false)
    {
        if (!CanSendNPCOperation(IsObserver) || index < 0 || amount <= 0) return;
        _net.Connection.Enqueue(new C.NPCBuy { Index = index, Amount = amount, GuildFunds = guildFunds });
    }
    public void SendNPCSell(List<CellLinkInfo> links)
    {
        if (!CanSendNPCOperation(IsObserver) || links == null || links.Count == 0) return;
        _net.Connection.Enqueue(new C.NPCSell { Links = links });
    }
    public void SendNPCRepair(List<CellLinkInfo> links, bool special = false, bool guildFunds = false)
    {
        if (!CanSendNPCOperation(IsObserver) || links == null || links.Count == 0) return;
        _net.Connection.Enqueue(new C.NPCRepair { Links = links, Special = special, GuildFunds = guildFunds });
    }

    private void OnNPCResponse(S.NPCResponse response)
    {
        if (response == null) return;
        if (AutoLoginArgs.InteractionAudit)
            GD.Print($"[InteractionAudit] NPC_RESPONSE object={response.ObjectID} page={response.Page?.DialogType}");
        _npcObjectId = response.ObjectID;
        _npcDialog?.ShowPage(response);
    }
    public void SendMagicKey(MagicType magic, Library.SpellKey s1, Library.SpellKey s2, Library.SpellKey s3, Library.SpellKey s4)
        => _net.Connection.SendMagicKey(magic, s1, s2, s3, s4);

    /// <summary>刷新魔法快捷栏和技能列表 (绑键后调用)。</summary>
    public void RefreshMagicBars()
    {
        _magicBar?.Refresh();
        _magicDialog?.Refresh();
    }

    public void SetMagicBarFrames(bool value)
    {
        if (ShowMagicBarFrames == value) return;
        ShowMagicBarFrames = value;
        ClientSettings.ShowMagicBarFrames = value;
        ClientSettings.Save();
        RefreshMagicBars();
    }

    public void SetRightClickDeTarget(bool value)
    {
        _rightClickDeTarget = value;
        ClientSettings.RightClickDeTarget = value;
        ClientSettings.Save();
    }

    public void SetEscapeCloseAll(bool value)
    {
        _escapeCloseAll = value;
        ClientSettings.EscapeCloseAll = value;
        ClientSettings.Save();
    }
    // ---- Tab 拾取 (250ms 节流) ----
    private void PickUpItems()
    {
        SendPickUp();
    }

    // ---- 使用冷却 ----
    public bool IsUseItemOnCooldown(ClientUserItem item) => Godot.Time.GetTicksMsec() < UseItemTime;

    public void SetUseItemCooldown(double ms)
    {
        double until = Godot.Time.GetTicksMsec() + ms;
        if (until > UseItemTime) UseItemTime = until;
        if (AutoLoginArgs.OperationAuditExt && _operationAuditExtStage != 0)
            GD.Print($"[OperationAuditExt] COOLDOWN-SET ms={ms} until={until} prev={UseItemTime - until}");
    }

    // ---- 数据填充 ----
    public void FillItems(List<ClientUserItem> items)
    {
        if (items == null) return;
        foreach (var item in items)
        {
            if (item.Slot >= Globals.EquipmentOffSet)
            {
                int slot = item.Slot - Globals.EquipmentOffSet;
                if (slot >= 0 && slot < Equipment.Length) Equipment[slot] = item;
            }
            else if (item.Slot >= 0 && item.Slot < Inventory.Length)
            {
                Inventory[item.Slot] = item;
            }
        }
        RefreshItemGrids();
    }

    public void FillStorage(List<ClientUserItem> items)
    {
        if (items == null) return;
        Array.Clear(Storage, 0, Storage.Length);
        Array.Clear(PartsStorage, 0, PartsStorage.Length);
        foreach (var item in items)
        {
            if (item.Slot >= Globals.PartsStorageOffset)
            {
                int slot = item.Slot - Globals.PartsStorageOffset;
                if (slot < PartsStorage.Length) PartsStorage[slot] = item;
            }
            else if (item.Slot >= 0 && item.Slot < Storage.Length)
            {
                Storage[item.Slot] = item;
            }
        }
        _storageDialog?.RefreshStorage();
    }

    public void ApplyBeltLinks(List<ClientBeltLink> links)
    {
        for (int i = 0; i < BeltLinks.Length; i++)
        {
            BeltLinks[i].Slot = i;
            BeltLinks[i].LinkInfoIndex = -1;
            BeltLinks[i].LinkItemIndex = -1;
        }
        if (links != null)
        {
            foreach (var link in links)
            {
                if (link.Slot < 0 || link.Slot >= BeltLinks.Length) continue;
                BeltLinks[link.Slot].LinkInfoIndex = link.LinkInfoIndex;
                BeltLinks[link.Slot].LinkItemIndex = link.LinkItemIndex;
            }
        }
        _beltDialog?.UpdateLinks();
    }

    // 物品离身/变更: 遍历腰带清失效链接 (原版 ItemChanged 的 ShouldLinkInfo 分支)
    private void ClearBeltLinkItem(int itemIndex, ClientUserItem replaceItem)
    {
        for (int i = 0; i < BeltLinks.Length; i++)
        {
            var link = BeltLinks[i];
            if (link.LinkItemIndex != itemIndex) continue;

            var cell = _beltDialog?.Grid?.Cells != null && i < _beltDialog.Grid.Cells.Length ? _beltDialog.Grid.Cells[i] : null;
            if (cell != null) cell.QuickItem = null; // setter 同步 link.LinkItemIndex = -1

            if (replaceItem != null && !replaceItem.Info.ShouldLinkInfo)
            {
                link.LinkItemIndex = replaceItem.Index;
                if (cell != null) cell.QuickItem = replaceItem;
            }
            if (!IsObserver)
                SendBeltLinkChanged(link.Slot, link.LinkInfoIndex, link.LinkItemIndex);
        }
    }

    // ---- 16 个 S 包处理器 ----

    // 拾取获得/系统发放
    private void OnItemsGained(S.ItemsGained p)
    {
        var items = p?.Items ?? new List<ClientUserItem>();
        MarkGainedItems(items, false);
        AddItems(items);
    }

    private void MarkGainedItems(IEnumerable<ClientUserItem> items, bool companion)
    {
        foreach (var item in items ?? Enumerable.Empty<ClientUserItem>())
        {
            if (!MarkGainedItemForAudit(item)) continue;
            // 原版获得提示对部件显示其 AddedStats.ItemIndex 对应的真实物品名，
            // 而不是显示通用的“物品部件”壳信息。
            var displayInfo = GainedDisplayInfo(item);
            var name = displayInfo?.Local() ?? item.Info.Local() ?? string.Empty;
            var suffix = item.Count > 1 ? $" x{item.Count}" : string.Empty;
            if (item.Flags.HasFlag(UserItemFlags.QuestItem)) suffix += Lang.GameQuestLabel;
            if (item.Info.ItemEffect == ItemEffect.ItemPart) suffix += Lang.GameUi589Label;
            ReceiveChat($"{(companion ? Lang.GameUi591Label : Lang.GameUi591Label)}: {name}{suffix}", MessageType.Combat);
        }
    }

    public static ItemInfo GainedDisplayInfo(ClientUserItem item)
    {
        if (item?.Info == null) return null;
        if (item.Info.ItemEffect != ItemEffect.ItemPart || item.AddedStats == null) return item.Info;
        int partIndex = item.AddedStats[Stat.ItemIndex];
        return partIndex > 0
            ? Globals.ItemInfoList?.Binding.FirstOrDefault(x => x.Index == partIndex) ?? item.Info
            : item.Info;
    }

    public static bool MarkGainedItemForAudit(ClientUserItem item)
    {
        if (item?.Info == null || item.Info.ItemEffect == ItemEffect.Experience) return false;
        item.New = true;
        return true;
    }

    public void AddItems(List<ClientUserItem> items)
    {
        foreach (var item in items)
        {
            if (item.Info?.ItemEffect == ItemEffect.Experience) continue;
            if ((item.Flags & UserItemFlags.QuestItem) == UserItemFlags.QuestItem) continue;

            var currency = Currencies.FirstOrDefault(x => x.Info?.DropItem == item.Info);
            if (currency != null)
            {
                currency.Amount += item.Count;
                RefreshCurrency();
                continue;
            }

            bool handled = false;
            if (item.Info.StackSize > 1 && (item.Flags & UserItemFlags.Expirable) != UserItemFlags.Expirable)
            {
                foreach (var cellItem in Inventory)
                {
                    if (cellItem == null || cellItem.Info != item.Info) continue;
                    if (cellItem.Count >= cellItem.Info.StackSize) continue;
                    if ((cellItem.Flags & UserItemFlags.Expirable) == UserItemFlags.Expirable) continue;
                    if ((cellItem.Flags & UserItemFlags.Bound) != (item.Flags & UserItemFlags.Bound)) continue;
                    if ((cellItem.Flags & UserItemFlags.Worthless) != (item.Flags & UserItemFlags.Worthless)) continue;
                    if ((cellItem.Flags & UserItemFlags.NonRefinable) != (item.Flags & UserItemFlags.NonRefinable)) continue;
                    if (cellItem.ExpireTime != item.ExpireTime) continue;
                    if (!cellItem.AddedStats.Compare(item.AddedStats)) continue;

                    if (cellItem.Count + item.Count <= item.Info.StackSize)
                    {
                        cellItem.Count += item.Count;
                        handled = true;
                        break;
                    }
                    item.Count -= item.Info.StackSize - cellItem.Count;
                    cellItem.Count = item.Info.StackSize;
                }
                if (handled) continue;
            }

            for (int i = 0; i < Inventory.Length; i++)
            {
                if (Inventory[i] != null) continue;
                Inventory[i] = item;
                item.Slot = i;
                break;
            }
        }
        RefreshItemGrids();
    }

    /// <summary>
    /// 原版 AddCompanionItems：伙伴获得物品包是在服务端合并前发送的，
    /// 因此客户端必须按现有伙伴背包重新堆叠，不能把临时回包直接追加到
    /// ClientUserCompanion.Items 后按 Slot 重建，否则合并物品常会覆盖 0 格。
    /// </summary>
    public void AddCompanionItems(List<ClientUserItem> items)
    {
        if (Companion == null || items == null) return;

        foreach (var item in items)
        {
            if (item?.Info == null || item.Info.ItemEffect == ItemEffect.Experience) continue;

            var currency = Currencies.FirstOrDefault(x => x?.Info?.DropItem == item.Info);
            if (currency != null)
            {
                currency.Amount += item.Count;
                RefreshCurrency();
                continue;
            }

            bool handled = false;
            if (item.Info.StackSize > 1 && !item.Flags.HasFlag(UserItemFlags.Expirable))
            {
                for (int i = 0; i < CompanionInventory.Length; i++)
                {
                    var existing = CompanionInventory[i];
                    if (existing == null || existing.Info != item.Info ||
                        existing.Count >= existing.Info.StackSize ||
                        existing.Flags.HasFlag(UserItemFlags.Expirable) != item.Flags.HasFlag(UserItemFlags.Expirable) ||
                        existing.Flags.HasFlag(UserItemFlags.Bound) != item.Flags.HasFlag(UserItemFlags.Bound) ||
                        existing.Flags.HasFlag(UserItemFlags.Worthless) != item.Flags.HasFlag(UserItemFlags.Worthless) ||
                        existing.Flags.HasFlag(UserItemFlags.NonRefinable) != item.Flags.HasFlag(UserItemFlags.NonRefinable) ||
                        !existing.AddedStats.Compare(item.AddedStats)) continue;

                    if (existing.Count + item.Count <= existing.Info.StackSize)
                    {
                        existing.Count += item.Count;
                        handled = true;
                        break;
                    }

                    item.Count -= existing.Info.StackSize - existing.Count;
                    existing.Count = existing.Info.StackSize;
                }
            }
            if (handled) continue;

            for (int i = 0; i < CompanionInventory.Length; i++)
            {
                if (CompanionInventory[i] != null) continue;
                CompanionInventory[i] = item;
                item.Slot = i;
                break;
            }
        }

        SyncCompanionItemList();
        // 原版 CConnection.Process(S.CompanionItemsGained) -> AddCompanionItems 只刷新格子,
        // 不做 ApplyCompanion 全量同步; 共享数组时 Clear+Copy 会把刚写入的物品抹空。
        _companionDialog?.RefreshCompanionStats(Companion);
        RefreshItemGrids();
    }

    private void SyncCompanionItemList()
    {
        if (Companion == null) return;
        Companion.Items = new List<ClientUserItem>();
        foreach (var item in CompanionInventory)
            if (item != null) Companion.Items.Add(item);
        for (int i = 0; i < CompanionEquipment.Length; i++)
        {
            var item = CompanionEquipment[i];
            if (item == null) continue;
            item.Slot = Globals.EquipmentOffSet + i;
            Companion.Items.Add(item);
        }
    }

    // 物品移动 (服务端权威, 直接执行; 原版无双向确认)
    private void OnItemMove(S.ItemMove p)
    {
        if (p == null) return;
        bool operationMove =
            (p.FromGrid == GridType.Inventory && p.ToGrid == GridType.Inventory
                && ((p.FromSlot == _operationAuditSourceSlot && p.ToSlot == _operationAuditTargetSlot && _operationAuditStage == 1)
                    || (p.FromSlot == _operationAuditTargetSlot && p.ToSlot == _operationAuditSourceSlot && _operationAuditStage == 2)))
            || (p.FromGrid == GridType.Inventory && p.ToGrid == GridType.Equipment
                && p.FromSlot == _operationAuditSourceSlot && p.ToSlot == _operationAuditEquipmentSlot && _operationAuditStage == 4)
            || (p.FromGrid == GridType.Equipment && p.ToGrid == GridType.Inventory
                && p.FromSlot == _operationAuditEquipmentSlot && p.ToSlot == _operationAuditTargetSlot && _operationAuditStage == 3)
            || (p.FromGrid == GridType.Equipment && p.ToGrid == GridType.Inventory
                && p.FromSlot == _operationAuditEquipmentSlot && p.ToSlot == _operationAuditSourceSlot && _operationAuditStage == 5)
            || (p.FromGrid == GridType.Inventory && p.ToGrid == GridType.Equipment
                && p.FromSlot == _operationAuditTargetSlot && p.ToSlot == _operationAuditEquipmentSlot && _operationAuditStage == 6);
        if (AutoLoginArgs.OperationAudit && operationMove)
        {
            _operationAuditLastSuccess = p.Success;
            _operationAuditResponsePending = true;
            CallDeferred(nameof(ContinueOperationAudit));
        }
        // S17: Inventory->CompanionInventory (食物入背包); S18: Inventory<->GuildStorage (仓库移动)
        // S18b/c: Inventory->GuildStorage 槽0 (b 用 _FromSlot, c 用 _SlotB 打不同物品);
        // S18d: GuildStorage 槽0 -> Inventory (From/To 相对 b 互换)
        bool operationExtMove =
            ((_operationAuditExtStage is >= 4 and <= 8 || _operationAuditExtStage == 12)
                && p.FromSlot == _operationAuditExtFromSlot && p.ToSlot == _operationAuditExtToSlot)
            || (_operationAuditExtStage == 17
                && p.FromGrid == GridType.Inventory && p.ToGrid == GridType.CompanionInventory
                && p.FromSlot == _operationAuditExtFromSlot && p.ToSlot == _operationAuditExtToSlot)
            || (_operationAuditExtStage == 18
                && ((_operationAuditExtGuildSubStage is 1 or 2
                        && p.FromGrid == GridType.Inventory && p.ToGrid == GridType.GuildStorage
                        && p.FromSlot == (_operationAuditExtGuildSubStage == 1 ? _operationAuditExtFromSlot : _operationAuditExtSlotB)
                        && p.ToSlot == _operationAuditExtToSlot)
                    || (_operationAuditExtGuildSubStage == 3
                        && p.FromGrid == GridType.GuildStorage && p.ToGrid == GridType.Inventory
                        && p.FromSlot == _operationAuditExtToSlot && p.ToSlot == _operationAuditExtFromSlot)));
        if (AutoLoginArgs.OperationAuditExt && operationExtMove)
        {
            _operationAuditExtLastSuccess = p.Success;
            _operationAuditExtResponsePending = true;
            CallDeferred(nameof(ContinueOperationAuditExt));
        }
        UnlockCell(p.FromGrid, p.FromSlot);
        UnlockCell(p.ToGrid, p.ToSlot);
        var fromArr = GetGrid(p.FromGrid);
        var toArr = GetGrid(p.ToGrid);
        if (fromArr == null || toArr == null) return;
        if (p.FromSlot < 0 || p.FromSlot >= fromArr.Length) return;
        if (p.ToSlot < 0 || p.ToSlot >= toArr.Length) return;

        var fromItem = fromArr[p.FromSlot];
        var toItem = toArr[p.ToSlot];

        // 背包<->装备移动: 清理旧物品的腰带链接
        if (p.FromGrid != p.ToGrid && p.Success)
        {
            if (p.FromGrid is GridType.Inventory or GridType.CompanionInventory && fromItem != null && !fromItem.Info.ShouldLinkInfo)
                ClearBeltLinkItem(fromItem.Index, p.ToGrid == GridType.Equipment ? toItem : null);
            else if (p.ToGrid is GridType.Inventory or GridType.CompanionInventory && toItem != null && !toItem.Info.ShouldLinkInfo)
                ClearBeltLinkItem(toItem.Index, null);
        }

        if (!p.Success) return;

        if (p.MergeItem)
        {
            if (fromItem == null || toItem == null) return;
            if (fromItem.Info != toItem.Info || !DXItemCell.CanMergeItems(fromItem, toItem)) return;
            if (toItem.Count + fromItem.Count <= toItem.Info.StackSize)
            {
                toItem.Count += fromItem.Count;
                fromArr[p.FromSlot] = null;
            }
            else
            {
                fromItem.Count -= toItem.Info.StackSize - toItem.Count;
                toItem.Count = toItem.Info.StackSize;
            }
        }
        else
        {
            toArr[p.ToSlot] = fromItem;
            fromArr[p.FromSlot] = toItem;
            // 服务端 ClientUserItem.Slot 使用协议槽位：装备/伙伴装备和碎片
            // 仓库分别带 EquipmentOffSet/PartsStorageOffset；界面数组则使用
            // 从 0 开始的本地槽位。回包交换后必须按目标网格恢复协议槽位，
            // 否则下一次伙伴同步、临时链接或仓库回包会引用错误位置。
            if (fromItem != null) fromItem.Slot = ProtocolItemSlot(p.ToGrid, p.ToSlot);
            if (toItem != null) toItem.Slot = ProtocolItemSlot(p.FromGrid, p.FromSlot);
        }

        if (p.FromGrid is GridType.CompanionInventory or GridType.CompanionEquipment ||
            p.ToGrid is GridType.CompanionInventory or GridType.CompanionEquipment)
            SyncCompanionItemList();
        RefreshItemGrids();
    }

    private static int ProtocolItemSlot(GridType grid, int slot) => grid switch
    {
        GridType.Equipment or GridType.CompanionEquipment => slot + Globals.EquipmentOffSet,
        GridType.PartsStorage => slot + Globals.PartsStorageOffset,
        _ => slot,
    };

    // 整理 (清空重排)
    private void OnItemSort(S.ItemSort p)
    {
        var arr = GetGrid(p.Grid);
        if (arr == null) return;

        var cells = p.Grid switch
        {
            GridType.Inventory => InventoryCells,
            GridType.Storage => StorageCells,
            GridType.PartsStorage => PartsStorageCells,
            GridType.GuildStorage => GuildStorageItemCells,
            GridType.CompanionInventory => CompanionInventoryCells,
            GridType.CompanionEquipment => CompanionEquipmentCells,
            _ => Array.Empty<DXItemCell>(),
        };
        for (int i = 0; i < arr.Length; i++)
        {
            if (i < cells.Length && cells[i] != null)
            {
                cells[i].Locked = false;
                cells[i].Selected = false;
                cells[i].UpdateBorder();
            }
        }

        DXItemCell.SelectedCell = null;
        // 失败/异常整理回包只能解除客户端锁，不能清空当前权威物品数组。
        if (!p.Success) return;

        for (int i = 0; i < arr.Length; i++)
            arr[i] = null;

        if (p.Items != null)
        {
            foreach (var item in p.Items)
            {
                int slot = item.Slot;
                if (p.Grid == GridType.PartsStorage) slot -= Globals.PartsStorageOffset;
                if (slot < 0 || slot >= arr.Length) continue;
                arr[slot] = item;
            }
        }
        if (p.Grid is GridType.CompanionInventory or GridType.CompanionEquipment)
            SyncCompanionItemList();
        RefreshItemGrids();
    }

    // 拆分
    private void OnItemSplit(S.ItemSplit p)
    {
        if (p == null) return;
        UnlockCell(p.Grid, p.Slot);
        var arr = GetGrid(p.Grid);
        if (arr == null) return;
        if (p.Slot < 0 || p.Slot >= arr.Length) return;
        if (!p.Success) return;

        var fromItem = arr[p.Slot];
        if (fromItem == null) return;
        if (p.NewSlot < 0 || p.NewSlot >= arr.Length) return;
        // 迟到/重复拆分回包不能覆盖新操作已经写入的目标格，
        // 也不能把当前堆叠拆出超过现有数量的副本。
        if (!TryApplyItemSplit(fromItem, arr, p.Slot, p.NewSlot, p.Count)) return;

        if (p.Grid == GridType.CompanionInventory) SyncCompanionItemList();
        RefreshItemGrids();
    }

    public static bool TryApplyItemSplit(ClientUserItem source, ClientUserItem[] grid,
        int sourceSlot, int newSlot, long count)
    {
        if (source == null || grid == null || sourceSlot < 0 || sourceSlot >= grid.Length ||
            newSlot < 0 || newSlot >= grid.Length || sourceSlot == newSlot ||
            !ReferenceEquals(grid[sourceSlot], source) || grid[newSlot] != null ||
            count <= 0 || count > source.Count)
            return false;

        grid[newSlot] = new ClientUserItem(source, count) { Slot = newSlot };
        if (count == source.Count) grid[sourceSlot] = null;
        else source.Count -= count;
        return true;
    }

    // 删除 (丢弃/移除)
    private void OnItemDelete(S.ItemDelete p)
    {
        if (p == null) return;
        var arr = GetGrid(p.Grid);
        if (arr == null) return;
        if (p.Slot < 0 || p.Slot >= arr.Length) return;

        var key = (p.Grid, p.Slot);
        if (_pendingItemDeletes.TryGetValue(key, out var expectedIndex))
        {
            _pendingItemDeletes.Remove(key);
            // ItemDelete 没有携带物品身份。若服务端回包已经落后于一次
            // 槽位复用，旧删除只能被忽略，且不能解锁当前的新物品。
            if (arr[p.Slot]?.Index != expectedIndex) return;
        }

        UnlockCell(p.Grid, p.Slot);
        DXItemCell.SelectedCell = null;
        if (!p.Success) return;

        var item = arr[p.Slot];
        if (item == null) return;

        if (!item.Info.ShouldLinkInfo)
            ClearBeltLinkItem(item.Index, null);

        arr[p.Slot] = null;
        if (p.Grid == GridType.CompanionInventory) SyncCompanionItemList();
        RefreshItemGrids();
    }

    // 锁定
    private void OnItemLock(S.ItemLock p)
    {
        var item = ItemAt(p.Grid, p.Slot);
        if (item == null) return;
        if (p.Locked) item.Flags |= UserItemFlags.Locked;
        else item.Flags &= ~UserItemFlags.Locked;
        RefreshItemGrids();
        if (AutoLoginArgs.OperationAuditExt && p.Grid == GridType.Inventory
            && p.Slot == _operationAuditExtSlotA && (_operationAuditExtStage == 2 || _operationAuditExtStage == 3))
        {
            _operationAuditExtLastSuccess = true;
            _operationAuditExtResponsePending = true;
            CallDeferred(nameof(ContinueOperationAuditExt));
        }
    }

    // 使用延迟: 服务端给的绝对冷却 (下次可用时间)
    private void OnItemUseDelay(S.ItemUseDelay p)
    {
        UseItemTime = Godot.Time.GetTicksMsec() + p.Delay.TotalMilliseconds;
        if (AutoLoginArgs.OperationAuditExt && _operationAuditExtStage != 0)
            GD.Print($"[OperationAuditExt] COOLDOWN-DELAY delayMs={p.Delay.TotalMilliseconds}");
    }

    // 数量变更 (使用消耗/拆分结果)
    private void OnItemChanged(S.ItemChanged p)
    {
        if (p?.Link == null) return;
        var arr = GetGrid(p.Link.GridType);
        if (arr == null) return;
        if (p.Link.Slot < 0 || p.Link.Slot >= arr.Length) return;

        var key = (p.Link.GridType, p.Link.Slot);
        if (_pendingItemUses.TryGetValue(key, out var expectedIndex))
        {
            _pendingItemUses.Remove(key);
            // ItemChanged 只包含槽位和数量，不包含物品 Index。槽位在请求发出后
            // 若已被移动/交换，迟到的扣数量回包不能写入当前新物品。
            if (arr[p.Link.Slot]?.Index != expectedIndex) return;
        }

        UnlockCell(p.Link.GridType, p.Link.Slot);
        _consignmentDialog?.ItemChanged(p);
        _npcDialog?.ClearAdvancedLinks(new[] { p.Link });
        if (!p.Success) return;

        var item = arr[p.Link.Slot];
        if (item == null) return;

        if (!item.Info.ShouldLinkInfo)
            ClearBeltLinkItem(item.Index, null);

        if (p.Link.Count == 0) arr[p.Link.Slot] = null;
        else item.Count = p.Link.Count;

        if (AutoLoginArgs.OperationAuditExt && _operationAuditExtStage == 17)
            GD.Print($"[OperationAuditExt] S17b hook-wrote arr0={arr[0]?.Info?.ItemName ?? "null"} cnt={arr[0]?.Count} slot={p.Link.Slot} pcnt={p.Link.Count}");

        if (p.Link.GridType is GridType.CompanionInventory or GridType.CompanionEquipment)
            SyncCompanionItemList();
        RefreshItemGrids();
        if (AutoLoginArgs.OperationAuditExt && _operationAuditExtStage == 17)
            GD.Print($"[OperationAuditExt] S17b raw ItemChanged grid={p.Link.GridType} slot={p.Link.Slot} success={p.Success} count={p.Link.Count} subStage={_operationAuditExtCompanionSubStage}");
        if (AutoLoginArgs.OperationAuditExt && p.Success
            && ((_operationAuditExtStage == 1
                    && p.Link.GridType == GridType.Inventory && p.Link.Slot == _operationAuditExtSlotA)
                || (_operationAuditExtStage == 17 && _operationAuditExtCompanionSubStage == 1
                    && p.Link.GridType == GridType.CompanionInventory && p.Link.Slot == _operationAuditExtToSlot)))
        {
            _operationAuditExtLastSuccess = p.Success;
            if (_operationAuditExtStage == 17)
            {
                // 等 S.CompanionUpdate (后发) 提供 Hunger 再判
                _operationAuditExtCompanionItemChanged = true;
                if (_operationAuditExtCompanionHungerAfter < 0) return;
            }
            _operationAuditExtResponsePending = true;
            CallDeferred(nameof(ContinueOperationAuditExt));
        }
    }

    // 属性变更 (附魔等)
    private void OnItemStatsChanged(S.ItemStatsChanged p)
    {
        var item = ItemAt(p.GridType, p.Slot);
        if (item == null) return;
        item.AddedStats.Add(p.NewStats);
        GD.Print($"[Item] 属性变更: {item.Info?.ItemName} +{p.NewStats.Count} 条");
        RefreshItemGrids();
    }

    private void OnItemStatsRefreshed(S.ItemStatsRefreshed p)
    {
        var item = ItemAt(p.GridType, p.Slot);
        if (item == null) return;
        item.AddedStats = p.NewStats;
        RefreshItemGrids();
    }

    // 耐久变化: 归零提示
    private void OnItemDurability(S.ItemDurability p)
    {
        var item = ItemAt(p.GridType, p.Slot);
        if (item == null) return;
        item.CurrentDurability = p.CurrentDurability;
        if (p.CurrentDurability == 0)
            GD.Print($"[Item] {item.Info?.ItemName} 耐久已耗尽");
        RefreshItemGrids();
    }

    // 武器熟练度经验
    private void OnItemExperience(S.ItemExperience p)
    {
        var item = ItemAt(p.Target.GridType, p.Target.Slot);
        if (item == null) return;
        ApplyItemExperience(item, p.Experience, p.Level, p.Flags);
        RefreshItemGrids();
    }

    /// <summary>
    /// 应用服务端完整的武器熟练度回包。
    /// 原版直接覆盖 Flags；不能只在升级时追加 Bound，否则降级/失败回包
    /// 会把旧的绑定状态残留在客户端，进而错误阻止交易、精炼或穿戴。
    /// </summary>
    public static void ApplyItemExperience(ClientUserItem item, decimal experience,
        int level, UserItemFlags flags)
    {
        if (item == null) return;
        item.Experience = experience;
        item.Level = level;
        item.Flags = flags;
    }

    // 批量变更 (仓库/交易等; Count == 当前 Count 表示整格移除)
    private void OnItemsChanged(S.ItemsChanged p)
    {
        if (p == null) return;
        _inventoryDialog?.ItemsChanged(p.Links, p.Success == true);
        _npcDialog?.ItemsChanged(p?.Links);
        _npcDialog?.ClearAdvancedLinks(p?.Links);
        _communicationDialog?.ItemsChanged(p?.Links, p?.Success == true);
        if (p.Links == null)
        {
            DXItemCell.SelectedCell = null;
            return;
        }

        foreach (var link in p.Links)
        {
            if (link == null) continue;
            // 先解锁再校验数组/槽位：仓库、邮件、NPC 等批量操作的异常或迟到回包
            // 不能把来源格永久留在 Locked 状态。
            UnlockCell(link.GridType, link.Slot);
            var arr = GetGrid(link.GridType);
            if (arr == null) continue;
            if (link.Slot < 0 || link.Slot >= arr.Length) continue;

            var item = arr[link.Slot];
            if (AutoLoginArgs.OperationAuditExt && _operationAuditExtStage == 17 && link.GridType == GridType.CompanionInventory)
                GD.Print($"[OperationAuditExt] S17b raw ItemsChanged slot={link.Slot} cnt={link.Count} success={p.Success} cur={(item == null ? "null" : item.Count.ToString())}");
            if (item == null || !p.Success) continue;

            if (!item.Info.ShouldLinkInfo)
                ClearBeltLinkItem(item.Index, null);

            if (!TryConsumeItemCount(item, link.Count, out bool remove))
                continue;
            if (remove) arr[link.Slot] = null;
        }
        DXItemCell.SelectedCell = null;
        if (p.Links.Any(x => x?.GridType is GridType.CompanionInventory or GridType.CompanionEquipment))
            SyncCompanionItemList();
        RefreshItemGrids();
        if (AutoLoginArgs.OperationAuditExt && _operationAuditExtStage == 13)
        {
            if (p.Success != true)
            {
                _operationAuditExtLastSuccess = false;
                _operationAuditExtResponsePending = true;
                CallDeferred(nameof(ContinueOperationAuditExt));
                return;
            }
            _operationAuditExtLastSuccess = true;
            if (_auditMailNewReceived)
            {
                _operationAuditExtResponsePending = true;
                CallDeferred(nameof(ContinueOperationAuditExt));
            }
        }
    }

    public static bool TryConsumeItemCount(ClientUserItem item, long count, out bool remove)
    {
        remove = false;
        if (item == null || count <= 0 || count > item.Count) return false;
        if (count == item.Count)
        {
            remove = true;
            return true;
        }
        item.Count -= count;
        return true;
    }

    // 货币变更
    private void OnCurrencyChanged(int currencyIndex, long amount)
    {
        var currency = Currencies.FirstOrDefault(x => x.CurrencyIndex == currencyIndex);
        if (currency != null) currency.Amount = amount;
        if (currency?.Info?.Type == CurrencyType.Gold)
            PlaySound(SoundIndex.GoldGained);
        RefreshCurrency();
        _currencyDialog?.RefreshCurrencies(Currencies);
    }

    public void SetDropFilters(string[] filters)
    {
        DropFilters = filters ?? Array.Empty<string>();
        _chatLog?.AddMessage(Lang.GameSettingsLabel2, new Color(1f, .85f, .45f));
    }

    // 负重变更
    private void OnWeightUpdate(int bag, int wear, int hand)
    {
        BagWeight = bag;
        WearWeight = wear;
        HandWeight = hand;
        _inventoryDialog?.SetWeight(bag);
        _characterDialog?.SetWeight(wear, hand);
    }

    // 仓库容量变更
    private void OnStorageSize(int size)
    {
        StorageSize = size;
        _storageDialog?.RefreshStorage();
    }

    // ---- 使用/穿戴校验 (移植自原版 CanUseItem/CanWearItem) ----
    public bool CanUseItem(ClientUserItem item)
    {
        if (item?.Info == null) return false;

        if (StartInfo == null) return false;

        RequiredGender gender = StartInfo.Gender == MirGender.Male ? RequiredGender.Male : RequiredGender.Female;
        if (!item.Info.RequiredGender.HasFlag(gender))
            return false;

        RequiredClass requiredClass = StartInfo.Class switch
        {
            MirClass.Warrior => RequiredClass.Warrior,
            MirClass.Wizard => RequiredClass.Wizard,
            MirClass.Taoist => RequiredClass.Taoist,
            MirClass.Assassin => RequiredClass.Assassin,
            _ => RequiredClass.None,
        };
        if (!item.Info.RequiredClass.HasFlag(requiredClass))
            return false;

        switch (item.Info.RequiredType)
        {
            case RequiredType.Level:
                if (_playerLevel < item.Info.RequiredAmount && _playerStats[Stat.Rebirth] == 0) return false;
                break;
            case RequiredType.MaxLevel:
                if (_playerLevel > item.Info.RequiredAmount || _playerStats[Stat.Rebirth] > 0) return false;
                break;
            case RequiredType.AC:
                if (_playerStats[Stat.MaxAC] < item.Info.RequiredAmount) return false;
                break;
            case RequiredType.MR:
                if (_playerStats[Stat.MaxMR] < item.Info.RequiredAmount) return false;
                break;
            case RequiredType.DC:
                if (_playerStats[Stat.MaxDC] < item.Info.RequiredAmount) return false;
                break;
            case RequiredType.MC:
                if (_playerStats[Stat.MaxMC] < item.Info.RequiredAmount) return false;
                break;
            case RequiredType.SC:
                if (_playerStats[Stat.MaxSC] < item.Info.RequiredAmount) return false;
                break;
            case RequiredType.Health:
                if (_playerStats[Stat.Health] < item.Info.RequiredAmount) return false;
                break;
            case RequiredType.Mana:
                if (_playerStats[Stat.Mana] < item.Info.RequiredAmount) return false;
                break;
            case RequiredType.Accuracy:
                if (_playerStats[Stat.Accuracy] < item.Info.RequiredAmount) return false;
                break;
            case RequiredType.Agility:
                if (_playerStats[Stat.Agility] < item.Info.RequiredAmount) return false;
                break;
            case RequiredType.CompanionLevel:
                if (Companion == null || Companion.Level < item.Info.RequiredAmount) return false;
                break;
            case RequiredType.MaxCompanionLevel:
                if (Companion == null || Companion.Level > item.Info.RequiredAmount) return false;
                break;
            case RequiredType.RebirthLevel:
                if (_playerStats[Stat.Rebirth] < item.Info.RequiredAmount) return false;
                break;
            case RequiredType.MaxRebirthLevel:
                if (_playerStats[Stat.Rebirth] > item.Info.RequiredAmount) return false;
                break;
        }

        if (item.Info.ItemType == ItemType.Book)
        {
            var magic = Globals.MagicInfoList?.Binding.FirstOrDefault(x => x.Index == item.Info.Shape);
            if (magic == null || magic.School == MagicSchool.None) return false;
            if (UserMagics.TryGetValue(magic, out var learned) &&
                (learned.Level < 3 || item.Flags.HasFlag(UserItemFlags.NonRefinable))) return false;
        }
        else if (item.Info.ItemType == ItemType.Consumable && item.Info.Shape == 1 &&
                 _buffs.Values.Any(x => x.Type == BuffType.ItemBuff && x.ItemIndex == item.Info.Index &&
                                        x.RemainingTime == TimeSpan.MaxValue))
            return false;

        return true;
    }

    public bool CanWearItem(ClientUserItem item, EquipmentSlot slot)
    {
        if (item?.Info == null) return false;
        if (!CanUseItem(item)) return false;

        // 类型与槽位匹配 (原版 CorrectSlot 校验, 客户端先行)
        if (!Functions.CorrectSlot(item.Info.ItemType, slot))
        {
            GD.Print($"[Item] {item.Info.ItemName} 不能穿戴到 {slot}");
            return false;
        }

        // 钓鱼配件必须在鱼竿装备时才能穿戴。
        if (slot is EquipmentSlot.Hook or EquipmentSlot.Float or EquipmentSlot.Bait or EquipmentSlot.Finder or EquipmentSlot.Reel &&
            Equipment[(int)EquipmentSlot.Weapon]?.Info?.ItemEffect != ItemEffect.FishingRod)
        {
            ReceiveChat($"无法持有{item.Info.Local()}，必须手持鱼竿。", MessageType.System);
            return false;
        }

        // 负重: 手持槽 (Weapon/Torch/Shield) 查 HandWeight, 其余查 WearWeight; 卸下旧装备减重
        ClientUserItem old = Equipment[(int)slot];
        int weight = item.Weight - (old?.Weight ?? 0);
        if (slot == EquipmentSlot.Weapon || slot == EquipmentSlot.Torch || slot == EquipmentSlot.Shield)
        {
            if (HandWeight + weight > _playerStats[Stat.HandWeight])
            {
                ReceiveChat($"无法持有{item.Info.Local()}，它太重了。", MessageType.System);
                return false;
            }
        }
        else if (WearWeight + weight > _playerStats[Stat.WearWeight])
        {
            ReceiveChat($"无法穿戴{item.Info.Local()}，它太重了。", MessageType.System);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 原版 CanCompanionWearItem：伙伴装备除了物品槽位外，还必须有当前伙伴，
    /// 并满足 CompanionLevel/MaxCompanionLevel 限制。
    /// </summary>
    public bool CanCompanionWearItem(ClientUserItem item, CompanionSlot slot)
    {
        if (Companion == null || item?.Info == null) return false;
        if (!Functions.CorrectSlot(item.Info.ItemType, slot)) return false;
        return CanCompanionUseItem(item.Info);
    }

    public bool CanCompanionUseItem(ItemInfo info)
    {
        if (info == null || Companion == null) return false;
        return info.RequiredType switch
        {
            RequiredType.CompanionLevel => Companion.Level >= info.RequiredAmount,
            RequiredType.MaxCompanionLevel => Companion.Level <= info.RequiredAmount,
            _ => true,
        };
    }

    // 死亡: 播 Die 动画后延迟移除
    private void OnObjectDied(uint objectID)
    {        if (objectID == _playerObjectID)
        {
            if (_player != null) _player.PlayDie();
            return;
        }
        if (_otherPlayers.TryGetValue(objectID, out var player))
        {
            player.Dead = true;
            player.PlayDie();
        }
        else if (_objects.TryGetValue(objectID, out var ob))
        {
            ob.Dead = true;
            ob.SetAnimation(MirAnimation.Die);
            ob.PlayDieSound();
            if (AutoLoginArgs.OperationAuditExt && _operationAuditExtStage == 16
                && objectID == _operationAuditExtCombatTargetId)
            {
                // D15: 目标死亡保留选中 (尸体高亮), 直到 ObjectRemove/切图/右键才清除
                _operationAuditExtCombatDied = true;
                _operationAuditExtCombatKeptTarget = _combatController?.TargetObject == ob;
                GD.Print($"[OperationAuditExt] S16 died-kept-target={_operationAuditExtCombatKeptTarget} target={_combatController?.TargetObject?.DisplayName ?? "null"}");
            }
        }
    }

    // 排空 StartGame 突发积压包(顺序与服务器一致: Move/Turn/Player/Monster/NPC/Item/Remove)
    private void DrainPendingObjects()
    {
        var conn = _net?.Connection;
        if (conn == null) return;
        while (conn.PendingMoves.Count > 0)
        {
            var m = conn.PendingMoves.Dequeue();
            OnObjectMove(m.ObjectID, m.Direction, m.Location, m.Distance, m.Slow, m.MapChanged);
        }
        while (conn.PendingTurns.Count > 0)
        {
            var turn = conn.PendingTurns.Dequeue();
            OnObjectTurn(turn.ObjectID, turn.Direction, turn.Location, turn.Slow);
        }
        while (conn.PendingPlayers.Count > 0)
            OnObjectPlayer(conn.PendingPlayers.Dequeue());
        while (conn.PendingMonsters.Count > 0)
            OnObjectMonster(conn.PendingMonsters.Dequeue());
        while (conn.PendingNPCs.Count > 0)
            OnObjectNPC(conn.PendingNPCs.Dequeue());
        while (conn.PendingItems.Count > 0)
            OnObjectItem(conn.PendingItems.Dequeue());
        while (conn.PendingChats.Count > 0)
            OnChat(conn.PendingChats.Dequeue());
        while (conn.PendingRemoves.Count > 0)
            OnObjectRemove(conn.PendingRemoves.Dequeue());
        while (conn.PendingAttacks.Count > 0)
        {
            var a = conn.PendingAttacks.Dequeue();
            OnObjectAttack(a);
        }
        while (conn.PendingMagics.Count > 0)
        {
            var m = conn.PendingMagics.Dequeue();
            OnObjectMagic(m.ObjectID, m.Direction, m.CurrentLocation, m.Type, m.Targets, m.Locations, m.Cast);
        }
        while (conn.PendingProjectiles.Count > 0) OnObjectProjectile(conn.PendingProjectiles.Dequeue());
        while (conn.PendingSpells.Count > 0) OnObjectSpell(conn.PendingSpells.Dequeue());
        while (conn.PendingSpellChanges.Count > 0) OnObjectSpellChanged(conn.PendingSpellChanges.Dequeue());
        while (conn.PendingObjectEffects.Count > 0)
        {
            var e = conn.PendingObjectEffects.Dequeue();
            OnObjectEffect(e.ObjectID, e.Effect);
        }
        while (conn.PendingMapEffects.Count > 0)
        {
            var e = conn.PendingMapEffects.Dequeue();
            OnMapEffect(e.Location, e.Effect, e.Direction);
        }
        while (conn.PendingHealthChanges.Count > 0)
        {
            var h = conn.PendingHealthChanges.Dequeue();
            OnHealthChanged(h.ObjectID, h.Change, h.Miss, h.Block, h.Critical, h.Resist);
        }
        while (conn.PendingHealthManas.Count > 0)
        {
            var h = conn.PendingHealthManas.Dequeue();
            OnDataObjectHealthMana(h.ObjectID, h.Health, h.Mana, h.Dead);
        }
        while (conn.PendingMaxHealthManas.Count > 0)
        {
            var h = conn.PendingMaxHealthManas.Dequeue();
            OnDataObjectMaxHealthMana(h.ObjectID, h.MaxHealth, h.MaxMana);
        }
        while (conn.PendingDataMonsters.Count > 0)
        {
            var m = conn.PendingDataMonsters.Dequeue();
            OnDataObjectMonsterInfo(m.ObjectID, m.Health, m.Stats != null ? m.Stats[Stat.Health] : 0,
                m.Stats != null ? m.Stats[Stat.Light] : 0, m.MonsterIndex, m.Dead);
        }
        while (conn.PendingDeaths.Count > 0)
            OnObjectDied(conn.PendingDeaths.Dequeue());
        while (conn.PendingStruck.Count > 0)
        {
            var s = conn.PendingStruck.Dequeue();
            OnObjectStruck(s.ObjectID, s.Direction, s.Location, s.AttackerID, s.Element);
        }
        while (conn.PendingStats.Count > 0)
            OnStatsUpdate(conn.PendingStats.Dequeue());
        while (conn.PendingLevelChanges.Count > 0)
            OnLevelChanged(conn.PendingLevelChanges.Dequeue());
        while (conn.PendingGainedExperience.Count > 0)
            OnGainedExperience(conn.PendingGainedExperience.Dequeue());
        while (conn.PendingMaxExperience.Count > 0)
            OnInformMaxExperience(conn.PendingMaxExperience.Dequeue());
        while (conn.PendingManaChanges.Count > 0)
        {
            var m = conn.PendingManaChanges.Dequeue();
            OnManaChanged(m.ObjectID, m.Change);
        }
        while (conn.PendingFocusChanges.Count > 0)
        {
            var f = conn.PendingFocusChanges.Dequeue();
            OnFocusChanged(f.ObjectID, f.Change);
        }
        while (conn.PendingBuffAdds.Count > 0)
            OnBuffAdd(conn.PendingBuffAdds.Dequeue());
        while (conn.PendingBuffRemoves.Count > 0)
            OnBuffRemove(conn.PendingBuffRemoves.Dequeue());
        while (conn.PendingBuffChangeds.Count > 0)
            OnBuffChanged(conn.PendingBuffChangeds.Dequeue());
        while (conn.PendingBuffTimes.Count > 0)
            OnBuffTime(conn.PendingBuffTimes.Dequeue());
        while (conn.PendingBuffPauseds.Count > 0)
        {
            var (idx, paused) = conn.PendingBuffPauseds.Dequeue();
            OnBuffPaused(idx, paused);
        }
        // M9 物品系统
        while (conn.PendingItemsGained.Count > 0)
            OnItemsGained(conn.PendingItemsGained.Dequeue());
        while (conn.PendingItemMoves.Count > 0)
            OnItemMove(conn.PendingItemMoves.Dequeue());
        while (conn.PendingItemSorts.Count > 0)
            OnItemSort(conn.PendingItemSorts.Dequeue());
        while (conn.PendingItemSplits.Count > 0)
            OnItemSplit(conn.PendingItemSplits.Dequeue());
        while (conn.PendingItemDeletes.Count > 0)
            OnItemDelete(conn.PendingItemDeletes.Dequeue());
        while (conn.PendingItemLocks.Count > 0)
            OnItemLock(conn.PendingItemLocks.Dequeue());
        while (conn.PendingItemUseDelays.Count > 0)
            OnItemUseDelay(conn.PendingItemUseDelays.Dequeue());
        while (conn.PendingItemChangeds.Count > 0)
            OnItemChanged(conn.PendingItemChangeds.Dequeue());
        while (conn.PendingItemStatsChangeds.Count > 0)
            OnItemStatsChanged(conn.PendingItemStatsChangeds.Dequeue());
        while (conn.PendingItemStatsRefresheds.Count > 0)
            OnItemStatsRefreshed(conn.PendingItemStatsRefresheds.Dequeue());
        while (conn.PendingItemDurabilities.Count > 0)
            OnItemDurability(conn.PendingItemDurabilities.Dequeue());
        while (conn.PendingItemExperiences.Count > 0)
            OnItemExperience(conn.PendingItemExperiences.Dequeue());
        while (conn.PendingItemsChangeds.Count > 0)
            OnItemsChanged(conn.PendingItemsChangeds.Dequeue());
        while (conn.PendingCurrencyChangeds.Count > 0)
        {
            var (idx, amount) = conn.PendingCurrencyChangeds.Dequeue();
            OnCurrencyChanged(idx, amount);
        }
        while (conn.PendingWeightUpdates.Count > 0)
        {
            var (bag, wear, hand) = conn.PendingWeightUpdates.Dequeue();
            OnWeightUpdate(bag, wear, hand);
        }
        while (conn.PendingStorageSizes.Count > 0)
            OnStorageSize(conn.PendingStorageSizes.Dequeue());
        while (conn.PendingNewMagics.Count > 0)
            OnNewMagic(conn.PendingNewMagics.Dequeue());
    }

    private void OnNewMagic(ClientUserMagic m)
    {
        if (m == null) return;
        if (m.Info == null) m.Complete();
        if (m.Info == null)
        {
            GD.PrintErr($"[Magic] 新技能信息解析失败 InfoIndex={m.InfoIndex}");
            return;
        }
        UserMagics[m.Info] = m;
        GD.Print($"[Magic] 学会技能: {m.Info.Name} (Magic={m.Info.Magic}) Set1={m.Set1Key} Level={m.Level}");
        _magicBar?.Refresh();
        _magicDialog?.Refresh();
    }

    private void OnMagicLeveled(S.MagicLeveled packet)
    {
        if (packet == null) return;
        var magic = UserMagics.Values.FirstOrDefault(x => x?.InfoIndex == packet.InfoIndex || x?.Info?.Index == packet.InfoIndex);
        if (magic == null) return;
        magic.Level = packet.Level;
        magic.Experience = packet.Experience;
        _magicBar?.Refresh();
        _magicDialog?.Refresh();
    }

    private void OnMagicCooldown(S.MagicCooldown packet)
    {
        if (packet == null) return;
        var magic = UserMagics.Values.FirstOrDefault(x => x?.InfoIndex == packet.InfoIndex || x?.Info?.Index == packet.InfoIndex);
        if (magic == null) return;
        magic.Cooldown = TimeSpan.FromMilliseconds(Math.Max(0, packet.Delay));
        magic.NextCast = Library.Time.Now + magic.Cooldown;
        _magicBar?.Refresh();
    }

    private void OnMagicToggle(S.MagicToggle packet)
    {
        if (packet == null) return;
        ReceiveChat($"{packet.Magic} {(packet.CanUse ? Lang.GameUi593Label : Lang.GameUi594Label)}", MessageType.System);
        _magicBar?.Refresh();
        _magicDialog?.Refresh();
    }

    private void AddObject(ObjectRenderer ob, uint objectID, int zIndex)
    {
        ob.ObjectID = objectID;
        ob.HitOrder = ++_nextObjectHitOrder;
        ob.ZIndex = zIndex;
        AddChild(ob);
        _objects[objectID] = ob;
        ob.SoundCue = PlaySound;
        UpdateObjectPositions();
        GD.Print($"[Game] 添加物体: {ob.Type} '{ob.DisplayName}' ObjectID={objectID} Cell=({ob.CellX},{ob.CellY})");

        // M12: 小/大地图动态标记
        _miniMap?.UpdateObject(objectID, ob.CellX, ob.CellY, ob.Type);
        _bigMap?.UpdateObject(objectID, ob.CellX, ob.CellY, ob.Type);
    }

    /// <summary>
    /// 重建地面掉落物名称的避让布局，并确保每个受影响节点真正进入
    /// Godot 的 CanvasItem 重绘队列。名称不仅要处理同格堆叠，也要处理
    /// 相邻格的图标/标签范围相交；否则两个掉落物虽然坐标不同，标签仍会互相盖住。
    /// </summary>
    private void RefreshGroundItemLabels(bool forceRedraw = false)
    {
        if (_objects == null) return;

        var items = _objects.Values.Where(x => x.Type == ObjectRenderer.Kind.Item).ToList();
        var placed = new List<Rect2>();
        var candidates = new List<Vector2>();
        int candidateCount = items.Count + 4;
        for (int level = 0; level < candidateCount; level++)
            candidates.Add(new Vector2(0f, -16f * level));
        for (int level = 1; level < candidateCount; level++)
        {
            float x = 72f * level;
            candidates.Add(new Vector2(-x, 0f));
            candidates.Add(new Vector2(x, 0f));
            candidates.Add(new Vector2(-x, -16f));
            candidates.Add(new Vector2(x, -16f));
        }

        // 最新掉落物优先占据物品上方的默认位置，旧物品依次向上/向两侧避让。
        foreach (var item in items.OrderByDescending(x => x.HitOrder))
        {
            float width = Math.Max(24f, RenderPrimitives.MeasureLabelWidth(item.DisplayName, 9f) + 8f);
            const float height = 18f;
            Vector2 baseCenter = new(item.CellX * 48f + 24f, item.CellY * 32f - 18f);
            Vector2 chosenOffset = Vector2.Zero;
            Rect2 chosenRect = default;
            bool found = false;

            foreach (Vector2 offset in candidates)
            {
                Vector2 center = baseCenter + offset;
                Rect2 rect = new(center.X - width / 2f, center.Y - 14f, width, height);
                if (placed.All(existing => !existing.Grow(2f).Intersects(rect)))
                {
                    chosenOffset = offset;
                    chosenRect = rect;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                chosenOffset = new Vector2(0f, -16f * placed.Count);
                Vector2 center = baseCenter + chosenOffset;
                chosenRect = new(center.X - width / 2f, center.Y - 14f, width, height);
            }

            placed.Add(chosenRect);
            bool changed = item.GroundItemLabelOffset != chosenOffset;
            item.GroundItemLabelOffset = chosenOffset;
            item.GroundItemLabelSlot = Math.Max(0, (int)Math.Round(-chosenOffset.Y / 16f));
            if (forceRedraw || changed)
                item.QueueRedraw();
        }
    }

    private void ShowUserLocation(int direction, int x, int y, int distance)
    {
        if (_player == null)
        {
            _moveServerLockUntilMs = 0;
            return;
        }
        // 只有在本次服务器回包真正应用到客户端状态后，才允许下一次
        // MouseWalker 请求。这样预测坐标与旧回包不会交叉覆盖。
        _moveServerLockUntilMs = 0;
        _canRun = true;
        MirDirection dir = (MirDirection)direction;
        _playerDirection = dir;

        // 原版 S.ObjectMove 正常只解锁+设 Slow, 不重 SetAction, 插值由发包时的
        // 预判(SetAction)一路播到底。复刻之: 若服务端确认的终点 == 预判终点,
        // 插值已在播, 这里只补方向/动画/小地图, 不重启插值时间轴, 避免回拉。
        // 仅当服务端位置≠预判(撞墙/被推/距离被改)时, 才走纠正路径重跳+重启插值。
        if (_playerLocation.X == x && _playerLocation.Y == y && _pendingDistance == distance)
        {
            // 预判命中: 不动 _playerLocation/CellX/CellY/Offset/_moveStartMs/_moveFrameCount,
            // 插值继续。只同步方向(服务端可能纠正朝向)与动画帧。
            _player.Direction = dir;
            UpdateAutoPathProgress();
            if (AutoLoginArgs.RunningTest || AutoLoginArgs.RightRunTest)
                GD.Print($"[{(AutoLoginArgs.RightRunTest ? "RightRunTest" : "RunningTest")}] APPLY(confirmed) distance={distance} animation={_player.Animation} " +
                         $"frameStart={_player.FrameIndex} location=({x},{y})");
            _statusLabel.Text = string.Format(Lang.GameLocationLabel2, x, y, dir);
            _miniMap?.UpdatePlayer(_player.CellX, _player.CellY);
            _bigMap?.UpdatePlayer(_player.CellX, _player.CellY);
            return;
        }

        // 纠正路径: 服务端位置≠预判(原版 Displacement 等价), 必须重跳+重启插值。
        // 原版 MovingOffSet：权威格立即切到终点，视觉位置从起点回拉。
        _moveFrom = _playerLocation;
        _moveStartMs = Godot.Time.GetTicksMsec();

        _playerLocation = new System.Drawing.Point(x, y);
        _player.CellX = x;
        _player.CellY = y;
        UpdateAutoPathProgress();
        _player.OffsetX = (_moveFrom.X - x) * 48f;
        _player.OffsetY = (_moveFrom.Y - y) * 32f;
        _player.Direction = dir;
        _pendingDistance = distance;
        // 原版 PlayerObject.SetFrame：Moving.Extra[0] >= 2 才使用 Running。
        // 右键只是请求跑步，最终动作必须以服务器接受的移动距离为准。
        _player.BeginMove(dir, distance, _playerHorse != HorseType.None, distance >= 2);
        _moveDurationMs = Math.Max(1.0, _player.MovementDurationMs);
        _moveVisualLockUntilMs = Godot.Time.GetTicksMsec() + _moveDurationMs;
        _mapView.CameraOffset = new Vector2(
            (x - _moveFrom.X) * 48f,
            (y - _moveFrom.Y) * 32f);
        if (AutoLoginArgs.RunningTest || AutoLoginArgs.RightRunTest)
            GD.Print($"[{(AutoLoginArgs.RightRunTest ? "RightRunTest" : "RunningTest")}] APPLY(corrected) distance={distance} animation={_player.Animation} " +
                     $"frameStart={_player.FrameIndex} location=({x},{y})");
        _moveFrameCount = 2;

        UpdatePlayerPosition();
        _statusLabel.Text = string.Format(Lang.GameLocationLabel2, x, y, dir);

        // M12: 地图玩家标记跟随
        _miniMap?.UpdatePlayer(_player.CellX, _player.CellY);
        _bigMap?.UpdatePlayer(_player.CellX, _player.CellY);
        UpdateAutoPathProgress();
    }

    private void StartRunningTest()
    {
        if (_net?.Connection?.Connected != true || _mapView?.Map == null || _player == null)
        {
            GD.PrintErr("[RunningTest] FAIL client is not ready");
            return;
        }

        _runningTestRightHeld = true;
        _canRun = false;
        int walkSteps = GetRunSteps();
        MirDirection walkDirection = FindRunningTestDirection(walkSteps);
        GD.Print($"[RunningTest] INPUT phase=walk canRun={_canRun} steps={walkSteps} " +
                 $"bag={BagWeight}/{_playerStats[Stat.BagWeight]} " +
                 $"wear={WearWeight}/{_playerStats[Stat.WearWeight]}");
        SendMouseMove(walkDirection, walkSteps, walkSteps >= 2);

        GetTree().CreateTimer(0.9).Timeout += () =>
        {
            int runSteps = GetRunSteps();
            MirDirection runDirection = FindRunningTestDirection(runSteps);
            GD.Print($"[RunningTest] INPUT phase=run canRun={_canRun} steps={runSteps} " +
                     $"bag={BagWeight}/{_playerStats[Stat.BagWeight]} " +
                     $"wear={WearWeight}/{_playerStats[Stat.WearWeight]}");
            SendMouseMove(runDirection, runSteps, runSteps >= 2);
        };

        GetTree().CreateTimer(1.8).Timeout += () =>
        {
            _runningTestRightHeld = false;
            GD.Print($"[RunningTest] RESULT animation={_player.Animation} frame={_player.FrameIndex} " +
                     $"location=({_playerLocation.X},{_playerLocation.Y})");
            GetTree().Quit();
        };
    }

    private void StartRightRunTest()
    {
        if (_net?.Connection?.Connected != true || _mapView?.Map == null || _player == null)
        {
            GD.PrintErr("[RightRunTest] FAIL client is not ready");
            return;
        }

        _runningTestRightHeld = true;
        _canRun = true;
        int steps = GetRunSteps();
        MirDirection direction = FindRunningTestDirection(steps);
        GD.Print($"[RightRunTest] INPUT right-held canRun={_canRun} steps={steps} direction={direction}");
        SendMouseMove(direction, steps, true);
        GetTree().CreateTimer(0.9).Timeout += () =>
        {
            int nextSteps = GetRunSteps();
            MirDirection nextDirection = FindRunningTestDirection(nextSteps);
            GD.Print($"[RightRunTest] INPUT right-held-second canRun={_canRun} steps={nextSteps} direction={nextDirection}");
            SendMouseMove(nextDirection, nextSteps, true);
        };
        GetTree().CreateTimer(1.8).Timeout += () =>
        {
            _runningTestRightHeld = false;
            GD.Print($"[RightRunTest] RESULT animation={_player.Animation} location=({_playerLocation.X},{_playerLocation.Y})");
            GetTree().Quit();
        };
    }

    private void ProcessGroundItemPickup()
    {
        if (_groundItemPickupTargetId == 0 || _player == null) return;
        if (!_objects.TryGetValue(_groundItemPickupTargetId, out var item)
            || item.Type != ObjectRenderer.Kind.Item)
        {
            _groundItemPickupTargetId = 0;
            return;
        }

        // 等当前移动插值和服务端回包完成，再判断是否已经到达拾取范围。
        // 这样不会在人物还滑行时提前发送 PickUp。
        double now = Godot.Time.GetTicksMsec();
        if (_moveFrameCount > 1 || now < _moveVisualLockUntilMs || now < _moveServerLockUntilMs)
            return;

        int distance = Functions.Distance(_playerLocation,
            new System.Drawing.Point(item.CellX, item.CellY));
        if (distance <= 1)
        {
            if (CanSendMapPickup(_observer, _player.Dead,
                    _playerPoison.HasFlag(PoisonType.Paralysis),
                    _playerPoison.HasFlag(PoisonType.Containment),
                    _player.DragonRepulsed))
                SendPickUp();
            _groundItemPickupTargetId = 0;
            return;
        }

        if (!CanPlayerMove()) return;
        MirDirection desired = Functions.DirectionFromPoint(_playerLocation,
            new System.Drawing.Point(item.CellX, item.CellY));
        MirDirection direction = desired;
        int steps = Math.Min(GetTargetRunSteps(), Math.Max(1, distance - 1));
        if (!CanMoveGroundItemPath(direction, steps))
        {
            steps = 1;
            direction = FindGroundItemDirection(item);
            if (!CanMoveGroundItemPath(direction, 1)) return;
        }
        SendMouseMove(direction, steps, steps >= 2);
    }

    private bool CanMoveGroundItemPath(MirDirection direction, int steps)
    {
        for (int i = 1; i <= steps; i++)
        {
            var point = Functions.Move(_playerLocation, direction, i);
            if (_mapView?.Map == null || point.X < 0 || point.Y < 0
                || point.X >= _mapView.Map.Width || point.Y >= _mapView.Map.Height
                || _mapView.Map.Cells[point.X, point.Y].Flag
                || IsMovementCellBlocked(point.X, point.Y))
                return false;
        }
        return true;
    }

    private MirDirection FindGroundItemDirection(ObjectRenderer item)
    {
        MirDirection best = MirDirection.Down;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < 8; i++)
        {
            var direction = (MirDirection)i;
            if (!CanMoveGroundItemPath(direction, 1)) continue;
            var next = Functions.Move(_playerLocation, direction);
            int distance = Functions.Distance(next, new System.Drawing.Point(item.CellX, item.CellY));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = direction;
            }
        }
        return best;
    }

    private void BeginGroundItemPickup(ObjectRenderer item)
    {
        if (item?.Type != ObjectRenderer.Kind.Item) return;
        _groundItemPickupTargetId = item.ObjectID;
        _combatController.TargetObject = null;
        _mouseWalker?.SuspendUntilInputRelease();
        _mouseWalker?.AutoRun = false;
        StopPlayerActionForInput();
    }

    private void SendMouseMove(MirDirection direction, int distance, bool running)
    {
        if (_player == null) return;
        bool offline = AutoLoginArgs.OfflineMovementTest;
        if (!offline && _net?.Connection?.Connected != true) return;
        distance = Math.Max(1, distance);
        // 战斗追击等非 MouseWalker 调用方也必须遵守同一条视觉时间轴。
        // 若上一段仍在移动，重启它会先把角色拉回上一格终点，再开始下一段。
        double now = Godot.Time.GetTicksMsec();
        if (_moveFrameCount > 1 && now < _moveVisualLockUntilMs)
            return;
        // 原版 UserObject.AttemptAction(Moving) → SetAction(Moving) 立即把
        // CurrentLocation 跳到预测终点并启动 MovingOffSet 插值; 回包(S.ObjectMove)
        // 正常只解锁+设 Slow, 不重 SetAction, 故无双重视觉/回拉。
        // 之前 Godot 这里只设动画不跳位置/不启动插值, 等回包 ShowUserLocation 才
        // 跳位置+设 Offset+_moveStartMs 重启插值; 由于回包(RTT~几十ms)远快于插值
        // 时长(~600ms), 回包到达时上一段插值还在播, ShowUserLocation 把 Offset
        // 重置回起点 → 视觉跳回起点再走 = 回拉/晃动。
        // 现复刻原版: 发包即跳到预测终点 + 设起点反向 Offset + 启动插值,
        // 回包只在服务端位置≠预测时纠正(见 ShowUserLocation)。
        var predicted = Functions.Move(_playerLocation, direction, distance);
        _moveFrom = _playerLocation;
        _moveStartMs = Godot.Time.GetTicksMsec();
        _playerLocation = predicted;
        _player.CellX = predicted.X;
        _player.CellY = predicted.Y;
        _player.OffsetX = (_moveFrom.X - predicted.X) * 48f;
        _player.OffsetY = (_moveFrom.Y - predicted.Y) * 32f;
        _player.Direction = direction;
        _pendingDistance = distance;
        _player.BeginMove(direction, distance, _playerHorse != HorseType.None,
            running && distance >= 2);
        _moveDurationMs = Math.Max(1.0, _player.MovementDurationMs);
        _moveVisualLockUntilMs = now + _moveDurationMs;
        _mapView.CameraOffset = new Vector2(
            (predicted.X - _moveFrom.X) * 48f,
            (predicted.Y - _moveFrom.Y) * 32f);
        _moveFrameCount = 2;
        UpdatePlayerPosition();
        UpdateAutoPathProgress();
        if (offline)
        {
            // 离线测试不等待服务端确认；本地预测就是权威位置，
            // 因此下一段移动可以按 MouseWalker 的 600ms 节拍继续。
            _moveServerLockUntilMs = 0;
            GD.Print($"[OfflineMove] MOVE distance={distance} running={running} " +
                     $"direction={direction} location=({_playerLocation.X},{_playerLocation.Y}) " +
                     $"animation={_player.Animation} frameCount={_player.MovementFrameCount} " +
                     $"durationMs={_moveDurationMs:0}");
        }
        else
        {
            _net.Connection.Enqueue(new C.Move { Direction = direction, Distance = distance });
            // 原版 AttemptAction 末尾 ServerTime = Now.AddSeconds(5): 锁住直到回包。
            // 5 秒是容错上限, 正常回包几十毫秒就解锁; 超时仍解锁避免永久卡死。
            _moveServerLockUntilMs = now + 5000.0;
        }
        // 原版 UserObject.AttemptAction(Moving) 在发包后立即允许下一段 Run。
        _canRun = true;
        if (AutoLoginArgs.RunningTest)
            GD.Print($"[RunningTest] SEND distance={distance} running={running} direction={direction} predicted=({predicted.X},{predicted.Y})");
        if (AutoLoginArgs.RightRunTest)
            GD.Print($"[RightRunTest] SEND distance={distance} running={running} direction={direction} predicted=({predicted.X},{predicted.Y})");
    }

    private int GetRunSteps()
    {
        // 原版规则：站立后的第一段先走；后续只有背包和穿戴均未超重才跑。
        bool cooldownOk = Godot.Time.GetTicksMsec() >= _runCooldownUntilMs;
        bool bagOk = BagWeight <= _playerStats[Stat.BagWeight];
        bool wearOk = WearWeight <= _playerStats[Stat.WearWeight];
        int steps = cooldownOk && _canRun && bagOk && wearOk ? 2 : 1;
        if (steps == 1 && IsRunInputHeld())
            GD.Print($"[RunDebug] BLOCKED canRun={_canRun} cooldownOk={cooldownOk} bag={BagWeight}/{_playerStats[Stat.BagWeight]} wear={WearWeight}/{_playerStats[Stat.WearWeight]}");
        if (steps > 1 && _playerHorse != Library.HorseType.None) steps++;
        return steps;
    }

    private int GetTargetRunSteps()
    {
        // 点击怪物后的自动追击由目标状态维持，不要求右键继续按住。
        // 第一段由 CombatController 固定走一格；从第二段开始只检查原版
        // 跑步条件（冷却、负重、坐骑），否则仍退回一格移动。
        bool cooldownOk = Godot.Time.GetTicksMsec() >= _runCooldownUntilMs;
        bool bagOk = BagWeight <= _playerStats[Stat.BagWeight];
        bool wearOk = WearWeight <= _playerStats[Stat.WearWeight];
        int steps = cooldownOk && bagOk && wearOk ? 2 : 1;
        if (steps > 1 && _playerHorse != Library.HorseType.None) steps++;
        return steps;
    }

    private bool IsRunInputHeld()
        => _runningTestRightHeld || _autoRun || Input.IsMouseButtonPressed(MouseButton.Right);

    private MirDirection FindRunningTestDirection(int distance)
    {
        // Use cardinal directions first: the legacy server's multi-cell move
        // validation treats diagonal segments differently, while right-run
        // itself must still be verified with the authoritative distance.
        int[] directions = { 0, 2, 4, 6, 1, 3, 5, 7 };
        foreach (int direction in directions)
        {
            var dir = (MirDirection)direction;
            bool open = true;
            for (int step = 1; step <= distance; step++)
            {
                var point = Functions.Move(_playerLocation, dir, step);
                if (point.X < 0 || point.Y < 0 || point.X >= _mapView.Map.Width || point.Y >= _mapView.Map.Height ||
                    _mapView.Map.Cells[point.X, point.Y].Flag)
                {
                    open = false;
                    break;
                }
            }
            if (open) return dir;
        }
        return _playerDirection;
    }

    private void LoadPlayerMap() => LoadPlayerMap(clearObjects: true);

    private void LoadPlayerMap(bool clearObjects)
    {
        var mapInfo = Globals.MapInfoList?.Binding.FirstOrDefault(m => m.Index == _playerMapIndex);
        if (mapInfo == null)
        {
            GD.PrintErr($"[Game] 找不到地图: MapIndex={_playerMapIndex}");
            _statusLabel.Text = string.Format(Lang.GameUi597Label, _playerMapIndex);
            return;
        }

        GD.Print($"[Game] 加载地图: MapIndex={_playerMapIndex} -> {mapInfo.FileName} ({mapInfo.Description})");
        Weather weather = ResolveMapWeather(mapInfo);
        GD.Print($"[Light] map={mapInfo.FileName} setting={mapInfo.Light} weather={weather} dayTime={DayTime:0.###}");
        _mapView.LoadMap(mapInfo.FileName, mapInfo.Background);
        _lightLayer?.SetMap(mapInfo, _mapView);
        _lightLayer?.SetDayTime(DayTime);
        _weatherLayer?.SetWeather(weather);
        _weatherLayer?.SetEnabled(DrawWeather);

        // M12: 小地图/大地图换图 (清动态标记, 重建静态 NPC/出口)
        if (_mapView.Map != null)
        {
            _miniMap?.SetMap(mapInfo, _mapView.Map.Width, _mapView.Map.Height, _playerObjectID);
            _bigMap?.SetMap(mapInfo, _mapView.Map.Width, _mapView.Map.Height, _playerObjectID, isCurrentMap: true);
        }

        // 换图: 清空旧地图的周围物体 (首次进图时 _objects 里是 Drain 的新图对象, 不清)
        if (clearObjects)
        {
            _combatController?.RemoveObjectReference(_combatController.TargetObject?.ObjectID ?? 0);
            foreach (var glows in _itemGlows.Values)
                foreach (var glow in glows) glow.QueueFree();
            _itemGlows.Clear();
            foreach (var ob in _objects.Values)
                ob.QueueFree();
            _objects.Clear();
            foreach (var player in _otherPlayers.Values) player.QueueFree();
            _otherPlayers.Clear();
            _combatController.TargetObject = null;
            _combatController.MouseObject = null;
            ClearMagicLock();
        }
        else
        {
            // 首次进图: SetMap 已清动态标记, 补回 Drain 阶段加过的
            foreach (var ob in _objects.Values)
            {
                _miniMap?.UpdateObject(ob.ObjectID, ob.CellX, ob.CellY, ob.Type);
                _bigMap?.UpdateObject(ob.ObjectID, ob.CellX, ob.CellY, ob.Type);
            }
        }

        _player.CellX = _playerLocation.X;
        _player.CellY = _playerLocation.Y;
        _player.UpdateAppearance(_pendingStartInfo ?? StartInfo);
        UpdatePlayerPosition();

        // 地图与相机精准定位完成后，平滑淡出并解耦进图黑幕
        if (_startupCoverRect != null && IsInstanceValid(_startupCoverRect))
        {
            var tween = CreateTween();
            tween.TweenProperty(_startupCoverRect, "color:a", 0f, 0.25f);
            tween.TweenCallback(Callable.From(() =>
            {
                if (_coverLayer != null && IsInstanceValid(_coverLayer))
                {
                    _coverLayer.QueueFree();
                    _coverLayer = null;
                    _startupCoverRect = null;
                }
            }));
        }

        if (AutoLoginArgs.ScreenshotAfterEnter)
            GetTree().CreateTimer(1.0).Timeout += SaveProductionAuditScreenshot;
    }

    private void SaveProductionAuditScreenshot()
    {
        if (!IsInsideTree() || _mapView?.Map == null)
        {
            GD.PrintErr("[ProductionScreenshot] FAIL game scene is not ready");
            GetTree().Quit();
            return;
        }

        if (DisplayServer.GetName() == "headless")
        {
            GD.PrintErr("[ProductionScreenshot] FAIL viewport image is unavailable (headless renderer)");
            GetTree().Quit();
            return;
        }

        var texture = GetViewport().GetTexture();
        var image = texture?.GetImage();
        if (image == null)
        {
            // Headless/dummy renderer 可以提供 Texture RID，但没有可读回的 Image。
            // 生产截图只在有实际渲染后端时成立，不能让回调抛 NullReferenceException。
            GD.PrintErr("[ProductionScreenshot] FAIL viewport image is unavailable (headless renderer)");
            GetTree().Quit();
            return;
        }
        const string output = "/tmp/zircon-game-audit.png";
        image.SavePng(output);
        GD.Print($"[ProductionScreenshot] PASS map={_playerMapIndex} instance={_playerInstanceIndex} " +
            $"viewport={image.GetWidth()}x{image.GetHeight()} path={output}");
        GetTree().Quit();
    }

    private static Weather ResolveMapWeather(MapInfo mapInfo)
        => mapInfo?.Weather ?? Weather.None;

    public override void _Notification(int what)
    {
        // 后台播放声音：失焦且未开启时 Master 静音，回焦恢复（设置页"后台播放声音"消费端）
        if (what == NotificationApplicationFocusOut && !ClientSettings.SoundInBackground)
            AudioServer.SetBusMute(0, true);
        else if (what == NotificationApplicationFocusIn)
        {
            // 失焦时静音的是 Master，总线恢复必须先解除 Master mute；
            // ApplyAudioSettings 只重设五条分类子总线，不会自动解除 Master。
            AudioServer.SetBusMute(0, false);
            ClientSettings.ApplyAudioSettings();
        }
        base._Notification(what);
    }

    public override void _Process(double delta)
    {
        // 高 DPI/窗口模式下，Godot 可能在 _Ready 之后才提交最终视口尺寸。
        // 若只在 _Ready/Control.Resized 布局，HUD 会保留旧的 (13,23) 等逻辑坐标，
        // 表现为技能栏跑到左上角、底栏与聊天控件互相错层。尺寸或缩放真正变化时
        // 重新计算一次全部 HUD 锚点；稳定帧不重复改写用户拖动的位置。
        Vector2 hudViewport = GetHudViewportSize();
        if (hudViewport != _lastHudViewport || !Mathf.IsEqualApprox(UiScale, _lastHudScale))
        {
            _lastHudViewport = hudViewport;
            _lastHudScale = UiScale;
            RefreshUiScale();
            LayoutHud();
        }
        else if (_uiLayer != null && IsInstanceValid(_uiLayer))
        {
            // 窗口拖动/缩放也必须实时受边界约束，而不是只在重排时约束。
            ClampHudControlsToViewport(hudViewport / UiScale);
        }

        // 深渊黑视期间的环绕特效循环; 死亡红染优先于深渊(原版同序)。
        if (_abyssVisionActive && !(_player?.Dead ?? false))
        {
            _abyssEffectTimer -= delta;
            if (_abyssEffectTimer <= 0)
            {
                SpawnAbyssVisionEffect();
                _abyssEffectTimer = AbyssEffectIntervalSeconds;
            }
        }

        // 原版 ProcessInput 第一步：MagicAction 队列在动作边界释放。
        // 释放条件：行动作已结束（当前帧不在移动动画），或超过走完期限
        // （覆盖自动寻路连续走、被击退打断等边界）。
        if (_pendingMagicPacket != null && CanPlayerTurn())
        {
            bool walking = IsPlayerWalking();
            if (!walking || Godot.Time.GetTicksMsec() >= _pendingMagicCastAtMs)
            {
                if (_net?.Connection?.Connected == true)
                    _net.Connection.Enqueue(_pendingMagicPacket);
                else
                    GD.Print("[Magic] 排队技能未发送：连接已断开");
                _pendingMagicPacket = null;
            }
        }
        TryContinueMining();
        ProcessPendingAutoPathMove();
        ProcessGroundItemPickup();
        UpdateViewRange();
        // Remote PlayerRenderer advances movement offsets in its own _Process;
        // refresh both the rendered node and its hit proxy in the same frame.
        foreach (var remotePlayer in _otherPlayers.Values)
            UpdateOtherPlayerPosition(remotePlayer);

        if (AutoLoginArgs.InteractionAudit && !_interactionAuditStarted && _startGameShown && _mapView?.Map != null
            && _objects.Values.Any(x => x?.Type == ObjectRenderer.Kind.NPC)
            && _objects.Values.Any(x => x?.Type == ObjectRenderer.Kind.Player))
        {
            _interactionAuditStarted = true;
            GetTree().CreateTimer(1.0).Timeout += StartInteractionAudit;
        }
        if (AutoLoginArgs.InteractionAudit && _interactionAuditDeadline > 0
            && Godot.Time.GetTicksMsec() >= _interactionAuditDeadline)
        {
            _interactionAuditDeadline = 0;
            bool pass = _interactionNpcSent && _interactionInspectSent == 1 && _interactionInspectReceived > 0;
            GD.Print($"[InteractionAudit] RESULT npcSent={_interactionNpcSent} ctrlLeftSent={_interactionInspectLeftSent} ctrlRightSent={_interactionInspectSent} inspectReceived={_interactionInspectReceived} pass={pass}");
            GetTree().Quit();
        }

        if ((AutoLoginArgs.RunningTest || AutoLoginArgs.RightRunTest) && !_runningTestStarted && _startGameShown && _mapView?.Map != null)
        {
            _runningTestStarted = true;
            GetTree().CreateTimer(1.0).Timeout += (AutoLoginArgs.RightRunTest ? StartRightRunTest : StartRunningTest);
        }

        if (AutoLoginArgs.OperationAudit && !_operationAuditStarted && _startGameShown && _mapView?.Map != null)
        {
            _operationAuditStarted = true;
            GetTree().CreateTimer(1.0).Timeout += StartOperationAudit;
        }

        if (AutoLoginArgs.OperationAuditExt && !_operationAuditExtStarted && _startGameShown && _mapView?.Map != null)
        {
            _operationAuditExtStarted = true;
            GetTree().CreateTimer(1.0).Timeout += StartOperationAuditExt;
        }

        // M9: 拿起物品跟随鼠标 + 悬浮提示
        UpdateMouseItem();

        // 旧版 GameScene.cs:1073-1079: Ctrl 按住 + 悬停地面物品 -> 显示物品名 (MouseItem)。
        // Godot: _hoverLabel 复用, Ctrl 松开即清。
        if (_combatController?.MouseObject?.Type == ObjectRenderer.Kind.Item
            && Input.IsKeyPressed(Key.Ctrl))
        {
            _hoverLabel.Visible = true;
            _hoverLabel.Text = _combatController.MouseObject.DisplayName;
            FitHoverLabelSize();
            var p = GetGlobalMousePosition() / UiScale;
            _hoverLabel.Position = new Vector2(p.X + 14, p.Y + 10);
        }
        else if (_hoverItem == null)
        {
            _hoverLabel.Visible = false;
        }
        if (_combatController != null)
        {
            RefreshGroundItemLabels();
            var hoveredMonster = _combatController.MouseObject?.Type == ObjectRenderer.Kind.Monster ? _combatController.MouseObject : null;
            _monsterDialog?.SetMonster(hoveredMonster);
            _monsterDialog?.Refresh();
            foreach (var ob in _objects.Values)
            {
                bool focused = ob.Type == ObjectRenderer.Kind.Item && ob == _combatController.MouseObject;
                bool nameHovered = ob == _combatController.MouseObject;
                if (ob.Focused != focused)
                {
                    ob.Focused = focused;
                    ob.QueueRedraw();
                }
                if (ob.NameHovered != nameHovered)
                {
                    ob.NameHovered = nameHovered;
                    ob.QueueRedraw();
                }

                bool highlighted = ClientSettings.ShowTargetOutline
                    && ob == _combatController.MouseObject
                    && ob.Type == ObjectRenderer.Kind.NPC;
                Color outline = ob.Type == ObjectRenderer.Kind.Monster
                    ? GetTargetOutlineColour(ob)
                    : highlighted ? GetTargetOutlineColour(ob) : Colors.Transparent;
                if (ob.TargetHighlighted != highlighted || ob.TargetOutlineColour != outline)
                {
                    ob.TargetHighlighted = highlighted;
                    ob.TargetOutlineColour = outline;
                    ob.QueueRedraw();
                }
            }

            uint hoveredPlayerId = _combatController.MouseObject?.Type == ObjectRenderer.Kind.Player
                ? _combatController.MouseObject.ObjectID : 0;
            bool localPlayerNameHovered = _player != null
                && _combatController.MouseCell() == _playerLocation
                && _combatController.MouseObject == null;
            if (_player != null && _player.NameHovered != localPlayerNameHovered)
            {
                _player.NameHovered = localPlayerNameHovered;
                _player.QueueRedraw();
            }
            foreach (var pair in _otherPlayers)
            {
                bool highlighted = ClientSettings.ShowTargetOutline && pair.Key == hoveredPlayerId;
                Color outline = highlighted
                    ? (_groupDialog?.IsMember(pair.Key) == true
                        ? ClientSettings.TargetPlayerFriendlyColour
                        : ClientSettings.TargetPlayerEnemyColour)
                    : Colors.Transparent;
                var player = pair.Value;
                bool nameHovered = pair.Key == hoveredPlayerId;
                if (player.NameHovered != nameHovered)
                {
                    player.NameHovered = nameHovered;
                    player.QueueRedraw();
                }
                if (player.TargetHighlighted != highlighted || player.TargetOutlineColour != outline)
                {
                    player.TargetHighlighted = highlighted;
                    player.TargetOutlineColour = outline;
                    player.QueueRedraw();
                }
            }
        }

        if (_debugLabel != null)
        {
            _debugLabel.Visible = ClientSettings.DebugLabel;
            if (_debugLabel.Visible)
            {
                string map = Globals.MapInfoList?.Binding.FirstOrDefault(x => x.Index == _playerMapIndex)?.FileName ?? $"Map{_playerMapIndex}";
                string dir = _player?.Direction.ToString() ?? "?";
                _debugLabel.Text = $"FPS: {Engine.GetFramesPerSecond():0}  Map: {map}  Pos: ({_playerLocation.X},{_playerLocation.Y})  Dir: {dir}";
            }
        }
        if (_statusLabel != null)
            _statusLabel.Visible = ClientSettings.DebugLabel;

        // M11: 状态窗口节流刷新 (200ms)
        if (_statusWindow != null && _statusWindow.Visible && _player != null)
        {
            double nowMs = Godot.Time.GetTicksMsec();
            if (nowMs - _statusRefreshMs > 200)
            {
                _statusRefreshMs = nowMs;
                RefreshStatusWindow();
            }
        }

        // 原版 UserObject.SetAction(Standing)：站定且右键未按下时结束连续跑步状态。
        if (_moveFrameCount <= 1 && !IsRunInputHeld())
            _canRun = false;

        // 原版移动插值：权威格是终点，Offset 以本段固定的走/跑动作时长从起点回拉。
        // 位移必须使用时间轴；动画帧只负责贴图。不能在这里按 FrameIndex
        // 计算，否则旧 Zircon.ini 中 SmoothMove=false 会导致角色跳格抖动。
        if (_moveFrameCount > 1 && _player != null)
        {
            // 位移和动作必须是一个不可分割的视觉状态。网络回包/转身事件
            // 可能把人物动作切回 Standing；此时位置仍在插值，就会表现成
            // “站着飘过去”。只在动作不一致时恢复走/跑，不重置正常移动的帧起点。
            _player.EnsureMoveAnimation(
                _playerHorse != HorseType.None,
                _player.MoveDistance >= 2);

            double t = Math.Clamp((Godot.Time.GetTicksMsec() - _moveStartMs) /
                Math.Max(1.0, _moveDurationMs), 0.0, 1.0);
            double k = 1.0 - t;
            float xStep = 48f * _player.MoveDistance * (float)k;
            float yStep = 32f * _player.MoveDistance * (float)k;
            // 地图中心已经切换到目标格；用同一时间轴把背景从旧格连续
            // 滚到目标格。玩家自己的 Offset 与这里方向相反，角色保持
            // 在连续摄像机的正确位置，不再出现“人平滑、地图跳格”。
            _mapView.CameraOffset = new Vector2(
                (_player.CellX - _moveFrom.X) * 48f * (float)k,
                (_player.CellY - _moveFrom.Y) * 32f * (float)k);
            _player.OffsetX = 0f;
            _player.OffsetY = 0f;
            switch (_player.Direction)
            {
                case MirDirection.Up: _player.OffsetY = yStep; break;
                case MirDirection.UpRight: _player.OffsetX = -xStep; _player.OffsetY = yStep; break;
                case MirDirection.Right: _player.OffsetX = -xStep; break;
                case MirDirection.DownRight: _player.OffsetX = -xStep; _player.OffsetY = -yStep; break;
                case MirDirection.Down: _player.OffsetY = -yStep; break;
                case MirDirection.DownLeft: _player.OffsetX = xStep; _player.OffsetY = -yStep; break;
                case MirDirection.Left: _player.OffsetX = xStep; break;
                case MirDirection.UpLeft: _player.OffsetX = xStep; _player.OffsetY = yStep; break;
            }

            if (k <= 0.0)
            {
                _moveFrameCount = 1;
                _moveVisualLockUntilMs = 0;
                // 原版进入 Standing 时，只有右键已松开才清 CanRun。
                // 连续按住右键时必须保留 true，下一段才会从 Walking 升到 Running。
                _canRun = IsRunInputHeld();
                _player.OffsetX = 0f;
                _player.OffsetY = 0f;
                _mapView.CameraOffset = Vector2.Zero;
                _player.PlayStandingForState();
            }
            UpdatePlayerPosition();
        }
        else if (_player != null)
        {
            _player.CellX = _playerLocation.X;
            _player.CellY = _playerLocation.Y;
            _player.OffsetX = 0f;
            _player.OffsetY = 0f;
            _mapView.CameraOffset = Vector2.Zero;
            UpdatePlayerPosition();
        }
    }

    private Color GetTargetOutlineColour(ObjectRenderer ob)
    {
        if (ob == null) return Colors.Transparent;
        if (ob.Type == ObjectRenderer.Kind.NPC)
            return ClientSettings.TargetNPCColour;
        if (ob.Type != ObjectRenderer.Kind.Monster)
            return Colors.Transparent;
        if (!string.IsNullOrWhiteSpace(ob.PetOwner) && ob.PetOwner == StartInfo?.Name)
            return ClientSettings.TargetMonsterFriendlyColour;

        int levelDiff = PlayerLevel - ob.Level;
        return levelDiff > 2
            ? ClientSettings.TargetMonsterLowLevelColour
            : levelDiff >= 0
                ? ClientSettings.TargetMonsterSameLevelColour
                : ClientSettings.TargetMonsterHighLevelColour;
    }

    private void StartOperationAudit()
    {
        var source = InventoryCells.FirstOrDefault(c => c?.Item != null && !c.Locked && c.Item.Info != null);
        var target = InventoryCells.FirstOrDefault(c => c != null && c.Item == null && !c.Locked && c.Enabled);
        if (source == null || target == null)
        {
            GD.PrintErr("[OperationAudit] FAIL no movable inventory item and empty target slot");
            GetTree().Quit();
            return;
        }
        _operationAuditSourceSlot = source.Slot;
        _operationAuditTargetSlot = target.Slot;
        var equipment = EquipmentCells.FirstOrDefault(c => c?.Item != null && c.Enabled
            && c.Slot >= 0 && c.Slot < Equipment.Length
            && !c.Item.Flags.HasFlag(UserItemFlags.Marriage)
            && Functions.CorrectSlot(source.Item.Info.ItemType, (EquipmentSlot)c.Slot)
            && CanWearItem(source.Item, (EquipmentSlot)c.Slot));
        if (equipment == null)
        {
            GD.PrintErr($"[OperationAudit] FAIL no compatible occupied equipment slot itemType={source.Item.Info.ItemType} " +
                $"item={source.Item.Info.ItemName}");
            GetTree().Quit();
            return;
        }
        _operationAuditEquipmentSlot = equipment.Slot;
        _operationAuditOriginalEquipment = equipment.Item;
        _operationAuditStage = 1;
        _operationAuditResponsePending = false;
        GD.Print($"[OperationAudit] MOVE_FORWARD from={source.Slot} to={target.Slot} item={source.Item.Info.ItemName}");
        source.MoveItem(target);
        GetTree().CreateTimer(5.0).Timeout += () =>
        {
            if (_operationAuditStage != 0 && !_operationAuditResponsePending)
            {
                GD.PrintErr("[OperationAudit] FAIL timeout waiting for ItemMove response");
                GetTree().Quit();
            }
        };
    }

    private void ContinueOperationAudit()
    {
        if (!_operationAuditResponsePending) return;
        _operationAuditResponsePending = false;
        if (!_operationAuditLastSuccess)
        {
            GD.PrintErr($"[OperationAudit] FAIL stage={_operationAuditStage} server_success=false");
            GetTree().Quit();
            return;
        }

        if (_operationAuditStage == 1)
        {
            bool moved = Inventory[_operationAuditSourceSlot] == null
                && Inventory[_operationAuditTargetSlot] != null
                && !InventoryCells[_operationAuditSourceSlot].Locked
                && !InventoryCells[_operationAuditTargetSlot].Locked;
            if (!moved)
            {
                GD.PrintErr("[OperationAudit] FAIL forward local state mismatch");
                GetTree().Quit();
                return;
            }

            _operationAuditStage = 2;
            GD.Print($"[OperationAudit] MOVE_REVERSE from={_operationAuditTargetSlot} to={_operationAuditSourceSlot}");
            InventoryCells[_operationAuditTargetSlot].MoveItem(InventoryCells[_operationAuditSourceSlot]);
            return;
        }

        bool restored = Inventory[_operationAuditSourceSlot] != null
            && Inventory[_operationAuditTargetSlot] == null
            && !InventoryCells[_operationAuditSourceSlot].Locked
            && !InventoryCells[_operationAuditTargetSlot].Locked;

        if (_operationAuditStage == 2)
        {
            _operationAuditStage = 3;
            GD.Print($"[OperationAudit] UNEQUIP_EXISTING from={_operationAuditEquipmentSlot} to={_operationAuditTargetSlot}");
            EquipmentCells[_operationAuditEquipmentSlot].MoveItem(InventoryCells[_operationAuditTargetSlot]);
            return;
        }

        if (_operationAuditStage == 3)
        {
            bool movedExisting = Inventory[_operationAuditTargetSlot] != null
                && Equipment[_operationAuditEquipmentSlot] == null
                && !InventoryCells[_operationAuditSourceSlot].Locked
                && !InventoryCells[_operationAuditTargetSlot].Locked
                && !EquipmentCells[_operationAuditEquipmentSlot].Locked;
            if (!movedExisting)
            {
                GD.PrintErr("[OperationAudit] FAIL existing equipment removal mismatch");
                GetTree().Quit();
                return;
            }
            _operationAuditStage = 4;
            GD.Print($"[OperationAudit] EQUIP from={_operationAuditSourceSlot} to={_operationAuditEquipmentSlot}");
            EquipmentCells[_operationAuditEquipmentSlot].ToEquipment(InventoryCells[_operationAuditSourceSlot]);
            return;
        }

        if (_operationAuditStage == 4)
        {
            bool equipped = Inventory[_operationAuditSourceSlot] == null
                && Equipment[_operationAuditEquipmentSlot] != null
                && !EquipmentCells[_operationAuditEquipmentSlot].Locked;
            if (!equipped)
            {
                GD.PrintErr("[OperationAudit] FAIL equipment local state mismatch");
                GetTree().Quit();
                return;
            }
            _operationAuditStage = 5;
            GD.Print($"[OperationAudit] UNEQUIP from={_operationAuditEquipmentSlot} to={_operationAuditSourceSlot}");
            EquipmentCells[_operationAuditEquipmentSlot].MoveItem(InventoryCells[_operationAuditSourceSlot]);
            return;
        }

        if (_operationAuditStage == 5)
        {
            bool unequipped = Inventory[_operationAuditSourceSlot] != null
                && Equipment[_operationAuditEquipmentSlot] == null
                && !InventoryCells[_operationAuditSourceSlot].Locked
                && !EquipmentCells[_operationAuditEquipmentSlot].Locked;
            if (!unequipped)
            {
                GD.PrintErr("[OperationAudit] FAIL equipment removal mismatch");
                GetTree().Quit();
                return;
            }
            _operationAuditStage = 6;
            GD.Print($"[OperationAudit] RESTORE_EXISTING from={_operationAuditTargetSlot} to={_operationAuditEquipmentSlot}");
            EquipmentCells[_operationAuditEquipmentSlot].ToEquipment(InventoryCells[_operationAuditTargetSlot]);
            return;
        }

        if (_operationAuditStage != 6)
        {
            GD.PrintErr($"[OperationAudit] FAIL unexpected stage={_operationAuditStage}");
            GetTree().Quit();
            return;
        }

        bool equipmentRestored = restored
            && Inventory[_operationAuditTargetSlot] == null
            && ReferenceEquals(Equipment[_operationAuditEquipmentSlot], _operationAuditOriginalEquipment);
        bool equipmentSlotCanonical = Equipment[_operationAuditEquipmentSlot]?.Slot ==
            Globals.EquipmentOffSet + _operationAuditEquipmentSlot;
        var beforeFailedSort = Inventory[_operationAuditSourceSlot];
        OnItemSort(new S.ItemSort { Grid = GridType.Inventory, Success = false });
        bool failedSortPreserved = ReferenceEquals(beforeFailedSort, Inventory[_operationAuditSourceSlot]);
        OnItemSplit(new S.ItemSplit
        {
            Grid = GridType.Inventory,
            Slot = _operationAuditSourceSlot,
            Count = 1,
            Success = false,
        });
        bool failedSplitPreserved = ReferenceEquals(beforeFailedSort, Inventory[_operationAuditSourceSlot]);
        OnItemDelete(new S.ItemDelete
        {
            Grid = GridType.Inventory,
            Slot = _operationAuditSourceSlot,
            Success = false,
        });
        bool failedDeletePreserved = ReferenceEquals(beforeFailedSort, Inventory[_operationAuditSourceSlot]);
        bool pass = equipmentRestored && equipmentSlotCanonical && failedSortPreserved && failedSplitPreserved && failedDeletePreserved;
        GD.Print($"[OperationAudit] RESULT forward={pass} reverse={restored} equipmentRestored={equipmentRestored} " +
            $"equipmentSlotCanonical={equipmentSlotCanonical} " +
            $"failedSortPreserved={failedSortPreserved} failedSplitPreserved={failedSplitPreserved} " +
            $"failedDeletePreserved={failedDeletePreserved} pass={pass}");
        _operationAuditStage = 0;
        GetTree().Quit();
    }

    // ---- --operation-audit-ext: 真实服务器矩阵 (B2 使用解锁 / B5 锁定 / C2 双戒指双手镯 / D1 腰带 / D3 自动药水) ----
    // 每阶段驱动真实 UI 交互 -> 等服务端回包 (或短延迟确认持久化) -> 断言解锁/状态。
    private void StartOperationAuditExt()
    {
        var potion = InventoryCells.FirstOrDefault(c => c?.Item != null && !c.Locked && c.Item.Info != null
            && c.Item.Info.CanAutoPot && c.Item.Info.ItemType == ItemType.Consumable && c.Item.Count > 0);
        if (potion == null)
        {
            foreach (var c in InventoryCells.Where(c => c?.Item != null))
                GD.Print($"[OperationAuditExt] inventory slot={c.Slot} item={c.Item.Info?.ItemName} count={c.Item.Count} canAutoPot={c.Item.Info?.CanAutoPot} effect={c.Item.Info?.ItemEffect}");
            GD.PrintErr("[OperationAuditExt] FAIL no auto-potion consumable in inventory");
            GetTree().Quit();
            return;
        }
        var empty = InventoryCells.FirstOrDefault(c => c != null && c.Item == null && !c.Locked && c.Enabled);
        if (empty == null)
        {
            GD.PrintErr("[OperationAuditExt] FAIL no empty inventory slot");
            GetTree().Quit();
            return;
        }
        var lockCell = InventoryCells.FirstOrDefault(c => c?.Item != null && !c.Locked && c.Item.Info != null
            && !c.Item.Flags.HasFlag(UserItemFlags.Locked));
        if (lockCell == null)
        {
            GD.PrintErr("[OperationAuditExt] FAIL no unlockable inventory item");
            GetTree().Quit();
            return;
        }

        _operationAuditExtStage = 1;
        _operationAuditExtResponsePending = false;
        _operationAuditExtLastSuccess = false;
        _operationAuditExtSlotA = potion.Slot;
        GD.Print($"[OperationAuditExt] S1 USE_POTION slot={potion.Slot} item={potion.Item.Info.ItemName} count={potion.Item.Count} durability={potion.Item.Info.Durability}");
        // 双击使用背包药水: 真实发 C.ItemUse -> 服务端 S.ItemChanged (扣 1) -> OnItemChanged 解锁
        potion.UseItem();
        GD.Print($"[OperationAuditExt] S1 use-sent-cooldown-now={UseItemTime - Godot.Time.GetTicksMsec()}");
        // 看门狗: 阶段感知 + 每次阶段推进重新武装, 60s 内同阶段无回包且无挂起才判定挂起
        // (S17b 会等待 S1 药水的 2s 冷却再发包, 期间 pending=false — 旧看门狗会误报)
        ArmOperationAuditExtWatchdog();
    }

    private void ArmOperationAuditExtWatchdog()
    {
        int stageAtCreate = _operationAuditExtStage;
        GetTree().CreateTimer(60.0).Timeout += () =>
        {
            if (_operationAuditExtStage == 0) return;                 // 审计已完成
            if (_operationAuditExtStage != stageAtCreate)             // 已推进到新阶段: 重新武装
            {
                ArmOperationAuditExtWatchdog();
                return;
            }
            if (!_operationAuditExtResponsePending)
            {
                GD.PrintErr($"[OperationAuditExt] FAIL timeout waiting for stage {_operationAuditExtStage} response");
                GetTree().Quit();
            }
        };
    }

    private void ContinueOperationAuditExt()
    {
        if (!_operationAuditExtResponsePending) return;
        _operationAuditExtResponsePending = false;
        // S18c 预期服务端拒绝 (不同物品 merge 打占用的槽) -> !Success 即 PASS, 由 case 18 判定
        if (!_operationAuditExtLastSuccess
            && !(_operationAuditExtStage == 18 && _operationAuditExtGuildSubStage == 2))
        {
            GD.PrintErr($"[OperationAuditExt] FAIL stage={_operationAuditExtStage} server_success=false");
            GetTree().Quit();
            return;
        }

        switch (_operationAuditExtStage)
        {
            case 1:
            {
                // S1 使用药水: 回包后来源格必须解锁 (B2)
                bool unlocked = !InventoryCells[_operationAuditExtSlotA].Locked;
                GD.Print($"[OperationAuditExt] S1 use-unlock={unlocked} count={Inventory[_operationAuditExtSlotA]?.Count}");
                if (!unlocked)
                {
                    GD.PrintErr("[OperationAuditExt] FAIL potion cell still locked after ItemChanged");
                    GetTree().Quit();
                    return;
                }
                // 继续 S2: 锁定背包格
                _operationAuditExtSlotA = lockCellForExt();
                if (_operationAuditExtSlotA < 0)
                {
                    GD.PrintErr("[OperationAuditExt] FAIL no item to lock");
                    GetTree().Quit();
                    return;
                }
                _operationAuditExtStage = 2;
                GD.Print($"[OperationAuditExt] S2 LOCK slot={_operationAuditExtSlotA}");
                InventoryCells[_operationAuditExtSlotA].ToggleLock();
                return;
            }
            case 2:
            {
                // S2 锁定: 服务端 S.ItemLock 回包后 Flags.Locked 必须置位 (B5)
                bool locked = Inventory[_operationAuditExtSlotA]?.Flags.HasFlag(UserItemFlags.Locked) == true;
                GD.Print($"[OperationAuditExt] S2 locked={locked}");
                if (!locked)
                {
                    GD.PrintErr("[OperationAuditExt] FAIL item not locked after ItemLock response");
                    GetTree().Quit();
                    return;
                }
                _operationAuditExtStage = 3;
                GD.Print($"[OperationAuditExt] S3 UNLOCK slot={_operationAuditExtSlotA}");
                InventoryCells[_operationAuditExtSlotA].ToggleLock();
                return;
            }
            case 3:
            {
                // S3 解锁 (B5 反向)
                bool unlocked = Inventory[_operationAuditExtSlotA]?.Flags.HasFlag(UserItemFlags.Locked) != true;
                GD.Print($"[OperationAuditExt] S3 unlocked={unlocked}");
                if (!unlocked)
                {
                    GD.PrintErr("[OperationAuditExt] FAIL item still locked after unlock");
                    GetTree().Quit();
                    return;
                }
                // S4: 卸下 RingL 到空背包格 (C4)
                var ringL = EquipmentCells.FirstOrDefault(c => c?.Item != null && c.Slot == (int)EquipmentSlot.RingL);
                var emptyInv = InventoryCells.FirstOrDefault(c => c != null && c.Item == null && !c.Locked && c.Enabled);
                if (ringL == null || emptyInv == null)
                {
                    GD.PrintErr("[OperationAuditExt] FAIL no RingL equipment or no empty inventory slot for C4");
                    GetTree().Quit();
                    return;
                }
                _operationAuditExtFromSlot = ringL.Slot;     // 7
                _operationAuditExtToSlot = emptyInv.Slot;    // 背包空槽
                _operationAuditExtSlotB = emptyInv.Slot;
                _operationAuditExtOriginalA = ringL.Item;
                _operationAuditExtStage = 4;
                GD.Print($"[OperationAuditExt] S4 UNEQUIP_RINGL eqSlot={ringL.Slot} to={emptyInv.Slot}");
                ringL.MoveItem(emptyInv);
                return;
            }
            case 4:
            {
                // S4 卸下 RingL: 装备空、背包有 (C4 部分)
                bool removed = Equipment[(int)EquipmentSlot.RingL] == null
                    && Inventory[_operationAuditExtSlotB] != null
                    && !EquipmentCells[(int)EquipmentSlot.RingL].Locked
                    && !InventoryCells[_operationAuditExtSlotB].Locked;
                GD.Print($"[OperationAuditExt] S4 removed={removed}");
                if (!removed)
                {
                    GD.PrintErr("[OperationAuditExt] FAIL ring not removed to inventory");
                    GetTree().Quit();
                    return;
                }
                // S5: 穿到 RingR (已占用) -> 替换路径 (C2 replace)
                _operationAuditExtFromSlot = _operationAuditExtSlotB;  // 背包
                _operationAuditExtToSlot = (int)EquipmentSlot.RingR;   // 8
                _operationAuditExtOriginalB = Equipment[(int)EquipmentSlot.RingR];
                _operationAuditExtStage = 5;
                GD.Print($"[OperationAuditExt] S5 EQUIP_RINGR_REPLACE from={_operationAuditExtSlotB} to={EquipmentSlot.RingR}");
                EquipmentCells[(int)EquipmentSlot.RingR].ToEquipment(InventoryCells[_operationAuditExtSlotB]);
                return;
            }
            case 5:
            {
                // S5 替换: RingR 有换入物品, 被换下的原 RingR 物品回到背包 from 槽 (C2 replace)
                bool replaced = Equipment[(int)EquipmentSlot.RingR] != null
                    && !ReferenceEquals(Equipment[(int)EquipmentSlot.RingR], _operationAuditExtOriginalB)
                    && !EquipmentCells[(int)EquipmentSlot.RingR].Locked;
                GD.Print($"[OperationAuditExt] S5 replaced={replaced} ringR={Equipment[(int)EquipmentSlot.RingR]?.Info?.ItemName}");
                if (!replaced)
                {
                    GD.PrintErr("[OperationAuditExt] FAIL ring replace on RingR failed");
                    GetTree().Quit();
                    return;
                }
                // S6: 从背包 (原 RingR 物品所在) 穿到 RingL 空槽 -> 空槽优先路径 (C2 empty)
                _operationAuditExtFromSlot = _operationAuditExtSlotB;
                _operationAuditExtToSlot = (int)EquipmentSlot.RingL;   // 7
                _operationAuditExtStage = 6;
                GD.Print($"[OperationAuditExt] S6 EQUIP_RINGL_EMPTY from={_operationAuditExtSlotB} to={EquipmentSlot.RingL}");
                EquipmentCells[(int)EquipmentSlot.RingL].ToEquipment(InventoryCells[_operationAuditExtSlotB]);
                return;
            }
            case 6:
            {
                // S6 空槽优先: RingL 有, 背包 from 槽空 (C2 empty priority)
                bool equipped = Equipment[(int)EquipmentSlot.RingL] != null
                    && Inventory[_operationAuditExtSlotB] == null
                    && !EquipmentCells[(int)EquipmentSlot.RingL].Locked;
                GD.Print($"[OperationAuditExt] S6 equipped-empty={equipped} ringL={Equipment[(int)EquipmentSlot.RingL]?.Info?.ItemName}");
                if (!equipped)
                {
                    GD.PrintErr("[OperationAuditExt] FAIL ring empty-slot equip failed");
                    GetTree().Quit();
                    return;
                }
                // S7: 卸下 BraceletL (C4 手镯)
                var braceL = EquipmentCells.FirstOrDefault(c => c?.Item != null && c.Slot == (int)EquipmentSlot.BraceletL);
                var emptyInv2 = InventoryCells.FirstOrDefault(c => c != null && c.Item == null && !c.Locked && c.Enabled);
                if (braceL == null || emptyInv2 == null)
                {
                    GD.PrintErr("[OperationAuditExt] FAIL no BraceletL or empty slot for C4 bracelet");
                    GetTree().Quit();
                    return;
                }
                _operationAuditExtFromSlot = braceL.Slot;     // 5
                _operationAuditExtToSlot = emptyInv2.Slot;    // 背包空槽
                _operationAuditExtSlotB = emptyInv2.Slot;
                _operationAuditExtOriginalA = braceL.Item;
                _operationAuditExtStage = 7;
                GD.Print($"[OperationAuditExt] S7 UNEQUIP_BRACELETL eqSlot={braceL.Slot} to={emptyInv2.Slot}");
                braceL.MoveItem(emptyInv2);
                return;
            }
            case 7:
            {
                // S7 卸下 BraceletL: 装备空 (C4 手镯)
                bool removed = Equipment[(int)EquipmentSlot.BraceletL] == null
                    && Inventory[_operationAuditExtSlotB] != null
                    && !EquipmentCells[(int)EquipmentSlot.BraceletL].Locked;
                GD.Print($"[OperationAuditExt] S7 bracelet-removed={removed}");
                if (!removed)
                {
                    GD.PrintErr("[OperationAuditExt] FAIL bracelet not removed");
                    GetTree().Quit();
                    return;
                }
                // S8: 穿到 BraceletR (已占用) -> 替换路径 (C2 bracelet replace)
                _operationAuditExtFromSlot = _operationAuditExtSlotB;
                _operationAuditExtToSlot = (int)EquipmentSlot.BraceletR;  // 6
                _operationAuditExtOriginalB = Equipment[(int)EquipmentSlot.BraceletR];
                _operationAuditExtStage = 8;
                GD.Print($"[OperationAuditExt] S8 EQUIP_BRACELETR_REPLACE from={_operationAuditExtSlotB} to={EquipmentSlot.BraceletR}");
                EquipmentCells[(int)EquipmentSlot.BraceletR].ToEquipment(InventoryCells[_operationAuditExtSlotB]);
                return;
            }
            case 8:
            {
                // S8 替换: BraceletR 有换入物品 (C2 bracelet replace)
                bool replaced = Equipment[(int)EquipmentSlot.BraceletR] != null
                    && !ReferenceEquals(Equipment[(int)EquipmentSlot.BraceletR], _operationAuditExtOriginalB)
                    && !EquipmentCells[(int)EquipmentSlot.BraceletR].Locked;
                GD.Print($"[OperationAuditExt] S8 bracelet-replaced={replaced}");
                if (!replaced)
                {
                    GD.PrintErr("[OperationAuditExt] FAIL bracelet replace failed");
                    GetTree().Quit();
                    return;
                }
                // S9: 腰带链接 (D1) — 拖背包物品到空腰带格
                var linkable = InventoryCells.FirstOrDefault(c => c?.Item != null && !c.Locked && c.Item.Info != null);
                var beltCell = _beltDialog?.Grid?.Cells?.FirstOrDefault(c => c?.QuickItem == null && c.QuickInfo == null);
                if (linkable == null || beltCell == null)
                {
                    GD.PrintErr("[OperationAuditExt] FAIL no linkable inventory item or empty belt slot for D1");
                    GetTree().Quit();
                    return;
                }
                _operationAuditExtSlotA = linkable.Slot;
                _operationAuditExtSlotB = beltCell.Slot;
                _operationAuditExtOriginalA = linkable.Item;
                _operationAuditExtStage = 9;
                GD.Print($"[OperationAuditExt] S9 BELT_LINK from={linkable.Slot} to=belt={beltCell.Slot} item={linkable.Item.Info.ItemName}");
                linkable.MoveItem(beltCell);
                // 无回包: 本地即时断言; 服务端持久化由下一轮审计 S.CharacterInfo 重载验证
                _operationAuditExtResponsePending = true;
                ContinueOperationAuditExt();
                return;
            }
            case 9:
            {
                // S9 腰带链接: BeltLinks[slot] 已指向物品 Index (D1 本地即时)
                bool linked = BeltLinks[_operationAuditExtSlotB].LinkItemIndex == _operationAuditExtOriginalA.Index
                    || BeltLinks[_operationAuditExtSlotB].LinkInfoIndex > 0;
                GD.Print($"[OperationAuditExt] S9 belt-linked={linked} linkItem={BeltLinks[_operationAuditExtSlotB].LinkItemIndex} expect={_operationAuditExtOriginalA.Index}");
                if (!linked)
                {
                    GD.PrintErr("[OperationAuditExt] FAIL belt link not established");
                    GetTree().Quit();
                    return;
                }
                // S10: 自动药水链接 (D3) — 拖药水到 AutoPotion 行
                var autoCell = AutoPotionBox?.Rows?.FirstOrDefault(r => r?.ItemCell?.QuickInfo == null);
                var potion2 = InventoryCells.FirstOrDefault(c => c?.Item != null && !c.Locked
                    && c.Item.Info.CanAutoPot && c.Item.Info.ItemType == ItemType.Consumable);
                if (autoCell == null || potion2 == null)
                {
                    GD.PrintErr("[OperationAuditExt] FAIL no auto-potion row or potion for D3");
                    GetTree().Quit();
                    return;
                }
                _operationAuditExtSlotA = potion2.Slot;
                _operationAuditExtSlotB = autoCell.Slot;
                _operationAuditExtOriginalB = potion2.Item;
                _operationAuditExtStage = 10;
                GD.Print($"[OperationAuditExt] S10 AUTO_POTION from={potion2.Slot} to=row={autoCell.Slot} item={potion2.Item.Info.ItemName}");
                potion2.MoveItem(autoCell.ItemCell);
                _operationAuditExtResponsePending = true;
                ContinueOperationAuditExt();
                return;
            }
            case 10:
            {
                // S10 自动药水链接: AutoPotionBox.Rows[slot].ItemCell.QuickInfo == 药水 Info (D3)
                var row = AutoPotionBox.Rows[_operationAuditExtSlotB];
                bool linked = ReferenceEquals(row.ItemCell.QuickInfo, _operationAuditExtOriginalB.Info);
                GD.Print($"[OperationAuditExt] S10 auto-linked={linked} info={row.ItemCell.QuickInfo?.ItemName}");
                if (!linked)
                {
                    GD.PrintErr("[OperationAuditExt] FAIL auto-potion link not established");
                    GetTree().Quit();
                    return;
                }
                // S11: 清理
                CleanupOperationAuditExt();
                return;
            }
            case 11:
            {
                // S11 清理完成
                bool ringsRestored = Equipment[(int)EquipmentSlot.RingL] != null && Equipment[(int)EquipmentSlot.RingR] != null;
                bool braceletsRestored = Equipment[(int)EquipmentSlot.BraceletL] != null && Equipment[(int)EquipmentSlot.BraceletR] != null;
                bool beltCleared = !BeltLinks.Any(l => l.LinkItemIndex > 0 || l.LinkInfoIndex > 0);
                bool autoCleared = !AutoPotionBox.Links.Any(l => l.LinkInfoIndex > 0);
                GD.Print($"[OperationAuditExt] S11 cleanup rings={ringsRestored} bracelets={braceletsRestored} " +
                    $"beltCleared={beltCleared} autoCleared={autoCleared}");
                if (!(ringsRestored && braceletsRestored && beltCleared && autoCleared))
                {
                    GD.PrintErr("[OperationAuditExt] FAIL cleanup incomplete");
                    GetTree().Quit();
                    return;
                }
                // S13 (E4 邮件): 管理员登录下自寄带附件邮件 -> 等 S.MailSend 阶段回包 +
                // S.ItemsChanged(成功扣量解锁) + S.MailNew(收件方=自己 -> 同连接, 列表刷新)
                var mailPotion = InventoryCells.FirstOrDefault(c => c?.Item != null && !c.Locked && c.Item.Info != null
                    && c.Item.Info.CanAutoPot && c.Item.Info.ItemType == ItemType.Consumable && c.Item.Count > 0);
                if (mailPotion == null)
                {
                    GD.PrintErr("[OperationAuditExt] FAIL no potion for mail attachment");
                    GetTree().Quit();
                    return;
                }
                _auditMailCountBefore = _communicationDialog?.MailSnapshot().Count ?? -1;
                _auditMailNewReceived = false;
                _auditMailSendCount = (int)mailPotion.Item.Count;
                _operationAuditExtSlotA = mailPotion.Slot;
                _operationAuditExtOriginalA = mailPotion.Item;
                _operationAuditExtStage = 13;
                _operationAuditExtResponsePending = false;
                _operationAuditExtLastSuccess = false;
                GD.Print($"[OperationAuditExt] S13 MAIL_SEND from={mailPotion.Slot} item={mailPotion.Item.Info.ItemName} count={mailPotion.Item.Count} recipient={AutoLoginArgs.Character} mailCountBefore={_auditMailCountBefore}");
                SendMail(AutoLoginArgs.Character, "operation-audit-ext", "operation audit ext",
                    new List<CellLinkInfo> { new CellLinkInfo { GridType = GridType.Inventory, Slot = mailPotion.Slot, Count = 1 } }, 0);
                return;
            }
            case 13:
            {
                // S13 发送: S.ItemsChanged(Success) 扣量+解锁, S.MailNew 列表加 1
                bool sent = _operationAuditExtLastSuccess && _auditMailNewReceived;
                bool unlocked = !InventoryCells[_operationAuditExtSlotA].Locked;
                bool deducted = Inventory[_operationAuditExtSlotA]?.Count == (_auditMailSendCount - 1);
                int mailCount = _communicationDialog?.MailSnapshot().Count ?? -1;
                GD.Print($"[OperationAuditExt] S13 mail-sent={sent} unlocked={unlocked} deducted={deducted} count={Inventory[_operationAuditExtSlotA]?.Count} expect={_auditMailSendCount - 1} mailCount={mailCount} expect={_auditMailCountBefore + 1}");
                var mail = _communicationDialog?.MailSnapshot().FirstOrDefault(x => x.Subject == "operation-audit-ext");
                if (!(sent && unlocked && deducted && mailCount == _auditMailCountBefore + 1 && mail != null))
                {
                    GD.PrintErr("[OperationAuditExt] FAIL mail send not confirmed");
                    GetTree().Quit();
                    return;
                }
                _auditMailIndex = mail.Index;
                _operationAuditExtStage = 14;
                _operationAuditExtResponsePending = false;
                _operationAuditExtLastSuccess = false;
                GD.Print($"[OperationAuditExt] S14 MAIL_GETITEM index={_auditMailIndex} slot=0 attachments={mail.Items?.Count ?? -1}");
                SendMailGetItem(_auditMailIndex, 0);
                return;
            }
            case 14:
            {
                // S14 领取附件: S.MailItemDelete 后附件格清空, 药水经 S.ItemsGained 叠回背包
                var mail = _communicationDialog?.FindMail(_auditMailIndex);
                bool claimed = mail == null || (mail.Items?.Count ?? 0) == 0;
                bool stacked = Inventory[_operationAuditExtSlotA]?.Count == _auditMailSendCount;
                GD.Print($"[OperationAuditExt] S14 mail-claimed={claimed} attachments={mail?.Items?.Count ?? -1} stacked={stacked} count={Inventory[_operationAuditExtSlotA]?.Count} expect={_auditMailSendCount}");
                if (!(claimed && stacked))
                {
                    GD.PrintErr("[OperationAuditExt] FAIL mail attachment claim failed");
                    GetTree().Quit();
                    return;
                }
                _operationAuditExtStage = 15;
                _operationAuditExtResponsePending = false;
                _operationAuditExtLastSuccess = false;
                GD.Print($"[OperationAuditExt] S15 MAIL_DELETE index={_auditMailIndex}");
                SendMailDelete(_auditMailIndex);
                return;
            }
            case 15:
            {
                // S15 删除空邮件: S.MailDelete 后列表回到发送前数量
                bool gone = (_communicationDialog?.MailSnapshot().Count ?? -1) == _auditMailCountBefore;
                GD.Print($"[OperationAuditExt] S15 mail-deleted={gone} mailCount={_communicationDialog?.MailSnapshot().Count ?? -1} expect={_auditMailCountBefore}");
                if (!gone)
                {
                    GD.PrintErr("[OperationAuditExt] FAIL mail delete failed");
                    GetTree().Quit();
                    return;
                }
                // S17: 伙伴食物移动/使用 (C6 实服端到端), 完成后接 S18 行会仓库
                StartOperationAuditExtCompanion();
                return;
            }
            case 17:
            {
                if (_operationAuditExtCompanionSubStage == 0)
                {
                    // S17a 移动回包: 食物已在伴侣背包槽0, 背包来源槽空, 双格解锁
                    bool moved = _operationAuditExtLastSuccess
                        && CompanionInventory[_operationAuditExtToSlot]?.Info?.ItemType == ItemType.CompanionFood
                        && Inventory[_operationAuditExtFromSlot] == null
                        && !InventoryCells[_operationAuditExtFromSlot].Locked
                        && !CompanionInventoryCells[_operationAuditExtToSlot].Locked;
                    GD.Print($"[OperationAuditExt] S17 moved={moved} food={CompanionInventory[_operationAuditExtToSlot]?.Info?.ItemName ?? "empty"} count={CompanionInventory[_operationAuditExtToSlot]?.Count} hunger={Companion?.Hunger}");
                    if (!moved)
                    {
                        GD.PrintErr("[OperationAuditExt] FAIL companion food move failed");
                        GetTree().Quit();
                        return;
                    }
                    _operationAuditExtCompanionSubStage = 1;
                    _operationAuditExtCompanionItemChanged = false;
                    _operationAuditExtCompanionHungerBefore = Companion?.Hunger ?? -1;
                    _operationAuditExtCompanionHungerAfter = -1;
                    _operationAuditExtResponsePending = false;
                    _operationAuditExtLastSuccess = false;
                    GD.Print($"[OperationAuditExt] S17b USE_FOOD slot={_operationAuditExtToSlot} hungerBefore={_operationAuditExtCompanionHungerBefore}");
                    // 冷却: S1 药水使用设置了 2s 冷却 (ItemInfo.Durability), 原版客户端对
                    // CompanionFood 走同款闸门 (Client/Controls/DXItemCell.cs Consumable case) —
                    // 真玩家此时点击无效, 冷却结束后重点击即可。此处模拟: 等待冷却结束再发包。
                    var foodItem = CompanionInventory[_operationAuditExtToSlot];
                    if (IsUseItemOnCooldown(foodItem))
                    {
                        _operationAuditExtS17bRetries = 0;
                        GetTree().CreateTimer(0.25).Timeout += RetryOperationAuditExtUseCompanionFood;
                        return;
                    }
                    TryOperationAuditExtUseCompanionFood();
                    return;
                }
                // S17b 双包齐 (S.ItemChanged + S.CompanionUpdate): count-1 + 解锁 + 饥饿严格增加
                GD.Print($"[OperationAuditExt] S17b cont arr0={(CompanionInventory[_operationAuditExtToSlot] == null ? "NULL" : CompanionInventory[_operationAuditExtToSlot].Info.ItemName)} itemsN={Companion?.Items?.Count}");
                bool used = _operationAuditExtLastSuccess
                    && CompanionInventory[_operationAuditExtToSlot]?.Count == _operationAuditExtCompanionFoodCount - 1
                    && !CompanionInventoryCells[_operationAuditExtToSlot].Locked
                    && _operationAuditExtCompanionHungerAfter > _operationAuditExtCompanionHungerBefore;
                GD.Print($"[OperationAuditExt] S17 used={used} count={CompanionInventory[_operationAuditExtToSlot]?.Count} expect={_operationAuditExtCompanionFoodCount - 1} hunger={_operationAuditExtCompanionHungerBefore}->{_operationAuditExtCompanionHungerAfter} unlocked={!CompanionInventoryCells[_operationAuditExtToSlot].Locked}");
                if (!used)
                {
                    GD.PrintErr("[OperationAuditExt] FAIL companion food use failed");
                    GetTree().Quit();
                    return;
                }
                _operationAuditExtCompanionPass = true;
                StartOperationAuditExtGuild();
                return;
            }
            case 18:
            {
                if (_operationAuditExtGuildSubStage == 0)
                {
                    // S18a 建会回包 (S.GuildInfo): StorageLimit=10、GuildFunds=0、扣 7.5M、仓库页启用格=10
                    long goldAfter = Currencies.FirstOrDefault(x => x.Info?.Type == CurrencyType.Gold)?.Amount ?? -1;
                    bool limitOk = _guildDialog?.StorageLimit == 10;
                    bool fundsOk = _guildDialog?.GuildFunds == 0;
                    bool goldOk = _auditGuildGoldBefore > 0 && goldAfter == _auditGuildGoldBefore - 7_500_000;
                    _guildDialog.SelectTab(2);
                    var storageCells = _guildDialog?.GuildStorageCells;
                    int enabled = storageCells?.Count(c => c.Enabled) ?? -1;
                    GD.Print($"[OperationAuditExt] S18a created limit={_guildDialog?.StorageLimit} funds={_guildDialog?.GuildFunds} gold={_auditGuildGoldBefore}->{goldAfter} enabledCells={enabled} expect=10");
                    if (!(limitOk && fundsOk && goldOk && enabled == 10))
                    {
                        GD.PrintErr("[OperationAuditExt] FAIL guild create contract");
                        GetTree().Quit();
                        return;
                    }
                    // S18b: 生产路径移一个可交易消耗品进仓库槽0
                    var tradeItem = InventoryCells.FirstOrDefault(c => c?.Item != null && !c.Locked
                        && c.Item.Info?.CanTrade == true && c.Item.Info?.ItemType == ItemType.Consumable);
                    if (tradeItem == null || storageCells == null || storageCells.Length == 0 || !storageCells[0].IsEnabled)
                    {
                        GD.PrintErr("[OperationAuditExt] FAIL no tradeable item or storage cell");
                        GetTree().Quit();
                        return;
                    }
                    _operationAuditExtFromSlot = tradeItem.Slot;
                    _operationAuditExtToSlot = 0;
                    _operationAuditExtGuildSubStage = 1;
                    _operationAuditExtResponsePending = false;
                    _operationAuditExtLastSuccess = false;
                    GD.Print($"[OperationAuditExt] S18b MOVE_IN from={tradeItem.Slot} item={tradeItem.Item.Info.ItemName}");
                    tradeItem.MoveItem(storageCells[0]);
                    return;
                }
                if (_operationAuditExtGuildSubStage == 1)
                {
                    // S18b 回包: 仓库槽0有物品、背包来源槽空、双格解锁
                    bool moved = _operationAuditExtLastSuccess
                        && _guildDialog?.GuildStorageItems?[0] != null
                        && Inventory[_operationAuditExtFromSlot] == null
                        && !InventoryCells[_operationAuditExtFromSlot].Locked
                        && !_guildDialog.GuildStorageCells[0].Locked;
                    GD.Print($"[OperationAuditExt] S18b moved={moved} guild0={_guildDialog?.GuildStorageItems?[0]?.Info?.ItemName ?? "empty"}");
                    if (!moved)
                    {
                        GD.PrintErr("[OperationAuditExt] FAIL guild storage move-in failed");
                        GetTree().Quit();
                        return;
                    }
                    // S18c: 用不同物品 merge 打占用的槽0 -> 服务端拒绝 (toItem.Info != fromItem.Info return)
                    var failItem = InventoryCells.FirstOrDefault(c => c?.Item != null && !c.Locked
                        && c.Item.Info?.CanTrade == true && c.Item.Info != _guildDialog.GuildStorageItems[0].Info);
                    if (failItem == null)
                    {
                        GD.PrintErr("[OperationAuditExt] FAIL no second tradeable item for merge-reject");
                        GetTree().Quit();
                        return;
                    }
                    _operationAuditExtSlotB = failItem.Slot;
                    _operationAuditExtGuildSubStage = 2;
                    _operationAuditExtResponsePending = false;
                    _operationAuditExtLastSuccess = false;
                    GD.Print($"[OperationAuditExt] S18c FAIL_MERGE from={failItem.Slot} item={failItem.Item.Info.ItemName} -> guild0");
                    SendItemMove(GridType.Inventory, GridType.GuildStorage, failItem.Slot, 0, true);
                    return;
                }
                if (_operationAuditExtGuildSubStage == 2)
                {
                    // S18c 回包: 服务端拒绝 (!Success), 仓库槽0保持原物品、背包不同物品仍在、双格解锁
                    bool rejected = !_operationAuditExtLastSuccess;
                    bool storageUnchanged = _guildDialog?.GuildStorageItems?[0] != null
                        && Inventory[_operationAuditExtSlotB] != null;
                    bool unlocked = !InventoryCells[_operationAuditExtSlotB].Locked
                        && !_guildDialog.GuildStorageCells[0].Locked;
                    GD.Print($"[OperationAuditExt] S18c rejected={rejected} storage-unchanged={storageUnchanged} unlocked={unlocked}");
                    if (!(rejected && storageUnchanged && unlocked))
                    {
                        GD.PrintErr("[OperationAuditExt] FAIL guild storage merge-reject contract");
                        GetTree().Quit();
                        return;
                    }
                    // S18d: 生产路径把仓库槽0移回原背包槽
                    _operationAuditExtGuildSubStage = 3;
                    _operationAuditExtResponsePending = false;
                    _operationAuditExtLastSuccess = false;
                    GD.Print($"[OperationAuditExt] S18d MOVE_OUT guild0 -> inv{_operationAuditExtFromSlot}");
                    _guildDialog.GuildStorageCells[0].MoveItem(InventoryCells[_operationAuditExtFromSlot]);
                    return;
                }
                // S18d 回包: 背包槽恢复物品、仓库槽0空、双格解锁
                bool restored = _operationAuditExtLastSuccess
                    && Inventory[_operationAuditExtFromSlot] != null
                    && _guildDialog?.GuildStorageItems?[0] == null
                    && !InventoryCells[_operationAuditExtFromSlot].Locked
                    && !_guildDialog.GuildStorageCells[0].Locked;
                GD.Print($"[OperationAuditExt] S18d restored={restored} inv={Inventory[_operationAuditExtFromSlot]?.Info?.ItemName ?? "empty"} guild0-empty={_guildDialog?.GuildStorageItems?[0] == null}");
                if (!restored)
                {
                    GD.PrintErr("[OperationAuditExt] FAIL guild storage move-out failed");
                    GetTree().Quit();
                    return;
                }
                _operationAuditExtGuildPass = true;
                // S16: 战斗在线实测 (真实窗口 + 实服)
                StartOperationAuditExtCombat();
                return;
            }
            case 12:
            {
                // S12 还原 BraceletL: 装备槽有原手镯, 背包 from 槽空
                bool restored = Equipment[(int)EquipmentSlot.BraceletL] != null
                    && Inventory[_operationAuditExtFromSlot] == null
                    && !EquipmentCells[(int)EquipmentSlot.BraceletL].Locked;
                GD.Print($"[OperationAuditExt] S12 bracelet-restored={restored} eq={Equipment[(int)EquipmentSlot.BraceletL]?.Info?.ItemName} bagSlot{_operationAuditExtFromSlot}={Inventory[_operationAuditExtFromSlot]?.Info?.ItemName ?? "empty"}");
                if (!restored)
                {
                    GD.PrintErr("[OperationAuditExt] FAIL bracelet restore failed");
                    GetTree().Quit();
                    return;
                }
                // 清腰带链接 (D1 清理): QuickItem/QuickInfo setter 只改本地, 必须显式发服务端
                foreach (var link in BeltLinks)
                {
                    if (link.LinkItemIndex <= 0 && link.LinkInfoIndex <= 0) continue;
                    var cell = _beltDialog?.Grid?.Cells != null && link.Slot >= 0 && link.Slot < _beltDialog.Grid.Cells.Length
                        ? _beltDialog.Grid.Cells[link.Slot] : null;
                    if (cell != null)
                    {
                        cell.QuickItem = null;
                        cell.QuickInfo = null;
                        SendBeltLinkChanged(link.Slot, -1, -1);
                    }
                }
                // 清自动药水行 (D3 清理): 显式发服务端
                for (int i = 0; i < AutoPotionBox.Rows.Length; i++)
                {
                    var row = AutoPotionBox.Rows[i];
                    if (row.ItemCell.QuickInfo == null) continue;
                    row.ItemCell.QuickInfo = null;
                    row.Health.Value = 0;
                    row.Mana.Value = 0;
                    row.EnabledCheck.Checked = false;
                    AutoPotionBox.SendRowUpdate(i);
                }
                _operationAuditExtStage = 11;
                _operationAuditExtResponsePending = true;
                ContinueOperationAuditExt();
                return;
            }
            default:
                GD.PrintErr($"[OperationAuditExt] FAIL unexpected stage={_operationAuditExtStage}");
                GetTree().Quit();
                return;
        }
    }

    private int lockCellForExt()
    {
        var c = InventoryCells.FirstOrDefault(x => x?.Item != null && !x.Locked && x.Item.Info != null
            && !x.Item.Flags.HasFlag(UserItemFlags.Locked));
        return c?.Slot ?? -1;
    }

    // ---- S16 战斗在线实测 (真实窗口 + 实服): 找怪 -> 走到相邻 -> 选中攻击 ->
    // 真实 C.Attack 发包节拍 (原版公式门控) + 死亡目标保留 (D15) ----
    // S17 (C6): 伙伴食物移动/使用端到端。食物入伴侣背包槽0, 等 S.ItemMove 回包
    private void StartOperationAuditExtCompanion()
    {
        var food = InventoryCells.FirstOrDefault(c => c?.Item != null && !c.Locked
            && c.Item.Info?.ItemType == ItemType.CompanionFood);
        var target = CompanionInventoryCells.FirstOrDefault(c => c != null && c.IsEnabled && c.Item == null);
        if (food == null || target == null || Companion == null)
        {
            GD.PrintErr($"[OperationAuditExt] FAIL no companion food ({(food?.Item?.Info?.ItemName ?? "null")}) or free companion slot ({target?.Slot ?? -1}) or companion ({Companion?.Name ?? "null"})");
            GetTree().Quit();
            return;
        }
        _operationAuditExtFromSlot = food.Slot;
        _operationAuditExtToSlot = target.Slot;
        _operationAuditExtCompanionSubStage = 0;
        _operationAuditExtCompanionFoodCount = (int)food.Item.Count;
        _operationAuditExtStage = 17;
        _operationAuditExtResponsePending = false;
        _operationAuditExtLastSuccess = false;
        GD.Print($"[OperationAuditExt] S17a MOVE_FOOD from={food.Slot} item={food.Item.Info.ItemName} count={food.Item.Count} -> companion{target.Slot} hunger={Companion.Hunger}");
        food.MoveItem(target);
    }

    // S17b: 冷却结束后真实双击使用伴侣食物 (原版闸门语义: 冷却中点击静默无效, 冷却后重点击生效)
    private void TryOperationAuditExtUseCompanionFood()
    {
        if (_operationAuditExtStage != 17 || _operationAuditExtCompanionSubStage != 1) return;
        bool useSent = CompanionInventoryCells[_operationAuditExtToSlot].UseItem();
        GD.Print($"[OperationAuditExt] S17b use-sent={useSent} locked={CompanionInventoryCells[_operationAuditExtToSlot].Locked} canUse={CanUseItem(CompanionInventory[_operationAuditExtToSlot])}");
        if (!useSent)
        {
            GD.PrintErr("[OperationAuditExt] FAIL companion food use blocked after cooldown");
            GetTree().Quit();
        }
    }

    private void RetryOperationAuditExtUseCompanionFood()
    {
        if (_operationAuditExtStage != 17 || _operationAuditExtCompanionSubStage != 1) return;
        var foodItem = CompanionInventory[_operationAuditExtToSlot];
        if (foodItem == null)
        {
            GD.PrintErr("[OperationAuditExt] FAIL companion food vanished during cooldown wait");
            GetTree().Quit();
            return;
        }
        if (IsUseItemOnCooldown(foodItem))
        {
            if (++_operationAuditExtS17bRetries > 120) // 0.25s x 120 = 30s 上限
            {
                GD.PrintErr("[OperationAuditExt] FAIL companion food cooldown never cleared");
                GetTree().Quit();
                return;
            }
            GetTree().CreateTimer(0.25).Timeout += RetryOperationAuditExtUseCompanionFood;
            return;
        }
        GD.Print($"[OperationAuditExt] S17b cooldown-cleared remain={UseItemTime - Godot.Time.GetTicksMsec()}");
        TryOperationAuditExtUseCompanionFood();
    }

    // S18 (E3): 行会创建/仓库移动/失败回滚端到端
    private void StartOperationAuditExtGuild()
    {
        _auditGuildGoldBefore = Currencies.FirstOrDefault(x => x.Info?.Type == CurrencyType.Gold)?.Amount ?? -1;
        _operationAuditExtGuildSubStage = 0;
        _operationAuditExtStage = 18;
        _operationAuditExtResponsePending = false;
        _operationAuditExtLastSuccess = false;
        GD.Print($"[OperationAuditExt] S18a GUILD_CREATE name=E3AuditGuild gold={_auditGuildGoldBefore}");
        SendGuildCreate("E3AuditGuild", true, 0, 0);
    }

    private void StartOperationAuditExtCombat()
    {
        int dcMin = PlayerStats[Stat.MinDC];
        int dcMax = PlayerStats[Stat.MaxDC];
        int aspeed = PlayerStats[Stat.AttackSpeed];
        double interval = ComputeAttackIntervalMs(Globals.AttackDelay, aspeed, Globals.ASpeedRate,
            BagWeight > PlayerStats[Stat.BagWeight], _playerPoison.HasFlag(PoisonType.Neutralize));
        GD.Print($"[OperationAuditExt] S16 COMBAT dc={dcMin}-{dcMax} as={aspeed} interval={interval}ms loc={_playerLocation}");

        var monsters = _objects.Values
            .Where(o => o.Type == ObjectRenderer.Kind.Monster && !o.Dead
                && string.IsNullOrWhiteSpace(o.PetOwner) && (o.MonsterInfo?.AI ?? -1) >= 0)
            .Select(m => (M: m, HP: m.MonsterInfo?.MonsterInfoStats.FirstOrDefault(s => s.Stat == Stat.Health).Amount ?? 0))
            .Where(t => t.HP <= 150) // 排除 Oma Hero(500, DC25-125 能打死 Lv70) 等碾压怪
            // 优先 HP>=40 (两刀以上可测真实间隔), 再按距离
            .OrderBy(t => t.HP >= 40 ? 0 : 1)
            .ThenBy(t => ChebyshevDistance(t.M.CellX, t.M.CellY, _playerLocation.X, _playerLocation.Y))
            .ToList();
        if (monsters.Count == 0)
        {
            // 空视野: 先 @monster 召怪重试一次 (TestHero 是 TempAdmin), 仍空才 FAIL
            if (!_operationAuditExtSpawnAttempted)
            {
                _operationAuditExtSpawnAttempted = true;
                GD.Print("[OperationAuditExt] S16 no monster in view, requesting @monster TigerSnake 3 (TempAdmin)");
                SendChat("@monster TigerSnake 3");
                GetTree().CreateTimer(4.0).Timeout += StartOperationAuditExtCombat;
                return;
            }
            GD.PrintErr("[OperationAuditExt] FAIL no monster in view for combat audit");
            GetTree().Quit();
            return;
        }
        foreach (var t in monsters.Take(3))
        {
            GD.Print($"[OperationAuditExt] S16 monster {t.M.DisplayName} id={t.M.ObjectID} dist={ChebyshevDistance(t.M.CellX, t.M.CellY, _playerLocation.X, _playerLocation.Y)} Lv={t.M.MonsterInfo?.Level ?? -1} HP={t.HP}");
        }

        _operationAuditExtCombatTarget = monsters[0].M;
        _operationAuditExtCombatTargetId = monsters[0].M.ObjectID;
        _operationAuditExtAttackTimes.Clear();
        _operationAuditExtCombatDied = false;
        _operationAuditExtCombatKeptTarget = false;
        _operationAuditExtCombatSelected = false;
        _operationAuditExtCombatSecond = false;
        _operationAuditExtWalkSteps = 0;
        _operationAuditExtStage = 16;
        _operationAuditExtResponsePending = false;
        GD.Print($"[OperationAuditExt] S16 target={_operationAuditExtCombatTarget.DisplayName} id={_operationAuditExtCombatTargetId}");

        // 视野内无 HP>=40 怪 (秒杀路径拿不到连续多刀节拍): 开发服务器 TempAdmin
        // 登录时用 @monster 生成 TigerSnake (HP70, 2-3 刀) 重入一次。
        if (!_operationAuditExtSpawnAttempted && !monsters.Any(t => t.HP >= 40))
        {
            _operationAuditExtSpawnAttempted = true;
            GD.Print("[OperationAuditExt] S16 no high-HP monster in view, requesting @monster TigerSnake 2 (TempAdmin)");
            SendChat("@monster TigerSnake 2");
            GetTree().CreateTimer(3.0).Timeout += StartOperationAuditExtCombat;
            return;
        }

        GetTree().CreateTimer(0.5).Timeout += CombatAuditWalkStep;
    }

    private int ChebyshevDistance(int x1, int y1, int x2, int y2)
        => Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));

    private void CombatAuditWalkStep()
    {
        if (!IsInstanceValid(_operationAuditExtCombatTarget))
        {
            GD.PrintErr("[OperationAuditExt] FAIL combat target removed during approach");
            GetTree().Quit();
            return;
        }
        // 目标死亡/已相邻 -> 选中攻击
        int dist = ChebyshevDistance(_operationAuditExtCombatTarget.CellX, _operationAuditExtCombatTarget.CellY,
            _playerLocation.X, _playerLocation.Y);
        if (_operationAuditExtCombatTarget.Dead || dist <= 1)
        {
            CombatAuditSelectTarget();
            return;
        }
        if (_operationAuditExtWalkSteps >= 80)
        {
            GD.PrintErr($"[OperationAuditExt] FAIL could not approach monster (dist={dist} steps=80)");
            GetTree().Quit();
            return;
        }
        // 上一步没动 (被挡/回包没到) 时重试同方向; 动了就按新位置走
        var dir = Functions.DirectionFromPoint(_playerLocation,
            new System.Drawing.Point(_operationAuditExtCombatTarget.CellX, _operationAuditExtCombatTarget.CellY));
        _operationAuditExtLastWalkPos = _playerLocation;
        SendMouseMove(dir, 1, false);
        _operationAuditExtWalkSteps++;
        GD.Print($"[OperationAuditExt] S16 walk step={_operationAuditExtWalkSteps} dir={dir} loc={_playerLocation} dist={dist}");
        GetTree().CreateTimer(0.6).Timeout += CombatAuditWalkStep;
    }

    private void CombatAuditSelectTarget()
    {
        if (_operationAuditExtCombatSelected) return;
        if (!IsInstanceValid(_operationAuditExtCombatTarget))
        {
            GD.PrintErr("[OperationAuditExt] FAIL combat target gone before select");
            GetTree().Quit();
            return;
        }
        int dist = ChebyshevDistance(_operationAuditExtCombatTarget.CellX, _operationAuditExtCombatTarget.CellY,
            _playerLocation.X, _playerLocation.Y);
        if (dist == 0)
        {
            // @monster 生成点可能与玩家同格 (dist=0), 自动攻击要求 Chebyshev==1: 走开一步再重入
            GD.Print("[OperationAuditExt] S16 target same-cell, stepping away");
            SendMouseMove((MirDirection)2, 1, false);
            GetTree().CreateTimer(0.8).Timeout += CombatAuditSelectTarget;
            return;
        }
        _operationAuditExtCombatSelected = true;
        GD.Print($"[OperationAuditExt] S16 select {_operationAuditExtCombatTarget.DisplayName} id={_operationAuditExtCombatTargetId} dist={dist} dead={_operationAuditExtCombatTarget.Dead}");
        // 左键选中 = 纯客户端状态 (服务端无包): 与 OnMouseDown 选中路径一致
        _combatController.TargetObject = _operationAuditExtCombatTarget;
        // 顶部自动攻击分支每帧跑: 相邻 + 冷却到 -> C.Attack (钩子记录时刻)
        GetTree().CreateTimer(2.0).Timeout += CombatAuditProgressCheck;
        GetTree().CreateTimer(25.0).Timeout += CombatAuditTimeout;
    }

    private void CombatAuditProgressCheck()
    {
        if (_operationAuditExtStage != 16) return;
        int attacks = _operationAuditExtAttackTimes.Count;
        bool targetDead = _operationAuditExtCombatDied;
        GD.Print($"[OperationAuditExt] S16 progress attacks={attacks} died={targetDead} kept={_operationAuditExtCombatKeptTarget}");
        if (attacks == 0 && !targetDead)
        {
            // 没出刀也没死: 可能没相邻 (怪移动了) 或节拍未到 -> 重试
            GD.PrintErr("[OperationAuditExt] FAIL no C.Attack sent within 2s of selecting adjacent target");
            GetTree().Quit();
            return;
        }
        if (!targetDead)
        {
            // 怪还活着: 继续等 (可能多次攻击), 2s 后再查
            GetTree().CreateTimer(2.0).Timeout += CombatAuditProgressCheck;
            return;
        }
        // 目标已死: D15 断言已由 OnObjectDied 钩子记录, 等移除后收尾
        GetTree().CreateTimer(1.8).Timeout += CombatAuditFinish;
    }

    private void CombatAuditPickSecondTarget()
    {
        // 第一只秒杀 (仅 1 刀): 换最近存活怪再打, 用攻击钩子记录的 t1 验证
        // 真实节拍间隔 (原版 _nextAttackMs 门控: 第二刀最早在 t0+interval)。
        var next = _objects.Values
            .Where(o => o.Type == ObjectRenderer.Kind.Monster && !o.Dead
                && string.IsNullOrWhiteSpace(o.PetOwner) && (o.MonsterInfo?.AI ?? -1) >= 0
                && o.ObjectID != _operationAuditExtCombatTargetId)
            .OrderBy(o => ChebyshevDistance(o.CellX, o.CellY, _playerLocation.X, _playerLocation.Y))
            .FirstOrDefault();
        if (next == null)
        {
            GD.Print("[OperationAuditExt] S16 no second monster in view for cadence retest");
            CombatAuditFinish();
            return;
        }
        _operationAuditExtCombatTarget = next;
        _operationAuditExtCombatTargetId = next.ObjectID;
        _operationAuditExtCombatSecond = true;
        _operationAuditExtCombatDied = false;
        _operationAuditExtCombatKeptTarget = false;
        _operationAuditExtCombatSelected = false;
        _operationAuditExtWalkSteps = 0;
        GD.Print($"[OperationAuditExt] S16 second-target={next.DisplayName} id={next.ObjectID}");
        CombatAuditWalkStep();
    }

    private void CombatAuditTimeout()
    {
        if (_operationAuditExtStage != 16) return;
        GD.PrintErr($"[OperationAuditExt] FAIL combat audit timeout attacks={_operationAuditExtAttackTimes.Count} died={_operationAuditExtCombatDied}");
        GetTree().Quit();
    }

    private void CombatAuditFinish()
    {
        if (_operationAuditExtStage != 16) return;
        if (!_operationAuditExtCombatSecond && _operationAuditExtCombatDied
            && _operationAuditExtCombatKeptTarget && _operationAuditExtAttackTimes.Count == 1)
        {
            // 秒杀: 打第二只拿节拍间隔 (t1-t0); 不满足则直接收尾
            CombatAuditPickSecondTarget();
            return;
        }
        double interval = ComputeAttackIntervalMs(Globals.AttackDelay, PlayerStats[Stat.AttackSpeed],
            Globals.ASpeedRate, BagWeight > PlayerStats[Stat.BagWeight],
            _playerPoison.HasFlag(PoisonType.Neutralize));
        bool cadence = true;
        string cadenceDetail = _operationAuditExtAttackTimes.Count switch
        {
            0 => "no-attack",
            1 => "single-hit(one-shot)",
            _ => "multi-hit"
        };
        if (_operationAuditExtAttackTimes.Count >= 2)
        {
            double gap = _operationAuditExtAttackTimes[1] - _operationAuditExtAttackTimes[0];
            if (_operationAuditExtCombatSecond)
            {
                // 第二目标: 走位间隔污染, 断言门控下界 (任何后续攻击不得早于 t0+interval)
                cadence = gap >= interval - 100.0;
                cadenceDetail = $"multi-target lower-bound gap={gap:F0}ms >= expect={interval:F0}ms";
            }
            else
            {
                cadence = Math.Abs(gap - interval) <= 300.0;
                cadenceDetail = $"gap={gap:F0}ms expect={interval:F0}ms";
            }
        }
        bool pass = _operationAuditExtCombatKeptTarget && _operationAuditExtCombatDied && cadence;
        GD.Print($"[OperationAuditExt] S16 combat-died={_operationAuditExtCombatDied} kept-target={_operationAuditExtCombatKeptTarget} cadence={cadence} ({cadenceDetail}) attacks={_operationAuditExtAttackTimes.Count}");
        GD.Print($"[OperationAuditExt] RESULT rings=true bracelets=true beltCleared=true autoCleared=true mailLifecycle=true companion={_operationAuditExtCompanionPass} guild={_operationAuditExtGuildPass} combat={pass} pass={pass}");
        _operationAuditExtStage = 0;
        GetTree().Quit();
    }

    private void CleanupOperationAuditExt()
    {
        // 还原装备: S5/S6 已把双戒指换位归位; S7/S8 把 BraceletL 物品换入 BraceletR,
        // 被换下的原 BraceletR 物品在背包 from 槽 -> 移回 BraceletL (真实回包驱动)。
        var bagged = _operationAuditExtFromSlot >= 0 && _operationAuditExtFromSlot < Inventory.Length
            ? Inventory[_operationAuditExtFromSlot] : null;
        if (bagged != null && Equipment[(int)EquipmentSlot.BraceletL] == null)
        {
            var bagCell = InventoryCells[_operationAuditExtFromSlot];
            var braceLCell = EquipmentCells[(int)EquipmentSlot.BraceletL];
            _operationAuditExtFromSlot = bagCell.Slot;
            _operationAuditExtToSlot = braceLCell.Slot;
            _operationAuditExtStage = 12;
            GD.Print($"[OperationAuditExt] S12 RESTORE_BRACELETL from={bagCell.Slot} to={EquipmentSlot.BraceletL}");
            braceLCell.ToEquipment(bagCell);
            return;
        }
        // 清腰带链接 (D1 清理): QuickItem/QuickInfo setter 只改本地, 必须显式发服务端
        foreach (var link in BeltLinks)
        {
            if (link.LinkItemIndex <= 0 && link.LinkInfoIndex <= 0) continue;
            var cell = _beltDialog?.Grid?.Cells != null && link.Slot >= 0 && link.Slot < _beltDialog.Grid.Cells.Length
                ? _beltDialog.Grid.Cells[link.Slot] : null;
            if (cell != null)
            {
                cell.QuickItem = null;
                cell.QuickInfo = null;
                SendBeltLinkChanged(link.Slot, -1, -1);
            }
        }
        // 清自动药水行 (D3 清理): 显式发服务端
        for (int i = 0; i < AutoPotionBox.Rows.Length; i++)
        {
            var row = AutoPotionBox.Rows[i];
            if (row.ItemCell.QuickInfo == null) continue;
            row.ItemCell.QuickInfo = null;
            row.Health.Value = 0;
            row.Mana.Value = 0;
            row.EnabledCheck.Checked = false;
            AutoPotionBox.SendRowUpdate(i);
        }
        _operationAuditExtStage = 11;
        _operationAuditExtResponsePending = true;
        ContinueOperationAuditExt();
    }

    private void StartInteractionAudit()
    {
        var npc = _objects.Values.FirstOrDefault(x => x?.Type == ObjectRenderer.Kind.NPC && x.Visible);
        if (npc != null)
        {
            var hitNpc = _combatController.PickObjectAtCellForAudit(new System.Drawing.Point(npc.CellX, npc.CellY));
            _combatController.MouseObject = hitNpc;
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
            bool npcDeferred = _npcObjectId != npc.ObjectID;
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            _interactionNpcSent = npcDeferred && hitNpc?.ObjectID == npc.ObjectID && _npcObjectId == npc.ObjectID;
            GD.Print($"[InteractionAudit] NPC_CLICK object={npc.ObjectID} hit={hitNpc?.ObjectID ?? 0} sent={_interactionNpcSent}");
        }
        else
            GD.PrintErr("[InteractionAudit] FAIL no visible NPC in current map");

        var player = _objects.Values.FirstOrDefault(x => x?.Type == ObjectRenderer.Kind.Player
            && x.ObjectID != _playerObjectID);
        if (player != null)
        {
            var hitPlayer = _combatController.PickObjectAtCellForAudit(new System.Drawing.Point(player.CellX, player.CellY));
            _combatController.MouseObject = hitPlayer;
            if (AutoLoginArgs.InteractionAudit && _otherPlayers.TryGetValue(player.ObjectID, out var visiblePlayer))
            {
                GD.Print($"[InteractionAudit] COORD cell=({player.CellX},{player.CellY}) " +
                    $"proxyLocal={player.Position} proxyCanvas={player.GetGlobalTransformWithCanvas().Origin} " +
                    $"playerLocal={visiblePlayer.Position} playerCanvas={visiblePlayer.GetGlobalTransformWithCanvas().Origin} " +
                    $"boxLogical={_mapView.CellToScreen(player.CellX, player.CellY, true)} " +
                    $"viewport={GetViewport().GetVisibleRect().Size} gameScale={Scale}");
            }
            _nextInspectMs = 0;
            int beforeLeft = _interactionInspectSent;
            _UnhandledInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                CtrlPressed = true,
            });
            _interactionInspectLeftSent = _interactionInspectSent - beforeLeft;

            _nextInspectMs = 0;
            _UnhandledInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Right,
                Pressed = true,
                CtrlPressed = true,
            });
            GD.Print($"[InteractionAudit] PLAYER_CTRL_CLICK object={player.ObjectID} hit={hitPlayer?.ObjectID ?? 0} leftSent={_interactionInspectLeftSent} rightSent={_interactionInspectSent}");
        }
        else
            GD.PrintErr("[InteractionAudit] FAIL no other player in current map");

        _interactionAuditDeadline = Godot.Time.GetTicksMsec() + 4000.0;
    }


    private void UpdatePlayerPosition()
    {
        if (_mapView?.Map == null) return;
        _mapView.CenterOn(_player.CellX, _player.CellY);
        _player.Position = _mapView.CellToScreen(_player.CellX, _player.CellY, true)
            + new Vector2(_player.OffsetX, _player.OffsetY);
        _player.ZIndex = RenderOrder.ObjectAtFoot(_player.RenderY);
        UpdateObjectPositions();
    }

    // 视野范围随视口尺寸自适应 (窗口模式视口大, 固定 12x15 画不满)
    private void UpdateViewRange()
    {
        if (_mapView == null) return;
        var vp = GetViewport().GetVisibleRect().Size / WorldScale;
        int vrx = (int)Math.Ceiling(vp.X / (2f * 48)) + 1;
        int vry = (int)Math.Ceiling(vp.Y / (2f * 32)) + 1;
        _mapView.ViewRangeX = Math.Max(_mapView.ViewRangeX, vrx);
        _mapView.ViewRangeY = Math.Max(_mapView.ViewRangeY, vry);
    }

    // M11: 填充状态窗口 (玩家真实数据)
    private void RefreshStatusWindow()
    {
        string mapName = Globals.MapInfoList?.Binding.FirstOrDefault(m => m.Index == _playerMapIndex)?.Local() ?? $"Map{_playerMapIndex}";
        string className = StartInfo?.Class.Local() ?? "-";
        _statusWindow.Refresh(
            StartInfo?.Name ?? "-", className,
            _player.Health, _player.MaxHealth, _player.MaxMana,
            _playerLocation.X, _playerLocation.Y, _playerDirection,
            mapName, _objects.Count);
    }

    // 相机锚定玩家后, 计算所有周围物体的屏幕位置 (含移动像素偏移 OffsetX/OffsetY)
    private void UpdateObjectPositions()
    {
        if (_mapView?.Map == null) return;

        foreach (var ob in _objects.Values)
        {
            ob.ComputeScreenPos(_mapView.CenterX, _mapView.CenterY, _mapView.ViewRangeX,
                _mapView.ViewRangeY, 0, 0, _mapView);
            ob.ZIndex = RenderOrder.ObjectAtFoot(ob.RenderY);
        }
        foreach (var player in _otherPlayers.Values) UpdateOtherPlayerPosition(player);
    }

    private void UpdateOtherPlayerPosition(PlayerRenderer player)
    {
        // Keep remote players on the same camera-centred transform as map
        // objects, the local player, and the hidden mouse-picking proxy.
        player.Position = _mapView.CellToScreen(player.CellX, player.CellY, true)
            + new Vector2(player.OffsetX, player.OffsetY);
        player.ZIndex = RenderOrder.ObjectAtFoot(player.RenderY);

        // The visible player uses PlayerRenderer, while mouse picking uses the
        // hidden ObjectRenderer proxy in _objects.
        foreach (var pair in _otherPlayers)
        {
            if (pair.Value != player || !_objects.TryGetValue(pair.Key, out var proxy)) continue;
            proxy.CellX = player.CellX;
            proxy.CellY = player.CellY;
            proxy.Direction = player.Direction;
            proxy.Dead = player.Dead;
            proxy.Visible = player.Visible;
            proxy.OffsetX = player.OffsetX;
            proxy.OffsetY = player.OffsetY;
            proxy.ComputeScreenPos(_mapView.CenterX, _mapView.CenterY, _mapView.ViewRangeX,
                _mapView.ViewRangeY, 0, 0, _mapView);
            proxy.ZIndex = RenderOrder.ObjectAtFoot(proxy.RenderY);
            break;
        }
    }

    // 格子坐标 -> 屏幕坐标 (与玩家居中公式一致)
    private Vector2 ComputeObjectScreenPos(int cellX, int cellY)
    {
        if (_mapView?.Map == null) return Vector2.Zero;

        return _mapView.CellToScreen(cellX, cellY, true);
    }

    // Fixed MapTarget effects use the legacy map origin. Only effects attached
    // to a target node use the object's baseline position.
    private Vector2 ComputeEffectScreenPos(int cellX, int cellY)
    {
        if (_mapView?.Map == null) return Vector2.Zero;

        return _mapView.CellToScreen(cellX, cellY, false);
    }

    private void ClearMagicLock()
    {
        if (_magicLockTargetObjectId == 0) return;
        GD.Print($"[Magic] 清除锁定目标 ObjectID={_magicLockTargetObjectId}");
        _magicLockTargetObjectId = 0;
    }

    private void StopPlayerActionForInput()
    {
        if (_player == null || _player.Dead) return;
        _player.StopActionForInput();
    }

    private ObjectRenderer GetMagicLockTarget()
    {
        if (_magicLockTargetObjectId == 0) return null;
        if (!_objects.TryGetValue(_magicLockTargetObjectId, out var target)
            || !CombatController.CanAttackObject(target))
        {
            ClearMagicLock();
            return null;
        }
        return target;
    }

    // F1~F12 -> Spell01~12, Shift+F1~F12 -> Spell13~24。
    // 键盘输入和技能栏点击共用这一条链路。
    public void UseMagicSlot(int slot)
    {
        if (slot < 0 || slot > 23) return;
        if (_observer || _player == null || IsMounted || _player.Dead || _player.DragonRepulsed ||
            _playerPoison.HasFlag(PoisonType.Paralysis) || _playerPoison.HasFlag(PoisonType.Silenced))
            return;
        if (_net?.Connection?.Connected != true)
        {
            GD.Print("[Magic] 未释放：网络尚未连接");
            return;
        }
        var key = (Library.SpellKey)(slot + 1);  // Spell01 = 1
        ClientUserMagic magic = null;
        foreach (var kv in UserMagics)
        {
            var m = kv.Value;
            if (m == null) continue;
            if (MagicBarSpellSet == 1 && m.Set1Key == key) magic = m;
            else if (MagicBarSpellSet == 2 && m.Set2Key == key) magic = m;
            else if (MagicBarSpellSet == 3 && m.Set3Key == key) magic = m;
            else if (MagicBarSpellSet == 4 && m.Set4Key == key) magic = m;
            if (magic != null) break;
        }
        if (magic == null)
        {
            GD.Print($"[Magic] 未释放：当前 Set{MagicBarSpellSet} 没有绑定 {key}");
            return;
        }
        if (magic.Info == null)
        {
            magic.Complete();
            if (magic.Info == null)
            {
                GD.PrintErr($"[Magic] 未释放：技能信息未解析 InfoIndex={magic.InfoIndex}");
                return;
            }
        }
        if (PlayerLevel < magic.Info.NeedLevel1) return;
        if (magic.ItemRequired && !Equipment.Any(x => x?.Info?.ItemEffect == ItemEffect.MagicRing
                                                       && x.Info.Shape == magic.Info.Index))
            return;

        switch (magic.Info.Magic)
        {
            case MagicType.Swordsmanship:
            case MagicType.SpiritSword:
            case MagicType.VineTreeDance:
            case MagicType.WillowDance:
                return;
            case MagicType.Thrusting:
            case MagicType.HalfMoon:
            case MagicType.DestructiveSurge:
            case MagicType.FlameSplash:
                if (Library.Time.Now < ToggleTime) return;
                ToggleTime = Library.Time.Now.AddSeconds(1);
                bool enabled = !_enabledToggleMagics.Contains(magic.Info.Magic);
                if (enabled) _enabledToggleMagics.Add(magic.Info.Magic);
                else _enabledToggleMagics.Remove(magic.Info.Magic);
                SendMagicToggle(magic.Info.Magic, enabled);
                return;
            case MagicType.FullBloom:
            case MagicType.WhiteLotus:
            case MagicType.RedLotus:
            case MagicType.SweetBrier:
            case MagicType.Karma:
                if (Library.Time.Now < ToggleTime || Library.Time.Now < magic.NextCast) return;
                if (!_buffs.Values.Any(x => x?.Type == BuffType.Cloak)) return;
                _attackMagic = magic.Info.Magic;
                ToggleTime = Library.Time.Now.AddMilliseconds(magic.Info.Magic == MagicType.Karma ? 500 : 200);
                return;
        }
        var pCell = _playerLocation;
        // 目标解析顺序：用户刚刚左键选中的新目标 > 已锁定目标 > 鼠标悬停目标。
        // 原版 MagicObject 会记住第一次成功施法的对象；仅临时读取 MouseObject
        // 会导致鼠标移开后第二次魔法退化为地面落点。
        var hovered = _combatController?.MouseObject;
        var selected = _combatController?.TargetObject;
        var locked = GetMagicLockTarget();
        ObjectRenderer target;
        if (locked != null)
        {
            // 有锁定时，只有用户明确左键选了另一个对象才切换锁定。
            target = CombatController.CanAttackObject(selected)
                && selected.ObjectID != locked.ObjectID ? selected : locked;
        }
        else
        {
            // 保留原有行为：首次施法仍然优先使用鼠标悬停目标，
            // 没有悬停目标时才使用左键选中目标。
            target = CombatController.CanAttackObject(hovered) ? hovered
                : CombatController.CanAttackObject(selected) ? selected : null;
        }
        uint targetID = target?.ObjectID ?? 0;
        var mouseCell = _combatController?.MouseCell() ?? new System.Drawing.Point(pCell.X, pCell.Y);
        var targetCell = target == null
            ? mouseCell
            : new System.Drawing.Point(target.CellX, target.CellY);

        // 原版 UseMagic 超距检查：目标超出技能范围时提示并拒绝施法。
        // 不检查的话服务端会把超距目标静默降级为纯地面落点 (特效命中
        // 但无伤害)，看起来像打中却没掉血。
        if (target != null && !Functions.InRange(pCell, targetCell, Globals.MagicRange))
        {
            if (Library.Time.Now < _magicTooFarAt) return;
            _magicTooFarAt = Library.Time.Now.AddSeconds(1);
            ReceiveChat(string.Format(Lang.GameNoneLabel2, magic.Info.Local()), MessageType.Hint);
            return;
        }

        // 原版的范围/落点技能即使鼠标下有目标，也把 MapLocation 发给服务端；
        // 普通锁定投射则使用目标格。没有目标时绝不能回退到玩家当前格。
        bool useMouseLocation = magic.Info.Magic switch
        {
            MagicType.Purification or MagicType.EvilSlayer or MagicType.GreaterEvilSlayer
                or MagicType.ExplosiveTalisman or MagicType.ImprovedExplosiveTalisman
                or MagicType.PoisonDust or MagicType.Neutralize or MagicType.BindingTalisman
                or MagicType.BrainStorm => true,
            _ => false
        };
        if (!useMouseLocation && target != null)
        {
            _magicLockTargetObjectId = target.ObjectID;
            GD.Print($"[Magic] 锁定目标 ObjectID={target.ObjectID} name={target.DisplayName}");
        }
        var castCell = useMouseLocation || target == null ? mouseCell : targetCell;
        if (magic.Info.School == MagicSchool.Toggle)
        {
            if (Library.Time.Now < ToggleTime)
                return;
            ToggleTime = Library.Time.Now.AddSeconds(1);
            bool enabled = !_enabledToggleMagics.Contains(magic.Info.Magic);
            if (enabled) _enabledToggleMagics.Add(magic.Info.Magic); else _enabledToggleMagics.Remove(magic.Info.Magic);
            SendMagicToggle(magic.Info.Magic, enabled);
            RefreshMagicBars();
            return;
        }
        if (Library.Time.Now < magic.NextCast || magic.Cost > _currentMP) return;
        MirDirection dir = Functions.DirectionFromPoint(
            new System.Drawing.Point(pCell.X, pCell.Y), castCell);
        GD.Print($"[Magic] 发包 {magic.Info.Name} Magic={magic.Info.Magic} Set={MagicBarSpellSet} Slot={slot + 1} " +
            string.Format(Lang.GameTargetLabel, targetID, pCell.X, pCell.Y, mouseCell.X, mouseCell.Y, castCell.X, castCell.Y, dir));
        var packet = new C.Magic
        {
            Direction = dir,
            Action = MirAction.Spell,
            Type = magic.Info.Magic,
            Target = targetID,
            Location = castCell,
        };
        SuspendMovementForMagic();
        if (IsPlayerWalking())
        {
            // 原版 UseMagic 只设置 User.MagicAction；真正发包在 ProcessInput
            // 第二步，等 NextActionTime 到期且 ActionQueue 清空（当前动作
            // 走完或被中断）。移动中直接发包，动作结束前 CombatController
            // 的自动攻击也不会像原版那样被队列暂停。
            _pendingMagicPacket = packet;
            _pendingMagicCastAtMs = _player.FrameStartMs + _player.MovementDurationMs;
            GD.Print($"[Magic] 排队 {magic.Info.Name}：等待当前移动结束 (castAt={_pendingMagicCastAtMs:0}ms)");
            return;
        }
        _net.Connection.Enqueue(packet);
    }

    private bool IsPlayerWalking()
        => _player != null && IsWalkAnimation(_player.Animation);

    // 兼容旧调用点；统一走同一条释放链路。
    private void UseMagicKey(int slot)
    {
        UseMagicSlot(slot);
    }

    private void UseBeltKey(int slot)
    {
        if (slot < 0 || slot >= (_beltDialog?.Grid?.Cells?.Length ?? 0)) return;
        var cell = _beltDialog.Grid.Cells[slot];
        if (IsObserver) return;
        // 原版 GameScene.OnKeyDown：快捷键首先把当前拿起的物品
        // 投放到目标腰带格；没有拿起物品时才调用腰带格 UseItem。
        if (ShouldRouteSelectedItemToBelt(DXItemCell.SelectedCell != null))
        {
            DXItemCell.SelectedCell.MoveItem(cell);
            return;
        }
        if (cell?.Item == null) return;
        if (cell.UseItem())
            GD.Print($"[Belt] 使用腰带槽 {slot + 1}: {cell.Item.Info?.ItemName}");
    }

    public static bool ShouldRouteSelectedItemToBelt(bool hasSelectedCell) => hasSelectedCell;

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (_net?.Connection?.Connected != true) return;

        // _Input 先于 Control._GuiInput/_UnhandledKeyInput 到达。任何原生
        // 文本编辑器获得焦点时都必须先把按键留给它；否则聊天框的“空格/回车
        // 打开聊天”快捷键会抢走配置、搜索等输入框的字符和确认键。
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit)
            return;

        if (_chatTextBox?.HandleGlobalKey(key) == true)
            return;

        // EI 旧版坐骑窗口热键为 Ctrl+S；保留现代 M 的坐骑动作键不变。
        if (key.Keycode == Key.S && key.CtrlPressed && !key.AltPressed && !key.ShiftPressed)
        {
            ToggleHorseWindow();
            return;
        }

        // Alt 是地面掉落物名称的显示开关。只处理单独按下 Alt，
        // 不影响 Alt+左键的采集/钓鱼/驯马操作。
        if (key.Keycode == Key.Alt && !key.CtrlPressed && !key.ShiftPressed)
        {
            ClientSettings.ShowGroundItemNames = !ClientSettings.ShowGroundItemNames;
            RefreshGroundItemLabels(forceRedraw: true);
            GD.Print($"[Game] 地面物品名称: {(ClientSettings.ShowGroundItemNames ? "显示" : "隐藏")}");
            return;
        }

        // Ctrl+F12 = 开发调试键：UI overlay 热重载 + 截图 + 可见窗口矩形导出
        // （截图/矩形供 uieditor 的 underlay 对齐用）。普通 F12 和 Shift+F12
        // 必须留给 KeyBindManager 的 SpellUse12/24，不能再被调试分支吞掉。
        if (key.Keycode == Key.F12 && key.CtrlPressed && !key.ShiftPressed)
        {
            UiOverlay.ReloadAll();
            DumpVisibleWindowRects();
            var img = GetViewport().GetTexture().GetImage();
            img.SavePng("/tmp/game_screenshot.png");
            GD.Print("[Game] F12: overlay 热重载 + 截图 /tmp/game_screenshot.png + 窗口矩形 /tmp/ui_window_rects.json");
            return;
        }

        if (key.Keycode == Key.Escape)
        {
            if (WindowManager.CloseTop())
            {
                if (_escapeCloseAll)
                    while (WindowManager.CloseTop()) { }
                return;
            }
        }

        // 窗口快捷键必须独立于其它窗口状态：Q/W 等键既要能在已有窗口
        // 打开时继续打开自己的窗口，也要能再次按下关闭自己的窗口。
        // 不能让下面的“有窗口时屏蔽全局快捷键”把它们提前吞掉。
        KeyBindAction windowBind = KeyBindManager.GetAction(key);
        if (IsWindowShortcut(windowBind))
        {
            HandleKeyBind(windowBind);
            return;
        }

        // 有其它可见窗口时，全局游戏快捷键也必须停止，避免窗口内的自定义
        // 控件在没有原生焦点时仍被游戏快捷键抢占。
        if (WindowManager.OpenWindows.Any(window => window != null && window.Visible))
            return;

        // 原版键位表分发：窗口、技能、物品、移动和战斗动作统一走这里。
        KeyBindAction bind = KeyBindManager.GetAction(key);
        if (bind != KeyBindAction.None)
        {
            HandleKeyBind(bind);
            return;
        }

        // 功能键 (不走 KeyBindManager, 避免改 KeyBindManager 与他人冲突)
        if (key.Keycode == Key.M)
        {
            _net.Connection.Enqueue(new C.Mount());
            GD.Print("[Game] 请求上下马");
            return;
        }
        if (key.Keycode == Key.D && !key.CtrlPressed && !key.AltPressed && !key.ShiftPressed)
        {
            _autoRun = !_autoRun;
            _mouseWalker.AutoRun = _autoRun;
            GD.Print($"[Game] 自动跑步: {(_autoRun ? "开" : "关")}");
        }
        // T 请求交易：服务端按玩家朝向检查相邻角色。
        if (key.Keycode == Key.T && !key.CtrlPressed && !key.AltPressed && !key.ShiftPressed)
        {
            _net.Connection.Enqueue(new C.TradeRequest());
            return;
        }

        MirDirection? dir = key.Keycode switch
        {
            Key.Up => MirDirection.Up,
            Key.Down => MirDirection.Down,
            Key.Left => MirDirection.Left,
            Key.Right => MirDirection.Right,
            _ => null,
        };

        if (dir != null)
        {
            if (CanPlayerMove())
                SendMouseMove(dir.Value, 1, false);
        }
    }

    private static bool IsWindowShortcut(KeyBindAction action)
        => action is KeyBindAction.MenuWindow
            or KeyBindAction.HelpWindow
            or KeyBindAction.ConfigWindow
            or KeyBindAction.CharacterWindow
            or KeyBindAction.InventoryWindow
            or KeyBindAction.MagicWindow
            or KeyBindAction.MagicBarWindow
            or KeyBindAction.DungeonFinderWindow
            or KeyBindAction.StorageWindow
            or KeyBindAction.BeltWindow
            or KeyBindAction.AutoPotionWindow
            or KeyBindAction.CurrencyWindow
            or KeyBindAction.FilterDropWindow
            or KeyBindAction.FortuneWindow
            or KeyBindAction.QuestTrackerWindow
            or KeyBindAction.MapMiniWindow
            or KeyBindAction.MapBigWindow
            or KeyBindAction.RankingWindow
            or KeyBindAction.GameStoreWindow
            or KeyBindAction.CompanionWindow
            or KeyBindAction.GroupWindow
            or KeyBindAction.GuildWindow
            or KeyBindAction.MailBoxWindow
            or KeyBindAction.MailSendWindow
            or KeyBindAction.BlockListWindow
            or KeyBindAction.QuestLogWindow
            or KeyBindAction.ChatOptionsWindow;

    /// <summary>
    /// 导出当前可见窗口的逻辑画布矩形（F12，配合 /tmp/game_screenshot.png）：
    /// uieditor 的截图 underlay 脚本按矩形裁剪出每窗口底图，保证像素级对齐。
    /// </summary>
    private void DumpVisibleWindowRects()
    {
        var rects = new Dictionary<string, int[]>();
        foreach (DXWindow window in DXWindow.Windows.ToArray())
        {
            if (window is not GodotObject g || !GodotObject.IsInstanceValid(g) || !window.Visible) continue;
            rects[window.GetType().Name] = new[]
            {
                (int)window.Position.X, (int)window.Position.Y,
                (int)window.Size.X, (int)window.Size.Y,
            };
        }
        try
        {
            File.WriteAllText("/tmp/ui_window_rects.json", System.Text.Json.JsonSerializer.Serialize(rects));
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Game] 窗口矩形导出失败: {ex.Message}");
        }
    }

    // 地图交互放在未处理输入阶段，确保背包/窗口控件先消费点击，
    // 对齐旧版 ActiveScene 的“UI 优先、地图其次”分发顺序。
    public override void _UnhandledInput(InputEvent @event)
    {
        // 原版 MapControl.OnMouseDown 首行直接拒绝观察模式；否则脚下拾取、
        // NPC 调用、观察和拖物品到地图等地图级分支会绕过物品格的 observer guard。
        if (_observer && @event is InputEventMouseButton)
            return;

        // 原版 NPC 点击由 MouseClick(抬起)派发；脚下格的 MouseDown 仍先
        // 发送 PickUp。保存这个特殊重叠场景，避免脚下掉落物把 NPC 点击吞掉。
        if (@event is InputEventMouseButton npcRelease && !npcRelease.Pressed &&
            npcRelease.ButtonIndex == MouseButton.Left && _pendingNpcClickObjectId != 0)
        {
            uint objectId = _pendingNpcClickObjectId;
            _pendingNpcClickObjectId = 0;
            if (_combatController?.MouseObject?.Type == ObjectRenderer.Kind.NPC &&
                _combatController.MouseObject.ObjectID == objectId)
            {
                TrySendNpcCall(objectId);
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        // 原版 TargetForm.OnMouseDown 的最前置分支：地图右键先取消
        // 已拿起物品/货币，不能继续落入自动寻路、目标取消或普通移动。
        if (@event is InputEventMouseButton cancelMouse && cancelMouse.Pressed &&
            cancelMouse.ButtonIndex == MouseButton.Right &&
            ShouldCancelMapRightClick(DXItemCell.SelectedCell != null, _selectedCurrency != null))
        {
            DXItemCell.SelectedCell = null;
            _selectedCurrency = null;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseButton stateMouse && stateMouse.Pressed &&
            (stateMouse.ButtonIndex == MouseButton.Left || stateMouse.ButtonIndex == MouseButton.Right))
        {
            CancelAutoPath();
            _groundItemPickupTargetId = 0;
            bool altLeft = stateMouse.ButtonIndex == MouseButton.Left &&
                (stateMouse.AltPressed || Input.IsKeyPressed(Key.Alt));
            // 原版 Alt 分支在 Fishing/Taming 状态下直接返回；它不会把
            // Alt 采集点击误解释为普通左键取消动作。
            bool cancelGathering = ShouldCancelGatheringForMapClick(altLeft, IsFishingActive, IsTamingActive);
            if (cancelGathering && IsFishingActive)
            {
                SendFishingCast(FishingState.Cancel);
                GetViewport().SetInputAsHandled();
                return;
            }
            if (cancelGathering && IsTamingActive)
            {
                CancelTaming();
                GetViewport().SetInputAsHandled();
                return;
            }
        }
        if (@event is InputEventMouseButton currencyMouse && currencyMouse.Pressed && currencyMouse.ButtonIndex == MouseButton.Left && _selectedCurrency != null)
        {
            var currency = _selectedCurrency;
            _selectedCurrency = null;
            // 原版 MapControl：`new DXItemAmountWindow("Drop Item", new ClientUserItem(DropItem, Amount))`
            // —— 预览格显示货币掉落物，输入数量时按 IsCurrencyItem 分支实时更新 Count。
            var dropItem = currency.Info?.DropItem;
            var preview = dropItem == null ? null : new ClientUserItem(dropItem, currency.Amount);
            var dialog = new ItemAmountDialog("Drop Item", currency.Amount, 1,
                amount => SendCurrencyDrop(currency.CurrencyIndex, amount), preview);
            WindowManager.Open(dialog, _uiLayer);
            GetViewport().SetInputAsHandled();
            return;
        }
        // 原版 MapControl.ProcessInput `case Left: Mining = false;`：
        // 任何一次左键按下都先停止正在进行的挖矿，挖矿分支随后重新设置。
        if (@event is InputEventMouseButton leftPress && leftPress.Pressed
            && leftPress.ButtonIndex == MouseButton.Left)
        {
            _mining = false;
        }
        if (@event is InputEventMouseButton dropMouse && dropMouse.Pressed && dropMouse.ButtonIndex == MouseButton.Left)
        {
            var cell = DXItemCell.SelectedCell;
            var item = cell?.Item;
            if (cell != null)
            {
                // MapControl.OnMouseDown handles an item picked up from the
                // belt/auto-potion bars before normal map movement.
                if (cell.GridType == GridType.Belt)
                {
                    var link = BeltLinks.ElementAtOrDefault(cell.Slot);
                    DXItemCell.SelectedCell = null;
                    if (link != null) SendBeltLinkChanged(link.Slot, link.LinkInfoIndex, link.LinkItemIndex);
                    GetViewport().SetInputAsHandled();
                    return;
                }
                if (cell.GridType == GridType.AutoPotion)
                {
                    int slot = cell.Slot;
                    DXItemCell.SelectedCell = null;
                    AutoPotionBox?.SendRowUpdate(slot);
                    GetViewport().SetInputAsHandled();
                    return;
                }
                if (item == null || item.Flags.HasFlag(UserItemFlags.Locked)
                    || cell.GridType is not (GridType.Inventory or GridType.CompanionInventory))
                {
                    DXItemCell.SelectedCell = null;
                    GetViewport().SetInputAsHandled();
                    return;
                }
                var source = cell;
                var amount = new ItemAmountDialog(item, count =>
                {
                    if (!CanBeginItemDrop(source))
                        return;
                    source.Locked = true;
                    source.UpdateBorder();
                    SendItemDrop(new CellLinkInfo
                    {
                        GridType = source.GridType,
                        Slot = source.Slot,
                        Count = (int)Math.Clamp(count, 1L, (long)source.Item.Count),
                    });
                });
                DXItemCell.SelectedCell = null;
                WindowManager.Open(amount, _uiLayer);
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (@event is InputEventMouseButton altMouse && altMouse.Pressed
            && altMouse.ButtonIndex == MouseButton.Left
            && (altMouse.AltPressed || Input.IsKeyPressed(Key.Alt)))
        {
            if (_player == null || _player.ElementalHurricane || _playerHorse != HorseType.None
                || IsFishingActive || IsTamingActive)
            {
                GetViewport().SetInputAsHandled();
                return;
            }

            var target = _combatController?.MouseCell() ?? _playerLocation;
            var direction = Functions.DirectionFromPoint(_playerLocation, target);
            int distance = Math.Max(Math.Abs(target.X - _playerLocation.X),
                Math.Abs(target.Y - _playerLocation.Y));
            // 原版只读取人物武器槽和护甲槽；不能从其它装备槽捞出
            // FishingRod/TamingLasso 伪造采集配置。
            var tool = Equipment.ElementAtOrDefault((int)EquipmentSlot.Weapon);
            var armour = Equipment.ElementAtOrDefault((int)EquipmentSlot.Armour);
            var mapInfo = Globals.MapInfoList?.Binding.FirstOrDefault(m => m.Index == _playerMapIndex);

            bool fishingSetup = IsFishingRig(tool?.Info?.ItemEffect, armour?.Info?.ItemEffect);
            if (fishingSetup && mapInfo != null
                && Functions.FishingZone(Globals.FishingInfoList, mapInfo,
                    _mapView.Map.Width, _mapView.Map.Height, target) != null
                && Functions.ValidFishingDistance(distance, _playerStats[Stat.ThrowDistance]))
            {
                _playerDirection = direction;
                _net?.Connection?.SendFishingCast(FishingState.Cast, direction, target);
                GetViewport().SetInputAsHandled();
                return;
            }
            // 已装备钓鱼配置但地点/距离不合法：原版不 break，回落到
            // 普通左键逻辑（拾取/挖矿/移动），而不是完全消费这次点击。
            // 驯服 AI==135 怪物超 TamingDistance 同理：原版不 break。

            var monster = _combatController?.MouseObject;
            bool lassoTarget = tool?.Info?.ItemEffect == ItemEffect.TamingLasso
                && monster?.Type == ObjectRenderer.Kind.Monster
                && monster.MonsterInfo?.AI == 135;
            if (lassoTarget)
            {
                if (distance <= Globals.TamingDistance)
                {
                    _tamingTargetObjectID = monster.ObjectID;
                    _playerDirection = direction;
                    _net?.Connection?.SendTaming(monster.ObjectID, TamingState.Cast, direction);
                    GetViewport().SetInputAsHandled();
                    return;
                }
                // 超距：原版公共尾部见鼠标下 AI135 活怪物 → break，
                // 不采集不移动；此处消费本次点击（等价 break）。
                GetViewport().SetInputAsHandled();
                return;
            }
            // 未装备钓具/驯具：原版 AttemptAction(Harvest) + return，
            // 同样消费点击（采集/拔草），但有 Globals.HarvestTime 冷却。
            double harvestNow = Godot.Time.GetTicksMsec();
            if (harvestNow >= _nextHarvestMs)
            {
                _playerDirection = direction;
                _net?.Connection?.SendHarvest(direction);
                _nextHarvestMs = harvestNow + Globals.HarvestTime.TotalMilliseconds;
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        var pickupObject = _combatController?.MouseObject;
        if (@event is InputEventMouseButton itemClick && itemClick.Pressed
            && itemClick.ButtonIndex == MouseButton.Left
            && pickupObject?.Type == ObjectRenderer.Kind.Item)
        {
            int itemDistance = Functions.Distance(_playerLocation,
                new System.Drawing.Point(pickupObject.CellX, pickupObject.CellY));
            if (itemDistance <= 1)
            {
                if (CanSendMapPickup(_observer, _player?.Dead == true,
                        _playerPoison.HasFlag(PoisonType.Paralysis),
                        _playerPoison.HasFlag(PoisonType.Containment),
                        _player?.DragonRepulsed == true))
                    SendPickUp();
                _groundItemPickupTargetId = 0;
            }
            else
                BeginGroundItemPickup(pickupObject);
            GetViewport().SetInputAsHandled();
            return;
        }
        bool mouseOnPlayerCell = _combatController?.MouseCell() == _playerLocation;
        bool mouseOnNearbyItem = pickupObject?.Type == ObjectRenderer.Kind.Item
            && Math.Max(Math.Abs(pickupObject.CellX - _playerLocation.X),
                Math.Abs(pickupObject.CellY - _playerLocation.Y)) <= 1;
        if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left &&
            (mouseOnPlayerCell || mouseOnNearbyItem))
        {
            if (!CanSendMapPickup(_observer, _player?.Dead == true,
                    _playerPoison.HasFlag(PoisonType.Paralysis),
                    _playerPoison.HasFlag(PoisonType.Containment),
                    _player?.DragonRepulsed == true))
            {
                GetViewport().SetInputAsHandled();
                return;
            }
            // 点击玩家当前格或拾取范围内的地面物品都发送 PickUp；
            // 物品精灵的可点击图像区域可能跨越逻辑格边界。
            _pendingNpcClickObjectId = _combatController.MouseObject?.Type == ObjectRenderer.Kind.NPC
                ? _combatController.MouseObject.ObjectID
                : 0;
            SendPickUp();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseButton inspectMouse && inspectMouse.Pressed &&
            inspectMouse.ButtonIndex == MouseButton.Right &&
            (inspectMouse.CtrlPressed || Input.IsKeyPressed(Key.Ctrl)) &&
            _combatController?.MouseObject?.Type == ObjectRenderer.Kind.Player &&
            _combatController.MouseObject.ObjectID != _playerObjectID)
        {
            double now = Godot.Time.GetTicksMsec();
            if (now >= _nextInspectMs)
            {
                _autoRun = false;
                if (_mouseWalker != null) _mouseWalker.AutoRun = false;
                _nextInspectMs = now + 2500.0;
                _net?.Connection?.Enqueue(new C.Inspect
                {
                    Index = _combatController.MouseObject.CharacterIndex,
                    Ranking = false
                });
                if (AutoLoginArgs.InteractionAudit) _interactionInspectSent++;
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseButton npcMouse && npcMouse.Pressed && npcMouse.ButtonIndex == MouseButton.Left &&
            _combatController?.MouseObject?.Type == ObjectRenderer.Kind.NPC)
        {
            _autoRun = false;
            if (_mouseWalker != null) _mouseWalker.AutoRun = false;
            // 原版 MapControl 把 NPC 调用放在 MouseClick/左键释放阶段；
            // 按下只记录对象，若用户按住后移出 NPC，则不应误触发对话。
            _pendingNpcClickObjectId = _combatController.MouseObject.ObjectID;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseButton miningMouse && miningMouse.Pressed && miningMouse.ButtonIndex == MouseButton.Left &&
            _combatController != null)
        {
            var mouseObject = _combatController.MouseObject;
            // 原版仅活着的非掉落对象会抢占矿点；掉落物和死亡对象不阻止挖矿。
            if (mouseObject != null && mouseObject.Type != ObjectRenderer.Kind.Item && !mouseObject.Dead)
                return;
            // 原版 MiningPoint = Functions.Move(玩家格, 鼠标方向) —— 挖鼠标
            // 方向的**第一格**，不是鼠标所在格；仅武器槽 PickAxe 有效。
            var target = _combatController.MouseCell();
            var direction = Functions.DirectionFromPoint(_playerLocation, target);
            var miningPoint = Functions.Move(_playerLocation, direction);
            var mapInfo = Globals.MapInfoList?.Binding.FirstOrDefault(m => m.Index == _playerMapIndex);
            var pickaxe = Equipment.ElementAtOrDefault((int)EquipmentSlot.Weapon);
            bool inBounds = _mapView?.Map != null
                && miningPoint.X >= 0 && miningPoint.Y >= 0
                && miningPoint.X < _mapView.Map.Width && miningPoint.Y < _mapView.Map.Height;
            bool cellFlag = inBounds && _mapView.Map.Cells[miningPoint.X, miningPoint.Y].Flag;
            bool adjacent = Math.Max(Math.Abs(miningPoint.X - _playerLocation.X),
                Math.Abs(miningPoint.Y - _playerLocation.Y)) == 1;
            if (CanMineNow(mapInfo?.CanMine == true, pickaxe?.Info?.ItemEffect,
                pickaxe?.CurrentDurability ?? 0, pickaxe?.Info?.Durability ?? 0,
                inBounds, cellFlag, adjacent, IsMounted))
            {
                _mining = true;
                _miningPoint = miningPoint;
                _playerDirection = direction;
                // 发包交给 _Process 的 TryContinueMining：原版按下后
                // Mining=true 并不立即发包，要等 AttackTime 冷却。
                GetViewport().SetInputAsHandled();
            }
        }

    }

    private bool TrySendNpcCall(uint objectId)
    {
        double now = Godot.Time.GetTicksMsec();
        if (now < _nextNpcCallMs) return false;
        _npcObjectId = objectId;
        _net?.Connection?.Enqueue(new C.NPCCall { ObjectID = objectId });
        _nextNpcCallMs = now + 1000.0;
        return true;
    }

    /// <summary>
    /// 原版 MapControl.ProcessInput 的 Mining 状态机（1045-1071）：
    /// 条件全部满足且 AttackTime 冷却到 → 重复 AttemptAction(Mining)；
    /// 任一条件失效（换武器/耐久 0/移动/骑马/矿点越界）→ Mining=false。
    /// </summary>
    private void TryContinueMining()
    {
        if (!_mining) return;
        var pickaxe = Equipment.ElementAtOrDefault((int)EquipmentSlot.Weapon);
        var mapInfo = Globals.MapInfoList?.Binding.FirstOrDefault(m => m.Index == _playerMapIndex);
        bool inBounds = _mapView?.Map != null
            && _miningPoint.X >= 0 && _miningPoint.Y >= 0
            && _miningPoint.X < _mapView.Map.Width && _miningPoint.Y < _mapView.Map.Height;
        bool cellFlag = inBounds && _mapView.Map.Cells[_miningPoint.X, _miningPoint.Y].Flag;
        bool adjacent = Math.Max(Math.Abs(_miningPoint.X - _playerLocation.X),
            Math.Abs(_miningPoint.Y - _playerLocation.Y)) == 1;
        bool stillValid = CanMineNow(mapInfo?.CanMine == true, pickaxe?.Info?.ItemEffect,
            pickaxe?.CurrentDurability ?? 0, pickaxe?.Info?.Durability ?? 0,
            inBounds, cellFlag, adjacent, IsMounted);
        if (!stillValid)
        {
            _mining = false;
            return;
        }
        double now = Godot.Time.GetTicksMsec();
        if (now < _nextMiningMs) return;
        var dir = Functions.DirectionFromPoint(_playerLocation, _miningPoint);
        if (_net?.Connection?.Connected == true)
            _net.Connection.Enqueue(new C.Mining { Direction = dir });
        // 原版 Mining 走 AttemptAction 的 AttackTime 冷却，且超重惩罚
        // 再翻倍（UserObject.cs Mining 分支：base + overweight + neutralize
        // 叠加，超重 = x3）。采矿间隔与普通攻击不同，不能共用攻击公式。
        _nextMiningMs = now + ComputeMiningIntervalMs(Globals.AttackDelay,
            PlayerStats[Stat.AttackSpeed], Globals.ASpeedRate,
            BagWeight > _playerStats[Stat.BagWeight],
            _playerPoison.HasFlag(PoisonType.Neutralize));
    }
}
