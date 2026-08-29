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
    public sealed partial class GamesPage : Page
    {
        private const string DigitGroupKey = "#";

        private enum GamesPageState
        {
            Loading,
            Content,
            Empty,
            Failed
        }

        private readonly ObservableCollection<GameGroup> _groups = new ObservableCollection<GameGroup>();

        private readonly CollectionViewSource _viewSource;

        private IReadOnlyList<SearchItem> _allGames = Array.Empty<SearchItem>();

        private CancellationTokenSource _filterDebounceCts;

        private bool _dataLoaded = false;

        private bool _loadInProgress = false;

        public GamesPage()
        {
            this.InitializeComponent();

            this.NavigationCacheMode = Windows.UI.Xaml.Navigation.NavigationCacheMode.Required;

            _viewSource = (CollectionViewSource)Resources["GamesViewSource"];
            _viewSource.Source = _groups;
            EnsureItemsSources();

            AddSemanticZoomAccelerators();

            Loaded += GamesPage_Loaded;
            Unloaded += GamesPage_Unloaded;
        }

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

        private async void LoadGamesAsync()
        {
            if (_loadInProgress) return;
            _loadInProgress = true;

            try
            {
                SetState(GamesPageState.Loading);

                var games = await SitemapService.LoadGameItemsAsync();

                List<SearchItem> sorted = new List<SearchItem>(games);
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

        private void RebuildGroups(IEnumerable<SearchItem> matches)
        {
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

        private static string GroupKeyFor(string title)
        {
            if (string.IsNullOrEmpty(title)) return DigitGroupKey;
            var first = char.ToUpperInvariant(title[0]);
            if (first >= 'A' && first <= 'Z') return first.ToString();
            return DigitGroupKey;
        }

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

                await WebLauncher.LaunchAsync(item.Url);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GamesPage: Failed to launch game - {ex.Message}");
            }
        }

        private void ZoomOutAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            SetZoomedInViewActive(false, args);
        }

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
            var cts = _filterDebounceCts;
            _filterDebounceCts = null;
            if (cts == null) return;

            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            cts.Dispose();
        }
    }
}
