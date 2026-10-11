using Library;
using Server.Envir.Commands.Command;
using Server.Envir.Commands.Command.Admin;
using Server.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Envir.Commands.Admin
{
    /// <summary>
    /// @mobcensus [地图文件名] —— 打印指定地图（缺省当前地图）上**活着的怪物**按种类统计。
    /// 用途：刷怪数据（RespawnInfo）落库后的服务端真值核对，替代"肉眼看屏幕数怪"。
    /// </summary>
    class MobCensus : AbstractParameterizedCommand<IAdminCommand>
    {
        public override string VALUE => "MOBCENSUS";
        public override int PARAMS_LENGTH => 1;

        public override void Action(PlayerObject player, string[] vals)
        {
            string want = vals.Length > 1 ? vals[1] : null;
            var maps = new List<Map>();

            if (want == null)
            {
                maps.Add(player.CurrentMap);
            }
            else
            {
                var info = SEnvir.MapInfoList.Binding.FirstOrDefault(
                    x => string.Equals(x.FileName, want, StringComparison.OrdinalIgnoreCase));
                if (info != null)
                {
                    var map = SEnvir.GetMap(info);
                    if (map != null) maps.Add(map);
                }
            }

            if (maps.Count == 0)
            {
                player.Connection.ReceiveChat($"[MOBCENSUS] 找不到地图: {want ?? "(current)"}", MessageType.System);
                return;
            }

            foreach (var map in maps)
            {
                var census = map.Objects
                    .OfType<MonsterObject>()
                    .Where(m => m.Spawned && !m.Dead)
                    .GroupBy(m => m.MonsterInfo?.MonsterName ?? "?")
                    .OrderByDescending(g => g.Count())
                    .ToList();

                int total = census.Sum(g => g.Count());
                player.Connection.ReceiveChat(
                    $"[MOBCENSUS] map={map.Info.FileName} total={total} kinds={census.Count}",
                    MessageType.System);

                // 机读输出：追加到 /tmp/mobcensus.txt（一行一张图），供刷怪审计脚本比对
                try
                {
                    System.IO.File.AppendAllText("/tmp/mobcensus.txt",
                        $"{map.Info.FileName}\ttotal={total}\t" +
                        string.Join(",", census.Select(g => $"{g.Key}={g.Count()}")) + "\n");
                }
                catch (Exception ex)
                {
                    SEnvir.Log($"[MOBCENSUS] 写文件失败: {ex.Message}");
                }

                foreach (var g in census)
                    player.Connection.ReceiveChat($"[MOBCENSUS] {map.Info.FileName}\t{g.Key}\t{g.Count()}",
                        MessageType.System);
            }
        }
    }
}
