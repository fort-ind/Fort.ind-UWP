using System;
using System.Diagnostics;
using Windows.ApplicationModel.Resources;
using Windows.UI.Core;

namespace Fort.ind_UWP
{
    /// <summary>
    /// Reads display text out of Strings\en-US\Resources.resw. XAML gets its strings through
    /// x:Uid without touching this; this is for the text that is only ever built in code -
    /// dialog bodies, status lines, the jump list task table.
    ///
    /// ResourceLoader.GetForCurrentView throws "may not be created on threads that do not have
    /// a CoreWindow" off the UI thread, which the localization docs call out explicitly. Some
    /// callers here (the live tile, the jump list) run from a Dispatcher continuation or a
    /// background restore, so the loader is fetched behind that guard and every lookup degrades
    /// to the resource key rather than throwing - a visibly wrong label beats a crash.
    /// </summary>
    public static class LocalizedStrings
    {

        private static ResourceLoader s_loader;

        private static ResourceLoader Loader
        {
            get
            {
                if (s_loader != null) return s_loader;

                // Documented guard: GetForCurrentView requires a CoreWindow on the calling thread.
                if (CoreWindow.GetForCurrentThread() == null) return null;

                try
                {
                    s_loader = ResourceLoader.GetForCurrentView();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"LocalizedStrings: could not open the resource loader - {ex.Message}");
                }

                return s_loader;
            }
        }

        /// <summary>
        /// The string for <paramref name="key"/>, or the key itself if it cannot be resolved.
        /// Resource names containing dots (property identifiers) are addressed from code with
        /// forward slashes - "Fare/Well" for a resw entry named "Fare.Well".
        /// </summary>
        public static string Get(string key)
        {
            var loader = Loader;
            if (loader == null) return key;

            try
            {
                var value = loader.GetString(key);
                return string.IsNullOrEmpty(value) ? key : value;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LocalizedStrings: no resource for '{key}' - {ex.Message}");
                return key;
            }
        }

        /// <summary>
        /// The string for <paramref name="key"/> with positional placeholders filled in. The
        /// placeholders live in the resource value so a translator can reorder them.
        /// </summary>
        public static string Format(string key, params object[] args)
        {
            var pattern = Get(key);

            try
            {
                return string.Format(pattern, args);
            }
            catch (FormatException ex)
            {
                // A translation whose placeholders don't match the arguments must not take the
                // app down; show the unformatted pattern instead.
                Debug.WriteLine($"LocalizedStrings: '{key}' has malformed placeholders - {ex.Message}");
                return pattern;
            }
        }

    }
}
