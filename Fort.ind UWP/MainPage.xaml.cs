// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.ApplicationModel.Core;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {

        // Static menu/settings items (never changes)
        private static readonly SearchItem[] s_staticSearchItems = {
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

        // All searchable items – volatile reference swapped once when sitemap loads (no lock needed for reads)
        private IReadOnlyList<SearchItem> _allSearchItems = s_staticSearchItems;

        // Guard to prevent multiple ContentDialogs from opening simultaneously.
        // Static, and so never disposed, for the reason spelled out on ProfilePage's copy: the
        // scope of "is a dialog open right now" is the process, not one page instance.
        private static readonly SemaphoreSlim _dialogSemaphore = new SemaphoreSlim(1, 1);

        // Guard to suppress appearance control event handlers during settings load
        private bool _loadingSettings = false;

        // Cancels stale search work while the user is still typing.
        private CancellationTokenSource _searchDebounceCts;

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

        // Light-mode equivalents for each dark preset tint color. Custom colors aren't in here -
        // they fall back to LightenForLightTheme(), which computes an approximation of the same
        // pastel treatment these hand-picked values use.
        private static readonly Dictionary<string, string> s_lightTintMap = new Dictionary<string, string>()
        {
            { "#1E3A5F", "#C8E0F5" },
            { "#2D1B69", "#DDD0F5" },
            { "#0F3D2E", "#C5E8D5" },
            { "#3D1515", "#F5CECE" },
            { "#1A1A2E", "#D0D0EA" },
            { "#0E3A3A", "#C5E8E8" },
            { "#3D2A0F", "#F5E3C0" },
            { "#3D1533", "#F5CEE9" },
            { "#2E3D0F", "#DEEBC0" },
            { "#232323", "#DCDCDC" }
        };

        public MainPage()
        {
            this.InitializeComponent();
            AboutVersionText.Text = $"Version {AppConstants.AppVersionDisplay}";
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
                List<SearchItem> combined = new List<SearchItem>(s_staticSearchItems.Length + sitemapItems.Count);
                combined.AddRange(s_staticSearchItems);
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

            CancelPendingSearch();
        }

        private async void OnAuthStateChanged(object sender, bool isLoggedIn)
        {
            try
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                    () =>
                    {
                        try
                        {
                            UpdateProfileNavItem();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"MainPage: UpdateProfileNavItem failed - {ex.Message}");
                        }
                    });
            }
            catch (Exception ex)
            {
                // Critical: Catch exceptions in async void to prevent app crash
                Debug.WriteLine($"MainPage: Auth state change handler failed – {ex.Message}");
            }
        }

        private void UpdateProfileNavItem()
        {
            // Update profile nav item based on login state
            var user = ProfileService.CurrentUser;
            if (user != null)
            {
                ProfileNavItem.Content = user.DisplayName;
                if (string.IsNullOrWhiteSpace(user.DisplayName))
                {
                    ProfileNavItem.Content = user.Username;
                }
            }
            else
            {
                ProfileNavItem.Content = "Your Profile";
            }

            UpdateProfileNavIcon(user == null ? null : user.AvatarUrl);
        }

        /// <summary>
        /// Swaps the profile nav item's contact glyph for the signed-in user's avatar, the way
        /// Groove put the account picture in its own pane. The circular PNG is baked by
        /// AvatarIconService (a BitmapIcon draws its bitmap as-is and there's no clip to apply);
        /// the glyph stays as the fallback for signed out, for an account with no avatar set, and
        /// for a download that didn't work out.
        ///
        /// Width/Height are pinned to the 16px the pane's icon column expects rather than left to
        /// the bitmap's own 48px, so a template that doesn't scale the icon for us can't push the
        /// nav item's layout around.
        /// </summary>
        private async void UpdateProfileNavIcon(string avatarUrl)
        {
            try
            {
                // Cheap re-entry guard: this method runs from the constructor, from Loaded, and
                // from every AuthStateChanged, and the same avatar shouldn't be rebuilt each time.
                if (string.Equals(avatarUrl, _navAvatarUrl, StringComparison.Ordinal))
                {
                    return;
                }
                _navAvatarUrl = avatarUrl;

                if (string.IsNullOrWhiteSpace(avatarUrl))
                {
                    ProfileNavItem.Icon = new SymbolIcon(Symbol.Contact);
                    return;
                }

                var iconUri = await AvatarIconService.GetCircularAvatarUriAsync(avatarUrl);

                // Signing out - or a profile refresh landing a new avatar - while the download was
                // in flight would otherwise stamp the stale picture onto the nav item.
                if (!string.Equals(avatarUrl, _navAvatarUrl, StringComparison.Ordinal))
                {
                    return;
                }

                if (iconUri == null)
                {
                    // Forget the URL so a later refresh retries: the usual reason to land here is
                    // a transient network failure, and the icon shouldn't stay generic until the
                    // next launch because of one.
                    _navAvatarUrl = null;
                    ProfileNavItem.Icon = new SymbolIcon(Symbol.Contact);
                    return;
                }

                var icon = new BitmapIcon();
                icon.ShowAsMonochrome = false;
                icon.UriSource = iconUri;
                icon.Width = 16;
                icon.Height = 16;
                ProfileNavItem.Icon = icon;
            }
            catch (Exception ex)
            {
                // Critical: Catch exceptions in async void to prevent app crash
                Debug.WriteLine($"MainPage: nav avatar update failed - {ex.Message}");
            }
        }

        private void SetupTitleBar()
        {
            // Extend view into title bar for seamless acrylic
            var coreTitleBar = CoreApplication.GetCurrentView().TitleBar;
            coreTitleBar.ExtendViewIntoTitleBar = true;

            // Set the draggable title bar region
            Window.Current.SetTitleBar(AppTitleBar);

            // The system owns the title bar's height and the width of the corner it reserves for
            // the caption buttons, and it changes both at runtime - tablet mode makes the bar
            // taller, and the reserved corner moves from right to left under RTL. Hardcoding 32px
            // and a right margin was right for exactly one configuration; everywhere else the
            // drag region and the caption buttons disagreed about where the title bar ended.
            // The subscription itself is attached in MainPage_Loaded and released in
            // MainPage_Unloaded, alongside the page's other handlers; this call is just the
            // initial value, so the first frame is drawn with the right height instead of the
            // XAML placeholder.
            ApplyTitleBarLayoutMetrics(coreTitleBar);

            // Make title bar buttons transparent to match acrylic
            UpdateTitleBarColors();
        }

        /// <summary>
        /// Mirrors the system's current title bar metrics onto the custom title bar: the row
        /// height, and the two padding columns that keep content clear of the caption buttons.
        ///
        /// The insets are applied as Grid columns rather than a Margin so that AppTitleBar's
        /// background still paints underneath the caption buttons - this app makes those buttons
        /// transparent in <see cref="UpdateTitleBarColors"/>, so anything that stopped short of
        /// the window edge would show as a notch of bare window behind them.
        /// </summary>
        private void ApplyTitleBarLayoutMetrics(CoreApplicationViewTitleBar coreTitleBar)
        {
            if (coreTitleBar == null) return;

            // Height is 0 before the view is fully initialized; keep the XAML value until the
            // system reports a real one rather than collapsing the bar to nothing.
            if (coreTitleBar.Height > 0)
            {
                AppTitleBar.Height = coreTitleBar.Height;
            }

            TitleBarLeftInset.Width = new GridLength(coreTitleBar.SystemOverlayLeftInset);
            TitleBarRightInset.Width = new GridLength(coreTitleBar.SystemOverlayRightInset);
        }

        private void OnTitleBarLayoutMetricsChanged(CoreApplicationViewTitleBar sender, object args)
        {
            try
            {
                ApplyTitleBarLayoutMetrics(sender);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to apply title bar layout metrics - {ex.Message}");
            }
        }

        private void UpdateTitleBarColors()
        {
            var titleBar = ApplicationView.GetForCurrentView().TitleBar;

            var isDark = IsEffectiveThemeDark();

            var fgColor = isDark ? Colors.White : Colors.Black;
            var inactiveFg = isDark ? Color.FromArgb(128, 255, 255, 255) : Color.FromArgb(128, 0, 0, 0);
            var hoverBg = isDark ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0);
            var pressedBg = isDark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(50, 0, 0, 0);

            // Button colors - transparent with subtle hover
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = hoverBg;
            titleBar.ButtonPressedBackgroundColor = pressedBg;

            // Button foreground colors
            titleBar.ButtonForegroundColor = fgColor;
            titleBar.ButtonHoverForegroundColor = fgColor;
            titleBar.ButtonPressedForegroundColor = fgColor;
            titleBar.ButtonInactiveForegroundColor = inactiveFg;
        }

        private void UpdateLiveTile()
        {
            try
            {
                // Update Live Tile with latest news
                List<NewsItem> newsItems = new List<NewsItem>()
                {
                    new NewsItem("What's new?", "2026.7 has been released for web go to fort1nd.com to see whats new", "welcome"),
                    new NewsItem("Get Started", "Hello! fort.uwp is now ready to use. :3", "features")
                };

                // Update tile with cycling news
                LiveTileService.UpdateTileWithMultipleNews(newsItems);

                // Show badge indicating new content
                LiveTileService.UpdateBadgeGlyph("newMessage");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: UpdateLiveTile failed – {ex.Message}");
            }
        }

        private async void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_navViewInitialized) return;
            _navViewInitialized = true;

            try
            {
                // Home normally, or wherever the user was if Windows terminated the app.
                // Resolved before the first ShowContent call, which would otherwise overwrite
                // the stored tag with "LatestNews" before it could be read back.
                var startupTag = ResolveStartupNavTag();
                SelectNavItemForTag(startupTag);

                // Assigning SelectedItem raises SelectionChanged, not ItemInvoked, so the
                // initial view has to be set up by hand. Without this the header is never
                // given a title, and a null header collapses the header row entirely -
                // leaving the floating pane toggle button to overlap the content.
                ShowContent(startupTag);

                // Ensure pane starts closed
                ClosePaneUnlessExpanded();

                // DisplayModeChanged does not fire for the mode the control starts in.
                UpdateContentPadding(NavView.DisplayMode);

                // The toggle button is a template part, so this has to wait until the template
                // has been applied - which Loaded guarantees.
                AlignPaneToggleButton();

                // Clear the badge now that the user has opened the app
                LiveTileService.ClearBadge();

                // Show welcome dialog on first launch
                var localSettings = ApplicationData.Current.LocalSettings;
                bool hideWelcome = false;
                if (localSettings.Values.ContainsKey(AppConstants.SettingHideWelcomeDialog))
                {
                    hideWelcome = Convert.ToBoolean(localSettings.Values[AppConstants.SettingHideWelcomeDialog]);
                }
                if (!hideWelcome)
                {
                    await ShowWelcomeDialogAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: NavView_Loaded failed – {ex.Message}");
            }
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                ShowContent(AppConstants.NavigationSettings);
            }
            else
            {
                var invokedItem = args.InvokedItemContainer as NavigationViewItem;
                if (invokedItem != null)
                {
                    var tag = invokedItem.Tag?.ToString() ?? AppConstants.NavigationLatestNews;
                    ShowContent(tag);
                }
            }

            // Close the pane after navigation
            ClosePaneUnlessExpanded();
        }

        /// <summary>
        /// Closes the navigation pane, except in Expanded display mode where the pane is
        /// docked beside the content rather than overlaying it, and is meant to stay open.
        /// </summary>
        private void ClosePaneUnlessExpanded()
        {
            if (NavView.DisplayMode != NavigationViewDisplayMode.Expanded)
            {
                NavView.IsPaneOpen = false;
            }
        }

        /// <summary>
        /// Page title for a panel, shown in the NavigationView header.
        /// </summary>
        private static string HeaderFor(string panelName)
        {
            switch (panelName)
            {
                case AppConstants.NavigationGames:
                    return "Games";
                case AppConstants.NavigationBetas:
                    return "Beta Programs";
                case AppConstants.NavigationProfile:
                    return "Your Profile";
                case AppConstants.NavigationSocial:
                    return "Social";
                case AppConstants.NavigationSettings:
                    return "Settings";
                default:
                    // Home and unknown tags all show the Home panel.
                    return "Welcome to Fort.ind";
            }
        }

        private void NavView_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
        {
            UpdateContentPadding(args.DisplayMode);
        }

        /// <summary>
        /// Minimal mode leaves less room for content, so it uses the tighter 12px margin the
        /// design guidance recommends; Compact and Expanded get the standard 24px.
        /// </summary>
        private void UpdateContentPadding(NavigationViewDisplayMode mode)
        {
            double inset = mode == NavigationViewDisplayMode.Minimal ? 12 : 24;
            ContentPanel.Padding = new Thickness(inset);
        }

        /// <summary>
        /// Central content switch. This is the single place that owns the decision between the
        /// app's two content hosts: page-backed views (Profile and Games) navigate the
        /// ContentFrame, while every other view is a lightweight inline panel shown in the
        /// ContentScrollViewer. Adding a new page-backed view is a one-line case here.
        /// </summary>
        private void ShowContent(string tag)
        {
            NavView.Header = HeaderFor(tag);
            RememberLastNavTag(tag);

            switch (tag)
            {
                case AppConstants.NavigationProfile:
                    // The one page-backed view: hosted in the Frame, not as an inline panel.
                    ShowProfilePage();
                    break;
                case AppConstants.NavigationGames:
                    // Second page-backed view: the grouped list needs a bounded height, which the
                    // inline ContentScrollViewer cannot give it.
                    ShowGamesPage();
                    break;
                case AppConstants.NavigationBetas:
                    ShowInlinePanel(BetasPanel);
                    break;
                case AppConstants.NavigationSocial:
                    ShowInlinePanel(SocialPanel);
                    break;
                case AppConstants.NavigationSettings:
                    ShowInlinePanel(SettingsPanel);
                    UpdateStorageInfo();
                    break;
                default:
                    // Home, Latest News, and any unknown tag fall back to the Home panel.
                    ShowInlinePanel(LatestNewsPanel);
                    break;
            }
        }

        /// <summary>
        /// Widens the pane toggle button from 40 to 48 so its box matches the glyph inside it.
        ///
        /// PaneToggleButtonStyle sets MinWidth from {StaticResource PaneToggleButtonWidth}, and a
        /// StaticResource is resolved once, when generic.xaml is parsed - so it is permanently 40
        /// and no app-level override can reach it. The same style's template sizes its first
        /// column from {ThemeResource PaneToggleButtonWidth}, which *does* honour the override and
        /// is 48. The glyph is centred in that column, so it lands on 24 and agrees with every
        /// nav item icon below it; the button around it is still 40 wide and centred on 20.
        ///
        /// Nothing is visible at rest (the button's background is transparent here), but the
        /// hover and press highlights trace the button, not the glyph - so the hamburger lit up a
        /// 40px box with its icon sitting 4px right of centre, while the items below it lit up the
        /// full pane width. Measured at runtime before being changed, not inferred.
        ///
        /// Done in code because the button is a template part - it cannot be reached by x:Name,
        /// and restyling it in XAML would mean copying the whole style to change one number.
        /// </summary>
        private void AlignPaneToggleButton()
        {
            try
            {
                var toggleButton = FindDescendantByName(NavView, "TogglePaneButton") as Control;
                if (toggleButton == null) return;

                toggleButton.MinWidth = 48;
                toggleButton.Width = 48;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to align pane toggle button - {ex.Message}");
            }
        }

        /// <summary>
        /// Depth-first search for a named element inside a control's applied template. Template
        /// parts are not page fields, so they cannot be reached by x:Name from code-behind.
        /// </summary>
        private static FrameworkElement FindDescendantByName(DependencyObject root, string name)
        {
            if (root == null) return null;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);

                var element = child as FrameworkElement;
                if (element != null && element.Name == name) return element;

                var found = FindDescendantByName(child, name);
                if (found != null) return found;
            }

            return null;
        }

        /// <summary>
        /// Records which view the user is on, so a resume from termination can return to it.
        /// Written eagerly on every switch rather than at suspend - see the note in
        /// App.OnSuspending for why. A LocalSettings write is a cheap in-memory update that the
        /// platform flushes for us, and this happens at most once per nav click.
        /// </summary>
        private static void RememberLastNavTag(string tag)
        {
            try
            {
                if (string.IsNullOrEmpty(tag)) return;
                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingLastNavTag] = tag;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to record last nav tag - {ex.Message}");
            }
        }

        /// <summary>
        /// Picks the nav item to open on launch: the one the user was last on if Windows
        /// terminated the app underneath them, otherwise Home.
        ///
        /// Deliberately not honoured after a normal close - App.ResumingFromTermination is only
        /// true for a genuine termination. Reopening an app you closed yourself should start at
        /// the top, and only a termination is the case where the user did not choose to leave.
        /// </summary>
        private static string ResolveStartupNavTag()
        {
            try
            {
                if (!App.ResumingFromTermination) return AppConstants.NavigationLatestNews;

                var saved = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingLastNavTag] as string;
                if (string.IsNullOrEmpty(saved)) return AppConstants.NavigationLatestNews;

                // Only tags this build still recognises - a value left behind by an older version
                // that has since dropped a nav item would otherwise select nothing at all.
                switch (saved)
                {
                    case AppConstants.NavigationLatestNews:
                    case AppConstants.NavigationGames:
                    case AppConstants.NavigationBetas:
                    case AppConstants.NavigationProfile:
                    case AppConstants.NavigationSocial:
                    case AppConstants.NavigationSettings:
                        return saved;
                    default:
                        return AppConstants.NavigationLatestNews;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to resolve startup nav tag - {ex.Message}");
                return AppConstants.NavigationLatestNews;
            }
        }

        /// <summary>
        /// Moves the NavigationView's selection to the item carrying <paramref name="tag"/>,
        /// without invoking it - ShowContent is called separately by the caller. Settings is not
        /// in MenuItems; it is the control's own SettingsItem.
        /// </summary>
        private void SelectNavItemForTag(string tag)
        {
            if (tag == AppConstants.NavigationSettings)
            {
                NavView.SelectedItem = NavView.SettingsItem;
                return;
            }

            foreach (var item in NavView.MenuItems)
            {
                var navItem = item as NavigationViewItem;
                if (navItem != null && (navItem.Tag as string) == tag)
                {
                    NavView.SelectedItem = navItem;
                    return;
                }
            }

            if (NavView.MenuItems.Count > 0)
            {
                NavView.SelectedItem = NavView.MenuItems[0];
            }
        }

        /// <summary>
        /// Shows the inline content host (the ScrollViewer) and makes exactly one panel visible.
        /// Collapses the Frame so the two hosts are never on screen at the same time.
        /// </summary>
        private void ShowInlinePanel(UIElement panel)
        {
            ContentFrame.Visibility = Visibility.Collapsed;
            ContentScrollViewer.Visibility = Visibility.Visible;

            LatestNewsPanel.Visibility = panel == LatestNewsPanel ? Visibility.Visible : Visibility.Collapsed;
            BetasPanel.Visibility = panel == BetasPanel ? Visibility.Visible : Visibility.Collapsed;
            SocialPanel.Visibility = panel == SocialPanel ? Visibility.Visible : Visibility.Collapsed;
            SettingsPanel.Visibility = panel == SettingsPanel ? Visibility.Visible : Visibility.Collapsed;

            PlayPanelEnterAnimation();
        }

        /// <summary>
        /// Plays the inline content host's page-refresh animation (slide up + fade in).
        ///
        /// ContentFrame gets the equivalent for free - a Frame runs NavigationThemeTransition on
        /// navigation, defaulting to page refresh - so without this, clicking Games animated and
        /// clicking Beta Programs did not. Failure here is deliberately swallowed: a missing
        /// animation must never take out navigation, and the panel is already visible by the time
        /// this runs.
        /// </summary>
        private void PlayPanelEnterAnimation()
        {
            try
            {
                var storyboard = Resources["PanelEnterStoryboard"] as Storyboard;
                if (storyboard == null) return;

                // Stop first: re-entering while a previous run is mid-flight would otherwise
                // leave ContentPanel at whatever opacity/offset it had reached.
                storyboard.Stop();
                storyboard.Begin();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Panel enter animation failed - {ex.Message}");
            }
        }


        /// <summary>
        /// Whether going back would land the user somewhere the nav pane still agrees with.
        ///
        /// Frame.CanGoBack on its own is the wrong question in this shell. The content Frame is
        /// shared by two unrelated nav destinations, so after visiting Profile and then Games its
        /// back stack holds ProfilePage - going "back" from Games would swap the content to
        /// Profile while the pane stayed highlighted on Games. The Frame is also hidden entirely
        /// while an inline panel is showing, where back means nothing at all.
        ///
        /// The one genuine back path in the app is LoginPage returning to ProfilePage, which
        /// stays inside the Profile nav item - so that is the case this reports.
        /// </summary>
        private bool CanGoBackInPlace()
        {
            if (ContentFrame == null) return false;
            if (ContentFrame.Visibility != Visibility.Visible) return false;
            if (!ContentFrame.CanGoBack) return false;

            return ContentFrame.Content is LoginPage;
        }

        /// <summary>
        /// The shell's back gestures - Alt+Left, the mouse back button, gamepad B, and the
        /// tablet-mode back button - all arrive here rather than through the nav pane's button.
        ///
        /// AppViewBackButtonVisibility is deliberately left alone: showing the shell's own back
        /// button would put a second one in the title bar this page draws itself, and would move
        /// SystemOverlayLeftInset out from under the layout that was just fixed to respect it.
        /// Handling the event without requesting the visual gets the input without the chrome.
        /// </summary>
        private void OnSystemBackRequested(object sender, BackRequestedEventArgs e)
        {
            if (e.Handled) return;
            e.Handled = TryGoBack();
        }

        /// <summary>
        /// Walks the content Frame back one entry. Returns whether anything actually moved, so
        /// the caller can leave BackRequested unhandled and let the system take its default
        /// action (which, at the root of a desktop app, is nothing).
        /// </summary>
        private bool TryGoBack()
        {
            try
            {
                if (!CanGoBackInPlace()) return false;

                ContentFrame.GoBack();

                // The nav pane's selection and header need no correction: CanGoBackInPlace only
                // returns true for LoginPage -> ProfilePage, which stays inside the Profile item.
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Back navigation failed - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Shows the Frame content host and navigates it to ProfilePage (or refreshes the UI if
        /// it is already there). Falls back to the Home panel if navigation fails.
        /// </summary>
        private void ShowProfilePage()
        {
            ContentScrollViewer.Visibility = Visibility.Collapsed;
            ContentFrame.Visibility = Visibility.Visible;
            try
            {
                if (ContentFrame != null)
                {
                    if (ContentFrame.Content is ProfilePage)
                    {
                        // Already on profile page – refresh the UI instead of re-navigating
                        ((ProfilePage)ContentFrame.Content).RefreshUI();
                    }
                    else
                    {
                        ContentFrame.Navigate(typeof(ProfilePage));
                        TrimContentBackStack();
                    }
                }
            }
            catch (Exception ex)
            {
                // Navigation failed – fall back to home
                Debug.WriteLine($"MainPage: Profile navigation failed – {ex.Message}");
                NavView.Header = HeaderFor(AppConstants.NavigationLatestNews);
                ShowInlinePanel(LatestNewsPanel);
            }
        }

        /// <summary>
        /// Shows the Frame content host and navigates it to GamesPage. The page sets
        /// NavigationCacheMode.Required and owns its own loading/empty/error states, so re-entering
        /// it is a no-op rather than a reload. Falls back to the Home panel if navigation fails.
        /// </summary>
        private void ShowGamesPage()
        {
            ContentScrollViewer.Visibility = Visibility.Collapsed;
            ContentFrame.Visibility = Visibility.Visible;
            try
            {
                if (ContentFrame != null)
                {
                    if (!(ContentFrame.Content is GamesPage))
                    {
                        ContentFrame.Navigate(typeof(GamesPage));
                        TrimContentBackStack();
                    }
                }
            }
            catch (Exception ex)
            {
                // Navigation failed - fall back to home.
                //
                // Logged with the exception type and inner exception, not just Message: when a
                // page fails to parse, the useful detail ("Failed to create a
                // 'Windows.System.VirtualKey' from the text '187'") is in the inner exception,
                // and the outer Message alone reads as a generic navigation failure. This
                // fallback silently turning a broken page into "the Home panel again" is exactly
                // the shape of bug that is hard to spot from the outside, so it should at least
                // be loud in the debugger.
                Debug.WriteLine($"MainPage: Games navigation failed - {ex.GetType().Name}: {ex.Message}"
                                + (ex.InnerException != null ? $" | inner: {ex.InnerException.Message}" : ""));
                NavView.Header = HeaderFor(AppConstants.NavigationLatestNews);
                ShowInlinePanel(LatestNewsPanel);
            }
        }

        /// <summary>
        /// Drops everything but the immediately previous entry from the content Frame's back stack.
        ///
        /// Switching between the Profile and Games nav items pushes a PageStackEntry every time,
        /// and nothing ever popped them - a session spent flipping between the two grew the stack
        /// (and the strong references it holds) without bound. One entry is kept because LoginPage
        /// relies on Frame.GoBack to return to ProfilePage after signing in.
        /// </summary>
        private void TrimContentBackStack()
        {
            try
            {
                var stack = ContentFrame.BackStack;
                while (stack.Count > 1)
                {
                    stack.RemoveAt(0);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to trim content back stack - {ex.Message}");
            }
        }

        private void UpdateStorageInfo()
        {
            try
            {
                StoragePathText.Text = $"Location: {LocalStorageService.DataPath}";

                var user = ProfileService.CurrentUser;
                if (user != null)
                {
                    CacheDescriptionText.Text = "You are logged in using fort.social. We cached your login details so we can quickly log you in :p";
                    UserCountText.Text = $"Signed in as @{user.Username}@{(string.IsNullOrWhiteSpace(user.Host) ? MisskeyAuthService.InstanceHost : user.Host)}";
                    ClearLoginInfoButton.Visibility = Visibility.Visible;
                }
                else
                {
                    CacheDescriptionText.Text = "Not signed in... why dont you go do that?";
                    UserCountText.Text = "";
                    ClearLoginInfoButton.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: UpdateStorageInfo failed - {ex.Message}");
                StoragePathText.Text = "";
                CacheDescriptionText.Text = "";
                UserCountText.Text = "";
                ClearLoginInfoButton.Visibility = Visibility.Collapsed;
            }
        }

        private async void ClearLoginInfoButton_Click(object sender, RoutedEventArgs e)
        {
            // Use semaphore to prevent concurrent dialog opening
            if (!await _dialogSemaphore.WaitAsync(0))
            {
                return; // Another dialog is already open
            }

            try
            {
                ContentDialog dialog = new ContentDialog();
                dialog.Title = "remove your account";
                dialog.Content = "this will remove the login data for your fort.social account, beware! this does not deauthorize your account from fort.social go to your profile > service integration and unlink your account from there if you dont want to use this account in fort.desktop";
                dialog.PrimaryButtonText = "Clear";
                dialog.CloseButtonText = "Cancel";
                dialog.DefaultButton = ContentDialogButton.Close;
                AppConstants.ApplyXamlRoot(dialog, this);

                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    await ProfileService.LogoutAsync();
                    UpdateStorageInfo();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Clear login info dialog failed – {ex.Message}");
            }
            finally
            {
                _dialogSemaphore.Release();
            }
        }

        /// <summary>
        /// Wipes all local app data. Destructive and irreversible, so it's gated behind two
        /// separate confirmations rather than one - the first explains what will happen, the
        /// second is a final "are you sure" with no way to back out afterward. Once the wipe is
        /// done, offers to restart the app immediately (via CoreApplication.RequestRestartAsync)
        /// since a handful of things - the appearance settings just re-read from LocalSettings
        /// during this same session, but anything cached only in memory elsewhere - are only
        /// guaranteed consistent after a fresh process start.
        /// </summary>
        private async void ResetAppButton_Click(object sender, RoutedEventArgs e)
        {
            // Use semaphore to prevent concurrent dialog opening
            if (!await _dialogSemaphore.WaitAsync(0))
            {
                return; // Another dialog is already open
            }

            try
            {
                ContentDialog explainDialog = new ContentDialog();
                explainDialog.Title = "Reset fort.desktop";
                explainDialog.Content = "This signs you out and deletes everything the app has saved locally - your cached profile, the sitemap cache, and all preferences (theme, tint color, panel states). It resets the app to a fresh install. This does not affect your fort.social account.";
                explainDialog.PrimaryButtonText = "Continue";
                explainDialog.CloseButtonText = "Cancel";
                explainDialog.DefaultButton = ContentDialogButton.Close;
                AppConstants.ApplyXamlRoot(explainDialog, this);

                if (await explainDialog.ShowAsync() != ContentDialogResult.Primary) return;

                ContentDialog confirmDialog = new ContentDialog();
                confirmDialog.Title = "Are you absolutely sure?";
                confirmDialog.Content = "This is permanent - everything fort.desktop has saved will be deleted and cannot be recovered.";
                confirmDialog.PrimaryButtonText = "Yes, Reset Everything";
                confirmDialog.CloseButtonText = "Cancel";
                confirmDialog.DefaultButton = ContentDialogButton.Close;
                AppConstants.ApplyXamlRoot(confirmDialog, this);

                if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary) return;

                await ProfileService.ResetAppDataAsync();
                LoadAppearanceSettings();
                UpdateStorageInfo();

                ContentDialog restartDialog = new ContentDialog();
                restartDialog.Title = "Reset complete";
                restartDialog.Content = "fort.desktop has been reset to a fresh install. Restart the app now for everything to take full effect.";
                restartDialog.PrimaryButtonText = "Restart Now";
                restartDialog.CloseButtonText = "Later";
                restartDialog.DefaultButton = ContentDialogButton.Primary;
                AppConstants.ApplyXamlRoot(restartDialog, this);

                if (await restartDialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    await RequestAppRestartAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Reset app dialog failed – {ex.Message}");
            }
            finally
            {
                _dialogSemaphore.Release();
            }
        }

        /// <summary>
        /// Asks the OS to terminate and relaunch this app. On success the process is torn down
        /// before this call returns, so any code after the await here only runs if the restart
        /// could NOT be started (e.g. the app isn't in the foreground) - in which case we just
        /// leave the (already-reset) app running and let the user restart it manually.
        /// </summary>
        private async Task RequestAppRestartAsync()
        {
            try
            {
                var failureReason = await Windows.ApplicationModel.Core.CoreApplication.RequestRestartAsync("");
                Debug.WriteLine($"MainPage: App restart request did not restart the app - {failureReason}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: App restart request threw - {ex.Message}");
            }
        }

        private void RefreshTileButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateLiveTile();
        }

        private void ClearTileButton_Click(object sender, RoutedEventArgs e)
        {
            LiveTileService.ClearTile();
            LiveTileService.ClearBadge();
        }

        /// <summary>
        /// Turns the tile/taskbar badge on or off. The service clears an already-showing badge when
        /// this is switched off, so the change is visible without waiting for the next tile update.
        /// </summary>
        private void TileBadgeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            LiveTileService.BadgeEnabled = TileBadgeToggle.IsOn;
        }

        // ── Appearance settings ──

        private void LoadAppearanceSettings()
        {
            _loadingSettings = true;
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;

                // Restore theme selection
                string theme = AppConstants.ThemeDefault;
                if (localSettings.Values.ContainsKey(AppConstants.SettingAppTheme))
                {
                    theme = localSettings.Values[AppConstants.SettingAppTheme].ToString();
                }
                switch (theme)
                {
                    case AppConstants.ThemeLight: ThemeLightRadio.IsChecked = true; break;
                    case AppConstants.ThemeDark: ThemeDarkRadio.IsChecked = true; break;
                    default: ThemeSystemRadio.IsChecked = true; break;
                }
                ApplyTheme(theme);

                // Restore tint color selection
                string tintTag = AppConstants.ThemeDefault;
                if (localSettings.Values.ContainsKey(AppConstants.SettingAppTintColor))
                {
                    tintTag = localSettings.Values[AppConstants.SettingAppTintColor].ToString();
                }
                // Reset the custom swatch to its palette glyph first - this runs again after an app
                // reset, and a stale color chip would imply a custom tint that no longer exists.
                TintCustomButton.ClearValue(Control.BackgroundProperty);
                TintCustomIcon.Visibility = Visibility.Visible;

                ApplyTintColor(tintTag);
                UpdateTintSelection(tintTag);

                // Keep the last custom pick visible on its swatch even while a preset is active.
                // The glyph still being visible means UpdateTintSelection didn't paint a chip.
                var rememberedCustom = localSettings.Values[AppConstants.SettingAppCustomTintColor] as string;
                if (TintCustomIcon.Visibility == Visibility.Visible && !string.IsNullOrEmpty(rememberedCustom))
                {
                    ShowCustomSwatchColor(rememberedCustom);
                }

                // Restore the tile badge toggle. Inside the _loadingSettings guard so assigning IsOn
                // here doesn't bounce straight back through Toggled and rewrite the setting.
                TileBadgeToggle.IsOn = LiveTileService.BadgeEnabled;

                // Restore settings panel states
                RestoreSettingsPanelStates();
            }
            finally
            {
                _loadingSettings = false;
            }
        }

        private void ApplyTheme(string theme)
        {
            var rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null) return;
            switch (theme)
            {
                case AppConstants.ThemeLight: rootFrame.RequestedTheme = ElementTheme.Light; break;
                case AppConstants.ThemeDark: rootFrame.RequestedTheme = ElementTheme.Dark; break;
                default: rootFrame.RequestedTheme = ElementTheme.Default; break;
            }
            if (!_loadingSettings)
            {
                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTheme] = theme;
            }
            UpdateTitleBarColors();
            // Repaint the window for the new theme. This runs for the untinted case too, not just
            // for a saved tint: the background is a brush this page builds, so nothing else will
            // swap it from the dark recipe to the light one. Skipped while settings are loading
            // only because LoadAppearanceSettings applies the saved tint immediately after.
            if (!_loadingSettings)
            {
                var savedTint = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTintColor]?.ToString();
                if (string.IsNullOrEmpty(savedTint)) savedTint = AppConstants.ThemeDefault;
                ApplyTintColor(savedTint);
                // Refresh the swatch chips and the selected swatch's highlight border so they
                // match the new theme (white outline in dark mode, black outline in light mode).
                UpdateTintSelection(savedTint);
            }
        }

        /// <summary>
        /// Repaints the parts of the window this page owns when the *system* theme changes under an
        /// app left on "System default". The window acrylic and the title bar buttons are painted
        /// from code rather than by a theme resource, so without this they keep the colours of the
        /// theme the app started in - light chrome with a dark window and white caption buttons.
        /// </summary>
        private void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            try
            {
                UpdateTitleBarColors();
                var savedTint = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTintColor]?.ToString();
                if (string.IsNullOrEmpty(savedTint)) savedTint = AppConstants.ThemeDefault;
                ApplyTintColor(savedTint);
                UpdateTintSelection(savedTint);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: OnActualThemeChanged failed – {ex.Message}");
            }
        }

        /// <summary>
        /// The one window-background acrylic brush, reused for the life of the page. A HostBackdrop
        /// AcrylicBrush is backed by a composition effect that samples the desktop, so building a
        /// fresh one per call was by far the most expensive thing the appearance code did - the
        /// colour picker's live preview calls this on every ColorChanged, i.e. continuously while
        /// the user drags. TintColor is a dependency property, so repainting is just a set.
        /// </summary>
        private AcrylicBrush _surfaceBrush;

        // The untinted window acrylic, per theme. These mirror AppSurfaceAcrylicBrush in App.xaml's
        // ThemeDictionaries; RootGrid's background has to be painted from code because a custom
        // tint cannot be expressed as a theme resource, and the untinted case then has to go the
        // same way rather than through a lookup - a ResourceDictionary indexer does not search
        // ThemeDictionaries, so it would fetch the wrong theme's brush (or nothing at all).
        // Values match the OS's own window acrylic: white at 0.8 in light, SystemChromeMediumLow
        // in dark. Keep the two in step.
        private static readonly Color s_surfaceTintDark = Color.FromArgb(255, 0x2B, 0x2B, 0x2B);
        private static readonly Color s_surfaceTintLight = Colors.White;
        private static readonly Color s_surfaceFallbackLight = Color.FromArgb(255, 0xF2, 0xF2, 0xF2);

        private void ApplyTintColor(string colorTag)
        {
            try
            {
                // Determine effective theme to choose the right tint shade
                var isDark = IsEffectiveThemeDark();

                Color tint;
                Color fallback;
                double tintOpacity;

                if (string.IsNullOrEmpty(colorTag) || colorTag == AppConstants.ThemeDefault)
                {
                    tint = isDark ? s_surfaceTintDark : s_surfaceTintLight;
                    fallback = isDark ? s_surfaceTintDark : s_surfaceFallbackLight;
                    tintOpacity = 0.8;
                }
                else
                {
                    tint = HexToColor(colorTag);
                    if (!isDark)
                    {
                        string lightHex = null;
                        tint = s_lightTintMap.TryGetValue(colorTag, out lightHex)
                            ? HexToColor(lightHex)
                            : LightenForLightTheme(tint);
                    }
                    fallback = tint;
                    // Light tints used to sit at 0.6, which let 40% of the desktop through a pale
                    // pastel - enough to drag the whole window grey-brown over a dark wallpaper.
                    // A dark tint absorbs that bleed; a pastel has nothing to absorb it with, so
                    // light holds more of its own colour.
                    tintOpacity = isDark ? 0.8 : 0.85;
                }

                if (_surfaceBrush == null)
                {
                    _surfaceBrush = new AcrylicBrush()
                    {
                        BackgroundSource = AcrylicBackgroundSource.HostBackdrop
                    };
                }
                _surfaceBrush.TintColor = tint;
                _surfaceBrush.TintOpacity = tintOpacity;
                _surfaceBrush.FallbackColor = fallback;

                if (!ReferenceEquals(RootGrid.Background, _surfaceBrush))
                {
                    RootGrid.Background = _surfaceBrush;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: ApplyTintColor failed – {ex.Message}");
            }

            if (!_loadingSettings)
            {
                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTintColor] = colorTag;
            }
        }

        /// <summary>
        /// Whether the app is currently rendering dark - the root Frame's explicit theme if it has
        /// one, otherwise the system's. Three call sites needed this identically.
        /// </summary>
        private static bool IsEffectiveThemeDark()
        {
            var rootFrame = Window.Current.Content as Frame;
            var effTheme = rootFrame != null ? rootFrame.RequestedTheme : ElementTheme.Default;
            return effTheme == ElementTheme.Default
                   ? Application.Current.RequestedTheme == ApplicationTheme.Dark
                   : effTheme == ElementTheme.Dark;
        }

        // Base accessible names for the tint swatches, keyed by control – selection state is
        // appended below since the selected swatch is otherwise only shown via border color,
        // which a screen reader cannot see.
        private static readonly Dictionary<string, string> s_tintSwatchNames = new Dictionary<string, string>()
        {
            { "Default", "Default background tint" },
            { "#1E3A5F", "Navy Blue background tint" },
            { "#2D1B69", "Deep Purple background tint" },
            { "#0F3D2E", "Forest Green background tint" },
            { "#3D1515", "Deep Red background tint" },
            { "#1A1A2E", "Dark Slate background tint" },
            { "#0E3A3A", "Deep Teal background tint" },
            { "#3D2A0F", "Bronze background tint" },
            { "#3D1533", "Deep Rose background tint" },
            { "#2E3D0F", "Olive background tint" },
            { "#232323", "Graphite background tint" }
        };

        /// <summary>
        /// Every preset swatch, in display order. The custom swatch is deliberately excluded -
        /// it has no fixed Tag, so it is matched by elimination rather than by lookup.
        /// Built once: the named fields never change, and this is walked on every tint click and
        /// every theme change.
        /// </summary>
        private Button[] _tintPresetSwatches;

        private Button[] TintPresetSwatches
        {
            get
            {
                if (_tintPresetSwatches == null)
                {
                    _tintPresetSwatches = new Button[] { TintDefaultButton, TintBlueButton, TintPurpleButton, TintGreenButton,
                                                         TintRedButton, TintSlateButton, TintTealButton, TintBronzeButton,
                                                         TintRoseButton, TintOliveButton, TintGraphiteButton };
                }
                return _tintPresetSwatches;
            }
        }

        /// <summary>
        /// Shared brushes for the unselected swatch outline, rather than twelve fresh
        /// SolidColorBrushes every time the selection or theme changes. Brushes are immutable as
        /// far as this code is concerned, so sharing one instance is safe.
        /// Transparent in dark - the chips are vivid enough on their own - but light theme's chips
        /// are pastels on a near-white window and dissolve into it without an edge.
        /// </summary>
        private static readonly SolidColorBrush s_restBrushDark = new SolidColorBrush(Colors.Transparent);
        private static readonly SolidColorBrush s_restBrushLight = new SolidColorBrush(Color.FromArgb(0x22, 0, 0, 0));

        /// <summary>Selection outline for the active swatch - white on dark, black on light.</summary>
        private static readonly SolidColorBrush s_selectedBrushDark = new SolidColorBrush(Colors.White);
        private static readonly SolidColorBrush s_selectedBrushLight = new SolidColorBrush(Colors.Black);

        // Chip colours per theme, keyed by button. The dark ones are the Background values set in
        // XAML and are captured on first use rather than restated here as a second table; the light
        // ones are the pastels the window actually takes in light theme (s_lightTintMap), because a
        // chip painted navy that turns the window pale blue is just a wrong label.
        private Dictionary<Button, Brush> _swatchChipsDark;
        private Dictionary<Button, Brush> _swatchChipsLight;

        private void UpdateSwatchChipColors(bool isDark)
        {
            if (_swatchChipsDark == null)
            {
                _swatchChipsDark = new Dictionary<Button, Brush>();
                _swatchChipsLight = new Dictionary<Button, Brush>();
                foreach (var btn in TintPresetSwatches)
                {
                    var tag = btn.Tag?.ToString() ?? "";
                    string lightHex = null;
                    // Skips the Default swatch, which has no chip - it carries a glyph on the
                    // ordinary button chrome, and that is already theme-aware.
                    if (!s_lightTintMap.TryGetValue(tag, out lightHex)) continue;
                    _swatchChipsDark[btn] = btn.Background;
                    _swatchChipsLight[btn] = new SolidColorBrush(HexToColor(lightHex));
                }
            }

            var chips = isDark ? _swatchChipsDark : _swatchChipsLight;
            foreach (var pair in chips)
            {
                pair.Key.Background = pair.Value;
            }
        }

        private void UpdateTintSelection(string selectedTag)
        {
            selectedTag = string.IsNullOrEmpty(selectedTag) ? AppConstants.ThemeDefault : selectedTag;

            var isDark = IsEffectiveThemeDark();
            var restBrush = isDark ? s_restBrushDark : s_restBrushLight;
            UpdateSwatchChipColors(isDark);

            Button sel = null;
            foreach (var btn in TintPresetSwatches)
            {
                btn.BorderBrush = restBrush;
                var tag = btn.Tag?.ToString() ?? "";
                string baseName = null;
                if (!s_tintSwatchNames.TryGetValue(tag, out baseName))
                {
                    baseName = tag;
                }
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(btn, baseName);
                if (string.Equals(tag, selectedTag, StringComparison.OrdinalIgnoreCase)) sel = btn;
            }

            // Anything that isn't Default and isn't one of the presets is a custom color, so the
            // custom swatch both takes the selection border and previews the color itself.
            TintCustomButton.BorderBrush = restBrush;
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(TintCustomButton, "Custom background tint");
            if (sel == null)
            {
                sel = TintCustomButton;
                ShowCustomSwatchColor(selectedTag);
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(
                    TintCustomButton, $"Custom background tint {selectedTag}");
            }
            else if (TintCustomIcon.Visibility == Visibility.Collapsed)
            {
                // A preset is active, but the custom swatch is still previewing the last custom
                // pick (its palette glyph is hidden). Repaint that from the stored value so it
                // follows the theme too, instead of keeping the shade it was painted in.
                var remembered = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppCustomTintColor] as string;
                if (!string.IsNullOrEmpty(remembered))
                {
                    ShowCustomSwatchColor(remembered);
                }
            }

            if (sel != null)
            {
                sel.BorderBrush = isDark ? s_selectedBrushDark : s_selectedBrushLight;
                var selBaseName = Windows.UI.Xaml.Automation.AutomationProperties.GetName(sel);
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(sel, selBaseName + " (selected)");
            }
        }

        private static Color HexToColor(string hex)
        {
            hex = hex.TrimStart('#');
            return Color.FromArgb(255,
                                  Convert.ToByte(hex.Substring(0, 2), 16),
                                  Convert.ToByte(hex.Substring(2, 2), 16),
                                  Convert.ToByte(hex.Substring(4, 2), 16));
        }

        private static string ColorToHex(Color c)
        {
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        /// <summary>
        /// Approximates the pastel light-theme shade that <see cref="s_lightTintMap"/> stores by
        /// hand for the presets, for colors the user picked themselves. Blending most of the way
        /// to white keeps the hue but stops a saturated pick from turning a light window murky.
        /// </summary>
        private static Color LightenForLightTheme(Color c)
        {
            const double keep = 0.22;
            // VB's CByte rounds half-to-even, where a plain (byte) cast in C# would truncate -
            // that shifts computed light-theme tints by a level (e.g. 205.5 -> 206 vs 205), so
            // the rounding has to be spelled out to keep these colours identical.
            return Color.FromArgb(255,
                                  (byte)Math.Round(255 - (255 - (int)c.R) * keep, MidpointRounding.ToEven),
                                  (byte)Math.Round(255 - (255 - (int)c.G) * keep, MidpointRounding.ToEven),
                                  (byte)Math.Round(255 - (255 - (int)c.B) * keep, MidpointRounding.ToEven));
        }

        /// <summary>
        /// Paints the custom swatch with the given color and hides its palette glyph, so the
        /// button reads as a color chip once the user has actually chosen one. In light theme it
        /// shows the lightened shade the window will actually take, the same way the preset chips
        /// do - <paramref name="hex"/> is always the stored (dark) value.
        /// </summary>
        private void ShowCustomSwatchColor(string hex)
        {
            try
            {
                var c = HexToColor(hex);
                if (!IsEffectiveThemeDark()) c = LightenForLightTheme(c);
                TintCustomButton.Background = new SolidColorBrush(c);
                TintCustomIcon.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: ShowCustomSwatchColor failed – {ex.Message}");
            }
        }

        private void AppearanceHeader_Tapped(object sender, RoutedEventArgs e)
        {
            ToggleSettingsRow(AppearanceContent, AppearanceChevronRotation, AppConstants.SettingSettingsAppearanceExpanded);
        }

        private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            var radio = sender as RadioButton;
            if (radio != null)
            {
                ApplyTheme(radio.Tag.ToString());
            }
        }

        private void TintColorButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null)
            {
                var tag = btn.Tag?.ToString() ?? "Default";
                ApplyTintColor(tag);
                UpdateTintSelection(tag);
            }
        }

        /// <summary>
        /// Opens a color picker so the user can choose a tint that isn't one of the presets. The
        /// pick is applied live while the dialog is open so the choice can be judged against the
        /// real window, and reverted to whatever was active before if the dialog is cancelled.
        /// </summary>
        private async void CustomTintButton_Click(object sender, RoutedEventArgs e)
        {
            if (!await _dialogSemaphore.WaitAsync(0))
            {
                return; // Another dialog is already open
            }

            var localSettings = ApplicationData.Current.LocalSettings;
            string previousTag = localSettings.Values[AppConstants.SettingAppTintColor]?.ToString()
                                 ?? AppConstants.ThemeDefault;

            try
            {
                // Seed with the active tint if it's already a custom one, otherwise the last color
                // the user picked here, otherwise a neutral starting point.
                string seed = previousTag;
                if (seed == AppConstants.ThemeDefault || s_tintSwatchNames.ContainsKey(seed))
                {
                    seed = localSettings.Values[AppConstants.SettingAppCustomTintColor]?.ToString() ?? "#1E3A5F";
                }

                ColorPicker picker = new ColorPicker()
                {
                    IsAlphaEnabled = false,
                    IsHexInputVisible = true,
                    IsColorChannelTextInputVisible = true,
                    Color = HexToColor(seed)
                };

                ContentDialog dialog = new ContentDialog()
                {
                    Title = "Custom background tint",
                    Content = picker,
                    PrimaryButtonText = "Apply",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary
                };
                AppConstants.ApplyXamlRoot(dialog, this);

                // Live preview: repaint the window as the user drags around the picker.
                TypedEventHandler<ColorPicker, ColorChangedEventArgs> previewHandler =
                    (s, args) => ApplyTintColorPreview(ColorToHex(args.NewColor));
                picker.ColorChanged += previewHandler;

                var result = await dialog.ShowAsync();
                picker.ColorChanged -= previewHandler;

                if (result == ContentDialogResult.Primary)
                {
                    var hex = ColorToHex(picker.Color);
                    localSettings.Values[AppConstants.SettingAppCustomTintColor] = hex;
                    ApplyTintColor(hex);
                    UpdateTintSelection(hex);
                }
                else
                {
                    ApplyTintColor(previousTag);
                    UpdateTintSelection(previousTag);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Custom tint dialog failed – {ex.Message}");
                ApplyTintColor(previousTag);
                UpdateTintSelection(previousTag);
            }
            finally
            {
                _dialogSemaphore.Release();
            }
        }

        /// <summary>
        /// Repaints the window with a tint without persisting it - used while the color picker is
        /// open so an abandoned dialog leaves no trace in LocalSettings.
        /// </summary>
        private void ApplyTintColorPreview(string hex)
        {
            var wasLoading = _loadingSettings;
            _loadingSettings = true;
            try
            {
                ApplyTintColor(hex);
            }
            finally
            {
                _loadingSettings = wasLoading;
            }
        }

        // ── Settings row expand/collapse ──

        private void StorageHeader_Tapped(object sender, RoutedEventArgs e)
        {
            ToggleSettingsRow(StorageContent, StorageChevronRotation, AppConstants.SettingSettingsStorageExpanded);
        }

        private void TileHeader_Tapped(object sender, RoutedEventArgs e)
        {
            ToggleSettingsRow(TileContent, TileChevronRotation, AppConstants.SettingSettingsTileExpanded);
        }

        private void WelcomeHeader_Tapped(object sender, RoutedEventArgs e)
        {
            ToggleSettingsRow(WelcomeContent, WelcomeChevronRotation, AppConstants.SettingSettingsWelcomeExpanded);
        }

        private void AboutHeader_Tapped(object sender, RoutedEventArgs e)
        {
            ToggleSettingsRow(AboutContent, AboutChevronRotation, AppConstants.SettingSettingsAboutExpanded);
        }

        /// <summary>
        /// Toggle settings row with state persistence
        /// </summary>
        private void ToggleSettingsRow(StackPanel content, RotateTransform chevronTransform, string settingKey = null)
        {
            var isExpanded = content.Visibility == Visibility.Collapsed;

            if (isExpanded)
            {
                content.Visibility = Visibility.Visible;
                chevronTransform.Angle = 90;
            }
            else
            {
                content.Visibility = Visibility.Collapsed;
                chevronTransform.Angle = 0;
            }

            // Save state if key is provided
            if (!string.IsNullOrEmpty(settingKey))
            {
                try
                {
                    ApplicationData.Current.LocalSettings.Values[settingKey] = isExpanded;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MainPage: Failed to save panel state - {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Restore settings panel expanded/collapsed states
        /// </summary>
        private void RestoreSettingsPanelStates()
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;

                // Restore each panel state
                RestorePanelState(AppConstants.SettingSettingsAppearanceExpanded, AppearanceContent, AppearanceChevronRotation);
                RestorePanelState(AppConstants.SettingSettingsStorageExpanded, StorageContent, StorageChevronRotation);
                RestorePanelState(AppConstants.SettingSettingsTileExpanded, TileContent, TileChevronRotation);
                RestorePanelState(AppConstants.SettingSettingsWelcomeExpanded, WelcomeContent, WelcomeChevronRotation);
                RestorePanelState(AppConstants.SettingSettingsAboutExpanded, AboutContent, AboutChevronRotation);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to restore panel states - {ex.Message}");
            }
        }

        /// <summary>
        /// Restore individual panel state
        /// </summary>
        private void RestorePanelState(string settingKey, StackPanel content, RotateTransform chevronTransform)
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values.ContainsKey(settingKey))
                {
                    var isExpanded = Convert.ToBoolean(localSettings.Values[settingKey]);
                    if (isExpanded)
                    {
                        content.Visibility = Visibility.Visible;
                        chevronTransform.Angle = 90;
                    }
                    else
                    {
                        content.Visibility = Visibility.Collapsed;
                        chevronTransform.Angle = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to restore {settingKey} - {ex.Message}");
            }
        }

        private async Task ShowWelcomeDialogAsync()
        {
            // Use semaphore to prevent concurrent dialog opening
            if (!await _dialogSemaphore.WaitAsync(0))
            {
                return; // Another dialog is already open
            }

            try
            {
                // Content comes from markup (WelcomeDialogContentTemplate in MainPage.xaml) but
                // the dialog itself is built fresh each time - see the note on the template for
                // why a reused ContentDialog instance stops animating on its second showing.
                var contentTemplate = Resources["WelcomeDialogContentTemplate"] as DataTemplate;
                if (contentTemplate == null) return;

                var dialogContent = contentTemplate.LoadContent() as FrameworkElement;
                if (dialogContent == null) return;

                // x:Name inside a DataTemplate is not a page field; it is resolved against the
                // stamped copy's own namescope.
                var dontShowCheckBox = dialogContent.FindName("WelcomeDontShowCheckBox") as CheckBox;

                ContentDialog welcomeDialog = new ContentDialog();
                welcomeDialog.Title = "Hi :)";
                welcomeDialog.Content = dialogContent;
                welcomeDialog.CloseButtonText = "got it";
                welcomeDialog.DefaultButton = ContentDialogButton.Close;
                AppConstants.ApplyXamlRoot(welcomeDialog, this);

                await welcomeDialog.ShowAsync();

                if (dontShowCheckBox != null && dontShowCheckBox.IsChecked.GetValueOrDefault(false))
                {
                    var localSettings = ApplicationData.Current.LocalSettings;
                    localSettings.Values[AppConstants.SettingHideWelcomeDialog] = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Welcome dialog failed – {ex.Message}");
            }
            finally
            {
                _dialogSemaphore.Release();
            }
        }

        private async void ResetWelcomeButton_Click(object sender, RoutedEventArgs e)
        {
            // Check (without holding) that no other dialog is open before flipping the setting -
            // ShowWelcomeDialogAsync takes the semaphore itself, so if we held it here too, its
            // own WaitAsync(0) would immediately fail and the dialog would never show.
            if (!await _dialogSemaphore.WaitAsync(0))
            {
                return; // Another dialog is already open
            }
            _dialogSemaphore.Release();

            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values[AppConstants.SettingHideWelcomeDialog] = false;
                await ShowWelcomeDialogAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Reset welcome failed – {ex.Message}");
            }
        }

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

                CancelPendingSearch();
                NavSearchBox.Text = "";
                NavSearchBox.ItemsSource = null;
                args.Handled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Clear search accelerator failed - {ex.Message}");
            }
        }

        private void CancelPendingSearch()
        {
            // Clear the field before disposing so no later caller can reach the dead source.
            var cts = _searchDebounceCts;
            _searchDebounceCts = null;
            if (cts == null)
            {
                return;
            }

            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already torn down elsewhere – nothing left to cancel.
            }
            cts.Dispose();
        }

        private void NavSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                var query = sender.Text.Trim();

                CancelPendingSearch();

                if (string.IsNullOrEmpty(query))
                {
                    sender.ItemsSource = null;
                    return;
                }

                var cts = new CancellationTokenSource();
                _searchDebounceCts = cts;
                ApplySearchSuggestionsAsync(sender, query, cts.Token);
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

                // Capture volatile references on the UI thread before going off-thread
                var snapshot = _allSearchItems;
                var currentUser = ProfileService.CurrentUser;

                var results = await Task.Run(() => BuildSearchSuggestions(query, snapshot, currentUser), cancellationToken);

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

        private static List<SearchItem> BuildSearchSuggestions(string query, IReadOnlyList<SearchItem> items, UserProfile currentUser)
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
            if (filtered.Count < AppConstants.SearchSuggestionLimit && currentUser != null)
            {
                var name = string.IsNullOrWhiteSpace(currentUser.DisplayName)
                           ? currentUser.Username
                           : currentUser.DisplayName;
                var profileTitle = $"Profile: {name}";
                if (profileTitle.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    AppConstants.CategoryProfile.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    filtered.Add(new SearchItem(profileTitle, AppConstants.CategoryProfile, AppConstants.NavigationProfile));
                }
            }

            return filtered;
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
                // Only http/https is launched - see AppConstants.TryCreateWebUri.
                await AppConstants.LaunchWebUriAsync(item.Url);
            }
            else if (!string.IsNullOrEmpty(item.NavigationTag))
            {
                ShowContent(item.NavigationTag);
            }
        }

    }
}
