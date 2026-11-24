using Godot;
using Library;
using ZirconClient.Controls;
using S = Library.Network.ServerPackets;

namespace ZirconClient.Scripts;

public partial class GameScene
{
    private uint _journalStorageNpc;
    private DXWindow _journalFocusedPane;
    private DXControl _journalServiceFocus;
    private ReferenceRect _journalServiceOutline;
    private bool _journalCapturedClick;
    private bool _journalTradeCancelPending;

    private static bool JournalWindowVisible(DXWindow window) => window != null && IsInstanceValid(window)
        && !window.IsQueuedForDeletion() && window.IsVisibleInTree();

    private DXWindow JournalNpcPane => JournalWindowVisible(_npcSocketCombineDialog) ? _npcSocketCombineDialog
        : JournalWindowVisible(_npcSocketDialog) ? _npcSocketDialog
        : JournalWindowVisible(_npcCompanionStorageDialog) ? _npcCompanionStorageDialog
        : JournalWindowVisible(_npcQuestDialog) ? _npcQuestDialog
        : JournalWindowVisible(_npcQuestListDialog) ? _npcQuestListDialog
        : JournalWindowVisible(_npcDialog) ? _npcDialog : null;

    private DXWindow JournalServiceRoot => JournalWindowVisible(_tradeDialog) ? _tradeDialog
        : _journalStorageNpc != 0 && JournalWindowVisible(_storageDialog) ? _storageDialog
        : JournalNpcPane ?? (JournalWindowVisible(_communicationDialog) ? _communicationDialog : null);

    private bool JournalServiceActive => AutoLoginArgs.Handheld && JournalServiceRoot != null;

    private void OnJournalNpcStorageOpened(S.NPCStorage packet)
    {
        if (AutoLoginArgs.Handheld && packet != null) _journalStorageNpc = packet.ObjectID;
        OpenNpcStorage();
    }

    private void ClearJournalNpcStorageContext()
    {
        if (_journalStorageNpc == 0) return;
        _journalStorageNpc = 0;
        CancelJournalServiceSelection();
        if (JournalWindowVisible(_storageDialog)) WindowManager.Close(_storageDialog);
        SetJournalServiceFocus(null, null);
    }

    private void UpdateJournalServiceContext()
    {
        if (!AutoLoginArgs.Handheld) return;
        InvalidateHandheldNpcPrompts();
        bool connected = _net?.Connection?.Connected == true;
        bool usable = connected && _player?.Dead == false && !_observer;
        if (_journalStorageNpc != 0 && (!usable || !JournalWindowVisible(_storageDialog)
                || !_objects.ContainsKey(_journalStorageNpc)))
            ClearJournalNpcStorageContext();
        if (JournalNpcPane != null && (!usable || _npcObjectId == 0 || !_objects.ContainsKey(_npcObjectId)))
        {
            CancelJournalServiceSelection();
            if (connected) CloseNPCDialog();
            else
            {
                _npcDialog?.CancelUnsubmittedLinks();
                _inventoryDialog?.NormalMode();
                _npcDialog?.Hide(); // NPCDialog.Close sends a packet.
                CloseNPCSocketDialogs();
                CloseNPCQuestDialogs();
                CloseNPCCompanionStorage();
            }
        }
        if (!connected && JournalWindowVisible(_tradeDialog)) _tradeDialog.ClearTrade();
        if (!JournalWindowVisible(_tradeDialog)) _journalTradeCancelPending = false;
        else if (!usable) RequestJournalTradeCancellation();
        SyncServiceBook();
        SyncHandheldNpcDialogue();
        if (_journalServiceFocus == null) return;
        var pane = JournalInputPane();
        if (!usable || !JournalFocusUsable(pane, _journalServiceFocus, out var rect)) SetJournalServiceFocus(null, null);
        else
        {
            _journalServiceOutline.Position = rect.Position;
            _journalServiceOutline.Size = rect.Size;
        }
    }

    // Another legacy modal must take priority over every service pane, regardless of focus.
    private DXWindow JournalInputPane()
    {
        var root = JournalServiceRoot;
        if (root == null) return null;
        var windows = WindowManager.OpenWindows;
        for (int i = windows.Count - 1; i >= 0; i--)
        {
            var top = windows[i];
            if (!JournalWindowVisible(top) || top.GetParent() != _uiLayer) continue;
            if (top is ItemAmountDialog or ConfirmDialog || IsJournalServicePane(root, top)) return top;
            return null;
        }
        return root;
    }

    private bool IsJournalServicePane(DXWindow root, DXWindow pane) => pane != null && (pane == root
        || (root != _communicationDialog && pane == _inventoryDialog)
        || (root == JournalNpcPane && (pane == _npcDialog || pane == _npcQuestListDialog
            || pane == _npcQuestDialog || pane == _npcSocketDialog || pane == _npcSocketCombineDialog
            || pane == _npcCompanionStorageDialog || pane == _characterDialog)));

    private static Rect2 JournalControlRect(Control control)
    {
        var transform = control.GetGlobalTransformWithCanvas();
        var origin = transform * Vector2.Zero;
        return new Rect2(origin, transform * control.Size - origin);
    }

    private bool JournalFocusUsable(DXWindow pane, DXControl control, out Rect2 rect)
    {
        rect = default;
        if (pane == null || pane != _journalFocusedPane || control == null || !IsInstanceValid(control)
            || control.IsQueuedForDeletion() || !pane.IsAncestorOf(control)) return false;
        return JournalCandidateRect(control, out rect);
    }

    private bool JournalCandidateRect(DXControl control, out Rect2 rect)
    {
        rect = default;
        if (!control.IsVisibleInTree() || control.IsQueuedForDeletion() || !control.IsEnabled
            || control.MouseFilter == Control.MouseFilterEnum.Ignore || control is DXButton { CanBePressed: false }) return false;
        rect = JournalControlRect(control).Intersection(GetViewport().GetVisibleRect());
        for (Node n = control.GetParent(); n is Control ancestor; n = n.GetParent())
            if (ancestor.ClipContents) rect = rect.Intersection(JournalControlRect(ancestor));
        return rect.HasArea();
    }

    private void SetJournalServiceFocus(DXWindow pane, DXControl control)
    {
        _journalFocusedPane = pane;
        _journalServiceFocus = control;
        if (control == null)
        {
            _journalServiceOutline?.Hide();
            return;
        }
        if (_journalServiceOutline == null)
        {
            var layer = new CanvasLayer { Layer = 99 };
            _journalServiceOutline = new ReferenceRect
            {
                MouseFilter = Control.MouseFilterEnum.Ignore,
                EditorOnly = false,
                BorderColor = new Color(1f, .83f, .36f),
                BorderWidth = 2f,
            };
            AddChild(layer);
            layer.AddChild(_journalServiceOutline);
        }
        if (!JournalCandidateRect(control, out var rect)) return;
        _journalServiceOutline.Position = rect.Position;
        _journalServiceOutline.Size = rect.Size;
        _journalServiceOutline.Show();
    }

    private void MoveJournalServiceFocus(DXWindow pane, Vector2 direction)
    {
        if (pane == null) return;
        bool relative = JournalFocusUsable(pane, _journalServiceFocus, out var current);
        DXControl best = null;
        float bestScore = float.PositiveInfinity;
        FindJournalServiceFocus(pane, relative, current.GetCenter(), direction, ref best, ref bestScore);
        if (best != null) SetJournalServiceFocus(pane, best);
    }

    private void FindJournalServiceFocus(Node parent, bool relative, Vector2 origin, Vector2 direction,
        ref DXControl best, ref float bestScore)
    {
        int children = parent.GetChildCount();
        for (int i = 0; i < children; i++)
        {
            Node node = parent.GetChild(i);
            if (node.IsQueuedForDeletion() || node is CanvasItem item && !item.IsVisibleInTree()) continue;
            if (node is DXControl control && (control is DXButton or DXItemCell or DXCheckBox or DXLabel)
                && JournalCandidateRect(control, out var rect))
            {
                var delta = rect.GetCenter() - origin;
                float forward = delta.Dot(direction);
                if (!relative || forward > 1f)
                {
                    float score = relative ? forward + 3f * Mathf.Abs(delta.Cross(direction)) : 0f;
                    if (score < bestScore) { bestScore = score; best = control; }
                }
            }
            FindJournalServiceFocus(node, relative, origin, direction, ref best, ref bestScore);
        }
    }

    // X routes a source through the specific visible service. Do not invoke generic
    // right-click: if a service rejects the item it can fall through to consuming it.
    private void RouteJournalServiceItem(DXWindow root, DXWindow pane)
    {
        if (_journalTradeCancelPending || _net?.Connection?.Connected != true || _player?.Dead != false
            || _observer || DXItemCell.SelectedCell != null || _selectedCurrency != null
            || !JournalFocusUsable(pane, _journalServiceFocus, out _)
            || _journalServiceFocus is not DXItemCell { Item: not null, Locked: false, ReadOnly: false } source) return;
        if (root == _tradeDialog && pane == _inventoryDialog) _tradeDialog.TryRouteItem(source);
        else if (root == _storageDialog && pane == _inventoryDialog && source.GridType == GridType.Inventory)
        {
            var grid = source.Item.Info?.ItemEffect == ItemEffect.ItemPart ? _storageDialog.PartGrid : _storageDialog.Grid;
            if (grid?.IsVisibleInTree() == true) source.MoveItem(grid);
        }
        else if (root == _storageDialog && pane == _storageDialog
            && source.GridType is GridType.Storage or GridType.PartsStorage
            && _inventoryDialog?.Grid?.IsVisibleInTree() == true) source.MoveItem(_inventoryDialog.Grid);
        else if (root == JournalNpcPane && (pane == _inventoryDialog || pane == _characterDialog))
            TryRouteItemToNpc(source);
    }

    private bool ActivateJournalServiceFocus(DXWindow pane)
    {
        if (!JournalFocusUsable(pane, _journalServiceFocus, out _)) return false;
        var target = _journalServiceFocus;
        if (target is DXButton or DXLabel) target.TriggerClick();
        else
        {
            // Use the original slot/checkbox handlers, not synthetic global input. A slot
            // can open a quantity prompt on down, so never send up to a replacement node.
            using var click = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true };
            target._GuiInput(click);
            if (IsInstanceValid(target) && !target.IsQueuedForDeletion())
            {
                click.Pressed = false;
                target._GuiInput(click);
            }
        }
        return true;
    }

    private void RequestJournalTradeCancellation()
    {
        if (_journalTradeCancelPending || _observer || _net?.Connection?.Connected != true) return;
        _journalTradeCancelPending = true;
        SendTradeClose(); // Leave the negotiated state to the existing TradeClose response.
    }

    private void CancelJournalServiceSelection()
    {
        DXItemCell.SelectedCell = null;
        _selectedCurrency = null;
    }

    private bool HandleJournalServicePointer(InputEvent input)
    {
        if (!AutoLoginArgs.Handheld) return false;
        if (input is InputEventMouseMotion motion && motion.Relative.LengthSquared() > 0f)
            SetJournalServiceFocus(null, null);
        bool hadService = input is InputEventMouseButton && JournalServiceActive;
        if (hadService) UpdateJournalServiceContext();
        if (HandheldNpcDialogueOpen) return true;
        if (_serviceBook?.Visible == true)
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } a)
            {
                _journalCapturedClick = a.Pressed;
                if (a.Pressed) HandleServiceBookKey(Key.Enter,true,false);
            }
            return true;
        }
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } click) return false;
        if (!click.Pressed && _journalCapturedClick)
        {
            _journalCapturedClick = false;
            return true;
        }
        if ((_journalTradeCancelPending || (hadService && !JournalServiceActive)) && click.Pressed)
        {
            _journalCapturedClick = true;
            return true;
        }
        if (!click.Pressed || !JournalServiceActive || _journalServiceFocus == null) return false;
        var pane = JournalInputPane();
        if (pane == null) return false;
        _journalCapturedClick = true;
        if (_net?.Connection?.Connected == true && _player?.Dead == false && !_observer && !_journalTradeCancelPending)
            ActivateJournalServiceFocus(pane);
        return true;
    }

    private bool HandleJournalServiceKey(InputEventKey key)
    {
        var root = JournalServiceRoot;
        if (!AutoLoginArgs.Handheld || root == null) return false;
        UpdateJournalServiceContext();
        root = JournalServiceRoot;
        if (root == null) return true; // The invalidated context owns this event, not the world.
        var pane = JournalInputPane();
        if (RouteHandheldNpcKey(key)) return true;
        if (pane is ItemAmountDialog amount)
        {
            if (key.Pressed && !key.Echo) amount.HandleHandheldKey(key.Keycode);
            return true;
        }
        if (pane is ConfirmDialog confirmation)
        {
            if (key.Pressed && !key.Echo) confirmation.HandleHandheldKey(key.Keycode);
            return true;
        }
        var code = key.Keycode;
        if (HandleServiceBookKey(code,key.Pressed,key.Echo)) return true;
        bool direction = code is Key.Up or Key.Down or Key.Left or Key.Right;
        if (!key.Pressed) return direction || IsHandheldSemanticKey(code)
            || code is Key.F10 or Key.F11 or Key.Menu or Key.F15;
        if (direction)
        {
            MoveJournalServiceFocus(pane, code == Key.Up ? Vector2.Up : code == Key.Down ? Vector2.Down
                : code == Key.Left ? Vector2.Left : Vector2.Right);
            return true;
        }
        if (key.Echo) return IsHandheldSemanticKey(code) || code is Key.F10 or Key.F11 or Key.Menu or Key.F15;
        if (pane == null) return code is Key.F10 or Key.F11 or Key.Menu or Key.F15;
        if (code is Key.Enter or Key.KpEnter)
        {
            if (_journalServiceFocus == null && pane is (ItemAmountDialog or ConfirmDialog)) return false;
            if (_net?.Connection?.Connected == true && _player?.Dead == false && !_observer && !_journalTradeCancelPending)
                ActivateJournalServiceFocus(pane);
            return true;
        }
        if (code == Key.Space)
        {
            RouteJournalServiceItem(root, pane);
            return true;
        }
        if (code == Key.Escape)
        {
            SetJournalServiceFocus(null, null);
            if (pane is ItemAmountDialog or ConfirmDialog) WindowManager.Close(pane);
            else if (DXItemCell.SelectedCell != null || _selectedCurrency != null) CancelJournalServiceSelection();
            else if (pane != root) WindowManager.Close(pane);
            else if (root == _tradeDialog) RequestJournalTradeCancellation();
            else if (root == _npcDialog) CloseNPCDialog();
            else if (root == _storageDialog) ClearJournalNpcStorageContext();
            else WindowManager.Close(root);
            StopHandheldMovement();
            return true;
        }
        if (code is Key.Q or Key.E)
        {
            if (pane is ItemAmountDialog or ConfirmDialog || _journalTradeCancelPending) return true;
            DXWindow companion = root == JournalNpcPane && code == Key.Q ? _characterDialog : _inventoryDialog;
            if (root != _communicationDialog && !JournalWindowVisible(companion))
                WindowManager.Open(companion, _uiLayer); // Personal view only; never opens a capability.
            var target = pane == companion || !JournalWindowVisible(companion) || !IsJournalServicePane(root, companion)
                ? root : companion;
            WindowManager.BringToFront(target);
            SetJournalServiceFocus(null, null);
            MoveJournalServiceFocus(target, Vector2.Down);
            return true;
        }
        return IsHandheldSemanticKey(code) || code is Key.F10 or Key.F11 or Key.Menu or Key.F15;
    }
}
