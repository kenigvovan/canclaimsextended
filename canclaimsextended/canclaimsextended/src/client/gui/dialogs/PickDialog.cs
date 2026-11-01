using System;

namespace canclaimsextended.src.client.gui.dialogs
{
    /// <summary>Prompt, dropdown, one button: picking a permission for a rank, a citizen for a rank.</summary>
    public sealed class PickDialog : GuiDialogPanel
    {
        private readonly string prompt;
        private readonly string[] values;
        private readonly string[] names;
        private readonly string buttonLabel;
        private readonly Action<string> onPicked;

        /// <param name="prompt">Already translated, as are <paramref name="names"/>.</param>
        public PickDialog(string prompt, string[] values, string[] names, string buttonLabel, Action<string> onPicked)
        {
            this.prompt = prompt;
            this.values = values;
            this.names = names;
            this.buttonLabel = buttonLabel;
            this.onPicked = onPicked;
        }

        protected override double Width => 300;

        protected override void BuildContent(DialogLayout l)
        {
            Text(l, prompt);
            if (Array.IndexOf(values, State.DialogPick) < 0) State.DialogPick = values.Length > 0 ? values[0] : "";
            DropDown(l, values, names, code => State.DialogPick = code, State.DialogPick);
            Button(l, buttonLabel, () =>
            {
                string picked = State.DialogPick;
                if (string.IsNullOrEmpty(picked)) return true;
                // Closed first, so onPicked may open the next dialog of a chain.
                Close();
                onPicked(picked);
                return true;
            });
        }
    }
}
