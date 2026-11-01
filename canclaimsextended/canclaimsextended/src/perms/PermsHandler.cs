using System.Text;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.perms.type;
using Vintagestory.API.Config;

namespace canclaimsextended.src.perms
{
    public class PermsHandler
    {
        /// <summary>
        /// pvpFlag - players can attack each other here.
        /// fireFlag - fire can start and spread here regardless of who lit it.
        /// blastFlag - blasts are allowed.
        /// noMobSpawnFlag - hostile mobs do not spawn here.
        /// </summary>
        public bool pvpFlag { get; set; }
        public bool fireFlag { get; set; }
        public bool blastFlag { get; set; }
        public bool noMobSpawnFlag { get; set; }
        public bool[] CitizenPerms { get; set; }
        public bool[] StrangerPerms { get; set; }
        public bool[] ComradePerms { get; set; }

        // Stored names of PermType values, by index.
        static readonly string[] TypeNames = Codes.PermTypes;

        public PermsHandler()
        {
            CitizenPerms = new bool[3];
            StrangerPerms = new bool[3];
            ComradePerms = new bool[3];
        }
        /// <summary>The flags and the rights table as /city set info and /plot here print them.</summary>
        public string getStringForChat()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Lang.Get("canclaimsextended:perms-chat-flags", OnOff(pvpFlag), OnOff(fireFlag), OnOff(blastFlag), OnOff(noMobSpawnFlag))).Append("\n");
            sb.Append(Lang.Get("canclaimsextended:perms-chat-types")).Append("\n");
            AppendGroup(sb, Lang.Get("canclaimsextended:perms-chat-citizen"), CitizenPerms);
            sb.Append("\n");
            AppendGroup(sb, Lang.Get("canclaimsextended:perms-chat-stranger"), StrangerPerms);
            sb.Append("\n");
            AppendGroup(sb, Lang.Get("canclaimsextended:perms-chat-friend"), ComradePerms);
            return sb.ToString();
        }

        // The words the commands take, so the output can be typed back.
        static string OnOff(bool on) => on ? "on" : "off";
        private static void AppendGroup(StringBuilder sb, string name, bool[] perms)
        {
            sb.Append(name);
            foreach (bool p in perms)
            {
                sb.Append(p ? "+" : "-");
            }
        }
        public void setPerm(PermsHandler copyFrom)
        {
            pvpFlag = copyFrom.pvpFlag;
            fireFlag = copyFrom.fireFlag;
            blastFlag = copyFrom.blastFlag;
            noMobSpawnFlag = copyFrom.noMobSpawnFlag;
            CitizenPerms = (bool[])copyFrom.CitizenPerms.Clone();
            StrangerPerms = (bool[])copyFrom.StrangerPerms.Clone();
            ComradePerms = (bool[])copyFrom.ComradePerms.Clone();
        }
        public bool getPerm(PermGroup group, PermType type)
        {
            return Row(group)[(int)type];
        }
        public void setPerm(PermGroup group, PermType type, bool val)
        {
            Row(group)[(int)type] = val;
        }
        bool[] Row(PermGroup group)
        {
            switch (group)
            {
                case PermGroup.CITIZEN: return CitizenPerms;
                case PermGroup.COMRADE: return ComradePerms;
                default: return StrangerPerms;
            }
        }
        public static bool TryParseGroup(string str, out PermGroup group)
        {
            switch (str)
            {
                case "stranger": group = PermGroup.STRANGER; return true;
                case "citizen": group = PermGroup.CITIZEN; return true;
                case "friend": group = PermGroup.COMRADE; return true;
            }
            group = PermGroup.STRANGER;
            return false;
        }
        public static bool TryParseType(string str, out PermType type)
        {
            switch (str)
            {
                case "use": type = PermType.USE_PERM; return true;
                case "build": type = PermType.BUILD_AND_DESTROY_PERM; return true;
                case "attack": type = PermType.ATTACK_ANIMALS_PERM; return true;
            }
            type = PermType.USE_PERM;
            return false;
        }
        /// <summary>Sets one of the flags by its chat name: pvp, fire, blast, nomobspawn.</summary>
        public bool trySetFlag(string flag, bool val)
        {
            switch (flag)
            {
                case "pvp": pvpFlag = val; return true;
                case "fire": fireFlag = val; return true;
                case "blast": blastFlag = val; return true;
                case "nomobspawn": noMobSpawnFlag = val; return true;
            }
            return false;
        }
        /// <summary>Parses the string written by <see cref="ToString"/>; unknown groups (an old "ally:") are skipped.</summary>
        public void setPerms(string loadedStr)
        {
            pvpFlag = false;
            fireFlag = false;
            blastFlag = false;
            noMobSpawnFlag = false;
            foreach (string it in loadedStr.Split(';'))
            {
                if (it.Length == 0)
                    continue;

                if (it.Contains(":"))
                {
                    string[] parts = it.Split(':');
                    if (TryParseGroup(parts[0], out PermGroup group) && TryParseType(parts[1], out PermType type))
                    {
                        setPerm(group, type, true);
                    }
                }
                else
                {
                    trySetFlag(it, true);
                }
            }
        }
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            if (pvpFlag) sb.Append("pvp;");
            if (fireFlag) sb.Append("fire;");
            if (blastFlag) sb.Append("blast;");
            if (noMobSpawnFlag) sb.Append("nomobspawn;");
            AppendStored(sb, "stranger", StrangerPerms);
            AppendStored(sb, "citizen", CitizenPerms);
            AppendStored(sb, "friend", ComradePerms);
            return sb.ToString();
        }
        private static void AppendStored(StringBuilder sb, string group, bool[] perms)
        {
            for (int i = 0; i < perms.Length; i++)
            {
                if (perms[i])
                {
                    sb.Append(group).Append(':').Append(TypeNames[i]).Append(';');
                }
            }
        }
    }
}
