using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.client.gui.widgets;
using canclaimsextended.src.part.structure;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace canclaimsextended.src.client.gui.pages
{
    /// <summary>
    /// Designs the city's coat of arms: the preview, the layers bottom first, and the pickers for
    /// the next layer. Nothing reaches the server before Apply.
    /// </summary>
    public sealed class EmblemPage : GuiPage
    {
        public override bool IsVisible(network.GuiStatePacket p)
            => HasCityIn(p) && (p.IsMayor || p.Permissions.Contains("CITY_SET_EMBLEM"));

        private const double PreviewSize = 96;
        private const double SwatchSize = 64;
        private const double InputHeight = 28;
        private const double AddButtonWidth = 120;

        private EmblemDraft Draft => State.Emblem;

        protected override void BuildContent(PageBuildContext ctx)
        {
            Columns(ctx, out var left, out var right);
            var compo = ctx.Compo;
            if (!HasCity || !P.CanSetEmblem)
            {
                Card.Note(compo, left, left.fixedY, T("gui-section-emblem"), T("gui-emblem-no-right"));
                return;
            }
            if (!Draft.Edited)
            {
                Draft.LoadFrom(P.CityEmblem);
            }

            double yLeft = PreviewCard(compo, left, left.fixedY);
            PickerCard(compo, left, yLeft);
            LayersCard(compo, right, right.fixedY);
        }

        private double PreviewCard(GuiComposer compo, ElementBounds column, double y)
        {
            double height = Card.HeaderHeight + PreviewSize + Card.ActionGap + Card.ActionSize + Card.Padding * 2;
            ElementBounds inner = Card.Frame(compo, column, y, height, T("gui-emblem-preview"));

            compo.AddEmblem(Draft.AsString(), inner.FlatCopy().WithFixedSize(PreviewSize, PreviewSize), "emblem-preview");

            // The layer string is what /city set emblem takes.
            var text = inner.FlatCopy().WithFixedSize(inner.fixedWidth - PreviewSize - Card.Gap, PreviewSize);
            text.fixedX += PreviewSize + Card.Gap;
            compo.AddStaticText(Draft.Layers.Count == 0 ? T("gui-emblem-empty") : Draft.AsString(),
                CairoFont.WhiteSmallText().WithColor(GuiColors.Label), text, "emblem-string");

            var slot = inner.FlatCopy().WithFixedSize(Card.ActionSize, Card.ActionSize);
            slot.fixedY = inner.fixedY + PreviewSize + Card.ActionGap;
            var actions = new ActionRow(compo, slot);
            actions.Add(GuiIcons.Accept, "emblem-apply", Apply, T("gui-emblem-apply"));
            actions.Add(GuiIcons.Remove, "emblem-clear", () =>
            {
                Draft.Layers.Clear();
                Draft.Edited = true;
                Gui.BuildMainWindow();
            }, T("gui-emblem-clear"));
            actions.Add(GuiIcons.Back, "emblem-back", () =>
            {
                Draft.Edited = false;
                GoTo(EnumGuiTab.City);
            }, T("gui-action-back"));
            return y + height + Card.Gap;
        }

        /// <summary>Pattern and colour of the next layer, the button to add it and two previews.</summary>
        private void PickerCard(GuiComposer compo, ElementBounds column, double y)
        {
            var patterns = EmblemHandler.AvailablePatternsForLayer(Draft.Layers.Count).ToArray();
            var colors = EmblemHandler.AvailableColors().ToArray();
            bool full = Draft.Layers.Count >= EmblemHandler.MaxLayers();

            double height = Card.HeaderHeight + InputHeight * 2 + Card.Gap * 2 + SwatchSize + Card.LineHeight + Card.Padding * 2;
            ElementBounds inner = Card.Frame(compo, column, y, height,
                T(Draft.Layers.Count == 0 ? "gui-emblem-add-background" : "gui-emblem-add"));

            double dropWidth = inner.fixedWidth - AddButtonWidth - Card.Gap;
            var patternBounds = inner.FlatCopy().WithFixedSize(dropWidth, InputHeight);
            compo.AddDropDown(patterns, patterns.Select(PatternName).ToArray(),
                System.Math.Max(0, System.Array.IndexOf(patterns, Draft.Pattern)),
                (code, selected) =>
                {
                    if (!selected) return;
                    Draft.Pattern = code;
                    Draft.Edited = true;
                    Gui.BuildMainWindow();
                }, patternBounds, "emblem-pattern");

            var colorBounds = patternBounds.FlatCopy();
            colorBounds.fixedY += InputHeight + Card.Gap;
            compo.AddDropDown(colors, colors.Select(ColorName).ToArray(),
                System.Math.Max(0, System.Array.IndexOf(colors, Draft.Color)),
                (code, selected) =>
                {
                    if (!selected) return;
                    Draft.Color = code;
                    Draft.Edited = true;
                    Gui.BuildMainWindow();
                }, colorBounds, "emblem-color");

            // Worked out once for the button and both previews, so they cannot disagree.
            string next = SelectedLayer(patterns, colors);
            var addBounds = inner.FlatCopy().WithFixedSize(AddButtonWidth, InputHeight);
            addBounds.fixedX += dropWidth + Card.Gap;
            compo.AddSmallButton(T("gui-emblem-add-layer"), () => AddLayer(next), addBounds, EnumButtonStyle.Normal, "emblem-add");
            if (full)
            {
                Tooltip.Add(compo, T("gui-emblem-full", EmblemHandler.MaxLayers()), addBounds, "tip-emblem-full");
            }

            double previewY = colorBounds.fixedY + InputHeight + Card.Gap;
            var swatch = inner.FlatCopy().WithFixedSize(SwatchSize, SwatchSize);
            swatch.fixedY = previewY;
            compo.AddEmblem(next, swatch, "emblem-next");
            Caption(compo, inner, previewY + SwatchSize, 0, T("gui-emblem-preview-layer"));

            var result = swatch.FlatCopy();
            result.fixedX += SwatchSize + Card.Gap;
            compo.AddEmblem(EmblemHandler.Join(Draft.Layers.Concat(new[] { next })), result, "emblem-result");
            Caption(compo, inner, previewY + SwatchSize, SwatchSize + Card.Gap, T("gui-emblem-preview-result"));
        }

        /// <summary>The stack, bottom first. The fill stays at the bottom; the others can move up or go.</summary>
        private void LayersCard(GuiComposer compo, ElementBounds column, double y)
        {
            var rows = new List<ItemRow>();
            for (int i = 0; i < Draft.Layers.Count; i++)
            {
                int index = i;
                var row = new ItemRow
                {
                    Label = T(i == 0 ? "gui-emblem-layer-background" : "gui-emblem-layer", i + 1, LayerName(Draft.Layers[i])),
                    Key = "emblem-layer-" + i
                };
                // Moving to the bottom would replace the fill, so the second layer stays where it is.
                if (i >= 2)
                {
                    row.Buttons.Add(new RowButton(GuiIcons.Extend, () => Swap(index, index - 1), T("gui-emblem-move-up")));
                }
                // The fill goes only when it is alone: without it the cloth would be transparent.
                if (i > 0 || Draft.Layers.Count == 1)
                {
                    row.Buttons.Add(new RowButton(GuiIcons.Remove, () => RemoveLayer(index), T("gui-emblem-remove")));
                }
                rows.Add(row);
            }
            Card.Items(compo, column, y, T("gui-emblem-layers", Draft.Layers.Count, EmblemHandler.MaxLayers()),
                rows, T("gui-emblem-layers-none"));
        }

        private void Caption(GuiComposer compo, ElementBounds inner, double y, double offsetX, string text)
        {
            var bounds = inner.FlatCopy().WithFixedSize(SwatchSize + Card.Gap, Card.LineHeight);
            bounds.fixedX += offsetX;
            bounds.fixedY = y;
            compo.AddStaticText(text, CairoFont.WhiteSmallText().WithColor(GuiColors.Label), bounds, "emblem-caption-" + offsetX);
        }

        /// <summary>A pick the current position does not allow falls back to the first entry, as the drop-down shows.</summary>
        private string SelectedLayer(string[] patterns, string[] colors)
        {
            string pattern = patterns.Contains(Draft.Pattern) ? Draft.Pattern : patterns.FirstOrDefault();
            string color = colors.Contains(Draft.Color) ? Draft.Color : colors.FirstOrDefault();
            return pattern == null || color == null ? "" : EmblemHandler.MakeLayer(pattern, color);
        }

        private bool AddLayer(string layer)
        {
            if (Draft.Layers.Count >= EmblemHandler.MaxLayers() || !EmblemHandler.IsValidLayer(layer)) return true;
            Draft.Layers.Add(layer);
            Draft.Edited = true;
            Gui.BuildMainWindow();
            return true;
        }

        private void RemoveLayer(int index)
        {
            if (index < 0 || index >= Draft.Layers.Count) return;
            Draft.Layers.RemoveAt(index);
            Draft.Edited = true;
            Gui.BuildMainWindow();
        }

        private void Swap(int a, int b)
        {
            if (a <= 0 || b <= 0 || a >= Draft.Layers.Count || b >= Draft.Layers.Count) return;
            (Draft.Layers[a], Draft.Layers[b]) = (Draft.Layers[b], Draft.Layers[a]);
            Draft.Edited = true;
            Gui.BuildMainWindow();
        }

        private void Apply()
        {
            // No layers clears the arms: the command takes a missing argument that way.
            City(("set emblem " + Draft.AsString()).TrimEnd());
            Draft.Edited = false;
            GoTo(EnumGuiTab.City);
        }

        /// <summary>"cross_white" as "Cross, white", translated where a translation exists.</summary>
        private static string LayerName(string layer)
        {
            if (!EmblemHandler.TrySplitLayer(layer, out string pattern, out string color)) return layer;
            return PatternName(pattern) + ", " + ColorName(color);
        }

        private static string PatternName(string pattern) => Translated("canclaimsextended:emblem-pattern-" + pattern, pattern);
        private static string ColorName(string color) => Translated("canclaimsextended:emblem-color-" + color, color);

        /// <summary>The raw asset name when there is no translation: patterns are many.</summary>
        private static string Translated(string key, string fallback)
            => Lang.HasTranslation(key, true, false) ? Lang.Get(key) : fallback.Replace('_', ' ');
    }
}
