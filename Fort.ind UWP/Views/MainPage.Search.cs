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
    /// <summary>
    /// The nav pane search box: its accelerators, the typing debounce, and what happens when a
    /// suggestion is chosen. The matching itself lives in <see cref="SearchCatalog"/>.
    /// </summary>
    public sealed partial class MainPage : Page
    {

        // ── Search bar handlers ──

        /// <summary>
        /// Ctrl+F - the standard Find accelerator - moves focus to the nav pane's search box.
        /// Declared in MainPage.xaml; see the comment there for why this is an accelerator rather
        /// than a CoreWindow key handler.
        /// </summary>
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

        /// <summary>
        /// Escape clears the search box, but only when it actually has text - leaving the event
        /// unhandled otherwise so Escape keeps its normal meaning everywhere else on the page
        /// (closing the nav pane's flyout in Minimal mode, for instance).
        /// </summary>
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

                // Capture volatile references on the UI thread before going off-thread. The
                // profile row is resolved here too, not inside the Task.Run: it needs the
                // resource loader, which is view-affine and throws off the UI thread.
                var snapshot = _allSearchItems;
                var profileResult = SearchCatalog.BuildProfileResultTitle(ProfileService.CurrentUser);

                var results = await Task.Run(() => SearchCatalog.BuildSuggestions(query, snapshot, profileResult), cancellationToken);

                if (!cancellationToken.IsCancellationRequested)
                {
                    sender.ItemsSource = results;
                }
            }
            catch (OperationCanceledException)
            {
                // Expected while typing quickly.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Debounced search failed – {ex.Message}");
            }
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
                    // User pressed Enter without picking a suggestion – navigate to first match
                    var query = args.QueryText.Trim();
                    if (!string.IsNullOrEmpty(query))
                    {
                        var items = _allSearchItems;

                        SearchItem match = null;
                        foreach (var i in items)
                        {
                            if (i.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                i.Category.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
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

        // Fixed: removed unnecessary async keyword (no await in this method)
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
            // Null check for item
            if (item == null)
            {
                Debug.WriteLine("MainPage: NavigateToSearchItem called with null item");
                return;
            }

            if (!string.IsNullOrEmpty(item.Url))
            {
                // Only http/https is launched - see WebLauncher.TryCreateWebUri.
                await WebLauncher.LaunchAsync(item.Url);
            }
            else if (!string.IsNullOrEmpty(item.NavigationTag))
            {
                ShowContent(item.NavigationTag);
            }
        }

    }
}
