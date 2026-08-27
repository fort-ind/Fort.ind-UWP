using System;
using System.Diagnostics;
using System.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    /// <summary>
    /// Profile viewing page. Read-only: the fort.social account is the source of truth,
    /// so editing happens on the instance, not here.
    /// </summary>
    public sealed partial class ProfilePage : Page
    {

        // Tracks whether the AuthStateChanged handler is attached, so repeated Loaded/Unloaded
        // cycles (which UWP can fire more than once) don't attach it more than once, and so
        // the handler is reliably reattached after an Unloaded/Loaded pair instead of leaving
        // this page permanently deaf to sign-in/out.
        private bool _authHandlerAttached = false;

        // The avatar URL currently shown, so a second RefreshUI for the same profile (e.g. the
        // background refresh in ProfileService.TryRestoreSessionAsync firing shortly after the
        // cached profile is first shown) doesn't re-download/re-decode an unchanged avatar image
        // or replay its fade-in animation.
        private string _lastAvatarUrl = null;

        public ProfilePage()
        {
            this.InitializeComponent();
            Loaded += ProfilePage_Loaded;
            Unloaded += ProfilePage_Unloaded;
        }

        private void ProfilePage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_authHandlerAttached)
            {
                ProfileService.AuthStateChanged -= OnAuthStateChanged;
                _authHandlerAttached = false;
            }
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
                            RefreshUI();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"ProfilePage: RefreshUI failed - {ex.Message}");
                        }
                    });
            }
            catch (Exception ex)
            {
                // Critical: Catch exceptions in async void to prevent app crash
                Debug.WriteLine($"ProfilePage: Auth state change handler failed - {ex.Message}");
            }
        }

        private void ProfilePage_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_authHandlerAttached)
            {
                ProfileService.AuthStateChanged += OnAuthStateChanged;
                _authHandlerAttached = true;
            }
            RefreshUI();
        }

        /// <summary>
        /// Refresh the UI based on login state
        /// </summary>
        public void RefreshUI()
        {
            if (ProfileService.CurrentUser != null)
            {
                ShowLoggedInState();
            }
            else
            {
                ShowNotLoggedInState();
            }
        }

        private void ShowLoggedInState()
        {
            NotLoggedInPanel.Visibility = Visibility.Collapsed;
            LoggedInPanel.Visibility = Visibility.Visible;

            var user = ProfileService.CurrentUser;
            var host = string.IsNullOrWhiteSpace(user.Host) ? MisskeyAuthService.InstanceHost : user.Host;

            // Update profile header
            DisplayNameText.Text = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName;
            UsernameText.Text = LocalizedStrings.Format("ProfileHandleFormat", user.Username, host);

            if (user.CreatedDate > DateTime.MinValue)
            {
                MemberSinceText.Text = LocalizedStrings.Format("ProfileMemberSinceFormat", FormatMemberSince(user.CreatedDate));
                MemberSinceText.Visibility = Visibility.Visible;
            }
            else
            {
                MemberSinceText.Visibility = Visibility.Collapsed;
            }

            if (user.LastLoginDate > DateTime.MinValue)
            {
                LastLoginText.Text = LocalizedStrings.Format("ProfileLastSignedInFormat", FormatLastLogin(user.LastLoginDate));
                LastLoginText.Visibility = Visibility.Visible;
            }
            else
            {
                LastLoginText.Visibility = Visibility.Collapsed;
            }

            // Set initials (up to two letters: first letter of each word)
            var name = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName;
            ProfileInitials.Text = GetInitials(name);

            // Update bio
            BioText.Text = string.IsNullOrWhiteSpace(user.Bio) ? LocalizedStrings.Get("ProfileNoBio") : user.Bio;

            // Fade in the avatar - only when it's actually new/changed, not on every RefreshUI.
            if (UpdateAvatarUI(user.AvatarUrl))
            {
                var sb = this.Resources["AvatarFadeIn"] as Storyboard;
                sb?.Begin();
            }
        }

        private void ShowNotLoggedInState()
        {
            NotLoggedInPanel.Visibility = Visibility.Visible;
            LoggedInPanel.Visibility = Visibility.Collapsed;
            // So the avatar reloads and fades in again on the next sign-in, even if it's the same
            // account/URL as before this sign-out.
            _lastAvatarUrl = null;
        }

        private void SignInButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(LoginPage));
        }

        private async void ManageOnFortSocialButton_Click(object sender, RoutedEventArgs e)
        {
            var user = ProfileService.CurrentUser;
            if (user == null) return;

            try
            {
                // Host and username come from the instance's JSON and are cached to disk, so they
                // are treated as untrusted when they are spliced into a URL. An unchecked host
                // ("evil.com/x") or username ("a/../..") would silently point this link somewhere
                // other than the user's profile.
                var host = string.IsNullOrWhiteSpace(user.Host) ? MisskeyAuthService.InstanceHost : user.Host;
                if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
                {
                    host = MisskeyAuthService.InstanceHost;
                }

                await WebLauncher.LaunchAsync($"https://{host}/@{Uri.EscapeDataString(user.Username)}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: Failed to open fort.social profile - {ex.Message}");
            }
        }

        private async void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            var confirmed = await DialogService.ShowConfirmAsync(
                this,
                LocalizedStrings.Get("SignOutDialogTitle"),
                LocalizedStrings.Get("SignOutDialogBody"),
                LocalizedStrings.Get("SignOutDialogConfirm"),
                LocalizedStrings.Get("DialogCancel"),
                ContentDialogButton.Close);

            if (!confirmed) return;

            try
            {
                await ProfileService.LogoutAsync();
                RefreshUI();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: Logout failed – {ex.Message}");
            }
        }

        /// <summary>
        /// Formats a sign-in timestamp the way the user's own Windows region settings would.
        ///
        /// The previous "MMM d, yyyy h:mm tt" custom pattern baked in US conventions - month
        /// before day, and a 12-hour clock with AM/PM - for every user regardless of locale.
        /// DateTimeFormatter is the WinRT globalization API: given the abstract shapes
        /// ("shortdate", "shorttime") it resolves ordering, separators and 12- vs 24-hour from
        /// Settings > Time &amp; Language, so a German user gets 03.06.2026 14:05 without this
        /// method knowing anything about German.
        ///
        /// Falls back to the invariant round-trip form if the formatter is unavailable, which is
        /// wrong-looking but never misleading - unlike a US-shaped date shown to someone who
        /// reads day-first, where 03/06 silently means the wrong day.
        /// </summary>
        /// <summary>
        /// Formats a join date as month and year. Same reasoning as <see cref="FormatLastLogin"/>:
        /// the previous "MMMM yyyy" custom pattern pinned month-before-year, which is wrong in
        /// the many locales that write the year first. "month year" is an abstract format
        /// template, so DateTimeFormatter resolves both the names and the order from the user's
        /// region settings.
        /// </summary>
        private static string FormatMemberSince(DateTime value)
        {
            try
            {
                var formatter = new Windows.Globalization.DateTimeFormatting.DateTimeFormatter("month year");
                return formatter.Format(new DateTimeOffset(value));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: Member-since formatting failed - {ex.Message}");
                return value.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        private static string FormatLastLogin(DateTime value)
        {
            try
            {
                var dateFormatter = new Windows.Globalization.DateTimeFormatting.DateTimeFormatter("shortdate");
                var timeFormatter = new Windows.Globalization.DateTimeFormatting.DateTimeFormatter("shorttime");

                // DateTimeFormatter takes a DateTimeOffset; these timestamps are stored as local
                // DateTimes, so let the conversion pick up the machine's current offset.
                DateTimeOffset offset = new DateTimeOffset(value);

                return $"{dateFormatter.Format(offset)} {timeFormatter.Format(offset)}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: Date formatting failed - {ex.Message}");
                return value.ToString("u", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        private string GetInitials(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "?";
            }

            // ToUpperInvariant, not ToUpper: ToUpper follows the *current* culture, so a user on a
            // Turkish system would see a dotted "İ" for a name starting with "i". These are
            // display initials for a fort.social handle, not culture-specific text. GamesPage's
            // GroupKeyFor already uses the invariant form for the same reason.
            var parts = name.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[1].Length > 0)
            {
                return (parts[0].Substring(0, 1) + parts[1].Substring(0, 1)).ToUpperInvariant();
            }

            return parts[0].Substring(0, 1).ToUpperInvariant();
        }

        /// <summary>
        /// Applies the avatar image (or falls back to initials). Returns False without doing any
        /// work if avatarUrl is the same one already applied - RefreshUI can run more than once
        /// for the same profile (e.g. the background refresh in
        /// ProfileService.TryRestoreSessionAsync), and without this check each call would
        /// re-download/re-decode an unchanged image and the caller would replay its fade-in.
        /// </summary>
        private bool UpdateAvatarUI(string avatarUrl)
        {
            if (string.Equals(avatarUrl, _lastAvatarUrl, StringComparison.Ordinal))
            {
                return false;
            }
            _lastAvatarUrl = avatarUrl;

            try
            {
                if (string.IsNullOrWhiteSpace(avatarUrl))
                {
                    ProfileImage.Source = null;
                    ProfileImage.Visibility = Visibility.Collapsed;
                    ProfileInitials.Visibility = Visibility.Visible;
                    return true;
                }

                // Only http/https avatars are fetched - the URL comes from the instance's JSON via
                // the on-disk profile cache, and BitmapImage will happily resolve other schemes.
                var avatarUri = WebLauncher.TryCreateWebUri(avatarUrl);
                if (avatarUri == null)
                {
                    ProfileImage.Source = null;
                    ProfileImage.Visibility = Visibility.Collapsed;
                    ProfileInitials.Visibility = Visibility.Visible;
                    return true;
                }

                // Decode at the displayed 80x80 size, not the source resolution - avatars served
                // at 512px+ would otherwise sit in memory fully decoded (several MB each) despite
                // being drawn into an 80px circle. DecodePixelType.Logical means these are view
                // pixels, which XAML already scales by the display's rasterization scale, so the
                // high-DPI case is handled without asking for 2x here on top of it.
                // Must be set before UriSource or the decode already happened at full size.
                BitmapImage bitmap = new BitmapImage();
                bitmap.DecodePixelType = DecodePixelType.Logical;
                bitmap.DecodePixelWidth = 80;
                bitmap.DecodePixelHeight = 80;
                bitmap.UriSource = avatarUri;
                ProfileImage.Source = bitmap;
                ProfileImage.Visibility = Visibility.Visible;
                ProfileInitials.Visibility = Visibility.Collapsed;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: Avatar load failed - {ex.Message}");
                ProfileImage.Source = null;
                ProfileImage.Visibility = Visibility.Collapsed;
                ProfileInitials.Visibility = Visibility.Visible;
                return true;
            }
        }

    }
}
