using System;
using canclaimsextended.src.client.gui.dialogs;
using canclaimsextended.src.client.gui.widgets;
using canclaimsextended.src.network;
using Vintagestory.API.Client;

namespace canclaimsextended.src.client.gui.pages
{
    /// <summary>
    /// City levels for admins: one line per citizens threshold, every value with its own button
    /// running /cadmin level set, and buttons to add and remove levels.
    /// </summary>
    public sealed class CityLevelsPage : GuiPage
    {
        public override bool IsVisible(GuiStatePacket p) => ShowAdminTabs(p);

        private const double RowHeight = 22;
        private const double ButtonSize = 20;

        private static readonly string[] Fields = { "citizens", "chunks", "extra", "plots", "volume", "summon" };

        protected override void BuildContent(PageBuildContext ctx)
        {
            var compo = ctx.Compo;
            var column = ctx.Line.BelowCopy(0, 14);
            column.Alignment = EnumDialogArea.LeftTop;

            int rows = P.CityLevels.Count;
            double height = Card.HeaderHeight + RowHeight * (rows + 1) + Card.ActionGap + Card.ActionSize + Card.Padding * 2;
            ElementBounds inner = Card.Frame(compo, column, column.fixedY, height, T("gui-section-levels"));

            // The last cell holds the remove button; the rest share the width.
            double cellWidth = (inner.fixedWidth - ButtonSize - 4) / Fields.Length;
            var headerFont = CairoFont.WhiteSmallText().WithColor(GuiColors.Label);
            for (int f = 0; f < Fields.Length; f++)
            {
                var bounds = Cell(inner, f, cellWidth, inner.fixedY + 2);
                compo.AddStaticText(Card.Fit(T("gui-level-" + Fields[f]), headerFont, cellWidth - 4), headerFont, bounds, "lvl-head-" + f);
                Tooltip.Add(compo, T("gui-level-" + Fields[f] + "-tooltip"), bounds, "lvl-headtip-" + f);
            }

            var valueFont = CairoFont.WhiteSmallText().WithColor(GuiColors.Value);
            for (int r = 0; r < rows; r++)
            {
                GuiCityLevelEntry level = P.CityLevels[r];
                double rowY = inner.fixedY + RowHeight * (r + 1);
                long[] values = { level.Citizens, level.Chunks, level.Extra, level.Plots, level.Volume, level.Summon };
                for (int f = 0; f < Fields.Length; f++)
                {
                    var text = Cell(inner, f, cellWidth - ButtonSize - 4, rowY + 2);
                    compo.AddStaticText(values[f].ToString(), valueFont, text, "lvl-" + r + "-" + f);

                    var button = inner.FlatCopy().WithFixedSize(ButtonSize, ButtonSize);
                    button.fixedX += (f + 1) * cellWidth - ButtonSize - 6;
                    button.fixedY = rowY;
                    string field = Fields[f];
                    long current = values[f];
                    int citizens = level.Citizens;
                    string key = "lvl-edit-" + r + "-" + f;
                    compo.AddIconButton(GuiIcons.Edit, _ => Open(new TextInputDialog(
                        T("gui-level-prompt", T("gui-level-" + field), citizens, current), T("gui-button-save"),
                        value => Cadmin($"level set {citizens} {field} {value}"), field != "volume")), button, key);
                    Tooltip.Add(compo, T("gui-action-level-edit"), button, "tip-" + key);
                }

                if (rows > 1)
                {
                    var remove = inner.FlatCopy().WithFixedSize(ButtonSize, ButtonSize);
                    remove.fixedX += inner.fixedWidth - ButtonSize;
                    remove.fixedY = rowY;
                    int citizens = level.Citizens;
                    string key = "lvl-remove-" + r;
                    compo.AddIconButton(GuiIcons.Remove, _ => Open(new ConfirmDialog(T("gui-confirm-level-remove", citizens),
                        () => Cadmin("level remove " + citizens))), remove, key);
                    Tooltip.Add(compo, T("gui-action-level-remove"), remove, "tip-" + key);
                }
            }

            var slot = inner.FlatCopy().WithFixedSize(Card.ActionSize, Card.ActionSize);
            slot.fixedY = inner.fixedY + RowHeight * (rows + 1) + Card.ActionGap;
            new ActionRow(compo, slot).Add(GuiIcons.Accept, "lvl-add", () =>
                Open(new TextInputDialog(T("gui-level-add-prompt"), T("gui-button-add"), value => Cadmin("level add " + value), true)),
                T("gui-action-level-add"));

            var hint = column.FlatCopy();
            Card.Note(compo, hint, column.fixedY + height + Card.Gap, null, T("gui-levels-hint"));
        }

        private static ElementBounds Cell(ElementBounds inner, int index, double width, double y)
        {
            var bounds = inner.FlatCopy().WithFixedSize(Math.Max(10, width), Card.LineHeight);
            bounds.fixedX += index * (inner.fixedWidth - ButtonSize - 4) / Fields.Length;
            bounds.fixedY = y;
            return bounds;
        }
    }
}
