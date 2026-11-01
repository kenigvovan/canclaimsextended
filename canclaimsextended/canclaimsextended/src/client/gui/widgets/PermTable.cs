using System;
using canclaimsextended.src.auxiliary;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace canclaimsextended.src.client.gui.widgets
{
    /// <summary>
    /// The rights grid (three groups by use/build/attack) and the flag switches, as cards. Each switch
    /// runs a command at once; the window redraws when the server answers.
    /// </summary>
    public static class PermTable
    {
        public static readonly string[] Groups = Codes.PermGroups;
        public static readonly string[] Types = Codes.PermTypes;
        public static readonly string[] Flags = Codes.Flags;

        private const double RowHeight = 26;
        private const double SwitchSize = 22;
        private const double ColumnWidth = 60;

        /// <summary>
        /// The grid. <paramref name="values"/> is row by row, group x type, as GuiStatePacket sends it.
        /// <paramref name="readOnly"/> shows the switches but ignores clicks.
        /// </summary>
        public static double Rights(GuiComposer compo, ElementBounds column, double y, string keyPrefix,
                                    bool[] values, bool readOnly, Action<string, string, bool> onToggle)
        {
            double height = Card.HeaderHeight + RowHeight * (Groups.Length + 1) + Card.Padding * 2;
            ElementBounds inner = Card.Frame(compo, column, y, height, Lang.Get("canclaimsextended:gui-section-rights"));

            var labelFont = CairoFont.WhiteSmallText().WithColor(GuiColors.Label);
            double labelWidth = inner.fixedWidth - ColumnWidth * Types.Length;

            for (int t = 0; t < Types.Length; t++)
            {
                var header = inner.FlatCopy().WithFixedSize(ColumnWidth, RowHeight);
                header.fixedX += labelWidth + t * ColumnWidth;
                compo.AddStaticText(Lang.Get("canclaimsextended:gui-perm-" + Types[t]),
                    CairoFont.WhiteSmallText().WithColor(GuiColors.Section).WithOrientation(EnumTextOrientation.Center),
                    header, keyPrefix + "-head-" + t);
            }

            for (int g = 0; g < Groups.Length; g++)
            {
                double rowY = inner.fixedY + RowHeight * (g + 1);
                var label = inner.FlatCopy().WithFixedSize(labelWidth, RowHeight);
                label.fixedY = rowY + 4;
                compo.AddStaticText(Lang.Get("canclaimsextended:gui-group-" + Groups[g]), labelFont, label, keyPrefix + "-group-" + g);

                for (int t = 0; t < Types.Length; t++)
                {
                    int index = g * Types.Length + t;
                    string group = Groups[g];
                    string type = Types[t];
                    var sw = inner.FlatCopy().WithFixedSize(SwitchSize, SwitchSize);
                    sw.fixedX += labelWidth + t * ColumnWidth + (ColumnWidth - SwitchSize) / 2;
                    sw.fixedY = rowY;
                    string key = keyPrefix + "-perm-" + index;
                    bool current = values != null && index < values.Length && values[index];
                    compo.AddSwitch(on =>
                    {
                        if (readOnly)
                        {
                            compo.GetSwitch(key).SetValue(current);
                            return;
                        }
                        onToggle(group, type, on);
                    }, sw, key, SwitchSize);
                }
            }
            return y + height + Card.Gap;
        }

        /// <summary>
        /// One row of the grid: what the members of a plots group may do (use, build, attack), under
        /// <paramref name="rowLabel"/>. Keys as in <see cref="Rights"/>, so ApplyValues sets it too.
        /// </summary>
        public static double MemberRights(GuiComposer compo, ElementBounds column, double y, string keyPrefix, string title,
                                          string rowLabel, bool readOnly, Action<string, bool> onToggle)
        {
            double height = Card.HeaderHeight + RowHeight * 2 + Card.Padding * 2;
            ElementBounds inner = Card.Frame(compo, column, y, height, title);
            double labelWidth = inner.fixedWidth - ColumnWidth * Types.Length;

            for (int t = 0; t < Types.Length; t++)
            {
                var header = inner.FlatCopy().WithFixedSize(ColumnWidth, RowHeight);
                header.fixedX += labelWidth + t * ColumnWidth;
                compo.AddStaticText(Lang.Get("canclaimsextended:gui-perm-" + Types[t]),
                    CairoFont.WhiteSmallText().WithColor(GuiColors.Section).WithOrientation(EnumTextOrientation.Center),
                    header, keyPrefix + "-head-" + t);
            }

            double rowY = inner.fixedY + RowHeight;
            var label = inner.FlatCopy().WithFixedSize(labelWidth, RowHeight);
            label.fixedY = rowY + 4;
            compo.AddStaticText(rowLabel, CairoFont.WhiteSmallText().WithColor(GuiColors.Label), label, keyPrefix + "-row");
            for (int t = 0; t < Types.Length; t++)
            {
                string type = Types[t];
                var sw = inner.FlatCopy().WithFixedSize(SwitchSize, SwitchSize);
                sw.fixedX += labelWidth + t * ColumnWidth + (ColumnWidth - SwitchSize) / 2;
                sw.fixedY = rowY;
                string key = keyPrefix + "-perm-" + t;
                compo.AddSwitch(on =>
                {
                    // A refused click goes back; the server's answer redraws the window anyway.
                    if (readOnly)
                    {
                        compo.GetSwitch(key).SetValue(!on);
                        return;
                    }
                    onToggle(type, on);
                }, sw, key, SwitchSize);
            }
            return y + height + Card.Gap;
        }

        /// <summary>The four flags as labelled switches, two per line.</summary>
        public static double FlagSwitches(GuiComposer compo, ElementBounds column, double y, string keyPrefix,
                                          bool[] values, bool readOnly, Action<string, bool> onToggle)
        {
            return FlagSwitches(compo, column, y, keyPrefix, values, flag => !readOnly, onToggle);
        }

        /// <summary>The same, with a right of its own for each flag: a switch the player may not touch stays put.</summary>
        public static double FlagSwitches(GuiComposer compo, ElementBounds column, double y, string keyPrefix,
                                          bool[] values, Func<string, bool> mayToggle, Action<string, bool> onToggle)
        {
            int lines = (Flags.Length + 1) / 2;
            double height = Card.HeaderHeight + RowHeight * lines + Card.Padding * 2;
            ElementBounds inner = Card.Frame(compo, column, y, height, Lang.Get("canclaimsextended:gui-section-flags"));

            double half = inner.fixedWidth / 2;
            var labelFont = CairoFont.WhiteSmallText().WithColor(GuiColors.Label);
            for (int i = 0; i < Flags.Length; i++)
            {
                string flag = Flags[i];
                double x = (i % 2) * half;
                double rowY = inner.fixedY + (i / 2) * RowHeight;

                var sw = inner.FlatCopy().WithFixedSize(SwitchSize, SwitchSize);
                sw.fixedX += x;
                sw.fixedY = rowY;
                string key = keyPrefix + "-flag-" + flag;
                bool current = values != null && i < values.Length && values[i];
                compo.AddSwitch(on =>
                {
                    if (!mayToggle(flag))
                    {
                        compo.GetSwitch(key).SetValue(current);
                        return;
                    }
                    onToggle(flag, on);
                }, sw, key, SwitchSize);

                var label = inner.FlatCopy().WithFixedSize(half - SwitchSize - 8, RowHeight);
                label.fixedX += x + SwitchSize + 6;
                label.fixedY = rowY + 4;
                compo.AddStaticText(Lang.Get("canclaimsextended:gui-flag-" + flag), labelFont, label, keyPrefix + "-flaglabel-" + flag);
                Tooltip.Add(compo, Lang.Get("canclaimsextended:gui-flag-" + flag + "-tooltip"), label, keyPrefix + "-flagtip-" + flag);
            }
            return y + height + Card.Gap;
        }

        /// <summary>Sets every switch to what the server sent. Must run after Compose.</summary>
        public static void ApplyValues(GuiComposer compo, string keyPrefix, bool[] rights, bool[] flags)
        {
            for (int i = 0; rights != null && i < rights.Length; i++)
            {
                compo.GetSwitch(keyPrefix + "-perm-" + i)?.SetValue(rights[i]);
            }
            for (int i = 0; flags != null && i < Flags.Length && i < flags.Length; i++)
            {
                compo.GetSwitch(keyPrefix + "-flag-" + Flags[i])?.SetValue(flags[i]);
            }
        }
    }
}
