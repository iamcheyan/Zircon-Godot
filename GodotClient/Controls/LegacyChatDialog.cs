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
    private readonly DXImageControl _scrollRail;
    private readonly DXButton _scrollUp, _scrollDown, _close;
    private readonly List<int> _linkedItems = new();
    private int _scrollOffset;
    private bool _draggingRail;

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

        _scrollRail = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 380,
            Location = new Vector2I(533, -208),
            FixedSize = true,
            Size = MirSkin.GetSize(LibraryFile.GameInter, 380),
            MouseFilter = MouseFilterEnum.Stop,
        };
        _scrollRail.MouseDown += (_, _) => _draggingRail = true;
        _scrollRail.MouseUp += (_, _) => _draggingRail = false;
        _scrollRail.MouseClick += (_, _) => UpdateRailScroll();
        AddControl(_scrollRail);

        _historyClip = new DXControl
        {
            Location = new Vector2I(40, 29),
            Size = new Vector2I(491, 279),
            Clip = true,
            MouseFilter = MouseFilterEnum.Stop,
        };
        _historyClip.MouseWheel += OnHistoryWheel;
        AddControl(_historyClip);

        _scrollUp = CreateSpriteButton(380, 381, new Vector2I(539, 25));
        _scrollUp.MouseClick += (_, _) => ScrollBy(VisibleRows);
        AddControl(_scrollUp);
        _scrollDown = CreateSpriteButton(382, 383, new Vector2I(539, 311));
        _scrollDown.MouseClick += (_, _) => ScrollBy(-VisibleRows);
        AddControl(_scrollDown);

        var channelFrames = new[] { (360, 361), (362, 363), (364, 365), (366, 367), (368, 369), (370, 371) };
        var channelText = new[] { "@拒绝 ", "!", "!!", "!~", "@拒绝私聊", "@拒绝行会聊天" };
        var tips = new[] { "拒绝和某人私聊", "世界喊话", "组队喊话", "行会喊话", "切换拒绝私聊", "切换拒绝行会聊天" };
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

    public override void _Process(double delta)
    {
        if (_draggingRail) UpdateRailScroll();
    }

    private static DXButton CreateSpriteButton(int frame, int hover, Vector2I location) => new()
    {
        LibraryFile = LibraryFile.GameInter,
        Index = frame,
        HoverIndex = hover,
        PressedIndex = hover,
        Location = location,
        CanBePressed = false,
    };

    public void OpenChat(Node parent)
    {
        WindowManager.Open(this, parent);
        _input.GrabFocus();
        _input.CaretColumn = _input.Text.Length;
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
        base.Close();
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

    private void UpdateRailScroll()
    {
        int maxOffset = Math.Max(0, _messages.Count - VisibleRows);
        float railRange = Math.Max(1, _scrollRail.Size.Y - 1);
        int value = Mathf.RoundToInt(Mathf.Clamp(_scrollRail.GetLocalMousePosition().Y / railRange, 0f, 1f) * maxOffset);
        SetScrollOffset(value);
    }

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
                AutoSize = false,
                Size = new Vector2I(491, LineStep),
                Location = new Vector2I(0, (i - start) * LineStep),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _historyClip.AddControl(row);
            _rows.Add(row);
        }
    }
}
