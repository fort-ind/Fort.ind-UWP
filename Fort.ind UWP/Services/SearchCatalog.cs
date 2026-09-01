using System;
using System.Collections.Generic;

namespace Fort.ind_UWP
{
    public static class SearchCatalog
    {
        private static SearchItem[] s_staticItems;

        /// <summary>
        /// The app's own screens and settings, as search results. Call from the UI thread.
        /// </summary>
        /// <remarks>
        /// A method rather than the static readonly array this used to be, because every title in
        /// it is now a resource lookup. A field initializer runs at type load - whenever something
        /// first touches SearchCatalog, on whatever thread - and LocalizedStrings returns the bare
        /// key off the UI thread, which would have baked resource names into the search list for
        /// the life of the process. Hence the memoization being conditional on the loader really
        /// being there.
        /// </remarks>
        public static SearchItem[] GetStaticItems()
        {
            var cached = s_staticItems;
            if (cached != null) return cached;

            var items = BuildStaticItems();

            if (LocalizedStrings.IsAvailable)
            {
                s_staticItems = items;
            }

            return items;
        }

        private static SearchItem[] BuildStaticItems()
        {
            return new SearchItem[]
            {
                new SearchItem(LocalizedStrings.Get("SearchItemHome"), AppConstants.CategoryMenu, AppConstants.NavigationLatestNews),
                new SearchItem(LocalizedStrings.Get("SearchItemLatestNews"), AppConstants.CategoryMenu, AppConstants.NavigationLatestNews),
                new SearchItem(LocalizedStrings.Get("SearchItemGames"), AppConstants.CategoryMenu, AppConstants.NavigationGames),
                new SearchItem(LocalizedStrings.Get("SearchItemBetaPrograms"), AppConstants.CategoryMenu, AppConstants.NavigationBetas),
                new SearchItem(LocalizedStrings.Get("SearchItemYourProfile"), AppConstants.CategoryMenu, AppConstants.NavigationProfile),
                new SearchItem(LocalizedStrings.Get("SearchItemSocial"), AppConstants.CategoryMenu, AppConstants.NavigationSocial),
                new SearchItem(LocalizedStrings.Get("SearchItemSettings"), AppConstants.CategoryMenu, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemDataStorage"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemLocalJsonStorage"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemLiveTile"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemRefreshLiveTile"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemClearLiveTile"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemWelcomeDialog"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemShowWelcomeDialogAgain"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemAppearance"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemTheme"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemDarkMode"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemLightMode"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemBackgroundColor"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemBackgroundTint"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemCustomBackgroundTint"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemAccount"), AppConstants.CategoryProfile, AppConstants.NavigationProfile),
                new SearchItem(LocalizedStrings.Get("SearchItemSignIn"), AppConstants.CategoryProfile, AppConstants.NavigationProfile)
            };
        }

        /// <summary>
        /// The "Profile: name" suggestion for the signed-in user, or null when signed out.
        /// Must be called on the UI thread.
        /// </summary>
        /// <remarks>
        /// Returns the finished <see cref="SearchItem"/> rather than just its title, because the
        /// SearchItem constructor resolves its category display name through the resw as well.
        /// Building it inside <see cref="BuildSuggestions"/> would run that lookup under Task.Run,
        /// where it would come back as the literal resource key.
        /// </remarks>
        public static SearchItem BuildProfileResultItem(UserProfile currentUser)
        {
            if (currentUser == null) return null;

            var name = string.IsNullOrWhiteSpace(currentUser.DisplayName)
                       ? currentUser.Username
                       : currentUser.DisplayName;

            return new SearchItem(LocalizedStrings.Format("SearchProfileResultFormat", name),
                                  AppConstants.CategoryProfile,
                                  AppConstants.NavigationProfile);
        }

        /// <summary>
        /// Pure query matcher - no resource lookups, so it is safe to run off the UI thread.
        /// </summary>
        public static List<SearchItem> BuildSuggestions(string query, IReadOnlyList<SearchItem> items, SearchItem profileResult)
        {
            List<SearchItem> filtered = new List<SearchItem>();
            foreach (var item in items)
            {
                if (Matches(item, query))
                {
                    filtered.Add(item);
                    if (filtered.Count >= AppConstants.SearchSuggestionLimit)
                    {
                        break;
                    }
                }
            }

            if (filtered.Count < AppConstants.SearchSuggestionLimit &&
                profileResult != null &&
                Matches(profileResult, query))
            {
                filtered.Add(profileResult);
            }

            return filtered;
        }

        private static bool Matches(SearchItem item, string query)
        {
            if (item == null) return false;

            return (item.Title != null && item.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                   || (item.Category != null && item.Category.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
