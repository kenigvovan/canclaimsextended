using System.Collections.Generic;
using caneconomy.src.implementations.RealMoney;
using caneconomy.src.interfaces;
using canclaimsextended.src.messages;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace canclaimsextended.src.economy.bridge
{
    /// <summary>
    /// Bank chests of the REAL_MONEY economy: a sign "[city]Name" or "[player]Name" on a chest makes
    /// it the account. A city's chest may stand anywhere on its territory, its chunks included, so a
    /// new city without plots has a treasury from the start.
    /// </summary>
    public static class RealBankActions
    {
        private static RealMoneyEconomyHandler RealHandler => caneconomy.caneconomy.getHandler() as RealMoneyEconomyHandler;

        public static void OnBlockRemoved(BlockEntityOpenableContainer be)
        {
            if (be is not BlockEntityGenericTypedContainer) return;
            var handler = RealHandler;
            if (handler == null) return;
            if (!handler.TryGetRealBankInfo(new Vec3i(be.Pos.X, be.Pos.Y, be.Pos.Z), out RealBankInfo rbi)) return;

            (handler as EconomyHandler).deleteAccount(rbi.AccountName);

            string accName = rbi.AccountName;
            string prefix = canclaimsextended.config.CITY_ACCOUNT_STRING_PREFIX;
            if (accName.StartsWith(prefix))
            {
                if (canclaimsextended.dataStorage.GetCityByGUID(accName.Substring(prefix.Length), out City city))
                {
                    MessageHandler.sendMsgToPlayerInfo(city.getMayor(), Lang.Get("canclaimsextended:city_bank_was_destroyed"));
                }
            }
            else if (canclaimsextended.dataStorage.GetPlayerByUid(accName, out PlayerInfo playerInfo))
            {
                MessageHandler.sendMsgToPlayerInfo(playerInfo, Lang.Get("canclaimsextended:your_bank_was_destroyed"));
            }
        }

        public static void OnButtonSave(BlockEntitySign sign, IPlayer player, int packetid)
        {
            // 1002: the sign's text was saved.
            if (packetid != 1002) return;
            var handler = RealHandler;
            if (handler == null || player is not IServerPlayer splayer) return;

            BlockPos chestPos = sign.Pos.Copy();
            string signCodePath = sign.Block.Code.Path;
            if (signCodePath.EndsWith("east")) chestPos.X += 1;
            else if (signCodePath.EndsWith("west")) chestPos.X -= 1;
            else if (signCodePath.EndsWith("north")) chestPos.Z -= 1;
            else if (signCodePath.EndsWith("south")) chestPos.Z += 1;

            if (canclaimsextended.sapi.World.BlockAccessor.GetBlockEntity(chestPos) is not BlockEntityGenericTypedContainer) return;

            string signText = (sign.text ?? "").Replace("\n", "").Trim();
            string cityPrefix = Lang.Get("canclaimsextended:economy_chest_bank_city_sign_prefix");
            string playerPrefix = Lang.Get("canclaimsextended:economy_chest_bank_player_sign_prefix");

            if (signText.StartsWith(cityPrefix))
            {
                SetCityBank(handler, splayer, chestPos, signText.Substring(cityPrefix.Length).Trim());
            }
            else if (signText.StartsWith(playerPrefix))
            {
                SetPlayerBank(handler, splayer, chestPos, signText.Substring(playerPrefix.Length).Trim());
            }
        }

        static void SetCityBank(RealMoneyEconomyHandler handler, IServerPlayer player, BlockPos chestPos, string nameOnSign)
        {
            // A plot or the bare land of a city: both belong to the city.
            City city = canclaimsextended.dataStorage.GetPlotAt(chestPos, out Plot plot) ? plot.getCity() : null;
            if (city == null)
            {
                MessageHandler.sendMsgToPlayer(player, Lang.Get("canclaimsextended:economy_there_is_no_city_here"));
                return;
            }
            if (!string.Equals(nameOnSign.Replace("_", " "), city.GetPartName().Replace("_", " "), System.StringComparison.OrdinalIgnoreCase))
            {
                MessageHandler.sendMsgToPlayer(player, Lang.Get("canclaimsextended:economy_different_name_city_sign"));
                return;
            }
            if (!canclaimsextended.dataStorage.GetPlayerByUid(player.PlayerUID, out PlayerInfo playerInfo) || !city.isMayor(playerInfo))
            {
                MessageHandler.sendMsgToPlayer(player, Lang.Get("canclaimsextended:economy_you_need_to_be_mayor"));
                return;
            }

            if (handler.TryGetRealBankInfo(city.MoneyAccountName, out RealBankInfo old) && SamePos(old.getChestCoors(), chestPos))
            {
                MessageHandler.sendMsgToPlayer(player, Lang.Get("canclaimsextended:economy_chest_bank_already_set_here"));
                return;
            }
            EconomyHandler eh = handler;
            // The old row goes even when its chest is gone, or the insert hits the accountname key.
            bool hadChest = eh.accountExist(city.MoneyAccountName);
            eh.deleteAccount(city.MoneyAccountName);
            if (hadChest)
            {
                MessageHandler.sendMsgToPlayer(player, Lang.Get("canclaimsextended:economy_city_chest_bank_removed"));
            }
            eh.newAccount(city.MoneyAccountName, new Dictionary<string, object> { { "chestPos", new Vec3i(chestPos.X, chestPos.Y, chestPos.Z) } });
            MessageHandler.sendMsgToPlayer(player, Lang.Get("canclaimsextended:economy_city_chest_bank_created"));
        }

        static void SetPlayerBank(RealMoneyEconomyHandler handler, IServerPlayer player, BlockPos chestPos, string nameOnSign)
        {
            if (!player.PlayerName.Equals(nameOnSign))
            {
                MessageHandler.sendMsgToPlayer(player, Lang.Get("canclaimsextended:economy_make_only_own_chest"));
                return;
            }
            if (handler.TryGetRealBankInfo(player.PlayerUID, out RealBankInfo old) && (handler as EconomyHandler).accountExist(player.PlayerUID))
            {
                Vec3i c = old.getChestCoors();
                MessageHandler.sendMsgToPlayer(player, SamePos(c, chestPos)
                    ? Lang.Get("canclaimsextended:economy_chest_bank_already_set_here")
                    : Lang.Get("canclaimsextended:economy_chest_bank_already_set_there", c.X, c.Y, c.Z));
                return;
            }
            EconomyHandler eh = handler;
            eh.deleteAccount(player.PlayerUID);
            eh.newAccount(player.PlayerUID, new Dictionary<string, object> { { "chestPos", new Vec3i(chestPos.X, chestPos.Y, chestPos.Z) } });
            MessageHandler.sendMsgToPlayer(player, Lang.Get("canclaimsextended:economy_player_chest_bank_created"));
        }

        static bool SamePos(Vec3i a, BlockPos b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    }
}
