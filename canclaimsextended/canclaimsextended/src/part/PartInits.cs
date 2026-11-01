using canclaimsextended.src.auxiliary;
using canclaimsextended.src.geometry;
using canclaimsextended.src.messages;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.rights;
using Vintagestory.API.Config;

namespace canclaimsextended.src.part
{
    public static class PartInits
    {
        /// <summary>
        /// Creates the city with its first chunk of territory and no plots. The caller has checked
        /// the chunk already.
        /// </summary>
        public static City initNewCity(PlayerInfo creator, ChunkPos firstChunk, string cityName)
        {
            City city = new City(cityName, System.Guid.NewGuid().ToString());
            canclaimsextended.dataStorage.AddCity(city);
            if (creator != null)
            {
                creator.setCity(city);
                city.setMayor(creator);
                city.getCityCitizens().Add(creator);
            }
            else
            {
                city.setIsTechnicalCity(true);
            }
            city.TimeStampCreated = TimeFunctions.GetEpochSeconds();

            var perms = city.getPermsHandler();
            perms.CitizenPerms[0] = true;
            perms.CitizenPerms[1] = true;
            perms.CitizenPerms[2] = true;

            AddCityChunk(city, firstChunk);
            canclaimsextended.economyProvider.NewAccount(city.MoneyAccountName,
                new System.Collections.Generic.Dictionary<string, object> { { "lastknownname", city.GetPartName() } });
            city.saveToDatabase();
            if (creator != null)
            {
                RightsHandler.reapplyRights(creator);
                creator.saveToDatabase();
            }
            citylog.CityLog.Add(city, citylog.EnumCityLogEvent.CityCreated, creator?.GetPartName() ?? "");
            City.FireCityCreated(city);
            MessageHandler.sendGlobalMsg(Lang.Get("canclaimsextended:new_city_created",
                StringFunctions.ReplaceUnderscore(cityName), creator != null ? creator.GetPartName() : ""));
            return city;
        }

        /// <summary>Adds the chunk to the city's territory. The caller has checked it already.</summary>
        public static CityChunk AddCityChunk(City city, ChunkPos pos, bool extraBought = false, bool isOutpost = false)
        {
            CityChunk chunk = new CityChunk(pos)
            {
                TimeStampClaimed = TimeFunctions.GetEpochSeconds(),
                ExtraBought = extraBought,
                IsOutpost = isOutpost
            };
            if (!canclaimsextended.dataStorage.AddChunk(city, chunk))
            {
                return null;
            }
            canclaimsextended.Db.saveChunk(city, chunk);
            // Anyone already standing in it had an answer cached for the wild.
            canclaimsextended.dataStorage.ResetCacheForAll();
            return chunk;
        }

        /// <summary>Makes the box a plot of the city. The caller has checked the box already.</summary>
        public static Plot AddCityPlot(City city, BlockBox box)
        {
            Plot plot = new Plot(System.Guid.NewGuid().ToString(), new PlotShape(box))
            {
                Type = PlotType.DEFAULT,
                TimeStampClaimed = TimeFunctions.GetEpochSeconds(),
                Number = city.NextPlotNumber()
            };
            plot.setCity(city);
            plot.getPermsHandler().setPerm(city.getPermsHandler());
            canclaimsextended.dataStorage.AddPlot(plot);
            city.getCityPlots().Add(plot);
            plot.saveToDatabase();
            // Anyone already standing in it had the land's answer cached.
            canclaimsextended.dataStorage.ResetCacheForAll();
            return plot;
        }
    }
}
