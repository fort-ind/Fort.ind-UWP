using System;
using System.Diagnostics;
using System.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public sealed partial class ProfilePage : Page
    {
        private bool _authHandlerAttached = false;

        private string _lastAvatarUrl = null;

        private bool _avatarApplied = false;

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

            var name = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName;
            ProfileInitials.Text = GetInitials(name);

            BioText.Text = string.IsNullOrWhiteSpace(user.Bio) ? LocalizedStrings.Get("ProfileNoBio") : user.Bio;

            if (UpdateAvatarUI(user.AvatarUrl))
            {
                PlayAvatarFadeIn();
            }

            // Belt and braces on top of PlayAvatarFadeIn's own fallback: AvatarGrid starts at
            // Opacity 0 in markup and the storyboard is the only thing that raises it, so any
            // path that skips the animation must still leave the avatar visible.
            AvatarGrid.Opacity = 1;
        }

        private void PlayAvatarFadeIn()
        {
            try
            {
                var storyboard = this.Resources["AvatarFadeIn"] as Storyboard;
                if (storyboard == null)
                {
                    AvatarGrid.Opacity = 1;
                    return;
                }

                storyboard.Stop();
                storyboard.Begin();
            }
            catch (Exception ex)
            {
                // A missing or broken animation must never take out the profile card - this runs
                // from Loaded, where an escaping exception is unhandled.
                Debug.WriteLine($"ProfilePage: avatar fade-in failed - {ex.Message}");
                AvatarGrid.Opacity = 1;
            }
        }

        private void ShowNotLoggedInState()
        {
            NotLoggedInPanel.Visibility = Visibility.Visible;
            LoggedInPanel.Visibility = Visibility.Collapsed;
            _lastAvatarUrl = null;
            _avatarApplied = false;
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

            var parts = name.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return "?";
            }

            // TextHelper rather than Substring(0, 1): a display name beginning with an emoji is a
            // surrogate pair, and taking one char off it renders as a replacement box.
            if (parts.Length >= 2 && parts[1].Length > 0)
            {
                return (TextHelper.FirstTextElements(parts[0], 1) +
                        TextHelper.FirstTextElements(parts[1], 1)).ToUpperInvariant();
            }

            return TextHelper.FirstTextElements(parts[0], 1).ToUpperInvariant();
        }

        private bool UpdateAvatarUI(string avatarUrl)
        {
            // _avatarApplied, not just the URL comparison: _lastAvatarUrl starts null, so an
            // account with no avatar made the very first call look like "already applied" and
            // return false. ShowLoggedInState then skipped the fade-in and AvatarGrid stayed at
            // Opacity 0 - a fully transparent circle with the initials invisible inside it.
            if (_avatarApplied && string.Equals(avatarUrl, _lastAvatarUrl, StringComparison.Ordinal))
            {
                return false;
            }
            _lastAvatarUrl = avatarUrl;
            _avatarApplied = true;

            try
            {
                if (string.IsNullOrWhiteSpace(avatarUrl))
                {
                    ProfileImage.Source = null;
                    ProfileImage.Visibility = Visibility.Collapsed;
                    ProfileInitials.Visibility = Visibility.Visible;
                    return true;
                }

                var avatarUri = WebLauncher.TryCreateWebUri(avatarUrl);
                if (avatarUri == null)
                {
                    ProfileImage.Source = null;
                    ProfileImage.Visibility = Visibility.Collapsed;
                    ProfileInitials.Visibility = Visibility.Visible;
                    return true;
                }

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
