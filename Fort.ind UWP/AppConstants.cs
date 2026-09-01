namespace Fort.ind_UWP
{
    public sealed class AppConstants
    {
        private AppConstants()
        {
        }

        // Category KEYS, not display text. They are matched ordinally (SearchItem.GetIconGlyph
        // picks a glyph by prefix, SitemapService filters the games list the same way) and are
        // never shown to anyone - SearchItem.Category resolves the key through the resw for that.
        // Keep the sub-category keys prefixed with their parent so the StartsWith matches hold.
        public const string CategoryMenu = "Menu";
        public const string CategorySettings = "Settings";
        public const string CategoryProfile = "Profile";
        public const string CategoryGames = "Games";
        public const string CategoryGamesHtml = "Games.Html";
        public const string CategoryGamesFlash = "Games.Flash";
        public const string CategoryGamesCodePen = "Games.CodePen";
        public const string CategoryGamesRetro = "Games.Retro";
        public const string CategoryGamesMinecraft = "Games.Minecraft";
        public const string CategorySocial = "Social";
        public const string CategoryEmulators = "Emulators";
        public const string CategoryApps = "Apps";
        public const string CategoryAppsAppStone = "Apps.AppStone";
        public const string CategoryExtras = "Extras";
        public const string CategoryLabsAndBetas = "Labs & Betas";
        public const string CategoryFortWebsite = "fort1nd.com";

        public const string NavigationLatestNews = "LatestNews";
        public const string NavigationGames = "Games";
        public const string NavigationBetas = "Betas";
        public const string NavigationProfile = "Profile";
        public const string NavigationSocial = "Social";
        public const string NavigationSettings = "Settings";

        public const string ThemeDefault = "Default";
        public const string ThemeLight = "Light";
        public const string ThemeDark = "Dark";

        public const string SettingHideWelcomeDialog = "HideWelcomeDialog";
        public const string SettingAppTheme = "AppTheme";
        public const string SettingAppTintColor = "AppTintColor";
        public const string SettingAppCustomTintColor = "AppCustomTintColor";
        public const string SettingSettingsAppearanceExpanded = "SettingsAppearanceExpanded";
        public const string SettingSettingsStorageExpanded = "SettingsStorageExpanded";
        public const string SettingSettingsTileExpanded = "SettingsTileExpanded";
        public const string SettingShowTileBadge = "ShowTileBadge";
        public const string SettingSettingsWelcomeExpanded = "SettingsWelcomeExpanded";
        public const string SettingSettingsAboutExpanded = "SettingsAboutExpanded";
        public const string SettingLastNavTag = "LastNavTag";

        public const string JumpArgumentPrefix = "jump:";

        public const string SettingJumpListRevision = "JumpListRevision";

        public const int SearchDebounceMilliseconds = 300;
        public const int SearchSuggestionLimit = 15;

        public const string SitemapCacheFileName = "sitemap_urls.cache";
        public const string SitemapCacheTimestampKey = "SitemapCacheUnixSeconds";
        public const string SitemapCacheAppVersionKey = "SitemapCacheAppVersion";
        public const int SitemapCacheTtlHours = 24;

        public const string VersionChannel = "";

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
                numeric = "2.2.0";
            }

            return string.IsNullOrEmpty(VersionChannel) ? numeric : $"{numeric} {VersionChannel}";
        }
    }
}
