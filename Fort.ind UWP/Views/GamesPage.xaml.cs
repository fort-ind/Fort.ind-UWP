using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;

namespace Fort.ind_UWP
{
    /// <summary>
    /// Browse-all-games view: every game URL in the bundled sitemap, grouped A–Z (with a "#"
    /// bucket for titles that start with a digit) inside a SemanticZoom, plus an in-page filter.
    /// Page-backed rather than an inline panel because MainPage's ContentScrollViewer gives its
    /// children an unconstrained vertical measure, and a SemanticZoom needs a bounded height -
    /// without one the list would realise every row and the zoomed-out view would have nothing
    /// to render into.
    /// </summary>
    public sealed partial class GamesPage : Page
    {

        /// <summary>
        /// Bucket for every title that does not start with A–Z. Sorts before the letters.
        /// </summary>
        private const string DigitGroupKey = "#";

        private enum GamesPageState
        {
            Loading,
            Content,
            Empty,
            Failed
        }

        /// <summary>
        /// The one collection the CollectionViewSource is bound to. Its identity never changes for
        /// the life of the page - the filter mutates it in place - so cvs.View is created once and
        /// both SemanticZoom views' bindings stay live.
        /// </summary>
        private readonly ObservableCollection<GameGroup> _groups = new ObservableCollection<GameGroup>();

        private readonly CollectionViewSource _viewSource;

        /// <summary>Every game, sorted once on load; the filter only ever reads from this.</summary>
        private IReadOnlyList<SearchItem> _allGames = Array.Empty<SearchItem>();

        /// <summary>Cancels stale filter work while the user is still typing.</summary>
        private CancellationTokenSource _filterDebounceCts;

        /// <summary>
        /// Guards the one-time load against UWP firing Loaded more than once, and against the page
        /// being revisited while cached (NavigationCacheMode.Required).
        /// </summary>
        private bool _dataLoaded = false;

        /// <summary>Prevents two overlapping loads if the user hammers Try again.</summary>
        private bool _loadInProgress = false;

        public GamesPage()
        {
            this.InitializeComponent();

            // Keep the parsed groups, scroll position and filter text when the user switches to
            // another nav item and back, instead of rebuilding from scratch each time.
            this.NavigationCacheMode = Windows.UI.Xaml.Navigation.NavigationCacheMode.Required;

            // Source is assigned here, AFTER InitializeComponent has applied IsSourceGrouped and
            // ItemsPath from markup. Assigning Source before those two produces an ungrouped view
            // with no error - empty headers and an empty jump grid, and nothing to diagnose from.
            _viewSource = (CollectionViewSource)Resources["GamesViewSource"];
            _viewSource.Source = _groups;
            EnsureItemsSources();

            AddSemanticZoomAccelerators();

            Loaded += GamesPage_Loaded;
            Unloaded += GamesPage_Unloaded;
        }

        /// <summary>
        /// Ctrl+Plus / Ctrl+Minus - the shortcuts Windows documents for semantic zoom. Before
        /// this the A-Z jump grid was reachable only by mouse (the zoom-out button SemanticZoom
        /// shows on pointer input) or by pinching.
        ///
        /// Built here rather than in markup because two of the four keys have no name in the
        /// VirtualKey enum: the +/- on the main keyboard row are VK_OEM_PLUS (187) and
        /// VK_OEM_MINUS (189), while Add/Subtract are the numeric keypad. A raw number in a XAML
        /// enum attribute passes the XAML compiler and then throws when the page is parsed, which
        /// takes down the entire page rather than just the accelerator.
        ///
        /// Direction follows the rest of Windows: minus zooms out to the letter grid, plus zooms
        /// back in to the full list.
        /// </summary>
        private void AddSemanticZoomAccelerators()
        {
            const int VirtualKeyOemPlus = 187;
            const int VirtualKeyOemMinus = 189;

            AddAccelerator(VirtualKey.Add, ZoomInAccelerator_Invoked);
            AddAccelerator((VirtualKey)VirtualKeyOemPlus, ZoomInAccelerator_Invoked);
            AddAccelerator(VirtualKey.Subtract, ZoomOutAccelerator_Invoked);
            AddAccelerator((VirtualKey)VirtualKeyOemMinus, ZoomOutAccelerator_Invoked);
        }

        private void AddAccelerator(VirtualKey key, TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> handler)
        {
            var accelerator = new KeyboardAccelerator();
            accelerator.Modifiers = VirtualKeyModifiers.Control;
            accelerator.Key = key;
            accelerator.Invoked += handler;
            KeyboardAccelerators.Add(accelerator);
        }

        /// <summary>
        /// Points both SemanticZoom views at the shared grouped view.
        /// Done in code rather than as a XAML binding: {Binding Path=View} on a
        /// CollectionViewSource resolves during InitializeComponent - before Source has been
        /// assigned, so View is still null - and View does not reliably re-notify afterwards,
        /// which leaves both views permanently and silently empty.
        /// Assigning imperatively is safe here precisely because there is nothing to refresh:
        /// Source is set exactly once and _groups' identity never changes, so this view object
        /// stays valid for the life of the page while the filter mutates the groups underneath it.
        /// Idempotent, so it can be called again from Loaded as a safety net.
        /// </summary>
        private void EnsureItemsSources()
        {
            var view = _viewSource.View;
            if (view == null) return;

            if (GamesList.ItemsSource == null)
            {
                GamesList.ItemsSource = view;
            }
            if (GamesJumpGrid.ItemsSource == null)
            {
                GamesJumpGrid.ItemsSource = view.CollectionGroups;
            }
        }

        private void GamesPage_Loaded(object sender, RoutedEventArgs e)
        {
            // If the view was not ready in the constructor, it certainly is by now.
            EnsureItemsSources();

            if (_dataLoaded) return;
            LoadGamesAsync();
        }

        private void GamesPage_Unloaded(object sender, RoutedEventArgs e)
        {
            CancelPendingFilter();
        }

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            LoadGamesAsync();
        }

        /// <summary>
        /// Pulls the game subset out of the sitemap, sorts it once, and hands it to the filter.
        /// async void, so every path is wrapped - an unhandled exception here would crash the app.
        /// </summary>
        private async void LoadGamesAsync()
        {
            if (_loadInProgress) return;
            _loadInProgress = true;

            try
            {
                SetState(GamesPageState.Loading);

                var games = await SitemapService.LoadGameItemsAsync();

                List<SearchItem> sorted = new List<SearchItem>(games);
                // Ordinal so the sort order agrees with GroupKeyFor's A–Z test: a title that lands
                // in the "#" bucket must also sort before every lettered one, or a group's items
                // would not be contiguous in the sorted sequence.
                sorted.Sort((left, right) => string.Compare(left.Title, right.Title, StringComparison.OrdinalIgnoreCase));
                _allGames = sorted.AsReadOnly();
                _dataLoaded = true;

                ApplyFilter(FilterBox.Text);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GamesPage: Failed to load games - {ex.Message}");
                SetState(GamesPageState.Failed);
            }
            finally
            {
                _loadInProgress = false;
            }
        }

        private void FilterBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

            CancelPendingFilter();

            CancellationTokenSource cts = new CancellationTokenSource();
            _filterDebounceCts = cts;
            ApplyFilterDebouncedAsync(sender.Text, cts.Token);
        }

        /// <summary>
        /// Debounced so a fast typist does not rebuild every group on each keystroke.
        /// async void, so everything is wrapped.
        /// </summary>
        private async void ApplyFilterDebouncedAsync(string query, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(AppConstants.SearchDebounceMilliseconds, cancellationToken);

                if (cancellationToken.IsCancellationRequested) return;
                if (!_dataLoaded) return;

                ApplyFilter(query);
            }
            catch (OperationCanceledException)
            {
                // Expected while typing quickly.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GamesPage: Filter failed - {ex.Message}");
            }
        }

        private void ApplyFilter(string query)
        {
            var trimmed = (query ?? string.Empty).Trim();

            List<SearchItem> matches;
            if (trimmed.Length == 0)
            {
                matches = new List<SearchItem>(_allGames);
            }
            else
            {
                matches = new List<SearchItem>();
                foreach (var item in _allGames)
                {
                    if (item.Title.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matches.Add(item);
                    }
                }
            }

            RebuildGroups(matches);
            UpdateCountText(matches.Count);

            if (_allGames.Count == 0)
            {
                SetState(GamesPageState.Failed);
            }
            else if (matches.Count == 0)
            {
                EmptyText.Text = $"No games match '{trimmed}'";
                SetState(GamesPageState.Empty);
            }
            else
            {
                SetState(GamesPageState.Content);
            }
        }

        /// <summary>
        /// Replaces the contents of the bound group collection with the groups for
        /// <paramref name="matches"/>. This is an in-place mutation of the collection the
        /// CollectionViewSource is bound to - cvs.Source is never reassigned, so cvs.View, and
        /// therefore both SemanticZoom views' bindings, survive untouched.
        /// A single Clear() raises one Reset, which both views handle by rebuilding wholesale.
        /// Removing groups one at a time instead is the fragile path: the jump GridView holds
        /// ICollectionViewGroup references that can be invalidated mid-transition.
        /// </summary>
        private void RebuildGroups(IEnumerable<SearchItem> matches)
        {
            // The jump grid points into the *current* grouping, so snap back to the list before the
            // groups underneath it change - otherwise SemanticZoom can be asked to scroll to a
            // group that no longer exists.
            if (!GamesZoom.IsZoomedInViewActive)
            {
                GamesZoom.IsZoomedInViewActive = true;
            }

            _groups.Clear();
            foreach (var group in BuildGroups(matches))
            {
                _groups.Add(group);
            }
        }

        /// <summary>
        /// Buckets an already-sorted item sequence into alphabetical groups. Uses a dictionary
        /// rather than a "did the first letter change" walk so a non-contiguous key could never
        /// produce two groups sharing a header.
        /// </summary>
        private static List<GameGroup> BuildGroups(IEnumerable<SearchItem> items)
        {
            Dictionary<string, GameGroup> lookup = new Dictionary<string, GameGroup>(StringComparer.Ordinal);
            List<GameGroup> ordered = new List<GameGroup>();

            foreach (var item in items)
            {
                var key = GroupKeyFor(item.Title);
                GameGroup group = null;
                if (!lookup.TryGetValue(key, out group))
                {
                    group = new GameGroup(key);
                    lookup.Add(key, group);
                    ordered.Add(group);
                }
                group.Items.Add(item);
            }

            ordered.Sort((left, right) => CompareGroupKeys(left.Key, right.Key));
            return ordered;
        }

        /// <summary>"#" for digits and anything outside A–Z, otherwise the uppercased first letter.</summary>
        private static string GroupKeyFor(string title)
        {
            if (string.IsNullOrEmpty(title)) return DigitGroupKey;
            var first = char.ToUpperInvariant(title[0]);
            if (first >= 'A' && first <= 'Z') return first.ToString();
            return DigitGroupKey;
        }

        /// <summary>"#" always sorts first; everything else is plain ordinal.</summary>
        private static int CompareGroupKeys(string left, string right)
        {
            if (string.Equals(left, right, StringComparison.Ordinal)) return 0;
            if (string.Equals(left, DigitGroupKey, StringComparison.Ordinal)) return -1;
            if (string.Equals(right, DigitGroupKey, StringComparison.Ordinal)) return 1;
            return string.Compare(left, right, StringComparison.Ordinal);
        }

        private void UpdateCountText(int shownCount)
        {
            var total = _allGames.Count;

            // Singular is a separate resource rather than a formatted "1 game(s)": a translator
            // needs to be able to change the whole sentence, not just the number in it.
            if (shownCount != total)
            {
                CountText.Text = LocalizedStrings.Format("GamesCountFilteredFormat", shownCount, total);
            }
            else if (total == 1)
            {
                CountText.Text = LocalizedStrings.Get("GamesCountOne");
            }
            else
            {
                CountText.Text = LocalizedStrings.Format("GamesCountAllFormat", total);
            }
        }

        /// <summary>
        /// Exactly one of the four states is on screen at a time. The filter box stays usable in
        /// the Empty state so the user can back out of a filter that matched nothing.
        /// </summary>
        private void SetState(GamesPageState state)
        {
            GamesZoom.Visibility = state == GamesPageState.Content ? Visibility.Visible : Visibility.Collapsed;
            LoadingPanel.Visibility = state == GamesPageState.Loading ? Visibility.Visible : Visibility.Collapsed;
            EmptyPanel.Visibility = state == GamesPageState.Empty ? Visibility.Visible : Visibility.Collapsed;
            ErrorPanel.Visibility = state == GamesPageState.Failed ? Visibility.Visible : Visibility.Collapsed;

            LoadingRing.IsActive = (state == GamesPageState.Loading);
            FilterBox.IsEnabled = (state == GamesPageState.Content || state == GamesPageState.Empty);
        }

        private async void GamesList_ItemClick(object sender, ItemClickEventArgs e)
        {
            try
            {
                var item = e.ClickedItem as SearchItem;
                if (item == null || string.IsNullOrEmpty(item.Url)) return;

                // Only http/https is launched - see WebLauncher.TryCreateWebUri.
                await WebLauncher.LaunchAsync(item.Url);
            }
            catch (Exception ex)
            {
                // Critical: catch exceptions in async void to prevent app crash
                Debug.WriteLine($"GamesPage: Failed to launch game - {ex.Message}");
            }
        }

        /// <summary>
        /// Ctrl+Minus - switch to the A-Z jump grid. Declared in GamesPage.xaml.
        /// Does nothing unless the list is actually on screen: in the Loading, Empty and Failed
        /// states the SemanticZoom is collapsed, and zooming a hidden control would leave the
        /// page in the zoomed-out state the next time content appeared.
        /// </summary>
        private void ZoomOutAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            SetZoomedInViewActive(false, args);
        }

        /// <summary>Ctrl+Plus - back to the full list. See <see cref="ZoomOutAccelerator_Invoked"/>.</summary>
        private void ZoomInAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            SetZoomedInViewActive(true, args);
        }

        private void SetZoomedInViewActive(bool zoomedIn, KeyboardAcceleratorInvokedEventArgs args)
        {
            try
            {
                if (GamesZoom.Visibility != Visibility.Visible) return;
                if (GamesZoom.IsZoomedInViewActive == zoomedIn)
                {
                    // Already there. Still mark it handled so the keystroke does not fall through
                    // to anything else while the user is holding the shortcut down.
                    args.Handled = true;
                    return;
                }

                GamesZoom.IsZoomedInViewActive = zoomedIn;
                args.Handled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GamesPage: Semantic zoom accelerator failed - {ex.Message}");
            }
        }

        private void CancelPendingFilter()
        {
            // Clear the field before disposing so no later caller can reach the dead source.
            var cts = _filterDebounceCts;
            _filterDebounceCts = null;
            if (cts == null) return;

            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already torn down elsewhere - nothing left to cancel.
            }
            cts.Dispose();
        }

    }
}
