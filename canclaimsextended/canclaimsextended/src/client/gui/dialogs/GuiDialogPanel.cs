using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace canclaimsextended.src.client.gui.dialogs
{
    /// <summary>
    /// One form of the secondary window. The frame, the title bar and composing are the base's job;
    /// a subclass only says what goes inside.
    /// </summary>
    public abstract class GuiDialogPanel
    {
        protected ClaimsExtGui Gui { get; private set; }
        protected GuiWindowState State => Gui.State;

        protected virtual double Width => 260;
        protected virtual string Title => "";

        internal GuiComposer Build(ClaimsExtGui gui, GuiComposer main)
        {
            Gui = gui;
            var layout = DialogLayout.Create(gui.Capi, main, Width);
            BuildContent(layout);
            layout.Compo.AddDialogTitleBar(Title, () => Gui.CloseDialog()).Compose();
            return layout.Compo;
        }

        protected abstract void BuildContent(DialogLayout l);

        protected void Text(DialogLayout l, string text)
        {
            CairoFont font = CairoFont.WhiteDetailText();
            l.FitRowToText(Gui.Capi, text, font);
            l.Compo.AddStaticText(text, font, l.Row);
        }

        protected static void TextInput(DialogLayout l, string key, Action<string> onChange, string initial)
        {
            var bounds = l.NextRow();
            l.Compo.AddTextInput(bounds, onChange, null, key);
            if (!string.IsNullOrEmpty(initial)) l.Compo.GetTextInput(key).SetValue(initial);
        }

        protected static void DropDown(DialogLayout l, string[] values, string[] names, Action<string> onPicked, string selected)
        {
            var bounds = l.NextRow();
            int index = string.IsNullOrEmpty(selected) ? -1 : Array.IndexOf(values, selected);
            l.Compo.AddDropDown(values, names, index, (code, _) => onPicked(code), bounds);
        }

        protected static void Button(DialogLayout l, string label, Func<bool> onClick)
        {
            var bounds = l.NextRow();
            l.Compo.AddButton(label, new ActionConsumable(onClick), bounds, EnumButtonStyle.Normal);
        }

        protected void ConfirmDeclineRow(DialogLayout l, Func<bool> onConfirm)
        {
            ElementBounds yes = l.SplitRow(out ElementBounds no);
            l.Compo.AddButton(Lang.Get("canclaimsextended:gui-confirm"), new ActionConsumable(onConfirm), yes, EnumButtonStyle.Normal);
            l.Compo.AddButton(Lang.Get("canclaimsextended:gui-cancel"), new ActionConsumable(() =>
            {
                Close();
                return true;
            }), no, EnumButtonStyle.Normal);
        }

        protected void Close() => Gui.CloseDialog();
    }
}
