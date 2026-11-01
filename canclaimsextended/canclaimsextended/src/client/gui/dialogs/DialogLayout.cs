using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace canclaimsextended.src.client.gui.dialogs
{
    /// <summary>The secondary window being built and a cursor that walks down it one row at a time.</summary>
    public sealed class DialogLayout
    {
        public const string ComposerKey = "canclaimsextendedgui-upper";

        public GuiComposer Compo;
        public ElementBounds Bg;
        public ElementBounds Row;

        /// <summary>Grows the current row to hold the text in full, so the next row starts below it.</summary>
        public void FitRowToText(ICoreClientAPI capi, string text, CairoFont font)
        {
            if (string.IsNullOrEmpty(text)) return;
            double width = Row.fixedWidth > 0 ? Row.fixedWidth : 180;
            // Measured in screen pixels, as the font is scaled.
            double height = capi.Gui.Text.GetMultilineTextHeight(font, text, GuiElement.scaled(width)) / RuntimeEnv.GUIScale;
            if (height > Row.fixedHeight) Row.fixedHeight = height;
        }

        /// <summary>Next row down, already registered as a child of the background.</summary>
        public ElementBounds NextRow(double gap = 15)
        {
            ElementBounds next = Row.BelowCopy(0, gap);
            Bg.WithChildren(next);
            Row = next;
            return next;
        }

        /// <summary>A row split into two halves, for confirm/decline pairs.</summary>
        public ElementBounds SplitRow(out ElementBounds right, double gap = 15)
        {
            ElementBounds left = NextRow(gap);
            left.fixedWidth /= 2;
            right = left.RightCopy(0, 0);
            Bg.WithChildren(right);
            return left;
        }

        /// <summary>The window to the right of the main one, as in claims.</summary>
        public static DialogLayout Create(ICoreClientAPI capi, GuiComposer main, double width)
        {
            ElementBounds leftDlgBounds = main.Bounds;

            ElementBounds bgBounds = ElementBounds.Fixed(0.0, 0.0, width, 120).WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;

            ElementBounds dialogBounds = bgBounds.ForkBoundingParent(0.0, 0.0, 0.0, 0.0)
                .WithAlignment(EnumDialogArea.None)
                .WithFixedAlignmentOffset((leftDlgBounds.renderX + leftDlgBounds.OuterWidth + 10.0) / RuntimeEnv.GUIScale,
                                          leftDlgBounds.renderY / RuntimeEnv.GUIScale);
            dialogBounds.BothSizing = ElementSizing.FitToChildren;
            dialogBounds.WithChild(bgBounds);

            ElementBounds textBounds = ElementBounds.FixedPos(EnumDialogArea.LeftTop, 0, 0);
            bgBounds.WithChildren(textBounds);

            var compo = capi.Gui.CreateCompo(ComposerKey, dialogBounds).AddShadedDialogBG(bgBounds, false, 5.0, 0.75f);

            ElementBounds firstRow = textBounds.CopyOffsetedSibling().WithFixedSize(width - 20, 30);
            bgBounds.WithChildren(firstRow);
            firstRow.fixedY += 20;

            return new DialogLayout { Compo = compo, Bg = bgBounds, Row = firstRow };
        }
    }
}
