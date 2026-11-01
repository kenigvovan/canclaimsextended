using System.Collections.Generic;
using canclaimsextended.src.rights;

namespace canclaimsextended.src.part.structure
{
    /// <summary>A rank of one city: a set of rights its mayor hands to citizens by name.</summary>
    public class CustomCityRank
    {
        public string Name { get; set; }
        public HashSet<EnumPlayerPermissions> Permissions { get; set; } = new();
        public HashSet<string> CitizensNames { get; set; } = new();

        public override bool Equals(object obj)
        {
            return obj is CustomCityRank other && Name == other.Name;
        }
        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }
    }
}
