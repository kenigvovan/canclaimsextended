using canclaimsextended.src.agreement;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace canclaimsextended.src.commands
{
    /// <summary>/agree: confirms the last action that asked for it.</summary>
    public class AgreeCommand : BaseCommand
    {
        public static TextCommandResult OnCommand(TextCommandCallingArgs args)
        {
            if (AgreementHandler.agreeFor(args.Caller.Player as IServerPlayer))
            {
                return TextCommandResult.Success();
            }
            return TextCommandResult.Error("canclaimsextended:no_agreements_awaits");
        }
    }
}
