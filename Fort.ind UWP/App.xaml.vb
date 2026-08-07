''' <summary>
''' Provides application-specific behavior to supplement the default Application class.
''' </summary>
NotInheritable Class App
    Inherits Application

    ''' <summary>
    ''' Invoked when the application is launched normally by the end user.  Other entry points
    ''' will be used when the application is launched to open a specific file, to display
    ''' search results, and so forth.
    ''' </summary>
    ''' <param name="e">Details about the launch request and process.</param>
    Protected Overrides Async Sub OnLaunched(e As Windows.ApplicationModel.Activation.LaunchActivatedEventArgs)
        Dim showStartupErrorDialog As Boolean = False
        Try
            Dim rootFrame As Frame = TryCast(Window.Current.Content, Frame)

        ' Do not repeat app initialization when the Window already has content,
        ' just ensure that the window is active

        If rootFrame Is Nothing Then
            ' Create a Frame to act as the navigation context and navigate to the first page
            rootFrame = New Frame()

            AddHandler rootFrame.NavigationFailed, AddressOf OnNavigationFailed

            If e.PreviousExecutionState = ApplicationExecutionState.Terminated Then
                ' TODO: Load state from previously suspended application
            End If

            ' Apply saved theme before rendering to prevent a flash of the default theme
            Dim savedTheme = Windows.Storage.ApplicationData.Current.LocalSettings.Values(AppConstants.SettingAppTheme)?.ToString()
            Select Case savedTheme
                Case "Light" : rootFrame.RequestedTheme = ElementTheme.Light
                Case "Dark"  : rootFrame.RequestedTheme = ElementTheme.Dark
                Case Else    : rootFrame.RequestedTheme = ElementTheme.Default
            End Select

            ' Place the frame in the current Window
            Window.Current.Content = rootFrame
        End If

        If e.PrelaunchActivated = False Then
            Dim isFirstNavigation = rootFrame.Content Is Nothing
            If isFirstNavigation Then
                ' Navigate to MainPage (it will handle the profile state)
                rootFrame.Navigate(GetType(MainPage), e.Arguments)
            End If

            ' Ensure the current window is active
            Window.Current.Activate()

            ' Session restore reads the credential vault, then the cached profile off disk,
            ' and - when there is a token but no cached profile - makes a network call to
            ' fort.social. Awaiting all that before the first Navigate/Activate meant a slow
            ' or unreachable instance held the window blank for the whole HTTP timeout.
            ' MainPage and ProfilePage both refresh from ProfileService.AuthStateChanged (and
            ' re-read CurrentUser when they load), so a session that lands after first paint
            ' is picked up normally.
            If isFirstNavigation Then
                RestoreSessionInBackground()
            End If
        End If
        Catch ex As Exception
            ' Log critical startup error
            Debug.WriteLine($"Critical: OnLaunched failed - {ex.Message}")
            showStartupErrorDialog = True
        End Try

        If showStartupErrorDialog AndAlso Window.Current.Content IsNot Nothing Then
            Try
                Dim errorDialog As New ContentDialog()
                errorDialog.Title = "Startup Error"
                errorDialog.Content = "The application failed to start properly. Please try restarting."
                errorDialog.PrimaryButtonText = "OK"
                AppConstants.ApplyXamlRoot(errorDialog, Window.Current.Content)
                Await errorDialog.ShowAsync()
            Catch
                ' Nothing more we can do
            End Try
        End If
    End Sub

    ''' <summary>
    ''' Initializes storage and restores any saved session without blocking the first frame.
    ''' Async Sub, so every path is wrapped - an unhandled exception here would crash the app.
    ''' </summary>
    Private Async Sub RestoreSessionInBackground()
        Try
            Await LocalStorageService.InitializeAsync()
            Await ProfileService.TryRestoreSessionAsync()
        Catch ex As Exception
            Debug.WriteLine($"App: background session restore failed - {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' Invoked when the app is activated by something other than a normal launch - here, only
    ''' the "fortind:" protocol, used as the MiAuth callback so the system browser can hand
    ''' control back to the app once the user approves sign-in on fort.social.
    ''' </summary>
    Protected Overrides Async Sub OnActivated(args As Windows.ApplicationModel.Activation.IActivatedEventArgs)
        Try
            If args.Kind <> Windows.ApplicationModel.Activation.ActivationKind.Protocol Then Return

            Dim protocolArgs = TryCast(args, Windows.ApplicationModel.Activation.ProtocolActivatedEventArgs)
            If protocolArgs Is Nothing Then Return

            ' Deliberately not logging the URI itself: its "session" parameter is the secret
            ' that gets exchanged for an access token, and debug output is not a private sink.
            Debug.WriteLine($"OnActivated: protocol callback received for {protocolArgs.Uri.Scheme}://{protocolArgs.Uri.Host}")

            Dim rootFrame As Frame = TryCast(Window.Current.Content, Frame)
            Dim isColdStart = rootFrame Is Nothing

            If isColdStart Then
                rootFrame = New Frame()
                AddHandler rootFrame.NavigationFailed, AddressOf OnNavigationFailed
                Window.Current.Content = rootFrame
                Await LocalStorageService.InitializeAsync()
                Await ProfileService.TryRestoreSessionAsync()
            End If

            Dim signInResult = Await MisskeyAuthService.HandleProtocolActivationAsync(protocolArgs.Uri)
            If signInResult IsNot Nothing AndAlso signInResult.Success Then
                Await ProfileService.ApplySignInResultAsync(signInResult)
            End If

            If isColdStart AndAlso rootFrame.Content Is Nothing Then
                rootFrame.Navigate(GetType(MainPage))
            End If

            Window.Current.Activate()
        Catch ex As Exception
            Debug.WriteLine($"OnActivated failed: {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' Invoked when Navigation to a certain page fails
    ''' </summary>
    ''' <param name="sender">The Frame which failed navigation</param>
    ''' <param name="e">Details about the navigation failure</param>
    Private Async Sub OnNavigationFailed(sender As Object, e As NavigationFailedEventArgs)
        ' Log the error instead of crashing the app
        Debug.WriteLine($"Navigation failed: {e.SourcePageType.FullName} - {If(e.Exception IsNot Nothing, e.Exception.Message, "Unknown error")}")
        
        ' Show user-friendly error dialog
        Try
            Dim errorDialog As New ContentDialog()
            errorDialog.Title = "Navigation Error"
            errorDialog.Content = $"Failed to load page. The application will return to the home screen."
            errorDialog.PrimaryButtonText = "OK"
            errorDialog.DefaultButton = ContentDialogButton.Primary
            AppConstants.ApplyXamlRoot(errorDialog, Window.Current.Content)
            Await errorDialog.ShowAsync()
        Catch ex As Exception
            ' If dialog fails, just log it
            Debug.WriteLine($"Error dialog failed: {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' Invoked when application execution is being suspended.  Application state is saved
    ''' without knowing whether the application will be terminated or resumed with the contents
    ''' of memory still intact.
    ''' </summary>
    ''' <param name="sender">The source of the suspend request.</param>
    ''' <param name="e">Details about the suspend request.</param>
    Private Sub OnSuspending(sender As Object, e As SuspendingEventArgs) Handles Me.Suspending
        Dim deferral As SuspendingDeferral = e.SuspendingOperation.GetDeferral()
        ' TODO: Save application state and stop any background activity
        deferral.Complete()
    End Sub

End Class
