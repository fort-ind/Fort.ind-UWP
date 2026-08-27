using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    /// <summary>
    /// Shell navigation: the NavigationView's own events, the switch between the inline panels
    /// and the page-backed views, back navigation, and the profile nav item that tracks sign-in
    /// state.
    /// </summary>
    public sealed partial class MainPage : Page
    {

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
                // Read back the item's own authored label rather than a second copy of it. A
                // property identifier is addressed from code with slashes where the resw name
                // has dots - see "Refer to a string resource identifier from code".
                ProfileNavItem.Content = LocalizedStrings.Get("ProfileNavItem/Content");
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

                // DisplayModeChanged does not fire for the mode the control starts in, and the
                // pane events only fire on a change - a pane that was already closed above
                // raises nothing - so both initial states are set by hand here.
                UpdateContentPadding(NavView.DisplayMode);
                UpdateAppTitleVisibility(NavView.IsPaneOpen);

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
                    return LocalizedStrings.Get("HeaderGames");
                case AppConstants.NavigationBetas:
                    return LocalizedStrings.Get("HeaderBetas");
                case AppConstants.NavigationProfile:
                    return LocalizedStrings.Get("HeaderProfile");
                case AppConstants.NavigationSocial:
                    return LocalizedStrings.Get("HeaderSocial");
                case AppConstants.NavigationSettings:
                    return LocalizedStrings.Get("HeaderSettings");
                default:
                    // Home and unknown tags all show the Home panel.
                    return LocalizedStrings.Get("HeaderHome");
            }
        }

        private void NavView_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
        {
            UpdateContentPadding(args.DisplayMode);
            UpdateAppTitleVisibility(sender.IsPaneOpen);
        }

        // The pane events, not the IsPaneOpen property: both fire as the pane starts to animate,
        // and at that point the property has only caught up for one of them - it is already true
        // in PaneOpening but still true in PaneClosing. Hence the explicit argument rather than
        // reading it back.
        private void NavView_PaneOpening(NavigationView sender, object args)
        {
            UpdateAppTitleVisibility(true);
        }

        private void NavView_PaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args)
        {
            UpdateAppTitleVisibility(false);
        }

        /// <summary>
        /// Shows the app name in the title bar only while the pane is open.
        ///
        /// The title bar sits over the pane rather than in a row of its own, so the name reads as
        /// the pane's heading - but only while there is a pane under it to read against. Closed,
        /// the pane is 48px in Compact and nothing at all in Minimal, and "Fort.ind" is wider than
        /// either, so it spilled out over the content area.
        /// </summary>
        private void UpdateAppTitleVisibility(bool isPaneOpen)
        {
            AppTitleText.Visibility = isPaneOpen ? Visibility.Visible : Visibility.Collapsed;
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
        /// Drives the shell to a nav tag from outside the page - currently a jump list task that
        /// arrived while the app was already running. Does what a pane click does: moves the
        /// selection so the pane agrees with the content, switches the content, then closes the
        /// pane the same way NavView_ItemInvoked would.
        ///
        /// Deliberately not routed through NavView_Loaded's startup path, which is one-shot
        /// behind _navViewInitialized and so would do nothing on a second activation. Unknown
        /// tags fall through ShowContent's default to Home; App resolves the argument through
        /// JumpListService.ResolveNavTag before calling this, so that is belt and braces.
        /// </summary>
        internal void NavigateToTag(string tag)
        {
            try
            {
                if (string.IsNullOrEmpty(tag)) return;

                SelectNavItemForTag(tag);
                ShowContent(tag);
                ClosePaneUnlessExpanded();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: NavigateToTag failed - {ex.Message}");
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
                var toggleButton = VisualTreeSearch.FindDescendantByName(NavView, "TogglePaneButton") as Control;
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
                // A jump list task outranks both of the cases below: the user named a
                // destination in the act of launching, which is more specific than "put me back
                // where I was" and than the Home default. Taken (not just read) so it is acted on
                // once - see App.TakePendingLaunchNavTag.
                var pending = App.TakePendingLaunchNavTag();
                if (!string.IsNullOrEmpty(pending)) return pending;

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
                if (ContentFrame != null && !(ContentFrame.Content is GamesPage))
                {
                    ContentFrame.Navigate(typeof(GamesPage));
                    TrimContentBackStack();
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

    }
}
