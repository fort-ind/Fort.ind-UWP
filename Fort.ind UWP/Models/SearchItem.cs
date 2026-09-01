using System;

namespace Fort.ind_UWP
{
    public class SearchItem
    {
        public string Title { get; set; }

        /// <summary>
        /// Stable, ordinal, never displayed - one of the AppConstants.Category* values.
        /// </summary>
        /// <remarks>
        /// Split out from <see cref="Category"/> because that one value was doing two
        /// incompatible jobs: it was both the grouping/icon key matched with StartsWith and the
        /// text shown under every search result. Localizing it in place would have quietly broken
        /// the games filter and the icon table for every language but English.
        /// </remarks>
        public string CategoryKey { get; set; }

        /// <summary>Localized display name for <see cref="CategoryKey"/>.</summary>
        public string Category { get; set; }

        public string NavigationTag { get; set; }

        public string Url { get; set; }

        public string Icon { get; set; }

        public SearchItem(string title, string categoryKey, string navigationTag, string url = null)
        {
            this.Title = title;
            this.CategoryKey = categoryKey ?? "";
            this.NavigationTag = navigationTag;
            this.Url = url;
            this.Icon = GetIconGlyph(this.CategoryKey);

            // Resolved eagerly rather than from a property getter: SearchCatalog.BuildSuggestions
            // reads Category under Task.Run, and off the UI thread LocalizedStrings degrades to
            // returning the key. Every construction path runs on the UI thread.
            this.Category = GetCategoryDisplayName(this.CategoryKey);
        }

        private static string GetIconGlyph(string categoryKey)
        {
            if (string.IsNullOrEmpty(categoryKey)) return "\uE774";
            if (categoryKey == AppConstants.CategoryMenu) return "\uE700";
            if (categoryKey == AppConstants.CategorySettings) return "\uE713";
            if (categoryKey == AppConstants.CategoryProfile) return "\uE77B";
            if (categoryKey.StartsWith(AppConstants.CategoryGames, StringComparison.Ordinal)) return "\uE768";
            if (categoryKey == AppConstants.CategorySocial) return "\uE716";
            if (categoryKey == AppConstants.CategoryEmulators) return "\uE768";
            if (categoryKey.StartsWith(AppConstants.CategoryApps, StringComparison.Ordinal)) return "\uE71D";
            if (categoryKey == AppConstants.CategoryExtras) return "\uE734";
            if (categoryKey == AppConstants.CategoryLabsAndBetas) return "\uE9D9";
            return "\uE774";
        }

        private static string GetCategoryDisplayName(string categoryKey)
        {
            switch (categoryKey)
            {
                case AppConstants.CategoryMenu: return LocalizedStrings.Get("CategoryMenu");
                case AppConstants.CategorySettings: return LocalizedStrings.Get("CategorySettings");
                case AppConstants.CategoryProfile: return LocalizedStrings.Get("CategoryProfile");
                case AppConstants.CategoryGames: return LocalizedStrings.Get("CategoryGames");
                case AppConstants.CategoryGamesHtml: return LocalizedStrings.Get("CategoryGamesHtml");
                case AppConstants.CategoryGamesFlash: return LocalizedStrings.Get("CategoryGamesFlash");
                case AppConstants.CategoryGamesCodePen: return LocalizedStrings.Get("CategoryGamesCodePen");
                case AppConstants.CategoryGamesRetro: return LocalizedStrings.Get("CategoryGamesRetro");
                case AppConstants.CategoryGamesMinecraft: return LocalizedStrings.Get("CategoryGamesMinecraft");
                case AppConstants.CategorySocial: return LocalizedStrings.Get("CategorySocial");
                case AppConstants.CategoryEmulators: return LocalizedStrings.Get("CategoryEmulators");
                case AppConstants.CategoryApps: return LocalizedStrings.Get("CategoryApps");
                case AppConstants.CategoryAppsAppStone: return LocalizedStrings.Get("CategoryAppsAppStone");
                case AppConstants.CategoryExtras: return LocalizedStrings.Get("CategoryExtras");
                case AppConstants.CategoryLabsAndBetas: return LocalizedStrings.Get("CategoryLabsAndBetas");
                case AppConstants.CategoryFortWebsite: return LocalizedStrings.Get("CategoryFortWebsite");
                default: return categoryKey;
            }
        }

        public override string ToString()
        {
            return $"{Title}  —  {Category}";
        }
    }
}
