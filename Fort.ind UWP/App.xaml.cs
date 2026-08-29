using System;
using System.Diagnostics;
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
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnActivated failed: {ex.Message}");
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

            deferral.Complete();
        }
    }
}
