using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Godot;
using ZirconClient.Controls;
using ZirconClient.Scripts;

namespace ZirconClient.Network;

/// <summary>
/// 单机模式启动器：客户端启动时若目标端口没有服务端监听，自动拉起
/// 本地 ServerCore，客户端退出时自动关闭（进程生命周期绑定）。
///
/// 设计要点（与用户沟通后确认）：
///  - 复用现有 test@test.com/TestHero 账号（不新建）；
///  - 端口已有监听（如用户手动开的服务器）→ 直接连，不拉起、退出时不杀；
///  - 仅杀掉"本启动器拉起"的 ServerCore（记录 PID），不误杀外部进程；
///  - 服务器数据目录：Debug/ServerCore（System.db + 809 张地图）。
///
/// 用法：
///  - 默认自动模式：端口无监听即拉起（连不上才触发，联网时无感）；
///  - 命令行 --single / --offline：强制单机模式（探测+拉起）；
///  - 命令行 --server host：指定远程服务器时不触发单机逻辑。
/// </summary>
public partial class SinglePlayerLauncher : Node
{
    /// <summary>本启动器拉起的 ServerCore 进程（退出时只杀它）。</summary>
    private Process _spawnedServer;

    /// <summary>本启动器拉起的 BotRunner 进程（单机模式机器人玩家，退出时一并关闭）。</summary>
    private Process _spawnedBots;

    /// <summary>本启动器是否拉起了服务端（LoginScene 据此决定是否等待就绪）。</summary>
    public bool IsSpawned => _spawnedServer != null;

    /// <summary>端口探测+拉起的互斥锁，避免重复拉起。</summary>
    private int _started;

    /// <summary>客户端进程退出钩子里调用（NOTIFICATION_WM_CLOSE / AutoAcceptQuit）。</summary>
    public void EnsureServerRunning(string host, int port)
    {
        if (_spawnedServer != null) return; // 已拉起
        if (Interlocked.Exchange(ref _started, 1) != 0) return;

        // 远程服务器模式：不触发单机逻辑
        if (AutoLoginArgs.ServerAddress != null) return;

        // 端口已有监听 → 连现有的（用户手动开的服务器），不拉起
        if (IsPortOpen(host, port))
        {
            GD.Print($"[Single] 端口 {host}:{port} 已有监听，使用现有服务端（不拉起）");
            return;
        }

        GD.Print("[Single] 未检测到服务端，启动单机模式：拉起本地 ServerCore ...");
        try
        {
            string root = ResolveServerRoot();
            string dll = Path.Combine(root, "ServerCore.dll");
            if (!File.Exists(dll))
            {
                GD.PrintErr($"[Single] ServerCore.dll 不存在: {dll}");
                return;
            }

            string configuredExe = System.Environment.GetEnvironmentVariable("ZIRCON_SERVER_EXE");
            string serverExe = string.IsNullOrWhiteSpace(configuredExe)
                ? null
                : Path.IsPathRooted(configuredExe)
                    ? configuredExe
                    : Path.GetFullPath(Path.Combine(root, configuredExe));
            if (serverExe != null && !File.Exists(serverExe))
            {
                GD.PrintErr($"[Single] ZIRCON_SERVER_EXE 不存在: {serverExe}");
                return;
            }

            var psi = new ProcessStartInfo
            {
                // 显式设置时运行随掌机包提供的自包含 apphost；未设置时保留桌面端原有的 dotnet 启动方式。
                FileName = serverExe ?? "dotnet",
                // --singleplayer-dev: 服务端给测试账号注入满级/全技能/全装备，
                // 配合客户端自动登录，双击客户端即可单机测试 UI。
                Arguments = serverExe == null
                    ? "ServerCore.dll --singleplayer-dev"
                    : "--singleplayer-dev",
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                // ServerCore 主线程 Console.ReadLine() 会阻塞等 stdin：
                // 重定向 stdin 并立即关闭，让 ReadLine 返回 null、主线程进入循环等待。
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            _spawnedServer = Process.Start(psi);
            _spawnedServer.StandardInput.Close(); // EOF → ReadLine 返回 null，不抛异常
            GD.Print($"[Single] ServerCore 已启动 PID={_spawnedServer.Id}");

            // 异步排空输出，避免管道写满阻塞
            _spawnedServer.OutputDataReceived += (_, e) => { if (e.Data != null) GD.Print($"[Srv] {e.Data}"); };
            _spawnedServer.ErrorDataReceived += (_, e) => { if (e.Data != null) GD.PrintErr($"[Srv] {e.Data}"); };
            _spawnedServer.BeginOutputReadLine();
            _spawnedServer.BeginErrorReadLine();

            // 服务端就绪后拉起机器人玩家（BotRunner，自动注册 bot01..botN 进城巡逻聊天）
            _ = StartBotsAsync(root, host, port);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Single] 启动 ServerCore 失败: {ex.Message}");
            _spawnedServer = null;
        }
    }

    /// <summary>
    /// 服务端端口就绪后拉起 BotRunner（机器人玩家）。找不到 dll/配置则静默跳过，
    /// 不影响单机主流程。机器人数量由 BotRunner.single.json 的 MaxBots 控制（默认 5）。
    /// </summary>
    private async System.Threading.Tasks.Task StartBotsAsync(string serverRoot, string host, int port)
    {
        try
        {
            // 等服务端端口就绪（复用 15s 预算内的一段）
            var deadline = System.Environment.TickCount64 + 12000;
            while (System.Environment.TickCount64 < deadline)
            {
                if (IsPortOpen(host, port)) break;
                if (_spawnedServer is { HasExited: true }) return;
                await System.Threading.Tasks.Task.Delay(300);
            }

            // BotRunner 与配置都在仓库 BotRunner/ 下（配置路径相对 ServerCore 运行目录）
            string repoBotDir = Path.GetFullPath(Path.Combine(serverRoot, "..", "..", "BotRunner"));
            string dll = Path.Combine(repoBotDir, "bin", "Debug", "net10.0", "BotRunner.dll");
            string cfg = Path.Combine(serverRoot, "BotRunner.single.json");
            if (!File.Exists(dll) || !File.Exists(cfg))
            {
                GD.Print($"[Single] BotRunner 未就绪,跳过机器人 (dll={File.Exists(dll)} cfg={File.Exists(cfg)})");
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{dll}\" \"{cfg}\"",
                WorkingDirectory = serverRoot, // 配置里的 Database/Map 相对路径基于 ServerCore
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            _spawnedBots = Process.Start(psi);
            _spawnedBots.OutputDataReceived += (_, e) => { if (e.Data != null) GD.Print($"[Bot] {e.Data}"); };
            _spawnedBots.ErrorDataReceived += (_, e) => { if (e.Data != null) GD.PrintErr($"[Bot] {e.Data}"); };
            _spawnedBots.BeginOutputReadLine();
            _spawnedBots.BeginErrorReadLine();
            GD.Print($"[Single] BotRunner 已启动 PID={_spawnedBots.Id}（机器人玩家）");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Single] BotRunner 启动失败(不影响单机): {ex.Message}");
            _spawnedBots = null;
        }
    }

    /// <summary>等待服务端端口就绪（最多约 15 秒，每 250ms 探测一次）。</summary>
    public bool WaitForServer(string host, int port, int timeoutMs = 15000)
    {
        var deadline = System.Environment.TickCount64 + timeoutMs;
        while (System.Environment.TickCount64 < deadline)
        {
            if (IsPortOpen(host, port)) return true;
            if (_spawnedServer != null && _spawnedServer.HasExited)
            {
                GD.PrintErr($"[Single] ServerCore 已退出: code={_spawnedServer.ExitCode}");
                return false;
            }
            Thread.Sleep(250);
        }
        GD.PrintErr("[Single] 等待服务端就绪超时");
        return false;
    }

    /// <summary>客户端退出时调用：只杀本启动器拉起的 ServerCore 与 BotRunner。</summary>
    public void Shutdown()
    {
        var bots = _spawnedBots;
        _spawnedBots = null;
        if (bots != null && !bots.HasExited)
        {
            GD.Print($"[Single] 关闭机器人 PID={bots.Id}");
            try { bots.Kill(entireProcessTree: true); } catch { /* 已退出 */ }
            bots.Dispose();
        }

        var p = _spawnedServer;
        _spawnedServer = null;
        if (p == null || p.HasExited) return;
        GD.Print($"[Single] 关闭单机模式服务端 PID={p.Id}");
        try { p.Kill(entireProcessTree: true); } catch { /* 已退出 */ }
        try { p.WaitForExit(3000); } catch { /* 忽略 */ }
        p.Dispose();
    }

    public override void _Notification(int what)
    {
        // 窗口关闭 / 场景树退出：关闭自己拉起的服务端（不误杀外部进程）
        if (what == NotificationWMCloseRequest || what == NotificationPredelete || what == NotificationExitTree)
            Shutdown();
    }

    private static bool IsPortOpen(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            var ar = client.BeginConnect(host, port, null, null);
            return ar.AsyncWaitHandle.WaitOne(500) && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 定位本地 ServerCore 运行目录。按环境变量、项目相对路径与工作目录候选依次探测。
    /// </summary>
    private static string ResolveServerRoot()
    {
        string configured = System.Environment.GetEnvironmentVariable("ZIRCON_SERVER_DIR");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)
            && File.Exists(Path.Combine(configured, "ServerCore.dll")))
            return configured;

        string resBase = ProjectSettings.GlobalizePath("res://");
        string[] candidates =
        {
            Path.GetFullPath(Path.Combine(resBase, "..", "Debug", "ServerCore")),
            Path.GetFullPath(Path.Combine(resBase, "..", "..", "Debug", "ServerCore")),
            Path.GetFullPath("Debug/ServerCore"),
            Path.GetFullPath("../Debug/ServerCore"),
            Path.GetFullPath("../../Debug/ServerCore"),
        };

        foreach (var dir in candidates)
        {
            if (File.Exists(Path.Combine(dir, "ServerCore.dll")))
                return dir;
        }

        return candidates[0];
    }
}
