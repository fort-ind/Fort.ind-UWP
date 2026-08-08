Imports Windows.Data.Json
Imports Windows.Security.Credentials
Imports Windows.Web.Http

''' <summary>
''' Signs the user in against the fort.social Misskey instance using MiAuth
''' (https://misskey-hub.net/en/docs/for-developers/api/token/miauth/) and stores the
''' resulting access token in the Windows credential vault.
'''
''' The consent page is opened in the user's default browser rather than
''' WebAuthenticationBroker's embedded web view - Sharkey/Misskey's frontend is a modern SPA
''' that hangs indefinitely in WAB's legacy embedded browser control. The app gets control
''' back via protocol activation: the callback URL uses a custom "fortind:" scheme registered
''' in the app manifest, so once fort.social redirects to it, Windows re-activates this app
''' with that URI (App.OnActivated), which we translate into a completed sign-in.
''' </summary>
Public Class MisskeyAuthService

    Public Const InstanceHost As String = "social.fort1nd.com"
    Private Const AppName As String = "Fort.ind"
    Private Const RequestedPermissions As String = "read:account"

    Private Const VaultResource As String = "Fort.ind.Misskey"
    Private Const VaultUsernameKey As String = "token"

    ''' <summary>
    ''' Must match the uap:Protocol Name registered in Package.appxmanifest.
    ''' </summary>
    Private Const CallbackScheme As String = "fortind"
    Private Const CallbackHost As String = "miauth-callback"
    Private Const CallbackSessionParam As String = "session"

    ''' <summary>
    ''' Local-settings keys used to recognize our own callback across a suspend/terminate
    ''' cycle (see HandleProtocolActivationAsync's cold-start path). Never trust a callback
    ''' whose session doesn't match what's recorded here - anyone can invoke a registered
    ''' custom URI scheme, so this is what stops a crafted "fortind://miauth-callback?session=..."
    ''' link from signing the app into an attacker-controlled account.
    ''' </summary>
    Private Const PendingSessionSettingKey As String = "MisskeyAuth.PendingSession"
    Private Const PendingSessionIssuedAtSettingKey As String = "MisskeyAuth.PendingSessionIssuedAtUtc"
    Private Shared ReadOnly PendingSessionExpiry As TimeSpan = TimeSpan.FromMinutes(10)

    Private Shared ReadOnly s_lock As New Object()
    Private Shared s_pendingSession As String = Nothing
    Private Shared s_pendingCompletion As TaskCompletionSource(Of Boolean) = Nothing

    ''' <summary>
    ''' One client for the process. A new HttpClient per request throws away the pooled
    ''' connection with it, so every sign-in and every background profile refresh paid for a
    ''' fresh TCP connect plus TLS handshake against the same host.
    '''
    ''' Lazy, not a plain field initializer: this type's shared constructor runs as soon as
    ''' anything touches it - including TryGetToken on the startup path - and constructing a
    ''' Windows.Web.Http.HttpClient brings up a default HttpBaseProtocolFilter with its own
    ''' response cache and cookie manager. A signed-out launch makes no request at all, so
    ''' eagerly building (and never disposing) all of that just adds to the working set for
    ''' the life of the process.
    ''' </summary>
    Private Shared ReadOnly s_client As New Lazy(Of HttpClient)(Function() New HttpClient())

    ''' <summary>
    ''' How long SignInAsync waits for the browser to hand control back before giving up.
    ''' </summary>
    Private Shared ReadOnly SignInTimeout As TimeSpan = TimeSpan.FromMinutes(5)

    ''' <summary>
    ''' Opens the fort.social consent page in the system browser, then waits for the browser
    ''' to redirect back into the app (via protocol activation) before exchanging the approved
    ''' session for an access token. Times out if the user never completes the browser flow.
    ''' </summary>
    Public Shared Async Function SignInAsync() As Task(Of MisskeyAuthResult)
        Dim session = Guid.NewGuid().ToString()
        Dim callbackUri As New Uri($"{CallbackScheme}://{CallbackHost}?{CallbackSessionParam}={session}")

        Dim startUri As New Uri(
            $"https://{InstanceHost}/miauth/{session}" &
            $"?name={Uri.EscapeDataString(AppName)}" &
            $"&callback={Uri.EscapeDataString(callbackUri.ToString())}" &
            $"&permission={RequestedPermissions}")

        Dim completion As New TaskCompletionSource(Of Boolean)
        SyncLock s_lock
            s_pendingSession = session
            s_pendingCompletion = completion
        End SyncLock
        PersistPendingSession(session)

        Try
            Dim launched = Await Windows.System.Launcher.LaunchUriAsync(startUri)
            If Not launched Then
                ClearPending(session)
                Return MisskeyAuthResult.Failed("Could not open your browser to sign in.")
            End If
        Catch ex As Exception
            ClearPending(session)
            Debug.WriteLine($"MisskeyAuthService: launch failed - {ex.Message}")
            Return MisskeyAuthResult.Failed("Could not open your browser to sign in.")
        End Try

        ' The timeout task is cancelled once the browser comes back, otherwise a completed
        ' sign-in would still leave a live 5-minute timer (and its continuation) rooted.
        Dim timeoutCts As New Threading.CancellationTokenSource()
        Dim finished As Task = Nothing
        Try
            finished = Await Task.WhenAny(completion.Task, Task.Delay(SignInTimeout, timeoutCts.Token))
        Finally
            timeoutCts.Cancel()
            timeoutCts.Dispose()
        End Try

        If finished IsNot completion.Task Then
            ClearPending(session)
            Return MisskeyAuthResult.Failed("Sign-in timed out. Please try again.")
        End If

        Dim approved = Await completion.Task
        If Not approved Then
            Return MisskeyAuthResult.Failed("Sign-in was cancelled.")
        End If

        Return Await CompleteSessionAsync(session)
    End Function

    ''' <summary>
    ''' Call from App.OnActivated when the app is reactivated via the "fortind:" protocol.
    ''' Anyone can invoke a registered custom URI scheme - another installed app, a webpage
    ''' link, a shortcut - so this never trusts an incoming callback on its own. It's only
    ''' honored if its session matches a session *we* issued:
    '''   - If a SignInAsync call is still waiting in this process, the callback's session
    '''     must match that in-flight session; only then is it unblocked (it does the token
    '''     exchange itself). A mismatched session is ignored and the real sign-in keeps
    '''     waiting.
    '''   - Otherwise - e.g. the app was suspended/terminated while the user was in the
    '''     browser - the session must match the one persisted by SignInAsync and still be
    '''     within PendingSessionExpiry. Only then is the exchange completed directly.
    ''' Any callback that fails both checks is rejected outright.
    ''' </summary>
    Public Shared Async Function HandleProtocolActivationAsync(uri As Uri) As Task(Of MisskeyAuthResult)
        If uri Is Nothing OrElse Not String.Equals(uri.Host, CallbackHost, StringComparison.OrdinalIgnoreCase) Then
            Return MisskeyAuthResult.Failed("Unrecognized sign-in callback.")
        End If

        Dim session = ExtractSessionFromCallback(uri)
        If String.IsNullOrWhiteSpace(session) Then
            Return MisskeyAuthResult.Failed("Sign-in link was missing session information.")
        End If

        Dim completion As TaskCompletionSource(Of Boolean) = Nothing
        SyncLock s_lock
            If s_pendingCompletion IsNot Nothing AndAlso String.Equals(s_pendingSession, session, StringComparison.Ordinal) Then
                completion = s_pendingCompletion
                s_pendingSession = Nothing
                s_pendingCompletion = Nothing
            End If
        End SyncLock

        If completion IsNot Nothing Then
            ClearPersistedSession()
            completion.TrySetResult(True)
            Return Nothing
        End If

        ' No in-process sign-in waiting on this exact session. Only fall back to the
        ' cold-start path if it's the session we ourselves persisted before launching the
        ' browser - otherwise this is a foreign/forged callback and must be rejected.
        If Not TryConsumePersistedSession(session) Then
            Return MisskeyAuthResult.Failed("This sign-in link is not valid.")
        End If

        Return Await CompleteSessionAsync(session)
    End Function

    ''' <summary>
    ''' Cancels an in-flight sign-in (e.g. the user gives up while stuck in the browser).
    ''' </summary>
    Public Shared Sub CancelPendingSignIn()
        Dim completion As TaskCompletionSource(Of Boolean) = Nothing
        SyncLock s_lock
            completion = s_pendingCompletion
            s_pendingSession = Nothing
            s_pendingCompletion = Nothing
        End SyncLock
        ClearPersistedSession()
        completion?.TrySetResult(False)
    End Sub

    Private Shared Sub ClearPending(session As String)
        SyncLock s_lock
            If s_pendingSession = session Then
                s_pendingSession = Nothing
                s_pendingCompletion = Nothing
            End If
        End SyncLock
        ClearPersistedSession()
    End Sub

    ''' <summary>
    ''' Records the session SignInAsync just issued so a cold-start callback (app suspended
    ''' or terminated while the user was in the browser) can be verified against it later.
    ''' </summary>
    Private Shared Sub PersistPendingSession(session As String)
        Dim values = Windows.Storage.ApplicationData.Current.LocalSettings.Values
        values(PendingSessionSettingKey) = session
        values(PendingSessionIssuedAtSettingKey) = DateTimeOffset.UtcNow.ToString("o")
    End Sub

    ''' <summary>
    ''' Checks whether the given session matches the one persisted by PersistPendingSession
    ''' and hasn't expired; if so, consumes (clears) it so it can't be replayed.
    ''' </summary>
    Private Shared Function TryConsumePersistedSession(session As String) As Boolean
        Dim values = Windows.Storage.ApplicationData.Current.LocalSettings.Values
        Dim storedSession = TryCast(values(PendingSessionSettingKey), String)
        Dim storedIssuedAtRaw = TryCast(values(PendingSessionIssuedAtSettingKey), String)

        If String.IsNullOrEmpty(storedSession) OrElse String.IsNullOrEmpty(storedIssuedAtRaw) Then
            Return False
        End If
        If Not String.Equals(storedSession, session, StringComparison.Ordinal) Then
            Return False
        End If

        Dim issuedAt As DateTimeOffset
        If Not DateTimeOffset.TryParse(storedIssuedAtRaw, System.Globalization.CultureInfo.InvariantCulture,
                                        System.Globalization.DateTimeStyles.RoundtripKind, issuedAt) Then
            Return False
        End If
        If DateTimeOffset.UtcNow - issuedAt > PendingSessionExpiry Then
            Return False
        End If

        ClearPersistedSession()
        Return True
    End Function

    Private Shared Sub ClearPersistedSession()
        Dim values = Windows.Storage.ApplicationData.Current.LocalSettings.Values
        values.Remove(PendingSessionSettingKey)
        values.Remove(PendingSessionIssuedAtSettingKey)
    End Sub

    Private Shared Function ExtractSessionFromCallback(uri As Uri) As String
        Try
            If uri Is Nothing OrElse String.IsNullOrEmpty(uri.Query) Then Return Nothing
            Dim decoder As New Windows.Foundation.WwwFormUrlDecoder(uri.Query)
            For Each entry In decoder
                If String.Equals(entry.Name, CallbackSessionParam, StringComparison.OrdinalIgnoreCase) Then
                    Return entry.Value
                End If
            Next
        Catch ex As Exception
            Debug.WriteLine($"MisskeyAuthService: failed to parse callback URI - {ex.Message}")
        End Try
        Return Nothing
    End Function

    ''' <summary>
    ''' Exchanges an approved MiAuth session for an access token, per
    ''' POST /api/miauth/{session}/check.
    ''' </summary>
    Private Shared Async Function CompleteSessionAsync(session As String) As Task(Of MisskeyAuthResult)
        Try
            Dim checkUri As New Uri($"https://{InstanceHost}/api/miauth/{Uri.EscapeDataString(session)}/check")
            Using content As New HttpStringContent("{}", Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json")
                Using response = Await s_client.Value.PostAsync(checkUri, content)
                    response.EnsureSuccessStatusCode()

                    Dim body = Await response.Content.ReadAsStringAsync()
                    Dim json = JsonObject.Parse(body)

                    If Not json.GetNamedBoolean("ok", False) Then
                        Return MisskeyAuthResult.Failed("fort.social did not approve the sign-in.")
                    End If

                    Dim token = json.GetNamedString("token", "")
                    If String.IsNullOrWhiteSpace(token) Then
                        Return MisskeyAuthResult.Failed("fort.social did not return an access token.")
                    End If

                    Dim profile = ParseUser(GetNamedObjectOrNull(json, "user"))
                    If profile Is Nothing Then
                        Return MisskeyAuthResult.Failed("fort.social did not return account details.")
                    End If

                    SaveToken(token)
                    Return MisskeyAuthResult.Succeeded(token, profile)
                End Using
            End Using
        Catch ex As Exception
            Debug.WriteLine($"MisskeyAuthService: check failed - {ex.Message}")
            Return MisskeyAuthResult.Failed("Could not reach fort.social.")
        End Try
    End Function

    ''' <summary>
    ''' Re-fetches the signed-in user's profile using a previously issued token, per POST /api/i.
    ''' Returns Nothing if the token is missing, invalid, or the instance is unreachable.
    ''' </summary>
    Public Shared Async Function FetchCurrentUserAsync(token As String) As Task(Of UserProfile)
        If String.IsNullOrWhiteSpace(token) Then Return Nothing

        Try
            Dim uri As New Uri($"https://{InstanceHost}/api/i")
            Dim bodyJson As New JsonObject()
            bodyJson.Add("i", JsonValue.CreateStringValue(token))

            Using content As New HttpStringContent(bodyJson.Stringify(), Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json")
                Using response = Await s_client.Value.PostAsync(uri, content)
                    If Not response.IsSuccessStatusCode Then Return Nothing

                    Dim body = Await response.Content.ReadAsStringAsync()
                    Return ParseUser(JsonObject.Parse(body))
                End Using
            End Using
        Catch ex As Exception
            Debug.WriteLine($"MisskeyAuthService: /api/i failed - {ex.Message}")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Builds a UserProfile from a Misskey user JSON object (works for both the "user" object
    ''' nested in the MiAuth check response and the root object returned by /api/i).
    ''' </summary>
    Private Shared Function ParseUser(obj As JsonObject) As UserProfile
        If obj Is Nothing Then Return Nothing

        Dim id = JsonString(obj, "id")
        Dim username = JsonString(obj, "username")
        If String.IsNullOrWhiteSpace(id) OrElse String.IsNullOrWhiteSpace(username) Then Return Nothing

        Dim profile As New UserProfile()
        profile.UserId = id
        profile.Username = username
        profile.Host = JsonString(obj, "host")
        profile.DisplayName = JsonString(obj, "name")
        profile.Bio = JsonString(obj, "description")
        profile.AvatarUrl = JsonString(obj, "avatarUrl")
        profile.LastLoginDate = DateTime.Now

        Dim createdAt = JsonString(obj, "createdAt")
        Dim parsedDate As DateTime
        If Not String.IsNullOrWhiteSpace(createdAt) AndAlso
           DateTime.TryParse(createdAt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, parsedDate) Then
            profile.CreatedDate = parsedDate
        End If

        Return profile
    End Function

    ''' <summary>
    ''' Reads a string field, tolerating a missing key or a JSON null (both routinely occur
    ''' for fields like "host" or "name" on Misskey accounts).
    ''' </summary>
    Private Shared Function JsonString(obj As JsonObject, key As String) As String
        If obj Is Nothing OrElse Not obj.ContainsKey(key) Then Return Nothing
        Dim v = obj.GetNamedValue(key)
        If v.ValueType <> JsonValueType.String Then Return Nothing
        Return v.GetString()
    End Function

    Private Shared Function GetNamedObjectOrNull(obj As JsonObject, key As String) As JsonObject
        If obj Is Nothing OrElse Not obj.ContainsKey(key) Then Return Nothing
        If obj.GetNamedValue(key).ValueType <> JsonValueType.Object Then Return Nothing
        Return obj.GetNamedObject(key)
    End Function

#Region "Token Storage"

    ''' <summary>
    ''' Persists the access token in the Windows credential vault, replacing any existing one.
    ''' </summary>
    Private Shared Sub SaveToken(token As String)
        ClearToken()
        Dim vault As New PasswordVault()
        vault.Add(New PasswordCredential(VaultResource, VaultUsernameKey, token))
    End Sub

    ''' <summary>
    ''' Retrieves the stored access token, or Nothing if the user isn't signed in.
    ''' PasswordVault throws (rather than returning Nothing) when no credential is stored,
    ''' so a missing token is treated as the normal "not signed in" case.
    ''' </summary>
    Public Shared Function TryGetToken() As String
        Try
            Dim vault As New PasswordVault()
            Dim credential = vault.Retrieve(VaultResource, VaultUsernameKey)
            credential.RetrievePassword()
            Return credential.Password
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' TryGetToken, off the calling thread. PasswordVault is a cross-process call that is slow
    ''' on first use, and "nothing stored" is signalled by an exception rather than a null - so
    ''' the signed-out case is the expensive one. Startup calls this from the UI thread, where
    ''' the synchronous version stalls the window after it has already been shown.
    ''' </summary>
    Public Shared Function TryGetTokenAsync() As Task(Of String)
        Return Task.Run(Function() TryGetToken())
    End Function

    ''' <summary>
    ''' Removes the stored access token, if any.
    ''' </summary>
    Public Shared Sub ClearToken()
        Try
            Dim vault As New PasswordVault()
            Dim credential = vault.Retrieve(VaultResource, VaultUsernameKey)
            vault.Remove(credential)
        Catch
            ' Nothing stored - already signed out.
        End Try
    End Sub

#End Region

End Class

''' <summary>
''' Result of a MiAuth sign-in attempt.
''' </summary>
Public Class MisskeyAuthResult
    Public Property Success As Boolean
    Public Property ErrorMessage As String
    Public Property Token As String
    Public Property Profile As UserProfile

    Private Sub New()
    End Sub

    Public Shared Function Failed(message As String) As MisskeyAuthResult
        Return New MisskeyAuthResult With {.Success = False, .ErrorMessage = message}
    End Function

    Public Shared Function Succeeded(token As String, profile As UserProfile) As MisskeyAuthResult
        Return New MisskeyAuthResult With {.Success = True, .Token = token, .Profile = profile}
    End Function
End Class
