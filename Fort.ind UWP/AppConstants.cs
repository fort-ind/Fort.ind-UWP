using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Foundation.Metadata;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    /// <summary>
    /// Centralized app constants to avoid repeated literals and drift.
    /// </summary>
    public sealed class AppConstants
    {

        private AppConstants()
        {
        }

        /// <summary>
        /// UIElement.XamlRoot was added in Windows 10 1903 (10.0.18362.0). This app's
        /// TargetPlatformMinVersion is 1809 (10.0.17763.0), where the property doesn't exist -
        /// reading or setting it throws, which every ContentDialog call site swallows in a
        /// try/catch, so on 1809 dialogs silently never appear. A single-window UWP app shows
        /// ContentDialogs fine with XamlRoot left unset, so just skip it when unsupported.
        /// </summary>
        private static readonly bool s_xamlRootSupported =
            ApiInformation.IsPropertyPresent("Windows.UI.Xaml.UIElement", "XamlRoot");

        public static void ApplyXamlRoot(ContentDialog dialog, UIElement owner)
        {
            if (s_xamlRootSupported)
            {
                dialog.XamlRoot = owner.XamlRoot;
            }
        }

        /// <summary>
        /// Parses a string into a web URI, accepting only http/https.
        ///
        /// Every URL the app launches originates outside the code: the bundled sitemap, the
        /// plain-text URL cache in LocalFolder, or profile JSON returned by the instance. Handing
        /// any of those straight to Launcher.LaunchUriAsync means one click can invoke *any*
        /// registered protocol on the machine - "ms-settings:", "file:", "shell:", or another
        /// installed app's custom scheme - because Uri.TryCreate(..., Absolute) happily accepts
        /// all of them. A browsable link is the only thing any of these call sites ever intends,
        /// so anything else is rejected here rather than at each call site.
        /// Returns null if the value is not a well-formed http/https URL.
        /// </summary>
        public static Uri TryCreateWebUri(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            Uri uri = null;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri)) return null;

            if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.IsNullOrEmpty(uri.Host)) return null;

            return uri;
        }

        /// <summary>
        /// Opens a URL in the user's browser, but only if it is a well-formed http/https URL.
        /// See <see cref="TryCreateWebUri"/> for why the scheme is checked. Returns False (and
        /// launches nothing) for anything else.
        /// </summary>
        public static async Task<bool> LaunchWebUriAsync(string value)
        {
            var uri = TryCreateWebUri(value);
            if (uri == null)
            {
                Debug.WriteLine($"AppConstants: refused to launch non-web URI - {value}");
                return false;
            }

            return await Windows.System.Launcher.LaunchUriAsync(uri);
        }

        // Search categories
        public const string CategoryMenu = "Menu";
        public const string CategorySettings = "Settings";
        public const string CategoryProfile = "Profile";
        public const string CategoryGames = "Games";
        public const string CategorySocial = "Social";
        public const string CategoryEmulators = "Emulators";
        public const string CategoryApps = "Apps";
        public const string CategoryExtras = "Extras";
        public const string CategoryLabsAndBetas = "Labs & Betas";
        public const string CategoryFortWebsite = "fort1nd.com";

        // Navigation tags
        public const string NavigationLatestNews = "LatestNews";
        public const string NavigationGames = "Games";
        public const string NavigationBetas = "Betas";
        public const string NavigationProfile = "Profile";
        public const string NavigationSocial = "Social";
        public const string NavigationSettings = "Settings";

        // Theme values
        public const string ThemeDefault = "Default";
        public const string ThemeLight = "Light";
        public const string ThemeDark = "Dark";

        // LocalSettings keys
        public const string SettingHideWelcomeDialog = "HideWelcomeDialog";
        public const string SettingAppTheme = "AppTheme";
        public const string SettingAppTintColor = "AppTintColor";
        // Remembers the last color picked in the custom-tint dialog so the custom swatch keeps
        // showing it even while a preset is the active tint.
        public const string SettingAppCustomTintColor = "AppCustomTintColor";
        public const string SettingSettingsAppearanceExpanded = "SettingsAppearanceExpanded";
        public const string SettingSettingsStorageExpanded = "SettingsStorageExpanded";
        public const string SettingSettingsTileExpanded = "SettingsTileExpanded";
        // Whether the app may show a badge on its tile / taskbar icon. Absent means "on".
        public const string SettingShowTileBadge = "ShowTileBadge";
        public const string SettingSettingsWelcomeExpanded = "SettingsWelcomeExpanded";
        public const string SettingSettingsAboutExpanded = "SettingsAboutExpanded";

        // Search behavior
        public const int SearchDebounceMilliseconds = 300;
        public const int SearchSuggestionLimit = 15;

        // Sitemap cache
        public const string SitemapCacheFileName = "sitemap_urls.cache";
        public const string SitemapCacheTimestampKey = "SitemapCacheUnixSeconds";
        public const string SitemapCacheAppVersionKey = "SitemapCacheAppVersion";
        public const int SitemapCacheTtlHours = 24;

        // Release channel suffix appended after the numeric version (e.g. "0.5.0 Beta")
        public const string VersionChannel = " ";

        /// <summary>
        /// The app version pulled from the package manifest, formatted as "Major.Minor.Build".
        /// Falls back to a static string if the package identity is unavailable (e.g. unpackaged).
        /// Single source of truth so the About screen never drifts from the manifest.
        ///
        /// Resolved once and cached: Package.Current is a cross-process call, and this is read on
        /// the startup path (twice per sitemap cache check, plus the About row).
        /// </summary>
        public static string AppVersionDisplay
        {
            get
            {
                return s_appVersionDisplay;
            }
        }

        private static readonly string s_appVersionDisplay = ResolveAppVersionDisplay();

        private static string ResolveAppVersionDisplay()
        {
            try
            {
                var v = Windows.ApplicationModel.Package.Current.Id.Version;
                return $"{v.Major}.{v.Minor}.{v.Build} {VersionChannel}";
            }
            catch
            {
                return $"2.1.0 {VersionChannel}";
            }
        }

    }
}
