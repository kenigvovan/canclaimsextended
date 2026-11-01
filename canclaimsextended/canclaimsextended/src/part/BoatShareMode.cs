namespace canclaimsextended.src.part
{
    /// <summary>
    /// Who, besides the owner, may use a boat. Lives on the boat, in WatchedAttributes: it persists
    /// with the entity, syncs to nearby clients, and is changed only next to that boat.
    /// </summary>
    public enum BoatShareMode
    {
        /// <summary>Vanilla behaviour: the owner alone.</summary>
        PERSONAL = 0,
        /// <summary>Everyone in the owner's city.</summary>
        CITY = 1
    }
}
