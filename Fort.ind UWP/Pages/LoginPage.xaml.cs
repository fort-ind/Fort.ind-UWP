using System;
using System.Diagnostics;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    /// <summary>
    /// Sign-in page: hands off to fort.social via MiAuth.
    /// </summary>
    public sealed partial class LoginPage : Page
    {

        private const string SkipHintSeenKey = "HasSeenSkipSignInTip";

        public LoginPage()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Show the "continue without account" TeachingTip once, on first visit
        /// </summary>
        private void LoginPage_Loaded(object sender, RoutedEventArgs e)
        {
            var settings = ApplicationData.Current.LocalSettings;
            if (!settings.Values.ContainsKey(SkipHintSeenKey))
            {
                settings.Values[SkipHintSeenKey] = true;
                SkipHintTip.IsOpen = true;
            }
        }

        /// <summary>
        /// Handle sign-in button click
        /// </summary>
        private async void SignInButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Visibility = Visibility.Collapsed;
            ShowLoading(true);

            try
            {
                var result = await ProfileService.LoginWithMisskeyAsync();

                if (result.Success)
                {
                    GoBackToProfile();
                }
                else
                {
                    ShowError(result.Message);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SignInButton_Click error: {ex}");
                ShowError("An error occurred. Please try again.");
            }
            finally
            {
                ShowLoading(false);
            }
        }

        /// <summary>
        /// Cancel a sign-in that's waiting on the browser
        /// </summary>
        private void CancelSignInButton_Click(object sender, RoutedEventArgs e)
        {
            MisskeyAuthService.CancelPendingSignIn();
        }

        /// <summary>
        /// Skip sign-in and continue without an account
        /// </summary>
        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            GoBackToProfile();
        }

        /// <summary>
        /// Returns to the page that opened this one.
        ///
        /// This page is always hosted in MainPage's ContentFrame (ProfilePage navigates here), so
        /// the fallback is ProfilePage, not MainPage - navigating to MainPage would load a second
        /// shell, nav pane and all, *inside* the first one's content area.
        /// </summary>
        private void GoBackToProfile()
        {
            if (Frame == null) return;

            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
            else
            {
                Frame.Navigate(typeof(ProfilePage));
            }
        }

        /// <summary>
        /// Show error message
        /// </summary>
        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Show/hide loading overlay
        /// </summary>
        private void ShowLoading(bool show)
        {
            LoadingOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            SignInButton.IsEnabled = !show;
            SkipButton.IsEnabled = !show;
        }

    }
}
