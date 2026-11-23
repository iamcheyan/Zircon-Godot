using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ZirconClient.Scripts;

public partial class GameScene
{
    internal void JournalLoreEntries(int tab, int cat, List<JournalEntry> rows)
    {
        if (tab == 2) JournalSkillEntries(cat, rows);
        else if (tab == 3) JournalQuestEntries(cat, rows);
    }

    internal IReadOnlyList<JournalAction> JournalLoreActions(int tab, int cat, string id, bool secondary) => tab switch
    {
        2 => JournalSkillActions(cat, id, secondary),
        3 => JournalQuestActions(cat, id, secondary),
        _ => Array.Empty<JournalAction>(),
    };

    // Per-character device preference. No changes to the learned magic list or shared F keys.
    private readonly HashSet<int> _journalFavorites = new();
    private int? _journalFavoriteCharacter;
    private string _journalFavoriteFile;
    private Error _journalFavoriteLoadError;

    private bool LoadJournalFavorites()
    {
        if (_player == null) return false;
        if (_journalFavoriteCharacter != _player.CharacterIndex)
        {
            _journalFavoriteCharacter = _player.CharacterIndex;
            var host = AutoLoginArgs.ServerAddress ?? (ClientSettings.UseNetworkConfig ? ClientSettings.IPAddress : "127.0.0.1");
            int port = AutoLoginArgs.ServerPort ?? (ClientSettings.UseNetworkConfig ? ClientSettings.Port : 7000);
            _journalFavoriteFile = $"user://handheld-skills-{host.Md5Text()}-{port}-{_player.CharacterIndex}.cfg";
            _journalFavorites.Clear();
            using var file = new ConfigFile();
            _journalFavoriteLoadError = file.Load(_journalFavoriteFile);
            if (_journalFavoriteLoadError == Error.Ok && file.HasSectionKey("skills", "favorites"))
                _journalFavorites.UnionWith(file.GetValue("skills", "favorites").AsInt32Array());
        }
        return _journalFavoriteLoadError is Error.Ok or Error.FileNotFound;
    }

    internal bool JournalFavorite(int skillIndex) => LoadJournalFavorites() && _journalFavorites.Contains(skillIndex);

    private string JournalToggleFavorite(int skillIndex)
    {
        if (!LoadJournalFavorites()) return "无法读取本地技能收藏";
        bool added = _journalFavorites.Add(skillIndex);
        if (!added) _journalFavorites.Remove(skillIndex);
        using var file = new ConfigFile();
        file.SetValue("skills", "favorites", _journalFavorites.ToArray());
        if (file.Save(_journalFavoriteFile) == Error.Ok)
            return added ? "已收藏，技能快选时优先显示" : "已取消收藏";
        if (added) _journalFavorites.Remove(skillIndex);
        else _journalFavorites.Add(skillIndex);
        return "未能保存本地技能收藏";
    }
}
