using System.Linq;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.part;
using Vintagestory.API.Common;

namespace canclaimsextended.src.commands
{
    /// <summary>/citizen ...: a player's own friends list and info about players.</summary>
    public class CitizenCommand : BaseCommand
    {
        public static TextCommandResult FriendAdd(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out _, out var playerInfo, out var err)) return err;
            if (!TryFindPlayer((string)args.LastArg, out PlayerInfo target, out err)) return err;
            if (target.Equals(playerInfo))
            {
                return TextCommandResult.Error("canclaimsextended:friend_self");
            }
            if (!playerInfo.addComrade(target))
            {
                return SuccessWithParams("canclaimsextended:friend_already", target.GetPartName());
            }
            playerInfo.saveToDatabase();
            // Friends get rights on the owner's plots, so cached answers are stale.
            canclaimsextended.dataStorage.ResetCacheForAll();
            canclaimsextended.dataStorage.NotifyPlayerRightsChanged(target);
            return SuccessWithParams("canclaimsextended:friend_added", target.GetPartName());
        }

        public static TextCommandResult FriendRemove(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out _, out var playerInfo, out var err)) return err;
            if (!TryFindPlayer((string)args.LastArg, out PlayerInfo target, out err)) return err;
            if (!playerInfo.removeComrade(target))
            {
                return SuccessWithParams("canclaimsextended:friend_not_in_list", target.GetPartName());
            }
            playerInfo.saveToDatabase();
            canclaimsextended.dataStorage.ResetCacheForAll();
            canclaimsextended.dataStorage.NotifyPlayerRightsChanged(target);
            return SuccessWithParams("canclaimsextended:friend_removed", target.GetPartName());
        }

        public static TextCommandResult FriendList(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out _, out var playerInfo, out var err)) return err;
            if (playerInfo.Friends.Count == 0)
            {
                return TextCommandResult.Success("canclaimsextended:friend_list_empty");
            }
            return SuccessWithParams("canclaimsextended:friend_list", StringFunctions.MakeStringPlayersName(playerInfo.Friends.OrderBy(f => f.GetPartName()), ", "));
        }

        /// <summary>/citizen info [player]; the caller themselves by default.</summary>
        public static TextCommandResult Info(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out _, out var playerInfo, out var err)) return err;
            PlayerInfo target = playerInfo;
            if (!args.Parsers[0].IsMissing && !TryFindPlayer((string)args.LastArg, out target, out err)) return err;
            string text = target.GetPartName() + "\n" + string.Join("", target.getStatus(playerInfo));
            // The fee is only taken with a real economy, so only then is it a payment to come.
            City city = target.City;
            if (city != null && city.fee > 0 && !city.isMayor(target) && canclaimsextended.economyProvider.SupportsPlayerWallet)
            {
                text += Vintagestory.API.Config.Lang.Get("canclaimsextended:citizen_next_fee", city.fee, city.getPartNameReplaceUnder()) + "\n";
            }
            return TextCommandResult.Success(text);
        }
    }
}
