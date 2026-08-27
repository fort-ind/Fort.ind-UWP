using System;
using System.Collections.Generic;
using Windows.UI;

namespace Fort.ind_UWP
{
    /// <summary>
    /// Colour maths for the appearance tint. Pure functions and one lookup table, with no
    /// dependency on any page, so the arithmetic is readable on its own and the light-theme
    /// rounding rule below lives somewhere it will not be "tidied up" by accident.
    /// </summary>
    public static class ColorHelper
    {

        /// <summary>
        /// Light-mode equivalents for each dark preset tint color. Custom colors aren't in here -
        /// they fall back to <see cref="LightenForLightTheme"/>, which computes an approximation
        /// of the same pastel treatment these hand-picked values use.
        /// </summary>
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

        /// <summary>
        /// The hand-picked light-theme shade for a preset tint, or null when the value is not one
        /// of the presets (i.e. the user picked it themselves).
        /// </summary>
        public static string TryGetLightPreset(string darkHex)
        {
            if (darkHex == null) return null;

            string lightHex;
            return s_lightTintMap.TryGetValue(darkHex, out lightHex) ? lightHex : null;
        }

        /// <summary>
        /// The colour a stored (dark) tint hex should actually paint in light theme: the
        /// hand-picked preset shade when there is one, otherwise the computed approximation.
        /// </summary>
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

        /// <summary>
        /// Approximates the pastel light-theme shade that <see cref="s_lightTintMap"/> stores by
        /// hand for the presets, for colors the user picked themselves. Blending most of the way
        /// to white keeps the hue but stops a saturated pick from turning a light window murky.
        /// </summary>
        public static Color LightenForLightTheme(Color c)
        {
            const double keep = 0.22;
            // VB's CByte rounds half-to-even, where a plain (byte) cast in C# would truncate -
            // that shifts computed light-theme tints by a level (e.g. 205.5 -> 206 vs 205), so
            // the rounding has to be spelled out to keep these colours identical.
            return Color.FromArgb(255,
                                  (byte)Math.Round(255 - (255 - (int)c.R) * keep, MidpointRounding.ToEven),
                                  (byte)Math.Round(255 - (255 - (int)c.G) * keep, MidpointRounding.ToEven),
                                  (byte)Math.Round(255 - (255 - (int)c.B) * keep, MidpointRounding.ToEven));
        }

    }
}
