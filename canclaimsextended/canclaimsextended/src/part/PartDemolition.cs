using System.Linq;
using canclaimsextended.src.delayed.invitations;
using canclaimsextended.src.part.structure;

namespace canclaimsextended.src.part
{
    public static class PartDemolition
    {
        public static void demolishCityPlot(Plot plot)
        {
            // The type lets go of what it registered (a temple point) while the plot still has its city.
            plot.Desc?.OnDeactivated(plot);
            if (plot.hasCity())
            {
                plot.getCity().getCityPlots().Remove(plot);
            }
            if (plot.hasPlotOwner())
            {
                plot.getPlotOwner().PlayerPlots.Remove(plot);
                plot.resetOwner();
            }
            canclaimsextended.dataStorage.RemovePlot(plot);
            canclaimsextended.dataStorage.ClearCacheForPlayersInPlot(plot);
            canclaimsextended.Db.deletePlot(plot);
            plot.Group = null;
            canclaimsextended.Db.deletePlotExtra(plot);
        }

        /// <summary>Takes the chunk out of the city's territory. The caller has checked it already.</summary>
        public static void unclaimCityChunk(City city, geometry.ChunkPos chunk)
        {
            if (!canclaimsextended.dataStorage.RemoveChunk(city, chunk)) return;
            canclaimsextended.Db.deleteChunk(chunk);
            canclaimsextended.dataStorage.ResetCacheForAll();
        }

        public static void demolishCity(City city)
        {
            // What is left in the treasury goes to the mayor rather than vanishing with the account.
            var money = canclaimsextended.economyProvider;
            if (money.SupportsPlayerWallet)
            {
                decimal left = money.GetBalance(city.MoneyAccountName);
                if (left > 0 && city.getMayor() != null)
                {
                    money.Transfer(city.MoneyAccountName, city.getMayor().Guid, left);
                }
                money.DeleteAccount(city.MoneyAccountName);
            }
            PlotsGroupHandler.DeleteAll(city);
            foreach (Plot plot in city.getCityPlots().ToArray())
            {
                demolishCityPlot(plot);
            }
            // The rows go with deleteCity below.
            foreach (CityChunk chunk in city.Chunks.ToArray())
            {
                canclaimsextended.dataStorage.RemoveChunk(city, chunk.Pos);
            }
            canclaimsextended.dataStorage.ResetCacheForAll();
            foreach (PlayerInfo citizen in city.getCityCitizens().ToArray())
            {
                citizen.resetCity();
            }
            city.getCityCitizens().Clear();
            // An invitation from a city that is gone would make its accepter a citizen of nothing.
            InvitationHandler.deleteAllInvitationsForSender(city);
            canclaimsextended.dataStorage.RemoveCityByGUID(city.Guid);
            canclaimsextended.Db.deleteCity(city);
            canclaimsextended.Db.deleteCriminalsOf(city);
            City.FireCityDestroyed(city);
        }
    }
}
