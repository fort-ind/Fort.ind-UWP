using System;
using System.Diagnostics;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    sealed partial class App : Application
    {

        /// <summary>
        /// Initializes the singleton application object. Unlike VB, C# does not synthesize this
        /// constructor for a XAML-backed class, so InitializeComponent (which loads
        /// Application.Resources, and with it XamlControlsResources) has to be called explicitly -
        /// without it every control silently falls back to the OS control templates. The
        /// Suspending subscription here replaces VB's "Handles Me.Suspending".
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used when the application is launched to open a specific file, to display
        /// search results, and so forth.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override async void OnLaunched(Windows.ApplicationModel.Activation.LaunchActivatedEventArgs e)
        {
            bool showStartupErrorDialog = false;
            try
            {
                Frame rootFrame = Window.Current.Content as Frame;

                // Do not repeat app initialization when the Window already has content,
                // just ensure that the window is active

                if (rootFrame == null)
                {
                    // Create a Frame to act as the navigation context and navigate to the first page
                    rootFrame = new Frame();

                    rootFrame.NavigationFailed += OnNavigationFailed;

                    if (e.PreviousExecutionState == ApplicationExecutionState.Terminated)
                    {
                        // TODO: Load state from previously suspended application
                    }

                    // Apply saved theme before rendering to prevent a flash of the default theme
                    var savedTheme = Windows.Storage.ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTheme]?.ToString();
                    switch (savedTheme)
                    {
                        case "Light": rootFrame.RequestedTheme = ElementTheme.Light; break;
                        case "Dark": rootFrame.RequestedTheme = ElementTheme.Dark; break;
                        default: rootFrame.RequestedTheme = ElementTheme.Default; break;
                    }

                    // Place the frame in the current Window
                    Window.Current.Content = rootFrame;
                }

                if (e.PrelaunchActivated == false)
                {
                    var isFirstNavigation = rootFrame.Content == null;
                    if (isFirstNavigation)
                    {
                        // Navigate to MainPage (it will handle the profile state)
                        rootFrame.Navigate(typeof(MainPage), e.Arguments);
                    }

                    // Ensure the current window is active
                    Window.Current.Activate();

                    // Session restore reads the credential vault, then the cached profile off disk,
                    // and - when there is a token but no cached profile - makes a network call to
                    // fort.social. Awaiting all that before the first Navigate/Activate meant a slow
                    // or unreachable instance held the window blank for the whole HTTP timeout.
                    // MainPage and ProfilePage both refresh from ProfileService.AuthStateChanged (and
                    // re-read CurrentUser when they load), so a session that lands after first paint
                    // is picked up normally.
                    //
                    // Queued at Low priority rather than started here: calling it inline only moves the
                    // work off the pre-Activate path and onto the UI thread's continuation queue, where
                    // it interleaves with MainPage's first layout and render. The window then appears
                    // promptly but stutters before it settles, which reads as a slower launch than
                    // blocking behind the splash screen did. Low runs it once the first frame is done.
                    if (isFirstNavigation)
                    {
                        var ignored = rootFrame.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low,
                                                                    RestoreSessionInBackground);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log critical startup error
                Debug.WriteLine($"Critical: OnLaunched failed - {ex.Message}");
                showStartupErrorDialog = true;
            }

            if (showStartupErrorDialog && Window.Current.Content != null)
            {
                try
                {
                    ContentDialog errorDialog = new ContentDialog();
                    errorDialog.Title = "Startup Error";
                    errorDialog.Content = "The application failed to start properly. Please try restarting.";
                    errorDialog.PrimaryButtonText = "OK";
                    AppConstants.ApplyXamlRoot(errorDialog, Window.Current.Content);
                    await errorDialog.ShowAsync();
                }
                catch
                {
                    // Nothing more we can do
                }
            }
        }

        /// <summary>
        /// Initializes storage and restores any saved session without blocking the first frame.
        /// async void, so every path is wrapped - an unhandled exception here would crash the app.
        /// </summary>
        private async void RestoreSessionInBackground()
        {
            try
            {
                await LocalStorageService.InitializeAsync();
                await ProfileService.TryRestoreSessionAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"App: background session restore failed - {ex.Message}");
            }
        }

        /// <summary>
        /// Invoked when the app is activated by something other than a normal launch - here, only
        /// the "fortind:" protocol, used as the MiAuth callback so the system browser can hand
        /// control back to the app once the user approves sign-in on fort.social.
        /// </summary>
        protected override async void OnActivated(Windows.ApplicationModel.Activation.IActivatedEventArgs args)
        {
            try
            {
                if (args.Kind != Windows.ApplicationModel.Activation.ActivationKind.Protocol) return;

                var protocolArgs = args as Windows.ApplicationModel.Activation.ProtocolActivatedEventArgs;
                if (protocolArgs == null) return;

                // Deliberately not logging the URI itself: its "session" parameter is the secret
                // that gets exchanged for an access token, and debug output is not a private sink.
                Debug.WriteLine($"OnActivated: protocol callback received for {protocolArgs.Uri.Scheme}://{protocolArgs.Uri.Host}");

                Frame rootFrame = Window.Current.Content as Frame;
                var isColdStart = rootFrame == null;

                if (isColdStart)
                {
                    rootFrame = new Frame();
                    rootFrame.NavigationFailed += OnNavigationFailed;
                    Window.Current.Content = rootFrame;
                    await LocalStorageService.InitializeAsync();
                    await ProfileService.TryRestoreSessionAsync();
                }

                var signInResult = await MisskeyAuthService.HandleProtocolActivationAsync(protocolArgs.Uri);
                if (signInResult != null && signInResult.Success)
                {
                    await ProfileService.ApplySignInResultAsync(signInResult);
                }

                if (isColdStart && rootFrame.Content == null)
                {
                    rootFrame.Navigate(typeof(MainPage));
                }

                Window.Current.Activate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnActivated failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        private async void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            // Log the error instead of crashing the app
            Debug.WriteLine($"Navigation failed: {e.SourcePageType.FullName} - {(e.Exception != null ? e.Exception.Message : "Unknown error")}");

            // Show user-friendly error dialog
            try
            {
                ContentDialog errorDialog = new ContentDialog();
                errorDialog.Title = "Navigation Error";
                errorDialog.Content = $"Failed to load page. The application will return to the home screen.";
                errorDialog.PrimaryButtonText = "OK";
                errorDialog.DefaultButton = ContentDialogButton.Primary;
                AppConstants.ApplyXamlRoot(errorDialog, Window.Current.Content);
                await errorDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                // If dialog fails, just log it
                Debug.WriteLine($"Error dialog failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Invoked when application execution is being suspended.  Application state is saved
        /// without knowing whether the application will be terminated or resumed with the contents
        /// of memory still intact.
        /// </summary>
        /// <param name="sender">The source of the suspend request.</param>
        /// <param name="e">Details about the suspend request.</param>
        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            SuspendingDeferral deferral = e.SuspendingOperation.GetDeferral();
            // TODO: Save application state and stop any background activity
            deferral.Complete();
        }

    }
}
