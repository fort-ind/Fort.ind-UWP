using System;
using System.Collections.Generic;

namespace Fort.ind_UWP
{
    /// <summary>
    /// What the app search box can find, and how a query is matched against it. Pure data and
    /// pure functions - no UI - so the matching runs on a worker thread and can be reasoned
    /// about without the shell in the way.
    /// </summary>
    public static class SearchCatalog
    {

        /// <summary>
        /// Static menu/settings destinations. Never changes, so it doubles as the search index
        /// the box starts with before the sitemap finishes loading.
        /// </summary>
        public static readonly SearchItem[] StaticItems =
        {
            new SearchItem("Home", AppConstants.CategoryMenu, AppConstants.NavigationLatestNews),
            new SearchItem("Latest News", AppConstants.CategoryMenu, AppConstants.NavigationLatestNews),
            new SearchItem("Games", AppConstants.CategoryMenu, AppConstants.NavigationGames),
            new SearchItem("Beta Programs", AppConstants.CategoryMenu, AppConstants.NavigationBetas),
            new SearchItem("Your Profile", AppConstants.CategoryMenu, AppConstants.NavigationProfile),
            new SearchItem("Social", AppConstants.CategoryMenu, AppConstants.NavigationSocial),
            new SearchItem("Settings", AppConstants.CategoryMenu, AppConstants.NavigationSettings),
            new SearchItem("Data Storage", AppConstants.CategorySettings, AppConstants.NavigationSettings),
            new SearchItem("Local JSON Storage", AppConstants.CategorySettings, AppConstants.NavigationSettings),
            new SearchItem("Live Tile", AppConstants.CategorySettings, AppConstants.NavigationSettings),
            new SearchItem("Refresh Live Tile", AppConstants.CategorySettings, AppConstants.NavigationSettings),
            new SearchItem("Clear Live Tile", AppConstants.CategorySettings, AppConstants.NavigationSettings),
            new SearchItem("Welcome Dialog", AppConstants.CategorySettings, AppConstants.NavigationSettings),
            new SearchItem("Show Welcome Dialog Again", AppConstants.CategorySettings, AppConstants.NavigationSettings),
            new SearchItem("Appearance", AppConstants.CategorySettings, AppConstants.NavigationSettings),
            new SearchItem("Theme", AppConstants.CategorySettings, AppConstants.NavigationSettings),
            new SearchItem("Dark Mode", AppConstants.CategorySettings, AppConstants.NavigationSettings),
            new SearchItem("Light Mode", AppConstants.CategorySettings, AppConstants.NavigationSettings),
            new SearchItem("Background Color", AppConstants.CategorySettings, AppConstants.NavigationSettings),
            new SearchItem("Background Tint", AppConstants.CategorySettings, AppConstants.NavigationSettings),
            new SearchItem("Custom Background Tint", AppConstants.CategorySettings, AppConstants.NavigationSettings),
            new SearchItem("Account", AppConstants.CategoryProfile, AppConstants.NavigationProfile),
            new SearchItem("Sign In", AppConstants.CategoryProfile, AppConstants.NavigationProfile)
        };

        /// <summary>
        /// The "Profile: Name" row the search box offers while signed in, or null when signed
        /// out. Resolved separately from <see cref="BuildSuggestions"/> because it needs the
        /// resource loader, which is view-affine and must be read on the UI thread.
        /// </summary>
        public static string BuildProfileResultTitle(UserProfile currentUser)
        {
            if (currentUser == null) return null;

            var name = string.IsNullOrWhiteSpace(currentUser.DisplayName)
                       ? currentUser.Username
                       : currentUser.DisplayName;

            return LocalizedStrings.Format("SearchProfileResultFormat", name);
        }

        /// <summary>
        /// Matches <paramref name="query"/> against title and category, capped at
        /// <see cref="AppConstants.SearchSuggestionLimit"/>. Takes the item list and the
        /// already-resolved profile row as arguments rather than reading either itself, so the
        /// caller can snapshot both on the UI thread before handing this to Task.Run.
        /// </summary>
        public static List<SearchItem> BuildSuggestions(string query, IReadOnlyList<SearchItem> items, string profileResultTitle)
        {
            List<SearchItem> filtered = new List<SearchItem>();
            foreach (var item in items)
            {
                if (item.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Category.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    filtered.Add(item);
                    if (filtered.Count >= AppConstants.SearchSuggestionLimit)
                    {
                        break;
                    }
                }
            }

            // Add profile-specific item if logged in and matches, respecting the suggestion limit
            if (filtered.Count < AppConstants.SearchSuggestionLimit && profileResultTitle != null)
            {
                if (profileResultTitle.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    AppConstants.CategoryProfile.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    filtered.Add(new SearchItem(profileResultTitle, AppConstants.CategoryProfile, AppConstants.NavigationProfile));
                }
            }

            return filtered;
        }

    }
}
