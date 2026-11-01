using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace canclaimsextended.src.client.gui.widgets
{
    /// <summary>
    /// The framed block the pages are built of: a heading, a rule, then rows. Taken over from claims
    /// so the new mod looks the same; item lists replace the claims cell lists.
    /// </summary>
    public static class Card
    {
        public const double LineHeight = 17;
        /// <summary>A list row is taller than a caption row: it carries icon buttons.</summary>
        public const double ItemHeight = 22;
        private const double ItemButtonSize = 20;

        private const double HeaderTextHeight = 20;
        private const double HeaderRuleGap = 5;
        private const double RuleBottomGap = 6;
        public const double HeaderHeight = HeaderTextHeight + HeaderRuleGap + 2 + RuleBottomGap;

        public const double Padding = 8;
        public const double Gap = 12;
        public const double ColumnGap = 18;
        public const double ValueWidth = 66;
        public const double ActionSize = 30;
        public const double ActionGap = 7;

        public static double HeightFor(int rowCount) => HeaderHeight + rowCount * LineHeight + Padding * 2;

        /// <summary>Draws the frame and its heading, returns the area inside it.</summary>
        public static ElementBounds Frame(GuiComposer compo, ElementBounds column, double y, double height, string title)
        {
            var card = column.FlatCopy();
            card.fixedY = y;
            card.fixedHeight = height;
            compo.AddInset(card, 2);

            double innerWidth = column.fixedWidth - Padding * 2;
            var inner = column.FlatCopy().WithFixedSize(innerWidth, height - Padding * 2);
            inner.fixedX += Padding;
            inner.fixedY = y + Padding;

            if (title != null)
            {
                var header = inner.FlatCopy().WithFixedHeight(HeaderTextHeight);
                var headerFont = CairoFont.WhiteSmallishText().WithColor(GuiColors.Section);
                // A long heading would wrap onto the rule and the first row.
                string shown = Fit(title, headerFont, innerWidth);
                compo.AddStaticText(shown, headerFont, header, UniqueKey(compo, "sec-" + title));
                if (shown != title) Tooltip.Add(compo, title, header, UniqueKey(compo, "sectip-" + title));

                var rule = header.BelowCopy(0, HeaderRuleGap).WithFixedHeight(2);
                compo.AddInset(rule);

                inner.fixedY += HeaderHeight;
                inner.fixedHeight -= HeaderHeight;
            }
            return inner;
        }

        /// <summary>A key no element on this composer holds yet: a repeated key makes the composer throw.</summary>
        public static string UniqueKey(GuiComposer compo, string key)
        {
            string result = key;
            for (int i = 2; compo.GetElement(result) != null; i++)
            {
                result = key + "-" + i;
            }
            return result;
        }

        /// <summary>A card of caption/value rows. Returns the y the next card starts at.</summary>
        public static double Rows(GuiComposer compo, ElementBounds column, double y, string title, List<CardRow> rows)
        {
            double height = HeightFor(rows.Count);
            ElementBounds inner = Frame(compo, column, y, height, title);
            DrawRows(compo, inner, rows);
            return y + height + Gap;
        }

        /// <summary>Rows with a strip of icon buttons under them; <paramref name="actions"/> gets the first slot.</summary>
        public static double RowsWithActions(GuiComposer compo, ElementBounds column, double y, string title,
                                             List<CardRow> rows, Action<ElementBounds> actions)
        {
            double height = HeightFor(rows.Count) + ActionGap + ActionSize;
            ElementBounds inner = Frame(compo, column, y, height, title);
            DrawRows(compo, inner, rows);

            var slot = inner.FlatCopy().WithFixedSize(ActionSize, ActionSize);
            slot.fixedY = inner.fixedY + rows.Count * LineHeight + ActionGap;
            actions(slot);
            return y + height + Gap;
        }

        /// <summary>
        /// A list card: one line per entry with its own buttons, an empty-state text when there is
        /// nothing, and an optional action strip at the bottom (null for none).
        /// </summary>
        public static double Items(GuiComposer compo, ElementBounds column, double y, string title,
                                   List<ItemRow> items, string emptyText, Action<ElementBounds> actions = null)
        {
            int lines = Math.Max(1, items.Count);
            double height = HeaderHeight + lines * ItemHeight + Padding * 2;
            if (actions != null) height += ActionGap + ActionSize;
            ElementBounds inner = Frame(compo, column, y, height, title);

            if (items.Count == 0)
            {
                var empty = inner.FlatCopy().WithFixedHeight(ItemHeight);
                compo.AddStaticText(emptyText ?? "", CairoFont.WhiteSmallText().WithColor(GuiColors.Label),
                    empty, UniqueKey(compo, "empty-" + title));
            }

            for (int i = 0; i < items.Count; i++)
            {
                DrawItem(compo, inner, items[i], inner.fixedY + i * ItemHeight);
            }

            if (actions != null)
            {
                var slot = inner.FlatCopy().WithFixedSize(ActionSize, ActionSize);
                slot.fixedY = inner.fixedY + lines * ItemHeight + ActionGap;
                actions(slot);
            }
            return y + height + Gap;
        }

        /// <summary>A card holding one block of wrapped text, such as a hint.</summary>
        public static double Note(GuiComposer compo, ElementBounds column, double y, string title, string text,
                                  Action<ElementBounds> actions = null)
        {
            CairoFont font = CairoFont.WhiteSmallText().WithColor(GuiColors.Label);
            double width = column.fixedWidth - Padding * 2;
            // Measured in screen pixels, as the font is scaled; the card works in unscaled units.
            double textHeight = compo.Api.Gui.Text.GetMultilineTextHeight(font, text, GuiElement.scaled(width)) / RuntimeEnv.GUIScale + 4;

            double height = HeaderHeight + textHeight + Padding * 2;
            if (actions != null) height += ActionGap + ActionSize;
            ElementBounds inner = Frame(compo, column, y, height, title);

            var textBounds = inner.FlatCopy().WithFixedHeight(textHeight);
            compo.AddStaticText(text, font, textBounds, UniqueKey(compo, "note-" + title));

            if (actions != null)
            {
                var slot = inner.FlatCopy().WithFixedSize(ActionSize, ActionSize);
                slot.fixedY = inner.fixedY + textHeight + ActionGap;
                actions(slot);
            }
            return y + height + Gap;
        }

        public static ElementBounds NextAction(ElementBounds slot) => slot.RightCopy(ActionGap);

        private static void DrawRows(GuiComposer compo, ElementBounds inner, List<CardRow> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                CardRow row = rows[i];
                double rowY = inner.fixedY + i * LineHeight;

                var labelFont = CairoFont.WhiteSmallText().WithColor(GuiColors.Label);
                var valueFont = CairoFont.WhiteSmallText().WithColor(row.ValueColor ?? GuiColors.Value)
                                         .WithOrientation(EnumTextOrientation.Right);

                // A name may not fit the reserved width, so the value borrows room from its caption.
                double rowValueWidth = Math.Max(ValueWidth,
                    Math.Min(valueFont.GetTextExtents(row.Value ?? "").Width + 6, inner.fixedWidth * 0.7));
                double labelWidth = inner.fixedWidth - rowValueWidth - 4;

                var labelBounds = inner.FlatCopy().WithFixedSize(labelWidth, LineHeight);
                labelBounds.fixedY = rowY;
                string shown = Fit(row.Label, labelFont, labelWidth);
                compo.AddStaticText(shown, labelFont, labelBounds, "cardlabel-" + row.Key);
                if (shown != row.Label && row.Tooltip == null)
                {
                    Tooltip.Add(compo, row.Label, labelBounds, "cardfull-" + row.Key);
                }

                var valueBounds = inner.FlatCopy().WithFixedSize(rowValueWidth, LineHeight);
                valueBounds.fixedX += inner.fixedWidth - rowValueWidth;
                valueBounds.fixedY = rowY;
                compo.AddStaticText(Fit(row.Value, valueFont, rowValueWidth), valueFont, valueBounds, "cardvalue-" + row.Key);

                if (row.Tooltip != null) Tooltip.Add(compo, row.Tooltip, labelBounds, "cardtip-" + row.Key);
            }
        }

        private static void DrawItem(GuiComposer compo, ElementBounds inner, ItemRow item, double rowY)
        {
            double buttonsWidth = item.Buttons.Count * (ItemButtonSize + 4);
            var labelFont = CairoFont.WhiteSmallText();
            var valueFont = CairoFont.WhiteSmallText().WithColor(item.ValueColor ?? GuiColors.Value)
                                     .WithOrientation(EnumTextOrientation.Right);

            double valueWidth = string.IsNullOrEmpty(item.Value) ? 0
                : Math.Min(valueFont.GetTextExtents(item.Value).Width + 6, inner.fixedWidth * 0.4);
            double labelWidth = inner.fixedWidth - buttonsWidth - valueWidth - 6;

            // Text sits a little lower than the buttons so both read as one line.
            var labelBounds = inner.FlatCopy().WithFixedSize(labelWidth, LineHeight);
            labelBounds.fixedY = rowY + 2;
            string shown = Fit(item.Label, labelFont, labelWidth);
            compo.AddStaticText(shown, labelFont, labelBounds, "itemlabel-" + item.Key);
            if (item.Tooltip != null) Tooltip.Add(compo, item.Tooltip, labelBounds, "itemtip-" + item.Key);
            else if (shown != item.Label) Tooltip.Add(compo, item.Label, labelBounds, "itemfull-" + item.Key);

            if (valueWidth > 0)
            {
                var valueBounds = inner.FlatCopy().WithFixedSize(valueWidth, LineHeight);
                valueBounds.fixedX += labelWidth + 2;
                valueBounds.fixedY = rowY + 2;
                compo.AddStaticText(item.Value, valueFont, valueBounds, "itemvalue-" + item.Key);
            }

            for (int b = 0; b < item.Buttons.Count; b++)
            {
                RowButton button = item.Buttons[b];
                var bounds = inner.FlatCopy().WithFixedSize(ItemButtonSize, ItemButtonSize);
                bounds.fixedX += inner.fixedWidth - (item.Buttons.Count - b) * (ItemButtonSize + 4) + 4;
                bounds.fixedY = rowY;
                string key = "itembtn-" + item.Key + "-" + b;
                compo.AddIconButton(button.Icon, _ => button.OnClick(), bounds, key);
                Tooltip.Add(compo, button.Tooltip, bounds, "tip-" + key);
            }
        }

        /// <summary>Shortens a caption with an ellipsis until it fits.</summary>
        public static string Fit(string text, CairoFont font, double maxWidth)
        {
            if (string.IsNullOrEmpty(text) || font.GetTextExtents(text).Width <= maxWidth) return text ?? "";

            string shortened = text;
            while (shortened.Length > 1 && font.GetTextExtents(shortened + "...").Width > maxWidth)
            {
                shortened = shortened.Substring(0, shortened.Length - 1);
            }
            return shortened + "...";
        }
    }
}
