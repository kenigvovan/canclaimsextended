using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.delayed.invitations;
using canclaimsextended.src.part.interfaces;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.perms;
using canclaimsextended.src.rights;
using Vintagestory.API.Config;

namespace canclaimsextended.src.part
{
    public class PlayerInfo : Part, IGetStatus, IReceiver
    {
        List<Invitation> receivedInvitations = new List<Invitation>();
        public long TimeStampLasOnline { get; set; }
        public long TimeStampFirstJoined { get; set; }
        public City City { get; private set; }
        public HashSet<PlayerInfo> Friends { get; set; }
        public HashSet<Plot> PlayerPlots { get; set; }
        public PermsHandler PermsHandler { get; set; }
        public PlayerCache PlayerCache { get; set; }
        public PlayerPermissions PlayerPermissionsHandler { get; set; }
        // Names of the city ranks this player holds.
        HashSet<string> cityTitles = new HashSet<string>();
        /// <summary>Set by the mayor (CITY_SET_OTHERS_PREFIX), shown around the name in general chat.</summary>
        public string Prefix { get; set; } = "";
        public string AfterName { get; set; } = "";
        /// <summary>Guid of the prison plot the player serves in; empty when free. See PrisonHandler.</summary>
        public string PrisonPlotGuid { get; set; } = "";
        /// <summary>Unix seconds when the sentence ends.</summary>
        public long PrisonReleaseAt { get; set; }
        public bool isPrisoned() => PrisonPlotGuid.Length > 0 && PrisonPlotGuid != PrisonHandler.RELEASE_ON_LOGIN;

        public PlayerInfo(string val, string uid) : base(val, uid)
        {
            PermsHandler = new PermsHandler();
            Friends = new HashSet<PlayerInfo>();
            PlayerPlots = new HashSet<Plot>();
            PlayerCache = new PlayerCache();
            PlayerPermissionsHandler = new PlayerPermissions();
        }

        public void setCity(City city)
        {
            // A mayor moving to another city must not leave the old one pointing at them.
            // The mayor himself: an admin acting for the city (AdminActing) is not its mayor.
            if (this.City != null && !this.City.Equals(city) && this.Equals(this.City.getMayor()))
            {
                this.City.setMayor(null);
            }
            this.City = city;
            RightsHandler.reapplyRights(this);
        }
        public void resetCity()
        {
            if (City != null && this.Equals(City.getMayor()))
            {
                City.setMayor(null);
            }
            // Ranks belong to the city left behind.
            if (City != null && cityTitles.Count > 0)
            {
                foreach (string title in cityTitles)
                {
                    City.RevokePlayerRank(title, this);
                }
                City.saveToDatabase();
            }
            cityTitles.Clear();
            // Titles were given by the city left behind.
            Prefix = "";
            AfterName = "";
            City = null;
            RightsHandler.reapplyRights(this);
            this.saveToDatabase();
        }
        /// <summary>Leaves the city: off its citizens list, and plots owned there go back to the city.</summary>
        public void leaveCity()
        {
            City city = City;
            if (city == null) return;
            city.getCityCitizens().Remove(this);
            PlotsGroupHandler.RemoveFromCityGroups(this, city);
            var returned = PlayerPlots.Where(p => city.Equals(p.getCity())).ToList();
            foreach (Plot plot in returned)
            {
                plot.ReturnToCity();
                plot.saveToDatabase();
                canclaimsextended.dataStorage.NotifyPlotChanged(plot);
                PlayerPlots.Remove(plot);
            }
            // The plots now have the city's rights: answers cached for others are stale.
            if (returned.Count > 0) canclaimsextended.dataStorage.ResetCacheForAll();
            city.saveToDatabase();
            resetCity();
            PlayerCache.setPlot(null);
        }
        public bool hasCity()
        {
            return City != null;
        }
        public void addCityTitle(string title)
        {
            cityTitles.Add(title);
        }
        public void removeCityTitle(string title)
        {
            cityTitles.Remove(title);
        }
        public bool hasCityTitle(string title)
        {
            return cityTitles.Contains(title);
        }
        public HashSet<string> getCityTitles()
        {
            return cityTitles;
        }
        public bool removeComrade(PlayerInfo val)
        {
            return val != null && Friends.Remove(val);
        }
        public bool addComrade(PlayerInfo val)
        {
            return val != null && Friends.Add(val);
        }
        public override bool saveToDatabase(bool update = true)
        {
            return canclaimsextended.Db.savePlayerInfo(this, update);
        }

        // IReceiver: invitations to cities.
        public List<Invitation> getReceivedInvitations() => receivedInvitations;
        public void deleteReceivedInvitation(Invitation invitation) => receivedInvitations.Remove(invitation);
        public void addReceivedInvitation(Invitation invitation) => receivedInvitations.Add(invitation);
        public int getMaxReceivedInvitations() => canclaimsextended.config.MAX_RECEIVED_INVITATIONS_PLAYER;
        public string getNameReceiver() => GetPartName();
        public List<string> getStatus(PlayerInfo forPlayer = null)
        {
            List<string> status = new List<string>
            {
                Lang.Get("canclaimsextended:last_online", TimeFunctions.GetDateFromEpochSeconds(TimeStampLasOnline)) + "\n",
                Lang.Get("canclaimsextended:first_joined", TimeFunctions.GetDateFromEpochSeconds(TimeStampFirstJoined)) + "\n"
            };
            if (City != null)
            {
                status.Add(Lang.Get("canclaimsextended:city") + City.GetPartName() + "\n");
            }
            return status;
        }

        // Identity is the player UID, as for City.
        public override int GetHashCode()
        {
            return Guid?.GetHashCode() ?? 0;
        }
        public override bool Equals(object obj)
        {
            return obj is PlayerInfo other && string.Equals(this.Guid, other.Guid);
        }
    }
}
