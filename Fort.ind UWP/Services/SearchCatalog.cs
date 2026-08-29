using System;
using System.Collections.Generic;

namespace Fort.ind_UWP
{
    public static class SearchCatalog
    {
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

        public static string BuildProfileResultTitle(UserProfile currentUser)
        {
            if (currentUser == null) return null;

            var name = string.IsNullOrWhiteSpace(currentUser.DisplayName)
                       ? currentUser.Username
                       : currentUser.DisplayName;

            return LocalizedStrings.Format("SearchProfileResultFormat", name);
        }

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
