using System;
using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>
        /// Parses a #RRGGBB string, throwing on anything else.
        /// </summary>
        /// <remarks>
        /// Keep using this for the built-in palette, where the input is a literal in this file and
        /// a failure really is a bug. For anything read back from LocalSettings use
        /// <see cref="TryHexToColor"/>: the old version had four distinct ways to throw on a value
        /// a user could have corrupted, and the caller's catch then swallowed it.
        /// </remarks>
        public static Color HexToColor(string hex)
        {
            Color color;
            if (!TryHexToColor(hex, out color))
            {
                throw new FormatException($"'{hex}' is not a #RRGGBB colour.");
            }

            return color;
        }

        /// <summary>
        /// Parses a #RRGGBB string, returning false rather than throwing on malformed input.
        /// </summary>
        public static bool TryHexToColor(string hex, out Color color)
        {
            color = Colors.Transparent;

            if (string.IsNullOrWhiteSpace(hex)) return false;

            var trimmed = hex.Trim().TrimStart('#');
            if (trimmed.Length != 6) return false;

            byte r, g, b;
            if (!TryParseHexByte(trimmed, 0, out r)) return false;
            if (!TryParseHexByte(trimmed, 2, out g)) return false;
            if (!TryParseHexByte(trimmed, 4, out b)) return false;

            color = Color.FromArgb(255, r, g, b);
            return true;
        }

        private static bool TryParseHexByte(string value, int start, out byte result)
        {
            return byte.TryParse(value.Substring(start, 2),
                                 NumberStyles.HexNumber,
                                 CultureInfo.InvariantCulture,
                                 out result);
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

        /// <summary>
        /// Black or white, whichever reads better on <paramref name="background"/>. Used for the
        /// checkmark drawn on the selected tint swatch, which sits on twelve different chip
        /// colours - a fixed foreground fails half of them.
        /// </summary>
        public static Color ContrastingForeground(Color background)
        {
            // WCAG relative luminance. The 0.179 threshold is the point where contrast against
            // black and against white are equal, so it maximises whichever we pick.
            var luminance = 0.2126 * LinearizeChannel(background.R)
                            + 0.7152 * LinearizeChannel(background.G)
                            + 0.0722 * LinearizeChannel(background.B);

            return luminance > 0.179 ? Colors.Black : Colors.White;
        }

        private static double LinearizeChannel(byte channel)
        {
            var v = channel / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
    }
}
