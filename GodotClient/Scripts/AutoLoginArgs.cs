using Godot;
using System.Collections.Generic;
using Library;

namespace ZirconClient.Scripts;

/// <summary>
/// 命令行直连测试参数（放在 `--` 之后，Godot 引擎参数之前用 --path 等）：
///   godot-mono --path GodotClient -- --user <邮箱> --pass <密码> --char <角色名>
///   godot-mono --path GodotClient -- --server 127.0.0.1 --port 7000 ...
/// 提供 --user（或 --username）即触发自动登录，不再需要 --auto-login；
/// --char 指定要进入的角色名（缺省进第一个角色）；提供 --char 时若角色不存在会报错留手动。
/// 兼容旧参数 --auto-login（固定 test@test.com / test123，无角色时自动建 TestHero）。
/// 也支持 `--user=xxx` 等号写法。
/// </summary>
public static class AutoLoginArgs
{
    private static readonly string[] Args = OS.GetCmdlineUserArgs();

    public static bool AutoLogin =>
        Has("--auto-login") || Has("--user") || Has("--username");

    public static string User =>
        GetValue("--user", "--username") ?? "test@test.com";

    public static string Password =>
        GetValue("--pass", "--password") ?? "test123";

    public static string Character =>
        GetValue("--char", "--character") ?? "";

    /// <summary>
    /// 启动命令指定的游戏服务器地址；未指定时交给 ClientSettings 处理。
    /// 支持 --server/--host 以及等号写法。
    /// </summary>
    public static string ServerAddress =>
        GetValue("--server", "--host");

    /// <summary>
    /// 启动命令指定的游戏服务器端口；未指定时交给 ClientSettings 处理。
    /// </summary>
    public static int? ServerPort
    {
        get
        {
            string raw = GetValue("--port", "--server-port");
            return int.TryParse(raw, out int port) && port is > 0 and <= 65535
                ? port
                : null;
        }
    }

    public static bool RunningTest => Has("--test-running");
    public static bool RightRunTest => Has("--test-right-run");

    /// <summary>启动指定 UI 语言：--lang ENGLISH / --lang CHINESE（Lang.Reload 优先使用）。</summary>
    public static string Language => GetValue("--lang", "--language");
    /// <summary>
    /// 只在本地执行移动预测/动画，不发送 C.Move，也不等待 S.ObjectMove。
    /// 登录和初始地图仍使用现有流程，便于直接在真实地图上检查走跑切换。
    /// </summary>
    public static bool OfflineMovementTest => Has("--offline-movement-test");
    public static bool InteractionAudit => Has("--interaction-audit");
    public static bool OperationAudit => Has("--operation-audit");
    public static bool OperationAuditExt => Has("--operation-audit-ext");
    public static bool ScreenshotAfterEnter => Has("--screenshot-after-enter");
    public static bool HexaAudit => Has("--hexa-audit");

    /// <summary>
    /// --npc-audit：全量 NPC 实机巡检。逐条读取清单，用聊天 @move 把角色传到目标
    /// NPC 面前，以真实点击路径唤起对话框、遍历可点链接并逐步截图，最后把行为写成
    /// JSONL。需要 Admin 账号（@move 是 GM 命令）。
    /// </summary>
    public static bool NpcAudit => Has("--npc-audit");

    /// <summary>--npc-audit-manifest &lt;path&gt;：巡检清单（缺省 tools/npc_audit_manifest.json，相对进程工作目录）。</summary>
    public static string NpcAuditManifest =>
        GetValue("--npc-audit-manifest") ?? "tools/npc_audit_manifest.json";

    /// <summary>--npc-audit-out &lt;dir&gt;：输出目录（results.jsonl + shots/）。</summary>
    public static string NpcAuditOut => GetValue("--npc-audit-out") ?? "/tmp/npc_audit";

    /// <summary>--npc-audit-limit N：只跑清单里的前 N 条（<=0 或缺省 = 全量）。</summary>
    public static int NpcAuditLimit
    {
        get
        {
            string raw = GetValue("--npc-audit-limit");
            return int.TryParse(raw, out int n) && n > 0 ? n : 0;
        }
    }

    /// <summary>--npc-audit-only 13,14：只跑指定 index（缺省 = 全量）。</summary>
    public static List<int> NpcAuditOnly()
    {
        var result = new List<int>();
        string raw = GetValue("--npc-audit-only");
        if (string.IsNullOrWhiteSpace(raw)) return result;
        foreach (string part in raw.Split(',', System.StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part.Trim(), out int index)) result.Add(index);
        }
        return result;
    }

    /// <summary>
    /// --npc-audit-quit=0|1：巡检结束后是否退出进程（缺省 1 = 退出，便于脚本化）。
    /// 只有显式传 0/false/no 才留在游戏里。
    /// </summary>
    public static bool NpcAuditQuit
    {
        get
        {
            string raw = GetValue("--npc-audit-quit");
            if (raw != null && raw.StartsWith("--")) raw = null;
            if (string.IsNullOrWhiteSpace(raw)) return true;
            return raw.Trim().ToLowerInvariant() switch
            {
                "0" or "false" or "no" or "off" => false,
                _ => true,
            };
        }
    }

    /// <summary>
    /// --npc-func-audit：功能性验证通道。在巡检的基础上用真实交互证明
    /// 「购买 / 收费传送 / 仓库存取（可选：卖出）」真的生效，只产出可核对的断言值，
    /// 不只看 UI 有没有弹窗。清单、NPC 坐标与 --npc-audit 共用同一份 manifest。
    /// </summary>
    public static bool NpcFuncAudit => Has("--npc-func-audit");

    /// <summary>--npc-func-audit-out &lt;dir&gt;：功能验证输出目录（缺省 /tmp/npc_func_audit）。</summary>
    public static string NpcFuncAuditOut => GetValue("--npc-func-audit-out") ?? "/tmp/npc_func_audit";

    /// <summary>
    /// --npc-func-audit-only buy,teleport,storage,sell：只跑指定用例（缺省全跑）；
    /// 未知名字会被忽略。用于迭代时只重跑失败的那一项。
    /// </summary>
    public static List<string> NpcFuncAuditOnly()
    {
        var result = new List<string>();
        string raw = GetValue("--npc-func-audit-only");
        if (string.IsNullOrWhiteSpace(raw)) return result;
        foreach (string part in raw.Split(',', System.StringSplitOptions.RemoveEmptyEntries))
        {
            string name = part.Trim().ToLowerInvariant();
            if (name.Length > 0 && !result.Contains(name)) result.Add(name);
        }
        return result;
    }

    /// <summary>
    /// --stay-select：与 --user 联用——登录成功后**停在选角屏**，
    /// 不自动进入游戏、也不对空账号自动建角。用于选角屏截图验证
    /// （洞窟槽位角色、创建面板等）。
    /// </summary>
    public static bool StayInSelect => Has("--stay-select");

    /// <summary>
    /// --legacy-slot-preview：**离线**取景开关。不连服务器、不登录，直接用合成角色
    /// 列表打开 SelectScene，用于在无服务端可用时对 EI 选角槽位做多时点截图取证
    /// （只影响渲染路径，不发送任何包、不建/删角色）。
    /// </summary>
    public static bool LegacySlotPreview => Has("--legacy-slot-preview");

    /// <summary>
    /// --legacy-slot-preview 的合成角色。默认是道士男主槽 + 道女副槽（覆盖两个槽）。
    /// `--legacy-preview-class=wizard|warrior|taoist` 可换成对应职业的男女组合 ——
    /// 法师/法师女是唯一带 **+40 元素特效**（F1080 火球 / F1385 闪电）的组合，
    /// 需要用它们验证特效层时用这个开关（合成角色不占服务端槽位）。
    /// </summary>
    public static List<SelectInfo> PreviewCharacters()
    {
        MirClass cls = ParsePreviewClass();
        if (Has("--legacy-preview-single"))
        {
            return new List<SelectInfo>
            {
                new SelectInfo { CharacterIndex = 0, CharacterName = "TestHero", Level = 255, Class = cls, Gender = Has("--legacy-preview-female") ? MirGender.Female : MirGender.Male, Location = 0 },
            };
        }
        if (Has("--legacy-preview-slot1"))
        {
            return new List<SelectInfo>
            {
                new SelectInfo { CharacterIndex = 0, CharacterName = "PreviewAlt", Level = 40, Class = cls, Gender = MirGender.Female, Location = 0 },
                new SelectInfo { CharacterIndex = 1, CharacterName = "TestHero", Level = 255, Class = cls, Gender = MirGender.Male, Location = 0 },
            };
        }
        return new List<SelectInfo>
        {
            new SelectInfo { CharacterIndex = 0, CharacterName = "TestHero", Level = 255, Class = cls, Gender = MirGender.Male, Location = 0 },
            new SelectInfo { CharacterIndex = 1, CharacterName = "PreviewAlt", Level = 40, Class = cls, Gender = MirGender.Female, Location = 0 },
        };
    }


    /// <summary>解析 `--legacy-preview-class=` 的职业值；缺省/非法都回落到道士。</summary>
    private static MirClass ParsePreviewClass()
    {
        string raw = GetValue("--legacy-preview-class");
        if (string.IsNullOrWhiteSpace(raw)) return MirClass.Taoist;
        return raw.Trim().ToLowerInvariant() switch
        {
            "wizard" or "法师" or "mage" => MirClass.Wizard,
            "warrior" or "战士" or "war" => MirClass.Warrior,
            "taoist" or "道士" or "tao" => MirClass.Taoist,
            _ => MirClass.Taoist,
        };
    }
    /// <summary>给每个 DXControl 画红色边框 + 四角方块/四边黄条 (临时布局诊断)</summary>
    public static bool UiDiagnosticBorders => Has("--ui-diagnostic-borders");
    /// <summary>
    /// 默认使用 EI 复古 UI；仅显式传入 --zircon-ui 时切换现代 Zircon UI。
    /// --legacy-ui 保留兼容，并可覆盖 --zircon-ui。
    /// </summary>
    public static bool LegacyUi => Has("--legacy-ui") || !Has("--zircon-ui");
    /// <summary>默认随 EI 复古 UI 启用旧版 HUD；--zircon-ui 可显式关闭。</summary>
    public static bool LegacyHud => Has("--legacy-hud") || LegacyUi;

    /// <summary>
    /// --window [=WxH]：强制窗口模式（覆盖 Zircon.ini 的全屏设置，直接开窗口）。
    /// 可选分辨率：--window=1600x900 或 --window 1600x900；缺省按主屏幕 75%
    /// 计算（ClientSettings.ApplyDisplaySettings 执行）。缩放由 GameScene.UiScale
    /// 按窗口高度自动适配，无需手工设置。
    /// </summary>
    public static bool Window => Has("--window");

    public static Vector2I WindowSize
    {
        get
        {
            string raw = GetValue("--window");
            // 裸 --window 时 GetValue 会吞掉下一个参数（如 --user），且等号写法
            // 缺省无值返回 null；以 "--" 开头一律视为无分辨率。
            if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("--")) return Vector2I.Zero;
            string[] parts = raw.Split('x', 'X');
            if (parts.Length == 2
                && int.TryParse(parts[0].Trim(), out int w)
                && int.TryParse(parts[1].Trim(), out int h))
                return new Vector2I(Mathf.Max(320, w), Mathf.Max(240, h));
            return Vector2I.Zero;
        }
    }

    public static bool Has(string name)
    {
        foreach (var a in Args)
        {
            if (a == name) return true;
            if (a.StartsWith(name + "=")) return true;
        }
        return false;
    }

    private static string GetValue(params string[] names)
    {
        for (int i = 0; i < Args.Length; i++)
        {
            foreach (var n in names)
            {
                if (Args[i] == n && i + 1 < Args.Length) return Args[i + 1];
                if (Args[i].StartsWith(n + "=")) return Args[i].Substring(n.Length + 1);
            }
        }
        return null;
    }
}
