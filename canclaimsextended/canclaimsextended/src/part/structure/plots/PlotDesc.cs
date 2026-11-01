namespace canclaimsextended.src.part.structure.plots
{
    /// <summary>
    /// What a plot type adds to a plot: its own data (kept in the plotdesc column) and what has to
    /// happen when a plot becomes or stops being of that type. The plain type has none of it.
    /// </summary>
    public class PlotDesc
    {
        public virtual string Serialize() => "";
        public virtual void Deserialize(string data) { }

        /// <summary>Whether this plot may take the type; the reason when it may not.</summary>
        public virtual ClaimCheck Validate(Plot plot) => ClaimCheck.Success;

        /// <summary>The plot has just become of this type.</summary>
        public virtual void OnActivated(Plot plot) { }

        /// <summary>The plot stops being of this type, or is gone.</summary>
        public virtual void OnDeactivated(Plot plot) { }
    }
}
