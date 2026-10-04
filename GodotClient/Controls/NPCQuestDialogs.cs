using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>原版 Interface 209 任务列表：六行可视、滚动条、选中后打开详情。</summary>
public sealed partial class NPCQuestListDialog : DXWindow
{
    private readonly DXControl _list;
    private readonly DXVScrollBar _scroll;
    private readonly List<QuestInfo> _quests = new();
    private readonly List<DXButton> _rows = new();
    private NPCInfo _npc;

    public NPCQuestListDialog()
    {
        HasTitle = false; HasFooter = false; Movable = false;
        // 2026-10-04 素材实测（wilsdk 解码 GameInter.wil）：任务窗是 **F700**，
        // 帧 512x512、alpha 可见区 (86,36)-(426,475) = **340x439**，bbox 原点 (86,36)。
        // 原实现用 LibraryFile.Interface 的 209 帧 —— 在 legacy 模式下同样落到原版
        // WIL 的 Interface1c[209]，那是一张 36x104 的小图标，**不是任务窗**，
        // 实机表现就是「任务列表」标题下整块黑屏。
        var background = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 700,
            FixedSize = true,
            StretchImage = false,
            Location = new Vector2I(-86, -36),   // bbox 原点对齐窗口 (0,0)
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddControl(background);
        Size = new Vector2I(340, 439);
        AddControl(new DXLabel { Text = Lang.NPCQuestsQuestLabel, FontSize = 10, TextColour = new Color(1f, .85f, .3f), DrawOutline = true, OutlineColour = Colors.Black, Align = HorizontalAlignment.Center, VAlign = VerticalAlignment.Center, Location = new Vector2I(0, 8), Size = new Vector2I((int)Size.X, 18), IsControl = false });
        var close = new DXButton { LibraryFile = LibraryFile.GameInter, Index = 161, HoverIndex = 162, PressedIndex = 162 };
        close.Location = new Vector2I(7, 7);
        close.MouseClick += (s, e) => WindowManager.Close(this); AddControl(close);
        int panelWidth = Math.Max(210, (int)Size.X - 25);
        _list = new DXControl { Location = new Vector2I(8, 37), Size = new Vector2I(panelWidth, 300), Clip = true }; AddControl(_list);
        AddControl(new DXLabel { Text = Lang.QuestQuestLabel2, FontSize = 9, Size = new Vector2I(170, 18), Location = new Vector2I(15, 344), IsControl = false, Align = HorizontalAlignment.Center });
        AddControl(new DXLabel { Text = Lang.NPCQuestsCountLabel, FontSize = 9, Size = new Vector2I(50, 18), Location = new Vector2I(205, 344), IsControl = false, Align = HorizontalAlignment.Center });
        _scroll = new DXVScrollBar { Location = new Vector2I(panelWidth - 20, 37), Size = new Vector2I(22, 300), VisibleSize = 300, Change = 22, HideWhenNoScroll = true };
        _scroll.UpButton.LibraryFile = LibraryFile.GameInter; _scroll.UpButton.Index = 723;
        _scroll.DownButton.LibraryFile = LibraryFile.GameInter; _scroll.DownButton.Index = 723;
        _scroll.PositionBar.LibraryFile = LibraryFile.None; _scroll.PositionBar.Index = -1;
        _scroll.ValueChanged += (s, e) => RefreshRows(); AddControl(_scroll);
    }

    public void OpenFor(NPCInfo npc)
    {
        _npc = npc;
        _quests.Clear();
        var complete = new List<QuestInfo>();
        var available = new List<QuestInfo>();
        var current = new List<QuestInfo>();
        foreach (var quest in npc?.StartQuests ?? Enumerable.Empty<QuestInfo>())
            if (quest != null && GameScene.Game?.CanAcceptQuest(quest) == true) available.Add(quest);
        foreach (var quest in npc?.FinishQuests ?? Enumerable.Empty<QuestInfo>())
        {
            var userQuest = quest == null ? null : GameScene.Game?.GetUserQuest(quest.Index);
            if (quest == null || userQuest == null || userQuest.Completed) continue;
            if (userQuest.IsComplete) complete.Add(quest); else current.Add(quest);
        }
        static void SortQuests(List<QuestInfo> list) => list.Sort((a, b) => string.Compare(a?.QuestName, b?.QuestName, System.StringComparison.Ordinal));
        SortQuests(complete); SortQuests(available); SortQuests(current);
        _quests.AddRange(complete); _quests.AddRange(available); _quests.AddRange(current);
        _scroll.Value = 0;
        _scroll.MaxValue = _quests.Count * 22;
        RefreshRows();
        if (_quests.Count > 0) WindowManager.Open(this, GameScene.Game?.UILayer);
        else WindowManager.Close(this);
    }

    private void RefreshRows()
    {
        foreach (var row in _rows) { _list.RemoveControl(row); row.QueueFree(); }
        _rows.Clear();
        int first = (int)_scroll.Value / 22;
        // 2026-10-04: 列表区高 300、行距 22 -> 可见 13 行；原先写死 7 行是按旧的
        // 134 高列表算的，换成 F700 窗口后必须跟着改，否则下半部分永远空着。
        int visibleRows = Math.Max(1, (int)_list.Size.Y / 22);
        for (int i = first; i < _quests.Count && i < first + visibleRows; i++)
        {
            var quest = _quests[i];
            var row = new DXButton
            {
                Text = quest?.QuestName ?? Lang.NPCQuestsQuestLabel3, FontSize = 9,
                TextColour = new Color(1f, .85f, .3f), LibraryFile = LibraryFile.Interface, Index = -1,
                Location = new Vector2I(0, (i - first) * 22), Size = new Vector2I(Math.Max(190, (int)_list.Size.X - 25), 21),
            };
            row.MouseClick += (s, e) => GameScene.Game?.OpenNPCQuestDialog(quest);
            _list.AddControl(row); _rows.Add(row);
        }
    }
}

/// <summary>原版 Interface 212 任务详情：描述、目标、奖励和接受/完成按钮。</summary>
public sealed partial class NPCQuestDialog : DXWindow
{
    private readonly DXLabel _name, _description, _tasks;
    private readonly DXItemGrid _rewardGrid, _choiceGrid;
    private readonly ClientUserItem[] _rewards = new ClientUserItem[5];
    private readonly ClientUserItem[] _choices = new ClientUserItem[4];
    private readonly List<QuestReward> _choiceRewards = new();
    private readonly DXButton _accept, _complete;
    private int _selectedChoice = -1;
    private QuestInfo _quest;

    /// <summary>原版 Complete 校验：有可选奖励时必须先选中一项（QuestSelectReward 提示）。</summary>
    public static bool CanComplete(int choiceRewards, int selectedChoice)
        => choiceRewards == 0 || (selectedChoice >= 0 && selectedChoice < choiceRewards);

    public NPCQuestDialog()
    {
        HasTitle = false; HasFooter = false; Movable = false;
        // 2026-10-04: 原用 LibraryFile.Interface[212] —— legacy 下落到
        // Interface1c[212]，实测是**空帧**（黑屏）。改用与任务列表同一个
        // GameInter[700] 任务窗底框（可见 340x439，bbox 原点 86,36）。
        var background = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 700,
            FixedSize = true,
            StretchImage = false,
            Location = new Vector2I(-86, -36),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddControl(background);
        Size = new Vector2I(340, 439);
        var close = new DXButton { LibraryFile = LibraryFile.GameInter, Index = 161, HoverIndex = 162, PressedIndex = 162 };
        close.Location = new Vector2I(7, 7);
        _name = new DXLabel { FontSize = 12, TextColour = new Color(1f, .85f, .3f), DrawOutline = true, Size = new Vector2I(334, 28), Location = new Vector2I(10, 40), IsControl = false }; AddControl(_name);
        _description = new DXLabel { FontSize = 10, TextColour = Colors.White, Size = new Vector2I(313, 81), Location = new Vector2I(13, 86), IsControl = false }; AddControl(_description);
        _tasks = new DXLabel { FontSize = 10, TextColour = Colors.White, Size = new Vector2I(334, 61), Location = new Vector2I(13, 185), IsControl = false }; AddControl(_tasks);
        AddControl(new DXLabel { Text = Lang.QuestTabRewardsLabel, FontSize = 10, DrawOutline = true, Location = new Vector2I(10, 270), IsControl = false });
        _rewardGrid = new DXItemGrid { GridSize = new Vector2I(5, 1), GridType = GridType.None, ItemGrid = _rewards, ReadOnly = true, Location = new Vector2I(12, 292) }; AddControl(_rewardGrid);
        AddControl(new DXLabel { Text = Lang.NPCQuestsRewardLabel, FontSize = 10, DrawOutline = true, Location = new Vector2I(215, 270), IsControl = false });
        _choiceGrid = new DXItemGrid { GridSize = new Vector2I(4, 1), GridType = GridType.None, ItemGrid = _choices, ReadOnly = true, Location = new Vector2I(217, 292) }; AddControl(_choiceGrid);
        for (int i = 0; i < _choiceGrid.Cells.Length; i++)
        {
            int choice = i;
            _choiceGrid.Cells[i].MouseClick += (s, e) =>
            {
                if (_choices[choice] == null) return;
                _selectedChoice = choice;
                for (int j = 0; j < _choiceGrid.Cells.Length; j++) _choiceGrid.Cells[j].Border = j == choice;
            };
        }
        _accept = new DXButton { Text = Lang.QuestQuestLabel7, FontSize = 10, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(250, (int)Size.Y - 43), Size = new Vector2I(80, 25) };
        _accept.MouseClick += (s, e) =>
        {
            if (_quest == null || GameScene.Game?.IsObserver == true) return;
            GameScene.Game.SendQuestAccept(_quest.Index);
            WindowManager.Close(this);
        }; AddControl(_accept);
        _complete = new DXButton { Text = Lang.NPCQuestsQuestLabel5, FontSize = 10, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(250, (int)Size.Y - 43), Size = new Vector2I(80, 25), Visible = false };
        _complete.MouseClick += (s, e) =>
        {
            if (_quest == null || GameScene.Game?.IsObserver == true) return;
            if (!CanComplete(_choiceRewards.Count, _selectedChoice))
            {
                GameScene.Game?.ReceiveChat("请选择一个奖励。", MessageType.System);
                return;
            }
            GameScene.Game?.SendQuestComplete(_quest.Index, _selectedChoice >= 0 ? _choiceRewards[_selectedChoice].Index : 0);
            WindowManager.Close(this);
        };
        AddControl(_complete);
    }

    public void OpenFor(QuestInfo quest)
    {
        _quest = quest;
        if (quest == null) return;
        _name.Text = quest.QuestName ?? Lang.QuestTabTasksLabel;
        var game = GameScene.Game;
        var userQuest = game?.GetUserQuest(quest.Index);
        _description.Text = game?.GetQuestText(quest, userQuest) ?? quest.AcceptText ?? quest.ProgressText ?? quest.CompletedText ?? string.Empty;
        _tasks.Text = game?.GetTaskText(quest, userQuest) ?? string.Empty;
        _selectedChoice = -1;
        _choiceRewards.Clear();
        for (int i = 0; i < _rewards.Length; i++) _rewards[i] = null;
        for (int i = 0; i < _choices.Length; i++) _choices[i] = null;
        int rewardIndex = 0, choiceIndex = 0;
        foreach (var reward in quest.Rewards ?? Enumerable.Empty<QuestReward>())
        {
            if (reward?.Item == null) continue;
            var item = new ClientUserItem(reward.Item, reward.Amount);
            if (reward.Bound) item.Flags |= UserItemFlags.Bound;
            if (reward.Choice)
            {
                if (choiceIndex < _choices.Length) { _choices[choiceIndex] = item; _choiceRewards.Add(reward); choiceIndex++; }
            }
            else if (rewardIndex < _rewards.Length) _rewards[rewardIndex++] = item;
        }
        _rewardGrid.RefreshGrid();
        _choiceGrid.RefreshGrid();
        _accept.Visible = userQuest == null;
        _complete.Visible = userQuest != null && !userQuest.Completed && userQuest.IsComplete;
        WindowManager.Open(this, GameScene.Game?.UILayer);
    }
}
