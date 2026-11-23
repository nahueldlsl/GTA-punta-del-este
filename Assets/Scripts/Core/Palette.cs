using UnityEngine;

namespace GtaPunta.Core
{
    /// <summary>
    /// Single-source Art Bible & Coastal Color Palette for GTA: Punta del Este.
    /// Style: Grounded sunlit South Atlantic coastal realism over white architectural stucco,
    /// warm golden sand, dual turquoise/cobalt seas, and vibrant GTA-inspired UI accents.
    /// </summary>
    public static class Palette
    {
        public static Color Hex(string hex, float alpha = 1f)
        {
            if (ColorUtility.TryParseHtmlString(hex, out Color c))
            {
                c.a = alpha;
                return c;
            }
            return new Color(0.95f, 0.92f, 0.86f, alpha);
        }

        // Atmosphere & Sky
        public static readonly Color SunAfternoon    = Hex("#FFF1D6");
        public static readonly Color SunSunset       = Hex("#FF9B52");
        public static readonly Color MoonNight       = Hex("#7CA8DF");
        public static readonly Color SkyZenithDay    = Hex("#2C78C9");
        public static readonly Color SkyHorizonDay   = Hex("#B9DDF5");
        public static readonly Color SkyZenithSunset = Hex("#2B3B6E");
        public static readonly Color SkyHorizonSunset= Hex("#F6985C");
        public static readonly Color SkyZenithNight  = Hex("#070F1F");
        public static readonly Color SkyHorizonNight = Hex("#142642");
        public static readonly Color ShadowCoolBounce= Hex("#5B7394");
        public static readonly Color GroundWarmBounce= Hex("#8C7A62");

        // Ocean (Playa Mansa vs Playa Brava)
        public static readonly Color MansaShallow    = Hex("#2494AD", 0.90f);
        public static readonly Color MansaDeep       = Hex("#0F4D73", 0.97f);
        public static readonly Color BravaShallow    = Hex("#1A7A9E", 0.94f);
        public static readonly Color BravaDeep       = Hex("#0A315C", 0.99f);
        public static readonly Color OceanFoam       = Hex("#EEF6FA");

        // Land, Roads & Architecture
        public static readonly Color PuntaSand       = Hex("#E6CFA8");
        public static readonly Color WarmStucco      = Hex("#F2ECE1");
        public static readonly Color CreamConcrete   = Hex("#E6DFD3");
        public static readonly Color AsphaltDark     = Hex("#3B3F45");
        public static readonly Color CoastalGlass    = Hex("#2B6A88");
        public static readonly Color CandelariaBlue  = Hex("#5BA4D6");
        public static readonly Color ParkGreen       = Hex("#487A45");
        public static readonly Color DockWood        = Hex("#96724D");

        // Modern GTA Coastal HUD
        public static readonly Color HudPanelBg      = Hex("#0B1626", 0.80f);
        public static readonly Color HudPanelBorder  = Hex("#28C7E8", 0.55f);
        public static readonly Color HudGoldAccent   = Hex("#F5B82E");
        public static readonly Color HudCyanAccent   = Hex("#28C7E8");
        public static readonly Color HudTextLight    = Hex("#F4F7FA");
        public static readonly Color HudTextMuted    = Hex("#A6BDD4");
        public static readonly Color HudWantedAlert  = Hex("#EF3B3B");
        public static readonly Color HudWantedPolice = Hex("#2E7BFF");
        public static readonly Color HudSuccess      = Hex("#32D583");
        public static readonly Color ScrimDark       = Hex("#08101C", 0.78f);
    }
}
