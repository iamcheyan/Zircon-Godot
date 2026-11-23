using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Library;
using Library.SystemModels;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

public partial class GameScene
{
    private void JournalQuestEntries(int cat, List<JournalEntry> rows)
    {
        foreach (var quest in _userQuests.Values.Where(q => JournalQuestMatches(cat, q))
                     .OrderBy(q => q.Completed).ThenBy(q => q.Quest.QuestType)
                     .ThenBy(q => q.Quest.QuestName).ThenBy(q => q.Index))
        {
            var info = quest.Quest;
            bool ready = !quest.Completed && quest.IsComplete;
            var map = JournalQuestMap(quest);
            string badge = quest.Completed ? "已完成" : ready ? "待交付" : "进行中";
            if (!quest.Completed && quest.Track) badge += " · 追踪中";

            string detail = $"{info.QuestType} · {map?.Local() ?? Lang.QuestUnknownLabel}\n\n"
                + GetQuestText(info, quest) + "\n\n" + GetTaskText(info, quest);
            if (ready && info.FinishNPC != null)
                detail += $"\n\n请与 {info.FinishNPC.Local()} 对话交付任务。";

            rows.Add(new JournalEntry(quest.Index.ToString(CultureInfo.InvariantCulture), info.QuestName, detail, badge));
        }
    }

    private IReadOnlyList<JournalAction> JournalQuestActions(int cat, string id, bool secondary)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            return Array.Empty<JournalAction>();

        var quest = GetUserQuest(index);
        if (!JournalQuestMatches(cat, quest)) return Array.Empty<JournalAction>();

        if (secondary)
        {
            return new[] { new JournalAction("查看地图", () =>
            {
                var current = GetUserQuest(index);
                if (!JournalQuestMatches(cat, current)) return "任务记录已变更。";
                var map = JournalQuestMap(current);
                if (_bigMap == null || _mapView == null || map == null
                    || MirSkin.GetTexture(LibraryFile.MiniMap, map.MiniMap) == null)
                    return "此任务没有可查看的地图。";

                OpenBigMapForMap(map);
                SyncJournalVisibility();
                return string.Empty;
            }) };
        }

        if (quest.Completed)
            return new[] { new JournalAction("任务状态", () => "已完成任务仅供查看。") };

        return new[] { new JournalAction(quest.Track ? "取消追踪" : "追踪任务", () =>
        {
            var current = GetUserQuest(index);
            if (!JournalQuestMatches(cat, current) || current.Completed) return "此任务已无法追踪。";
            if (!CanSendQuestOperation(IsObserver, index)) return "当前无法追踪任务。";
            if (_net?.Connection?.Connected != true) return "未连接服务器。";

            // The server stores tracking without a reply; mirror the legacy client's local update.
            bool track = !current.Track;
            SendQuestTrack(index, track);
            current.Track = track;
            if (track && !QuestTrackerVisible) SetQuestTrackerVisible(true);
            else _questTracker?.PopulateQuests(_userQuests.Values);
            return track ? "已追踪任务。" : "已取消追踪。";
        }) };
    }

    internal Godot.Texture2D JournalQuestPreview(string id)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out int index)) return null;
        var quest = GetUserQuest(index);
        var map = quest?.Quest == null ? null : JournalQuestMap(quest);
        return map == null ? null : MirSkin.GetTexture(LibraryFile.MiniMap, map.MiniMap);
    }

    private static bool JournalQuestMatches(int cat, ClientUserQuest quest)
        => quest?.Quest != null && (cat == 0 || (quest.Completed ? cat == 2 : cat == 1));

    private static MapInfo JournalQuestMap(ClientUserQuest quest)
    {
        var info = quest.Quest;
        var startMap = info.StartNPC?.Region?.Map;
        var finishMap = info.FinishNPC?.Region?.Map;
        if (quest.Completed || quest.IsComplete) return finishMap ?? startMap;

        foreach (var task in info.Tasks)
        {
            if (task == null || quest.Tasks.Any(progress => progress?.TaskIndex == task.Index && progress.Amount >= task.Amount))
                continue;

            if (task.RegionParameter?.Map is MapInfo regionMap) return regionMap;
            if (task.MonsterDetails == null) continue;
            foreach (var monster in task.MonsterDetails)
                if (monster?.Map is MapInfo monsterMap) return monsterMap;
        }

        return startMap ?? finishMap;
    }
}
