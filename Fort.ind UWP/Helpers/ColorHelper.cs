using System;
using System.Collections.Generic;
using Windows.UI;

namespace Fort.ind_UWP
{
    public static class ColorHelper
    {
        private static readonly Dictionary<string, string> s_lightTintMap = new Dictionary<string, string>()
        {
            { "#1E3A5F", "#C8E0F5" },
            { "#2D1B69", "#DDD0F5" },
            { "#0F3D2E", "#C5E8D5" },
            { "#3D1515", "#F5CECE" },
            { "#1A1A2E", "#D0D0EA" },
            { "#0E3A3A", "#C5E8E8" },
            { "#3D2A0F", "#F5E3C0" },
            { "#3D1533", "#F5CEE9" },
            { "#2E3D0F", "#DEEBC0" },
            { "#232323", "#DCDCDC" }
        };

        public static string TryGetLightPreset(string darkHex)
        {
            if (darkHex == null) return null;

            string lightHex;
            return s_lightTintMap.TryGetValue(darkHex, out lightHex) ? lightHex : null;
        }

        public static Color ForLightTheme(string darkHex)
        {
            var preset = TryGetLightPreset(darkHex);
            return preset != null ? HexToColor(preset) : LightenForLightTheme(HexToColor(darkHex));
        }

        public static Color HexToColor(string hex)
        {
            hex = hex.TrimStart('#');
            return Color.FromArgb(255,
                                  Convert.ToByte(hex.Substring(0, 2), 16),
                                  Convert.ToByte(hex.Substring(2, 2), 16),
                                  Convert.ToByte(hex.Substring(4, 2), 16));
        }

        public static string ColorToHex(Color c)
        {
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        public static Color LightenForLightTheme(Color c)
        {
            const double keep = 0.22;
            return Color.FromArgb(255,
                                  (byte)Math.Round(255 - (255 - (int)c.R) * keep, MidpointRounding.ToEven),
                                  (byte)Math.Round(255 - (255 - (int)c.G) * keep, MidpointRounding.ToEven),
                                  (byte)Math.Round(255 - (255 - (int)c.B) * keep, MidpointRounding.ToEven));
        }
    }
}
