using Server.Envir.Commands.Exceptions;
using Server.Models;
using System;
using System.Collections.Generic;
using System.Linq;
namespace Server.Envir.Commands.Command
{
    public abstract class AbstractParameterizedCommand<CommandType> : IParameterizedCommand
        where CommandType : ICommand
    {
        public abstract string VALUE { get; }
        public abstract int PARAMS_LENGTH { get; }

        public abstract void Action(PlayerObject player, string[] vals);

        /// <summary>
        /// 把被空格拆散的多词参数重新合并。
        ///
        /// **为什么需要**：`PlayerObject.Chat` 用 `Split(' ')` 切分 `@` 命令，
        /// 于是 `@make Wood Sword 5` → `["MAKE","Wood","Sword","5"]`，
        /// `Make` 只读 `vals[1]` → 找 "Wood"，带空格的英文名永远匹配不到
        /// （`SEnvir.GetItemInfo` 内部已做 `Replace(" ","")` 归一化，
        /// 瓶颈只在切分这一步）。
        ///
        /// **用法**：需要"名字可能含空格"的参数（物品/怪物/技能…）在命令
        /// Action 开头调用一次即可，不必逐个命令打补丁。`from` 是该参数在
        /// vals 中的起始下标；`tailCount` 是它**之后**固定的非名称参数个数
        /// （数量、目标角色名…），必须留在尾部不被合并。
        /// </summary>
        protected static string[] RejoinNameArgs(string[] vals, int from = 1, int tailCount = 0)
        {
            if (vals == null || from >= vals.Length) return vals;
            int nameEnd = vals.Length - tailCount;
            if (nameEnd - from < 2) return vals;   // 本来就是单段，无需合并

            var result = new List<string>(vals.Length);
            for (int i = 0; i < from; i++) result.Add(vals[i]);
            result.Add(string.Join(" ", vals.Skip(from).Take(nameEnd - from)));
            for (int i = nameEnd; i < vals.Length; i++) result.Add(vals[i]);
            return result.ToArray();
        }

        /// <summary>
        /// 贪心地把 tokens 合并成**最长的、能命中目标名**的前缀。
        /// 用于「数量跟在名字后面」的场景（`@make Wood Sword 5`）：
        /// 从最长候选开始试，命中即停，剩余原样作为尾部参数。
        /// </summary>
        protected static string[] RejoinLongestMatch(string[] vals, Func<string, bool> exists, int from = 1)
        {
            if (vals == null || from + 1 >= vals.Length || exists == null) return vals;
            for (int take = vals.Length - from; take >= 2; take--)
            {
                string candidate = string.Join(" ", vals.Skip(from).Take(take));
                if (!exists(candidate)) continue;
                var result = new List<string>(vals.Take(from)) { candidate };
                result.AddRange(vals.Skip(from + take));
                return result.ToArray();
            }
            return vals;
        }

        public UserCommandException ThrowNewInvalidParametersException()
        {
            throw new UserCommandException(string.Format("Invalid Parameters for command @{0}", VALUE));
        }
    }
}
