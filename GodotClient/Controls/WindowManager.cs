using System.Collections.Generic;
using Godot;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>
/// 窗口管理器 (原版 DXWindow.Windows 列表 + ActiveScene 窗口管理的 Godot 版)。
/// 可见窗口按打开顺序排 Z; Esc 关闭最上层; 打开/关闭/切换统一入口。
/// 所有游戏窗口都应通过 WindowManager 开关, 保证 Z 序一致。
/// </summary>
public static class WindowManager
{
    /// <summary>按打开顺序排列的可见窗口 (列表尾 = 最上层)</summary>
    public static readonly List<DXWindow> OpenWindows = new();

    /// <summary>窗口 Z 序起点 (在 UI CanvasLayer 下, 100 起留足余量)</summary>
    public const int BaseZ = 100;

    public static void Open(DXWindow w, Node parent)
    {
        if (w == null || parent == null) return;
        if (!OpenWindows.Contains(w)) OpenWindows.Add(w);
        w.ShowWindow(parent);
        RefreshZOrder();
    }
    public static void Close(DXWindow w)
    {
        if (w == null) return;
        OpenWindows.Remove(w);
        GameScene.Game?.SetHoverItem(null);
        w.Close();
        RefreshZOrder();
    }

    public static void Toggle(DXWindow w, Node parent)
    {
        if (w.Visible) Close(w);
        else Open(w, parent);
    }

    /// <summary>关闭最上层窗口 (Esc 用); 没有可见窗口返回 false</summary>
    public static bool CloseTop()
    {
        for (int i = OpenWindows.Count - 1; i >= 0; i--)
        {
            var w = OpenWindows[i];
            if (!w.Visible)
            {
                OpenWindows.RemoveAt(i);
                continue;
            }
            Close(w);
            return true;
        }
        return false;
    }

    /// <summary>把窗口置顶 (点击/拖动标题栏时调用)</summary>
    public static void BringToFront(DXWindow w)
    {
        if (!w.Visible) return;
        if (OpenWindows.Remove(w)) OpenWindows.Add(w);
        RefreshZOrder();
    }

    // 可见窗口按打开顺序重排 Z; 顺带清掉已关闭的残留
    private static void RefreshZOrder()
    {
        for (int i = 0; i < OpenWindows.Count; i++)
        {
            var w = OpenWindows[i];
            if (!w.Visible)
            {
                OpenWindows.RemoveAt(i);
                i--;
                continue;
            }
            w.ZIndex = BaseZ + i;
        }
    }
}
