using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Library;
using ZirconClient.Controls;
using Point = System.Drawing.Point;

namespace ZirconClient.Scripts;

public partial class GameScene
{
    private const double HandheldLongPressMs = 600;
    private bool _handheldMoveStarted;
    private double _nextMoveSkipTraceMs;
    private bool _handheldStickMoving;
    private bool _handheldRequireNeutral;
    private bool _handheldAutoRun;
    private MirDirection _handheldMoveDirection;
    private double _handheldSelectDownMs = -1;
    private bool _handheldSelectLong;
    private bool _handheldTargetLocked;
    private double _nextFocusScanMs;
    private double _nextHandheldTraceMs;
    private readonly bool _handheldTrace = System.Environment.GetEnvironmentVariable("ZIRCON_HANDHELD_TRACE") == "1";
    private bool _handheldPointerMode;
    public bool HandheldPointerMode => AutoLoginArgs.Handheld && _handheldPointerMode;
    public bool HandheldCombatBlocked => AutoLoginArgs.Handheld &&
        (HandheldGameplayBlocked || _handheldStickMoving || _handheldAutoRun);
    private uint _handheldNpcApproachId;
    private Point _handheldAimCell;
    private bool _handheldHasAim;
    public ObjectRenderer _handheldFocusObject;
    private HandheldFocusReticle _handheldFocusReticle;

    // Legacy windows do not all register with WindowManager (e.g. big map).
    // Inspect visible window roots too, but exclude permanent HUD widgets.
    private bool HandheldHasModalWindow()
    {
        if (_uiLayer == null) return false;
        for (int i = 0, count = _uiLayer.GetChildCount(); i < count; i++)
            if (_uiLayer.GetChild(i) is DXWindow w && w.Visible && !w.IsQueuedForDeletion()
                && w != _miniMap && w != _beltDialog && w != _buffDialog && w != _questTracker
                && w != _monsterDialog && w != _chatTextBox) return true;
        return false;
    }

    private bool HandheldGameplayBlocked => AutoLoginArgs.Handheld &&
        (GmInspectionOpen || HandheldWorldMapOpen || JournalOpen || HandheldActionBindingsOpen || HandheldHasModalWindow() || HandheldTextEditing);

    private bool HandheldTextEditing => GetViewport()?.GuiGetFocusOwner() is Control control
        && control.IsVisibleInTree() && control is LineEdit or TextEdit;

    private void InitHandheldSystem()
    {
        if (!AutoLoginArgs.Handheld) return;
        _handheldFocusReticle = new HandheldFocusReticle(this);
        AddChild(_handheldFocusReticle);
        UpdateHandheldPointerMode();
        var window = GetWindow();
        window.FocusExited += OnHandheldActionFocusLost;
        window.FocusEntered += OnHandheldActionFocusEntered;
        GD.Print("[Handheld] left stick moves; right stick selects targets; Start switches HUD");
    }

    private void UpdateHandheldPointerMode()
    {
        if (!AutoLoginArgs.Handheld) return;
        // The fbdev backend reads native getenv and emits semantic Enter in this mode.
        _handheldPointerMode = false;
        if (OS.GetEnvironment("ZIRCON_HANDHELD_POINTER") != "0")
            OS.SetEnvironment("ZIRCON_HANDHELD_POINTER", "0");
        if (Input.MouseMode != Input.MouseModeEnum.Hidden)
            Input.MouseMode = Input.MouseModeEnum.Hidden;
    }

    private void StopHandheldMovement()
    {
        _handheldAutoRun = false;
        _handheldStickMoving = false;
        _handheldMoveStarted = false;
        _handheldRequireNeutral = true;
        _autoRun = false;
        if (_mouseWalker != null)
        {
            _mouseWalker.AutoRun = false;
            _mouseWalker.SuspendUntilInputRelease();
        }
        _groundItemPickupTargetId = 0;
        if (_handheldNpcApproachId != 0 || _autoPathRoutes.Count > 0 || _pendingAutoPathMove != null)
            CancelAutoPath();
        _handheldNpcApproachId = 0;
        if (_combatController != null) _combatController.TargetObject = null;
        _mining = false;
    }

    private void ProcessHandheldInput(double delta)
    {
        if (!AutoLoginArgs.Handheld) return;
        UpdateJournalServiceContext();
        UpdateHandheldPointerMode();
        double now = Godot.Time.GetTicksMsec();
        EnsureHandheldActionConfiguration();
        SyncHandheldActionOwnership(now);
        if (!_handheldActionFocused) return;
        if (GmInspectionOpen) return;
        if (HandheldNpcDialogueOpen) { ProcessHandheldNpcStick(now); return; }
        if (HandheldActionBindingsOpen) { ProcessHandheldBindings(now); return; }
        if (_handheldTrace && now >= _nextHandheldTraceMs)
        {
            _nextHandheldTraceMs = now + 500;
            var left = new Vector2(Input.GetJoyAxis(0, JoyAxis.LeftX), Input.GetJoyAxis(0, JoyAxis.LeftY));
            var right = new Vector2(Input.GetJoyAxis(0, JoyAxis.RightX), Input.GetJoyAxis(0, JoyAxis.RightY));
            var windows = string.Join(",", _uiLayer.GetChildren().OfType<DXWindow>().Where(w => w.Visible).Select(w => w.GetType().Name));
            string state = $"[HandheldTrace] fps={Engine.GetFramesPerSecond()} mouse={GetViewport().GetMousePosition()} weight={BagWeight}/{PlayerStats[Stat.BagWeight]} weapon={_player?.LibraryWeaponShape} mp={_currentMP} windows={windows} pos={_playerLocation.X},{_playerLocation.Y} L={left} R={right} blocked={HandheldGameplayBlocked} pointer={_handheldPointerMode} neutral={_handheldRequireNeutral} move={_moveFrameCount} target={_handheldFocusObject?.ObjectID ?? 0} locked={_handheldTargetLocked}";
            GD.Print(state);
            System.IO.File.WriteAllText("/tmp/zircon-handheld-state", state);
        }
        if (JournalOpen) { ProcessJournal(now); return; }
        ValidateHandheldFocus();
        if (_handheldSelectDownMs >= 0 && !_handheldSelectLong
            && now - _handheldSelectDownMs >= HandheldLongPressMs)
        {
            _handheldSelectLong = true;
            if (HandheldActionOwnsWorld)
            {
                ClientSettings.ShowGroundItemNames = !ClientSettings.ShowGroundItemNames;
                RefreshGroundItemLabels(forceRedraw: true);
                _handheldHud?.ShowToast(ClientSettings.ShowGroundItemNames ? "掉落物名称：显示" : "掉落物名称：隐藏");
            }
        }
        if (!HandheldActionOwnsWorld || _mapView?.Map == null) return;
        ProcessHandheldFocus(now);
        _handheldActionEngine.Tick(now);
        RunHandheldActionIntents();
        if (!HandheldActionOwnsWorld) return;
        ProcessHandheldMovement(now);
        if (_handheldNpcApproachId != 0 && _objects.TryGetValue(_handheldNpcApproachId, out var npc)
            && Functions.Distance(_playerLocation, new Point(npc.CellX, npc.CellY)) <= 2)
        {
            uint id = _handheldNpcApproachId;
            _handheldNpcApproachId = 0;
            CancelAutoPath();
            TrySendNpcCall(id);
        }
    }

    private void ProcessHandheldMovement(double now)
    {
        Vector2 stick = new(Input.GetJoyAxis(0, JoyAxis.LeftX), Input.GetJoyAxis(0, JoyAxis.LeftY));
        bool pushed = stick.Length() >= 0.25f;
        if (_handheldRequireNeutral)
        {
            if (!pushed) _handheldRequireNeutral = false;
            return;
        }
        if (!pushed && !_handheldAutoRun)
        {
            _handheldStickMoving = false;
            _handheldMoveStarted = false;
            return;
        }
        if (pushed)
        {
            if (!_handheldStickMoving)
            {
                CancelAutoPath();
                _groundItemPickupTargetId = 0;
                _handheldNpcApproachId = 0;
                if (_combatController != null) _combatController.TargetObject = null;
                _handheldMoveStarted = false;
            }
            double angle = (Math.Atan2(stick.X, -stick.Y) * 180 / Math.PI + 382.5) % 360;
            _handheldMoveDirection = (MirDirection)(int)(angle / 45);
        }
        _handheldStickMoving = pushed;
        // Obey the authoritative move/server clocks, not a second fixed 540ms
        // timer. Every intermediate run cell is checked; blocked runs fall back.
        if (_handheldTrace && now >= _nextMoveSkipTraceMs)
        {
            _nextMoveSkipTraceMs = now + 1000;
            GD.Print($"[MoveTiming] WAIT now={now:0} visual={_moveVisualLockUntilMs:0} server={_moveServerLockUntilMs:0} frame={_moveFrameCount} canMove={CanPlayerMove()} pushed={pushed} started={_handheldMoveStarted}");
        }
        if (!CanPlayerMove() || now < _moveVisualLockUntilMs
            || now < _moveServerLockUntilMs) return;
        // Walk the first segment, then use the same run gate as PC right-click.
        bool running = _handheldAutoRun || _handheldMoveStarted;
        int steps = running ? GetRunSteps() : 1;
        if (!CanMoveGroundItemPath(_handheldMoveDirection, steps)) steps = 1;
        if (CanMoveGroundItemPath(_handheldMoveDirection, steps))
        {
            SendMouseMove(_handheldMoveDirection, steps, running && steps >= 2);
            _handheldMoveStarted = true;
        }
        else
        {
            _handheldAutoRun = false;
            if (CanPlayerTurn()) SendTurn(_handheldMoveDirection);
        }
    }

    private bool IsHandheldFocusCandidate(ObjectRenderer ob)
        => ob != null && IsInstanceValid(ob) && !ob.IsQueuedForDeletion() && ob.Visible
           && ob.ObjectID != _playerObjectID
           && ob.Type is ObjectRenderer.Kind.NPC or ObjectRenderer.Kind.Monster or ObjectRenderer.Kind.Item or ObjectRenderer.Kind.Player
           && !(ob.Type == ObjectRenderer.Kind.Monster && (ob.Dead || (ob.MonsterInfo?.AI ?? 0) < 0))
           && Functions.Distance(_playerLocation, new Point(ob.CellX, ob.CellY)) <= 24;

    private void ValidateHandheldFocus()
    {
        var focus = _handheldFocusObject;
        if (focus != null && (!IsHandheldFocusCandidate(focus) || !_objects.ContainsKey(focus.ObjectID)))
            ClearHandheldFocus();
    }

    private void ClearHandheldFocus()
    {
        var old = _handheldFocusObject;
        _handheldFocusObject = null;
        _handheldTargetLocked = false;
        if (_combatController != null)
        {
            if (_combatController.MouseObject == old) _combatController.MouseObject = null;
            _combatController.TargetObject = null;
        }
        _handheldHasAim = false;
        ClearMagicLock();
    }

    private void ProcessHandheldFocus(double now)
    {
        Vector2 axis = new(Input.GetJoyAxis(0, JoyAxis.RightX), Input.GetJoyAxis(0, JoyAxis.RightY));
        if (axis.Length() < 0.30f || _handheldTargetLocked) return;
        // Separate ground aim from target focus; never cast at a stale virtual
        // mouse position after using a quick panel.
        _handheldAimCell = new Point(_playerLocation.X + (int)Math.Round(axis.X * 6),
            _playerLocation.Y + (int)Math.Round(axis.Y * 6));
        if (_mapView?.Map != null)
            _handheldAimCell = new Point(Math.Clamp(_handheldAimCell.X, 0, _mapView.Map.Width - 1),
                Math.Clamp(_handheldAimCell.Y, 0, _mapView.Map.Height - 1));
        _handheldHasAim = true;
        if (now < _nextFocusScanMs) return;
        _nextFocusScanMs = now + 250;
        Vector2 direction = axis.Normalized();
        var best = _objects.Values.Where(IsHandheldFocusCandidate)
            .Select(ob => (Object: ob, Delta: new Vector2(ob.CellX - _playerLocation.X, ob.CellY - _playerLocation.Y)))
            .Where(x => x.Delta.Length() > 0 && x.Delta.Normalized().Dot(direction) > 0.35f)
            .OrderByDescending(x => x.Delta.Normalized().Dot(direction) * 3 - x.Delta.Length() / 24)
            .Select(x => x.Object).FirstOrDefault();
        if (best != null) SetHandheldFocus(best);
    }

    private void SetHandheldFocus(ObjectRenderer target)
    {
        if (target == _handheldFocusObject) return;
        _handheldFocusObject = target;
        _handheldTargetLocked = false;
        if (_combatController != null)
        {
            _combatController.MouseObject = target;
            _combatController.TargetObject = null; // focus alone must NOT attack/chase
        }
        ClearMagicLock();
    }

    private void CycleHandheldTarget(int delta)
    {
        var candidates = _objects.Values.Where(IsHandheldFocusCandidate)
            .OrderBy(ob => Functions.Distance(_playerLocation, new Point(ob.CellX, ob.CellY)))
            .ThenBy(ob => ob.ObjectID).ToList();
        if (candidates.Count == 0) { ClearHandheldFocus(); return; }
        int current = candidates.IndexOf(_handheldFocusObject);
        int next = current < 0 ? (delta > 0 ? 0 : candidates.Count - 1)
            : (current + delta + candidates.Count) % candidates.Count;
        SetHandheldFocus(candidates[next]);
    }

    private void ToggleHandheldTargetLock()
    {
        if (_handheldFocusObject == null) CycleHandheldTarget(1);
        if (_handheldFocusObject == null) return;
        _handheldTargetLocked = !_handheldTargetLocked;
        _handheldHud?.ShowToast(_handheldTargetLocked ? "目标已锁定" : "目标锁定已解除");
    }

    private void ToggleHandheldAutoRun()
    {
        if (_handheldAutoRun) { StopHandheldMovement(); return; }
        _handheldMoveDirection = _handheldStickMoving ? _handheldMoveDirection : _playerDirection;
        _handheldAutoRun = true;
        _handheldRequireNeutral = false;
        _handheldMoveStarted = false;
        // Do not enable MouseWalker.AutoRun: it runs toward the mouse!
        if (_mouseWalker != null) _mouseWalker.AutoRun = false;
        _handheldHud?.ShowToast("自动跑步：开（B 停止）");
    }

    // Releases and OS repeats are consumed here too, before desktop shortcuts.
    private bool HandleHandheldInputEvent(InputEvent @event)
    {
        if (!AutoLoginArgs.Handheld) return false;
        if (HandheldWorldMapOpen && @event is InputEventMouse) return true;
        if (@event is InputEventMouse && HandleJournalServicePointer(@event)) return true;
        if (JournalOpen && _journal.Visible && @event is InputEventMouse) return true;
        if (@event is not InputEventKey key) return false;
        double now = Godot.Time.GetTicksMsec();
        EnsureHandheldActionConfiguration();
        SyncHandheldActionOwnership(now);
        var button = HandheldButtonForKey(key.Keycode);
        // Track physical edges against the owner before routing. A release that
        // closes a menu still belongs to that menu and can only clear its gate.
        if (button is { } mapped) TrackHandheldActionKey(key, mapped, now);
        if (!_handheldActionFocused) return IsHandheldSemanticKey(key.Keycode) || IsHandheldSystemKey(key.Keycode);
        try { return RouteHandheldInputKey(key, button.HasValue, now); }
        finally { SyncHandheldActionOwnership(Godot.Time.GetTicksMsec()); }
    }

    private bool RouteHandheldInputKey(InputEventKey key, bool mapped, double now)
    {
        if (GmInspectionOpen) return _gmInspection.HandleKey(key) || IsHandheldSemanticKey(key.Keycode) || IsHandheldSystemKey(key.Keycode);
        if (HandheldWorldMapOpen)
        {
            if (key.Keycode == Key.F15) RouteHandheldSelect(key, now);
            else if (key is { Keycode: Key.Escape, Pressed: true, Echo: false }) CloseHandheldWorldMap();
            return true;
        }
        if (HandheldActionBindingsOpen) { RouteHandheldBindingsKey(key); return true; }
        if (RouteHandheldPromptKey(key)) return true;
        if (!HandheldTextEditing && JournalServiceActive && HandleJournalServiceKey(key)) return true;
        if (key.Keycode == Key.F15 && (!JournalOpen || _journal.Visible))
        {
            RouteHandheldSelect(key, now);
            return true;
        }
        if (RouteJournalKey(key, now)) return true;
        if (key.Keycode == Key.F10)
        {
            if (key.Pressed && !key.Echo && !HandheldGameplayBlocked && !JournalServiceActive) ToggleHandheldHudMode();
            return true;
        }
        if (key.Keycode is Key.F11 or Key.Menu)
        {
            if (key.Pressed && !key.Echo && !HandheldGameplayBlocked && !JournalServiceActive)
                OpenJournal();
            return true;
        }
        if (HandheldGameplayBlocked || JournalServiceActive)
        {
            if (key is { Keycode: Key.Escape, Pressed: true, Echo: false })
            {
                GetViewport()?.GuiGetFocusOwner()?.ReleaseFocus();
                if (DXItemCell.SelectedCell != null || _selectedCurrency != null)
                { DXItemCell.SelectedCell = null; _selectedCurrency = null; }
                else if (_bigMap?.Visible == true) _bigMap.Visible = false;
                else WindowManager.CloseTop();
            }
            // Native text/confirmation widgets retain their Enter action.
            return IsHandheldSemanticKey(key.Keycode) && key.Keycode is not (Key.Enter or Key.KpEnter);
        }
        if (mapped)
        {
            RunHandheldActionIntents();
            return true;
        }
        if (key.Keycode is not (Key.F13 or Key.F14)) return false;
        if (key.Pressed && !key.Echo && HandheldActionOwnsWorld)
        {
            if (key.Keycode == Key.F13) ToggleHandheldAutoRun();
            else ToggleHandheldTargetLock();
        }
        return true;
    }

    private static bool IsHandheldSystemKey(Key key) => key is Key.F10 or Key.F11 or Key.F15 or Key.Menu;

    private static bool IsHandheldSemanticKey(Key key) => HandheldButtonForKey(key).HasValue || key is Key.F13 or Key.F14;

    private void HandheldPickUp()
    {
        if (_observer || _player?.Dead != false || _net?.Connection?.Connected != true) return;
        var item = IsHandheldFocusCandidate(_handheldFocusObject) && _handheldFocusObject.Type == ObjectRenderer.Kind.Item
            ? _handheldFocusObject : _objects.Values.Where(IsHandheldFocusCandidate)
                .Where(x => x.Type == ObjectRenderer.Kind.Item)
                .OrderBy(x => Functions.Distance(_playerLocation, new Point(x.CellX, x.CellY))).FirstOrDefault();
        StopHandheldMovement();
        if (item != null) BeginGroundItemPickup(item); else SendPickUp();
    }

    private void HandheldAttack(ObjectRenderer target)
    {
        if (!HandheldActionOwnsWorld || !CanPlayerTurn() || _combatController == null
            || _pendingMagicPacket != null || IsFishingActive || IsTamingActive
            || target.Type != ObjectRenderer.Kind.Monster || !string.IsNullOrWhiteSpace(target.PetOwner)
            || !CombatController.CanAttackObject(target)) return;
        StopHandheldMovement();
        _combatController.TargetObject = target;
        var targetCell = new Point(target.CellX, target.CellY);
        if (Functions.Distance(_playerLocation, targetCell) <= 1)
            _combatController.RequestHandheldAttack(Functions.DirectionFromPoint(_playerLocation, targetCell));
    }

    private void HandheldConfirm()
    {
        var target = _handheldFocusObject;
        if (!HandheldActionOwnsWorld || !IsHandheldFocusCandidate(target)) return;
        if (target.Type == ObjectRenderer.Kind.NPC)
        {
            StopHandheldMovement();
            // PC sends NPCCall directly and lets the server decide interaction
            // range. Pathing to the occupied NPC cell can otherwise stall forever.
            if (!TrySendNpcCall(target.ObjectID)) _handheldHud?.ShowToast("正在联系 NPC，请稍候");
        }
        else if (target.Type == ObjectRenderer.Kind.Item) HandheldPickUp();
        else if (target.Type == ObjectRenderer.Kind.Monster) HandheldAttack(target);
    }

    // 服务端保存全部十个链接；旧版 HUD 只画六格，不能依赖可见腰带控件。
    private ClientBeltLink GetHandheldBeltLink(int slot)
        => AutoLoginArgs.Handheld && (uint)slot < (uint)BeltLinks.Length ? BeltLinks[slot] : null;

    private static bool MatchesHandheldBeltLink(ClientBeltLink link, ClientUserItem item)
        => link != null && item?.Info != null && item.Count > 0
           && (link.LinkInfoIndex > 0 ? item.Info.Index == link.LinkInfoIndex
                                      : link.LinkItemIndex > 0 && item.Index == link.LinkItemIndex);

    internal ClientUserItem GetHandheldBeltItem(int slot)
    {
        var link = GetHandheldBeltLink(slot);
        if (link == null) return null;
        var item = Inventory.FirstOrDefault(candidate => MatchesHandheldBeltLink(link, candidate))
            ?? CompanionInventory.FirstOrDefault(candidate => MatchesHandheldBeltLink(link, candidate));
        return IsHandheldBeltItem(item) ? item : null;
    }

    internal long GetHandheldBeltCount(int slot)
    {
        var link = GetHandheldBeltLink(slot);
        if (link == null) return 0;
        if (link.LinkInfoIndex <= 0) return GetHandheldBeltItem(slot)?.Count ?? 0;
        return Inventory.Concat(CompanionInventory)
            .Where(item => MatchesHandheldBeltLink(link, item) && IsHandheldBeltItem(item)).Sum(item => item.Count);
    }

    private bool CanUseHandheldQuickAction => AutoLoginArgs.Handheld
        && _startGameShown && !_observer && _player?.Dead == false
        && _net?.Connection?.Connected == true && !HandheldGameplayBlocked
        && DXItemCell.SelectedCell == null && _selectedCurrency == null;

    internal bool TryUseHandheldBelt(int slot)
    {
        if (!CanUseHandheldQuickAction) return false;
        var link = GetHandheldBeltLink(slot);
        if (link == null) return false;
        // 使用真实的背包格，让物品限制、锁定、冷却和发包沿用同一条链路。
        var cell = InventoryCells.FirstOrDefault(cell => MatchesHandheldBeltLink(link, cell?.Item) && IsHandheldBeltItem(cell?.Item))
            ?? CompanionInventoryCells.FirstOrDefault(cell => MatchesHandheldBeltLink(link, cell?.Item) && IsHandheldBeltItem(cell?.Item));
        return cell?.UseItem() == true;
    }

    internal bool TryUseHandheldMagic(ClientUserMagic magic)
        => CanUseHandheldQuickAction && _pendingMagicPacket == null && CanRequestMagic()
           && IsHandheldBindableSkill(magic)
           && UserMagics.TryGetValue(magic.Info, out var learned) && ReferenceEquals(learned, magic)
           && UseHandheldFocusedMagic(magic);

    private bool UseHandheldFocusedMagic(ClientUserMagic magic)
    {
        ValidateHandheldFocus();
        if (_combatController != null) _combatController.MouseObject = _handheldFocusObject;
        return UseMagic(magic);
    }

    private Point HandheldMagicCell(Point mouseCell)
    {
        if (AutoLoginArgs.Handheld) return HandheldCastCell();
        return _combatController?.MouseCell() ?? mouseCell;
    }

    private Point HandheldCastCell()
    {
        if (IsHandheldFocusCandidate(_handheldFocusObject))
            return new Point(_handheldFocusObject.CellX, _handheldFocusObject.CellY);
        if (_handheldHasAim) return _handheldAimCell;
        return Functions.Move(_playerLocation, _playerDirection, 3);
    }
}

/// <summary>fbdev 没有硬件鼠标光标；直接在视口坐标绘制对话框指针。</summary>
public partial class HandheldPointer : Control
{
    private readonly GameScene _scene;
    private static readonly Vector2[] Points = { Vector2.Zero, new(0, 15), new(4, 11), new(8, 18), new(11, 16), new(7, 10), new(14, 10) };
    private static readonly Vector2[] Outline = { Points[0], Points[1], Points[2], Points[3], Points[4], Points[5], Points[6], Points[0] };
    public HandheldPointer(GameScene scene)
    {
        _scene = scene;
        MouseFilter = MouseFilterEnum.Ignore;
    }
    public override void _Process(double delta)
    {
        Visible = _scene.HandheldPointerMode;
        if (Visible) { Position = GetViewport().GetMousePosition(); QueueRedraw(); }
    }
    public override void _Draw()
    {
        DrawColoredPolygon(Points, Colors.White);
        DrawPolyline(Outline, Colors.Black, 1);
    }
}

/// <summary>掌机目标高亮与独立地面施法落点。</summary>
public partial class HandheldFocusReticle : Node2D
{
    private readonly GameScene _scene;
    public HandheldFocusReticle(GameScene scene) { _scene = scene; ZIndex = 250; }
    public override void _Process(double delta) => QueueRedraw();
    public override void _Draw()
    {
        var target = _scene._handheldFocusObject;
        if (target == null || !IsInstanceValid(target)) return;
        Vector2 center = target.Position + new Vector2(0, -16);
        Color gold = new(1, 0.84f, 0, 0.95f);
        const float w = 24, h = 32, corner = 8;
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            {
                var p = center + new Vector2(x * w, y * h);
                DrawLine(p, p - new Vector2(x * corner, 0), gold, 2);
                DrawLine(p, p - new Vector2(0, y * corner), gold, 2);
            }
        string name = target.DisplayName ?? target.Name;
        if (!string.IsNullOrEmpty(name))
            RenderPrimitives.DrawLabelWithBackground(this, name, center + new Vector2(0, h + 16), Colors.White, 9);
    }
}
