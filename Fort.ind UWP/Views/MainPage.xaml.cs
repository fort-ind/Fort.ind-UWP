// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

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
    /// <summary>
    /// The app shell: a NavigationView over two mutually exclusive content hosts - the inline
    /// panels declared in MainPage.xaml, and a Frame for the page-backed views.
    ///
    /// The rest of the class is split across sibling partial files, one per concern:
    /// <list type="bullet">
    ///   <item><description>MainPage.Navigation.cs - nav pane, content switching, back navigation</description></item>
    ///   <item><description>MainPage.TitleBar.cs - the custom title bar and the live tile push</description></item>
    ///   <item><description>MainPage.Appearance.cs - theme and acrylic tint</description></item>
    ///   <item><description>MainPage.Settings.cs - the Settings panel and its dialogs</description></item>
    ///   <item><description>MainPage.Search.cs - the nav pane search box</description></item>
    /// </list>
    /// This file keeps the shared fields and the page lifetime: construction, Loaded, Unloaded.
    /// </summary>
    public sealed partial class MainPage : Page
    {

        // All searchable items – volatile reference swapped once when sitemap loads (no lock
        // needed for reads). Starts as the static menu/settings table so the box works before
        // the sitemap has finished parsing.
        private IReadOnlyList<SearchItem> _allSearchItems = SearchCatalog.StaticItems;

        // Guard to suppress appearance control event handlers during settings load
        private bool _loadingSettings = false;

        // Cancels stale search work while the user is still typing.
        private readonly Debouncer _searchDebounce = new Debouncer();

        // Tracks whether the AuthStateChanged handler is attached, for the same reason - and so
        // the handler is reliably reattached after an Unloaded/Loaded pair instead of leaving
        // this page permanently deaf to sign-in/out.
        private bool _authHandlerAttached = false;

        // Same guard again for ActualThemeChanged, which repaints the window when the system
        // theme flips underneath an app set to "System default".
        private bool _themeHandlerAttached = false;

        // And again for CoreApplicationViewTitleBar.LayoutMetricsChanged, which keeps the custom
        // title bar's height and caption-button insets matching the system's.
        private bool _titleBarMetricsHandlerAttached = false;

        // And for SystemNavigationManager.BackRequested, which is what makes Alt+Left, the mouse
        // back button and the tablet-mode shell back button reach the content Frame. Unlike the
        // others this one is a per-view singleton shared with the rest of the app, so leaving a
        // handler on it outlives the page.
        private bool _systemBackHandlerAttached = false;

        // Guards NavView_Loaded's one-time startup initialization (selecting Home, closing the
        // pane, showing the welcome dialog) against UWP firing Loaded more than once - without
        // this, a second firing would silently snap the user back to the Home tab and re-close
        // the pane no matter what they were looking at.
        private bool _navViewInitialized = false;

        // Avatar URL the profile nav item's icon was last built for, so the repeated
        // UpdateProfileNavItem calls (constructor, Loaded, every AuthStateChanged) don't rebuild
        // the same icon, and so a download that finishes after the account changed can tell.
        private string _navAvatarUrl = null;

        public MainPage()
        {
            this.InitializeComponent();
            AboutVersionText.Text = LocalizedStrings.Format("AboutVersionFormat", AppConstants.AppVersionDisplay);
            SetupTitleBar();
            UpdateProfileNavItem();
            LoadSitemapItems();
            LoadAppearanceSettings();

            // Building the adaptive tile payloads and pushing them to the shell is off the critical
            // path for showing the window, so it runs at Low priority - after layout and first
            // render - rather than inline in the constructor.
            var ignored = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low,
                                              () => UpdateLiveTile());

            Unloaded += MainPage_Unloaded;
            Loaded += MainPage_Loaded;
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Ctrl+F and Escape are KeyboardAccelerators declared in MainPage.xaml - they need no
            // attach/detach here, which is half the reason for using them.
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

                // Catch anything that changed between the constructor's initial apply and here.
                ApplyTitleBarLayoutMetrics(coreTitleBar);
            }

            if (!_systemBackHandlerAttached)
            {
                SystemNavigationManager.GetForCurrentView().BackRequested += OnSystemBackRequested;
                _systemBackHandlerAttached = true;
            }

            // Re-read the current user *after* the handler is attached. Session restore now runs
            // in the background (App.RestoreSessionInBackground), so it can finish either side of
            // this point - if it landed before the handler existed, its AuthStateChanged was
            // raised into the void and only this call puts the account name on the nav item.
            UpdateProfileNavItem();
        }

        /// <summary>
        /// Loads the sitemap into the search index. Called from the constructor, so it starts on
        /// the UI thread and every continuation resumes there too - the indicator is therefore
        /// touched directly rather than through Dispatcher.RunAsync, which only added two queued
        /// round-trips to reach the thread we were already on.
        /// </summary>
        private async void LoadSitemapItems()
        {
            try
            {
                SetSitemapLoadingIndicator(true);

                var sitemapItems = await SitemapService.LoadSearchItemsAsync();
                // Build a new combined list and swap the reference (atomic, no lock needed)
                List<SearchItem> combined = new List<SearchItem>(SearchCatalog.StaticItems.Length + sitemapItems.Count);
                combined.AddRange(SearchCatalog.StaticItems);
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
            LoadingIndicator.IsActive = active;
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
