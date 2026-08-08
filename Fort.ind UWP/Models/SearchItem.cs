namespace Fort.ind_UWP
{
    /// <summary>
    /// Represents a searchable item in the app search bar
    /// </summary>
    public class SearchItem
    {

        /// <summary>
        /// Display text shown in the suggestion list
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Category label (e.g. "Menu", "Settings", "Profile", "fort1nd.com")
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Navigation tag or URL used when the item is selected
        /// </summary>
        public string NavigationTag { get; set; }

        /// <summary>
        /// Optional URL for external items from fort1nd.com
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// Segoe MDL2 Assets glyph character for the category icon
        /// </summary>
        public string Icon { get; set; }

        public SearchItem(string title, string category, string navigationTag, string url = null)
        {
            this.Title = title;
            this.Category = category;
            this.NavigationTag = navigationTag;
            this.Url = url;
            this.Icon = GetIconGlyph(category);
        }

        private static string GetIconGlyph(string category)
        {
            if (category == AppConstants.CategoryMenu) return "\uE700";                // GlobalNavButton
            if (category == AppConstants.CategorySettings) return "\uE713";            // Setting (gear)
            if (category == AppConstants.CategoryProfile) return "\uE77B";             // Contact (person)
            if (category.StartsWith(AppConstants.CategoryGames)) return "\uE768";      // Play
            if (category == AppConstants.CategorySocial) return "\uE716";              // People
            if (category == AppConstants.CategoryEmulators) return "\uE768";           // Play (gaming)
            if (category.StartsWith(AppConstants.CategoryApps)) return "\uE71D";       // Apps
            if (category == AppConstants.CategoryExtras) return "\uE734";              // Favorite (star)
            if (category == AppConstants.CategoryLabsAndBetas) return "\uE9D9";        // Beaker
            return "\uE774";                                                           // Globe (fort1nd.com + default)
        }

        public override string ToString()
        {
            return $"{Title}  —  {Category}";
        }

    }
}
