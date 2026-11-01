using System;
using System.Collections.Generic;
using Vintagestory.API.Client;

namespace canclaimsextended.src.client.gui.pages
{
    /// <summary>Everything a page needs for one rebuild, and nothing that outlives it.</summary>
    public sealed class PageBuildContext
    {
        /// <summary>The composer being built. Pages add elements but never compose it.</summary>
        public GuiComposer Compo;
        /// <summary>The separator under the tab bar, for full-width sizing.</summary>
        public ElementBounds Line;
        /// <summary>The window body, for height calculations.</summary>
        public ElementBounds Main;

        private readonly List<Action> after = new List<Action>();

        /// <summary>Work that needs the composed layout, such as setting switch values.</summary>
        public void AfterCompose(Action action) => after.Add(action);

        internal void RunAfterCompose()
        {
            foreach (var action in after) action();
            after.Clear();
        }
    }
}
