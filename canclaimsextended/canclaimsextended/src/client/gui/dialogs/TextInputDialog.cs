using System;
using System.Globalization;
using Vintagestory.API.Config;

namespace canclaimsextended.src.client.gui.dialogs
{
    /// <summary>Prompt, text field, one button - as NeedNameDialog in claims.</summary>
    public sealed class TextInputDialog : GuiDialogPanel
    {
        private const string InputKey = "dialog-text-input";

        private readonly string prompt;
        private readonly string buttonLabel;
        private readonly Action<string> onSubmit;
        private readonly bool integerOnly;

        /// <param name="prompt">Already translated.</param>
        /// <param name="onSubmit">Gets the typed text, trimmed; usually runs a command with it.</param>
        public TextInputDialog(string prompt, string buttonLabel, Action<string> onSubmit, bool integerOnly = false)
        {
            this.prompt = prompt;
            this.buttonLabel = buttonLabel;
            this.onSubmit = onSubmit;
            this.integerOnly = integerOnly;
        }

        protected override void BuildContent(DialogLayout l)
        {
            Text(l, prompt);
            TextInput(l, InputKey, value => State.DialogText = value, State.DialogText);

            Button(l, buttonLabel, () =>
            {
                string typed = (State.DialogText ?? "").Trim();
                if (typed.Length == 0) return true;
                if (integerOnly && !int.TryParse(typed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    Gui.Capi.ShowChatMessage(Lang.Get("canclaimsextended:gui-not-a-number"));
                    return true;
                }
                onSubmit(typed);
                Close();
                return true;
            });
        }
    }
}
