using Library;
using Server.Envir.Commands.Exceptions;
using Server.Models;
using System.Linq;

namespace Server.Envir.Commands.Command.Admin
{
    class SpawnMonster : AbstractParameterizedCommand<IAdminCommand>
    {
        public override string VALUE => "MONSTER";
        public override int PARAMS_LENGTH => 2;

        public override void Action(PlayerObject player, string[] vals)
        {
            if (vals.Length < PARAMS_LENGTH)
                ThrowNewInvalidParametersException();

            // 2026-10-18 修复：怪物名普遍含空格（126 只里 96 只，如 "Forest Yeti"、
            // "Dungeon Goddess 1"）。原实现只取 vals[1]，名字在第一个空格处被截断，
            // 于是只有 30 只单词名能刷出来，其余一律回 "Could not find monster: <第一个词>"。
            // 规则（向后兼容）：先把 vals[1..] 整体当名字试（覆盖以数字结尾的名字，
            // 例如 "Dungeon Goddess 1"）；失败且末尾 token 是整数时，再按"名字 + 数量"拆
            // （覆盖 "Forest Yeti 5"；0 沿用原语义 = 1 只）。
            string rawName = string.Join(" ", vals.Skip(1));
            var monsterInfo = SEnvir.GetMonsterInfo(rawName);
            int value = 1;

            if (monsterInfo == null && vals.Length > 2 && int.TryParse(vals[vals.Length - 1], out int count) && count >= 0)
            {
                string nameWithoutCount = string.Join(" ", vals.Skip(1).Take(vals.Length - 2));
                var byNameAndCount = SEnvir.GetMonsterInfo(nameWithoutCount);

                if (byNameAndCount != null)
                {
                    monsterInfo = byNameAndCount;
                    value = count == 0 ? 1 : count;
                }
            }

            if (monsterInfo == null)
                throw new UserCommandException(string.Format("Could not find monster: {0}", rawName));

            while (value > 0)
            {
                var monster = MonsterObject.GetMonster(monsterInfo);
                monster.Spawn(player.CurrentMap, Functions.Move(player.CurrentLocation, player.Direction));
                value -= 1;
            }
        }
    }
}
