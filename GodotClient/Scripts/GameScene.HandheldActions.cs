using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using Library;
using ZirconClient.Controls;
using ZirconClient.HandheldActions;

namespace ZirconClient.Scripts;

internal sealed record HandheldBindingChoice(ActionBinding Binding, string Name, string Detail, Texture2D Icon);

public partial class GameScene
{
    private HandheldActionConfiguration _handheldActionConfig = HandheldActionConfiguration.CreateDefault();
    private HandheldActionConfiguration _effectiveHandheldActionConfig = HandheldActionConfiguration.CreateDefault();
    private readonly HandheldActionEngine _handheldActionEngine = new(HandheldActionConfiguration.CreateDefault());
    private string _handheldActionPath;
    private bool _handheldActionFocused = true;
    private bool _handheldActionBlocked;
    private HandheldActionBindings _handheldActionBindings;
    private bool HandheldActionBindingsOpen => _handheldActionBindings?.Visible == true;

    internal void ShowHandheldToast(string message) => _handheldHud?.ShowToast(message);

    internal void OpenHandheldActionBindings()
    {
        if (!AutoLoginArgs.Handheld || !_startGameShown || HandheldHasModalWindow()) return;
        if (_handheldActionBindings == null)
        {
            var layer = new CanvasLayer { Name = "HandheldBindingsLayer", Layer = 85 };
            AddChild(layer);
            _handheldActionBindings = new HandheldActionBindings(this);
            layer.AddChild(_handheldActionBindings);
        }
        _handheldBindingsStickNeutral = true;
        _handheldBindingsStickDirection = Key.None;
        StopHandheldMovement();
        ClearHandheldFocus();
        _handheldActionBindings.Open();
        SyncJournalVisibility();
        SyncHandheldActionOwnership(Godot.Time.GetTicksMsec());
    }

    private bool _handheldBindingsStickNeutral = true;
    private Key _handheldBindingsStickDirection;
    private double _nextHandheldBindingsStickMs;

    private void ProcessHandheldBindings(double now)
    {
        var axis = new Vector2(Input.GetJoyAxis(0, JoyAxis.LeftX), Input.GetJoyAxis(0, JoyAxis.LeftY));
        if (_handheldBindingsStickNeutral)
        {
            if (axis.LengthSquared() < .1f) _handheldBindingsStickNeutral = false;
            return;
        }
        Key direction = axis.LengthSquared() < .25f ? Key.None
            : Math.Abs(axis.X) > Math.Abs(axis.Y) ? (axis.X > 0 ? Key.Right : Key.Left)
            : axis.Y > 0 ? Key.Down : Key.Up;
        if (direction != Key.None && (direction != _handheldBindingsStickDirection || now >= _nextHandheldBindingsStickMs))
        {
            _handheldActionBindings.HandleKey(direction);
            _nextHandheldBindingsStickMs = now + (direction == _handheldBindingsStickDirection ? 160 : 360);
        }
        _handheldBindingsStickDirection = direction;
    }

    private void RouteHandheldBindingsKey(InputEventKey key)
    {
        if (!key.Pressed || key.Echo) return;
        Key command = key.Keycode switch
        {
            Key.KpEnter => Key.Enter,
            Key.Q => Key.Q, Key.E => Key.E,
            Key.F13 => Key.F13,
            Key.Space => Key.Space,
            Key.Enter => Key.Enter,
            Key.Escape => Key.Escape,
            Key.Left or Key.Right or Key.Up or Key.Down => key.Keycode,
            _ => Key.None
        };
        if (command != Key.None) _handheldActionBindings.HandleKey(command);
    }

    // Presentation/editor copy. Mutating it cannot mutate the live engine.
    internal HandheldActionConfiguration HandheldActionConfig
    {
        get { EnsureHandheldActionConfiguration(); return _handheldActionConfig; }
    }

    internal HandheldActionSnapshot HandheldActionState => _handheldActionEngine.Snapshot(Godot.Time.GetTicksMsec());

    internal HandheldBindingChoice HandheldDirectAction(int index)
    {
        EnsureHandheldActionConfiguration();
        var button = HandheldActionPage.DirectButtons()[index];
        return DescribeHandheldBinding(_handheldActionConfig.Pages[_handheldActionEngine.Snapshot(Godot.Time.GetTicksMsec()).CurrentPage].Bindings[button]);
    }

    internal HandheldBindingChoice HandheldFixedAction(string slot)
    {
        EnsureHandheldActionConfiguration();
        return DescribeHandheldBinding(_handheldActionConfig.FixedSlots[slot]);
    }

    private void EnsureHandheldActionConfiguration()
    {
        if (!AutoLoginArgs.Handheld || !_startGameShown || _observer || StartInfo == null) return;
        var host = AutoLoginArgs.ServerAddress ?? (ClientSettings.UseNetworkConfig ? ClientSettings.IPAddress : "127.0.0.1");
        int port = AutoLoginArgs.ServerPort ?? (ClientSettings.UseNetworkConfig ? ClientSettings.Port : 7000);
        string path = ProjectSettings.GlobalizePath($"user://handheld-actions/{host.Md5Text()}-{port}-{StartInfo.Index}.json");
        if (path == _handheldActionPath) return;
        _handheldActionPath = path;
        var config = CreateHandheldActionDefaults();
        try { config = HandheldActionConfigurationStore.Load(path); }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (Exception error) when (IsHandheldConfigError(error))
        {
            string message = $"无法读取掌机按键配置，已使用默认绑定。原文件已保留。{error.Message}";
            GD.PushWarning(message);
            _handheldHud?.ShowToast(message);
        }
        ActivateHandheldActionConfiguration(config);
    }

    private static bool IsHandheldConfigError(Exception error) =>
        error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException;

    private HandheldActionConfiguration CreateHandheldActionDefaults()
    {
        var config = HandheldActionConfiguration.CreateDefault();
        var skills = HandheldBindableSkills().Where(skill => StartInfo != null && skill.Info.MatchesClass(StartInfo.Class));
        var slots = config.Pages.SelectMany(page => HandheldActionPage.DirectButtons().Select(button => (page, button)));
        foreach (var (slot, skill) in slots.Zip(skills))
            slot.page.Bindings[slot.button] = HandheldSkillBinding(skill);
        for (int i = 0; i < 2; i++)
        {
            var item = Inventory.Concat(CompanionInventory)
                .FirstOrDefault(item => MatchesHandheldBeltLink(GetHandheldBeltLink(i), item)
                    && IsHandheldUsableItemType(item.Info.ItemType) && CanUseItem(item));
            if (item != null) config.FixedSlots[i == 0 ? "L2tap" : "R2tap"] = ActionBinding.Item(HandheldStableId(item.Info.Index));
        }
        config.FixedSlots["L1+R1"] = ActionBinding.Menu(MenuAction.OpenMap);
        return config;
    }

    internal HandheldActionConfiguration CloneHandheldActionConfig() => CopyHandheldActionConfiguration(HandheldActionConfig);

    private static HandheldActionConfiguration CopyHandheldActionConfiguration(HandheldActionConfiguration source)
    {
        static ActionBinding Copy(ActionBinding binding) => new()
        {
            Kind = binding.Kind, StableId = binding.StableId, SkillMode = binding.SkillMode, MenuAction = binding.MenuAction
        };
        return new()
        {
            SchemaVersion = source.SchemaVersion,
            LongPressThresholdMs = source.LongPressThresholdMs,
            ChordWindowMs = source.ChordWindowMs,
            Pages = source.Pages.Select(page => new HandheldActionPage
                { Bindings = page.Bindings.ToDictionary(entry => entry.Key, entry => Copy(entry.Value)) }).ToList(),
            FixedSlots = source.FixedSlots.ToDictionary(entry => entry.Key, entry => Copy(entry.Value))
        };
    }

    internal string SaveHandheldActionBindings() => SaveHandheldActionBindings(HandheldActionConfig);

    internal string SaveHandheldActionBindings(HandheldActionConfiguration draft)
    {
        EnsureHandheldActionConfiguration();
        string error = SaveHandheldActionDraft(draft);
        // Discard edits to the legacy mutable UI reference after a failed save as well.
        if (error != null) _handheldActionConfig = CopyHandheldActionConfiguration(_effectiveHandheldActionConfig);
        return error;
    }

    private string SaveHandheldActionDraft(HandheldActionConfiguration draft)
    {
        if (_handheldActionPath == null || _observer) return "角色尚未就绪，无法保存按键配置。";
        if (draft == null) return "按键配置为空。";
        var errors = draft.Validate();
        if (errors.Count != 0) return string.Join(" ", errors);
        // Check even after successful initial load in case the file was changed externally.
        try { _ = HandheldActionConfigurationStore.Load(_handheldActionPath); }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (Exception error) when (IsHandheldConfigError(error))
        { return $"原按键配置无法读取，已保留原文件。{error.Message}"; }
        var config = CopyHandheldActionConfiguration(draft);
        var saved = HandheldActionConfigurationStore.Save(_handheldActionPath, config);
        if (!saved.Success) return $"无法保存按键配置。{saved.Error}";
        ActivateHandheldActionConfiguration(config);
        return null;
    }

    private void ActivateHandheldActionConfiguration(HandheldActionConfiguration config)
    {
        _handheldActionEngine.Reload(config);
        _effectiveHandheldActionConfig = config;
        _handheldActionConfig = CopyHandheldActionConfiguration(config);
    }

    // Both IDs are the invariant decimal Info.Index in System.db, not a bag slot or learned-instance index.
    private static string HandheldStableId(int index) => index.ToString(CultureInfo.InvariantCulture);
    private static bool TryHandheldStableId(string id, out int index) =>
        int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out index);

    private static bool IsHandheldBindableSkill(ClientUserMagic magic) => magic?.Info != null
        && !IsJournalPassive(magic.Info) && magic.Info.School is not (MagicSchool.None or MagicSchool.Discipline)
        && magic.Info.Magic is not (MagicType.Swordsmanship or MagicType.SpiritSword or MagicType.VineTreeDance or MagicType.WillowDance);

    private IEnumerable<ClientUserMagic> HandheldBindableSkills() => UserMagics.Values.Where(IsHandheldBindableSkill)
        .OrderBy(magic => magic.Info.NeedLevel1).ThenBy(magic => magic.Info.Index);

    private static ActionBinding HandheldSkillBinding(ClientUserMagic magic) => ActionBinding.Skill(HandheldStableId(magic.Info.Index),
        magic.Info.School == MagicSchool.Toggle || magic.Info.Property == MagicProperty.Toggle ||
        magic.Info.Magic is MagicType.Thrusting or MagicType.HalfMoon or MagicType.DestructiveSurge or MagicType.FlameSplash
            ? SkillActionMode.Toggle : SkillActionMode.Active);

    private ClientUserMagic FindHandheldSkill(string id) => TryHandheldStableId(id, out int index)
        ? UserMagics.Values.FirstOrDefault(magic => IsHandheldBindableSkill(magic) && magic.Info.Index == index) : null;

    internal ClientUserMagic FindHandheldSkillForHud(int index) => FindHandheldSkill(HandheldStableId(index));

    // The immediate-use branches of DXItemCell.UseItem. Equipment, system changes and box dialogs are excluded.
    private static bool IsHandheldUsableItemType(ItemType type) =>
        type is ItemType.Consumable or ItemType.Scroll or ItemType.CompanionFood or ItemType.ItemPart or ItemType.Book;

    private IEnumerable<ClientUserItem> HandheldBindableItems() => Inventory.Concat(CompanionInventory)
        .Where(item => item?.Info != null && item.Count > 0 && IsHandheldUsableItemType(item.Info.ItemType));

    internal IReadOnlyList<HandheldBindingChoice> HandheldBindingChoices(ActionKind kind) => kind switch
    {
        ActionKind.Empty => [DescribeHandheldBinding(ActionBinding.Empty())],
        ActionKind.Skill => HandheldBindableSkills().Select(magic => DescribeHandheldBinding(HandheldSkillBinding(magic))).ToArray(),
        ActionKind.Item => HandheldBindableItems().Where(CanUseItem).DistinctBy(item => item.Info.Index)
            .OrderBy(item => item.Info.ItemName).ThenBy(item => item.Info.Index)
            .Select(item => DescribeHandheldBinding(ActionBinding.Item(HandheldStableId(item.Info.Index)))).ToArray(),
        ActionKind.Menu => Enum.GetValues<MenuAction>().Select(action => DescribeHandheldBinding(ActionBinding.Menu(action))).ToArray(),
        _ => Array.Empty<HandheldBindingChoice>()
    };

    internal HandheldBindingChoice DescribeHandheldBinding(ActionBinding binding)
    {
        if (binding == null || binding.Kind == ActionKind.Empty) return new(ActionBinding.Empty(), "空", "不执行动作", null);
        if (binding.Kind == ActionKind.Menu) return new(binding, HandheldMenuActionName(binding.MenuAction), "菜单动作", null);
        if (binding.Kind == ActionKind.Skill && FindHandheldSkill(binding.StableId) is { } magic)
        {
            string mode = HandheldSkillBinding(magic).SkillMode == SkillActionMode.Toggle ? "开关" : "主动";
            return new(binding, magic.Info.Local() ?? magic.Info.Name, $"{mode} · Lv.{magic.Level} · 耗蓝 {magic.Cost}",
                MirSkin.GetTexture(LibraryFile.MagicIcon, magic.Info.Icon));
        }
        if (binding.Kind == ActionKind.Item && TryHandheldStableId(binding.StableId, out int index))
        {
            var items = HandheldBindableItems().Where(item => item.Info.Index == index).ToArray();
            var info = items.FirstOrDefault()?.Info ?? Globals.ItemInfoList?.Binding.FirstOrDefault(info => info.Index == index);
            if (info != null && IsHandheldUsableItemType(info.ItemType))
            {
                int frame = items.Length == 0 ? info.Image : DXItemCell.GetItemDrawIndex(items[0]);
                return new(binding, info.Local() ?? info.ItemName, $"持有 {items.Sum(item => item.Count)}",
                    MirSkin.GetTexture(DXItemCell.ItemIconLibraryFile, frame) ?? MirSkin.GetTexture(LibraryFile.Inventory, frame));
            }
        }
        return new(binding, binding.Kind == ActionKind.Skill ? "技能不可用" : "物品不可用", binding.StableId ?? "", null);
    }

    private static string HandheldMenuActionName(MenuAction? action) => action switch
    {
        MenuAction.Interact => "互动", MenuAction.Cancel => "取消", MenuAction.OpenInventory => "打开背包",
        MenuAction.OpenCharacter => "打开人物", MenuAction.OpenSkills => "打开技能", MenuAction.OpenQuests => "打开任务",
        MenuAction.OpenSocial => "打开社交", MenuAction.OpenSystem => "打开系统", MenuAction.Target => "切换目标",
        MenuAction.OpenMap => "打开大地图", MenuAction.FirstPage => "回到第一页", _ => "动作不可用"
    };

    private static HandheldButton? HandheldButtonForKey(Key key) => key switch
    {
        Key.Space => HandheldButton.X, Key.F16 => HandheldButton.Y,
        Key.Up => HandheldButton.Up, Key.Down => HandheldButton.Down,
        Key.Left => HandheldButton.Left, Key.Right => HandheldButton.Right,
        Key.Enter or Key.KpEnter => HandheldButton.A, Key.Escape => HandheldButton.B,
        Key.Q => HandheldButton.L1, Key.E => HandheldButton.R1,
        Key.Key1 => HandheldButton.L2, Key.Key2 => HandheldButton.R2, _ => null
    };

    private bool HandheldActionOwnsWorld => _handheldActionFocused && !HandheldGameplayBlocked && !JournalServiceActive
        && _startGameShown && !_observer && _player?.Dead == false && _net?.Connection?.Connected == true;

    private void SyncHandheldActionOwnership(double now)
    {
        bool blocked = !HandheldActionOwnsWorld;
        if (blocked && !_handheldActionBlocked)
        {
            StopHandheldMovement();
            _pendingMagicPacket = null;
            _handheldSelectDownMs = -1;
        }
        _handheldActionBlocked = blocked;
        _handheldActionEngine.SetMenuOwnsInput(blocked, now);
    }

    private void TrackHandheldActionKey(InputEventKey key, HandheldButton button, double now)
    {
        if (!key.Pressed) _handheldActionEngine.Release(button, now);
        else if (!key.Echo) _handheldActionEngine.Press(button, now);
    }

    private void RunHandheldActionIntents()
    {
        foreach (var intent in _handheldActionEngine.DrainIntents())
        {
            if (!HandheldActionOwnsWorld) break;
            switch (intent.Kind)
            {
                case ActionKind.Skill:
                    if (!TryUseHandheldMagic(FindHandheldSkill(intent.ActionId)))
                        _handheldHud?.ShowToast(_pendingMagicPacket != null ? "已有技能等待当前动作结束" : "当前无法释放该技能");
                    break;
                case ActionKind.Item:
                    if (!TryUseHandheldActionItem(intent.ActionId)) _handheldHud?.ShowToast("当前无法使用该物品");
                    break;
                case ActionKind.Menu:
                    RunHandheldMenuAction(intent.MenuAction);
                    break;
            }
        }
        SyncHandheldActionOwnership(Godot.Time.GetTicksMsec());
    }

    private bool TryUseHandheldActionItem(string id)
    {
        if (!CanUseHandheldQuickAction || !TryHandheldStableId(id, out int index)) return false;
        foreach (var cell in InventoryCells.Concat(CompanionInventoryCells))
        {
            var item = cell?.Item;
            if (item?.Info == null || item.Info.Index != index || item.Count <= 0 || !IsHandheldUsableItemType(item.Info.ItemType)
                || cell.GridType is not (GridType.Inventory or GridType.CompanionInventory) || !JournalReadyCell(cell)
                || !CanUseItem(item)) continue;
            if (cell.UseItem()) return true;
        }
        return false;
    }

    private void RunHandheldMenuAction(MenuAction? action)
    {
        switch (action)
        {
            case MenuAction.Interact: HandheldConfirm(); break;
            case MenuAction.Cancel: CancelHandheldWorldInput(); break;
            case MenuAction.Target: CycleHandheldTarget(1); break;
            case MenuAction.FirstPage: _handheldActionEngine.FirstPage(); break;
            case MenuAction.OpenMap: StopHandheldMovement(); OpenBigMap(); break;
            case MenuAction.OpenInventory: OpenHandheldJournalTab(0); break;
            case MenuAction.OpenCharacter: OpenHandheldJournalTab(1); break;
            case MenuAction.OpenSkills: OpenHandheldJournalTab(2); break;
            case MenuAction.OpenQuests: OpenHandheldJournalTab(3); break;
            case MenuAction.OpenSocial: OpenHandheldJournalTab(4); break;
            case MenuAction.OpenSystem: OpenHandheldJournalTab(5); break;
        }
    }

    private void OpenHandheldJournalTab(int tab)
    {
        OpenJournal();
        if (JournalOpen) _journal.SelectTab(tab);
    }

    private void CancelHandheldWorldInput()
    {
        DXItemCell.SelectedCell = null;
        _selectedCurrency = null;
        _pendingMagicPacket = null;
        StopHandheldMovement();
        ClearHandheldFocus();
    }

    private void OnHandheldActionFocusLost()
    {
        _handheldActionFocused = false;
        double now = Godot.Time.GetTicksMsec();
        _handheldActionEngine.FocusLost(now);
        _handheldBindingsStickNeutral = true;
        _handheldBindingsStickDirection = Key.None;
        SyncHandheldActionOwnership(now);
        ResetJournalNavigation();
    }

    private void OnHandheldActionFocusEntered() => _handheldActionFocused = true;
}
