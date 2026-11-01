namespace canclaimsextended.src.part
{
    public abstract class Part
    {
        private string partName;
        public string Guid { get; set; }

        public Part(string val, string guid)
        {
            this.partName = val;
            this.Guid = guid;
        }

        public string getPartNameReplaceUnder()
        {
            return auxiliary.StringFunctions.ReplaceUnderscore(partName);
        }
        public string GetPartName()
        {
            return partName;
        }
        public bool SetPartName(string val)
        {
            if (partName.Equals(val))
            {
                return false;
            }
            this.partName = val;
            return true;
        }
        abstract public bool saveToDatabase(bool update = true);
    }
}
