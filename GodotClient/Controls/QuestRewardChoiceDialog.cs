using System.Collections.Generic;
using Godot;
using Library.SystemModels;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>旧版任务提交时的可选奖励列表。ChoiceIndex 使用 QuestReward.Index。</summary>
public sealed partial class QuestRewardChoiceDialog : DXWindow
{
    public QuestRewardChoiceDialog()
    {
        Text = Lang.QuestRewardChoiceQuestLabel;
        Size = new Vector2I(360, 250);
    }

    public void Open(QuestInfo quest, IEnumerable<QuestReward> rewards)
    {
        foreach (var child in GetChildren())
        {
            if (child is not Node node) continue;
            if (node is DXControl control) RemoveControl(control);
            else RemoveChild(node);
            node.QueueFree();
        }

        var title = new DXLabel { Text = Lang.QuestRewardChoiceSelectLabel, FontSize = 11, Location = new Vector2I(20, 35), Size = new Vector2I(300, 25) };
        AddControl(title);
        var y = 70;
        foreach (var reward in rewards)
        {
            if (reward?.Item == null) continue;
            var choice = reward;
            var button = new DXButton
            {
                Text = $"{choice.Item.Local()} x{choice.Amount}",
                Location = new Vector2I(20, y), Size = new Vector2I(300, 28)
            };
            button.MouseClick += (s, e) =>
            {
                if (GameScene.Game?.IsObserver == true || !GameScene.CanSendQuestOperation(false, quest.Index)) return;
                GameScene.Game?.SendQuestComplete(quest.Index, choice.Index);
                WindowManager.Close(this);
            };
            AddControl(button);
            y += 32;
        }
        WindowManager.Open(this, GameScene.Game?.UILayer);
    }
}
