using Godot;
using System.Collections.Generic;
using System.Linq;
using Library;
using Library.Network;
using Library.SystemModels;
using ZirconClient.Scripts;
using C = Library.Network.ClientPackets;

namespace ZirconClient.Controls;

public sealed partial class NPCSocketPanel : DXControl
{
    private const int SocketCount = 3;
    private readonly DXItemCell _target;
    private readonly DXItemCell _gem;
    private readonly DXItemCell[] _sockets = new DXItemCell[3];
    private readonly DXButton _start;
    private readonly DXAnimatedControl _gemLoop;
    private readonly DXAnimatedControl[] _socketLoops = new DXAnimatedControl[3];
    private readonly DXAnimatedControl[] _socketing = new DXAnimatedControl[3];
    private int _pendingAnimations;
    private bool _pendingOperation;

    public NPCSocketPanel()
    {
        Size = new Vector2I(188, 320);
        _target = Cell(GridType.SocketTarget, 8, 38, new Vector2I(170, 150), false, true);
        _target.ItemLibraryFile = LibraryFile.Inventory;
        _gem = Cell(GridType.SocketGem, 136, 45, new Vector2I(46, 46));
        for (int i = 0; i < _sockets.Length; i++)
        {
            _sockets[i] = Cell(GridType.SocketTarget, 21 + i * 53, 211, new Vector2I(46, 46), true);
            _socketLoops[i] = Loop(15 + i * 53, 214);
            int slot = i;
            _socketing[i] = new DXAnimatedControl
            {
                LibraryFile = LibraryFile.GameInter, BaseIndex = 5770, FrameCount = 9,
                AnimationDelay = System.TimeSpan.FromSeconds(1), UseOffSet = true, Blend = true,
                Location = new Vector2I(15 + i * 53, 214), Visible = false, IsControl = false,
            };
            _socketing[i].AfterAnimation += (s, e) => SocketingFinished(slot);
            AddControl(_socketing[i]);
        }
        _gemLoop = Loop(130, 48);
        _target.LinkChanged += cell => { _target.QueueRedraw(); RefreshSockets(); };
        _gem.LinkChanged += cell => { _gem.QueueRedraw(); SetLoopAnimation(_gemLoop, _gem.Item?.Info); };
        _start = new DXButton { Text = "开始", Type = DXButton.ButtonType.Default, Size = new Vector2I(70, 24), Location = new Vector2I(16, 280), LibraryFile = LibraryFile.Interface, Index = -1 };
        _start.MouseClick += (s, e) => Send();
        AddControl(_start);
    }

    public bool AuditLayout(out string details)
    {
        bool pass = Size == new Vector2I(188, 320)
            && _target.Position == new Vector2I(8, 38)
            && _target.Size == new Vector2I(170, 150)
            && _target.Hidden
            && _target.ItemLibraryFile == LibraryFile.Inventory
            && _gem.Position == new Vector2I(136, 45)
            && _sockets[0].Position == new Vector2I(21, 211)
            && _sockets[1].Position == new Vector2I(74, 211)
            && _sockets[2].Position == new Vector2I(127, 211)
            && _start.Position == new Vector2I(16, 280);
        details = $"target={_target.Position}/{_target.Size} targetLibrary={_target.ItemLibraryFile} sockets={_sockets.Length}";
        return pass;
    }

    private DXItemCell Cell(GridType type, int x, int y, Vector2I size, bool readOnly = false, bool hidden = false)
    {
        var cell = new DXItemCell { GridType = type, ItemGrid = new ClientUserItem[1], Slot = 0, Location = new Vector2I(x, y), Size = size, Border = !hidden, ReadOnly = readOnly, Hidden = hidden };
        cell.ShowCountLabel = false;
        AddControl(cell);
        return cell;
    }

    private void Send()
    {
        if (_target.LinkedSourceSlot < 0 || _gem.LinkedSourceSlot < 0) return;
        if (!CanUseGem(_gem.Item)) return;
        _gemLoop.Visible = false;
        foreach (var animation in _socketing) animation.Visible = false;
        _start.Enabled = false;
        _pendingOperation = true;
        GameScene.Game?.SendNPCSocketItem(new CellLinkInfo { GridType = _target.LinkedSourceGrid, Slot = _target.LinkedSourceSlot }, new CellLinkInfo { GridType = _gem.LinkedSourceGrid, Slot = _gem.LinkedSourceSlot });
    }

    private bool CanUseGem(ClientUserItem gem)
    {
        if (gem?.Info == null || gem.Info.ItemType != ItemType.SocketGem)
        {
            GameScene.Game?.ReceiveChat(Lang.UnableToSocketInvalidGem, MessageType.System);
            return false;
        }
        if (_target.Item?.Info == null)
        {
            GameScene.Game?.ReceiveChat(string.Format(Lang.NPCSocketPanelsNoneLabel, gem.Info.Local()), MessageType.System);
            return false;
        }
        var target = _target.Item;
        int shape = gem.Info.Shape;
        if (shape is not (0 or 1 or 2 or 4))
        {
            GameScene.Game?.ReceiveChat(Lang.UnableToSocketInvalidGem, MessageType.System);
            return false;
        }
        if (target.Info.ItemType is not (ItemType.Weapon or ItemType.Armour))
        {
            GameScene.Game?.ReceiveChat(string.Format(Lang.NPCSocketPanelsNoneLabel2, gem.Info.Local()), MessageType.System);
            return false;
        }
        if (shape is 0 or 1 or 2 && gem.Info.Rarity != target.Info.Rarity)
        {
            GameScene.Game?.ReceiveChat(string.Format(Lang.NPCSocketPanelsRarityLabel, gem.Info.Local()), MessageType.System);
            return false;
        }
        if (shape == 0 && target.Sockets.Count >= SocketCount)
        {
            GameScene.Game?.ReceiveChat(string.Format(Lang.NPCSocketPanelsNoneLabel3, gem.Info.Local()), MessageType.System);
            return false;
        }
        if ((shape == 1 && target.Info.ItemType != ItemType.Weapon) ||
            (shape == 2 && target.Info.ItemType != ItemType.Armour))
        {
            GameScene.Game?.ReceiveChat(string.Format(Lang.NPCSocketPanelsNoneLabel2, gem.Info.Local()), MessageType.System);
            return false;
        }
        if (shape is 1 or 2 && target.Sockets.All(x => x.Gem != null) &&
            target.Sockets.All(x => x.Gem?.InfoIndex != gem.Info.Index))
        {
            GameScene.Game?.ReceiveChat(Lang.UnableToSocketNoAvailableSocket, MessageType.System);
            return false;
        }
        return true;
    }

    private DXAnimatedControl Loop(int x, int y)
    {
        var animation = new DXAnimatedControl { LibraryFile = LibraryFile.GameInter, BaseIndex = 5800, FrameCount = 50, AnimationDelay = System.TimeSpan.FromSeconds(5), Opacity = 0.1f, UseOffSet = true, Blend = true, Loop = true, Animated = true, Visible = false, Location = new Vector2I(x, y), IsControl = false };
        AddControl(animation);
        return animation;
    }

    public void Result(Library.Network.ServerPackets.NPCSocketItem packet)
    {
        if (packet == null) return;
        if (!string.IsNullOrWhiteSpace(packet.Message)) GD.Print($"[NPC Socket] {packet.Message}");
        if (!packet.Success)
        {
            _pendingOperation = false;
            _start.Enabled = true;
            Clear(_target); Clear(_gem);
            return;
        }

        if (packet.Item != null)
        {
            if (packet.GridType == GridType.Inventory && packet.Slot >= 0 && packet.Slot < (GameScene.Game?.Inventory?.Length ?? 0))
            {
                GameScene.Game.Inventory[packet.Slot] = packet.Item;
                GameScene.Game.RefreshInventorySlot(packet.Slot);
            }
            _target.ItemGrid[0] = packet.Item;
            _target.RefreshItem();
            RefreshSockets();
        }

        // 原版回包到达时如果窗口已关闭，会直接完成操作，不等待不可见动画。
        if (!Visible)
        {
            FinishResult();
            return;
        }

        _pendingAnimations = 0;
        if (packet.GemShape == 4)
        {
            for (int i = 0; i < _sockets.Length; i++)
                if (_sockets[i].Item != null) StartSocketingAnimation(i);
        }
        else if (packet.SocketSlot >= 0 && packet.SocketSlot < _sockets.Length)
            StartSocketingAnimation(packet.SocketSlot);

        if (_pendingAnimations == 0) FinishResult();
    }

    private void StartSocketingAnimation(int slot)
    {
        if (slot < 0 || slot >= _socketing.Length) return;
        _pendingAnimations++;
        _socketing[slot].Visible = true;
        _socketing[slot].Restart(false);
    }

    private void SocketingFinished(int slot)
    {
        _socketing[slot].Visible = false;
        if (--_pendingAnimations <= 0) FinishResult();
    }

    private void FinishResult()
    {
        _pendingAnimations = 0;
        _pendingOperation = false;
        _start.Enabled = true;
        Clear(_target); Clear(_gem);
    }

    private void RefreshSockets()
    {
        var sockets = _target.Item?.Sockets ?? new System.Collections.Generic.List<ClientUserItemSocket>();
        for (int i = 0; i < _sockets.Length; i++)
        {
            var socket = sockets.FirstOrDefault(x => x.Slot == i);
            _sockets[i].ItemGrid[0] = socket?.Gem;
            _sockets[i].RefreshItem();
            SetLoopAnimation(_socketLoops[i], socket?.Gem?.Info);
        }
    }

    private static void SetLoopAnimation(DXAnimatedControl animation, ItemInfo info)
    {
        int index = info?.Shape switch { 1 => 5900, 2 => 6000, 4 => 5800, _ => 0 };
        if (index == 0)
        {
            animation.Animated = false;
            animation.Visible = false;
            return;
        }
        bool restart = animation.BaseIndex != index || !animation.Visible;
        animation.BaseIndex = index;
        animation.Visible = true;
        if (restart) animation.Restart(true);
    }

    public bool TryRouteItem(DXItemCell source)
    {
        DXItemCell target = source.Item?.Info?.ItemType == ItemType.SocketGem ? _gem : _target;
        if (target.LinkedSourceSlot >= 0 || source?.Item == null || !source.CanLinkToSpecialGrid(target.GridType)) return false;
        if (target == _gem && !CanUseGem(source.Item)) return false;
        source.MoveItem(target);
        return true;
    }

    public void Reset()
    {
        // 普通关闭不能释放服务端仍在处理的来源；回包会在窗口隐藏时走 FinishResult。
        if (_pendingOperation) return;
        if (_target.LinkedSourceSlot >= 0) GameScene.Game?.UnlockItemLink(new CellLinkInfo { GridType = _target.LinkedSourceGrid, Slot = _target.LinkedSourceSlot });
        if (_gem.LinkedSourceSlot >= 0) GameScene.Game?.UnlockItemLink(new CellLinkInfo { GridType = _gem.LinkedSourceGrid, Slot = _gem.LinkedSourceSlot });
        foreach (var cell in _sockets)
            if (cell.LinkedSourceSlot >= 0) GameScene.Game?.UnlockItemLink(new CellLinkInfo { GridType = cell.LinkedSourceGrid, Slot = cell.LinkedSourceSlot });
        Clear(_target); Clear(_gem);
        foreach (var cell in _sockets) { cell.ItemGrid[0] = null; cell.RefreshItem(); }
        foreach (var animation in _socketLoops) { animation.Visible = false; animation.Animated = false; }
        foreach (var animation in _socketing) { animation.Visible = false; animation.Animated = false; }
        _pendingAnimations = 0;
        _start.Enabled = true;
    }

    public void CancelPending()
    {
        _pendingOperation = false;
        Reset();
    }

    private static void Clear(DXItemCell cell)
    {
        if (cell.LinkedSourceSlot >= 0)
            GameScene.Game?.UnlockItemLink(new CellLinkInfo { GridType = cell.LinkedSourceGrid, Slot = cell.LinkedSourceSlot });
        if (cell.ItemGrid != null) cell.ItemGrid[0] = null;
        cell.LinkedSourceGrid = GridType.None;
        cell.LinkedSourceSlot = -1;
        cell.RefreshItem();
    }
}

public sealed partial class NPCSocketCombinePanel : DXControl
{
    private static readonly (Vector2I A, Vector2I B, Vector2I C)[] CombineFrames =
    {
        (new(7, -50), new(-44, 26), new(57, 26)), (new(-5, -49), new(-37, 36), new(62, 16)),
        (new(-14, -45), new(-29, 42), new(63, 7)), (new(-19, -39), new(-20, 45), new(61, -2)),
        (new(-23, -31), new(-10, 44), new(57, -8)), (new(-24, -24), new(-2, 43), new(51, -13)),
        (new(-24, -16), new(4, 40), new(45, -16)), (new(-21, -10), new(9, 35), new(37, -16)),
        (new(-18, -6), new(12, 31), new(32, -15)), (new(-12, -1), new(15, 25), new(27, -13)),
        (new(-8, 2), new(16, 21), new(21, -10)), (new(-3, 4), new(15, 15), new(17, -6)),
        (new(13, -2), new(6, 5), new(13, 10)),
        (new(10, 5), new(10, 5), new(10, 5)), (new(10, 5), new(10, 5), new(10, 5)),
        (new(10, 5), new(10, 5), new(10, 5)), (new(10, 5), new(10, 5), new(10, 5)),
        (new(10, 5), new(10, 5), new(10, 5)), (new(10, 5), new(10, 5), new(10, 5)),
        (new(10, 5), new(10, 5), new(10, 5)), (new(10, 5), new(10, 5), new(10, 5)),
    };

    private readonly DXItemCell[] _gems = new DXItemCell[3];
    private readonly DXItemCell _result;
    private readonly DXButton _start;
    private readonly DXAnimatedControl[] _gemLoops = new DXAnimatedControl[3];
    private readonly DXAnimatedControl _combineAnimation, _combineOverlay, _resultAnimation;
    private Library.Network.ServerPackets.NPCSocketCombine _pendingResult;
    private bool _operating, _combineFinished, _resultStarted;

    public NPCSocketCombinePanel()
    {
        Size = new Vector2I(192, 326);
        for (int i = 0; i < 3; i++)
        {
            int slot = i;
            Vector2I[] locations = { new(77, 45), new(26, 121), new(127, 121) };
            _gems[i] = new DXItemCell { GridType = (GridType)((int)GridType.SocketCombine1 + i), ItemGrid = new ClientUserItem[1], Location = locations[i], Border = true, Size = new Vector2I(46, 46), ShowCountLabel = false };
            AddControl(_gems[i]);
            _gemLoops[i] = new DXAnimatedControl { LibraryFile = LibraryFile.GameInter, BaseIndex = 5800, FrameCount = 50, AnimationDelay = System.TimeSpan.FromSeconds(5), Opacity = 0.1f, UseOffSet = true, Blend = true, Loop = true, Animated = true, Visible = false, Location = locations[i] - new Vector2I(6, -3), IsControl = false };
            AddControl(_gemLoops[i]);
        }
        _result = new DXItemCell { GridType = GridType.SocketCombineResult, ItemGrid = new ClientUserItem[1], Slot = 0, Location = new Vector2I(77, 213), Size = new Vector2I(46, 46), Border = true, ReadOnly = true, ShowCountLabel = false };
        AddControl(_result);
        _combineAnimation = new DXAnimatedControl { LibraryFile = LibraryFile.GameInter, BaseIndex = 5740, FrameCount = CombineFrames.Length, AnimationDelay = System.TimeSpan.FromSeconds(2), UseOffSet = true, Visible = false, Location = new Vector2I(70, 95), IsControl = false };
        _combineOverlay = new DXAnimatedControl { LibraryFile = LibraryFile.GameInter, BaseIndex = 5710, FrameCount = CombineFrames.Length, AnimationDelay = System.TimeSpan.FromSeconds(2), UseOffSet = true, Blend = true, Visible = false, Location = new Vector2I(70, 95), IsControl = false };
        _resultAnimation = new DXAnimatedControl { LibraryFile = LibraryFile.GameInter, BaseIndex = 5770, FrameCount = 10, AnimationDelay = System.TimeSpan.FromSeconds(1), UseOffSet = true, Blend = true, Visible = false, Location = new Vector2I(70, 215), IsControl = false };
        _combineAnimation.AfterAnimation += (s, e) =>
        {
            _combineAnimation.Visible = false; _combineOverlay.Visible = false;
            ApplyFrame(CombineFrames[^1]);
            _combineFinished = true;
            TryStartResultAnimation();
        };
        _resultAnimation.AfterAnimation += (s, e) => { _resultAnimation.Visible = false; FinishOperation(); };
        AddControl(_combineAnimation); AddControl(_combineOverlay); AddControl(_resultAnimation);
        for (int i = 0; i < _gems.Length; i++)
        {
            int slot = i;
            _gems[i].LinkChanged += cell => _gemLoops[slot].Visible = _gems[slot].LinkedSourceSlot >= 0;
        }
        _start = new DXButton { Text = "合成", Type = DXButton.ButtonType.Default, Size = new Vector2I(70, 24), Location = new Vector2I(17, 285), LibraryFile = LibraryFile.Interface, Index = -1 };
        _start.MouseClick += (s, e) => Send();
        AddControl(_start);
    }

    public bool AuditLayout(out string details)
    {
        bool pass = Size == new Vector2I(192, 326)
            && _gems[0].Position == new Vector2I(77, 45)
            && _gems[1].Position == new Vector2I(26, 121)
            && _gems[2].Position == new Vector2I(127, 121)
            && _result.Position == new Vector2I(77, 213)
            && _start.Position == new Vector2I(17, 285)
            && CombineFrames.Length == 21;
        details = $"gems={_gems[0].Position},{_gems[1].Position},{_gems[2].Position} result={_result.Position} frames={CombineFrames.Length}";
        return pass;
    }

    private void Send()
    {
        if (_gems[0].LinkedSourceSlot < 0 || _gems[1].LinkedSourceSlot < 0 || _gems[2].LinkedSourceSlot < 0)
        {
            GameScene.Game?.ReceiveChat(Lang.UnableToSocketMissingGems, MessageType.System);
            return;
        }
        if (_gems.Any(cell => cell.Item?.Info?.ItemType != ItemType.SocketGem))
        {
            GameScene.Game?.ReceiveChat(Lang.UnableToSocketMissingGems, MessageType.System);
            return;
        }
        if (_gems.Skip(1).Any(cell => cell.Item?.Info != _gems[0].Item?.Info))
        {
            GameScene.Game?.ReceiveChat(Lang.UnableToSocketMismatch, MessageType.System);
            return;
        }
        _operating = true;
        _combineFinished = false;
        _resultStarted = false;
        _pendingResult = null;
        _start.Enabled = false;
        foreach (var loop in _gemLoops) loop.Visible = false;
        _combineAnimation.Visible = true; _combineOverlay.Visible = true;
        _combineAnimation.FrameCount = CombineFrames.Length;
        _combineAnimation.Restart(false); _combineOverlay.Restart(false);
        GameScene.Game?.SendNPCSocketCombine(
            Link(_gems[0]), Link(_gems[1]), Link(_gems[2]));
    }

    private static CellLinkInfo Link(DXItemCell cell) => new() { GridType = cell.LinkedSourceGrid, Slot = cell.LinkedSourceSlot };

    public void Result(Library.Network.ServerPackets.NPCSocketCombine packet)
    {
        if (packet == null) return;
        if (!string.IsNullOrWhiteSpace(packet.Message)) GD.Print($"[NPC Socket Combine] {packet.Message}");
        if (!packet.Accepted)
        {
            _start.Enabled = true;
            if (!Visible)
            {
                _operating = false;
                Reset();
                return;
            }
            _operating = false;
            return;
        }
        _pendingResult = packet;
        if (!Visible)
        {
            ApplyResult(packet);
            FinishOperation();
            return;
        }
        TryStartResultAnimation();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (!_operating || !_combineAnimation.Visible) return;
        int frame = _combineAnimation.Index - 5740;
        if (frame >= 0 && frame < CombineFrames.Length) ApplyFrame(CombineFrames[frame]);
    }

    private void ApplyFrame((Vector2I A, Vector2I B, Vector2I C) frame)
    {
        Vector2I[] offsets = { frame.A, frame.B, frame.C };
        for (int i = 0; i < _gems.Length; i++)
        {
            _gems[i].Location = new Vector2I(70, 95) + offsets[i];
            _gemLoops[i].Location = _gems[i].Location + new Vector2I(-6, 3);
        }
    }

    private void TryStartResultAnimation()
    {
        if (!_combineFinished || _pendingResult == null || _resultStarted) return;
        _resultStarted = true;
        ApplyResult(_pendingResult);
        if (!_pendingResult.Success)
        {
            FinishOperation();
            return;
        }
        _resultAnimation.Visible = true;
        _resultAnimation.Restart(false);
    }

    private void ApplyResult(Library.Network.ServerPackets.NPCSocketCombine packet)
    {
        foreach (var slot in packet.ClearedSlots ?? new List<int>())
        {
            if (slot < 0 || slot >= (GameScene.Game?.Inventory?.Length ?? 0)) continue;
            GameScene.Game.Inventory[slot] = null;
            GameScene.Game.RefreshInventorySlot(slot);
        }
        foreach (var item in packet.Items ?? new List<ClientUserItem>())
        {
            if (item == null || item.Slot < 0 || item.Slot >= (GameScene.Game?.Inventory?.Length ?? 0)) continue;
            GameScene.Game.Inventory[item.Slot] = item;
            GameScene.Game.RefreshInventorySlot(item.Slot);
        }
        var result = packet.Items?.FirstOrDefault(x => x != null && x.Slot == packet.ResultSlot)
                     ?? packet.Items?.LastOrDefault(x => x != null);
        _result.ItemGrid[0] = result;
        _result.RefreshItem();
    }

    private void FinishOperation()
    {
        foreach (var cell in _gems)
        {
            if (cell.LinkedSourceSlot >= 0)
                GameScene.Game?.UnlockItemLink(new CellLinkInfo { GridType = cell.LinkedSourceGrid, Slot = cell.LinkedSourceSlot });
            cell.ItemGrid[0] = null;
            cell.LinkedSourceSlot = -1;
            cell.RefreshItem();
        }
        ApplyFrame((new(7, -50), new(-44, 26), new(57, 26)));
        _combineAnimation.Visible = false;
        _combineOverlay.Visible = false;
        _resultAnimation.Visible = false;
        _operating = false;
        _combineFinished = false;
        _resultStarted = false;
        _pendingResult = null;
        _start.Enabled = true;
    }

    public void CancelPending()
    {
        _operating = false;
        _pendingResult = null;
        _combineFinished = false;
        _resultStarted = false;
        ClearInputs();
        _start.Enabled = true;
    }

    private void ClearInputs()
    {
        foreach (var cell in _gems)
        {
            if (cell.LinkedSourceSlot >= 0)
                GameScene.Game?.UnlockItemLink(new CellLinkInfo { GridType = cell.LinkedSourceGrid, Slot = cell.LinkedSourceSlot });
            cell.ItemGrid[0] = null;
            cell.LinkedSourceGrid = GridType.None;
            cell.LinkedSourceSlot = -1;
            cell.RefreshItem();
        }
    }

    public bool TryRouteItem(DXItemCell source)
    {
        var target = _gems.FirstOrDefault(c => c.LinkedSourceSlot < 0);
        if (target == null || source?.Item == null || source.GridType != GridType.Inventory ||
            !source.CanLinkToSpecialGrid(target.GridType) ||
            (_gems.FirstOrDefault(c => c.Item != null)?.Item?.Info is ItemInfo input && input != source.Item.Info)) return false;
        source.MoveItem(target);
        return true;
    }

    public void Reset()
    {
        // 提交中的合成由回包完成；普通关闭不能提前释放来源锁。
        if (_operating) return;
        ClearInputs();
        _result.ItemGrid[0] = null; _result.RefreshItem();
        foreach (var loop in _gemLoops) { loop.Visible = false; loop.Animated = false; }
        _combineAnimation.Visible = false; _combineAnimation.Animated = false;
        _combineOverlay.Visible = false; _combineOverlay.Animated = false;
        _resultAnimation.Visible = false; _resultAnimation.Animated = false;
        ApplyFrame(CombineFrames[0]);
        _pendingResult = null; _operating = false; _combineFinished = false; _resultStarted = false;
        _start.Enabled = true;
    }
}
