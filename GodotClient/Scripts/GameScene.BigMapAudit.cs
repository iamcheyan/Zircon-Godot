using System;
using System.Threading.Tasks;
using Godot;

namespace ZirconClient.Scripts;

/// <summary>
/// Tab 大地图（及"输入框夺走自定义热键"）的实机巡检通道。
///
/// 背景（2026-10-16 用户报「Tab 呼出大地图一会能用一会不能用」）：
/// `GameScene._Input` 开头有一条"有文本输入焦点就整体早退"的保护，于是任一时点
/// 聊天输入框持有焦点，Tab 就静默失效；而 legacy 聊天条按**空格/回车/`/`/`@`/`!`**
/// 都会 `GrabFocus`（见 LegacyChatDialog.HandleGlobalKey），所以玩着玩着按过空格，
/// Tab 就"不能用了"，回车/ESC 之后又好了 —— 症状正是"时灵时不灵"。
///
/// 本巡检**不注入真实键盘**（macOS 下 AppleScript keystroke 到 Godot 不可靠），
/// 而是走与真实按键完全相同的入口 `GameScene._Input(InputEventKey)`，聊天输入框
/// 也走真实路径（HandleGlobalKey / Viewport.PushInput），因此判定等价于实机按键。
///
/// 触发: --bigmap-audit [--bigmap-audit-out /tmp/xxx]
/// 结果: 打印 [BigMapAudit] PASS/FAIL 并保存逐步截图，最后退出进程。
/// </summary>
public partial class GameScene
{
    private bool _bigMapAuditStarted;
    private bool _bigMapAuditTriggerLogged;

    internal void StartBigMapAudit() => _ = RunBigMapAuditAsync();

    private enum ChatKind { None, Legacy, Modern }

    private async Task RunBigMapAuditAsync()
    {
        string outDir = AutoLoginArgs.BigMapAuditOut;
        try { System.IO.Directory.CreateDirectory(outDir); }
        catch (Exception ex) { GD.PrintErr($"[BigMapAudit] 无法创建输出目录 {outDir}: {ex.Message}"); }

        int fail = 0;
        void Check(bool ok, string what)
        {
            if (!ok) fail++;
            GD.Print($"[BigMapAudit] {(ok ? "ok " : "FAIL")} {what}");
        }

        string Shot(string name)
        {
            try
            {
                var image = GetViewport()?.GetTexture()?.GetImage();
                if (image == null) return "";
                string file = System.IO.Path.Combine(outDir, name + ".png");
                image.SavePng(file);
                return file;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[BigMapAudit] 截图异常: {ex.GetType().Name}: {ex.Message}");
                return "";
            }
        }

        // 等地图与 HUD 就绪（与其它审计同一口径）
        for (int i = 0; i < 120 && (_mapView?.Map == null || !_startGameShown); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        GD.Print($"[BigMapAudit] start mapIndex={_playerMapIndex} "
            + $"bigMapNode={(_bigMap == null ? "null" : "ok")} out={outDir}");

        if (_bigMap == null)
        {
            GD.PrintErr("[BigMapAudit] 大地图控件不存在，无法巡检（HUD 未初始化？）");
            GetTree().Quit(2);
            return;
        }

        // ---- 场景 1/2：无输入焦点时 Tab 开关大地图 --------------------------
        _bigMap.Visible = false;
        PressKey(Key.Tab);
        await NextFrameAsync();
        Check(_bigMap.Visible, "S1 无焦点按 Tab -> 大地图打开");
        GD.Print($"[BigMapAudit] shot {Shot("s1_tab_open")}");

        PressKey(Key.Tab);
        await NextFrameAsync();
        Check(!_bigMap.Visible, "S2 再按 Tab -> 大地图关闭");
        GD.Print($"[BigMapAudit] shot {Shot("s2_tab_close")}");

        // ---- 场景 3：聊天输入框获得焦点（真实路径：空格） --------------------
        var (kind, hasFocus) = await FocusChatInputAsync();
        GD.Print($"[BigMapAudit] 聊天输入框 kind={kind} hasFocus={hasFocus}");
        if (kind != ChatKind.None)
        {
            Check(hasFocus, "S3a 空格后聊天输入框确实持有焦点（复现用户触发条件）");
            GD.Print($"[BigMapAudit] shot {Shot("s3a_chat_focused")}");

            PressKey(Key.Tab);
            await NextFrameAsync();
            Check(_bigMap.Visible, "S3b 空聊天输入框时按 Tab -> 大地图仍打开（本次修复点）");
            GD.Print($"[BigMapAudit] shot {Shot("s3b_tab_open_with_chat_focus")}");

            // ---- 场景 4：输入框非空（正在打字）时 Tab 应留给输入框 ----------
            await TypeIntoFocusedAsync("abc");
            await NextFrameAsync();
            if (_bigMap.Visible) { PressKey(Key.Tab); await NextFrameAsync(); }   // 关掉地图再测
            bool before = _bigMap.Visible;
            PressKey(Key.Tab);
            await NextFrameAsync();
            Check(_bigMap.Visible == before, "S4 正在打字（非空）时 Tab 不开大地图");
            GD.Print($"[BigMapAudit] shot {Shot("s4_tab_blocked_while_typing")}");

            // ---- 场景 5：ESC 取消输入后 Tab 恢复 ----------------------------
            await CancelChatInputAsync();
            await NextFrameAsync();
            bool wasVisible = _bigMap.Visible;
            PressKey(Key.Tab);
            await NextFrameAsync();
            Check(_bigMap.Visible != wasVisible, "S5 ESC 取消输入后按 Tab -> 恢复开关");
            GD.Print($"[BigMapAudit] shot {Shot("s5_tab_after_escape")}");
        }

        GD.Print($"[BigMapAudit] {(fail == 0 ? "PASS" : "FAIL")} failures={fail}");
        GetTree().Quit(fail == 0 ? 0 : 1);
    }

    /// <summary>走 GameScene._Input —— 与真实按键同一个入口（不做任何旁路）。</summary>
    private void PressKey(Key code)
        => _Input(new InputEventKey { Keycode = code, PhysicalKeycode = code, Pressed = true });

    /// <summary>用真实路径（聊天条的空格键处理）让输入框拿到焦点。</summary>
    private async Task<(ChatKind Kind, bool HasFocus)> FocusChatInputAsync()
    {
        if (_legacyChatDialog is { Visible: true })
        {
            _legacyChatDialog.HandleGlobalKey(
                new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = true }, _uiLayer);
            await NextFrameAsync();
            return (ChatKind.Legacy, _legacyChatDialog.InputHasFocus);
        }
        if (_chatTextBox is { Visible: true })
        {
            _chatTextBox.HandleGlobalKey(
                new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = true });
            await NextFrameAsync();
            return (ChatKind.Modern, _chatTextBox.InputHasFocus);
        }
        return (ChatKind.None, false);
    }

    /// <summary>
    /// 往当前焦点控件里"打字"：Viewport.PushInput 走 Godot 正常事件分发，
    /// 由持有焦点的 LineEdit 自己消费，等价于真实键盘输入。
    /// </summary>
    private async Task TypeIntoFocusedAsync(string text)
    {
        foreach (char ch in text)
        {
            GetViewport()?.PushInput(new InputEventKey
            {
                Keycode = Key.A,          // 键码不参与文本插入
                Unicode = ch,
                Pressed = true,
            });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    /// <summary>ESC 丢给焦点控件（DXTextInput 的 Canceled -> ReleaseFocus）。</summary>
    private async Task CancelChatInputAsync()
    {
        GetViewport()?.PushInput(new InputEventKey
        {
            Keycode = Key.Escape,
            PhysicalKeycode = Key.Escape,
            Pressed = true,
        });
        await NextFrameAsync();
    }
}
