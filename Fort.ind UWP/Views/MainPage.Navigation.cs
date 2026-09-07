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
                Debug.WriteLine($"MainPage: Auth state change handler failed – {ex.Message}");
            }
        }

        private void UpdateProfileNavItem()
        {
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
                ProfileNavItem.Content = LocalizedStrings.Get("ProfileNavItem/Content");
            }

            UpdateProfileNavIcon(user == null ? null : user.AvatarUrl);
        }

        private async void UpdateProfileNavIcon(string avatarUrl)
        {
            try
            {
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

                if (!string.Equals(avatarUrl, _navAvatarUrl, StringComparison.Ordinal))
                {
                    return;
                }

                if (iconUri == null)
                {
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
                Debug.WriteLine($"MainPage: nav avatar update failed - {ex.Message}");
            }
        }
        private async void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_navViewInitialized) return;
            _navViewInitialized = true;

            try
            {
                var startupTag = ResolveStartupNavTag();
                SelectNavItemForTag(startupTag);

                ShowContent(startupTag);

                ClosePaneUnlessExpanded();

                UpdateContentPadding(NavView.DisplayMode);
                UpdateAppTitleVisibility(NavView.IsPaneOpen);

                AlignPaneToggleButton();
                MarkNavigationPaneLandmark();

                LiveTileService.ClearBadge();

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
                ShowContent(AppConstants.NavigationSettings, true);
            }
            else
            {
                var invokedItem = args.InvokedItemContainer as NavigationViewItem;
                if (invokedItem != null)
                {
                    var tag = invokedItem.Tag?.ToString() ?? AppConstants.NavigationLatestNews;
                    ShowContent(tag, true);
                }
            }

            ClosePaneUnlessExpanded();
        }

        private void ClosePaneUnlessExpanded()
        {
            if (NavView.DisplayMode != NavigationViewDisplayMode.Expanded)
            {
                NavView.IsPaneOpen = false;
            }
        }

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
                    return LocalizedStrings.Get("HeaderHome");
            }
        }

        private void NavView_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
        {
            UpdateContentPadding(args.DisplayMode);
            UpdateAppTitleVisibility(sender.IsPaneOpen);
        }

        private void NavView_PaneOpening(NavigationView sender, object args)
        {
            UpdateAppTitleVisibility(true);
        }

        private void NavView_PaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args)
        {
            UpdateAppTitleVisibility(false);
        }

        private void UpdateAppTitleVisibility(bool isPaneOpen)
        {
            AppTitleText.Visibility = isPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateContentPadding(NavigationViewDisplayMode mode)
        {
            double inset = mode == NavigationViewDisplayMode.Minimal ? 12 : 24;
            ContentPanel.Padding = new Thickness(inset);
        }

        /// <param name="tag">The nav tag to show; see the Navigation* constants on AppConstants.</param>
        /// <param name="moveFocus">
        /// True when the user drove this, false for the startup and session-restore paths. A nav
        /// gesture that only flips Visibility leaves keyboard focus stranded in the pane and tells
        /// assistive technology nothing, so a user-initiated switch hands focus to the content
        /// region; doing the same at startup would be an unrequested context change.
        /// </param>
        private void ShowContent(string tag, bool moveFocus = false)
        {
            var header = HeaderFor(tag);
            NavView.Header = header;
            RememberLastNavTag(tag);

            // The content host is the Main landmark and the focus target, so it carries the
            // section name - that is what gets read out when focus lands on it.
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(ContentHost, header);
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(ContentScrollViewer, header);

            switch (tag)
            {
                case AppConstants.NavigationProfile:
                    ShowProfilePage();
                    break;
                case AppConstants.NavigationGames:
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
                    ShowInlinePanel(LatestNewsPanel);
                    break;
            }

            if (moveFocus)
            {
                FocusContentRegion();
            }
        }

        /// <summary>
        /// Moves keyboard focus out of the nav pane and onto whichever content host is showing.
        /// Programmatic focus, so no focus rectangle is drawn - the point is where the next Tab
        /// and the next screen-reader read start from, not a visible highlight.
        /// </summary>
        private void FocusContentRegion()
        {
            try
            {
                if (ContentScrollViewer.Visibility == Visibility.Visible)
                {
                    ContentScrollViewer.Focus(FocusState.Programmatic);
                    return;
                }

                // Page-backed views: the page itself, when it will take focus. GamesPage and
                // ProfilePage are ordinary Pages and may decline, which is fine - they hold real
                // controls a Tab away, and the header has already been retitled.
                var page = ContentFrame.Content as Control;
                if (page != null)
                {
                    page.Focus(FocusState.Programmatic);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to focus content region - {ex.Message}");
            }
        }

        /// <summary>
        /// Tags the pane as the Navigation landmark. It has to be done from code against the
        /// template part: the pane lives inside NavigationView's template, and putting the
        /// landmark on NavView itself would wrap the content in it too.
        /// </summary>
        private void MarkNavigationPaneLandmark()
        {
            try
            {
                // PaneContentGrid is the platform NavigationView's pane container - see
                // generic.xaml, the ControlTemplate for Windows.UI.Xaml.Controls.NavigationView.
                var pane = VisualTreeSearch.FindDescendantByName(NavView, "PaneContentGrid");
                if (pane == null)
                {
                    Debug.WriteLine("MainPage: PaneContentGrid not found; navigation landmark not set.");
                    return;
                }

                Windows.UI.Xaml.Automation.AutomationProperties.SetLandmarkType(
                    pane, Windows.UI.Xaml.Automation.Peers.AutomationLandmarkType.Navigation);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to mark navigation landmark - {ex.Message}");
            }
        }

        internal void NavigateToTag(string tag)
        {
            try
            {
                if (string.IsNullOrEmpty(tag)) return;

                SelectNavItemForTag(tag);
                ShowContent(tag, true);
                ClosePaneUnlessExpanded();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: NavigateToTag failed - {ex.Message}");
            }
        }

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

        private static string ResolveStartupNavTag()
        {
            try
            {
                var pending = App.TakePendingLaunchNavTag();
                if (!string.IsNullOrEmpty(pending)) return pending;

                if (!App.ResumingFromTermination) return AppConstants.NavigationLatestNews;

                var saved = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingLastNavTag] as string;
                if (string.IsNullOrEmpty(saved)) return AppConstants.NavigationLatestNews;

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

        private void PlayPanelEnterAnimation()
        {
            try
            {
                var storyboard = Resources["PanelEnterStoryboard"] as Storyboard;
                if (storyboard == null) return;

                storyboard.Stop();
                storyboard.Begin();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Panel enter animation failed - {ex.Message}");
            }
        }

        private bool CanGoBackInPlace()
        {
            if (ContentFrame == null) return false;
            if (ContentFrame.Visibility != Visibility.Visible) return false;
            if (!ContentFrame.CanGoBack) return false;

            return ContentFrame.Content is LoginPage;
        }

        private void OnSystemBackRequested(object sender, BackRequestedEventArgs e)
        {
            if (e.Handled) return;
            e.Handled = TryGoBack();
        }

        private bool TryGoBack()
        {
            try
            {
                if (!CanGoBackInPlace()) return false;

                ContentFrame.GoBack();

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Back navigation failed - {ex.Message}");
                return false;
            }
        }

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
                Debug.WriteLine($"MainPage: Profile navigation failed – {ex.Message}");
                NavView.Header = HeaderFor(AppConstants.NavigationLatestNews);
                ShowInlinePanel(LatestNewsPanel);
            }
        }

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
                Debug.WriteLine($"MainPage: Games navigation failed - {ex.GetType().Name}: {ex.Message}"
                                + (ex.InnerException != null ? $" | inner: {ex.InnerException.Message}" : ""));
                NavView.Header = HeaderFor(AppConstants.NavigationLatestNews);
                ShowInlinePanel(LatestNewsPanel);
            }
        }

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
