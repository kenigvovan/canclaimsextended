using System.Collections.Generic;

namespace canclaimsextended.src.part.interfaces
{
    public interface IGetStatus
    {
        List<string> getStatus(PlayerInfo forPlayer);
    }
}
