using System;
using System.Collections.Generic;
using Godot;
using Library;
using ZirconClient.Controls;
using ServiceLine = ZirconClient.Controls.HandheldJournalService.Line;

namespace ZirconClient.Scripts;

public partial class GameScene
{
    private void FillHandheldNpcReplies(List<ServiceLine> rows, NPCDialog owner, int revision)
    {
        foreach (var option in owner.HandheldOptions)
        {
            var selected = option;
            rows.Add(new("reply:" + selected.id, selected.label, "NPC 对话选项", Act: () =>
                TrySubmitHandheldNpcReply(owner, revision, selected.id)
                    ? "已提交，等待服务器回应。" : "正在等待回应，或对话内容已变更。"));
        }
    }

    // Reuse service handlers. A displayed capability must still be a visible,
    // enabled child of the server-opened NPC panel when the action is confirmed.
    private void FillNpcServiceLedger(List<ServiceLine> left, List<ServiceLine> right)
    {
        var owner = _npcDialog;
        int revision = owner.PageShowCount;
        bool Current() => owner == _npcDialog && JournalWindowVisible(owner)
            && owner.PageShowCount == revision && JournalInputPane() == owner
            && _net?.Connection?.Connected == true && !_observer && _player?.Dead == false;
        FillHandheldNpcReplies(left, owner, revision);
        Visit(owner);
        foreach (var cell in InventoryCells)
        {
            var item = cell?.Item;
            if (item?.Info == null || JournalSourceCell(false, cell.Slot, item) != cell) continue;
            var source = cell;
            right.Add(new($"item:{item.Index}", item.Info.Local(), "提交给当前服务（按原有条件检查）",
                MirSkin.GetTexture(DXItemCell.ItemIconLibraryFile, DXItemCell.GetItemDrawIndex(item)), () =>
                {
                    if (!Current() || !ReferenceEquals(source.Item, item) || source.Locked || source.ReadOnly)
                        return "物品或服务状态已改变。";
                    return TryRouteItemToNpc(source) ? "已选择服务物品。" : "此服务不接受这件物品。";
                }));
        }

        void Visit(Node node)
        {
            foreach (var child in node.GetChildren())
            {
                if (child is not DXControl control || !control.IsVisibleInTree() || owner.IsHandheldReplyControl(control)) continue;
                if (control is DXButton button && !string.IsNullOrWhiteSpace(button.Text))
                {
                    var selected = button;
                    left.Add(new($"control:{selected.GetInstanceId()}", selected.Text,
                        selected.IsEnabled ? "按原有服务规则执行" : "当前不可用", Act: selected.IsEnabled ? () =>
                        {
                            if (!Current() || !IsInstanceValid(selected) || !selected.IsVisibleInTree()
                                || !selected.IsEnabled || !owner.IsAncestorOf(selected)) return "服务状态已改变。";
                            selected.TriggerClick();
                            return "";
                        } : null));
                }
                else if (control is DXCheckBox checkbox)
                {
                    var selected = checkbox;
                    left.Add(new($"check:{selected.GetInstanceId()}", selected.Label.Text,
                        selected.Checked ? "已开启" : "已关闭", Act: selected.IsEnabled ? () =>
                        {
                            if (!Current() || !IsInstanceValid(selected) || !selected.IsVisibleInTree() || !selected.IsEnabled || selected.ReadOnly || !owner.IsAncestorOf(selected))
                                return "服务状态已改变。";
                            selected.Checked = !selected.Checked;
                            return "";
                        } : null));
                }
                else if (control is DXLabel label && !string.IsNullOrWhiteSpace(label.Text))
                    left.Add(new($"label:{label.GetInstanceId()}", label.Text, "服务说明"));
                Visit(control);
            }
        }
    }
}
