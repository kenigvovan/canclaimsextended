using System;
using canclaimsextended.src.agreement;
using canclaimsextended.src.economy;
using canclaimsextended.src.messages;
using canclaimsextended.src.part;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace canclaimsextended.src.commands
{
    /// <summary>
    /// Something that costs money: done at once and for free without an economy, otherwise after
    /// /agree, when everything is checked again before the money is taken.
    /// </summary>
    public static class PaidAction
    {
        public static bool IsFree => !canclaimsextended.economyProvider.SupportsPlayerWallet;

        /// <summary>
        /// What the city pays for: nothing without an economy, nothing for a technical (admin) city,
        /// nothing under /cadmin as - an /agree would come after the admin stopped acting.
        /// </summary>
        public static bool IsFreeFor(City city) => IsFree || AdminActing.Active || (city != null && city.isTechnicalCity());

        /// <summary>An error with the price and the balance when the account cannot pay, null otherwise.</summary>
        public static TextCommandResult CheckFunds(string account, decimal cost, string langKey)
        {
            decimal balance = canclaimsextended.economyProvider.GetBalance(account);
            return balance < cost ? BaseCommand.ErrorWithParams(langKey, cost, balance) : null;
        }

        /// <summary>
        /// Arms the /agree. On agree: recheck (a message when it no longer holds), then the payment,
        /// then apply, then the message from done.
        /// </summary>
        public static void AskToConfirm(IServerPlayer player, string account, decimal cost,
            Func<string> recheck, Action apply, Func<string> done)
        {
            AgreementHandler.addNewAgreementOrReplace(new Agreement(() =>
            {
                string problem = recheck();
                if (problem != null)
                {
                    MessageHandler.sendMsgToPlayer(player, problem);
                    return;
                }
                if (canclaimsextended.economyProvider.Withdraw(account, cost) != MoneyOperationResult.Success)
                {
                    MessageHandler.sendMsgToPlayer(player, Lang.Get("canclaimsextended:economy_money_transaction_error"));
                    return;
                }
                apply();
                MessageHandler.sendMsgToPlayer(player, done());
            }, player.PlayerUID));
        }

        public static string Outdated()
        {
            return Lang.Get("canclaimsextended:agreement_outdated");
        }

        /// <summary>The "cannot be claimed" message for a failed check, null for a passed one.</summary>
        public static string Problem(ClaimCheck check)
        {
            return check.Ok ? null : Lang.Get("canclaimsextended:selection_cannot_claim", Lang.Get(check.LangKey, check.Args));
        }
    }
}
