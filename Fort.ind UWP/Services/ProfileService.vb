''' <summary>
''' Manages the signed-in fort.social (Misskey) identity: sign-in via MiAuth, session restore,
''' and sign-out. fort.social is the source of truth for the account; there is no local
''' registration/password system.
''' </summary>
Public Class ProfileService

    ''' <summary>
    ''' The currently signed-in user profile
    ''' </summary>
    Public Shared Property CurrentUser As UserProfile

    ''' <summary>
    ''' Event raised when the user signs in or out
    ''' </summary>
    Public Shared Event AuthStateChanged As EventHandler(Of Boolean)

    ''' <summary>
    ''' Runs the MiAuth sign-in flow against fort.social (opens the system browser and waits
    ''' for it to redirect back into the app). On success, caches the returned profile and
    ''' access token.
    ''' </summary>
    Public Shared Async Function LoginWithMisskeyAsync() As Task(Of LoginResult)
        Dim result = Await MisskeyAuthService.SignInAsync()
        If Not result.Success Then
            Return New LoginResult(False, result.ErrorMessage)
        End If

        Await ApplySignInResultAsync(result)
        Return New LoginResult(True, "Signed in!", result.Profile)
    End Function

    ''' <summary>
    ''' Applies a completed MiAuth sign-in (profile + token already obtained), caching the
    ''' profile and notifying listeners. Shared by the normal in-app sign-in flow and the
    ''' cold-start protocol-activation path (App.OnActivated), where the app process that
    ''' started SignInAsync may no longer be the one that receives the browser's callback.
    ''' </summary>
    Public Shared Async Function ApplySignInResultAsync(result As MisskeyAuthResult) As Task(Of Boolean)
        If result Is Nothing OrElse Not result.Success Then Return False

        CurrentUser = result.Profile
        Await LocalStorageService.SaveProfileAsync(result.Profile)
        RaiseEvent AuthStateChanged(Nothing, True)

        Dim displayName = If(String.IsNullOrWhiteSpace(result.Profile.DisplayName), result.Profile.Username, result.Profile.DisplayName)
        LiveTileService.SendToast("Welcome back!", $"Signed in as {displayName}")

        Return True
    End Function

    ''' <summary>
    ''' Signs the current user out and clears the cached profile and access token.
    ''' </summary>
    Public Shared Async Function LogoutAsync() As Task
        Dim name = If(CurrentUser IsNot Nothing, If(String.IsNullOrWhiteSpace(CurrentUser.DisplayName), CurrentUser.Username, CurrentUser.DisplayName), "")
        CurrentUser = Nothing
        MisskeyAuthService.ClearToken()
        Await LocalStorageService.ClearProfileAsync()
        RaiseEvent AuthStateChanged(Nothing, False)
        If Not String.IsNullOrEmpty(name) Then LiveTileService.SendToast("Signed out", $"Goodbye, {name}!")
    End Function

    ''' <summary>
    ''' Full app reset: signs out, clears the stored auth token, clears the live tile/badge,
    ''' and wipes every file and setting this app has ever written locally - cached profile,
    ''' sitemap cache, theme/tint/panel preferences, everything. Leaves the app in the same
    ''' state as a fresh install. Does not affect the fort.social account itself.
    ''' </summary>
    Public Shared Async Function ResetAppDataAsync() As Task
        MisskeyAuthService.ClearToken()
        CurrentUser = Nothing
        LiveTileService.ClearTile()
        LiveTileService.ClearBadge()
        Await LocalStorageService.ResetAllAppDataAsync()
        RaiseEvent AuthStateChanged(Nothing, False)
    End Function

    ''' <summary>
    ''' Restores a session at app startup. Shows the cached profile immediately (so the UI
    ''' isn't blocked on network access), then refreshes it from fort.social in the background.
    ''' If there's no cached profile yet (token present but first run after an update), it
    ''' fetches synchronously instead.
    ''' </summary>
    Public Shared Async Function TryRestoreSessionAsync() As Task(Of Boolean)
        Try
            Dim token = Await MisskeyAuthService.TryGetTokenAsync()
            If String.IsNullOrEmpty(token) Then
                Return False
            End If

            Dim cached = Await LocalStorageService.LoadProfileAsync()
            If cached Is Nothing Then
                Dim fetched = Await MisskeyAuthService.FetchCurrentUserAsync(token)
                If fetched Is Nothing Then
                    Await LogoutAsync()
                    Return False
                End If

                CurrentUser = fetched
                Await LocalStorageService.SaveProfileAsync(fetched)
                RaiseEvent AuthStateChanged(Nothing, True)
                Return True
            End If

            CurrentUser = cached
            RaiseEvent AuthStateChanged(Nothing, True)

            ' Refresh from fort.social in the background so a slow/offline network doesn't
            ' delay startup. A revoked token simply leaves the cached profile in place until
            ' the user explicitly signs out.
            RefreshCurrentUserInBackground(token)

            Return True
        Catch ex As Exception
            Debug.WriteLine($"TryRestoreSessionAsync failed: {ex}")
            Return False
        End Try
    End Function

    Private Shared Async Sub RefreshCurrentUserInBackground(token As String)
        Try
            Dim fetched = Await MisskeyAuthService.FetchCurrentUserAsync(token)
            If fetched Is Nothing Then Return

            ' The user may have signed out, reset the app, or signed into a different account
            ' while this network call was in flight. Applying a stale result in that case would
            ' silently resurrect a session the user just explicitly cleared (or clobber a newer
            ' one), so only apply it if our token is still the one currently signed in.
            If Not String.Equals(Await MisskeyAuthService.TryGetTokenAsync(), token, StringComparison.Ordinal) Then
                Return
            End If

            CurrentUser = fetched
            Await LocalStorageService.SaveProfileAsync(fetched)
            RaiseEvent AuthStateChanged(Nothing, True)
        Catch ex As Exception
            Debug.WriteLine($"RefreshCurrentUserInBackground failed: {ex.Message}")
        End Try
    End Sub

End Class

''' <summary>
''' Result of a sign-in attempt
''' </summary>
Public Class LoginResult
    Public Property Success As Boolean
    Public Property Message As String
    Public Property Profile As UserProfile

    Public Sub New(success As Boolean, message As String, Optional profile As UserProfile = Nothing)
        Me.Success = success
        Me.Message = message
        Me.Profile = profile
    End Sub
End Class
