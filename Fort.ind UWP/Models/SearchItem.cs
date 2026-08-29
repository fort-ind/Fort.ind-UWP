namespace Fort.ind_UWP
{
    public class SearchItem
    {
        public string Title { get; set; }

        public string Category { get; set; }

        public string NavigationTag { get; set; }

        public string Url { get; set; }

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
            if (category == AppConstants.CategoryMenu) return "\uE700";
            if (category == AppConstants.CategorySettings) return "\uE713";
            if (category == AppConstants.CategoryProfile) return "\uE77B";
            if (category.StartsWith(AppConstants.CategoryGames)) return "\uE768";
            if (category == AppConstants.CategorySocial) return "\uE716";
            if (category == AppConstants.CategoryEmulators) return "\uE768";
            if (category.StartsWith(AppConstants.CategoryApps)) return "\uE71D";
            if (category == AppConstants.CategoryExtras) return "\uE734";
            if (category == AppConstants.CategoryLabsAndBetas) return "\uE9D9";
            return "\uE774";
        }

        public override string ToString()
        {
            return $"{Title}  —  {Category}";
        }
    }
}
