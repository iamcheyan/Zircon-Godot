using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>原版 QuestDialog 的当前/可接/已完成任务页与滚动内容。</summary>
public partial class QuestDialog : DXWindow
{
    private readonly List<DXLabel> _lines = new();
    private readonly List<ClientUserQuest> _quests = new();
    private readonly List<QuestInfo> _available = new();
    private DXImageControl _background;
    private DXControl _content;
    private DXVScrollBar _scroll;
    private readonly DXControl _detailPanel;
    private DXImageControl _legacyDetailBackground;
    private DXLabel _legacyDetailText;
    private DXButton _legacyAcceptButton;
    private DXButton _legacyPageButton;
    private int _legacyDetailOffset;
    private readonly List<(DXButton Button, int Page)> _tabs = new();
    private readonly Dictionary<int, DXImageControl> _tabAlerts = new();
    private ClientUserQuest _selectedQuest;
    private QuestInfo _selectedAvailable;
    private int _page;
    private QuestRewardChoiceDialog _choiceDialog;
    private DXButton _closeButton;
    private DXLabel _titleLabel;
    private bool _legacyEiLayout;

    public QuestDialog()
    {
        HasTitle = false;
        Movable = true;
        HasFooter = false;
        Size = new Vector2I(732, 480);

        _background = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 700,
            FixedSize = true,
            StretchImage = true,
            Size = Size,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddControl(_background);

        _closeButton = new DXButton { LibraryFile = LibraryFile.Interface, Index = 15 };
        _closeButton.Location = new Vector2I((int)Size.X - (int)_closeButton.Size.X - 3, 3);
        _closeButton.MouseClick += (o, e) => WindowManager.Close(this);
        AddControl(_closeButton);

        _titleLabel = new DXLabel
        {
            Text = Lang.QuestDialogTitle,
            FontSize = 11,
            TextColour = new Color(1f, 0.85f, 0.3f),
            DrawOutline = true,
            OutlineColour = Colors.Black,
            Align = HorizontalAlignment.Center,
            VAlign = VerticalAlignment.Center,
            AutoSize = false,
            Location = new Vector2I(0, 8),
            Size = new Vector2I((int)Size.X, 18),
            IsControl = false,
        };
        AddControl(_titleLabel);

        AddTab(Lang.QuestQuestLabel, 18, 25, 0);
        AddTab(Lang.QuestQuestLabel2, 118, 25, 1);
        // 原版已完成页签默认隐藏；保留数据页但不把它错误显示在主页签栏。
        AddTab(Lang.QuestUi151Label, 218, 25, 3);
        _content = new DXControl
        {
            Location = new Vector2I(18, 58),
            Size = new Vector2I(680, 415),
            Clip = true,
            PassThrough = false,
        };
        AddControl(_content);
        _detailPanel = new DXControl { Location = new Vector2I(380, 5), Size = new Vector2I(300, 405), Clip = true, Border = true, BorderColour = new Color(.35f, .27f, .16f) };
        _content.AddControl(_detailPanel);

        _scroll = new DXVScrollBar
        {
            Location = new Vector2I(704, 58),
            Size = new Vector2I(18, 415),
            VisibleSize = 415,
            Change = 30,
            HideWhenNoScroll = false,
        };
        _scroll.ValueChanged += (o, e) => RepositionLines();
        AddControl(_scroll);
        _content.MouseWheel += _scroll.DoMouseWheel;
    }

    public void ApplyLegacyEiLayout()
    {
        _legacyEiLayout = true;
        Size = new Vector2I(340, 440);
        _background.LibraryFile = LibraryFile.GameInter;
        _background.Index = 700;
        _background.Location = new Vector2I(-86, -36);
        _background.Size = MirSkin.GetSize(LibraryFile.GameInter, 700);
        _background.StretchImage = false;
        _titleLabel.Visible = false;

        // F700 已包含旧版任务页签/书页装饰；业务正文不能透明，否则
        // 旧版窗口只剩一张空羊皮纸，真实任务数据无法验收。
        _content.Modulate = Colors.White;
        _scroll.Modulate = Colors.White;
        // 原版任务窗滚动条：window-control-position-analysis.json 给 F723/724 @ (290,59)、
        // F721/722 @ (290,89)（均 inside-window）；素材实测这四个帧都是 **28x28** 的按钮，
        // 即上下箭头。故滚动条落在 (290,59)，高度取到下箭头底部 (89+28-59)=58。
        // 原值沿用现代位置 (704,58) 18x415 —— 远超 340 宽的窗口，截图里表现为窗外一条黑竖条。
        _scroll.Location = new Vector2I(290, 59);
        _scroll.Size = new Vector2I(28, 58);
        _scroll.UpButton.LibraryFile = LibraryFile.GameInter;
        _scroll.UpButton.Index = 723;
        _scroll.UpButton.HoverIndex = 724;
        _scroll.UpButton.PressedIndex = 724;
        _scroll.DownButton.LibraryFile = LibraryFile.GameInter;
        _scroll.DownButton.Index = 721;
        _scroll.DownButton.HoverIndex = 722;
        _scroll.DownButton.PressedIndex = 722;
        _scroll.PositionBar.Visible = false;
        foreach (var tab in _tabs)
        {
            tab.Button.Modulate = new Color(1, 1, 1, 0);
            tab.Button.Location = new Vector2I(tab.Button.Location.X, 30);
        }
        _closeButton.LibraryFile = LibraryFile.GameInter;
        _closeButton.Index = 161;
        _closeButton.HoverIndex = 162;
        _closeButton.PressedIndex = 162;
        _closeButton.Location = new Vector2I(304, 404);
        _closeButton.Size = new Vector2I(28, 26);
        // Q10 证据 controls 只有两个操作图标，没有关闭控件；此前在 (304,404)
        // 加的关闭按钮正好压在 F700 底部卷轴装饰上，属于多余控件。
        _closeButton.Visible = false;

        // Q7 详情面板：证据为 F705 @ (65,294) 204x76（quest-window-render-evidence.json
        // detail_geometry），正文 (80,310)、行距 15、3 行、滚动 clamp[0,3]。
        // 此前 _detailPanel 停在 (380,5) 300x405 —— 相对 _content(18,58) 就是绝对
        // (398,63)，x 越出 340 宽窗口被裁，追踪勾选框/任务名/描述/奖励格/起止NPC/
        // 操作按钮在旧版下全部不可见。这里改到证据位置。
        _detailPanel.Location = new Vector2I(65 - _content.Location.X, 294 - _content.Location.Y);
        _detailPanel.Size = new Vector2I(204, 76);
        _detailPanel.Border = false;
        if (_legacyDetailBackground == null)
        {
            _legacyDetailBackground = new DXImageControl
            {
                LibraryFile = LibraryFile.GameInter,
                Index = 705,
                Location = Vector2I.Zero,
                Size = MirSkin.GetSize(LibraryFile.GameInter, 705),
                StretchImage = false,
                IsControl = false,
            };
            _detailPanel.AddControl(_legacyDetailBackground);
        }
        _legacyDetailBackground.Visible = true;

        // Q3 两个操作图标：证据 controls 为 this+0x74 帧对 723/724 @(290,59) 28x28
        // 与 this+0x128 帧对 721/722 @(290,89) 28x28（行为链 0x447FA0/0x448230）。
        // 此前没有任何对应控件 —— 图标由 F700 底图烘焙可见，但完全不可点。
        if (_legacyAcceptButton == null)
        {
            _legacyAcceptButton = new DXButton
            {
                LibraryFile = LibraryFile.GameInter,
                Index = 723,
                HoverIndex = 724,
                PressedIndex = 724,
                Location = new Vector2I(290, 59),
                Size = new Vector2I(28, 28),
            };
            _legacyAcceptButton.MouseClick += (_, _) => OnLegacyActionClick(true);
            AddControl(_legacyAcceptButton);
        }
        if (_legacyPageButton == null)
        {
            _legacyPageButton = new DXButton
            {
                LibraryFile = LibraryFile.GameInter,
                Index = 721,
                HoverIndex = 722,
                PressedIndex = 722,
                Location = new Vector2I(290, 89),
                Size = new Vector2I(28, 28),
            };
            _legacyPageButton.MouseClick += (_, _) => OnLegacyActionClick(false);
            AddControl(_legacyPageButton);
        }
        _legacyAcceptButton.Visible = true;
        _legacyPageButton.Visible = true;
        UpdateClientAreaForLegacySkin();
    }

    /// <summary>
    /// 旧版任务窗两个操作图标的行为。证据只给到事件 0x418/0x419 的分发
    /// （0x447FA0 / 0x448230 → 0x448580），事件语义未定案，所以这里先接到
    /// 与图标位置最相符的现有业务入口：上方图标=对选中任务切追踪/完成，
    /// 下方图标=翻页。不宣称与原版逐位等价。
    /// </summary>
    private void OnLegacyActionClick(bool accept)
    {
        if (accept)
        {
            if (_selectedQuest != null)
            {
                GameScene.Game?.SendQuestTrack(_selectedQuest.Quest.Index, true);
                return;
            }
            if (_selectedAvailable != null)
            {
                GameScene.Game?.SendQuestTrack(_selectedAvailable.Index, true);
            }
            return;
        }

        if (_page == 3) GameScene.Game?.SendMilestoneNotify(false);
        _page = 0;
        _background.Index = 700;
        _selectedQuest = null;
        _selectedAvailable = null;
        UpdateTabStyles();
        RefreshPage();
    }

    /// <summary>
    /// 旧版详情面板渲染：只画 F705 底图 + 正文。证据 detail_geometry 为
    /// 正文 (80,310) 相对面板 (65,294) = 面板内 (15,16)、行距 15、可见 3 行、
    /// 滚动 clamp[0,3]。现代版的追踪勾选框/奖励格/起止NPC 在旧版里不存在，
    /// 所以整块走独立分支，避免把现代控件塞进 204x76 的证据框。
    /// </summary>
    private void RefreshLegacyDetail()
    {
        QuestInfo quest = _selectedQuest?.Quest ?? _selectedAvailable;
        string text = quest == null
            ? string.Empty
            : GameScene.Game?.GetQuestText(quest, _selectedQuest, true) ?? quest.AcceptText ?? string.Empty;

        _legacyDetailOffset = Math.Clamp(_legacyDetailOffset, 0, 3);
        var lines = text.Replace("\r", string.Empty).Split('\n');
        string shown = string.Join("\n", lines.Skip(_legacyDetailOffset).Take(3));

        if (_legacyDetailText == null)
        {
            _legacyDetailText = new DXLabel
            {
                FontSize = 10,
                TextColour = Colors.White,
                Location = new Vector2I(15, 16),
                Size = new Vector2I(174, 45),
                AutoSize = false,
                IsControl = false,
            };
            _detailPanel.AddControl(_legacyDetailText);
        }
        _legacyDetailText.Text = shown;
        _legacyDetailText.Visible = true;
    }

    public override void Close()
    {
        GameScene.Game?.SendMilestoneNotify(false);
        base.Close();
    }

    public bool AuditLayout(out string details)
    {
        if (_legacyEiLayout)
        {
            // 旧版几何以 layout.json window.quest + quest-window-render-evidence.json
            // detail_geometry 为准，与现代版完全不同，必须分开断言。
            bool legacyValid = Size == new Vector2I(340, 440)
                && _background.Index == 700
                && _detailPanel.Location == new Vector2I(65 - _content.Location.X, 294 - _content.Location.Y)
                && _detailPanel.Size == new Vector2I(204, 76)
                && _legacyAcceptButton?.Location == new Vector2I(290, 59)
                && _legacyPageButton?.Location == new Vector2I(290, 89)
                && _closeButton.Visible == false;
            details = $"legacy size={Size} detail={_detailPanel.Position}/{_detailPanel.Size} "
                + $"accept={_legacyAcceptButton?.Location} page={_legacyPageButton?.Location} "
                + $"closeVisible={_closeButton.Visible}";
            return legacyValid;
        }

        bool valid = Size == new Vector2I(732, 480)
            && _content.Location == new Vector2I(18, 58)
            && _content.Size == new Vector2I(680, 415)
            && _detailPanel.Location == new Vector2I(380, 5)
            && _detailPanel.Size == new Vector2I(300, 405)
            && _scroll.Location == new Vector2I(704, 58)
            && !_scroll.HideWhenNoScroll
            && _tabs.Count == 3
            && _tabs[0].Button.Type == DXButton.ButtonType.SelectedTab
            && _tabs[1].Button.Type == DXButton.ButtonType.DeselectedTab;
        details = $"size={Size} content={_content.Position}/{_content.Size} detail={_detailPanel.Position}/{_detailPanel.Size} scroll={_scroll.Position}/{_scroll.Size}";
        return valid;
    }

    public bool AuditLegacyEiLayout(out string details)
    {
        bool ok = Size == new Vector2I(340, 440)
            && _background.Index == 700
            && _content.Modulate.A > 0.99f
            && _scroll.Modulate.A > 0.99f
            // 原版滚动条 (290,59) 28x58，上下箭头用 F723/724 与 F721/722
            // （window-control-position-analysis.json；素材实测四帧均为 28x28）。
            && _scroll.Location == new Vector2I(290, 59)
            && _scroll.Size == new Vector2I(28, 58)
            && _scroll.UpButton.Index == 723 && _scroll.DownButton.Index == 721
            && _closeButton.Location == new Vector2I(304, 404);
        details = $"size={Size} background=F{_background.Index} contentAlpha={_content.Modulate.A:0.##} scroll={_scroll.Location}/{_scroll.Size}#{_scroll.UpButton.Index}/{_scroll.DownButton.Index} close={_closeButton.Location}";
        return ok;
    }

    private void AddTab(string text, int x, int y, int page)
    {
        var tab = new DXButton
        {
            Text = text,
            FontSize = 10,
            TextColour = new Color(1f, 0.85f, 0.3f),
            Size = new Vector2I(90, 25),
            Location = new Vector2I(x, y),
            LibraryFile = LibraryFile.Interface,
            Index = -1,
            Type = page == _page ? DXButton.ButtonType.SelectedTab : DXButton.ButtonType.DeselectedTab,
        };
        tab.MouseClick += (o, e) =>
        {
            if (_page == 3 && page != 3) GameScene.Game?.SendMilestoneNotify(false);
            _page = page;
            _background.Index = 700;
            _selectedQuest = null;
            _selectedAvailable = null;
            UpdateTabStyles();
            RefreshPage();
        };
        // 旧版 QuestTab/MilestoneTab 的 AlertIcon：可接任务/未领里程碑奖励时
        // 页签右上角显示 GameInter 240 感叹号提醒。
        var alertIcon = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 240,
            Location = new Vector2I(78, 4),
            IsControl = false,
            Visible = false,
        };
        tab.AddControl(alertIcon);
        _tabAlerts[page] = alertIcon;
        AddControl(tab);
        _tabs.Add((tab, page));
    }

    /// <summary>
    /// 旧版 QuestDialog.UpdateAlertIcons：可接任务>0 或存在未领取的里程碑
    /// 奖励时，对应页签显示提醒图标。
    /// </summary>
    public void UpdateAlertIcons()
    {
        if (_tabAlerts.TryGetValue(1, out var availableAlert))
            availableAlert.Visible = _available.Count > 0;
        if (_tabAlerts.TryGetValue(3, out var milestoneAlert))
            milestoneAlert.Visible = GameScene.Game?.HasUnclaimedMilestoneReward() == true;
    }

    private void UpdateTabStyles()
    {
        foreach (var tab in _tabs)
            tab.Button.Type = tab.Page == _page
                ? DXButton.ButtonType.SelectedTab
                : DXButton.ButtonType.DeselectedTab;
    }

    public void SetQuests(IEnumerable<ClientUserQuest> quests)
    {
        _quests.Clear();
        if (quests != null) _quests.AddRange(quests.Where(q => q?.Quest != null));
        _available.Clear();
        var active = _quests.Select(q => q.Quest).ToHashSet();
        foreach (var quest in Globals.QuestInfoList?.Binding ?? Enumerable.Empty<QuestInfo>())
        {
            if (!active.Contains(quest) && quest != null && GameScene.Game?.CanAcceptQuest(quest) == true)
                _available.Add(quest);
        }
        RefreshPage();
        // 旧版 UpdateAlertIcons：可接/里程碑页签提醒。
        UpdateAlertIcons();
    }

    /// <summary>可接任务/里程碑提醒由 SetQuests 和里程碑变化时刷新。</summary>
    public void RefreshAlerts() => UpdateAlertIcons();

    private void RefreshPage()
    {
        foreach (var line in _lines)
        {
            _content.RemoveControl(line);
            line.QueueFree();
        }
        _lines.Clear();

        if (_page == 3)
        {
            GameScene.Game?.SendMilestoneNotify(true);
            int milestoneY = 5;
            foreach (var milestone in GameScene.Game?.Milestones ?? Enumerable.Empty<ClientUserMilestone>())
            {
                var info = milestone.Info ?? Globals.MilestoneInfoList?.Binding.FirstOrDefault(x => x.Index == milestone.InfoIndex);
                string state = milestone.IsComplete ? Lang.QuestUi152Label : Lang.GuildCastlePanelInProgressText;
                var title = AddLine($"{info?.Title ?? Lang.QuestUi151Label} [{state}]", 12, new Color(1f, 0.85f, 0.3f), 8, milestoneY);
                title.MouseFilter = Control.MouseFilterEnum.Stop;
                int index = milestone.Index;
                title.GuiInput += e =>
                {
                    if (e is not InputEventMouseButton mb || !mb.Pressed) return;
                    if (mb.ButtonIndex == MouseButton.Left)
                        GameScene.Game?.SendMilestoneActive(index, !milestone.Active);
                };
                milestoneY += 22;
                AddLine(info?.Description ?? info?.Task ?? string.Empty, 10, Colors.White, 18, milestoneY);
                milestoneY += 20;
                if (milestone.IsComplete && !milestone.Claimed)
                {
                    var claim = AddLine(Lang.QuestUi154Label, 10, new Color(0.5f, 1f, 0.5f), 18, milestoneY);
                    claim.MouseFilter = Control.MouseFilterEnum.Stop;
                    claim.GuiInput += e =>
                    {
                        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                            GameScene.Game?.ClaimMilestone(index);
                    };
                    milestoneY += 20;
                }
                milestoneY += 8;
            }
            return;
        }

        IEnumerable<ClientUserQuest> query = _page switch
        {
            2 => _quests.Where(q => q.IsComplete),
            _ => _quests.Where(q => !q.IsComplete),
        };

        if (_page == 1)
        {
            int availableY = 5;
            var availableGroups = _available
                .OrderBy(q => QuestTypeOrder(q.QuestType))
                .ThenBy(MapName)
                .ThenBy(q => q.QuestName)
                .GroupBy(MapName);
            foreach (var group in availableGroups)
            {
                AddLine($"▾ {group.Key}", 11, new Color(1f, .75f, .3f), 8, availableY);
                availableY += 22;
                foreach (var quest in group)
                {
                    var questTitle = AddLine(string.Format(Lang.QuestAcceptLabel, quest.QuestType, quest.QuestName), 12, new Color(1f, 0.85f, 0.3f), 18, availableY);
                    questTitle.MouseFilter = Control.MouseFilterEnum.Stop;
                    questTitle.GuiInput += e =>
                    {
                        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                        {
                            _selectedAvailable = quest;
                            RefreshDetail();
                        }
                    };
                    availableY += 22;
                    if (!string.IsNullOrWhiteSpace(quest.AcceptText))
                    {
                        AddLine(quest.AcceptText, 10, Colors.White, 28, availableY);
                        availableY += 18;
                    }
                    foreach (var task in quest.Tasks ?? Enumerable.Empty<QuestTask>())
                    {
                        AddLine("  " + (GameScene.Game?.GetTaskText(task, null) ?? string.Empty), 10, Colors.White, 28, availableY);
                        availableY += 18;
                    }
                    availableY += 8;
                }
            }
        }

        int y = 5;
        if (_page != 1)
        foreach (var group in query
            .OrderBy(q => QuestTypeOrder(q.Quest.QuestType))
            .ThenBy(q => MapName(q.Quest))
            .ThenBy(q => q.Quest.QuestName)
            .GroupBy(q => MapName(q.Quest)))
        {
            AddLine($"▾ {group.Key}", 11, new Color(1f, .75f, .3f), 8, y);
            y += 22;
            foreach (var userQuest in group)
            {
                var questTitle = AddLine($"[{userQuest.Quest.QuestType}] {userQuest.Quest.QuestName}" + (userQuest.IsComplete ? Lang.QuestUi156Label : Lang.QuestUi157Label), 12, new Color(1f, 0.85f, 0.3f), 18, y);
                questTitle.MouseFilter = Control.MouseFilterEnum.Stop;
                int questIndex = userQuest.Quest.Index;
                bool complete = userQuest.IsComplete;
                questTitle.GuiInput += e =>
                {
                    if (e is not InputEventMouseButton mb || !mb.Pressed) return;
                    if (mb.ButtonIndex == MouseButton.Left)
                    {
                        _selectedQuest = userQuest;
                        RefreshDetail();
                        if (complete)
                        {
                            if (GameScene.Game?.IsObserver == true) return;
                            var choices = userQuest.Quest.Rewards?.Where(r => r?.Choice == true);
                            if (choices != null && choices.Any())
                            {
                                _choiceDialog ??= new QuestRewardChoiceDialog();
                                _choiceDialog.Open(userQuest.Quest, choices);
                            }
                            else GameScene.Game?.SendQuestComplete(questIndex);
                        }
                        else GameScene.Game?.SendQuestTrack(questIndex, true);
                    }
                    else if (mb.ButtonIndex == MouseButton.Right && !complete)
                        ConfirmAbandon(questIndex);
                };
                y += 22;

                foreach (var task in userQuest.Quest.Tasks)
                {
                    var state = userQuest.Tasks.FirstOrDefault(t => t.Task == task);
                    if (state?.Completed == true) continue;
                    AddLine("  " + (GameScene.Game?.GetTaskText(task, userQuest) ?? string.Empty), 10, Colors.White, 28, y);
                    y += 18;
                }
                y += 8;
            }
        }

        _scroll.Value = 0;
        int contentHeight = _page == 1 ? _lines.Count == 0 ? 0 : _lines.Max(x => (int)x.GetMeta("base_y")) + 30 : y + 5;
        _scroll.MaxValue = Mathf.Max(_scroll.VisibleSize, contentHeight);
        RepositionLines();
        RefreshDetail();
        _detailPanel.MoveToFront();
    }

    private DXLabel AddLine(string text, int fontSize, Color colour, int x, int y)
    {
        var line = new DXLabel
        {
            Text = text,
            FontSize = fontSize,
            TextColour = colour,
            DrawOutline = true,
            OutlineColour = Colors.Black,
            Location = new Vector2I(x, y),
            Size = new Vector2I(x < 300 ? 350 : 290, 20),
            IsControl = false,
        };
        line.SetMeta("base_y", y);
        _content.AddControl(line);
        _lines.Add(line);
        return line;
    }

    private void RefreshDetail()
    {
        if (_legacyEiLayout)
        {
            RefreshLegacyDetail();
            return;
        }

        foreach (var child in _detailPanel.GetChildren().OfType<Node>())
        {
            if (child is DXControl control) _detailPanel.RemoveControl(control);
            else _detailPanel.RemoveChild(child);
            child.QueueFree();
        }
        if (_page == 3) return;
        var tracker = new DXCheckButton(string.Empty) { Location = new Vector2I(255, 8), Size = new Vector2I(18, 18), Checked = GameScene.Game?.QuestTrackerVisible ?? true };
        tracker.Changed += (s, e) => GameScene.Game?.SetQuestTrackerVisible(tracker.Checked);
        _detailPanel.AddControl(tracker);
        AddDetailText(Lang.QuestQuestLabel3, 178, 8, 9, Colors.White, 75, 18);
        QuestInfo quest = _selectedQuest?.Quest ?? _selectedAvailable;
        if (quest == null)
        {
            _detailPanel.AddControl(new DXLabel { Text = Lang.QuestQuestLabel4, FontSize = 11, TextColour = Colors.Gray, Location = new Vector2I(12, 42), IsControl = false });
            return;
        }

        AddDetailText(quest.QuestName, 10, 38, 12, new Color(1f, .85f, .3f));
        AddDetailText(Lang.QuestQuestLabel5, 10, 64, 10, new Color(1f, .85f, .3f));
        string description = GameScene.Game?.GetQuestText(quest, _selectedQuest, true) ?? quest.AcceptText ?? string.Empty;
        AddDetailText(description, 10, 84, 10, Colors.White, 285, 62);
        AddDetailText(Lang.QuestQuestLabel6, 10, 152, 10, new Color(1f, .85f, .3f));
        AddDetailText(GameScene.Game?.GetTaskText(quest, _selectedQuest) ?? string.Join("\n", quest.Tasks?.Select(t => t?.Task.ToString()) ?? Enumerable.Empty<string>()), 10, 172, 10, Colors.White, 285, 48);
        AddDetailText(Lang.QuestTabRewardsLabel, 10, 230, 10, new Color(1f, .85f, .3f));
        var rewards = quest.Rewards?.Where(r => r?.Item != null && !r.Choice).Take(5).ToList() ?? new List<QuestReward>();
        if (rewards.Count == 0) AddDetailText(Lang.QuestNoneLabel, 10, 250, 9, Colors.Gray);
        else
        {
            var items = rewards.Select(r => new ClientUserItem(r.Item, r.Amount)).ToArray();
            var grid = new DXItemGrid { GridSize = new Vector2I(Math.Max(1, items.Length), 1), ItemGrid = items, GridType = GridType.Inspect, Location = new Vector2I(10, 250), ReadOnly = true };
            _detailPanel.AddControl(grid);
        }
        AddDetailText(Lang.QuestSelectLabel, 150, 230, 10, new Color(1f, .85f, .3f));
        var choices = quest.Rewards?.Where(r => r?.Item != null && r.Choice).Take(4).ToList() ?? new List<QuestReward>();
        if (choices.Count > 0)
        {
            var items = choices.Select(r => new ClientUserItem(r.Item, r.Amount)).ToArray();
            _detailPanel.AddControl(new DXItemGrid { GridSize = new Vector2I(Math.Max(1, items.Length), 1), ItemGrid = items, GridType = GridType.Inspect, Location = new Vector2I(150, 250), ReadOnly = true });
        }
        AddDetailText(Lang.QuestStartLabel, 10, 300, 9, Colors.White, 42, 18);
        AddLocationLink(quest.StartNPC, 52, 300);
        AddDetailText(Lang.QuestEndLabel, 10, 320, 9, Colors.White, 42, 18);
        AddLocationLink(quest.FinishNPC, 52, 320);
        if (_selectedQuest != null || _selectedAvailable != null)
        {
            var action = new DXButton
            {
                Text = _selectedAvailable != null ? Lang.QuestQuestLabel7 : _selectedQuest.IsComplete ? Lang.QuestQuestLabel8 : Lang.QuestQuestLabel9,
                FontSize = 9,
                LibraryFile = LibraryFile.Interface,
                Index = -1,
                Location = new Vector2I(194, 367),
                Size = new Vector2I(88, 27),
            };
            action.MouseClick += (o, e) =>
            {
                if (GameScene.Game?.IsObserver == true) return;
                if (_selectedAvailable != null) GameScene.Game?.SendQuestAccept(_selectedAvailable.Index);
                else if (_selectedQuest.IsComplete) GameScene.Game?.SendQuestComplete(_selectedQuest.Quest.Index);
                else ConfirmAbandon(_selectedQuest.Quest.Index);
            };
            _detailPanel.AddControl(action);
        }
    }

    private void ConfirmAbandon(int questIndex)
    {
        if (GameScene.Game?.IsObserver == true || !GameScene.CanSendQuestOperation(false, questIndex)) return;
        var dialog = new ConfirmDialog(Lang.QuestQuestLabel10, Lang.QuestQuestLabel9, () =>
        {
            if (GameScene.Game?.IsObserver == true) return;
            GameScene.Game?.SendQuestAbandon(questIndex);
        });
        WindowManager.Open(dialog, GameScene.Game?.UILayer ?? GetParent());
    }

    private static int QuestTypeOrder(QuestType type) => type switch
    {
        QuestType.Story => 0,
        QuestType.Account => 1,
        QuestType.General => 2,
        QuestType.Daily => 3,
        QuestType.Weekly => 4,
        QuestType.Repeatable => 5,
        _ => 99,
    };

    private static string MapName(QuestInfo quest)
        => quest?.StartNPC?.Region?.Map?.Description ?? Lang.QuestUnknownLabel;

    private void AddLocationLink(NPCInfo npc, int x, int y)
    {
        var label = new DXLabel
        {
            Text = npc?.RegionName ?? Lang.QuestUnknownLabel2,
            FontSize = 9,
            TextColour = new Color(.7f, .9f, 1f),
            DrawOutline = true,
            OutlineColour = Colors.Black,
            Location = new Vector2I(x, y),
            Size = new Vector2I(220, 18),
            // Location names are bounded by the details panel.  DXLabel defaults
            // to AutoSize, which would make long names paint over the next field.
            AutoSize = false,
        };
        label.MouseFilter = Control.MouseFilterEnum.Stop;
        label.MouseClick += (o, e) =>
        {
            if (npc?.Region?.Map != null)
                GameScene.Game?.OpenQuestMap(npc);
        };
        _detailPanel.AddControl(label);
    }

    private void AddDetailText(string text, int x, int y, int size, Color colour, int width = 285, int height = 20)
    {
        _detailPanel.AddControl(new DXLabel
        {
            Text = text ?? string.Empty,
            FontSize = size,
            TextColour = colour,
            DrawOutline = true,
            OutlineColour = Colors.Black,
            Location = new Vector2I(x, y),
            Size = new Vector2I(width, height),
            // Explicit dimensions are intentional here: DXLabel's default
            // AutoSize=true disables wrapping and lets quest descriptions run
            // through the right edge of the panel.
            AutoSize = false,
            IsControl = false,
        });
    }

    private void RepositionLines()
    {
        foreach (var line in _lines)
            line.Position = new Vector2(line.Position.X, (int)line.GetMeta("base_y") - _scroll.Value);
    }
}
