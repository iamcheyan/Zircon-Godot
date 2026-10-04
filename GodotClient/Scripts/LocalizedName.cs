using System.Collections.Generic;
using System.Text.Json;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Translations;

namespace ZirconClient.Scripts;

/// <summary>
/// 数据层本土化显示名映射表（方案 B：不动数据库，客户端查表显示）。
/// 对齐原版架构建议：世界数据（物品/怪物/NPC/魔法/地图名）保持英文原名，
/// 翻译放客户端显示层，按当前语言查表替换，查不到回退英文。
///
/// 数据源: GodotClient/translations/db_names.json（由翻译工具生成，可热更新）。
/// </summary>
public static class LocalizedName
{
    private static Dictionary<string, Dictionary<string, string>> _items;
    private static Dictionary<string, Dictionary<string, string>> _monsters;
    private static Dictionary<string, Dictionary<string, string>> _npcs;
    private static Dictionary<string, Dictionary<string, string>> _magics;
    private static Dictionary<string, Dictionary<string, string>> _maps;
    private static bool _loaded;

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            var text = FileAccess.GetFileAsString("res://translations/db_names.json");
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            _items = ParseSection(root, "items");
            _monsters = ParseSection(root, "monsters");
            _npcs = ParseSection(root, "npcs");
            _magics = ParseSection(root, "magics");
            _maps = ParseSection(root, "maps");
            GD.Print($"[Localize] 名称映射表已加载: 物品{_items.Count} 怪物{_monsters.Count} NPC{_npcs.Count} 魔法{_magics.Count} 地图{_maps.Count}");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[Localize] 名称映射表加载失败: {ex.Message}");
            _items = _monsters = _npcs = _magics = _maps = new Dictionary<string, Dictionary<string, string>>();
        }
    }

    private static Dictionary<string, Dictionary<string, string>> ParseSection(JsonElement root, string section)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(System.StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty(section, out var sec)) return result;
        foreach (var prop in sec.EnumerateObject())
        {
            var entry = new Dictionary<string, string>();
            if (prop.Value.TryGetProperty("zh", out var zh)) entry["zh"] = zh.GetString() ?? "";
            if (prop.Value.TryGetProperty("ja", out var ja)) entry["ja"] = ja.GetString() ?? "";
            result[prop.Name] = entry;

            string compact = prop.Name.Replace(" ", "");
            if (!result.ContainsKey(compact))
            {
                result[compact] = entry;
            }
        }
        return result;
    }

    /// <summary>当前语言码（EN/CN/JA）。</summary>
    private static string LangCode =>
        Lang.Current switch
        {
            EnglishMessages => "EN",
            JapaneseMessages => "JA",
            _ => "CN",
        };

    private static string Lookup(Dictionary<string, Dictionary<string, string>> table, string name, string fallback)
    {
        if (string.IsNullOrEmpty(name)) return fallback ?? "";
        if (table != null)
        {
            if (table.TryGetValue(name, out var entry) ||
                table.TryGetValue(name.Replace(" ", ""), out entry))
            {
                string lang = LangCode;
                if (entry.TryGetValue("zh", out var zh) && !string.IsNullOrWhiteSpace(zh) && lang == "CN") return zh;
                if (entry.TryGetValue("ja", out var ja) && !string.IsNullOrWhiteSpace(ja) && lang == "JA") return ja;
            }
        }
        return fallback ?? name;
    }

    public static string Local(this ItemInfo info) { EnsureLoaded(); return Lookup(_items, info?.ItemName, info?.ItemName); }
    public static string Local(this MonsterInfo info) { EnsureLoaded(); return Lookup(_monsters, info?.MonsterName, info?.MonsterName); }
    public static string Local(this NPCInfo info) { EnsureLoaded(); return Lookup(_npcs, info?.NPCName, info?.NPCName); }
    public static string Local(this MagicInfo info) { EnsureLoaded(); return Lookup(_magics, info?.Name, info?.Name); }
    public static string Local(this MapInfo info) { EnsureLoaded(); return Lookup(_maps, info?.Description, info?.Description); }

    /// <summary>职业中文名（Warrior=战士 等）。</summary>
    public static string Local(this MirClass cls) =>
        cls switch
        {
            MirClass.Warrior => "战士",
            MirClass.Wizard => "法师",
            MirClass.Taoist => "道士",
            MirClass.Assassin => "刺客",
            _ => cls.ToString(),
        };

    /// <summary>装备需求职业中文名（RequiredClass 位标志，可组合）。</summary>
    public static string Local(this RequiredClass req)
    {
        if (req == RequiredClass.None) return "无";
        var parts = new List<string>();
        if ((req & RequiredClass.Warrior) != 0) parts.Add("战士");
        if ((req & RequiredClass.Wizard) != 0) parts.Add("法师");
        if ((req & RequiredClass.Taoist) != 0) parts.Add("道士");
        if ((req & RequiredClass.Assassin) != 0) parts.Add("刺客");
        return parts.Count > 0 ? string.Join("/", parts) : req.ToString();
    }

    /// <summary>性别中文名。</summary>
    public static string Local(this MirGender gender) =>
        gender switch
        {
            MirGender.Male => "男",
            MirGender.Female => "女",
            _ => gender.ToString(),
        };
}
