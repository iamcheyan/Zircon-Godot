using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>原版 ChatTab 的基础聊天记录区域。</summary>
public partial class ChatLogPanel : Control
{
    private static readonly Regex LinkedItemPattern = new(@"\[(?<Text>[^\[\]:]+):(?<ID>\d+)\]", RegexOptions.Compiled);
    private readonly List<DXLabel> _lines = new();
    private readonly DXControl _textArea;
    private readonly DXControl _tabBar;
    private readonly DXVScrollBar _scroll;
    private readonly List<DXButton> _tabs = new();
    private readonly List<ChatTabSettings> _tabSettings = new();
    private readonly List<ChatMessage> _messages = new();
    private readonly List<DXLabel> _linkedLabels = new();
    private int _selectedTab;
    private double _idleSeconds;
    private bool _legacyHudLayout;
    private const float LegacyHudChatOpacity = 1f;
    private const int MaxLines = 250;
 
    public sealed class ChatTabSettings
    {
        public string Title = Lang.ChatLogPanelChatLabel;
        public bool Transparent;
        public bool Alert;
        public bool HideTab;
        public bool ReverseList;
        public bool CleanUp;
        public bool FadeOut;
        public DXImageControl AlertIcon;
        public readonly HashSet<MessageType> EnabledTypes = new();

        public ChatTabSettings()
        {
            foreach (MessageType type in Enum.GetValues(typeof(MessageType)))
                EnabledTypes.Add(type);
        }
    }

    public ChatLogPanel()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        // 原版 ChatOptionsDialog.CreateDefaultWindows：主聊天窗初始为
        // ChatTextBox.Width × 150，而 ChatTextBox 的默认宽度是 400。
        Size = new Vector2(400, 150);
        ClipContents = true;
        _tabBar = new DXControl { Location = Vector2I.Zero, Size = new Vector2I(400, 22), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_tabBar);
        _textArea = new DXControl { Location = new Vector2I(0, 22), Size = new Vector2I(380, 124), Clip = true, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_textArea);
        _scroll = new DXVScrollBar { Location = new Vector2I(380, 22), Size = new Vector2I(18, 124), VisibleSize = 124, Change = 32, HideWhenNoScroll = true };
        _scroll.ValueChanged += (s, e) => UpdateLines();
        _textArea.MouseWheel += _scroll.DoMouseWheel;
        AddChild(_scroll);
        AddTab(Lang.ChatLogPanelChatLabel);
        var defaultSettings = GetTabSettings();
        defaultSettings.Transparent = true;
        defaultSettings.HideTab = true;
        defaultSettings.FadeOut = true;
        defaultSettings.EnabledTypes.Remove(MessageType.System);
        defaultSettings.EnabledTypes.Remove(MessageType.Combat);
        ApplySettings();
        Visible = !ClientSettings.HideChatBar;
    }

    /// <summary>
    /// 将记录面板放入 EI F50 的常驻聊天槽。F350 仍使用
    /// LegacyChatDialog 的独立 572×388 几何，不能共享这个根框。
    /// </summary>
    public void ApplyLegacyHudLayout()
    {
        _legacyHudLayout = true;
        Size = LegacyHudLayout.ChatLogSize;
        ClipContents = true;
        _tabBar.Visible = false;
        _tabBar.Size = Size;
        _textArea.Position = Vector2I.Zero;
        _textArea.Size = Size;
        _scroll.Position = Vector2I.Zero;
        _scroll.Size = Vector2I.One;
        _scroll.VisibleSize = (int)Size.Y;
        _scroll.Change = 14;
        _scroll.Visible = false;
        // EI 的 F50 常驻聊天面板必须显示系统消息；现代 ChatTab 的默认
        // 配置会隐藏 System，但该过滤器不能沿用到 legacy HUD。
        GetTabSettings().EnabledTypes.Add(MessageType.System);
        // EI 常驻聊天窗保留历史内容；现代 ChatTab 的 10 秒淡出策略会让
        // 已收到的消息从旧版固定聊天区域消失，与原版历史列表不符。
        GetTabSettings().FadeOut = false;
        GetTabSettings().CleanUp = false;
        ApplySettings();
        RebuildVisibleLines(false);
    }

    public int MessageCount => _messages.Count;
    public int VisibleLineCount => _lines.Count(line => line.Visible);
    public Vector2I TextAreaSize => new((int)_textArea.Size.X, (int)_textArea.Size.Y);

    public override void _Process(double delta)
    {
        if (_tabSettings.Count == 0) return;
 
        _idleSeconds += delta;
        var settings = _tabSettings[_selectedTab];
 
        if (settings.CleanUp && _idleSeconds > 5.0 && _messages.Count > 0)
        {
            // 原版 Remove Old 只清理普通聊天历史；保留公告/系统消息，避免重要提示消失。
            _messages.RemoveAll(message => message.Type != MessageType.Announcement && message.Type != MessageType.System);
            RebuildVisibleLines(false);
            _idleSeconds = 0;
        }
 
        bool faded = settings.FadeOut && settings.Transparent && _idleSeconds > 10.0;
        float opacity = faded
            ? (_legacyHudLayout ? 0f : 0.15f)
            : (_legacyHudLayout ? LegacyHudChatOpacity : 1f);
        _textArea.Opacity = opacity;
        // 透明/淡出时禁止滚动条被 HideWhenNoScroll 重新打开（地图中「悬空滑块」）。
        UpdateChromeVisibility(opacity);
    }

    public override void _Draw()
    {
        if (_tabSettings.Count == 0) return;
        var settings = _tabSettings[_selectedTab];
        bool transparent = settings.Transparent;
        bool faded = settings.FadeOut && transparent && _idleSeconds > 10.0;
        // 透明聊天模式下背景由「每条消息的半透明黑底」承担（旧版 GetBackColour
        // 的 FromArgb(100,0,0,0)），面板本身不再画整块底色，避免主面板上方悬浮灰色块。
        if (_lines.Count == 0 || faded || transparent) return;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0f, 0f, 0f, 0.26f));
    }

    public void AddMessage(string text, Color colour)
        => AddMessage(text, MessageType.Announcement, colour, null);
 
    public void AddMessage(string text, MessageType type, Color colour)
        => AddMessage(text, type, colour, null);
 
    public void AddMessage(string text, MessageType type, Color colour, List<ClientUserItem> linkedItems)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _idleSeconds = 0;
        _textArea.Opacity = _legacyHudLayout ? LegacyHudChatOpacity : 1f;
        _messages.Add(new ChatMessage(text, type, colour, ClientSettings.ChatBackColour(type), linkedItems));
        if (ClientSettings.LogChat)
        {
            using var file = FileAccess.Open("user://Chat Logs.txt", FileAccess.ModeFlags.WriteRead);
            if (file != null)
            {
                file.SeekEnd();
                file.StoreLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{type}] {text}");
            }
        }
        while (_messages.Count > MaxLines)
        {
            _messages.RemoveAt(0);
        }
        for (int i = 0; i < _tabSettings.Count; i++)
        {
            var settings = _tabSettings[i];
            if (i != _selectedTab && settings.Alert && IsMessageEnabled(settings.EnabledTypes, type))
                settings.AlertIcon.Visible = true;
        }
        RebuildVisibleLines(true);
    }

    public void AddTab(string title)
    {
        int index = _tabs.Count;
        var settings = new ChatTabSettings { Title = string.IsNullOrWhiteSpace(title) ? string.Format(Lang.ChatLogPanelChatLabel3, index + 1) : title };
        var tab = new DXButton
        {
            Text = settings.Title,
            FontSize = 9,
            TextColour = index == 0 ? new Color(1f, .85f, .3f) : Colors.White,
            Location = new Vector2I(index * 82, 0),
            Size = new Vector2I(80, 21),
            LibraryFile = LibraryFile.Interface,
            Index = -1,
        };
        tab.MouseClick += (s, e) => SelectTab(tab);
        settings.AlertIcon = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 240,
            Location = new Vector2I(61, 2),
            IsControl = false,
            Visible = false,
        };
        tab.AddControl(settings.AlertIcon);
        _tabBar.AddControl(tab);
        _tabs.Add(tab);
        _tabSettings.Add(settings);
        ApplySettings();
    }

    public void ResetTabs()
    {
        foreach (var tab in _tabs)
        {
            _tabBar.RemoveControl(tab);
            tab.QueueFree();
        }
        _tabs.Clear();
        _tabSettings.Clear();
        _selectedTab = 0;
        AddTab(Lang.ChatLogPanelChatLabel);
        var defaultSettings = GetTabSettings();
        defaultSettings.Transparent = true;
        defaultSettings.HideTab = true;
        defaultSettings.FadeOut = true;
        defaultSettings.EnabledTypes.Remove(MessageType.System);
        defaultSettings.EnabledTypes.Remove(MessageType.Combat);
        ApplySettings();
        RebuildVisibleLines(false);
    }

    private void SelectTab(DXButton selected)
    {
        int index = _tabs.IndexOf(selected);
        if (index < 0) return;
        SelectTab(index);
    }

    public void SelectTab(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;
        _selectedTab = index;
        foreach (var tab in _tabs)
            tab.TextColour = tab == _tabs[index] ? new Color(1f, .85f, .3f) : Colors.White;
        _tabSettings[index].AlertIcon.Visible = false;
        ApplySettings();
        RebuildVisibleLines(false);
    }

    public int SelectedTabIndex => _selectedTab;
    public int TabCount => _tabs.Count;
    public string GetTabTitle(int index) => index >= 0 && index < _tabSettings.Count ? _tabSettings[index].Title : string.Empty;
    public ChatTabSettings GetTabSettings(int index = -1)
        => _tabSettings.Count == 0 ? null : _tabSettings[Mathf.Clamp(index < 0 ? _selectedTab : index, 0, _tabSettings.Count - 1)];

    public void RenameTab(int index, string title)
    {
        if (index < 0 || index >= _tabs.Count || string.IsNullOrWhiteSpace(title)) return;
        string value = title.Trim();
        _tabSettings[index].Title = value;
        _tabs[index].Text = value;
    }

    public void RemoveTab(int index)
    {
        if (_tabs.Count <= 1 || index < 0 || index >= _tabs.Count) return;
        var tab = _tabs[index];
        _tabBar.RemoveControl(tab);
        tab.QueueFree();
        _tabs.RemoveAt(index);
        _tabSettings.RemoveAt(index);
        for (int i = index; i < _tabs.Count; i++)
            _tabs[i].Location = new Vector2I(i * 82, 0);
        _selectedTab = Math.Clamp(_selectedTab >= _tabs.Count ? _tabs.Count - 1 : _selectedTab, 0, _tabs.Count - 1);
        SelectTab(_selectedTab);
    }

    public void SetOption(string option, bool enabled)
    {
        var settings = GetTabSettings();
        if (settings == null) return;
        switch (option)
        {
            case "transparent": settings.Transparent = enabled; break;
            case "alert": settings.Alert = enabled; break;
            case "hideTab": settings.HideTab = enabled; break;
            case "reverse": settings.ReverseList = enabled; break;
            case "cleanup": settings.CleanUp = enabled; break;
            case "fade": settings.FadeOut = enabled; break;
            default: return;
        }
        ApplySettings();
        RebuildVisibleLines(false);
    }

    public bool GetOption(string option)
    {
        var settings = GetTabSettings();
        if (settings == null) return false;
        return option switch
        {
            "transparent" => settings.Transparent,
            "alert" => settings.Alert,
            "hideTab" => settings.HideTab,
            "reverse" => settings.ReverseList,
            "cleanup" => settings.CleanUp,
            "fade" => settings.FadeOut,
            _ => false,
        };
    }

    public void SetTypeEnabled(MessageType type, bool enabled)
    {
        if (_selectedTab < 0 || _selectedTab >= _tabSettings.Count) return;
        if (enabled) _tabSettings[_selectedTab].EnabledTypes.Add(type);
        else _tabSettings[_selectedTab].EnabledTypes.Remove(type);
        RebuildVisibleLines(false);
    }

    public bool IsTypeEnabled(MessageType type)
        => _selectedTab >= 0 && _selectedTab < _tabSettings.Count && _tabSettings[_selectedTab].EnabledTypes.Contains(type);

    public bool IsTypeEnabled(int tab, MessageType type)
        => tab >= 0 && tab < _tabSettings.Count && _tabSettings[tab].EnabledTypes.Contains(type);

    public void SetTypeEnabled(int tab, MessageType type, bool enabled)
    {
        if (tab < 0 || tab >= _tabSettings.Count) return;
        if (enabled) _tabSettings[tab].EnabledTypes.Add(type);
        else _tabSettings[tab].EnabledTypes.Remove(type);
        if (_selectedTab == tab) RebuildVisibleLines(false);
    }

    public int LinkedLabelCount => _linkedLabels.Count;

    public bool AuditLinkedItems(out string details)
    {
        var info = Globals.ItemInfoList?.Binding?.FirstOrDefault();
        if (info == null)
        {
            details = "itemInfo=missing";
            return false;
        }
        var item = new ClientUserItem(info, 1) { Index = 9001 };
        AddMessage(Lang.ChatLogPanelItemLabel, MessageType.Normal, Colors.White,
            new List<ClientUserItem> { item });
        bool valid = _linkedLabels.Count == 1 && _linkedLabels[0].Text.Contains(Lang.ChatLogPanelItemLabel2)
            && _tabSettings[0].AlertIcon != null;
        details = $"tabs={_tabs.Count} messages={_messages.Count} linkedLabels={_linkedLabels.Count} alertIcon={_tabSettings[0].AlertIcon != null}";
        return valid;
    }

    public void SaveTabs()
    {
        var config = new ConfigFile();
        config.SetValue("chat", "count", _tabSettings.Count);
        for (int i = 0; i < _tabSettings.Count; i++)
        {
            var settings = _tabSettings[i];
            string section = $"tab_{i}";
            config.SetValue(section, "title", settings.Title);
            config.SetValue(section, "transparent", settings.Transparent);
            config.SetValue(section, "alert", settings.Alert);
            config.SetValue(section, "hide_tab", settings.HideTab);
            config.SetValue(section, "reverse", settings.ReverseList);
            config.SetValue(section, "cleanup", settings.CleanUp);
            config.SetValue(section, "fade", settings.FadeOut);
            config.SetValue(section, "types", string.Join(",", settings.EnabledTypes.Select(type => type.ToString())));
        }
        config.SetValue("chat", "selected", _selectedTab);
        config.Save("user://chat_tabs.cfg");
    }

    public bool LoadTabs()
    {
        var config = new ConfigFile();
        if (config.Load("user://chat_tabs.cfg") != Error.Ok) return false;
        int count = Math.Max(1, config.GetValue("chat", "count", 1).AsInt32());
        foreach (var tab in _tabs)
        {
            _tabBar.RemoveControl(tab);
            tab.QueueFree();
        }
        _tabs.Clear();
        _tabSettings.Clear();
        _selectedTab = 0;
        for (int i = 0; i < count; i++)
        {
            string section = $"tab_{i}";
            AddTab(config.GetValue(section, "title", string.Format(Lang.ChatLogPanelChatLabel5, i + 1)).AsString());
            var settings = _tabSettings[i];
            settings.Transparent = config.GetValue(section, "transparent", false).AsBool();
            settings.Alert = config.GetValue(section, "alert", false).AsBool();
            settings.HideTab = config.GetValue(section, "hide_tab", false).AsBool();
            settings.ReverseList = config.GetValue(section, "reverse", false).AsBool();
            settings.CleanUp = config.GetValue(section, "cleanup", false).AsBool();
            settings.FadeOut = config.GetValue(section, "fade", false).AsBool();
            settings.EnabledTypes.Clear();
            foreach (string raw in config.GetValue(section, "types", string.Empty).AsString().Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (Enum.TryParse(raw, out MessageType type)) settings.EnabledTypes.Add(type);
        }
        SelectTab(Math.Clamp(config.GetValue("chat", "selected", 0).AsInt32(), 0, _tabs.Count - 1));
        return true;
    }

    private void ApplySettings()
    {
        if (_tabSettings.Count == 0) return;
        var settings = _tabSettings[_selectedTab];
        _tabBar.Visible = !_legacyHudLayout && (!settings.HideTab || _tabs.Count > 1);
        UpdateChromeVisibility(_textArea.Opacity);
        QueueRedraw();
    }

    /// <summary>
    /// 透明主聊天默认不显示滚动条；有溢出内容且非透明时才显示。
    /// DXVScrollBar.HideWhenNoScroll 会在 MaxValue 变化后强制 Visible，必须在其后覆盖。
    /// </summary>
    private void UpdateChromeVisibility(float textOpacity = 1f)
    {
        if (_scroll == null) return;
        if (_tabSettings.Count == 0)
        {
            _scroll.Visible = false;
            return;
        }
        var settings = _tabSettings[_selectedTab];
        bool faded = settings.FadeOut && settings.Transparent && _idleSeconds > 10.0;
        bool showScroll = !settings.Transparent
            && !faded
            && textOpacity > 0.2f
            && _lines.Count > 0
            && _scroll.MaxValue > _scroll.VisibleSize;
        _scroll.Visible = showScroll;
    }

    /// <summary>供 GameScene 布局回归审计使用；透明主聊天不应留下悬浮滚动条。</summary>
    public bool IsScrollChromeVisible => _scroll?.Visible == true;

    private void RebuildVisibleLines(bool keepBottom)
    {
        foreach (var linked in _linkedLabels)
            linked.QueueFree();
        _linkedLabels.Clear();
        foreach (var line in _lines)
        {
            _textArea.RemoveControl(line);
            line.QueueFree();
        }
        _lines.Clear();
        var filter = _tabSettings.Count > 0 ? _tabSettings[_selectedTab].EnabledTypes : null;
        foreach (var message in _messages)
        {
            if (filter != null && !IsMessageEnabled(filter, message.Type)) continue;
            string displayText = NormalizeLinkedText(message.Text);
            var line = new DXLabel
            {
                Text = displayText,
                FontSize = _legacyHudLayout ? 8 : 10,
                TextColour = message.Colour,
                // F50 自身提供底板和边框；EI 常驻槽不为每条消息绘制白色消息底。
                BackColour = _legacyHudLayout ? Colors.Transparent
                    : ResolveMessageBackColour(message.BackColour, _tabSettings[_selectedTab].Transparent),
                // EI ChatTab 的消息标签明确关闭 Outline；当前 DXLabel 的
                // 阴影也不是原版路径，会给 8px 文字增加一圈脏边。
                DrawShadow = false,
                IsControl = false,
                // Legacy HUD rows use a measured multiline height below. Leave
                // DXLabel's default AutoSize on for modern chat tabs only.
                AutoSize = !_legacyHudLayout,
                Size = new Vector2I(Math.Max(1, (int)_textArea.Size.X - 8), _legacyHudLayout ? 14 : 16),
            };
            line.Size = new Vector2I((int)line.Size.X,
                MeasureTextHeight(displayText, (int)line.Size.X, line.FontSize, _legacyHudLayout ? 14 : 16));
            _textArea.AddControl(line);
            _lines.Add(line);
            AttachPlayerNameAction(line, message.Type);
            AddLinkedItemLabels(line, message.Text, displayText, message.LinkedItems);
        }
        _scroll.MaxValue = Mathf.Max(_scroll.VisibleSize, _lines.Sum(line => (int)line.Size.Y) + 4);
        if (keepBottom) _scroll.Value = _scroll.MaxValue;
        UpdateLines();
        UpdateChromeVisibility(_textArea.Opacity);
        QueueRedraw();
    }

    private void UpdateLines()
    {
        bool reverse = _tabSettings.Count > 0 && _tabSettings[_selectedTab].ReverseList;
        int y;
        if (reverse)
        {
            y = (int)_textArea.Size.Y + _scroll.Value;
            foreach (var line in _lines)
            {
                y -= (int)line.Size.Y;
                line.Position = new Vector2(6, y);
                line.Visible = y < _textArea.Size.Y && y + line.Size.Y > 0;
            }
            return;
        }

        y = -_scroll.Value;
        foreach (var line in _lines)
        {
            line.Position = new Vector2(6, y);
            line.Visible = y < _textArea.Size.Y && y + line.Size.Y > 0;
            y += (int)line.Size.Y;
        }
    }

    private static int MeasureTextHeight(string text, int width, int fontSize, int minimumLineHeight)
    {
        float lineWidth = 0;
        int lines = 1;
        float lineHeight = Math.Max(minimumLineHeight, MirSkin.MeasureText(Lang.ChatLogPanelUi114Label, fontSize).Y);
        foreach (char ch in text ?? string.Empty)
        {
            if (ch == '\n')
            {
                lines++;
                lineWidth = 0;
                continue;
            }
            float charWidth = MirSkin.MeasureText(ch.ToString(), fontSize).X;
            if (lineWidth > 0 && lineWidth + charWidth > width)
            {
                lines++;
                lineWidth = 0;
            }
            lineWidth += charWidth;
        }
        return Math.Max(minimumLineHeight, (int)Math.Ceiling(lines * lineHeight));
    }

    private static bool IsMessageEnabled(HashSet<MessageType> filter, MessageType type)
    {
        return type switch
        {
            MessageType.Announcement or MessageType.Debug => true,
            MessageType.WhisperOut or MessageType.GMWhisperIn => filter.Contains(MessageType.WhisperIn),
            _ => filter.Contains(type),
        };
    }

    /// <summary>
    /// 对齐旧版 ChatTab.GetBackColour：透明聊天模式下，原本全透明的消息背景
    /// 换成半透明黑底 (FromArgb(100,0,0,0))，保证文字在任何画面下都可读；
    /// 非透明模式保留配置的背景色（System/公告等白色高亮底）。
    /// </summary>
    private static Color ResolveMessageBackColour(Color backColour, bool transparent)
    {
        if (!transparent) return backColour;
        if (backColour.A <= 0f) return new Color(0f, 0f, 0f, 100f / 255f);
        return backColour;
    }

    private static string NormalizeLinkedText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return LinkedItemPattern.Replace(text,
            match => $"[{match.Groups["Text"].Value}]");
    }

    private void AttachPlayerNameAction(DXLabel line, MessageType type)
    {
        if (type is not (MessageType.Normal or MessageType.Shout or MessageType.Global
            or MessageType.WhisperIn or MessageType.WhisperOut or MessageType.Group
            or MessageType.ObserverChat or MessageType.Guild)) return;

        line.MouseFilter = MouseFilterEnum.Stop;
        line.MouseClick += (sender, args) =>
        {
            int close = line.Text.IndexOf(']');
            int colon = line.Text.IndexOf(':', close + 1);
            if (close < 0 || colon <= close + 1) return;
            string name = line.Text.Substring(close + 1, colon - close - 1).Trim();
            if (!string.IsNullOrWhiteSpace(name))
                GameScene.Game?.StartPrivateMessage(name);
        };
    }

    private void AddLinkedItemLabels(DXLabel line, string sourceText, string displayText, List<ClientUserItem> linkedItems)
    {
        if (linkedItems == null || linkedItems.Count == 0) return;
        int search = 0;
        foreach (Match match in LinkedItemPattern.Matches(sourceText))
        {
            if (!int.TryParse(match.Groups["ID"].Value, out int id)) continue;
            var item = linkedItems.FirstOrDefault(x => x?.Index == id);
            if (item?.Info == null) continue;

            string visibleText = $"[{match.Groups["Text"].Value}]";
            int start = displayText.IndexOf(visibleText, search, StringComparison.Ordinal);
            if (start < 0) continue;
            search = start + visibleText.Length;

            Vector2 position = TextPosition(displayText, start, line.Size.X, line.FontSize);
            Vector2 measured = MirSkin.MeasureText(visibleText, line.FontSize);
            var linked = new DXLabel
            {
                Text = visibleText,
                FontSize = line.FontSize,
                TextColour = new Color(1f, .9f, .25f),
                DrawOutline = true,
                OutlineColour = Colors.Black,
                // 旧版 ProcessText 用 FontStyle.Underline 标记物品链接
                DrawUnderline = true,
                // 旧版链接 Sound = SoundIndex.ButtonC（悬停音效）
                Sound = Library.SoundIndex.ButtonC,
                AutoSize = false,
                Size = new Vector2I(Math.Max(1, (int)measured.X + 2), Math.Max(1, (int)measured.Y)),
                Position = position,
            };
            // 旧版悬停变红 + 显示物品悬浮提示；离开恢复黄色
            linked.MouseEnter += (sender, args) =>
            {
                GameScene.Game?.SetHoverItem(item);
                linked.TextColour = Colors.Red;
                linked.QueueRedraw();
                // 旧版链接 Sound = ButtonC 在 MouseEnter 触发
                GameScene.Game?.PlaySound(Library.SoundIndex.ButtonC);
            };
            linked.MouseLeave += (sender, args) =>
            {
                GameScene.Game?.SetHoverItem(null);
                linked.TextColour = new Color(1f, .9f, .25f);
                linked.QueueRedraw();
            };
            line.AddControl(linked);
            _linkedLabels.Add(linked);
        }
    }

    private static Vector2 TextPosition(string text, int target, float width, int fontSize)
    {
        float x = 0, y = 0;
        float lineHeight = MirSkin.MeasureText(Lang.ChatLogPanelUi114Label, fontSize).Y;
        for (int i = 0; i < target && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                x = 0;
                y += lineHeight;
                continue;
            }
            float charWidth = MirSkin.MeasureText(text[i].ToString(), fontSize).X;
            if (x > 0 && x + charWidth > width)
            {
                x = 0;
                y += lineHeight;
            }
            x += charWidth;
        }
        return new Vector2(x, y);
    }

    private sealed class ChatMessage
    {
        public readonly string Text;
        public readonly MessageType Type;
        public readonly Color Colour;
        public readonly Color BackColour;
        public readonly List<ClientUserItem> LinkedItems;

        public ChatMessage(string text, MessageType type, Color colour, Color backColour, List<ClientUserItem> linkedItems)
        {
            Text = text; Type = type; Colour = colour; BackColour = backColour;
            LinkedItems = linkedItems ?? new List<ClientUserItem>();
        }
    }

}
