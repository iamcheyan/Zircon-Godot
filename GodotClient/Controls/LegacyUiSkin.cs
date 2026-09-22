using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using Library;

namespace ZirconClient.Controls;

/// <summary>
/// 旧版 UI 视觉基准的运行时读取器。
/// 只提供布局/资源/视觉参数，不接管窗口事件和业务逻辑。
/// </summary>
public static class LegacyUiSkin
{
    public readonly record struct WindowProfile(
        string Id, string Name, Vector2I Location, Vector2I Size,
        bool Movable, LibraryFile BackgroundLibrary, int BackgroundFrame,
        bool Candidate);

    private static readonly Dictionary<string, WindowProfile> Profiles = new(StringComparer.Ordinal);
    private static bool _loaded;

    public static readonly Color WindowFill = new(0.063f, 0.031f, 0.031f, 0.98f);
    public static readonly Color TitleText = new(1f, 0.95f, 0.7f, 1f);
    public static readonly Color LegacyGold = new(0.72f, 0.57f, 0.20f, 1f);

    public static IReadOnlyDictionary<string, WindowProfile> Windows
    {
        get { EnsureLoaded(); return Profiles; }
    }

    public static bool TryGetWindow(string name, out WindowProfile profile)
    {
        EnsureLoaded();
        return Profiles.TryGetValue(name, out profile);
    }

    /// <summary>旧版 800x600 逻辑坐标转换为当前 Godot 1024x768 逻辑坐标。</summary>
    public static Vector2I ToGodotLocation(Vector2I legacy) => new(
        Mathf.RoundToInt(legacy.X * 1024f / 800f),
        Mathf.RoundToInt(legacy.Y * 768f / 600f));

    public static Vector2I ToGodotSize(Vector2I legacy) => new(
        Mathf.RoundToInt(legacy.X * 1024f / 800f),
        Mathf.RoundToInt(legacy.Y * 768f / 600f));

    /// <summary>按旧版 profile 应用公共窗口属性；geometry 由迁移阶段显式开启。</summary>
    public static bool ApplyWindowProfile(DXWindow window, bool geometry = false)
    {
        if (window == null || !TryGetWindow(window.GetType().Name, out WindowProfile profile)) return false;
        ApplyVisualDefaults(window);
        if (!geometry || profile.Candidate) return !profile.Candidate;

        window.Size = ToGodotSize(profile.Size);
        window.Location = ToGodotLocation(profile.Location);
        window.Movable = profile.Movable;
        window.UpdateClientAreaForLegacySkin();
        return true;
    }

    /// <summary>
    /// 测试场专用：把窗口的第一张背景图切换到旧版 GameInter 资源。
    /// 正式窗口仍由各自的业务布局控制；这个入口用于验证旧版素材是否能
    /// 在独立 HUD 实验场中正确显示，避免误用当前版 Interface 黑底。
    /// </summary>
    public static bool ApplyLegacyTestWindow(DXWindow window, Vector2I location)
    {
        if (window == null) return false;

        (int frame, Vector2I size, Vector2I imageOrigin, bool stretch) profile = window.GetType().Name switch
        {
            // WIL 帧含透明画布。人物/背包/技能必须保持有效像素原始比例，
            // 用 alpha bbox 原点裁掉透明边距，不能把整张 512 画布缩进窗口。
            "InventoryDialog" => (250, new Vector2I(284, 324), new Vector2I(114, 94), false),
            "CharacterDialog" => (200, new Vector2I(244, 328), new Vector2I(6, 92), false),
            // wrapper 0x439250 原始参数明确给出 452x380；F400 bbox 451x378。
            "MagicDialog" => (400, new Vector2I(452, 380), new Vector2I(30, 67), false),
            "GroupDialog" => (900, new Vector2I(256, 244), new Vector2I(0, 6), false),
            "QuestDialog" => (700, new Vector2I(340, 440), new Vector2I(86, 36), false),
            "CommunicationDialog" => (350, new Vector2I(572, 388), new Vector2I(226, 62), false),
            "MenuDialog" => (750, new Vector2I(248, 264), new Vector2I(4, 119), false),
            _ => (-1, Vector2I.Zero, Vector2I.Zero, false),
        };
        if (profile.frame < 0) return false;

        window.Location = location;
        window.Size = profile.size;
        window.Clip = true;
        window.DrawChrome = false;
        window.DropShadow = false;
        window.HasTitle = false;
        window.ShowCloseButton = false;
        switch (window)
        {
            case InventoryDialog inventory:
                inventory.ApplyLegacyEiLayout();
                return true;
            case CharacterDialog character:
                character.ApplyLegacyEiLayout();
                return true;
            case MagicDialog magic:
                magic.ApplyLegacyEiLayout();
                return true;
            case GroupDialog group:
                group.ApplyLegacyEiLayout();
                return true;
            case QuestDialog quest:
                quest.ApplyLegacyEiLayout();
                return true;
            case MenuDialog menu:
                menu.ApplyLegacyEiLayout();
                return true;
            case CommunicationDialog communication:
                communication.ApplyLegacyEiLayout();
                return true;
            case BeltDialog belt:
                belt.ApplyLegacyEiLayout();
                return true;
        }
        foreach (var child in window.Controls)
        {
            if (child is not DXImageControl image) continue;
            image.LibraryFile = LibraryFile.GameInter;
            image.Index = profile.frame;
            image.FixedSize = true;
            image.StretchImage = profile.stretch;
            image.Location = -profile.imageOrigin;
            image.Size = profile.stretch
                ? profile.size
                : MirSkin.GetSize(LibraryFile.GameInter, profile.frame);
            image.MouseFilter = Control.MouseFilterEnum.Ignore;
            window.UpdateClientAreaForLegacySkin();
            return true;
        }
        return false;
    }

    public static Color GetControlText(bool enabled = true) => enabled ? TitleText : new Color(.45f, .4f, .3f, 1f);

    /// <summary>
    /// 应用公共旧版视觉约定。窗口具体尺寸不在这里强制覆盖，避免破坏现有
    /// 窗口的动态布局；第四阶段迁移窗口时再按 profile 显式采用尺寸。
    /// </summary>
    public static void ApplyVisualDefaults(DXWindow window)
    {
        if (window == null) return;
        window.DropShadow = true;
        if (window.TitleLabel != null)
            window.TitleLabel.TextColour = TitleText;
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        string path = ProjectSettings.GlobalizePath("res://UI/legacy_ui.json");
        if (!File.Exists(path)) return;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("windows", out JsonElement windows)) return;
            foreach (JsonElement item in windows.EnumerateArray())
            {
                string name = item.GetProperty("name").GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name)) continue;
                int[] location = ReadPair(item, "location");
                int[] size = ReadPair(item, "size");
                JsonElement background = item.GetProperty("background");
                string library = background.GetProperty("library").GetString() ?? "GameInter";
                if (!Enum.TryParse(library, true, out LibraryFile libraryFile)) continue;
                Profiles[name] = new WindowProfile(
                    item.GetProperty("id").GetString() ?? name,
                    name,
                    new Vector2I(location[0], location[1]),
                    new Vector2I(size[0], size[1]),
                    item.GetProperty("movable").GetBoolean(),
                    libraryFile,
                    background.GetProperty("frame").GetInt32(),
                    string.Equals(item.GetProperty("status").GetString(), "candidate", StringComparison.OrdinalIgnoreCase));
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LegacyUiSkin] legacy_ui.json 加载失败: {ex.Message}");
            Profiles.Clear();
        }
    }

    private static int[] ReadPair(JsonElement parent, string property)
    {
        JsonElement pair = parent.GetProperty(property);
        return new[] { pair[0].GetInt32(), pair[1].GetInt32() };
    }
}
