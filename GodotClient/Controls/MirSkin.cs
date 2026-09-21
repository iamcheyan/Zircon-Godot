using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Library;
using ZirconClient.Formats;

namespace ZirconClient.Controls;

/// <summary>
/// 皮肤基础设施: .Zl 图库缓存 + 贴图缓存 + 中文字体。
/// 控件库统一从这里取图, 避免每个控件自己开文件流。
/// </summary>
public static class MirSkin
{
    /// <summary>客户端数据目录。原硬编码为 /home/tetsuya/development/Zircon/...（大写），
    /// 实际检出目录是小写 zircon；Linux 大小写敏感导致 UI 图库加载静默失败、
    /// 背景贴图全部缺失。复用 LibraryCache 的动态解析（相对 res:// 探测），
    /// 与地图/角色/快捷栏图库的加载路径保持一致。</summary>
    public static string DataPath = ResolveDataPath();

    private static string ResolveDataPath()
    {
        try
        {
            string projectDir = ProjectSettings.GlobalizePath("res://");
            string resolved = Path.GetFullPath(Path.Combine(projectDir, "..", "Debug", "Client", "Data"));
            if (Directory.Exists(resolved)) return resolved + Path.DirectorySeparatorChar;
        }
        catch
        {
            // 回退到候选列表
        }
        string[] candidates =
        {
            "/home/tetsuya/mir3ei/Data/",
            "/home/tetsuya/development/Zircon/Debug/Client/Data/",
            "/home/tetsuya/development/zircon/Debug/Client/Data/",
        };
        foreach (string candidate in candidates)
        {
            if (Directory.Exists(candidate)) return candidate;
        }
        // 兜底：取第一个候选（保持原行为，便于报错定位）
        return candidates[0];
    }

    private static readonly Dictionary<LibraryFile, ZlLibrary> _libraries = new();
    private static readonly Dictionary<(LibraryFile, int), Texture2D> _textures = new();
    private static readonly Dictionary<(LibraryFile, int), Texture2D> _overlayTextures = new();

    private static FontFile _font;
    private static readonly List<Font> _fontFallbacks = new();
    private static bool _fontFailed;
    private static float _uiScale = 1f;

    /// <summary>界面缩放倍率；像素字体用反向倍率保持屏幕上的物理字号不变。</summary>
    public static void SetUiScale(float scale) => _uiScale = Mathf.Max(1f, scale);

    public static ZlLibrary GetLibrary(LibraryFile file)
    {
        if (_libraries.TryGetValue(file, out var lib)) return lib;
        if (!Libraries.LibraryList.TryGetValue(file, out string path)) return null;

        // LibraryList 路径是 Windows 格式 "Data\xxx.Zl": 先转正斜杠再剥 Data/ 前缀
        string p = path.Replace('\\', '/');
        if (p.StartsWith("Data/")) p = p.Substring(5);
        string full = Path.Combine(DataPath, p);
        full = ResolvePath(full);
        if (!File.Exists(full)) return null;

        lib = new ZlLibrary(full);
        _libraries[file] = lib;
        return lib;
    }

    private static string ResolvePath(string fullPath)
    {
        if (File.Exists(fullPath)) return fullPath;
        string dir = Path.GetDirectoryName(fullPath);
        string filename = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return fullPath;
        foreach (string file in Directory.EnumerateFiles(dir))
        {
            if (string.Equals(Path.GetFileName(file), filename, StringComparison.OrdinalIgnoreCase))
                return file;
        }
        return fullPath;
    }

    /// <summary>取图库第 index 帧的 Texture2D; 缺图返回 null (控件应静默跳过)</summary>
    public static Texture2D GetTexture(LibraryFile file, int index)
    {
        if (index < 0) return null;
        var key = (file, index);
        if (_textures.TryGetValue(key, out var tex) && tex != null) return tex;

        var lib = GetLibrary(file);
        if (lib == null || index >= lib.Images.Length) return null;

        tex = lib.GetImageTexture(index);
        if (tex == null) return null;
        _textures[key] = tex;
        return tex;
    }

    public static Texture2D GetOverlayTexture(LibraryFile file, int index)
    {
        if (index < 0) return null;
        var key = (file, index);
        if (_overlayTextures.TryGetValue(key, out var tex) && tex != null) return tex;

        var lib = GetLibrary(file);
        if (lib == null || index >= lib.Images.Length) return null;
        tex = lib.GetOverlayTexture(index);
        if (tex == null) return null;
        _overlayTextures[key] = tex;
        return tex;
    }

    public static Vector2I GetSize(LibraryFile file, int index)
    {
        if (index < 0) return Vector2I.Zero;
        var lib = GetLibrary(file);
        if (lib == null || index >= lib.Images.Length || lib.Images[index] == null) return Vector2I.Zero;
        return new Vector2I(lib.Images[index].Width, lib.Images[index].Height);
    }

    public static Vector2I GetOffset(LibraryFile file, int index)
    {
        if (index < 0) return Vector2I.Zero;
        var lib = GetLibrary(file);
        if (lib == null || index >= lib.Images.Length || lib.Images[index] == null) return Vector2I.Zero;
        return new Vector2I(lib.Images[index].OffSetX, lib.Images[index].OffSetY);
    }

    /// <summary>中文字体 (Noto Sans CJK)。优先用客户端自带 Fonts/, 其次系统路径与
    /// ~/.local/share/fonts。Godot 默认字体不含 CJK, UI 文字必须用它。</summary>
    public static FontFile GetFont()
    {
        if (_font != null) return _font;
        if (_fontFailed) return null;
        var candidates = new List<string>();
        string projectDir = string.Empty;
        try { projectDir = ProjectSettings.GlobalizePath("res://"); }
        catch { }
        if (!string.IsNullOrEmpty(projectDir))
        {
            // Fusion Pixel 的中文和拉丁字符分包；中文主字体保持 12px 点阵，
            // 拉丁字体作为 fallback，避免数字/英文又回退到平滑的系统字体。
            candidates.Add(Path.Combine(projectDir, "Fonts/third_party/fusion-pixel/fusion-pixel-12px-proportional-zh_hans.otf"));
        }
        // 客户端自带字体优先 (GodotClient/Fonts/NotoSansCJK*, 随仓库分发): 不依赖
        // 系统环境, nixos-rebuild / 换机 / 无中文字体的系统都不受影响。
        // Fonts/ 带 .gdignore — 不让 Godot 编辑器扫描生成 .import 噪音。
        try
        {
            candidates.AddRange(Directory.EnumerateFiles(
                Path.Combine(projectDir, "Fonts"), "NotoSansCJK*"));
        }
        catch (Exception) { /* 目录不存在或不可读 */ }
        candidates.AddRange(new[]
        {
            "/usr/share/fonts/google-noto-sans-cjk-vf-fonts/NotoSansCJK-VF.ttc",
            "/usr/share/fonts/noto-cjk/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
        });
        // NixOS: 系统字体在 /nix/store（路径每次 rebuild 变化, 不能硬编码）;
        // 用户字体目录 ~/.local/share/fonts 跨世代稳定, 是 fontconfig 同源的事实位置。
        string home = System.Environment.GetEnvironmentVariable("HOME") ?? "";
        if (home.Length > 0)
        {
            try
            {
                candidates.AddRange(Directory.EnumerateFiles(
                    Path.Combine(home, ".local/share/fonts"), "NotoSansCJK*"));
            }
            catch (Exception) { /* 目录不存在或不可读 */ }
        }

        foreach (string path in candidates)
        {
            if (!File.Exists(path)) continue;
            var font = new FontFile();
            if (font.LoadDynamicFont(path) == Error.Ok)
            {
                ConfigurePixelFont(font);
                _font = font;
                LoadFusionLatinFallback(projectDir);
                if (_fontFallbacks.Count > 0)
                    _font.Fallbacks = new Godot.Collections.Array<Font>(_fontFallbacks);
                GD.Print($"[MirSkin] 中文字体加载: {path}");
                return font;
            }
            font.Dispose();
        }

        _fontFailed = true;
        GD.PrintErr("[MirSkin] 找不到中文字体 (Noto Sans CJK), UI 中文将无法显示");
        return null;
    }

    private static void LoadFusionLatinFallback(string projectDir)
    {
        if (string.IsNullOrEmpty(projectDir)) return;
        string path = Path.Combine(projectDir, "Fonts/third_party/fusion-pixel/fusion-pixel-12px-proportional-latin.otf");
        if (!File.Exists(path)) return;
        var latin = new FontFile();
        if (latin.LoadDynamicFont(path) == Error.Ok)
        {
            ConfigurePixelFont(latin);
            _fontFallbacks.Add(latin);
        }
        else
            latin.Dispose();
    }

    private static void ConfigurePixelFont(FontFile font)
    {
        // Fusion Pixel 是按固定像素网格设计的；Godot 默认的灰度抗锯齿和
        // 亚像素定位会给笔画加入半透明边缘，放在老游戏 UI 上会显得发糊。
        font.Antialiasing = TextServer.FontAntialiasing.None;
        font.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
    }

    /// <summary>
    /// 像素字体只使用稳定的字号档位。原版字号很多是 8/9/10/11/13，
    /// 若继续乘 4/3 会得到 11/12/13/15/17，Fusion Pixel 的 12px 字形
    /// 就会被重新栅格化，出现灰边和模糊。当前把常规 UI 统一落到 12px，
    /// 大标题使用 16px；文字测量和实际绘制共用此映射。
    /// </summary>
    public static int ScaledSize(int size)
    {
        int physicalSize = size <= 13 ? 12 : size <= 18 ? 16 : 24;
        return Mathf.Max(1, Mathf.RoundToInt(physicalSize / _uiScale));
    }

    public static int ScaledSize(float size) => ScaledSize(Mathf.RoundToInt(size));


    public static Vector2 MeasureText(string text, int fontSize)
    {
        var font = GetFont();
        if (font == null || string.IsNullOrEmpty(text)) return Vector2.Zero;
        return font.GetStringSize(text, HorizontalAlignment.Left, -1, ScaledSize(fontSize));
    }

    /// <summary>进程退出前调用: 释放所有静态资源, 消除 Godot 退出时的 RID 泄漏警告。
    /// 纹理对象的所有权在 ZlLibrary 的缓存里 (GetPartTexture 写入), 此处不重复
    /// Dispose 纹理, 只清引用并释放图库 (图库 Dispose 会释放自己的纹理缓存)。</summary>
    public static void DisposeAll()
    {
        foreach (var lib in _libraries.Values)
            lib?.Dispose();
        _libraries.Clear();

        _textures.Clear();
        _overlayTextures.Clear();

        _font?.Dispose();
        _font = null;
        foreach (var fallback in _fontFallbacks) fallback.Dispose();
        _fontFallbacks.Clear();
        _fontFailed = false;
    }
}
