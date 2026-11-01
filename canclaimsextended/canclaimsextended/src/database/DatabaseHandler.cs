using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;

namespace canclaimsextended.src.database
{
    public abstract class DatabaseHandler
    {
        abstract public bool savePlayerInfo(PlayerInfo player, bool update = true);
        abstract public bool deletePlayerInfo(PlayerInfo player);
        abstract public bool saveCity(City city, bool update = true);
        abstract public bool deleteCity(City city);
        abstract public bool savePlot(Plot plot, bool update = true);
        abstract public bool deletePlot(Plot plot);
        abstract public bool saveWorldInfo(WorldInfo world);
        abstract public bool saveInvitation(delayed.invitations.Invitation invitation);
        abstract public bool deleteInvitation(delayed.invitations.Invitation invitation);
        /// <summary>Writes the player's sentence, or removes it when the player is free.</summary>
        abstract public bool savePrisoner(PlayerInfo player);
        abstract public bool saveCriminal(City city, string uid);
        abstract public bool deleteCriminal(City city, string uid);
        abstract public bool deleteCriminalsOf(City city);
        /// <summary>Appends one line to the city's stored history.</summary>
        abstract public bool saveCityLogEntry(City city, part.citylog.CityLogEntry entry);
        /// <summary>The group of a plot; kept apart from PLOTS.</summary>
        abstract public bool savePlotExtra(Plot plot);
        abstract public bool deletePlotExtra(Plot plot);
        /// <summary>One chunk of the city's territory with its marks.</summary>
        abstract public bool saveChunk(City city, CityChunk chunk);
        abstract public bool deleteChunk(geometry.ChunkPos chunk);
        /// <summary>The group with its members.</summary>
        abstract public bool savePlotsGroup(PlotsGroup group);
        abstract public bool deletePlotsGroup(PlotsGroup group);

        /// <summary>Loads everything into canclaimsextended.dataStorage. False if anything failed.</summary>
        abstract public bool loadEverything();
        /// <summary>Writes out every pending change right away.</summary>
        abstract public void saveEverything();
        /// <summary>A copy of the database with every pending change in it, written to path.</summary>
        abstract public bool makeBackup(string path);
        abstract public void close();
    }
}
