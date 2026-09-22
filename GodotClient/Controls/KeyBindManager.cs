using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ZirconClient.Controls;

/// <summary>
/// 键位表 (移植自 Client/Envir/CEnvir.cs 默认键位 + GetKeyBindLabel)。
/// M12 子集: 窗口开关 N/H/O/Q/W/E/V/B/L + 功能 Ctrl+H/Ctrl+A + 方向键。
/// 支持 Control/Alt/Shift 修饰与 Key1/Key2 双键。
/// </summary>
public enum KeyBindAction
{
    None,
    MenuWindow,        // N
    HelpWindow,        // H
    ConfigWindow,      // O
    CharacterWindow,   // Q
    InventoryWindow,   // W
    MagicWindow,       // E
    MagicBarWindow,    // X
    DungeonFinderWindow,
    StorageWindow,     // S
    BeltWindow,        // Z
    AutoPotionWindow,  // Ctrl+P
    CurrencyWindow,    // Ctrl+C
    FilterDropWindow,  // Ctrl+F
    FortuneWindow,     // Ctrl+R
    ItemPickUp,        // Tab
    QuestTrackerWindow,// L
    MapMiniWindow,     // V
    MapBigWindow,      // B
    RankingWindow,      // R
    GameStoreWindow,    // Y
    CompanionWindow,    // U
    GroupWindow,        // P
    GuildWindow,        // G
    MailBoxWindow,      // ,
    MailSendWindow,     // .
    BlockListWindow,
    QuestLogWindow,
    ChatOptionsWindow,  // Ctrl+O
    ExitGameWindow,     // Esc
    GroupAllowSwitch,
    GroupTarget,
    TradeRequest,
    TradeAllowSwitch,
    PartnerTeleport,
    MountToggle,
    AutoRunToggle,
    ChangeChatMode,
    ToggleItemLock,
    UseBelt01, UseBelt02, UseBelt03, UseBelt04, UseBelt05,
    UseBelt06, UseBelt07, UseBelt08, UseBelt09, UseBelt10,
    ChangeAttackMode,  // Ctrl+H
    ChangePetMode,     // Ctrl+A
    SpellSet01,
    SpellSet02,
    SpellSet03,
    SpellSet04,
    SpellUse01,
    SpellUse02,
    SpellUse03,
    SpellUse04,
    SpellUse05,
    SpellUse06,
    SpellUse07,
    SpellUse08,
    SpellUse09,
    SpellUse10,
    SpellUse11,
    SpellUse12,
    SpellUse13,
    SpellUse14,
    SpellUse15,
    SpellUse16,
    SpellUse17,
    SpellUse18,
    SpellUse19,
    SpellUse20,
    SpellUse21,
    SpellUse22,
    SpellUse23,
    SpellUse24,
}

public class KeyBindInfo
{
    public KeyBindAction Action;
    public Key Key1, Key2 = Key.None;
    public bool Control1, Alt1, Shift1;
    public bool Control2, Alt2, Shift2;

    public KeyBindInfo(KeyBindAction action, Key key1, bool control1 = false, bool alt1 = false, bool shift1 = false)
    {
        Action = action;
        Key1 = key1;
        Control1 = control1;
        Alt1 = alt1;
        Shift1 = shift1;
    }
}

public static class KeyBindManager
{
    public static readonly List<KeyBindInfo> KeyBinds = new()
    {
        new KeyBindInfo(KeyBindAction.MenuWindow, Key.N),
        new KeyBindInfo(KeyBindAction.HelpWindow, Key.H),
        new KeyBindInfo(KeyBindAction.ConfigWindow, Key.O),
        new KeyBindInfo(KeyBindAction.CharacterWindow, Key.Q),
        new KeyBindInfo(KeyBindAction.InventoryWindow, Key.W),
        new KeyBindInfo(KeyBindAction.MagicWindow, Key.E),
        new KeyBindInfo(KeyBindAction.MagicBarWindow, Key.E, control1: true),
        new KeyBindInfo(KeyBindAction.StorageWindow, Key.S),
        new KeyBindInfo(KeyBindAction.BeltWindow, Key.Z),
        new KeyBindInfo(KeyBindAction.AutoPotionWindow, Key.A),
        new KeyBindInfo(KeyBindAction.CurrencyWindow, Key.C, control1: true),
        new KeyBindInfo(KeyBindAction.FilterDropWindow, Key.F, control1: true),
        new KeyBindInfo(KeyBindAction.FortuneWindow, Key.W, control1: true),
        new KeyBindInfo(KeyBindAction.ItemPickUp, Key.Tab),
        new KeyBindInfo(KeyBindAction.QuestTrackerWindow, Key.L),
        new KeyBindInfo(KeyBindAction.MapMiniWindow, Key.V),
        new KeyBindInfo(KeyBindAction.MapBigWindow, Key.B),
        new KeyBindInfo(KeyBindAction.RankingWindow, Key.R),
        new KeyBindInfo(KeyBindAction.GroupWindow, Key.P),
        new KeyBindInfo(KeyBindAction.GuildWindow, Key.G),
        new KeyBindInfo(KeyBindAction.MailBoxWindow, Key.Comma),
        new KeyBindInfo(KeyBindAction.MailSendWindow, Key.Period),
        new KeyBindInfo(KeyBindAction.BlockListWindow, Key.F),
        new KeyBindInfo(KeyBindAction.QuestLogWindow, Key.J),
        new KeyBindInfo(KeyBindAction.ChatOptionsWindow, Key.O, control1: true),
        new KeyBindInfo(KeyBindAction.ExitGameWindow, Key.Q, alt1: true) { Key2 = Key.X, Alt2 = true },
        new KeyBindInfo(KeyBindAction.GroupAllowSwitch, Key.P, alt1: true),
        new KeyBindInfo(KeyBindAction.GroupTarget, Key.G, control1: true),
        new KeyBindInfo(KeyBindAction.TradeRequest, Key.T),
        new KeyBindInfo(KeyBindAction.TradeAllowSwitch, Key.T, control1: true),
        new KeyBindInfo(KeyBindAction.MountToggle, Key.M),
        new KeyBindInfo(KeyBindAction.AutoRunToggle, Key.D),
        new KeyBindInfo(KeyBindAction.ChangeChatMode, Key.K),
        new KeyBindInfo(KeyBindAction.PartnerTeleport, Key.Z, shift1: true),
        new KeyBindInfo(KeyBindAction.UseBelt01, Key.Key1) { Key2 = Key.Key1, Shift2 = true },
        new KeyBindInfo(KeyBindAction.UseBelt02, Key.Key2) { Key2 = Key.Key2, Shift2 = true },
        new KeyBindInfo(KeyBindAction.UseBelt03, Key.Key3) { Key2 = Key.Key3, Shift2 = true },
        new KeyBindInfo(KeyBindAction.UseBelt04, Key.Key4) { Key2 = Key.Key4, Shift2 = true },
        new KeyBindInfo(KeyBindAction.UseBelt05, Key.Key5) { Key2 = Key.Key5, Shift2 = true },
        new KeyBindInfo(KeyBindAction.UseBelt06, Key.Key6) { Key2 = Key.Key6, Shift2 = true },
        new KeyBindInfo(KeyBindAction.UseBelt07, Key.Key7) { Key2 = Key.Key7, Shift2 = true },
        new KeyBindInfo(KeyBindAction.UseBelt08, Key.Key8) { Key2 = Key.Key8, Shift2 = true },
        new KeyBindInfo(KeyBindAction.UseBelt09, Key.Key9) { Key2 = Key.Key9, Shift2 = true },
        new KeyBindInfo(KeyBindAction.UseBelt10, Key.Key0) { Key2 = Key.Key0, Shift2 = true },
        new KeyBindInfo(KeyBindAction.ChangeAttackMode, Key.H, control1: true),
        new KeyBindInfo(KeyBindAction.ChangePetMode, Key.A, control1: true),
        new KeyBindInfo(KeyBindAction.ToggleItemLock, Key.Scrolllock),
        new KeyBindInfo(KeyBindAction.SpellSet01, Key.F1, control1: true),
        new KeyBindInfo(KeyBindAction.SpellSet02, Key.F2, control1: true),
        new KeyBindInfo(KeyBindAction.SpellSet03, Key.F3, control1: true),
        new KeyBindInfo(KeyBindAction.SpellSet04, Key.F4, control1: true),
        new KeyBindInfo(KeyBindAction.SpellUse01, Key.F1),
        new KeyBindInfo(KeyBindAction.SpellUse02, Key.F2),
        new KeyBindInfo(KeyBindAction.SpellUse03, Key.F3),
        new KeyBindInfo(KeyBindAction.SpellUse04, Key.F4),
        new KeyBindInfo(KeyBindAction.SpellUse05, Key.F5),
        new KeyBindInfo(KeyBindAction.SpellUse06, Key.F6),
        new KeyBindInfo(KeyBindAction.SpellUse07, Key.F7),
        new KeyBindInfo(KeyBindAction.SpellUse08, Key.F8),
        new KeyBindInfo(KeyBindAction.SpellUse09, Key.F9),
        new KeyBindInfo(KeyBindAction.SpellUse10, Key.F10),
        new KeyBindInfo(KeyBindAction.SpellUse11, Key.F11),
        new KeyBindInfo(KeyBindAction.SpellUse12, Key.F12),
        new KeyBindInfo(KeyBindAction.SpellUse13, Key.F1, shift1: true),
        new KeyBindInfo(KeyBindAction.SpellUse14, Key.F2, shift1: true),
        new KeyBindInfo(KeyBindAction.SpellUse15, Key.F3, shift1: true),
        new KeyBindInfo(KeyBindAction.SpellUse16, Key.F4, shift1: true),
        new KeyBindInfo(KeyBindAction.SpellUse17, Key.F5, shift1: true),
        new KeyBindInfo(KeyBindAction.SpellUse18, Key.F6, shift1: true),
        new KeyBindInfo(KeyBindAction.SpellUse19, Key.F7, shift1: true),
        new KeyBindInfo(KeyBindAction.SpellUse20, Key.F8, shift1: true),
        new KeyBindInfo(KeyBindAction.SpellUse21, Key.F9, shift1: true),
        new KeyBindInfo(KeyBindAction.SpellUse22, Key.F10, shift1: true),
        new KeyBindInfo(KeyBindAction.SpellUse23, Key.F11, shift1: true),
        new KeyBindInfo(KeyBindAction.SpellUse24, Key.F12, shift1: true),
    };

    private static readonly List<KeyBindInfo> Defaults = KeyBinds.Select(Clone).ToList();
    private static bool _loaded;

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        var file = new ConfigFile();
        if (file.Load("user://ZirconKeyBinds.ini") != Error.Ok) return;
        foreach (var bind in KeyBinds)
        {
            string section = bind.Action.ToString();
            if (!file.HasSectionKey(section, "Key1")) continue;
            bind.Key1 = (Key)file.GetValue(section, "Key1").AsInt32();
            bind.Key2 = (Key)file.GetValue(section, "Key2", (int)Key.None).AsInt32();
            bind.Control1 = file.GetValue(section, "Control1", bind.Control1).AsBool();
            bind.Alt1 = file.GetValue(section, "Alt1", bind.Alt1).AsBool();
            bind.Shift1 = file.GetValue(section, "Shift1", bind.Shift1).AsBool();
            bind.Control2 = file.GetValue(section, "Control2", false).AsBool();
            bind.Alt2 = file.GetValue(section, "Alt2", false).AsBool();
            bind.Shift2 = file.GetValue(section, "Shift2", false).AsBool();
        }
    }

    public static void Save()
    {
        var file = new ConfigFile();
        foreach (var bind in KeyBinds)
        {
            string section = bind.Action.ToString();
            file.SetValue(section, "Key1", (int)bind.Key1);
            file.SetValue(section, "Key2", (int)bind.Key2);
            file.SetValue(section, "Control1", bind.Control1);
            file.SetValue(section, "Alt1", bind.Alt1);
            file.SetValue(section, "Shift1", bind.Shift1);
            file.SetValue(section, "Control2", bind.Control2);
            file.SetValue(section, "Alt2", bind.Alt2);
            file.SetValue(section, "Shift2", bind.Shift2);
        }
        file.Save("user://ZirconKeyBinds.ini");
    }

    public static void ResetDefaults()
    {
        foreach (var bind in KeyBinds)
        {
            var fallback = Defaults.First(x => x.Action == bind.Action);
            bind.Key1 = fallback.Key1;
            bind.Key2 = fallback.Key2;
            bind.Control1 = fallback.Control1;
            bind.Alt1 = fallback.Alt1;
            bind.Shift1 = fallback.Shift1;
            bind.Control2 = fallback.Control2;
            bind.Alt2 = fallback.Alt2;
            bind.Shift2 = fallback.Shift2;
        }
        Save();
    }

    private static KeyBindInfo Clone(KeyBindInfo source)
        => new(source.Action, source.Key1, source.Control1, source.Alt1, source.Shift1)
        {
            Key2 = source.Key2,
            Control2 = source.Control2,
            Alt2 = source.Alt2,
            Shift2 = source.Shift2,
        };

    /// <summary>按键事件 -> 命中的键位动作 (第一键/第二键均可触发，含修饰键匹配)</summary>
    public static KeyBindAction GetAction(InputEventKey key)
    {
        if (key == null || key.Keycode == Key.None) return KeyBindAction.None;

        bool ctrl = key.CtrlPressed;
        bool alt = key.AltPressed;
        bool shift = key.ShiftPressed;
        Key pressed = NormalizeKey(key.Keycode);

        foreach (var bind in KeyBinds)
        {
            bool first = bind.Key1 == pressed
                && bind.Control1 == ctrl && bind.Alt1 == alt && bind.Shift1 == shift;
            bool second = bind.Key2 != Key.None && bind.Key2 == pressed
                && bind.Control2 == ctrl && bind.Alt2 == alt && bind.Shift2 == shift;
            if (first || second) return bind.Action;
        }
        return KeyBindAction.None;
    }

    private static Key NormalizeKey(Key key)
        => key is >= Key.Kp0 and <= Key.Kp9
            ? (Key)((int)Key.Key0 + ((int)key - (int)Key.Kp0))
            : key;

    /// <summary>键位显示文本 (原版 GetKeyBindLabel 格式: "Ctrl + H")</summary>
    public static string GetKeyBindLabel(KeyBindAction action)
    {
        var bind = KeyBinds.Find(x => x.Action == action);
        if (bind == null) return "None";

        string text = "";
        if (bind.Control1) text += "Ctrl + ";
        if (bind.Alt1) text += "Alt + ";
        if (bind.Shift1) text += "Shift + ";
        text += GetKeyText(bind.Key1);

        if (bind.Key2 != Key.None)
        {
            text += ", ";
            if (bind.Control2) text += "Ctrl + ";
            if (bind.Alt2) text += "Alt + ";
            if (bind.Shift2) text += "Shift + ";
            text += GetKeyText(bind.Key2);
        }

        return text;
    }

    public static string GetKeyText(Key key)
    {
        // 可读键名: 字母/数字直接显示, 特殊键映射
        if (key >= Key.A && key <= Key.Z) return key.ToString();
        if (key >= Key.Key0 && key <= Key.Key9) return ((char)('0' + (key - Key.Key0))).ToString();
        return key switch
        {
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Tab => "Tab",
            Key.Escape => "Esc",
            Key.F1 => "F1",
            Key.F2 => "F2",
            _ => key.ToString(),
        };
    }
}
