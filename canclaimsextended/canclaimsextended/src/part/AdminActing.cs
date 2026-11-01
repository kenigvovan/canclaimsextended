using System;

namespace canclaimsextended.src.part
{
    /// <summary>
    /// /cadmin as &lt;city&gt; &lt;command&gt;: while that one command runs, the admin counts as the mayor of
    /// the city, the city is theirs for the city and plot commands, nothing costs money and the
    /// limits of plots and cities do not apply. Set and cleared around a synchronous call on the
    /// server thread, so nothing else - timers, fees, other players, a later /agree - ever sees it.
    /// </summary>
    public static class AdminActing
    {
        [ThreadStatic] static PlayerInfo actor;
        [ThreadStatic] static City city;

        /// <summary>A command runs under /cadmin as right now.</summary>
        public static bool Active => actor != null;

        /// <summary>The player is the admin of the command running under /cadmin as.</summary>
        public static bool IsActor(PlayerInfo playerInfo) => actor != null && actor.Equals(playerInfo);

        /// <summary>The player is the admin acting for this city in the running command.</summary>
        public static bool IsActingFor(PlayerInfo playerInfo, City c)
        {
            return c != null && IsActor(playerInfo) && c.Equals(city);
        }

        /// <summary>The city the player's commands are about: the acted-for one, else their own.</summary>
        public static City CityOf(PlayerInfo playerInfo)
        {
            return IsActor(playerInfo) ? city : playerInfo?.City;
        }

        /// <summary>Runs the action with the admin acting for the city. Does not nest.</summary>
        public static T Run<T>(PlayerInfo admin, City c, Func<T> action)
        {
            if (admin == null || c == null) throw new ArgumentNullException(admin == null ? nameof(admin) : nameof(c));
            if (actor != null) throw new InvalidOperationException("/cadmin as does not nest.");
            actor = admin;
            city = c;
            try
            {
                return action();
            }
            finally
            {
                actor = null;
                city = null;
            }
        }
    }
}
