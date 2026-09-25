using System.Collections.Generic;
using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>
/// 原版独立 ChatTextBox：聊天记录、输入栏、频道按钮和选项按钮是四个不同的
/// 控件层级，不能把输入框塞进 ChatTab 的滚动区域。
/// </summary>
public sealed partial class ChatTextBox : DXWindow
{
    public enum ChatMode
    {
        Local, Whisper, Group, Guild, Shout, Global, Observer,
    }

    private static readonly string[] ModeNames = { Lang.CommonControlConfigWindowColoursTabShoutChatLabel, Lang.ChatOptionsPanelWhisperChatLabel, Lang.ChatOptionsPanelGroupChatLabel, Lang.ChatOptionsPanelGuildChatLabel, Lang.ChatOptionsPanelShoutChatLabel, Lang.ChatOptionsPanelGlobalChatLabel, Lang.ChatOptionsPanelObserverChatLabel };
    private readonly DXTextInput _input;
    private readonly DXButton _modeButton;
    private readonly DXButton _optionsButton;
    private readonly List<int> _linkedItemIndexes = new();
    /// <summary>聊天/命令历史（按发送顺序，最新在末尾）。上下键在输入框内切换。</summary>
    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    /// <summary>记录切换历史前输入框里未提交的内容，下键返回时恢复。</summary>
    private string _historyDraft = string.Empty;

    public ChatMode Mode { get; private set; }
    public string LastPM { get; private set; } = string.Empty;

    public ChatTextBox()
    {
        HasTitle = false;
        HasTopBorder = false;
        HasFooter = false;
        ShowCloseButton = false;
        Movable = false;
        AllowResize = true;
        CanResizeHeight = false; // 原版 ChatTextBox: 只允许横向拉宽
        Size = new Vector2I(400, 25);
        Opacity = 0.6f;
        BackColour = new Color(0f, 0f, 0f, 0.35f);

        _modeButton = new DXButton
        {
            Text = ModeNames[0], FontSize = 9, Type = DXButton.ButtonType.SmallButton, Location = new Vector2I(0, 0),
            Size = new Vector2I(60, 24), LibraryFile = LibraryFile.Interface, Index = -1,
        };
        _modeButton.MouseClick += (s, e) => CycleMode();
        AddControl(_modeButton);

        _optionsButton = new DXButton
        {
            Text = Lang.ChatTextBoxOptionsButtonLabel, FontSize = 9, Type = DXButton.ButtonType.SmallButton, Location = new Vector2I(345, 0),
            Size = new Vector2I(50, 24), LibraryFile = LibraryFile.Interface, Index = -1,
        };
        _optionsButton.MouseClick += (s, e) => GameScene.Game?.OpenChatOptionsDialog();
        AddControl(_optionsButton);

        _input = new DXTextInput
        {
            Position = new Vector2(65, 1), Size = new Vector2(275, 23),
        };
        _input.MaxLength = Globals.MaxChatLength;
        _input.TextSubmitted += SubmitChat;
        _input.HistoryUp += () => NavigateHistory(true);
        _input.HistoryDown += () => NavigateHistory(false);
        AddControl(_input);
    }

    public void CycleMode()
    {
        Mode = (ChatMode)(((int)Mode + 1) % ModeNames.Length);
        _modeButton.Text = ModeNames[(int)Mode];
    }

    public void LinkItem(ClientUserItem item)
    {
        if (item?.Info == null || _linkedItemIndexes.Count >= Globals.MaxChatItemLinks) return;
        _input.Text += $"[{item.Info.Local()}]";
        _linkedItemIndexes.Add(item.Index);
        _input.CaretColumn = _input.Text.Length;
        _input.GrabFocus();
    }

    public void StartPM(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        LastPM = $"/{name}";
        _input.Text = LastPM + " ";
        _input.GrabFocus();
        _input.CaretColumn = _input.Text.Length;
    }

    public void OpenChat()
    {
        // HideChatBar 只表示平时隐藏输入栏；按回车/空格进入聊天时，
        // 原版会重新显示输入控件。之前这里只 GrabFocus，控件仍保持
        // Visible=false，导致按键实际上生效但用户看不到聊天窗口。
        Visible = true;
        if (string.IsNullOrEmpty(_input.Text))
        {
            _input.Text = Mode switch
            {
                ChatMode.Shout => "!",
                ChatMode.Whisper when !string.IsNullOrWhiteSpace(LastPM) => LastPM + " ",
                ChatMode.Group => "!!",
                ChatMode.Guild => "!~",
                ChatMode.Global => "!@",
                ChatMode.Observer => "#",
                _ => string.Empty,
            };
        }
        _input.GrabFocus();
        _input.CaretColumn = _input.Text.Length;
    }

    /// <summary>处理原版由 GameScene 转发的空格、回车和频道快捷键。</summary>
    public bool HandleGlobalKey(InputEventKey key)
    {
        if (!key.Pressed) return false;
        var focused = GetViewport()?.GuiGetFocusOwner();
        // 输入框获得焦点时，所有字母/数字快捷键完全留给 LineEdit。
        if (focused == _input || (focused != null && IsAncestorOf(focused)))
            return true;

        if (ClientSettings.ShiftOpenChat && key.ShiftPressed && key.Keycode is >= Key.Key0 and <= Key.Key9)
        {
            OpenChat();
            GetViewport()?.SetInputAsHandled();
            return true;
        }

        if (key.Keycode == Key.Space || key.Keycode == Key.Enter)
        {
            OpenChat();
            GetViewport()?.SetInputAsHandled();
            return true;
        }
        if (key.Keycode == Key.Slash)
        {
            OpenChat();
            if (string.IsNullOrWhiteSpace(_input.Text)) _input.Text = string.IsNullOrWhiteSpace(LastPM) ? "/" : LastPM + " ";
            _input.CaretColumn = _input.Text.Length;
            GetViewport()?.SetInputAsHandled();
            return true;
        }
        if (key.Unicode == '@' || (key.Unicode == '!' && key.ShiftPressed))
        {
            OpenChat();
            _input.Text = key.Unicode == '!' ? "!" : "@";
            _input.CaretColumn = _input.Text.Length;
            GetViewport()?.SetInputAsHandled();
            return true;
        }
        return false;
    }

    private void SubmitChat(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            GameScene.Game?.SendChat(text, new List<int>(_linkedItemIndexes));
            if (text.StartsWith('/')) LastPM = text.Split(' ')[0];

            // 记录到历史：与上一条相同则不重复，限制 100 条
            if (_history.Count == 0 || _history[_history.Count - 1] != text)
                _history.Add(text);
            if (_history.Count > 100)
                _history.RemoveAt(0);
        }
        _linkedItemIndexes.Clear();
        _historyIndex = -1;
        _historyDraft = string.Empty;
        _input.Text = string.Empty;
        _input.ReleaseFocus();
    }

    /// <summary>↑/↓ 切换历史消息。上键向前（旧），下键向后（新）。</summary>
    private void NavigateHistory(bool up)
    {
        if (_history.Count == 0) return;

        // 第一次按上键时保存当前草稿
        if (_historyIndex == -1)
        {
            _historyDraft = _input.Text;
            _historyIndex = _history.Count; // 指向末尾之后，上键从最后一条开始
        }

        if (up)
        {
            if (_historyIndex <= 0) return; // 已到最旧一条
            _historyIndex--;
        }
        else
        {
            if (_historyIndex >= _history.Count)
            {
                // 已回到草稿区，恢复草稿
                _input.Text = _historyDraft;
                _historyIndex = -1;
            }
            else
            {
                _historyIndex++;
                if (_historyIndex >= _history.Count)
                {
                    _input.Text = _historyDraft;
                    _historyIndex = -1;
                }
            }
            if (_historyIndex == -1)
            {
                _input.CaretColumn = _input.Text.Length;
                return;
            }
        }

        _input.Text = _history[_historyIndex];
        _input.CaretColumn = _input.Text.Length;
    }
}
