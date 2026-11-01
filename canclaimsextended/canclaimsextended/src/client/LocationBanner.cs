using System;
using System.Collections.Generic;
using canclaimsextended.src.geometry;
using canclaimsextended.src.network;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace canclaimsextended.src.client
{
    /// <summary>
    /// Where the player is, on screen: the city name as a title when they walk into a city (or
    /// "Wilderness" when they leave one), a short line above the hotbar when they move between
    /// plots of one city or onto its land outside plots. Worked out on the client from the plots
    /// and the territory the server already sends.
    /// </summary>
    public class LocationBanner : IRenderer
    {
        readonly ICoreClientAPI capi;
        readonly ClientPrefs prefs;
        long tickListener;

        // The plot under the player's feet as last seen, and since when; "" for none.
        string candidate;
        long candidateSince;
        // The place the player is counted in.
        bool placed;
        string currentPlot = "";
        string currentCityGuid = "";
        string currentCityName = "";
        bool currentPvp;

        // City guid ("" for wilderness) -> when its title was last shown.
        readonly Dictionary<string, long> titleShownAt = new();

        LoadedTexture title, subtitle, line;
        long titleStart = -1, lineStart = -1;

        public double RenderOrder => 0.9;
        public int RenderRange => 0;

        public LocationBanner(ICoreClientAPI capi, ClientPrefs prefs)
        {
            this.capi = capi;
            this.prefs = prefs;
            tickListener = capi.Event.RegisterGameTickListener(_ => Check(), ClientSettings.BANNER_CHECK_MS);
        }

        public bool Enabled => prefs.ShowBanner;

        public void Toggle()
        {
            prefs.ShowBanner = !prefs.ShowBanner;
            prefs.Save(capi);
            if (!prefs.ShowBanner) titleStart = lineStart = -1;
        }

        void Check()
        {
            ClientPlotStore store = ClientSetup.Store;
            var entity = capi.World.Player?.Entity;
            if (store == null || entity == null) return;

            ClientPlot plot = PlotAt(store, entity.Pos.X, entity.Pos.Y + 0.1, entity.Pos.Z);
            // The territory spans the whole height: above a roof or down a mine is still the city.
            ClientCityLand land = null;
            ClientSetup.Land?.TryGetCityAtBlock(entity.Pos.X, entity.Pos.Z, out land);
            // A place is a plot, or the land of a city outside its plots, or the wild.
            string guid = plot?.Guid ?? (land != null ? "land:" + land.Guid + "/" + land.GroupGuid : "");
            long now = capi.ElapsedMilliseconds;
            if (guid != candidate)
            {
                candidate = guid;
                candidateSince = now;
                return;
            }
            if (now - candidateSince < ClientSettings.BANNER_STABLE_MS || (placed && guid == currentPlot)) return;

            string cityGuid = plot?.CityGuid ?? land?.Guid ?? "";
            string cityName = plot?.CityName ?? land?.Name ?? "";
            int cityColor = plot?.CityColor ?? land?.Color ?? 0;
            bool inCity = cityGuid != "";
            bool pvp = plot != null ? PlotMarkerFlags.Has(plot.Flags, PlotMarkerFlags.PVP)
                     : land != null && PlotMarkerFlags.Has(land.Flags, PlotMarkerFlags.PVP);
            bool firstPlace = !placed;
            bool cityChanged = cityGuid != currentCityGuid;
            bool pvpChanged = pvp != currentPvp;
            string leftCity = currentCityName;

            placed = true;
            currentPlot = guid;
            currentCityGuid = cityGuid;
            currentCityName = cityName;
            currentPvp = pvp;

            if (!prefs.ShowBanner) return;
            if (firstPlace)
            {
                // Logging in on free land says nothing.
                if (inCity && TakeTitle(cityGuid, now)) ShowCity(cityName, cityColor, pvp);
            }
            else if (cityChanged)
            {
                if (!TakeTitle(cityGuid, now))
                {
                    // Walking along the border: the same title again so soon would be noise.
                    if (inCity) ShowPlaceLine(plot, land, pvpChanged ? pvp : null);
                }
                else if (inCity) ShowCity(cityName, cityColor, pvp);
                else ShowWilderness(leftCity);
            }
            else if (inCity)
            {
                ShowPlaceLine(plot, land, pvpChanged ? pvp : null);
            }
        }

        /// <summary>False when this title (a city, or "" for wilderness) was shown a moment ago.</summary>
        bool TakeTitle(string cityGuid, long now)
        {
            if (titleShownAt.TryGetValue(cityGuid, out long at) && now - at < ClientSettings.BANNER_REPEAT_MS) return false;
            titleShownAt[cityGuid] = now;
            return true;
        }

        static ClientPlot PlotAt(ClientPlotStore store, double x, double y, double z)
        {
            int bx = (int)Math.Floor(x), by = (int)Math.Floor(y), bz = (int)Math.Floor(z);
            foreach (ClientPlot plot in store.Query(new BlockBox(bx, by, bz, bx + 1, by + 1, bz + 1)))
            {
                if (plot.Shape.Contains(x, y, z)) return plot;
            }
            return null;
        }

        void ShowCity(string cityName, int cityColor, bool pvp)
        {
            SetTitle(ClientText.Pretty(cityName), Lighten(cityColor),
                Lang.Get(pvp ? "canclaimsextended:banner-pvp-on" : "canclaimsextended:banner-pvp-off"),
                pvp ? new[] { 0.95, 0.35, 0.30, 1.0 } : new[] { 0.60, 0.90, 0.60, 1.0 });
            lineStart = -1;
        }

        void ShowWilderness(string leftCity)
        {
            SetTitle(Lang.Get("canclaimsextended:banner-wilderness"), new[] { 0.85, 0.85, 0.80, 1.0 },
                string.IsNullOrEmpty(leftCity) ? "" : Lang.Get("canclaimsextended:banner-left-city", leftCity),
                new[] { 0.75, 0.75, 0.75, 1.0 });
            lineStart = -1;
        }

        /// <summary>
        /// For a plot its name or owner and the price when it is for sale; for the land outside
        /// plots just that. Pvp only when it differs from the last place.
        /// </summary>
        void ShowPlaceLine(ClientPlot plot, ClientCityLand land, bool? pvp)
        {
            string text;
            if (plot == null)
            {
                text = Lang.Get("canclaimsextended:banner-city-land");
                if (!string.IsNullOrEmpty(land?.GroupName)) text += " · " + ClientText.Pretty(land.GroupName);
            }
            else
            {
                text = !string.IsNullOrEmpty(plot.Name) ? plot.Name
                    : !string.IsNullOrEmpty(plot.OwnerName) ? Lang.Get("canclaimsextended:banner-plot-of", plot.OwnerName)
                    : Lang.Get("canclaimsextended:banner-city-plot");
                if (!string.IsNullOrEmpty(plot.Name) && !string.IsNullOrEmpty(plot.OwnerName)) text += " · " + plot.OwnerName;
                if (plot.Number > 0) text = Lang.Get("canclaimsextended:marker-number", plot.Number) + " " + text;
                if (!string.IsNullOrEmpty(plot.GroupName)) text += " · " + ClientText.Pretty(plot.GroupName);
                if (plot.ForSale) text += " · " + Lang.Get("canclaimsextended:marker-price", plot.Price);
            }
            if (pvp.HasValue) text += " · " + Lang.Get(pvp.Value ? "canclaimsextended:banner-pvp-on" : "canclaimsextended:banner-pvp-off");

            line?.Dispose();
            line = capi.Gui.TextTexture.GenTextTexture(text, CairoFont.WhiteSmallishText().WithStroke(new[] { 0.0, 0.0, 0.0, 0.8 }, 2));
            lineStart = capi.ElapsedMilliseconds;
        }

        void SetTitle(string text, double[] color, string sub, double[] subColor)
        {
            title?.Dispose();
            subtitle?.Dispose();
            var stroke = new[] { 0.0, 0.0, 0.0, 0.85 };
            title = capi.Gui.TextTexture.GenTextTexture(text, new CairoFont(40, GuiStyle.DecorativeFontName, color).WithStroke(stroke, 3));
            subtitle = string.IsNullOrEmpty(sub) ? null
                : capi.Gui.TextTexture.GenTextTexture(sub, new CairoFont(20, GuiStyle.StandardFontName, subColor).WithStroke(stroke, 2));
            titleStart = capi.ElapsedMilliseconds;
        }

        /// <summary>City colors are stored as ABGR; dark ones are lifted so the name stays readable.</summary>
        static double[] Lighten(int abgr)
        {
            double r = (abgr & 0xFF) / 255.0, g = ((abgr >> 8) & 0xFF) / 255.0, b = ((abgr >> 16) & 0xFF) / 255.0;
            const double lift = 0.35;
            return new[] { r + (1 - r) * lift, g + (1 - g) * lift, b + (1 - b) * lift, 1.0 };
        }

        /// <summary>0..1: fades in, holds, fades out; 0 once over.</summary>
        float Alpha(long start, int duration)
        {
            if (start < 0) return 0;
            long t = capi.ElapsedMilliseconds - start;
            if (t >= duration) return 0;
            if (t < ClientSettings.BANNER_FADE_IN_MS) return (float)t / ClientSettings.BANNER_FADE_IN_MS;
            long left = duration - t;
            return left < ClientSettings.BANNER_FADE_OUT_MS ? (float)left / ClientSettings.BANNER_FADE_OUT_MS : 1f;
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (!prefs.ShowBanner || capi.HideGuis) return;
            IRenderAPI rapi = capi.Render;

            float a = Alpha(titleStart, ClientSettings.BANNER_TITLE_MS);
            if (a > 0 && title != null)
            {
                float y = rapi.FrameHeight * 0.16f;
                Draw(title, rapi.FrameWidth / 2f, y, a);
                if (subtitle != null) Draw(subtitle, rapi.FrameWidth / 2f, y + title.Height, a);
            }
            else if (titleStart >= 0)
            {
                titleStart = -1;
            }

            a = Alpha(lineStart, ClientSettings.BANNER_LINE_MS);
            if (a > 0 && line != null)
            {
                // Above the hotbar and the held item's name.
                Draw(line, rapi.FrameWidth / 2f, rapi.FrameHeight - (float)GuiElement.scaled(170), a);
            }
            else if (lineStart >= 0)
            {
                lineStart = -1;
            }
        }

        void Draw(LoadedTexture tex, float centerX, float topY, float alpha)
        {
            // Text textures are premultiplied, so every channel fades together.
            capi.Render.Render2DTexturePremultipliedAlpha(tex.TextureId, centerX - tex.Width / 2f, topY, tex.Width, tex.Height, 20f,
                new Vec4f(alpha, alpha, alpha, alpha));
        }

        public void Dispose()
        {
            if (tickListener != 0) capi.Event.UnregisterGameTickListener(tickListener);
            tickListener = 0;
            title?.Dispose();
            subtitle?.Dispose();
            line?.Dispose();
            title = subtitle = line = null;
        }
    }
}
