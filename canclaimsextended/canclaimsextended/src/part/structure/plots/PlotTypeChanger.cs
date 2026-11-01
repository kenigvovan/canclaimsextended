namespace canclaimsextended.src.part.structure.plots
{
    /// <summary>Checks and applies a plot type change; the command only adds who pays.</summary>
    public static class PlotTypeChanger
    {
        public static ClaimCheck Check(Plot plot, string code, out PlotInfo info)
        {
            if (!PlotTypes.TryGet(code, out info))
            {
                return ClaimCheck.Fail("canclaimsextended:unknown_plot_type", string.Join(", ", PlotTypes.EnabledCodes));
            }
            if (PlotTypes.IsDisabled(code))
            {
                return ClaimCheck.Fail("canclaimsextended:plot_type_disabled");
            }
            if (info.Type == plot.Type)
            {
                return ClaimCheck.Fail("canclaimsextended:plot_type_same");
            }
            return info.CreateDesc().Validate(plot);
        }

        /// <summary>Sets the type without checks: the caller has run Check.</summary>
        public static void Apply(Plot plot, PlotInfo info)
        {
            plot.Desc?.OnDeactivated(plot);
            PlotDesc desc = info.CreateDesc();
            plot.Type = info.Type;
            plot.Desc = desc;
            desc.OnActivated(plot);
            plot.saveToDatabase();
            canclaimsextended.dataStorage.NotifyPlotChanged(plot);
        }
    }
}
