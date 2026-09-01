using System;
using System.Diagnostics;
using Windows.ApplicationModel.Resources;
using Windows.UI.Core;

namespace Fort.ind_UWP
{
    public static class LocalizedStrings
    {
        private static ResourceLoader s_loader;

        private static ResourceLoader Loader
        {
            get
            {
                if (s_loader != null) return s_loader;

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
        /// True once the resource loader can actually be opened - i.e. on the UI thread.
        /// </summary>
        /// <remarks>
        /// <see cref="Get"/> degrades to returning the key off the UI thread rather than throwing,
        /// which is right for a one-off lookup and wrong for anything that memoizes the result:
        /// caching a key would leave that text reading as a resource name for the rest of the
        /// process. Callers that cache check this first.
        /// </remarks>
        public static bool IsAvailable
        {
            get { return Loader != null; }
        }

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

        public static string Format(string key, params object[] args)
        {
            var pattern = Get(key);

            try
            {
                return string.Format(pattern, args);
            }
            catch (FormatException ex)
            {
                Debug.WriteLine($"LocalizedStrings: '{key}' has malformed placeholders - {ex.Message}");
                return pattern;
            }
        }
    }
}
