using System;
using System.Collections.Generic;
using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>EI id 8 chat popup (GameInter F350), separate from friends/mail CommunicationDialog.</summary>
public sealed partial class LegacyChatDialog : DXWindow
{
    private const int VisibleRows = 19;
    private const int LineStep = 14;
    private const int MaxMessages = 250;
    private readonly List<(string Text, Color Colour)> _messages = new();
    private readonly List<DXLabel> _rows = new();
    private readonly DXControl _historyClip;
    private readonly DXTextInput _input;
    private readonly DXImageControl _background;
    private readonly ChatScrollRail _scrollRail;
    private readonly DXButton _scrollUp, _scrollDown, _close;
    private readonly List<int> _linkedItems = new();
    private int _scrollOffset;

    /// <summary>
    /// C12 聊天窗右侧的锁链轨道（GameInter 380，16x502）在证据里是
    /// "evidence-only non-interactive"：原版滚动由消息环偏移 + 两个滚动按钮
    /// （±19 行 = ±266px）驱动，轨道本身不接收输入。此前给它加了点击/拖拽
    /// 定位，属于多余交互，已移除；滚轮与上下按钮仍然可用。
    /// </summary>
    private sealed partial class ChatScrollRail : DXImageControl
    {
        public override void _Ready()
        {
            base._Ready();
            MouseFilter = MouseFilterEnum.Ignore;
        }
    }
    public LegacyChatDialog()
    {
        HasTitle = false;
        HasTopBorder = false;
        HasFooter = false;
        DrawChrome = false;
        ShowCloseButton = false;
        DropShadow = false;
        Movable = true;
        Size = new Vector2I(572, 388);

        _background = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 350,
            Location = new Vector2I(-226, -62),
            FixedSize = true,
            Size = MirSkin.GetSize(LibraryFile.GameInter, 350),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddControl(_background);

        _scrollRail = new ChatScrollRail
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 380,
            Location = new Vector2I(533, -208),
            FixedSize = true,
            Size = MirSkin.GetSize(LibraryFile.GameInter, 380),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddControl(_scrollRail);

        // C4/4.5 证据 chat_window_evidence.refresh.draw_geometry：
        // 文字原点 (40,29)，但**首行裁剪框**是 (35,28)-(520,43) = 485x15
        // （比文字原点左多 5px），按 14px 步进；19 行 -> 高 266。
        // 此前用 (40,29) 491x279，右边多出 6px、左边少 5px。
        _historyClip = new DXControl
        {
            Location = new Vector2I(35, 28),
            Size = new Vector2I(485, 14 * 19),
            Clip = true,
            MouseFilter = MouseFilterEnum.Stop,
        };
        _historyClip.MouseWheel += OnHistoryWheel;
        AddControl(_historyClip);

        _scrollUp = CreateSpriteButton(380, 381, new Vector2I(539, 25), new Vector2I(19, 14));
        _scrollUp.MouseClick += (_, _) => ScrollBy(VisibleRows);
        AddControl(_scrollUp);
        _scrollDown = CreateSpriteButton(382, 383, new Vector2I(539, 311), new Vector2I(19, 14));
        _scrollDown.MouseClick += (_, _) => ScrollBy(-VisibleRows);
        AddControl(_scrollDown);

        var channelFrames = new[] { (360, 361), (362, 363), (364, 365), (366, 367), (368, 369), (370, 371) };
        var channelText = new[] { "@拒绝 ", "!", "!!", "!~", "@拒绝私聊", "@拒绝行会聊天" };
        var tips = new[]
        {
            "拒绝和 某人 私聊(@拒绝 某人名)",
            "大喊话(!喊话)",
            "编组 喊话(!!喊话)",
            "行会 喊话(!~喊话)",
            "拒绝 私聊(@拒绝私聊)",
            "拒绝 行会 聊天(@拒绝行会聊天)",
        };
        for (int i = 0; i < channelFrames.Length; i++)
        {
            int index = i;
            var button = CreateSpriteButton(channelFrames[i].Item1, channelFrames[i].Item2,
                new Vector2I(25 + 40 * i, 332));
            button.TooltipText = tips[i];
            button.MouseClick += (_, _) => InsertChannelTemplate(channelText[index]);
            AddControl(button);
        }

        _input = new DXTextInput
        {
            Position = new Vector2(25, 311),
            Size = new Vector2(499, 15),
            MaxLength = Globals.MaxChatLength,
        };
        _input.TextSubmitted += Submit;
        AddControl(_input);

        _close = CreateSpriteButton(161, 162, new Vector2I(532, 350));
        _close.Size = new Vector2I(28, 26);
        _close.TooltipText = Lang.CommonControlClose;
        _close.MouseClick += (_, _) => CloseChat();
        AddControl(_close);
        Visible = false;
    }

    private static DXButton CreateSpriteButton(int frame, int hover, Vector2I location, Vector2I? fallbackHitSize = null)
    {
        var button = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = frame,
            HoverIndex = hover,
            PressedIndex = hover,
            Location = location,
            CanBePressed = true,
        };
        if (fallbackHitSize.HasValue
            && (MirSkin.GetSize(LibraryFile.GameInter, frame) == Vector2I.Zero
                || MirSkin.GetSize(LibraryFile.GameInter, hover) == Vector2I.Zero))
        {
            GD.Print($"[LegacyChat] scroll button frames {frame}/{hover} unavailable; using {fallbackHitSize.Value} hit zone without sprite");
            button.Index = -1;
            button.HoverIndex = -1;
            button.PressedIndex = -1;
            button.Size = fallbackHitSize.Value;
        }
        return button;
    }

    public void OpenChat(Node parent)
    {
        WindowManager.Open(this, parent);
        _input.GrabFocus();
        _input.CaretColumn = _input.Text.Length;
        CallDeferred(nameof(RefreshVisuals));
    }

    private void RefreshVisuals()
    {
        if (!Visible) return;
        QueueRedraw();
        foreach (Node child in GetChildren())
            if (child is CanvasItem item)
                item.QueueRedraw();
    }
    public bool InputHasFocus => _input.HasFocus;

    public void CloseChat()
    {
        _input.ReleaseFocus();
        WindowManager.Close(this);
    }

    public override void Close()
    {
        _input.ReleaseFocus();
        Visible = false;
    }

    public bool HandleGlobalKey(InputEventKey key, Node parent)
    {
        if (!key.Pressed) return false;
        if (_input.HasFocus) return true;
        if (ClientSettings.ShiftOpenChat && key.ShiftPressed && key.Keycode is >= Key.Key0 and <= Key.Key9)
        {
            OpenChat(parent);
            GetViewport()?.SetInputAsHandled();
            return true;
        }
        if (key.Keycode is Key.Enter or Key.Space or Key.Slash || key.Unicode == '@' || key.Unicode == '!')
        {
            OpenChat(parent);
            if (key.Keycode == Key.Slash) _input.Text = "/";
            else if (key.Unicode == '@' || key.Unicode == '!') _input.Text = ((char)key.Unicode).ToString();
            _input.CaretColumn = _input.Text.Length;
            GetViewport()?.SetInputAsHandled();
            return true;
        }
        return false;
    }

    public void AddMessage(string text, MessageType type, Color colour)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _messages.Add((text, colour));
        if (_messages.Count > MaxMessages) _messages.RemoveAt(0);
        if (_scrollOffset == 0) RebuildRows();
    }

    public void LinkItem(ClientUserItem item)
    {
        if (item?.Info == null || _linkedItems.Count >= Globals.MaxChatItemLinks) return;
        _input.Text += $"[{item.Info.Local()}]";
        _linkedItems.Add(item.Index);
        _input.CaretColumn = _input.Text.Length;
        _input.GrabFocus();
    }


    public void StartPrivateMessage(string name, Node parent)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        WindowManager.Open(this, parent);
        _input.Text = $"/{name} ";
        _input.GrabFocus();
        _input.CaretColumn = _input.Text.Length;
    }

    private void Submit(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            GameScene.Game?.SendChat(text, new List<int>(_linkedItems));
        _linkedItems.Clear();
        _input.Text = string.Empty;
        _input.ReleaseFocus();
    }

    private void InsertChannelTemplate(string text)
    {
        if (text is "@拒绝私聊" or "@拒绝行会聊天")
            _input.Text = string.IsNullOrEmpty(_input.Text) ? text : _input.Text + " " + text;
        else if (string.IsNullOrEmpty(_input.Text))
            _input.Text = text;
        else if (text is "!" or "!!" or "!~")
            _input.Text = text + _input.Text.TrimStart('!', '~');
        else
            _input.Text = text + _input.Text;
        _input.GrabFocus();
        _input.CaretColumn = _input.Text.Length;
    }

    private void OnHistoryWheel(object sender, MouseWheelEventArgs mouseEvent)
    {
        ScrollBy(mouseEvent.Delta > 0 ? VisibleRows : -VisibleRows);
    }

    private void ScrollBy(int rows) => SetScrollOffset(_scrollOffset + rows);

    private void SetScrollOffset(int value)
    {
        _scrollOffset = Mathf.Clamp(value, 0, Math.Max(0, _messages.Count - VisibleRows));
        RebuildRows();
    }

    private void RebuildRows()
    {
        foreach (var row in _rows)
        {
            _historyClip.RemoveControl(row);
            row.QueueFree();
        }
        _rows.Clear();
        int end = Math.Max(0, _messages.Count - _scrollOffset);
        int start = Math.Max(0, end - VisibleRows);
        for (int i = start; i < end; i++)
        {
            var row = new DXLabel
            {
                Text = _messages[i].Text,
                FontSize = 9,
                TextColour = _messages[i].Colour,
                DrawOutline = true,
                // EI renders one record per fixed 14px row; the parent clip
                // truncates text horizontally instead of wrapping it.
                AutoSize = true,
                Size = new Vector2I(491, LineStep),
                Location = new Vector2I(0, (i - start) * LineStep),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _historyClip.AddControl(row);
            _rows.Add(row);
        }
    }
}
