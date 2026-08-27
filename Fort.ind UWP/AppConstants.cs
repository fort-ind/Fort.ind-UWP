namespace Fort.ind_UWP
{
    /// <summary>
    /// Centralized app constants to avoid repeated literals and drift. Constants only - the
    /// behaviour that used to live here moved out to <see cref="WebLauncher"/> (URL launching)
    /// and <see cref="DialogService"/> (XamlRoot probing).
    ///
    /// Nav tags, category names, LocalSettings keys and timings all belong here rather than
    /// inline at their use sites. Display text does not: that lives in
    /// Strings\en-US\Resources.resw.
    /// </summary>
    public sealed class AppConstants
    {

        private AppConstants()
        {
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
        // The nav item the user was last looking at, so a resume from termination comes back to
        // it instead of to Home. Written on every content switch rather than at suspend, which is
        // the same eager approach the panel-expansion keys above use.
        public const string SettingLastNavTag = "LastNavTag";

        // Prefix on every jump list task's argument string, so an argument the app put there is
        // distinguishable from any other way the app can be launched with arguments. The rest of
        // the string is a navigation tag; JumpListService.ResolveNavTag is the only thing that
        // should parse it, because the value arrives from the shell and is therefore untrusted.
        public const string JumpArgumentPrefix = "jump:";

        // Which revision of the jump list task table was last handed to the shell, so SaveAsync -
        // a cross-process call - is skipped on launches that would rewrite an identical list.
        public const string SettingJumpListRevision = "JumpListRevision";

        // Search behavior
        public const int SearchDebounceMilliseconds = 300;
        public const int SearchSuggestionLimit = 15;

        // Sitemap cache
        public const string SitemapCacheFileName = "sitemap_urls.cache";
        public const string SitemapCacheTimestampKey = "SitemapCacheUnixSeconds";
        public const string SitemapCacheAppVersionKey = "SitemapCacheAppVersion";
        public const int SitemapCacheTtlHours = 24;

        // Release channel suffix appended after the numeric version (e.g. "2.2.0 Beta"). Empty
        // for a plain release - the separating space is added only when there is a channel to
        // separate, so the About row does not end in trailing whitespace.
        public const string VersionChannel = "";

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
            string numeric;
            try
            {
                var v = Windows.ApplicationModel.Package.Current.Id.Version;
                numeric = $"{v.Major}.{v.Minor}.{v.Build}";
            }
            catch
            {
                // Unpackaged only. Keep the Major.Minor here in step with Package.appxmanifest's
                // Identity/@Version and AssemblyInfo.cs - all three drift silently otherwise.
                numeric = "2.2.0";
            }

            return string.IsNullOrEmpty(VersionChannel) ? numeric : $"{numeric} {VersionChannel}";
        }

    }
}
