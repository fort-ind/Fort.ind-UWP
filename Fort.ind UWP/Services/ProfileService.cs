using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Fort.ind_UWP
{
    public class ProfileService
    {
        public static UserProfile CurrentUser { get; set; }

        public static event EventHandler<bool> AuthStateChanged;

        public static async Task<LoginResult> LoginWithMisskeyAsync()
        {
            var result = await MisskeyAuthService.SignInAsync();
            if (!result.Success)
            {
                return new LoginResult(false, result.ErrorMessage);
            }

            await ApplySignInResultAsync(result);
            return new LoginResult(true, "Signed in!", result.Profile);
        }

        public static async Task<bool> ApplySignInResultAsync(MisskeyAuthResult result)
        {
            if (result == null || !result.Success) return false;

            CurrentUser = result.Profile;
            await LocalStorageService.SaveProfileAsync(result.Profile);
            AuthStateChanged?.Invoke(null, true);

            var displayName = string.IsNullOrWhiteSpace(result.Profile.DisplayName) ? result.Profile.Username : result.Profile.DisplayName;
            LiveTileService.SendToast("Welcome back!", $"Signed in as {displayName}");

            return true;
        }

        public static async Task LogoutAsync()
        {
            var name = CurrentUser != null ? (string.IsNullOrWhiteSpace(CurrentUser.DisplayName) ? CurrentUser.Username : CurrentUser.DisplayName) : "";
            CurrentUser = null;
            MisskeyAuthService.ClearToken();
            await LocalStorageService.ClearProfileAsync();
            AuthStateChanged?.Invoke(null, false);
            if (!string.IsNullOrEmpty(name)) LiveTileService.SendToast("Signed out", $"Goodbye, {name}!");
        }

        public static async Task ResetAppDataAsync()
        {
            MisskeyAuthService.ClearToken();
            CurrentUser = null;
            LiveTileService.ClearTile();
            LiveTileService.ClearBadge();
            await LocalStorageService.ResetAllAppDataAsync();

            // The wipe deletes the cached avatar PNG, but AvatarIconService remembers its URI in a
            // static that outlives the reset - so signing back in with the same avatar handed the
            // nav item an ms-appdata URI pointing at a file that no longer exists.
            AvatarIconService.InvalidateCache();

            AuthStateChanged?.Invoke(null, false);
        }

        public static async Task<bool> TryRestoreSessionAsync()
        {
            try
            {
                var token = await MisskeyAuthService.TryGetTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    return false;
                }

                var cached = await LocalStorageService.LoadProfileAsync();
                if (cached == null)
                {
                    var fetched = await MisskeyAuthService.FetchCurrentUserAsync(token);
                    if (fetched.Profile == null)
                    {
                        // Only discard the token when the instance actually refused it. This used
                        // to sign the user out on any null, so launching with no network and no
                        // cached profile threw away a perfectly good token and made them sign in
                        // again; now the session simply stays unrestored until the next launch.
                        if (fetched.TokenRejected)
                        {
                            await LogoutAsync();
                        }
                        return false;
                    }

                    CurrentUser = fetched.Profile;
                    await LocalStorageService.SaveProfileAsync(fetched.Profile);
                    AuthStateChanged?.Invoke(null, true);
                    return true;
                }

                CurrentUser = cached;
                AuthStateChanged?.Invoke(null, true);

                RefreshCurrentUserInBackground(token);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TryRestoreSessionAsync failed: {ex}");
                return false;
            }
        }

        private static async void RefreshCurrentUserInBackground(string token)
        {
            try
            {
                var fetched = await MisskeyAuthService.FetchCurrentUserAsync(token);

                // Check the token is still the live one before acting on anything: the user may
                // have signed out, or signed in as someone else, while this was in flight.
                if (!string.Equals(await MisskeyAuthService.TryGetTokenAsync(), token, StringComparison.Ordinal))
                {
                    return;
                }

                if (fetched.TokenRejected)
                {
                    // The instance says this token is gone - revoked from fort.social's settings,
                    // or the account was deleted. The cached profile would otherwise keep the app
                    // looking signed in indefinitely against credentials that can never work, with
                    // every request silently failing behind a perfectly normal-looking UI.
                    Debug.WriteLine("ProfileService: stored token was rejected; signing out");
                    await LogoutAsync();
                    return;
                }

                if (fetched.Profile == null) return;

                CurrentUser = fetched.Profile;
                await LocalStorageService.SaveProfileAsync(fetched.Profile);
                AuthStateChanged?.Invoke(null, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RefreshCurrentUserInBackground failed: {ex.Message}");
            }
        }
    }

    public class LoginResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public UserProfile Profile { get; set; }

        public LoginResult(bool success, string message, UserProfile profile = null)
        {
            this.Success = success;
            this.Message = message;
            this.Profile = profile;
        }
    }
}
