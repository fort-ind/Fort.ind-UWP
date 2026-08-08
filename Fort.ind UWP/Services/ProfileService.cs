using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Fort.ind_UWP
{
    /// <summary>
    /// Manages the signed-in fort.social (Misskey) identity: sign-in via MiAuth, session restore,
    /// and sign-out. fort.social is the source of truth for the account; there is no local
    /// registration/password system.
    /// </summary>
    public class ProfileService
    {

        /// <summary>
        /// The currently signed-in user profile
        /// </summary>
        public static UserProfile CurrentUser { get; set; }

        /// <summary>
        /// Event raised when the user signs in or out
        /// </summary>
        public static event EventHandler<bool> AuthStateChanged;

        /// <summary>
        /// Runs the MiAuth sign-in flow against fort.social (opens the system browser and waits
        /// for it to redirect back into the app). On success, caches the returned profile and
        /// access token.
        /// </summary>
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

        /// <summary>
        /// Applies a completed MiAuth sign-in (profile + token already obtained), caching the
        /// profile and notifying listeners. Shared by the normal in-app sign-in flow and the
        /// cold-start protocol-activation path (App.OnActivated), where the app process that
        /// started SignInAsync may no longer be the one that receives the browser's callback.
        /// </summary>
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

        /// <summary>
        /// Signs the current user out and clears the cached profile and access token.
        /// </summary>
        public static async Task LogoutAsync()
        {
            var name = CurrentUser != null ? (string.IsNullOrWhiteSpace(CurrentUser.DisplayName) ? CurrentUser.Username : CurrentUser.DisplayName) : "";
            CurrentUser = null;
            MisskeyAuthService.ClearToken();
            await LocalStorageService.ClearProfileAsync();
            AuthStateChanged?.Invoke(null, false);
            if (!string.IsNullOrEmpty(name)) LiveTileService.SendToast("Signed out", $"Goodbye, {name}!");
        }

        /// <summary>
        /// Full app reset: signs out, clears the stored auth token, clears the live tile/badge,
        /// and wipes every file and setting this app has ever written locally - cached profile,
        /// sitemap cache, theme/tint/panel preferences, everything. Leaves the app in the same
        /// state as a fresh install. Does not affect the fort.social account itself.
        /// </summary>
        public static async Task ResetAppDataAsync()
        {
            MisskeyAuthService.ClearToken();
            CurrentUser = null;
            LiveTileService.ClearTile();
            LiveTileService.ClearBadge();
            await LocalStorageService.ResetAllAppDataAsync();
            AuthStateChanged?.Invoke(null, false);
        }

        /// <summary>
        /// Restores a session at app startup. Shows the cached profile immediately (so the UI
        /// isn't blocked on network access), then refreshes it from fort.social in the background.
        /// If there's no cached profile yet (token present but first run after an update), it
        /// fetches synchronously instead.
        /// </summary>
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
                    if (fetched == null)
                    {
                        await LogoutAsync();
                        return false;
                    }

                    CurrentUser = fetched;
                    await LocalStorageService.SaveProfileAsync(fetched);
                    AuthStateChanged?.Invoke(null, true);
                    return true;
                }

                CurrentUser = cached;
                AuthStateChanged?.Invoke(null, true);

                // Refresh from fort.social in the background so a slow/offline network doesn't
                // delay startup. A revoked token simply leaves the cached profile in place until
                // the user explicitly signs out.
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
                if (fetched == null) return;

                // The user may have signed out, reset the app, or signed into a different account
                // while this network call was in flight. Applying a stale result in that case would
                // silently resurrect a session the user just explicitly cleared (or clobber a newer
                // one), so only apply it if our token is still the one currently signed in.
                if (!string.Equals(await MisskeyAuthService.TryGetTokenAsync(), token, StringComparison.Ordinal))
                {
                    return;
                }

                CurrentUser = fetched;
                await LocalStorageService.SaveProfileAsync(fetched);
                AuthStateChanged?.Invoke(null, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RefreshCurrentUserInBackground failed: {ex.Message}");
            }
        }

    }

    /// <summary>
    /// Result of a sign-in attempt
    /// </summary>
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
