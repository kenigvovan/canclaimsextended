namespace canclaimsextended.src.part
{
    /// <summary>Outcome of a claim check: OK, or a lang key with its arguments saying why not.</summary>
    public class ClaimCheck
    {
        public bool Ok { get; }
        public string LangKey { get; }
        public object[] Args { get; }

        private ClaimCheck(bool ok, string langKey, object[] args)
        {
            Ok = ok;
            LangKey = langKey;
            Args = args;
        }

        public static readonly ClaimCheck Success = new ClaimCheck(true, null, new object[0]);
        public static ClaimCheck Fail(string langKey, params object[] args) => new ClaimCheck(false, langKey, args);
    }
}
