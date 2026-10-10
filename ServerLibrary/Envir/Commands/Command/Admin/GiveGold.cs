using Library;
using Server.DBModels;
using Server.Envir.Commands.Command;
using Server.Envir.Commands.Command.Admin;
using Server.Envir.Commands.Exceptions;
using Server.Models;

namespace Server.Envir.Commands.Admin
{
    /// <summary>
    /// @giveGold &lt;角色名&gt; &lt;数量&gt; —— 发放金币（Characters.Gold，非 GameGold）。
    /// 用途：商店买卖/NPC 收费传送等功能的实机验证需要可控金币来源。
    /// </summary>
    class GiveGold : AbstractParameterizedCommand<IAdminCommand>
    {
        public override string VALUE => "GIVEGOLD";
        public override int PARAMS_LENGTH => 3;

        public override void Action(PlayerObject player, string[] vals)
        {
            CharacterInfo character = SEnvir.GetCharacter(vals[1]);
            if (character == null)
                throw new UserCommandException(string.Format("Could not find player: {0}.", vals[1]));

            if (!long.TryParse(vals[2], out long count))
                ThrowNewInvalidParametersException();

            character.Account.Gold.Amount += count;
            character.Player?.GoldChanged();
            player.Connection.ReceiveChat($"[GIVE GOLD] {character.CharacterName} Amount: {count}",
                MessageType.System);
        }
    }
}
