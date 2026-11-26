using System;
using System.Linq;
using Godot;
using Library;
using Library.Network;
using ZirconClient.Controls;
using ZirconClient.Network;
using S = Library.Network.ServerPackets;

namespace ZirconClient.Scripts;

public partial class GameScene
{
    private GmInspectionPanel _gmInspection;
    private ServerConnection _gmConnection;
    private int _gmMapIndex = -1;
    private ulong _gmRequestAt;
    private S.GmInspectResult _gmRendered;
    private GmInspectionRow[] _gmMaps = Array.Empty<GmInspectionRow>();
    private bool GmInspectionOpen => _gmInspection?.Visible == true;
    private bool GmInspectionAllowed => _startGameShown && !_leavingGame && !IsObserver &&
        _net?.Connection is { Connected: true, GmInspectionAllowed: true } connection && connection == _gmConnection;

    private void ProcessGmInspection()
    {
        var connection = _net?.Connection;
        if (connection != _gmConnection)
        {
            CloseGmInspection();
            _gmConnection = connection;
        }
        if (_startGameShown && !_leavingGame && !IsObserver && connection?.Connected == true)
            connection.ProbeGmInspection();
        if (!GmInspectionOpen) return;
        if (!GmInspectionAllowed) { CloseGmInspection(); return; }

        var result = connection.LastGmInspection;
        if (result?.MapIndex == _gmMapIndex && !ReferenceEquals(_gmRendered, result))
            PresentGmInspection(result);
        ulong now = Godot.Time.GetTicksMsec();
        if (now >= _gmRequestAt)
        {
            connection.QueryGmInspection(_gmMapIndex, now);
            _gmRequestAt = now + 3000;
        }
    }

    private void OpenGmInspection()
    {
        if (!GmInspectionAllowed || HandheldHasModalWindow() || _bigMap?.Visible == true || HandheldWorldMapOpen || HandheldActionBindingsOpen) return;
        if (_gmInspection == null)
        {
            var layer = new CanvasLayer { Name = "GmInspectionLayer", Layer = 85 };
            AddChild(layer);
            _gmInspection = new GmInspectionPanel(AutoLoginArgs.Handheld);
            layer.AddChild(_gmInspection);
            _gmInspection.CloseRequested += CloseGmInspection;
            _gmInspection.MapSelected += SelectGmInspectionMap;
        }
        if (JournalOpen) CloseJournal();
        StopHandheldMovement();
        ClearHandheldFocus();
        _pendingMagicPacket = null;
        _pendingNpcClickObjectId = 0;
        GetViewport()?.GuiGetFocusOwner()?.ReleaseFocus();
        _gmMaps = Globals.MapInfoList?.Binding.OrderByDescending(map => map.Index == _playerMapIndex)
            .ThenBy(map => map.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(map => new GmInspectionRow($"{(map.Index == _playerMapIndex ? "当前 · " : "")}{map.Description} [{map.FileName}]",
                $"地图编号：{map.Index}。选中只查询，不传送角色。", map.Index)).ToArray() ?? Array.Empty<GmInspectionRow>();
        _gmInspection.Show();
        _gmMapIndex = -1;
        SelectGmInspectionMap(_playerMapIndex);
    }

    private void CloseGmInspection()
    {
        if (!GmInspectionOpen) return;
        _gmInspection.Hide();
        StopHandheldMovement();
        ResetJournalNavigation();
    }

    private void SelectGmInspectionMap(int index)
    {
        if (!GmInspectionAllowed || !GmInspectionOpen || GetMapInfo(index) == null) return;
        if (_gmMapIndex != index || _gmRendered == null)
        {
            _gmMapIndex = index;
            // Keep at least 600 ms between requests, even when clicking several maps quickly.
            ulong now = Godot.Time.GetTicksMsec();
            _gmRequestAt = Math.Max(now, _gmConnection.LastGmInspectionRequestAt + 600);
            PresentGmInspection(null);
        }
    }

    private bool HandleGmInspectionInput(InputEvent action)
    {
        if (GmInspectionOpen && !GmInspectionAllowed) CloseGmInspection();
        if (action is not InputEventKey key) return false;
        bool shortcut = !AutoLoginArgs.Handheld && key.Keycode == Key.F8 && key.CtrlPressed && key.ShiftPressed && !key.AltPressed && !key.MetaPressed;
        if (shortcut && GmInspectionAllowed)
        {
            if (key.Pressed && !key.Echo)
            {
                if (GmInspectionOpen) CloseGmInspection();
                else if (GetViewport()?.GuiGetFocusOwner() is not (LineEdit or TextEdit) &&
                    _legacyChatDialog?.InputHasFocus != true && _chatTextBox?.InputHasFocus != true)
                    OpenGmInspection();
                else return false;
            }
            return true;
        }
        if (!GmInspectionOpen) return false;
        if (AutoLoginArgs.Handheld && key.Keycode is Key.F11 or Key.Menu)
        {
            if (key.Pressed && !key.Echo) CloseGmInspection();
            return true;
        }
        return _gmInspection.HandleKey(key);
    }

    private void PresentGmInspection(S.GmInspectResult result)
    {
        _gmRendered = result;
        var standing = GetMapInfo(_playerMapIndex);
        var inspected = GetMapInfo(_gmMapIndex);
        string state = result == null ? "等待服务器回报" : result.Reason?.Length > 0 ? GmInspectionReason(result.Reason)
            : result.Truncated ? "结果已截断" : "只读诊断；未验收踩门或 NPC 功能";
        string summary = $"{standing?.FileName} ({_playerLocation.X},{_playerLocation.Y}) · Lv.{PlayerLevel} · 技能 {UserMagics.Count} · 已穿 {Equipment.Count(item => item != null)}\n" +
            $"查看 {inspected?.FileName} · 数据库 {_gmConnection.GmInspectionDatabaseVersion} · {state}";
        _gmInspection.Present(summary, _gmMaps,
            result?.Ports.Select(port => new GmInspectionRow($"#{port.MovementIndex} → {port.DestinationMapName} [{port.DestinationMapFileName}]",
                $"入口：{string.Join("、", port.SourcePoints.Select(p => $"({p.X},{p.Y})"))}\n" +
                $"目标落点：{string.Join("、", port.Destinations.Select(p => $"({p.Location.X},{p.Location.Y}) {GmInspectionReason(p.Reason)}"))}\n" +
                $"等级限制：{port.MinimumLevel}–{(port.MaximumLevel <= 0 ? "无上限" : port.MaximumLevel.ToString())} · 职业：{port.RequiredClass}\n" +
                $"道具：{GmValue(port.NeedItem)} · 清怪规则：{(port.NeedSpawnIndex < 0 ? "无" : $"#{port.NeedSpawnIndex}")}\n" +
                $"穴口：{(port.NeedHole ? "需要" : "无")} · 副本：{GmValue(port.NeedInstance)}\n{GmInspectionReason(port.Reason)}",
                port.MovementIndex)).ToArray() ?? Array.Empty<GmInspectionRow>(),
            result?.Npcs.Select(npc => new GmInspectionRow($"#{npc.NpcIndex} {npc.Name}",
                $"坐标：{(npc.HasLocation ? $"({npc.Location.X},{npc.Location.Y})" : "无")} · {(npc.IsSpawned ? "服务端已生成" : "未确认生成；坐标取自配置")}\n" +
                $"入口页：{(npc.EntryPageIndex < 0 ? "无" : $"#{npc.EntryPageIndex} {npc.EntryPage}")}\n服务（配置）：{string.Join("、", npc.Services.DefaultIfEmpty("未识别"))}\n未验收对话或功能。",
                npc.NpcIndex)).ToArray() ?? Array.Empty<GmInspectionRow>());
    }

    private static string GmValue(string value) => string.IsNullOrWhiteSpace(value) ? "无" : value;
    private static string GmInspectionReason(string value) => value switch
    {
        null or "" => "静态检查未发现障碍；未实测",
        "TerrainUnknown" => "目标地形未加载；未验证",
        "BlockedTerrain" => "落点在障碍上",
        "OutOfBounds" => "落点越界",
        "InvalidSourceTerrain" => "入口在障碍上或越界",
        "MissingDestinationMap" => "缺少目标地图",
        "MapNotFound" => "服务器没有这张地图",
        "CoordinatesUnavailable" => "坐标暂不可用",
        "NoPoints" => "没有配置坐标",
        _ => value,
    };
}
