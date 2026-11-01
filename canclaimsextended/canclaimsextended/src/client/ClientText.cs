namespace canclaimsextended.src.client
{
    /// <summary>Text the client shows that it does not get translated.</summary>
    public static class ClientText
    {
        /// <summary>A city or group name as players read it: names keep underscores for spaces.</summary>
        public static string Pretty(string name) => (name ?? "").Replace("_", " ");
    }
}
