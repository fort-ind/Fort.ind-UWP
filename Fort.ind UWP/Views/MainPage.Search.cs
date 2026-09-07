using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace Fort.ind_UWP
{
    public sealed partial class MainPage : Page
    {
        private void FocusSearchAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            try
            {
                NavSearchBox.Focus(FocusState.Keyboard);
                args.Handled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Focus search accelerator failed - {ex.Message}");
            }
        }

        private void ClearSearchAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            try
            {
                if (string.IsNullOrEmpty(NavSearchBox.Text)) return;

                _searchDebounce.Cancel();
                NavSearchBox.Text = "";
                NavSearchBox.ItemsSource = null;
                args.Handled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Clear search accelerator failed - {ex.Message}");
            }
        }

        private void NavSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                var query = sender.Text.Trim();

                if (string.IsNullOrEmpty(query))
                {
                    _searchDebounce.Cancel();
                    sender.ItemsSource = null;
                    return;
                }

                ApplySearchSuggestionsAsync(sender, query, _searchDebounce.Restart());
            }
        }

        private async void ApplySearchSuggestionsAsync(AutoSuggestBox sender, string query, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(AppConstants.SearchDebounceMilliseconds, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                var snapshot = _allSearchItems;

                // Built here, on the UI thread, and passed into the Task.Run below: it needs the
                // resource loader, which is unavailable off the UI thread.
                var profileResult = SearchCatalog.BuildProfileResultItem(ProfileService.CurrentUser);

                var results = await Task.Run(() => SearchCatalog.BuildSuggestions(query, snapshot, profileResult), cancellationToken);

                if (!cancellationToken.IsCancellationRequested)
                {
                    sender.ItemsSource = results;
                    AnnounceSearchResultCount(results.Count);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Debounced search failed – {ex.Message}");
            }
        }

        /// <summary>
        /// Speaks the size of the suggestion list. Replacing an AutoSuggestBox's ItemsSource
        /// raises nothing a screen reader reports, and the empty case is the one that matters:
        /// without this, typing a query that matches nothing is indistinguishable from typing one
        /// whose results have not arrived yet.
        /// </summary>
        private void AnnounceSearchResultCount(int count)
        {
            string message;
            if (count == 0)
            {
                message = LocalizedStrings.Get("SearchResultsNone");
            }
            else if (count == 1)
            {
                // A separate resource rather than a "1" substituted into the plural string:
                // languages differ on which counts take which form, and a format string here
                // would force every translator into the wrong one.
                message = LocalizedStrings.Get("SearchResultsOne");
            }
            else
            {
                message = LocalizedStrings.Format("SearchResultsCountFormat", count);
            }

            AutomationHelper.AnnounceStatus(NavSearchBox, message, "FortIndSearchResults");
        }

        private async void NavSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            try
            {
                if (args.ChosenSuggestion != null)
                {
                    var item = args.ChosenSuggestion as SearchItem;
                    if (item != null)
                    {
                        await NavigateToSearchItem(item);
                    }
                }
                else
                {
                    var query = args.QueryText.Trim();
                    if (!string.IsNullOrEmpty(query))
                    {
                        var items = _allSearchItems;

                        SearchItem match = null;
                        foreach (var i in items)
                        {
                            if ((i.Title != null && i.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (i.Category != null && i.Category.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                match = i;
                                break;
                            }
                        }
                        if (match != null)
                        {
                            await NavigateToSearchItem(match);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Search query failed – {ex.Message}");
            }
        }

        private void NavSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            var item = args.SelectedItem as SearchItem;
            if (item != null)
            {
                sender.Text = item.Title;
            }
        }

        private async Task NavigateToSearchItem(SearchItem item)
        {
            if (item == null)
            {
                Debug.WriteLine("MainPage: NavigateToSearchItem called with null item");
                return;
            }

            if (!string.IsNullOrEmpty(item.Url))
            {
                await WebLauncher.LaunchAsync(item.Url);
            }
            else if (!string.IsNullOrEmpty(item.NavigationTag))
            {
                ShowContent(item.NavigationTag);
            }
        }
    }
}
