using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Security.Credentials;
using Windows.Web.Http;

namespace Fort.ind_UWP
{
    /// <summary>
    /// Signs the user in against the fort.social Misskey instance using MiAuth
    /// (https://misskey-hub.net/en/docs/for-developers/api/token/miauth/) and stores the
    /// resulting access token in the Windows credential vault.
    ///
    /// The consent page is opened in the user's default browser rather than
    /// WebAuthenticationBroker's embedded web view - Sharkey/Misskey's frontend is a modern SPA
    /// that hangs indefinitely in WAB's legacy embedded browser control. The app gets control
    /// back via protocol activation: the callback URL uses a custom "fortind:" scheme registered
    /// in the app manifest, so once fort.social redirects to it, Windows re-activates this app
    /// with that URI (App.OnActivated), which we translate into a completed sign-in.
    /// </summary>
    public class MisskeyAuthService
    {

        public const string InstanceHost = "social.fort1nd.com";
        private const string AppName = "Fort.ind";
        private const string RequestedPermissions = "read:account";

        private const string VaultResource = "Fort.ind.Misskey";
        private const string VaultUsernameKey = "token";

        /// <summary>
        /// Must match the uap:Protocol Name registered in Package.appxmanifest.
        /// </summary>
        private const string CallbackScheme = "fortind";
        private const string CallbackHost = "miauth-callback";
        private const string CallbackSessionParam = "session";

        /// <summary>
        /// Local-settings keys used to recognize our own callback across a suspend/terminate
        /// cycle (see HandleProtocolActivationAsync's cold-start path). Never trust a callback
        /// whose session doesn't match what's recorded here - anyone can invoke a registered
        /// custom URI scheme, so this is what stops a crafted "fortind://miauth-callback?session=..."
        /// link from signing the app into an attacker-controlled account.
        /// </summary>
        private const string PendingSessionSettingKey = "MisskeyAuth.PendingSession";
        private const string PendingSessionIssuedAtSettingKey = "MisskeyAuth.PendingSessionIssuedAtUtc";
        private static readonly TimeSpan PendingSessionExpiry = TimeSpan.FromMinutes(10);

        private static readonly object s_lock = new object();
        private static string s_pendingSession = null;
        private static TaskCompletionSource<bool> s_pendingCompletion = null;

        /// <summary>
        /// One client for the process. A new HttpClient per request throws away the pooled
        /// connection with it, so every sign-in and every background profile refresh paid for a
        /// fresh TCP connect plus TLS handshake against the same host.
        ///
        /// Lazy, not a plain field initializer: this type's static constructor runs as soon as
        /// anything touches it - including TryGetToken on the startup path - and constructing a
        /// Windows.Web.Http.HttpClient brings up a default HttpBaseProtocolFilter with its own
        /// response cache and cookie manager. A signed-out launch makes no request at all, so
        /// eagerly building (and never disposing) all of that just adds to the working set for
        /// the life of the process.
        /// </summary>
        private static readonly Lazy<HttpClient> s_client = new Lazy<HttpClient>(() => new HttpClient());

        /// <summary>
        /// How long SignInAsync waits for the browser to hand control back before giving up.
        /// </summary>
        private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Opens the fort.social consent page in the system browser, then waits for the browser
        /// to redirect back into the app (via protocol activation) before exchanging the approved
        /// session for an access token. Times out if the user never completes the browser flow.
        /// </summary>
        public static async Task<MisskeyAuthResult> SignInAsync()
        {
            var session = Guid.NewGuid().ToString();
            Uri callbackUri = new Uri($"{CallbackScheme}://{CallbackHost}?{CallbackSessionParam}={session}");

            Uri startUri = new Uri(
                $"https://{InstanceHost}/miauth/{session}" +
                $"?name={Uri.EscapeDataString(AppName)}" +
                $"&callback={Uri.EscapeDataString(callbackUri.ToString())}" +
                $"&permission={RequestedPermissions}");

            TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
            lock (s_lock)
            {
                s_pendingSession = session;
                s_pendingCompletion = completion;
            }
            PersistPendingSession(session);

            try
            {
                var launched = await Windows.System.Launcher.LaunchUriAsync(startUri);
                if (!launched)
                {
                    ClearPending(session);
                    return MisskeyAuthResult.Failed("Could not open your browser to sign in.");
                }
            }
            catch (Exception ex)
            {
                ClearPending(session);
                Debug.WriteLine($"MisskeyAuthService: launch failed - {ex.Message}");
                return MisskeyAuthResult.Failed("Could not open your browser to sign in.");
            }

            // The timeout task is cancelled once the browser comes back, otherwise a completed
            // sign-in would still leave a live 5-minute timer (and its continuation) rooted.
            CancellationTokenSource timeoutCts = new CancellationTokenSource();
            Task finished = null;
            try
            {
                finished = await Task.WhenAny(completion.Task, Task.Delay(SignInTimeout, timeoutCts.Token));
            }
            finally
            {
                timeoutCts.Cancel();
                timeoutCts.Dispose();
            }

            if (finished != completion.Task)
            {
                ClearPending(session);
                return MisskeyAuthResult.Failed("Sign-in timed out. Please try again.");
            }

            var approved = await completion.Task;
            if (!approved)
            {
                return MisskeyAuthResult.Failed("Sign-in was cancelled.");
            }

            return await CompleteSessionAsync(session);
        }

        /// <summary>
        /// Call from App.OnActivated when the app is reactivated via the "fortind:" protocol.
        /// Anyone can invoke a registered custom URI scheme - another installed app, a webpage
        /// link, a shortcut - so this never trusts an incoming callback on its own. It's only
        /// honored if its session matches a session *we* issued:
        ///   - If a SignInAsync call is still waiting in this process, the callback's session
        ///     must match that in-flight session; only then is it unblocked (it does the token
        ///     exchange itself). A mismatched session is ignored and the real sign-in keeps
        ///     waiting.
        ///   - Otherwise - e.g. the app was suspended/terminated while the user was in the
        ///     browser - the session must match the one persisted by SignInAsync and still be
        ///     within PendingSessionExpiry. Only then is the exchange completed directly.
        /// Any callback that fails both checks is rejected outright.
        /// </summary>
        public static async Task<MisskeyAuthResult> HandleProtocolActivationAsync(Uri uri)
        {
            if (uri == null || !string.Equals(uri.Host, CallbackHost, StringComparison.OrdinalIgnoreCase))
            {
                return MisskeyAuthResult.Failed("Unrecognized sign-in callback.");
            }

            var session = ExtractSessionFromCallback(uri);
            if (string.IsNullOrWhiteSpace(session))
            {
                return MisskeyAuthResult.Failed("Sign-in link was missing session information.");
            }

            TaskCompletionSource<bool> completion = null;
            lock (s_lock)
            {
                if (s_pendingCompletion != null && string.Equals(s_pendingSession, session, StringComparison.Ordinal))
                {
                    completion = s_pendingCompletion;
                    s_pendingSession = null;
                    s_pendingCompletion = null;
                }
            }

            if (completion != null)
            {
                ClearPersistedSession();
                completion.TrySetResult(true);
                return null;
            }

            // No in-process sign-in waiting on this exact session. Only fall back to the
            // cold-start path if it's the session we ourselves persisted before launching the
            // browser - otherwise this is a foreign/forged callback and must be rejected.
            if (!TryConsumePersistedSession(session))
            {
                return MisskeyAuthResult.Failed("This sign-in link is not valid.");
            }

            return await CompleteSessionAsync(session);
        }

        /// <summary>
        /// Cancels an in-flight sign-in (e.g. the user gives up while stuck in the browser).
        /// </summary>
        public static void CancelPendingSignIn()
        {
            TaskCompletionSource<bool> completion = null;
            lock (s_lock)
            {
                completion = s_pendingCompletion;
                s_pendingSession = null;
                s_pendingCompletion = null;
            }
            ClearPersistedSession();
            completion?.TrySetResult(false);
        }

        private static void ClearPending(string session)
        {
            lock (s_lock)
            {
                if (s_pendingSession == session)
                {
                    s_pendingSession = null;
                    s_pendingCompletion = null;
                }
            }
            ClearPersistedSession();
        }

        /// <summary>
        /// Records the session SignInAsync just issued so a cold-start callback (app suspended
        /// or terminated while the user was in the browser) can be verified against it later.
        /// </summary>
        private static void PersistPendingSession(string session)
        {
            var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            values[PendingSessionSettingKey] = session;
            values[PendingSessionIssuedAtSettingKey] = DateTimeOffset.UtcNow.ToString("o");
        }

        /// <summary>
        /// Checks whether the given session matches the one persisted by PersistPendingSession
        /// and hasn't expired; if so, consumes (clears) it so it can't be replayed.
        /// </summary>
        private static bool TryConsumePersistedSession(string session)
        {
            var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            var storedSession = values[PendingSessionSettingKey] as string;
            var storedIssuedAtRaw = values[PendingSessionIssuedAtSettingKey] as string;

            if (string.IsNullOrEmpty(storedSession) || string.IsNullOrEmpty(storedIssuedAtRaw))
            {
                return false;
            }
            if (!string.Equals(storedSession, session, StringComparison.Ordinal))
            {
                return false;
            }

            DateTimeOffset issuedAt;
            if (!DateTimeOffset.TryParse(storedIssuedAtRaw, System.Globalization.CultureInfo.InvariantCulture,
                                            System.Globalization.DateTimeStyles.RoundtripKind, out issuedAt))
            {
                return false;
            }
            if (DateTimeOffset.UtcNow - issuedAt > PendingSessionExpiry)
            {
                return false;
            }

            ClearPersistedSession();
            return true;
        }

        private static void ClearPersistedSession()
        {
            var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            values.Remove(PendingSessionSettingKey);
            values.Remove(PendingSessionIssuedAtSettingKey);
        }

        private static string ExtractSessionFromCallback(Uri uri)
        {
            try
            {
                if (uri == null || string.IsNullOrEmpty(uri.Query)) return null;
                Windows.Foundation.WwwFormUrlDecoder decoder = new Windows.Foundation.WwwFormUrlDecoder(uri.Query);
                foreach (var entry in decoder)
                {
                    if (string.Equals(entry.Name, CallbackSessionParam, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MisskeyAuthService: failed to parse callback URI - {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Exchanges an approved MiAuth session for an access token, per
        /// POST /api/miauth/{session}/check.
        /// </summary>
        private static async Task<MisskeyAuthResult> CompleteSessionAsync(string session)
        {
            try
            {
                Uri checkUri = new Uri($"https://{InstanceHost}/api/miauth/{Uri.EscapeDataString(session)}/check");
                using (HttpStringContent content = new HttpStringContent("{}", Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json"))
                {
                    using (var response = await s_client.Value.PostAsync(checkUri, content))
                    {
                        response.EnsureSuccessStatusCode();

                        var body = await response.Content.ReadAsStringAsync();
                        var json = JsonObject.Parse(body);

                        if (!json.GetNamedBoolean("ok", false))
                        {
                            return MisskeyAuthResult.Failed("fort.social did not approve the sign-in.");
                        }

                        var token = json.GetNamedString("token", "");
                        if (string.IsNullOrWhiteSpace(token))
                        {
                            return MisskeyAuthResult.Failed("fort.social did not return an access token.");
                        }

                        var profile = ParseUser(GetNamedObjectOrNull(json, "user"));
                        if (profile == null)
                        {
                            return MisskeyAuthResult.Failed("fort.social did not return account details.");
                        }

                        SaveToken(token);
                        return MisskeyAuthResult.Succeeded(token, profile);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MisskeyAuthService: check failed - {ex.Message}");
                return MisskeyAuthResult.Failed("Could not reach fort.social.");
            }
        }

        /// <summary>
        /// Re-fetches the signed-in user's profile using a previously issued token, per POST /api/i.
        /// Returns null if the token is missing, invalid, or the instance is unreachable.
        /// </summary>
        public static async Task<UserProfile> FetchCurrentUserAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;

            try
            {
                Uri uri = new Uri($"https://{InstanceHost}/api/i");
                JsonObject bodyJson = new JsonObject();
                bodyJson.Add("i", JsonValue.CreateStringValue(token));

                using (HttpStringContent content = new HttpStringContent(bodyJson.Stringify(), Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json"))
                {
                    using (var response = await s_client.Value.PostAsync(uri, content))
                    {
                        if (!response.IsSuccessStatusCode) return null;

                        var body = await response.Content.ReadAsStringAsync();
                        return ParseUser(JsonObject.Parse(body));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MisskeyAuthService: /api/i failed - {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Builds a UserProfile from a Misskey user JSON object (works for both the "user" object
        /// nested in the MiAuth check response and the root object returned by /api/i).
        /// </summary>
        private static UserProfile ParseUser(JsonObject obj)
        {
            if (obj == null) return null;

            var id = JsonString(obj, "id");
            var username = JsonString(obj, "username");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(username)) return null;

            UserProfile profile = new UserProfile();
            profile.UserId = id;
            profile.Username = username;
            profile.Host = JsonString(obj, "host");
            profile.DisplayName = JsonString(obj, "name");
            profile.Bio = JsonString(obj, "description");
            profile.AvatarUrl = JsonString(obj, "avatarUrl");
            profile.LastLoginDate = DateTime.Now;

            var createdAt = JsonString(obj, "createdAt");
            DateTime parsedDate;
            if (!string.IsNullOrWhiteSpace(createdAt) &&
                DateTime.TryParse(createdAt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out parsedDate))
            {
                profile.CreatedDate = parsedDate;
            }

            return profile;
        }

        /// <summary>
        /// Reads a string field, tolerating a missing key or a JSON null (both routinely occur
        /// for fields like "host" or "name" on Misskey accounts).
        /// </summary>
        private static string JsonString(JsonObject obj, string key)
        {
            if (obj == null || !obj.ContainsKey(key)) return null;
            var v = obj.GetNamedValue(key);
            if (v.ValueType != JsonValueType.String) return null;
            return v.GetString();
        }

        private static JsonObject GetNamedObjectOrNull(JsonObject obj, string key)
        {
            if (obj == null || !obj.ContainsKey(key)) return null;
            if (obj.GetNamedValue(key).ValueType != JsonValueType.Object) return null;
            return obj.GetNamedObject(key);
        }

        #region Token Storage

        /// <summary>
        /// Persists the access token in the Windows credential vault, replacing any existing one.
        /// </summary>
        private static void SaveToken(string token)
        {
            ClearToken();
            PasswordVault vault = new PasswordVault();
            vault.Add(new PasswordCredential(VaultResource, VaultUsernameKey, token));
        }

        /// <summary>
        /// Retrieves the stored access token, or null if the user isn't signed in.
        /// PasswordVault throws (rather than returning null) when no credential is stored,
        /// so a missing token is treated as the normal "not signed in" case.
        /// </summary>
        public static string TryGetToken()
        {
            try
            {
                PasswordVault vault = new PasswordVault();
                var credential = vault.Retrieve(VaultResource, VaultUsernameKey);
                credential.RetrievePassword();
                return credential.Password;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// TryGetToken, off the calling thread. PasswordVault is a cross-process call that is slow
        /// on first use, and "nothing stored" is signalled by an exception rather than a null - so
        /// the signed-out case is the expensive one. Startup calls this from the UI thread, where
        /// the synchronous version stalls the window after it has already been shown.
        /// </summary>
        public static Task<string> TryGetTokenAsync()
        {
            return Task.Run(() => TryGetToken());
        }

        /// <summary>
        /// Removes the stored access token, if any.
        /// </summary>
        public static void ClearToken()
        {
            try
            {
                PasswordVault vault = new PasswordVault();
                var credential = vault.Retrieve(VaultResource, VaultUsernameKey);
                vault.Remove(credential);
            }
            catch
            {
                // Nothing stored - already signed out.
            }
        }

        #endregion

    }

    /// <summary>
    /// Result of a MiAuth sign-in attempt.
    /// </summary>
    public class MisskeyAuthResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string Token { get; set; }
        public UserProfile Profile { get; set; }

        private MisskeyAuthResult()
        {
        }

        public static MisskeyAuthResult Failed(string message)
        {
            return new MisskeyAuthResult { Success = false, ErrorMessage = message };
        }

        public static MisskeyAuthResult Succeeded(string token, UserProfile profile)
        {
            return new MisskeyAuthResult { Success = true, Token = token, Profile = profile };
        }
    }
}
