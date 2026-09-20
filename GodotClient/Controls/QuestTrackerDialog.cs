using System.Collections.Generic;
using System.Linq;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>
/// 任务追踪窗口 (移植自 Client/Scenes/Views/QuestTrackerDialog.cs)。
/// 无标题/边框, 悬停半透明; 数据源 = StartInfo.Quests 中 Track=true 项。
/// QuestIcon 使用原版 QuestIcons.Zl 的分类/完成状态索引；Godot 版保留静态帧，避免把任务图标从追踪列表中省略。
/// </summary>
public partial class QuestTrackerDialog : DXWindow
{
    public bool TrackingEnabled { get; set; } = true;
    private DXVScrollBar ScrollBar;
    private DXControl TextPanel;
    private readonly List<DXLabel> Lines = new();
    private readonly List<DXAnimatedControl> _icons = new();

    public QuestTrackerDialog()
    {
        HasFooter = false;
        HasTitle = false;
        HasTopBorder = false;
        ShowCloseButton = false;
        Movable = true;
        AllowResize = true;
        Opacity = 0.0f;
        Size = new Vector2I(250, 100);

        ScrollBar = new DXVScrollBar { Change = 15 };
        TextPanel = new DXControl { PassThrough = true };

        AddControl(ScrollBar);
        AddControl(TextPanel);
        MouseEnter += (o, e) => Opacity = 0.3f;
        MouseLeave += (o, e) => Opacity = 0.0f;
        UpdateLayout();
    }

    public override void _Ready()
    {
        base._Ready();
        Resized += () => UpdateLayout();
        UpdateLayout();
    }

    private void UpdateLayout()
    {
        if (ScrollBar == null || TextPanel == null) return;

        int resizeBuffer = 4;
        ScrollBar.Size = new Vector2I(14, (int)Size.Y - resizeBuffer * 2);
        ScrollBar.Location = new Vector2I((int)(Size.X - ScrollBar.Size.X) - resizeBuffer, resizeBuffer);
        ScrollBar.VisibleSize = (int)TextPanel.Size.Y;
        ScrollBar.HideWhenNoScroll = true;

        TextPanel.Location = new Vector2I(0, resizeBuffer);
        TextPanel.Size = new Vector2I((int)(Size.X - ScrollBar.Size.X - 1) - resizeBuffer, (int)Size.Y - resizeBuffer * 2);
        ScrollBar.VisibleSize = (int)TextPanel.Size.Y;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (!IsEnabled)
        {
            if (@event is InputEventMouseButton or InputEventMouseMotion) AcceptEvent();
            return;
        }
        base._GuiInput(@event);
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed)
            ScrollBar.Value -= ScrollBar.Change;
        if (@event is InputEventMouseButton mb2 && mb2.ButtonIndex == MouseButton.WheelDown && mb2.Pressed)
            ScrollBar.Value += ScrollBar.Change;
    }

    /// <summary>任务行 (Track=true), 滚动条复位</summary>
    public void PopulateQuests(IEnumerable<ClientUserQuest> quests)
    {
        foreach (var line in Lines)
            line.QueueFree();
        Lines.Clear();
        foreach (var icon in _icons)
            icon.QueueFree();
        _icons.Clear();

        if (!TrackingEnabled || quests == null)
        {
            Visible = false;
            return;
        }

        foreach (var userQuest in quests.Where(q => q.Track))
        {
            var quest = userQuest.Quest;
            if (quest == null) continue;

            var label = new DXLabel
            {
                Text = quest.QuestName,
                DrawOutline = true,
                OutlineColour = Colors.Black,
                IsControl = false,
                Location = new Vector2I(15, Lines.Count * 15),
            };
            TextPanel.AddControl(label);
            Lines.Add(label);

            var icon = new DXAnimatedControl
            {
                LibraryFile = LibraryFile.QuestIcon,
                BaseIndex = QuestIconIndex(userQuest),
                FrameCount = 2,
                AnimationDelay = System.TimeSpan.FromSeconds(1),
                Animated = true,
                Loop = true,
                Location = new Vector2I(0, Lines.Count * 15 - 15),
                IsControl = false,
            };
            icon.SetMeta("base_y", Lines.Count * 15 - 15);
            TextPanel.AddControl(icon);
            _icons.Add(icon);

            if (userQuest.IsComplete)
            {
                label.Text += " (Complete)";
                var finish = new DXLabel
                {
                    Text = $"Goto {quest.FinishNPC?.Local()} in {quest.FinishNPC?.RegionName}",
                    TextColour = Colors.White,
                    DrawOutline = true,
                    OutlineColour = Colors.Black,
                    IsControl = false,
                    Location = new Vector2I(25, Lines.Count * 15),
                };
                TextPanel.AddControl(finish);
                Lines.Add(finish);
            }
            else
            {
                foreach (var task in quest.Tasks)
                {
                    var userTask = userQuest.Tasks.FirstOrDefault(x => x.Task == task);
                    if (userTask != null && userTask.Completed) continue;

                    var taskLabel = new DXLabel
                    {
                        Text = GameScene.Game?.GetTaskText(task, userQuest) ?? string.Empty,
                        TextColour = Colors.White,
                        DrawOutline = true,
                        OutlineColour = Colors.Black,
                        IsControl = false,
                        Location = new Vector2I(25, Lines.Count * 15),
                    };
                    TextPanel.AddControl(taskLabel);
                    Lines.Add(taskLabel);
                }
            }
        }

        Visible = Lines.Count > 0;
        ScrollBar.Value = 0;
        UpdateScrollBar();
    }

    private void UpdateScrollBar()
    {
        ScrollBar.MaxValue = Lines.Count * 15;

        for (int i = 0; i < Lines.Count; i++)
        {
            var pos = Lines[i].Location;
            Lines[i].Location = new Vector2I(pos.X, i * 15 - ScrollBar.Value);
        }
        foreach (var icon in _icons)
            icon.Position = new Vector2(0, (int)icon.GetMeta("base_y") - ScrollBar.Value);
    }

    private static int QuestIconIndex(ClientUserQuest userQuest)
    {
        if (userQuest?.Quest == null) return 2;
        int start = userQuest.Quest.QuestType switch
        {
            QuestType.Daily or QuestType.Weekly => 76,
            QuestType.Story => 56,
            QuestType.Account => 36,
            _ => 16,
        };
        return userQuest.IsComplete ? start + 2 : 2;
    }

}
