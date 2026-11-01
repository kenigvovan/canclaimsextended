using System.Collections.Generic;

namespace canclaimsextended.src.rights
{
    public class PlayerPermissions
    {
        HashSet<EnumPlayerPermissions> permissions = new HashSet<EnumPlayerPermissions>();
        public bool HasPermission(EnumPlayerPermissions permission)
        {
            return permissions.Contains(permission);
        }
        public bool AddPermission(EnumPlayerPermissions permission)
        {
            return permissions.Add(permission);
        }
        public void AddPermissions(HashSet<EnumPlayerPermissions> newPermissions)
        {
            permissions.UnionWith(newPermissions);
        }
        public void ClearPermissions()
        {
            permissions.Clear();
        }
        public IReadOnlyCollection<EnumPlayerPermissions> GetPermissions()
        {
            return permissions;
        }
    }
}
