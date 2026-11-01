namespace canclaimsextended.src.client.gui
{
    /// <summary>What the window is showing. Pages and dialogs are stateless; they read and write here.</summary>
    public sealed class GuiWindowState
    {
        public EnumGuiTab Tab = EnumGuiTab.City;
        /// <summary>Rank the rank page is looking at.</summary>
        public string SelectedRank = "";
        /// <summary>Text typed into the secondary window, kept across rebuilds of the window.</summary>
        public string DialogText = "";
        /// <summary>Dropdown pick in the secondary window.</summary>
        public string DialogPick = "";
        /// <summary>Blocks a face of the selection moves per click on the land tab.</summary>
        public int SelectionStep = 1;
        /// <summary>The face buttons of the land tab move the plot part the player stands in, not the selection.</summary>
        public bool FacesMovePlot;
        /// <summary>The prices tab shows how prices are made instead of the plot types.</summary>
        public bool PricesExplainOpen;
        /// <summary>Plots group shown on the right of its tab; dropped when the group is gone.</summary>
        public string SelectedPlotsGroup = "";
        /// <summary>The coat of arms being designed on the emblem tab.</summary>
        public EmblemDraft Emblem = new EmblemDraft();
    }
}
