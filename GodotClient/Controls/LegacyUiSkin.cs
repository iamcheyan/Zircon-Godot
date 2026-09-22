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
