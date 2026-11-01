using canclaimsextended.src.part.structure;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace canclaimsextended.src.events
{
    public static class OnTryEntitySpawn
    {
        /// <summary>Hostile mobs do not spawn in plots with the nomobspawn flag.</summary>
        public static bool Event_OnTrySpawnEntity(IBlockAccessor blockAccessor, ref EntityProperties properties, Vec3d spawnPosition, long herdId)
        {
            // Group may be null: many creatures have runtime spawn conditions without one.
            bool hostile = properties?.Server?.SpawnConditions?.Runtime?.Group == "hostile";
            if (!hostile || spawnPosition == null || canclaimsextended.dataStorage == null)
            {
                return true;
            }
            // World flags win over plot flags: forced on, then forbidden, then what the land says.
            WorldInfo world = canclaimsextended.dataStorage.World;
            if (world.mobSpawnEverywhere)
            {
                return true;
            }
            if (world.mobSpawnForbidden)
            {
                return false;
            }
            if (canclaimsextended.dataStorage.GetPlotAt(spawnPosition, out Plot plot))
            {
                return !plot.getPermsHandler().noMobSpawnFlag;
            }
            return true;
        }
    }
}
