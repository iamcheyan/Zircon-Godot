using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Godot;
using Library;
using Library.Network;
using G = Library.Network.GeneralPackets;
using C = Library.Network.ClientPackets;
using S = Library.Network.ServerPackets;

namespace ZirconClient.Network;

// Godot 版网络连接: 继承 LibraryCore/BaseConnection, 用 C# event 通知 UI
public partial class ServerConnection : BaseConnection
{
    private readonly string _checkSum;
    private bool _disconnectNotified;
    protected override TimeSpan TimeOutDelay => TimeSpan.FromSeconds(30);
    public ServerConnection(TcpClient client) : base(client)
    {
        const string path = "user://checksum.bin";
        if (FileAccess.FileExists(path))
            _checkSum = FileAccess.GetFileAsString(path).Trim();
        if (string.IsNullOrEmpty(_checkSum))
        {
            _checkSum = Guid.NewGuid().ToString("N")[..20];
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
            file.StoreString(_checkSum);
        }
    }

    public override void TryDisconnect() => NotifyDisconnected(closeTransport: true);
    public override void TrySendDisconnect(Packet p) { SendDisconnect(p); }
    public void StartReceive() { BeginReceive(); }
    public int Ping { get; private set; }

    /// <summary>
    /// 统一处理服务器主动断开、TCP EOF、轮询异常和客户端主动退出。
    /// 原版只会让断线状态进入一次 UI 生命周期；Godot 侧也必须保证事件只发一次。
    /// </summary>
    public void NotifyDisconnected(bool closeTransport)
    {
        Connected = false;
        if (closeTransport)
        {
            try { Client?.Close(); }
            catch (Exception ex) { GD.PrintErr($"[Net] 关闭 TCP 失败: {ex.Message}"); }
        }

        if (_disconnectNotified) return;
        _disconnectNotified = true;
        DisconnectedEvent?.Invoke();
    }

    public override void Disconnect()
    {
        NotifyDisconnected(closeTransport: true);
        base.Disconnect();
    }

    protected override void ProcessUnhandledPacket(Packet p)
    {
        GD.Print($"[Net] 未处理包: {p.PacketType.Name}");
        UnhandledPacket?.Invoke(p.PacketType.Name);
    }

    // UI 层订阅这些事件
    public event Action<string> UnhandledPacket;
    public event Action ConnectedEvent;
    public event Action<string, string> VersionOK;       // version, dbKeyInfo
    public event Action DisconnectedEvent;
    public event Action<LoginResult, string, List<SelectInfo>, string> LoginResultEvent;
    public event Action<ChangePasswordResult> ChangePasswordResultEvent;
    public event Action<RequestPasswordResetResult> RequestPasswordResetResultEvent;
    public event Action<ResetPasswordResult> ResetPasswordResultEvent;
    public event Action<ActivationResult> ActivationResultEvent;
    public event Action<RequestActivationKeyResult> RequestActivationKeyResultEvent;
    public event Action<IList<ClientBlockInfo>> BlockListEvent;
    public event Action<ClientBlockInfo> BlockAddedEvent;
    public event Action<int> BlockRemovedEvent;
    public event Action<NewAccountResult> NewAccountResultEvent;
    public event Action<NewCharacterResult, SelectInfo> NewCharacterResultEvent;
    public event Action<DeleteCharacterResult, int> DeleteCharacterResultEvent;
    public event Action<StartGameResult, StartInformation> StartGameResultEvent;
    public event Action<int, int> MapChangedEvent;       // mapIndex, instanceIndex
    public event Action<MirDirection, System.Drawing.Point> UserLocationEvent;
    public event Action<uint, MirDirection, System.Drawing.Point, int, TimeSpan, bool> ObjectMoveEvent; // objectID, dir, loc, distance, slow, mapChanged
    public event Action<S.ObjectIdle> ObjectIdleEvent;
    public event Action<S.ObjectShow> ObjectShowEvent;
    public event Action<S.ObjectHide> ObjectHideEvent;
    public event Action<S.ObjectNameColour> ObjectNameColourEvent;
    public event Action<S.ObjectPetOwnerChanged> ObjectPetOwnerChangedEvent;
    public event Action<S.ObjectLeveled> ObjectLeveledEvent;
    public event Action<S.ObjectRevive> ObjectReviveEvent;
    public event Action<S.ObjectStats> ObjectStatsEvent;
    public event Action<S.ObjectHarvested> ObjectHarvestedEvent;
    public event Action<S.CompanionShapeUpdate> CompanionShapeUpdateEvent;
    public event Action<S.SafeZoneChanged> SafeZoneChangedEvent;
    public event Action<S.CombatTime> CombatTimeEvent;
    public event Action<S.GuildChanged> GuildChangedEvent;
    public event Action<S.GuildWarStarted> GuildWarStartedEvent;
    public event Action<S.GuildWarFinished> GuildWarFinishedEvent;
    public event Action<S.GuildWar> GuildWarEvent;
    public event Action<S.MarriageInfo> MarriageInfoEvent;
    public event Action<S.MarriageRemoveRing> MarriageRemoveRingEvent;
    public event Action<S.MarriageMakeRing> MarriageMakeRingEvent;
    public event Action<S.MarriageOnlineChanged> MarriageOnlineChangedEvent;
    public event Action<S.MailSend> MailSendEvent;
    public event Action<S.MarketPlaceStoreBuy> MarketPlaceStoreBuyEvent;
    public event Action<S.MountFailed> MountFailedEvent;
    public event Action<S.TradeAddItem> TradeAddItemEvent;
    public event Action<S.TradeAddGold> TradeAddGoldEvent;
    public event Action<S.DataObjectLocation> DataObjectLocationEvent;
    public event Action<S.DataObjectRemove> DataObjectRemoveEvent;
    public event Action<S.Inspect> InspectEvent;
    public event Action<S.ObjectMonster> ObjectMonsterEvent;
    public event Action<S.ObjectPlayer> ObjectPlayerEvent;
    public event Action<S.PlayerUpdate> PlayerUpdateEvent;
    public event Action<S.PlayerChangeUpdate> PlayerChangeUpdateEvent;
    public event Action<S.HelmetToggle> HelmetToggleEvent;
    public event Action<S.DisciplineUpdate> DisciplineUpdateEvent;
    public event Action<long> DisciplineExperienceChangedEvent;
    public event Action<S.MarriageInvite> MarriageInviteEvent;
    public event Action<S.ObjectNPC> ObjectNPCEvent;
    public event Action<S.NPCResponse> NPCResponseEvent;
    public event Action<S.NPCStorage> NPCStorageEvent;
    public event Action NPCClosedEvent;
    public event Action<S.NPCRepair> NPCRepairEvent;
    public event Action<S.BundleOpen> BundleOpenEvent;
    public event Action BundleCloseEvent;
    public event Action<S.FortuneUpdate> FortuneUpdateEvent;
    public event Action<S.LootBoxOpen> LootBoxOpenEvent;
    public event Action LootBoxCloseEvent;
    public event Action<S.NPCSocketItem> NPCSocketItemEvent;
    public event Action<S.NPCSocketCombine> NPCSocketCombineEvent;
    public event Action<S.SetTimer> SetTimerEvent;
    public event Action<S.ObjectFishing> ObjectFishingEvent;
    public event Action<S.FishingStats> FishingStatsEvent;
    public event Action<S.AutoPathChanged> AutoPathChangedEvent;
    public event Action<S.ObjectTaming> ObjectTamingEvent;
    public event Action<S.JoinInstance> JoinInstanceEvent;
    public event Action<S.UserMilestones> UserMilestonesEvent;
    public event Action<S.MilestoneEarned> MilestoneEarnedEvent;
    public event Action<S.MarketPlaceConsign> MarketPlaceConsignEvent;
    public event Action<S.MarketPlaceSearch> MarketPlaceSearchEvent;
    public event Action<S.MarketPlaceSearchCount> MarketPlaceSearchCountEvent;
    public event Action<S.MarketPlaceSearchIndex> MarketPlaceSearchIndexEvent;
    public event Action<S.MarketPlaceBuy> MarketPlaceBuyEvent;
    public event Action<S.MarketPlaceConsignChanged> MarketPlaceConsignChangedEvent;
    public event Action<S.MarketPlaceHistory> MarketPlaceHistoryEvent;
    public event Action<S.ObjectItem> ObjectItemEvent;
    public event Action<S.Chat> ChatEvent;
    public event Action<uint, string> GroupMemberEvent;
    public event Action<uint> GroupRemoveEvent;
    public event Action<S.GroupLFG> GroupLFGEvent;
    public event Action<S.GroupInvite> GroupInviteEvent;
    public event Action<S.GroupRequest> GroupRequestEvent;
    public event Action<S.GroupUpdate> GroupUpdateEvent;
    public event Action<S.GroupSwitch> GroupSwitchEvent;
    public event Action<List<ClientMailInfo>> MailListEvent;
    public event Action<ClientMailInfo> MailNewEvent;
    public event Action<int> MailDeleteEvent;
    public event Action<int, int> MailItemDeleteEvent;
    public event Action<S.FriendUpdate> FriendUpdateEvent;
    public event Action<S.FriendAdd> FriendAddEvent;
    public event Action<S.FriendRemove> FriendRemoveEvent;
    public event Action<S.TradeOpen> TradeOpenEvent;
    public event Action<S.TradeRequest> TradeRequestEvent;
    public event Action<S.NPCRoll> NPCRollEvent;
    public event Action TradeCloseEvent;
    public event Action<ClientUserItem> TradeItemAddedEvent;
    public event Action<long> TradeGoldAddedEvent;
    public event Action TradeUnlockEvent;
    public event Action<S.Rankings> RankingsEvent;
    public event Action<uint> ObjectRemoveEvent;
    public event Action<uint, MirDirection, System.Drawing.Point, TimeSpan> ObjectTurnEvent;
    public event Action<S.ObjectHarvest> ObjectHarvestEvent;
    public event Action<S.ObjectMount> ObjectMountEvent;
    public event Action<S.ObjectDash> ObjectDashEvent;
    public event Action<S.ObjectPushed> ObjectPushedEvent;
    public event Action<S.ObjectMining> ObjectMiningEvent;
    // M5 战斗
    public event Action<S.ObjectAttack> ObjectAttackEvent;
    public event Action<S.ObjectRangeAttack> ObjectRangeAttackEvent;
    public event Action<uint, MirDirection, System.Drawing.Point, MagicType, List<uint>, List<System.Drawing.Point>, bool> ObjectMagicEvent; // id, dir, loc, type, targets, locations, cast
    public event Action<S.ObjectProjectile> ObjectProjectileEvent;
    public event Action<S.ObjectSpell> ObjectSpellEvent;
    public event Action<S.ObjectSpellChanged> ObjectSpellChangedEvent;
    public event Action<uint, BuffType, int> ObjectBuffAddEvent;
    public event Action<uint, BuffType> ObjectBuffRemoveEvent;
    public event Action<uint, PoisonType> ObjectPoisonEvent;
    public event Action<uint, Effect> ObjectEffectEvent;
    public event Action<System.Drawing.Point, Effect, MirDirection> MapEffectEvent;
    public event Action<uint, int, bool, bool, bool, bool> HealthChangedEvent; // id, change, miss, block, critical, resist
    public event Action<uint, int, int, bool> DataObjectHealthManaEvent; // id, health, mana, dead
    public event Action<uint, int, int> DataObjectMaxHealthManaEvent; // id, maxHealth, maxMana
    public event Action<uint, int, int, int, int, bool> DataObjectMonsterEvent; // id, health, maxHealth, light, monsterIndex, dead
    public event Action<uint> ObjectDiedEvent;
    public event Action<uint, MirDirection, System.Drawing.Point, uint, Element> ObjectStruckEvent; // id, dir, loc, attackerID, element
    public event Action<S.StatsUpdate> StatsUpdateEvent; // 完整属性
    public event Action<float> DayTimeChangedEvent;
    public event Action<TimeOfDay, string> TimeOfDayChangedEvent;
    // M12 HUD: 等级/经验/蓝/专注/Buff
    public event Action<S.LevelChanged> LevelChangedEvent;
    public event Action<decimal> GainedExperienceEvent;    // amount
    public event Action<decimal> InformMaxExperienceEvent; // maxExperience
    public event Action<uint, int> ManaChangedEvent;       // objectID, change
    public event Action<uint, int> FocusChangedEvent;      // objectID, change
    public event Action<S.BuffAdd> BuffAddEvent;
    public event Action<int> BuffRemoveEvent;              // index
    public event Action<S.BuffChanged> BuffChangedEvent;
    public event Action<S.BuffTime> BuffTimeEvent;
    public event Action<int, bool> BuffPausedEvent;        // index, paused
    public event Action<AttackMode> AttackModeChangedEvent; // 服务端回显
    public event Action<PetMode> PetModeChangedEvent;
    // M9 物品系统
    public event Action<S.ItemsGained> ItemsGainedEvent;
    public event Action<S.ItemMove> ItemMoveEvent;
    public event Action<S.ItemSort> ItemSortEvent;
    public event Action<S.ItemSplit> ItemSplitEvent;
    public event Action<S.ItemDelete> ItemDeleteEvent;
    public event Action<S.ItemLock> ItemLockEvent;
    public event Action<S.ItemUseDelay> ItemUseDelayEvent;
    public event Action<S.ItemChanged> ItemChangedEvent;
    public event Action<S.ItemStatsChanged> ItemStatsChangedEvent;
    public event Action<S.ItemStatsRefreshed> ItemStatsRefreshedEvent;
    public event Action<S.ItemDurability> ItemDurabilityEvent;
    public event Action<S.ItemExperience> ItemExperienceEvent;
    public event Action<S.ItemsChanged> ItemsChangedEvent;
    public event Action<S.CompanionAdopt> CompanionAdoptEvent;
    public event Action<S.CompanionUpdate> CompanionUpdateEvent;
    public event Action<S.CompanionItemsGained> CompanionItemsGainedEvent;
    public event Action<S.CompanionWeightUpdate> CompanionWeightUpdateEvent;
    public event Action<S.CompanionSkillUpdate> CompanionSkillUpdateEvent;
    public event Action<S.CompanionRetrieve> CompanionRetrieveEvent;
    public event Action<S.CompanionRelease> CompanionReleaseEvent;
    public event Action<S.CompanionStore> CompanionStoreEvent;
    public event Action<S.CompanionUnlock> CompanionUnlockEvent;
    public event Action<S.GameStoreData> GameStoreDataEvent;
    public event Action<S.GameStoreTopItems> GameStoreTopItemsEvent;
    public event Action<S.GameStoreFavouriteChanged> GameStoreFavouriteChangedEvent;
    public event Action<S.GameStoreGift> GameStoreGiftEvent;
    public event Action<S.GuildNewItem> GuildNewItemEvent;
    public event Action<S.GuildGetItem> GuildGetItemEvent;
    public event Action<S.GuildInfo> GuildInfoEvent;
    public event Action<S.GuildNoticeChanged> GuildNoticeChangedEvent;
    public event Action<S.GuildUpdate> GuildUpdateEvent;
    public event Action<S.GuildMemberOffline> GuildMemberOfflineEvent;
    public event Action<S.GuildMemberOnline> GuildMemberOnlineEvent;
    public event Action<S.GuildMemberContribution> GuildMemberContributionEvent;
    public event Action<S.GuildFundsChanged> GuildFundsChangedEvent;
    public event Action<S.GuildInvite> GuildInviteEvent;
    public event Action<S.QuestChanged> QuestChangedEvent;
    public event Action<S.QuestCancelled> QuestCancelledEvent;
    public event Action<S.GuildCastleInfo> GuildCastleInfoEvent;
    public event Action<S.GuildConquestDate> GuildConquestDateEvent;
    public event Action<S.GuildConquestStarted> GuildConquestStartedEvent;
    public event Action<S.GuildConquestFinished> GuildConquestFinishedEvent;
    public event Action<S.RefineList> RefineListEvent;
    public event Action<S.NPCRefinementStone> NPCRefinementStoneEvent;
    public event Action<S.NPCRefine> NPCRefineEvent;
    public event Action<S.NPCMasterRefine> NPCMasterRefineEvent;
    public event Action<S.NPCAccessoryLevelUp> NPCAccessoryLevelUpEvent;
    public event Action<S.NPCAccessoryUpgrade> NPCAccessoryUpgradeEvent;
    public event Action<S.NPCAccessoryRefine> NPCAccessoryRefineEvent;
    public event Action<S.NPCWeaponCraft> NPCWeaponCraftEvent;
    public event Action<S.NPCRefineRetrieve> NPCRefineRetrieveEvent;
    public event Action<S.ItemAcessoryRefined> ItemAcessoryRefinedEvent;
    public event Action<S.ReviveTimers> ReviveTimersEvent;
    public event Action<S.ObservableSwitch> ObservableSwitchEvent;
    public event Action<S.GuildCreate> GuildCreateEvent;
    public event Action<S.GuildKick> GuildKickEvent;
    public event Action<S.GuildTax> GuildTaxEvent;
    public event Action<S.GuildIncreaseMember> GuildIncreaseMemberEvent;
    public event Action<S.GuildIncreaseStorage> GuildIncreaseStorageEvent;
    public event Action<S.GuildInviteMember> GuildInviteMemberEvent;
    public event Action<S.GuildDayReset> GuildDayResetEvent;
    public event Action<S.SendCompanionFilters> SendCompanionFiltersEvent;
    public event Action<S.RankSearch> RankSearchEvent;
    public event Action<S.GameLogout> GameLogoutEvent;
    public event Action<S.SelectLogout> SelectLogoutEvent;
    public event Action<S.StartObserver> StartObserverEvent;
    public event Action<S.DataObjectPlayer> DataObjectPlayerEvent;
    public event Action<S.DataObjectItem> DataObjectItemEvent;
    public event Action<int, long> CurrencyChangedEvent;   // currencyIndex, amount
    public event Action<int, int, int> WeightUpdateEvent;  // bag, wear, hand
    public event Action<int> StorageSizeEvent;
    // StartGame 突发包缓冲: GameScene._Ready 前的事件订阅来不及, 这些包在订阅前已被 Process 丢弃。
    // Process 里 Enqueue + Invoke 双发; GameScene._Ready 一次性 Drain 积压, 之后靠事件接实时包。
    public readonly Queue<S.ObjectMove> PendingMoves = new();
    public readonly Queue<S.ObjectMonster> PendingMonsters = new();
    public readonly Queue<S.ObjectPlayer> PendingPlayers = new();
    public readonly Queue<S.ObjectNPC> PendingNPCs = new();
    public readonly Queue<S.ObjectItem> PendingItems = new();
    public readonly Queue<S.Chat> PendingChats = new();
    public readonly Queue<uint> PendingRemoves = new();
    public readonly Queue<S.ObjectTurn> PendingTurns = new();
    public readonly Queue<S.ObjectAttack> PendingAttacks = new();
    public readonly Queue<S.ObjectMagic> PendingMagics = new();
    public readonly Queue<S.ObjectProjectile> PendingProjectiles = new();
    public readonly Queue<S.ObjectSpell> PendingSpells = new();
    public readonly Queue<S.ObjectSpellChanged> PendingSpellChanges = new();
    public readonly Queue<S.ObjectEffect> PendingObjectEffects = new();
    public readonly Queue<S.MapEffect> PendingMapEffects = new();

    /// <summary>
    /// StartGame 之前事件订阅可能尚未建立，包需要暂存；进入 GameScene 后包只应派发一次。
    /// 原版没有“入队后再次排空”的重复路径，运行态必须关闭缓冲。
    /// </summary>
    public bool BufferPendingPackets { get; set; } = true;

    public void StopPendingPacketBuffering()
    {
        BufferPendingPackets = false;
    }

    /// <summary>切图时丢弃尚未排空的旧世界包，避免旧地图对象在新地图重建后复活。</summary>
    public void ClearPendingWorldPackets()
    {
        foreach (var field in GetType().GetFields(System.Reflection.BindingFlags.Instance |
                                                   System.Reflection.BindingFlags.Public |
                                                   System.Reflection.BindingFlags.NonPublic))
        {
            if (!field.Name.StartsWith("Pending", StringComparison.Ordinal)) continue;
            var queue = field.GetValue(this);
            queue?.GetType().GetMethod("Clear", Type.EmptyTypes)?.Invoke(queue, null);
        }
    }
    public readonly Queue<S.HealthChanged> PendingHealthChanges = new();
    public readonly Queue<S.DataObjectHealthMana> PendingHealthManas = new();
    public readonly Queue<S.DataObjectMaxHealthMana> PendingMaxHealthManas = new();
    public readonly Queue<S.DataObjectMonster> PendingDataMonsters = new();
    public readonly Queue<uint> PendingDeaths = new();
    public readonly Queue<S.ObjectStruck> PendingStruck = new();
    public readonly Queue<S.StatsUpdate> PendingStats = new();
    public readonly Queue<S.LevelChanged> PendingLevelChanges = new();
    public readonly Queue<decimal> PendingGainedExperience = new();
    public readonly Queue<decimal> PendingMaxExperience = new();
    public readonly Queue<S.ManaChanged> PendingManaChanges = new();
    public readonly Queue<S.FocusChanged> PendingFocusChanges = new();
    public readonly Queue<S.BuffAdd> PendingBuffAdds = new();
    public readonly Queue<int> PendingBuffRemoves = new();
    public readonly Queue<S.BuffChanged> PendingBuffChangeds = new();
    public readonly Queue<S.BuffTime> PendingBuffTimes = new();
    public readonly Queue<(int, bool)> PendingBuffPauseds = new();
    public readonly Queue<S.ItemsGained> PendingItemsGained = new();
    public readonly Queue<S.ItemMove> PendingItemMoves = new();
    public readonly Queue<S.ItemSort> PendingItemSorts = new();
    public readonly Queue<S.ItemSplit> PendingItemSplits = new();
    public readonly Queue<S.ItemDelete> PendingItemDeletes = new();
    public readonly Queue<S.ItemLock> PendingItemLocks = new();
    public readonly Queue<S.ItemUseDelay> PendingItemUseDelays = new();
    public readonly Queue<S.ItemChanged> PendingItemChangeds = new();
    public readonly Queue<S.ItemStatsChanged> PendingItemStatsChangeds = new();
    public readonly Queue<S.ItemStatsRefreshed> PendingItemStatsRefresheds = new();
    public readonly Queue<S.ItemDurability> PendingItemDurabilities = new();
    public readonly Queue<S.ItemExperience> PendingItemExperiences = new();
    public readonly Queue<S.ItemsChanged> PendingItemsChangeds = new();
    public readonly Queue<(int, long)> PendingCurrencyChangeds = new();
    public readonly Queue<(int, int, int)> PendingWeightUpdates = new();
    public readonly Queue<int> PendingStorageSizes = new();

    public void Process(G.Connected p)
    {
        ConnectedEvent?.Invoke();
        Enqueue(new G.Connected());
    }
    public void Process(G.GoodVersion p)
    {
        Enqueue(new C.SelectLanguage { Language = "Chinese" });
        VersionOK?.Invoke(p.SystemDatabaseVersion ?? "", p.DatabaseKey?.Length.ToString() ?? "null");
    }
    public void Process(G.Disconnect p)
    {
        GD.Print($"[Net] Disconnect: {p.Reason}");
        NotifyDisconnected(closeTransport: true);
    }
    public void Process(G.Ping p) { Enqueue(new G.Ping()); }
    public void Process(G.PingResponse p) { Ping = p?.Ping ?? 0; }

    // S.Login.Items = 仓库物品 (登录时到达, GameScene 在 StartGame 后消费 FillStorage)
    public List<ClientUserItem> PendingStorageItems = new();

    public void Process(S.Login p)
    {
        PendingStorageItems = p.Items ?? new List<ClientUserItem>();
        BlockListEvent?.Invoke(p.BlockList ?? new List<ClientBlockInfo>());
        Globals.IsGM = p.IsGM;
        LoginResultEvent?.Invoke(p.Result, p.Message ?? "", p.Characters, p.Address ?? string.Empty);
    }
    public void Process(S.ChangePassword p) => ChangePasswordResultEvent?.Invoke(p.Result);
    public void Process(S.RequestPasswordReset p) => RequestPasswordResetResultEvent?.Invoke(p.Result);
    public void Process(S.ResetPassword p) => ResetPasswordResultEvent?.Invoke(p.Result);
    public void Process(S.Activation p) => ActivationResultEvent?.Invoke(p.Result);
    public void Process(S.RequestActivationKey p) => RequestActivationKeyResultEvent?.Invoke(p.Result);
    public void Process(S.BlockAdd p) => BlockAddedEvent?.Invoke(p.Info);
    public void Process(S.BlockRemove p) => BlockRemovedEvent?.Invoke(p.Index);
    public void Process(S.DisciplineUpdate p) => DisciplineUpdateEvent?.Invoke(p);
    public void Process(S.DisciplineExperienceChanged p) => DisciplineExperienceChangedEvent?.Invoke(p.Experience);
    public void Process(S.MarriageInvite p) => MarriageInviteEvent?.Invoke(p);
    public void Process(S.NewAccount p)
    {
        NewAccountResultEvent?.Invoke(p.Result);
    }
    public void Process(S.NewCharacter p)
    {
        NewCharacterResultEvent?.Invoke(p.Result, p.Character);
    }

    public void Process(S.DeleteCharacter p)
    {
        DeleteCharacterResultEvent?.Invoke(p.Result, p.DeletedIndex);
    }
    public void Process(S.StartGame p)
    {
        GD.Print($"[Net] 收到 S.StartGame: Result={p.Result}, Magics={p.StartInformation?.Magics?.Count ?? 0}, 前3个Set1=[{string.Join(",", (p.StartInformation?.Magics ?? new()).Take(3).Select(m => $"{m.InfoIndex}:{m.Set1Key}"))}]");
        StartGameResultEvent?.Invoke(p.Result, p.StartInformation);
    }
    public void Process(S.MapChanged p)
    {
        // 启动阶段的 MapChanged 只是状态通知，不能清掉随后随 StartGame 一起排空的初始对象。
        // 运行态切图才清理旧地图残留包。
        if (!BufferPendingPackets) ClearPendingWorldPackets();
        MapChangedEvent?.Invoke(p.MapIndex, p.InstanceIndex);
    }
    public void Process(S.UserLocation p)
    {
        UserLocationEvent?.Invoke(p.Direction, p.Location);
    }

    public void Process(S.ObjectMove p)
    {
        if (BufferPendingPackets) PendingMoves.Enqueue(p);
        ObjectMoveEvent?.Invoke(p.ObjectID, p.Direction, p.Location, p.Distance, p.Slow, p.MapChanged);
    }
    public void Process(S.ObjectIdle p) => ObjectIdleEvent?.Invoke(p);
    public void Process(S.ObjectShow p) => ObjectShowEvent?.Invoke(p);
    public void Process(S.ObjectHide p) => ObjectHideEvent?.Invoke(p);
    public void Process(S.ObjectNameColour p) => ObjectNameColourEvent?.Invoke(p);
    public void Process(S.ObjectPetOwnerChanged p) => ObjectPetOwnerChangedEvent?.Invoke(p);
    public void Process(S.ObjectLeveled p) => ObjectLeveledEvent?.Invoke(p);
    public void Process(S.ObjectRevive p) => ObjectReviveEvent?.Invoke(p);
    public void Process(S.ObjectStats p) => ObjectStatsEvent?.Invoke(p);
    public void Process(S.ObjectHarvested p) => ObjectHarvestedEvent?.Invoke(p);
    public void Process(S.CompanionShapeUpdate p) => CompanionShapeUpdateEvent?.Invoke(p);
    public void Process(S.SafeZoneChanged p) => SafeZoneChangedEvent?.Invoke(p);
    public void Process(S.CombatTime p) => CombatTimeEvent?.Invoke(p);
    public void Process(S.GuildChanged p) => GuildChangedEvent?.Invoke(p);
    public void Process(S.GuildWarStarted p) => GuildWarStartedEvent?.Invoke(p);
    public void Process(S.GuildWarFinished p) => GuildWarFinishedEvent?.Invoke(p);
    public void Process(S.GuildWar p) => GuildWarEvent?.Invoke(p);
    public void Process(S.MarriageInfo p) => MarriageInfoEvent?.Invoke(p);
    public void Process(S.MarriageRemoveRing p) => MarriageRemoveRingEvent?.Invoke(p);
    public void Process(S.MarriageMakeRing p) => MarriageMakeRingEvent?.Invoke(p);
    public void Process(S.MarriageOnlineChanged p) => MarriageOnlineChangedEvent?.Invoke(p);
    public void Process(S.MailSend p) => MailSendEvent?.Invoke(p);
    public void Process(S.MarketPlaceStoreBuy p) => MarketPlaceStoreBuyEvent?.Invoke(p);
    public void Process(S.MountFailed p) => MountFailedEvent?.Invoke(p);
    public void Process(S.TradeAddItem p) => TradeAddItemEvent?.Invoke(p);
    public void Process(S.TradeAddGold p) => TradeAddGoldEvent?.Invoke(p);
    public void Process(S.DataObjectLocation p) => DataObjectLocationEvent?.Invoke(p);
    public void Process(S.DataObjectRemove p) => DataObjectRemoveEvent?.Invoke(p);
    public void Process(S.ItemAcessoryRefined p) => ItemAcessoryRefinedEvent?.Invoke(p);
    public void Process(S.ReviveTimers p) => ReviveTimersEvent?.Invoke(p);
    public void Process(S.ObservableSwitch p) => ObservableSwitchEvent?.Invoke(p);
    public void Process(S.GuildCreate p) => GuildCreateEvent?.Invoke(p);
    public void Process(S.GuildKick p) => GuildKickEvent?.Invoke(p);
    public void Process(S.GuildTax p) => GuildTaxEvent?.Invoke(p);
    public void Process(S.GuildIncreaseMember p) => GuildIncreaseMemberEvent?.Invoke(p);
    public void Process(S.GuildIncreaseStorage p) => GuildIncreaseStorageEvent?.Invoke(p);
    public void Process(S.GuildInviteMember p) => GuildInviteMemberEvent?.Invoke(p);
    public void Process(S.GuildDayReset p) => GuildDayResetEvent?.Invoke(p);
    public void Process(S.SendCompanionFilters p) => SendCompanionFiltersEvent?.Invoke(p);
    public void Process(S.RankSearch p) => RankSearchEvent?.Invoke(p);
    public void Process(S.GameLogout p) => GameLogoutEvent?.Invoke(p);
    public void Process(S.SelectLogout p) => SelectLogoutEvent?.Invoke(p);
    public void Process(S.StartObserver p) => StartObserverEvent?.Invoke(p);
    public void Process(S.DataObjectPlayer p) => DataObjectPlayerEvent?.Invoke(p);
    public void Process(S.DataObjectItem p) => DataObjectItemEvent?.Invoke(p);
    public void Process(S.Inspect p) => InspectEvent?.Invoke(p);

    public void Process(S.ObjectMonster p)
    {
        if (BufferPendingPackets) PendingMonsters.Enqueue(p);
        ObjectMonsterEvent?.Invoke(p);
    }
    public void Process(S.ObjectPlayer p)
    {
        if (BufferPendingPackets) PendingPlayers.Enqueue(p);
        ObjectPlayerEvent?.Invoke(p);
    }
    public void Process(S.PlayerUpdate p) => PlayerUpdateEvent?.Invoke(p);
    public void Process(S.PlayerChangeUpdate p) => PlayerChangeUpdateEvent?.Invoke(p);
    public void Process(S.HelmetToggle p) => HelmetToggleEvent?.Invoke(p);

    public void Process(S.ObjectNPC p)
    {
        if (BufferPendingPackets) PendingNPCs.Enqueue(p);
        ObjectNPCEvent?.Invoke(p);
    }
    public void Process(S.NPCResponse p) => NPCResponseEvent?.Invoke(p);
    public void Process(S.NPCStorage p) => NPCStorageEvent?.Invoke(p);
    public void Process(S.NPCClose p) => NPCClosedEvent?.Invoke();
    public void Process(S.NPCRepair p) => NPCRepairEvent?.Invoke(p);
    public void Process(S.NPCRefinementStone p) => NPCRefinementStoneEvent?.Invoke(p);
    public void Process(S.NPCRefine p) => NPCRefineEvent?.Invoke(p);
    public void Process(S.NPCMasterRefine p) => NPCMasterRefineEvent?.Invoke(p);
    public void Process(S.NPCAccessoryLevelUp p) => NPCAccessoryLevelUpEvent?.Invoke(p);
    public void Process(S.NPCAccessoryUpgrade p) => NPCAccessoryUpgradeEvent?.Invoke(p);
    public void Process(S.NPCAccessoryRefine p) => NPCAccessoryRefineEvent?.Invoke(p);
    public void Process(S.NPCWeaponCraft p) => NPCWeaponCraftEvent?.Invoke(p);
    public void Process(S.NPCRefineRetrieve p) => NPCRefineRetrieveEvent?.Invoke(p);
    public void Process(S.BundleOpen p) => BundleOpenEvent?.Invoke(p);
    public void Process(S.BundleClose p) => BundleCloseEvent?.Invoke();
    public void Process(S.FortuneUpdate p) => FortuneUpdateEvent?.Invoke(p);
    public void Process(S.LootBoxOpen p) => LootBoxOpenEvent?.Invoke(p);
    public void Process(S.LootBoxClose p) => LootBoxCloseEvent?.Invoke();
    public void Process(S.NPCSocketItem p) => NPCSocketItemEvent?.Invoke(p);
    public void Process(S.NPCSocketCombine p) => NPCSocketCombineEvent?.Invoke(p);
    public void Process(S.SetTimer p) => SetTimerEvent?.Invoke(p);
    public void Process(S.ObjectFishing p) => ObjectFishingEvent?.Invoke(p);
    public void Process(S.FishingStats p) => FishingStatsEvent?.Invoke(p);
    public void Process(S.AutoPathChanged p) => AutoPathChangedEvent?.Invoke(p);
    public void Process(S.ObjectTaming p) => ObjectTamingEvent?.Invoke(p);
    public void Process(S.JoinInstance p) => JoinInstanceEvent?.Invoke(p);
    public void Process(S.UserMilestones p) => UserMilestonesEvent?.Invoke(p);
    public void Process(S.MilestoneEarned p) => MilestoneEarnedEvent?.Invoke(p);
    public void Process(S.MarketPlaceConsign p) => MarketPlaceConsignEvent?.Invoke(p);
    public void Process(S.MarketPlaceSearch p) => MarketPlaceSearchEvent?.Invoke(p);
    public void Process(S.MarketPlaceSearchCount p) => MarketPlaceSearchCountEvent?.Invoke(p);
    public void Process(S.MarketPlaceSearchIndex p) => MarketPlaceSearchIndexEvent?.Invoke(p);
    public void Process(S.MarketPlaceBuy p) => MarketPlaceBuyEvent?.Invoke(p);
    public void Process(S.MarketPlaceConsignChanged p) => MarketPlaceConsignChangedEvent?.Invoke(p);
    public void Process(S.MarketPlaceHistory p) => MarketPlaceHistoryEvent?.Invoke(p);

    public void Process(S.ObjectItem p)
    {
        if (BufferPendingPackets) PendingItems.Enqueue(p);
        ObjectItemEvent?.Invoke(p);
    }
    public void Process(S.CompanionAdopt p) => CompanionAdoptEvent?.Invoke(p);
    public void Process(S.CompanionUpdate p) => CompanionUpdateEvent?.Invoke(p);
    public void Process(S.CompanionItemsGained p) => CompanionItemsGainedEvent?.Invoke(p);
    public void Process(S.CompanionWeightUpdate p) => CompanionWeightUpdateEvent?.Invoke(p);
    public void Process(S.CompanionSkillUpdate p) => CompanionSkillUpdateEvent?.Invoke(p);
    public void Process(S.CompanionRetrieve p) => CompanionRetrieveEvent?.Invoke(p);
    public void Process(S.CompanionRelease p) => CompanionReleaseEvent?.Invoke(p);
    public void Process(S.CompanionStore p) => CompanionStoreEvent?.Invoke(p);
    public void Process(S.CompanionUnlock p) => CompanionUnlockEvent?.Invoke(p);
    public void Process(S.GameStoreData p) => GameStoreDataEvent?.Invoke(p);
    public void Process(S.GameStoreTopItems p) => GameStoreTopItemsEvent?.Invoke(p);
    public void Process(S.GameStoreFavouriteChanged p) => GameStoreFavouriteChangedEvent?.Invoke(p);
    public void Process(S.GameStoreGift p) => GameStoreGiftEvent?.Invoke(p);
    public void Process(S.GuildNewItem p) => GuildNewItemEvent?.Invoke(p);
    public void Process(S.GuildGetItem p) => GuildGetItemEvent?.Invoke(p);
    public void Process(S.GuildInfo p) => GuildInfoEvent?.Invoke(p);
    public void Process(S.GuildNoticeChanged p) => GuildNoticeChangedEvent?.Invoke(p);
    public void Process(S.GuildUpdate p) => GuildUpdateEvent?.Invoke(p);
    public void Process(S.GuildMemberOffline p) => GuildMemberOfflineEvent?.Invoke(p);
    public void Process(S.GuildMemberOnline p) => GuildMemberOnlineEvent?.Invoke(p);
    public void Process(S.GuildMemberContribution p) => GuildMemberContributionEvent?.Invoke(p);
    public void Process(S.GuildFundsChanged p) => GuildFundsChangedEvent?.Invoke(p);
    public void Process(S.GuildInvite p) => GuildInviteEvent?.Invoke(p);
    public void Process(S.QuestChanged p) => QuestChangedEvent?.Invoke(p);
    public void Process(S.QuestCancelled p) => QuestCancelledEvent?.Invoke(p);
    public void Process(S.GuildCastleInfo p) => GuildCastleInfoEvent?.Invoke(p);
    public void Process(S.GuildConquestDate p) => GuildConquestDateEvent?.Invoke(p);
    public void Process(S.GuildConquestStarted p) => GuildConquestStartedEvent?.Invoke(p);
    public void Process(S.GuildConquestFinished p) => GuildConquestFinishedEvent?.Invoke(p);
    public void Process(S.RefineList p) => RefineListEvent?.Invoke(p);

    public void Process(S.Chat p)
    {
        if (BufferPendingPackets) PendingChats.Enqueue(p);
        ChatEvent?.Invoke(p);
    }

    public void Process(S.GroupMember p) => GroupMemberEvent?.Invoke(p.ObjectID, p.Name);
    public void Process(S.GroupRemove p) => GroupRemoveEvent?.Invoke(p.ObjectID);
    public void Process(S.GroupLFG p) => GroupLFGEvent?.Invoke(p);
    public void Process(S.GroupInvite p) => GroupInviteEvent?.Invoke(p);
    public void Process(S.GroupRequest p) => GroupRequestEvent?.Invoke(p);
    public void Process(S.GroupUpdate p) => GroupUpdateEvent?.Invoke(p);
    public void Process(S.GroupSwitch p) => GroupSwitchEvent?.Invoke(p);
    public void Process(S.MailList p) => MailListEvent?.Invoke(p.Mail ?? new List<ClientMailInfo>());
    public void Process(S.MailNew p) => MailNewEvent?.Invoke(p.Mail);
    public void Process(S.MailDelete p) => MailDeleteEvent?.Invoke(p.Index);
    public void Process(S.MailItemDelete p) => MailItemDeleteEvent?.Invoke(p.Index, p.Slot);
    public void Process(S.FriendUpdate p) => FriendUpdateEvent?.Invoke(p);
    public void Process(S.FriendAdd p) => FriendAddEvent?.Invoke(p);
    public void Process(S.FriendRemove p) => FriendRemoveEvent?.Invoke(p);
    public void Process(S.TradeOpen p) => TradeOpenEvent?.Invoke(p);
    public void Process(S.TradeRequest p) => TradeRequestEvent?.Invoke(p);
    public void Process(S.NPCRoll p) => NPCRollEvent?.Invoke(p);
    public void Process(S.TradeClose p) => TradeCloseEvent?.Invoke();
    public void Process(S.TradeItemAdded p) => TradeItemAddedEvent?.Invoke(p.Item);
    public void Process(S.TradeGoldAdded p) => TradeGoldAddedEvent?.Invoke(p.Gold);
    public void Process(S.TradeUnlock p) => TradeUnlockEvent?.Invoke();
    public void Process(S.Rankings p) => RankingsEvent?.Invoke(p);

    public void Process(S.ObjectRemove p)
    {
        if (BufferPendingPackets) PendingRemoves.Enqueue(p.ObjectID);
        ObjectRemoveEvent?.Invoke(p.ObjectID);
    }

    public void Process(S.ObjectTurn p)
    {
        if (BufferPendingPackets) PendingTurns.Enqueue(p);
        ObjectTurnEvent?.Invoke(p.ObjectID, p.Direction, p.Location, p.Slow);
    }

    public void Process(S.ObjectHarvest p) => ObjectHarvestEvent?.Invoke(p);
    public void Process(S.ObjectMount p) => ObjectMountEvent?.Invoke(p);
    public void Process(S.ObjectDash p) => ObjectDashEvent?.Invoke(p);
    public void Process(S.ObjectPushed p) => ObjectPushedEvent?.Invoke(p);
    public void Process(S.ObjectMining p) => ObjectMiningEvent?.Invoke(p);

    public void Process(S.ObjectAttack p)
    {
        if (BufferPendingPackets) PendingAttacks.Enqueue(p);
        ObjectAttackEvent?.Invoke(p);
    }

    public void Process(S.ObjectRangeAttack p)
    {
        ObjectRangeAttackEvent?.Invoke(p);
    }

    public void Process(S.ObjectMagic p)
    {
        if (BufferPendingPackets) PendingMagics.Enqueue(p);
        ObjectMagicEvent?.Invoke(p.ObjectID, p.Direction, p.CurrentLocation, p.Type, p.Targets, p.Locations, p.Cast);
    }

    public void Process(S.ObjectProjectile p)
    {
        if (BufferPendingPackets) PendingProjectiles.Enqueue(p);
        ObjectProjectileEvent?.Invoke(p);
    }

    public void Process(S.ObjectSpell p)
    {
        if (BufferPendingPackets) PendingSpells.Enqueue(p);
        ObjectSpellEvent?.Invoke(p);
    }

    public void Process(S.ObjectSpellChanged p)
    {
        if (BufferPendingPackets) PendingSpellChanges.Enqueue(p);
        ObjectSpellChangedEvent?.Invoke(p);
    }

    public void Process(S.ObjectBuffAdd p) => ObjectBuffAddEvent?.Invoke(p.ObjectID, p.Type, p.Extra);
    public void Process(S.ObjectBuffRemove p) => ObjectBuffRemoveEvent?.Invoke(p.ObjectID, p.Type);
    public void Process(S.ObjectPoison p) => ObjectPoisonEvent?.Invoke(p.ObjectID, p.Poison);

    public void Process(S.ObjectEffect p)
    {
        if (BufferPendingPackets) PendingObjectEffects.Enqueue(p);
        ObjectEffectEvent?.Invoke(p.ObjectID, p.Effect);
    }

    public void Process(S.MapEffect p)
    {
        if (BufferPendingPackets) PendingMapEffects.Enqueue(p);
        MapEffectEvent?.Invoke(p.Location, p.Effect, p.Direction);
    }

    public void Process(S.HealthChanged p)
    {
        if (BufferPendingPackets) PendingHealthChanges.Enqueue(p);
        HealthChangedEvent?.Invoke(p.ObjectID, p.Change, p.Miss, p.Block, p.Critical, p.Resist);
    }

    public void Process(S.DataObjectHealthMana p)
    {
        if (BufferPendingPackets) PendingHealthManas.Enqueue(p);
        DataObjectHealthManaEvent?.Invoke(p.ObjectID, p.Health, p.Mana, p.Dead);
    }

    public void Process(S.DataObjectMaxHealthMana p)
    {
        if (BufferPendingPackets) PendingMaxHealthManas.Enqueue(p);
        DataObjectMaxHealthManaEvent?.Invoke(p.ObjectID, p.MaxHealth, p.MaxMana);
    }

    public void Process(S.DataObjectMonster p)
    {
        if (BufferPendingPackets) PendingDataMonsters.Enqueue(p);
        int maxHealth = p.Stats != null ? p.Stats[Stat.Health] : 0;
        int light = p.Stats != null ? p.Stats[Stat.Light] : 0;
        DataObjectMonsterEvent?.Invoke(p.ObjectID, p.Health, maxHealth, light, p.MonsterIndex, p.Dead);
    }

    public void Process(S.ObjectDied p)
    {
        if (BufferPendingPackets) PendingDeaths.Enqueue(p.ObjectID);
        ObjectDiedEvent?.Invoke(p.ObjectID);
    }

    public void Process(S.ObjectStruck p)
    {
        if (BufferPendingPackets) PendingStruck.Enqueue(p);
        ObjectStruckEvent?.Invoke(p.ObjectID, p.Direction, p.Location, p.AttackerID, p.Element);
    }

    public void Process(S.StatsUpdate p)
    {
        if (BufferPendingPackets) PendingStats.Enqueue(p);
        StatsUpdateEvent?.Invoke(p);
    }

    public void Process(S.DayChanged p)
    {
        DayTimeChangedEvent?.Invoke(p.DayTime);
    }

    public void Process(S.TimeOfDayChanged p)
    {
        TimeOfDayChangedEvent?.Invoke(p.TimeOfDay, p.TimeOfDayLabel);
    }

    public void Process(S.LevelChanged p)
    {
        if (BufferPendingPackets) PendingLevelChanges.Enqueue(p);
        LevelChangedEvent?.Invoke(p);
    }

    public void Process(S.GainedExperience p)
    {
        if (BufferPendingPackets) PendingGainedExperience.Enqueue(p.Amount);
        GainedExperienceEvent?.Invoke(p.Amount);
    }

    public void Process(S.InformMaxExperience p)
    {
        if (BufferPendingPackets) PendingMaxExperience.Enqueue(p.MaxExperience);
        InformMaxExperienceEvent?.Invoke(p.MaxExperience);
    }

    public void Process(S.ManaChanged p)
    {
        if (BufferPendingPackets) PendingManaChanges.Enqueue(p);
        ManaChangedEvent?.Invoke(p.ObjectID, p.Change);
    }

    public void Process(S.FocusChanged p)
    {
        if (BufferPendingPackets) PendingFocusChanges.Enqueue(p);
        FocusChangedEvent?.Invoke(p.ObjectID, p.Change);
    }

    public void Process(S.BuffAdd p)
    {
        if (BufferPendingPackets) PendingBuffAdds.Enqueue(p);
        BuffAddEvent?.Invoke(p);
    }

    public void Process(S.BuffRemove p)
    {
        if (BufferPendingPackets) PendingBuffRemoves.Enqueue(p.Index);
        BuffRemoveEvent?.Invoke(p.Index);
    }

    public void Process(S.BuffChanged p)
    {
        if (BufferPendingPackets) PendingBuffChangeds.Enqueue(p);
        BuffChangedEvent?.Invoke(p);
    }

    public void Process(S.BuffTime p)
    {
        if (BufferPendingPackets) PendingBuffTimes.Enqueue(p);
        BuffTimeEvent?.Invoke(p);
    }

    public void Process(S.BuffPaused p)
    {
        if (BufferPendingPackets) PendingBuffPauseds.Enqueue((p.Index, p.Paused));
        BuffPausedEvent?.Invoke(p.Index, p.Paused);
    }

    public void Process(S.ChangeAttackMode p)
    {
        AttackModeChangedEvent?.Invoke(p.Mode);
    }

    public void Process(S.ChangePetMode p)
    {
        PetModeChangedEvent?.Invoke(p.Mode);
    }

    // ---- M9 物品系统 ----

    public void Process(S.ItemsGained p)
    {
        if (BufferPendingPackets) PendingItemsGained.Enqueue(p);
        ItemsGainedEvent?.Invoke(p);
    }

    public void Process(S.ItemMove p)
    {
        if (BufferPendingPackets) PendingItemMoves.Enqueue(p);
        ItemMoveEvent?.Invoke(p);
    }

    public void Process(S.ItemSort p)
    {
        if (BufferPendingPackets) PendingItemSorts.Enqueue(p);
        ItemSortEvent?.Invoke(p);
    }

    public void Process(S.ItemSplit p)
    {
        if (BufferPendingPackets) PendingItemSplits.Enqueue(p);
        ItemSplitEvent?.Invoke(p);
    }

    public void Process(S.ItemDelete p)
    {
        if (BufferPendingPackets) PendingItemDeletes.Enqueue(p);
        ItemDeleteEvent?.Invoke(p);
    }

    public void Process(S.ItemLock p)
    {
        if (BufferPendingPackets) PendingItemLocks.Enqueue(p);
        ItemLockEvent?.Invoke(p);
    }

    public void Process(S.ItemUseDelay p)
    {
        if (BufferPendingPackets) PendingItemUseDelays.Enqueue(p);
        ItemUseDelayEvent?.Invoke(p);
    }

    public void Process(S.ItemChanged p)
    {
        if (BufferPendingPackets) PendingItemChangeds.Enqueue(p);
        ItemChangedEvent?.Invoke(p);
    }

    public void Process(S.ItemStatsChanged p)
    {
        if (BufferPendingPackets) PendingItemStatsChangeds.Enqueue(p);
        ItemStatsChangedEvent?.Invoke(p);
    }

    public void Process(S.ItemStatsRefreshed p)
    {
        if (BufferPendingPackets) PendingItemStatsRefresheds.Enqueue(p);
        ItemStatsRefreshedEvent?.Invoke(p);
    }

    public void Process(S.ItemDurability p)
    {
        if (BufferPendingPackets) PendingItemDurabilities.Enqueue(p);
        ItemDurabilityEvent?.Invoke(p);
    }

    public void Process(S.ItemExperience p)
    {
        if (BufferPendingPackets) PendingItemExperiences.Enqueue(p);
        ItemExperienceEvent?.Invoke(p);
    }

    public void Process(S.ItemsChanged p)
    {
        if (BufferPendingPackets) PendingItemsChangeds.Enqueue(p);
        ItemsChangedEvent?.Invoke(p);
    }

    public void Process(S.CurrencyChanged p)
    {
        if (BufferPendingPackets) PendingCurrencyChangeds.Enqueue((p.CurrencyIndex, p.Amount));
        CurrencyChangedEvent?.Invoke(p.CurrencyIndex, p.Amount);
    }

    public void Process(S.WeightUpdate p)
    {
        if (BufferPendingPackets) PendingWeightUpdates.Enqueue((p.BagWeight, p.WearWeight, p.HandWeight));
        WeightUpdateEvent?.Invoke(p.BagWeight, p.WearWeight, p.HandWeight);
    }

    public void Process(S.StorageSize p)
    {
        if (BufferPendingPackets) PendingStorageSizes.Enqueue(p.Size);
        StorageSizeEvent?.Invoke(p.Size);
    }

    // UI 层调用: 发包
    public void SendLogin(string email, string password)
    {
        Enqueue(new C.Login { EMailAddress = email, Password = password, CheckSum = _checkSum });
    }
    public void SendChangePassword(string email, string current, string next)
        => Enqueue(new C.ChangePassword { EMailAddress = email, CurrentPassword = current, NewPassword = next, CheckSum = _checkSum });
    public void SendRequestPasswordReset(string email)
        => Enqueue(new C.RequestPasswordReset { EMailAddress = email, CheckSum = _checkSum });
    public void SendResetPassword(string key, string next)
        => Enqueue(new C.ResetPassword { ResetKey = key, NewPassword = next, CheckSum = _checkSum });
    public void SendActivation(string key)
        => Enqueue(new C.Activation { ActivationKey = key, CheckSum = _checkSum });
    public void SendRequestActivationKey(string email)
        => Enqueue(new C.RequestActivationKey { EMailAddress = email, CheckSum = _checkSum });
    public void SendNewAccount(string email, string password, string realName = "Player")
        => SendNewAccount(email, password, realName, new DateTime(1990, 1, 1), string.Empty);

    public void SendNewAccount(string email, string password, string realName, DateTime birthDate, string referral)
    {
        Enqueue(new C.NewAccount
        {
            EMailAddress = email,
            Password = password,
            BirthDate = birthDate,
            RealName = realName,
            Referral = referral ?? string.Empty,
            CheckSum = _checkSum,
        });
    }
    public void SendNewCharacter(string name, MirClass cls, MirGender gender)
        => SendNewCharacter(name, cls, gender, 1, System.Drawing.Color.Black, System.Drawing.Color.White);

    public void SendNewCharacter(string name, MirClass cls, MirGender gender, int hairType, System.Drawing.Color hairColour, System.Drawing.Color armourColour)
    {
        Enqueue(new C.NewCharacter
        {
            CharacterName = name,
            Class = cls,
            Gender = gender,
            HairType = hairType,
            HairColour = hairColour,
            ArmourColour = armourColour,
            CheckSum = _checkSum,
        });
    }
    public void SendStartGame(int characterIndex)
    {
        GD.Print($"[Net] SendStartGame charIndex={characterIndex}, Connected={Connected}, SendList={(SendList?.Count ?? -1)}");
        Enqueue(new C.StartGame { CharacterIndex = characterIndex });
    }
    public void SendDeleteCharacter(int characterIndex)
        => Enqueue(new C.DeleteCharacter { CharacterIndex = characterIndex, CheckSum = _checkSum });

    // ---- M9 物品发包 ----

    public void SendItemMove(GridType fromGrid, GridType toGrid, int fromSlot, int toSlot, bool mergeItem)
    {
        Enqueue(new C.ItemMove
        {
            FromGrid = fromGrid,
            ToGrid = toGrid,
            FromSlot = fromSlot,
            ToSlot = toSlot,
            MergeItem = mergeItem,
        });
    }

    public void SendItemUse(GridType grid, int slot)
    {
        Enqueue(new C.ItemUse { Link = new CellLinkInfo { GridType = grid, Slot = slot, Count = 1 } });
    }

    public void SendItemSplit(GridType grid, int slot, long count)
    {
        Enqueue(new C.ItemSplit { Grid = grid, Slot = slot, Count = count });
    }

    public void SendItemLock(GridType grid, int slot, bool locked)
    {
        Enqueue(new C.ItemLock { GridType = grid, SlotIndex = slot, Locked = locked });
    }

    public void SendItemSort(GridType grid)
    {
        Enqueue(new C.ItemSort { Grid = grid });
    }

    public void SendItemDelete(GridType grid, int slot)
    {
        Enqueue(new C.ItemDelete { Grid = grid, Slot = slot });
    }

    public void SendMailGetItem(int index, int slot)
    {
        Enqueue(new C.MailGetItem { Index = index, Slot = slot });
    }

    public void SendMailDelete(int index)
    {
        Enqueue(new C.MailDelete { Index = index });
    }

    public void SendTradeItem(CellLinkInfo cell)
        => Enqueue(new C.TradeAddItem { Cell = cell });

    public void SendItemDrop(CellLinkInfo link)
    {
        Enqueue(new C.ItemDrop { Link = link });
    }

    public void SendPickUp()
    {
        Enqueue(new C.PickUp());
    }

    public void SendBeltLinkChanged(int slot, int linkInfoIndex, int linkItemIndex)
    {
        Enqueue(new C.BeltLinkChanged { Slot = slot, LinkIndex = linkInfoIndex, LinkItemIndex = linkItemIndex });
    }

    // ---- 原版寄售行 ----
    public void SendMarketSearch(string name, MarketPlaceSort sort, bool itemTypeFilter = false, ItemType itemType = ItemType.Nothing)
    {
        Enqueue(new C.MarketPlaceSearch { Name = name ?? string.Empty, Sort = sort, ItemTypeFilter = itemTypeFilter, ItemType = itemType });
    }

    public void SendMarketSearchIndex(int index) => Enqueue(new C.MarketPlaceSearchIndex { Index = index });
    public void SendMarketHistory(int index, int partIndex, int display) => Enqueue(new C.MarketPlaceHistory { Index = index, PartIndex = partIndex, Display = display });
    public void SendMarketBuy(long index, long count, bool guildFunds = false) => Enqueue(new C.MarketPlaceBuy { Index = index, Count = count, GuildFunds = guildFunds });
    public void SendMarketCancel(int index, long count) => Enqueue(new C.MarketPlaceCancelConsign { Index = index, Count = count });
    public void SendMarketConsign(GridType grid, int slot, long count, int price, bool guildFunds = false)
    {
        Enqueue(new C.MarketPlaceConsign
        {
            Link = new CellLinkInfo { GridType = grid, Slot = slot, Count = count },
            Price = price,
            Message = string.Empty,
            GuildFunds = guildFunds,
        });
    }

    public void SendFishingCast(FishingState state, MirDirection direction, System.Drawing.Point location, bool caught = false)
    {
        Enqueue(new C.FishingCast { State = state, Direction = direction, FloatLocation = location, CaughtFish = caught });
    }

    public void SendAutoPathStart(int npcIndex) => Enqueue(new C.AutoPathStart { NPCIndex = npcIndex });
    public void SendAutoPathWaypoint(int mapIndex, System.Drawing.Point location) => Enqueue(new C.AutoPathWaypoint { MapIndex = mapIndex, Location = location });
    public void SendAutoPathMoveStarted() => Enqueue(new C.AutoPathMoveStarted());
    public void SendAutoPathCancel() => Enqueue(new C.AutoPathCancel());
    public void SendTaming(uint objectID, TamingState state, MirDirection direction) => Enqueue(new C.Taming { ObjectID = objectID, State = state, Direction = direction });
    public void SendTamingSuccess(uint objectID) => Enqueue(new C.TamingSuccess { ObjectID = objectID });
    public void SendJoinInstance(int index) => Enqueue(new C.JoinInstance { Index = index });
    public void SendGenderChange(MirGender gender, int hairType, System.Drawing.Color hairColour)
        => Enqueue(new C.GenderChange { Gender = gender, HairType = hairType, HairColour = hairColour });
    public void SendHairChange(int hairType, System.Drawing.Color hairColour)
        => Enqueue(new C.HairChange { HairType = hairType, HairColour = hairColour });
    public void SendArmourDye(System.Drawing.Color colour)
        => Enqueue(new C.ArmourDye { ArmourColour = colour });
    public void SendNameChange(string name) => Enqueue(new C.NameChange { Name = name ?? string.Empty });
    public void SendMilestoneClaim(int index) => Enqueue(new C.MilestoneClaim { Index = index });
    public void SendMilestoneNotify(bool receive) => Enqueue(new C.MilestoneNotify { Receive = receive });
    public void SendMilestoneActive(int index, bool active) => Enqueue(new C.MilestoneActive { Index = index, Active = active });
    public void SendLogout() => Enqueue(new C.Logout());
    public void SendSelectLanguage(string language) => Enqueue(new C.SelectLanguage { Language = language ?? string.Empty });
    public void SendGameStoreBuy(int index, long count, bool useHuntGold)
        => Enqueue(new C.MarketPlaceStoreBuy { Index = index, Count = count, UseHuntGold = useHuntGold });
    public void SendGameStoreFavourite(int index)
        => Enqueue(new C.GameStoreFavouriteToggle { Index = index });
    public void SendGameStoreGift(int index, long count, bool useHuntGold, string recipient)
        => Enqueue(new C.GameStoreGift { Index = index, Count = count, UseHuntGold = useHuntGold, Recipient = recipient ?? string.Empty });
    public void SendGuildInviteMember(string name) => Enqueue(new C.GuildInviteMember { Name = name ?? string.Empty });
    public void SendGuildEditNotice(string notice) => Enqueue(new C.GuildEditNotice { Notice = notice ?? string.Empty });
    public void SendGuildIncreaseMember() => Enqueue(new C.GuildIncreaseMember());
    public void SendGuildIncreaseStorage() => Enqueue(new C.GuildIncreaseStorage());
    public void SendGuildCreate(string name, bool useGold, int members, int storage)
        => Enqueue(new C.GuildCreate { Name = name ?? string.Empty, UseGold = useGold, Members = Math.Max(0, members), Storage = Math.Max(0, storage) });
    public void SendJoinStarterGuild() => Enqueue(new C.JoinStarterGuild());
    public void SendGuildColour(System.Drawing.Color colour) => Enqueue(new C.GuildColour { Colour = colour });
    public void SendGuildFlag(int flag) => Enqueue(new C.GuildFlag { Flag = flag });
    public void SendTradeRequestResponse(bool accept) => Enqueue(new C.TradeRequestResponse { Accept = accept });
    public void SendNPCRollResult() => Enqueue(new C.NPCRollResult());
    public void SendRankSearch(string name) => Enqueue(new C.RankSearch { Name = name ?? string.Empty });
    public void SendRankings(int startIndex = 0, bool onlineOnly = false)
        => Enqueue(new C.RankRequest { Class = RequiredClass.None, OnlineOnly = onlineOnly, StartIndex = startIndex });
    public void SendOnlineState(OnlineState state) => Enqueue(new C.ChangeOnlineState { State = state });
    public void SendHelmetToggle(bool hide) => Enqueue(new C.HelmetToggle { HideHelmet = hide });
    public void SendBlockAdd(string name) => Enqueue(new C.BlockAdd { Name = name ?? string.Empty });
    public void SendBlockRemove(int index) => Enqueue(new C.BlockRemove { Index = index });
    public void SendIncreaseDiscipline() => Enqueue(new C.IncreaseDiscipline());
    public void SendGuildTax(long tax) => Enqueue(new C.GuildTax { Tax = tax });
    public void SendMarriageResponse(bool accept) => Enqueue(new C.MarriageResponse { Accept = accept });
    public void SendMarriageMakeRing(int slot) => Enqueue(new C.MarriageMakeRing { Slot = slot });
    public void SendTeleportRing(System.Drawing.Point location, int mapIndex) => Enqueue(new C.TeleportRing { Location = location, Index = mapIndex });
    public void SendGroupLfg(bool enabled, string name, string type, int maxCount) => Enqueue(new C.GroupLFGUpdate { Enabled = enabled, Name = name ?? string.Empty, Type = type ?? string.Empty, MaxCount = Math.Max(1, maxCount) });
    public void SendGroupNotify(bool receive) => Enqueue(new C.GroupNotify { Receive = receive });
    public void SendMagicToggle(MagicType magic, bool canUse) => Enqueue(new C.MagicToggle { Magic = magic, CanUse = canUse });
    public void SendHermit(Stat stat) => Enqueue(new C.Hermit { Stat = stat });
    public void SendObservable(bool allow) => Enqueue(new C.ObservableSwitch { Allow = allow });
    public void SendTownRevive() => Enqueue(new C.TownRevive());
    public void SendHarvest(MirDirection direction) => Enqueue(new C.Harvest { Direction = direction });
    public void SendRangeAttack(MirDirection direction, uint target) => Enqueue(new C.RangeAttack { Direction = direction, Target = target });
    public void SendCurrencyDrop(int currencyIndex, long amount) => Enqueue(new C.CurrencyDrop { CurrencyIndex = currencyIndex, Amount = amount });
    public void SendGuildWar(string guildName) => Enqueue(new C.GuildWar { GuildName = guildName ?? string.Empty });
    public void SendGuildToggleCastleGates() => Enqueue(new C.GuildToggleCastleGates());
    public void SendGuildRepairCastleGates() => Enqueue(new C.GuildRepairCastleGates());
    public void SendGuildRepairCastleGuards() => Enqueue(new C.GuildRepairCastleGuards());
    public void SendCompanionStore(int index) => Enqueue(new C.CompanionStore { Index = index });
    public void SendCompanionRetrieve(int index) => Enqueue(new C.CompanionRetrieve { Index = index });
    public void SendCompanionRelease(int index) => Enqueue(new C.CompanionRelease { Index = index });
    public void SendCompanionUnlock(int index) => Enqueue(new C.CompanionUnlock { Index = index });
    public void SendCompanionAdopt(int index, string name) => Enqueue(new C.CompanionAdopt { Index = index, Name = name ?? string.Empty });
    public void SendGuildRequestConquest(int index) => Enqueue(new C.GuildRequestConquest { Index = index });
    public void SendGuildResponse(string guildName, bool accept) => Enqueue(new C.GuildResponse { Accept = accept });
    public void SendFriendAdd(string name) => Enqueue(new C.FriendAdd { Name = name ?? string.Empty });
    public void SendFriendRemove(int index) => Enqueue(new C.FriendRemove { Index = index });
    public void SendGroupResponse(string name, bool accept) => Enqueue(new C.GroupResponse { Name = name ?? string.Empty, Accept = accept });

    // 玩家学新技能 (S.NewMagic)
    public event Action<ClientUserMagic> NewMagicEvent;
    public event Action<S.MagicLeveled> MagicLeveledEvent;
    public event Action<S.MagicCooldown> MagicCooldownEvent;
    public event Action<S.MagicToggle> MagicToggleEvent;
    public readonly Queue<ClientUserMagic> PendingNewMagics = new();
    public void Process(S.NewMagic p)
    {
        if (BufferPendingPackets) PendingNewMagics.Enqueue(p.Magic);
        NewMagicEvent?.Invoke(p.Magic);
    }
    public void Process(S.MagicLeveled p) => MagicLeveledEvent?.Invoke(p);
    public void Process(S.MagicCooldown p) => MagicCooldownEvent?.Invoke(p);
    public void Process(S.MagicToggle p) => MagicToggleEvent?.Invoke(p);

    // 绑定/解绑技能快捷键 (原版 Image_KeyDown 后发此包持久化)
    public void SendMagicKey(MagicType magic, SpellKey set1, SpellKey set2, SpellKey set3, SpellKey set4)
    {
        Enqueue(new C.MagicKey { Magic = magic, Set1Key = set1, Set2Key = set2, Set3Key = set3, Set4Key = set4 });
    }
}
