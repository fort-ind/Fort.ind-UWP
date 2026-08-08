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

        // Guard to prevent multiple ContentDialogs from opening simultaneously
        private SemaphoreSlim _dialogSemaphore = new SemaphoreSlim(1, 1);

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
            UsernameText.Text = $"@{user.Username}@{host}";

            if (user.CreatedDate > DateTime.MinValue)
            {
                MemberSinceText.Text = $"Member since {user.CreatedDate:MMMM yyyy}";
                MemberSinceText.Visibility = Visibility.Visible;
            }
            else
            {
                MemberSinceText.Visibility = Visibility.Collapsed;
            }

            if (user.LastLoginDate > DateTime.MinValue)
            {
                LastLoginText.Text = $"Last signed in: {user.LastLoginDate:MMM d, yyyy h:mm tt}";
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
            BioText.Text = string.IsNullOrWhiteSpace(user.Bio) ? "No bio set" : user.Bio;

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

                await AppConstants.LaunchWebUriAsync($"https://{host}/@{Uri.EscapeDataString(user.Username)}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: Failed to open fort.social profile - {ex.Message}");
            }
        }

        private async void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            // Use semaphore to prevent concurrent dialog opening
            if (!await _dialogSemaphore.WaitAsync(0))
            {
                return; // Another dialog is already open
            }

            try
            {
                ContentDialog dialog = new ContentDialog();
                dialog.Title = "Sign Out";
                dialog.Content = "Are you sure you want to sign out?";
                dialog.PrimaryButtonText = "Sign Out";
                dialog.CloseButtonText = "Cancel";
                dialog.DefaultButton = ContentDialogButton.Close;
                AppConstants.ApplyXamlRoot(dialog, this);

                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    await ProfileService.LogoutAsync();
                    RefreshUI();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: Logout dialog failed – {ex.Message}");
            }
            finally
            {
                _dialogSemaphore.Release();
            }
        }

        private string GetInitials(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "?";
            }

            var parts = name.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[1].Length > 0)
            {
                return (parts[0].Substring(0, 1) + parts[1].Substring(0, 1)).ToUpper();
            }

            return parts[0].Substring(0, 1).ToUpper();
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
                var avatarUri = AppConstants.TryCreateWebUri(avatarUrl);
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
