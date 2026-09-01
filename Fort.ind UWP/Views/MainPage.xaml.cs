
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class MainPage : Page
    {
        private IReadOnlyList<SearchItem> _allSearchItems = SearchCatalog.GetStaticItems();

        private bool _loadingSettings = false;

        private readonly Debouncer _searchDebounce = new Debouncer();

        private bool _authHandlerAttached = false;

        private bool _themeHandlerAttached = false;

        private bool _titleBarMetricsHandlerAttached = false;

        private bool _systemBackHandlerAttached = false;

        private bool _navViewInitialized = false;

        private string _navAvatarUrl = null;

        public MainPage()
        {
            this.InitializeComponent();
            AboutVersionText.Text = LocalizedStrings.Format("AboutVersionFormat", AppConstants.AppVersionDisplay);
            SetupTitleBar();
            UpdateProfileNavItem();
            LoadSitemapItems();
            LoadAppearanceSettings();

            var ignored = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low,
                                              () => UpdateLiveTile());

            Unloaded += MainPage_Unloaded;
            Loaded += MainPage_Loaded;
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_authHandlerAttached)
            {
                ProfileService.AuthStateChanged += OnAuthStateChanged;
                _authHandlerAttached = true;
            }

            if (!_themeHandlerAttached)
            {
                ActualThemeChanged += OnActualThemeChanged;
                _themeHandlerAttached = true;
            }

            if (!_titleBarMetricsHandlerAttached)
            {
                var coreTitleBar = CoreApplication.GetCurrentView().TitleBar;
                coreTitleBar.LayoutMetricsChanged += OnTitleBarLayoutMetricsChanged;
                _titleBarMetricsHandlerAttached = true;

                ApplyTitleBarLayoutMetrics(coreTitleBar);
            }

            if (!_systemBackHandlerAttached)
            {
                SystemNavigationManager.GetForCurrentView().BackRequested += OnSystemBackRequested;
                _systemBackHandlerAttached = true;
            }

            UpdateProfileNavItem();
        }

        private async void LoadSitemapItems()
        {
            try
            {
                SetSitemapLoadingIndicator(true);

                var sitemapItems = await SitemapService.LoadSearchItemsAsync();
                var staticItems = SearchCatalog.GetStaticItems();
                List<SearchItem> combined = new List<SearchItem>(staticItems.Length + sitemapItems.Count);
                combined.AddRange(staticItems);
                combined.AddRange(sitemapItems);
                _allSearchItems = combined;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to load sitemap items – {ex.Message}");
            }
            finally
            {
                SetSitemapLoadingIndicator(false);
            }
        }

        private void SetSitemapLoadingIndicator(bool active)
        {
            if (LoadingIndicator == null) return;
            LoadingIndicator.IsIndeterminate = active;
            LoadingIndicator.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_authHandlerAttached)
            {
                ProfileService.AuthStateChanged -= OnAuthStateChanged;
                _authHandlerAttached = false;
            }

            if (_themeHandlerAttached)
            {
                ActualThemeChanged -= OnActualThemeChanged;
                _themeHandlerAttached = false;
            }

            if (_titleBarMetricsHandlerAttached)
            {
                try
                {
                    CoreApplication.GetCurrentView().TitleBar.LayoutMetricsChanged -= OnTitleBarLayoutMetricsChanged;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MainPage: Failed to remove title bar metrics handler - {ex.Message}");
                }
                _titleBarMetricsHandlerAttached = false;
            }

            if (_systemBackHandlerAttached)
            {
                try
                {
                    SystemNavigationManager.GetForCurrentView().BackRequested -= OnSystemBackRequested;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MainPage: Failed to remove system back handler - {ex.Message}");
                }
                _systemBackHandlerAttached = false;
            }

            _searchDebounce.Cancel();
        }
    }
}
