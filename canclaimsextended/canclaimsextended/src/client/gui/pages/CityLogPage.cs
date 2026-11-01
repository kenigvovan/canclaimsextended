using System.Collections.Generic;
using canclaimsextended.src.client.gui.dialogs;
using canclaimsextended.src.client.gui.widgets;
using canclaimsextended.src.part.citylog;
using Vintagestory.API.Client;

namespace canclaimsextended.src.client.gui.pages
{
    /// <summary>The city's message of the day and its latest log entries; the full log is /city log.</summary>
    public sealed class CityLogPage : GuiPage
    {
        public override bool IsVisible(network.GuiStatePacket p) => HasCityIn(p);

        const int MAX_ROWS = 10;

        protected override void BuildContent(PageBuildContext ctx)
        {
            var compo = ctx.Compo;
            var column = ctx.Line.BelowCopy(0, 14);
            column.Alignment = EnumDialogArea.LeftTop;
            column.WithFixedWidth(ctx.Line.fixedWidth);

            double y = DailyMsgCard(compo, column, column.fixedY);
            // The server sends the log only to who may read it.
            if (MayorOr("CITY_SEE_LOG")) LogCard(compo, column, y);
        }

        private double DailyMsgCard(GuiComposer compo, ElementBounds column, double y)
        {
            string text = string.IsNullOrEmpty(P.CityDailyMsg) ? T("gui-city-daily-msg-empty") : P.CityDailyMsg;
            System.Action<ElementBounds> actions = null;
            if (MayorOr("CITY_SET_ALL", "CITY_SET_DAILY_MSG"))
            {
                actions = slot =>
                {
                    var row = new ActionRow(compo, slot);
                    row.Add(GuiIcons.Edit, "dailymsg-set", () =>
                        Open(new TextInputDialog(T("gui-prompt-daily-msg"), T("gui-button-save"), msg => City("set dailymsg " + msg))),
                        T("gui-action-daily-msg-set"));
                    if (!string.IsNullOrEmpty(P.CityDailyMsg))
                    {
                        row.Add(GuiIcons.Remove, "dailymsg-reset", () => City("set dailymsg"), T("gui-action-daily-msg-reset"));
                    }
                };
            }
            return Card.Note(compo, column, y, T("gui-section-daily-msg"), text, actions);
        }

        private void LogCard(GuiComposer compo, ElementBounds column, double y)
        {
            var rows = new List<ItemRow>();
            for (int i = 0; i < P.CityLog.Count; i++)
            {
                var entry = P.CityLog[i];
                rows.Add(new ItemRow
                {
                    Label = CityLog.Text(entry.Event, entry.Args),
                    Value = CityLog.When(entry.Timestamp),
                    Key = "log-" + i
                });
            }
            Card.Items(compo, column, y, T("gui-section-city-log"), Limit(rows, MAX_ROWS), T("gui-city-log-empty"));
        }
    }
}
