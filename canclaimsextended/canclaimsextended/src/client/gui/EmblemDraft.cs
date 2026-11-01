using System.Collections.Generic;
using canclaimsextended.src.part.structure;

namespace canclaimsextended.src.client.gui
{
    /// <summary>The coat of arms being designed (GuiWindowState.Emblem). Sent only on Apply; until then it lives here.</summary>
    public sealed class EmblemDraft
    {
        public List<string> Layers = new();
        public string Pattern = "";
        public string Color = "";
        /// <summary>False until the player changes something: the draft follows the city's arms till then.</summary>
        public bool Edited;

        public void LoadFrom(string emblem)
        {
            Layers = EmblemHandler.Sanitize(EmblemHandler.Parse(emblem));
            Edited = false;
        }

        public string AsString() => EmblemHandler.Join(Layers);
    }
}
