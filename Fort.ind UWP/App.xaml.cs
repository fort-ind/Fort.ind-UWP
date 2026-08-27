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
        /// True when Windows terminated the app last time (to reclaim memory, typically) rather
        /// than the user closing it. Only in that case is the app expected to put the user back
        /// where they were; after a deliberate close, coming up on Home is the correct behaviour.
        /// MainPage reads this once, when its NavigationView loads.
        /// </summary>
        public static bool ResumingFromTermination { get; private set; }

        /// <summary>
        /// Navigation tag from a jump list task that launched the app cold, held until MainPage's
        /// NavigationView loads and can act on it. Same reasoning as ResumingFromTermination: the
        /// state belongs to MainPage, which does not exist yet at the point OnLaunched sees it.
        /// </summary>
        private static string s_pendingLaunchNavTag;

        /// <summary>
        /// Reads the pending jump list tag and clears it, so it is acted on exactly once. Without
        /// the clear, a MainPage rebuilt later in the same process would replay the tag and pull
        /// the user off whatever they had navigated to since.
        /// </summary>
        internal static string TakePendingLaunchNavTag()
        {
            var tag = s_pendingLaunchNavTag;
            s_pendingLaunchNavTag = null;
            return tag;
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

                    // Recorded rather than acted on here: the state that needs restoring belongs
                    // to MainPage (which nav item was open), and MainPage does not exist yet.
                    ResumingFromTermination = e.PreviousExecutionState == ApplicationExecutionState.Terminated;

                    ApplySavedTheme(rootFrame);

                    // Place the frame in the current Window
                    Window.Current.Content = rootFrame;
                }

                if (!e.PrelaunchActivated)
                {
                    var isFirstNavigation = rootFrame.Content == null;

                    // A jump list task launches the app with the argument string it was created
                    // with, and Windows raises OnLaunched for it whether or not the app was
                    // already running - so both paths have to be handled, and the warm one is
                    // easy to miss because it previously fell through to nothing but Activate().
                    // Anything unrecognised resolves to null, which is also what a plain launch
                    // produces, so the no-argument case needs no separate branch.
                    var jumpNavTag = JumpListService.ResolveNavTag(e.Arguments);

                    if (isFirstNavigation)
                    {
                        // Cold start: MainPage's NavigationView has not loaded, so there is
                        // nothing to drive yet. Handed over for ResolveStartupNavTag to pick up.
                        s_pendingLaunchNavTag = jumpNavTag;

                        // Navigate to MainPage (it will handle the profile state)
                        rootFrame.Navigate(typeof(MainPage), e.Arguments);
                    }
                    else if (jumpNavTag != null)
                    {
                        // Warm start: the shell is the root Frame's content, and the page-backed
                        // views live in a Frame inside it, so this cast is the live shell.
                        var mainPage = rootFrame.Content as MainPage;
                        if (mainPage != null)
                        {
                            mainPage.NavigateToTag(jumpNavTag);
                        }
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

            if (showStartupErrorDialog)
            {
                // DialogService swallows its own failures, but this is an async void: reading
                // Window.Current here is outside its reach, and an exception escaping this method
                // takes the process down.
                try
                {
                    if (Window.Current.Content != null)
                    {
                        await DialogService.ShowMessageAsync(Window.Current.Content,
                                                             LocalizedStrings.Get("StartupErrorDialogTitle"),
                                                             LocalizedStrings.Get("StartupErrorDialogBody"),
                                                             LocalizedStrings.Get("DialogOk"));
                    }
                }
                catch
                {
                    // Nothing more we can do
                }
            }
        }

        /// <summary>
        /// Applies the user's saved Light/Dark preference to a freshly created root Frame, before
        /// it has anything in it, so the first frame is painted in the right theme instead of
        /// flashing the system default and correcting itself.
        ///
        /// Called from both entry points that can create the root Frame. OnActivated is a real
        /// cold-start path, not just a resume: the MiAuth callback can arrive after the app was
        /// terminated while the user was approving sign-in in the browser, in which case
        /// OnActivated - not OnLaunched - is what builds the window. Applying this in only one of
        /// the two meant signing in that way brought the app up ignoring the saved theme.
        /// </summary>
        private static void ApplySavedTheme(Frame rootFrame)
        {
            if (rootFrame == null) return;

            try
            {
                var savedTheme = Windows.Storage.ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTheme]?.ToString();
                switch (savedTheme)
                {
                    case AppConstants.ThemeLight: rootFrame.RequestedTheme = ElementTheme.Light; break;
                    case AppConstants.ThemeDark: rootFrame.RequestedTheme = ElementTheme.Dark; break;
                    default: rootFrame.RequestedTheme = ElementTheme.Default; break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"App: Failed to apply saved theme - {ex.Message}");
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

                // Jump list publishing rides along here for the same reason the session restore
                // does: SaveAsync is cross-process shell work, and it has nothing to do with
                // getting the first frame on screen. It no-ops entirely once the list is current.
                await JumpListService.EnsureTasksAsync();
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
                    ApplySavedTheme(rootFrame);
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
            // async void: everything is wrapped, because an exception escaping here crashes the app.
            try
            {
                // Log the error instead of crashing the app
                Debug.WriteLine($"Navigation failed: {e.SourcePageType.FullName} - {(e.Exception != null ? e.Exception.Message : "Unknown error")}");

                // Show user-friendly error dialog
                await DialogService.ShowMessageAsync(Window.Current.Content,
                                                     LocalizedStrings.Get("NavigationErrorDialogTitle"),
                                                     LocalizedStrings.Get("NavigationErrorDialogBody"),
                                                     LocalizedStrings.Get("DialogOk"));
            }
            catch (Exception ex)
            {
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

            // Nothing to write here, and that is deliberate rather than unfinished. Every piece
            // of state this app restores - the theme, the tint, which settings sections are
            // expanded, and now the open nav item - is committed to LocalSettings at the moment
            // the user changes it, not batched until suspend. That is the more robust ordering:
            // suspend handlers run under a short deadline and are not guaranteed to complete, so
            // state saved only here is exactly the state most likely to be lost.
            //
            // There is also no background activity to stop: the sitemap load and avatar fetches
            // are one-shot awaits tied to page lifetime, and the live tile is pushed to the shell
            // rather than kept alive in-process.
            deferral.Complete();
        }

    }
}
