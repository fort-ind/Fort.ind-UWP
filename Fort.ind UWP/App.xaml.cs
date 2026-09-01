using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    sealed partial class App : Application
    {
        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;
        }

        public static bool ResumingFromTermination { get; private set; }

        private static string s_pendingLaunchNavTag;

        internal static string TakePendingLaunchNavTag()
        {
            var tag = s_pendingLaunchNavTag;
            s_pendingLaunchNavTag = null;
            return tag;
        }

        protected override async void OnLaunched(Windows.ApplicationModel.Activation.LaunchActivatedEventArgs e)
        {
            bool showStartupErrorDialog = false;
            try
            {
                Frame rootFrame = Window.Current.Content as Frame;

                if (rootFrame == null)
                {
                    rootFrame = new Frame();

                    rootFrame.NavigationFailed += OnNavigationFailed;

                    ResumingFromTermination = e.PreviousExecutionState == ApplicationExecutionState.Terminated;

                    ApplySavedTheme(rootFrame);

                    Window.Current.Content = rootFrame;
                }

                if (!e.PrelaunchActivated)
                {
                    var isFirstNavigation = rootFrame.Content == null;

                    var jumpNavTag = JumpListService.ResolveNavTag(e.Arguments);

                    if (isFirstNavigation)
                    {
                        s_pendingLaunchNavTag = jumpNavTag;

                        rootFrame.Navigate(typeof(MainPage), e.Arguments);
                    }
                    else if (jumpNavTag != null)
                    {
                        var mainPage = rootFrame.Content as MainPage;
                        if (mainPage != null)
                        {
                            mainPage.NavigateToTag(jumpNavTag);
                        }
                    }

                    Window.Current.Activate();

                    if (isFirstNavigation)
                    {
                        var ignored = rootFrame.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low,
                                                                    RestoreSessionInBackground);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Critical: OnLaunched failed - {ex.Message}");
                showStartupErrorDialog = true;
            }

            if (showStartupErrorDialog)
            {
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
                }
            }
        }

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

        private async void RestoreSessionInBackground()
        {
            try
            {
                await LocalStorageService.InitializeAsync();
                await ProfileService.TryRestoreSessionAsync();

                await JumpListService.EnsureTasksAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"App: background session restore failed - {ex.Message}");
            }
        }

        protected override async void OnActivated(Windows.ApplicationModel.Activation.IActivatedEventArgs args)
        {
            try
            {
                if (args.Kind != Windows.ApplicationModel.Activation.ActivationKind.Protocol) return;

                var protocolArgs = args as Windows.ApplicationModel.Activation.ProtocolActivatedEventArgs;
                if (protocolArgs == null) return;

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

                if (signInResult != null && !signInResult.Success)
                {
                    // A null result is the warm path handing control back to the SignInAsync still
                    // being awaited on LoginPage, which reports its own errors. A non-null failure
                    // has nobody waiting on it - it is the cold-start path, where the app was
                    // terminated while the user was in the browser - so without this the window
                    // just opens signed out and never says why.
                    await ShowSignInFailedAsync(signInResult.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnActivated failed: {ex.Message}");
            }
        }

        private static async Task ShowSignInFailedAsync(string reason)
        {
            try
            {
                var body = string.IsNullOrWhiteSpace(reason)
                           ? LocalizedStrings.Get("SignInFailedDialogBody")
                           : LocalizedStrings.Format("SignInFailedDialogBodyFormat", reason);

                await DialogService.ShowMessageAsync(Window.Current.Content,
                                                     LocalizedStrings.Get("SignInFailedDialogTitle"),
                                                     body,
                                                     LocalizedStrings.Get("DialogOk"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"App: could not report the sign-in failure - {ex.Message}");
            }
        }

        private async void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            try
            {
                Debug.WriteLine($"Navigation failed: {e.SourcePageType.FullName} - {(e.Exception != null ? e.Exception.Message : "Unknown error")}");

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

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            SuspendingDeferral deferral = e.SuspendingOperation.GetDeferral();

            try
            {
                // The badge marks tile content the user has not come back to yet, so it goes on
                // the way out and MainPage's NavView_Loaded clears it on the way in. Setting it at
                // launch instead raced that clear and lost: the tile push is deferred to
                // CoreDispatcherPriority.Low, so it ran last and relit the badge every time.
                //
                // Safe to do under the suspend deadline in a way that saved state would not be:
                // this is one synchronous, self-guarding, best-effort call, and losing it costs
                // nothing but a missing badge. Nothing that must survive termination lives here -
                // that is all still written eagerly at the moment it changes.
                LiveTileService.UpdateBadgeGlyph(LiveTileService.NewContentBadgeGlyph);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"App: suspend badge update failed - {ex.Message}");
            }

            deferral.Complete();
        }
    }
}
