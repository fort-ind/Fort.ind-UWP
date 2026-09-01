using System.Globalization;
using System.Text;

namespace Fort.ind_UWP
{
    /// <summary>
    /// String helpers for user-facing text that must not be cut at an arbitrary char boundary.
    /// </summary>
    public static class TextHelper
    {
        /// <summary>
        /// Returns the first <paramref name="count"/> user-perceived characters of a string.
        /// </summary>
        /// <remarks>
        /// Not <c>Substring(0, count)</c>: a <see cref="string"/> is indexed in UTF-16 code units,
        /// so a display name starting with an emoji is a surrogate pair and slicing one char off it
        /// yields half a code point, which renders as a replacement box. A combining accent has the
        /// same problem one level up. <see cref="StringInfo.GetTextElementEnumerator(string)"/>
        /// walks whole grapheme clusters, which is what "the first letter" means to a reader.
        /// </remarks>
        public static string FirstTextElements(string value, int count)
        {
            if (string.IsNullOrEmpty(value) || count <= 0) return "";

            StringBuilder builder = new StringBuilder();
            var enumerator = StringInfo.GetTextElementEnumerator(value);

            int taken = 0;
            while (taken < count && enumerator.MoveNext())
            {
                builder.Append((string)enumerator.Current);
                taken += 1;
            }

            return builder.ToString();
        }
    }
}
