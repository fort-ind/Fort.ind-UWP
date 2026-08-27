using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Fort.ind_UWP
{
    /// <summary>
    /// The Settings panel: storage info and the destructive resets, the live tile controls, the
    /// expand/collapse state of each section, and the welcome dialog.
    /// </summary>
    public sealed partial class MainPage : Page
    {

        private void UpdateStorageInfo()
        {
            try
            {
                StoragePathText.Text = LocalizedStrings.Format("StorageLocationFormat", LocalStorageService.DataPath);

                var user = ProfileService.CurrentUser;
                if (user != null)
                {
                    var host = string.IsNullOrWhiteSpace(user.Host) ? MisskeyAuthService.InstanceHost : user.Host;
                    CacheDescriptionText.Text = LocalizedStrings.Get("StorageCacheSignedIn");
                    UserCountText.Text = LocalizedStrings.Format("StorageSignedInAsFormat", user.Username, host);
                    ClearLoginInfoButton.Visibility = Visibility.Visible;
                }
                else
                {
                    CacheDescriptionText.Text = LocalizedStrings.Get("StorageCacheSignedOut");
                    UserCountText.Text = "";
                    ClearLoginInfoButton.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: UpdateStorageInfo failed - {ex.Message}");
                StoragePathText.Text = "";
                CacheDescriptionText.Text = "";
                UserCountText.Text = "";
                ClearLoginInfoButton.Visibility = Visibility.Collapsed;
            }
        }

        private async void ClearLoginInfoButton_Click(object sender, RoutedEventArgs e)
        {
            var confirmed = await DialogService.ShowConfirmAsync(
                this,
                LocalizedStrings.Get("ClearLoginDialogTitle"),
                LocalizedStrings.Get("ClearLoginDialogBody"),
                LocalizedStrings.Get("ClearLoginDialogConfirm"),
                LocalizedStrings.Get("DialogCancel"),
                ContentDialogButton.Close);

            if (!confirmed) return;

            try
            {
                await ProfileService.LogoutAsync();
                UpdateStorageInfo();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Clear login info failed – {ex.Message}");
            }
        }

        /// <summary>
        /// Wipes all local app data. Destructive and irreversible, so it's gated behind two
        /// separate confirmations rather than one - the first explains what will happen, the
        /// second is a final "are you sure" with no way to back out afterward. Once the wipe is
        /// done, offers to restart the app immediately (via CoreApplication.RequestRestartAsync)
        /// since a handful of things - the appearance settings just re-read from LocalSettings
        /// during this same session, but anything cached only in memory elsewhere - are only
        /// guaranteed consistent after a fresh process start.
        /// </summary>
        private async void ResetAppButton_Click(object sender, RoutedEventArgs e)
        {
            // One RunExclusiveAsync for all three dialogs: the gate is not reentrant, so each of
            // them uses the *Core* overload that assumes the caller already holds it.
            await DialogService.RunExclusiveAsync(async () =>
            {
                var explained = await DialogService.ShowConfirmCoreAsync(
                    this,
                    LocalizedStrings.Get("ResetExplainDialogTitle"),
                    LocalizedStrings.Get("ResetExplainDialogBody"),
                    LocalizedStrings.Get("ResetExplainDialogConfirm"),
                    LocalizedStrings.Get("DialogCancel"),
                    ContentDialogButton.Close);

                if (!explained) return;

                var confirmed = await DialogService.ShowConfirmCoreAsync(
                    this,
                    LocalizedStrings.Get("ResetConfirmDialogTitle"),
                    LocalizedStrings.Get("ResetConfirmDialogBody"),
                    LocalizedStrings.Get("ResetConfirmDialogConfirm"),
                    LocalizedStrings.Get("DialogCancel"),
                    ContentDialogButton.Close);

                if (!confirmed) return;

                await ProfileService.ResetAppDataAsync();
                LoadAppearanceSettings();
                UpdateStorageInfo();

                var restartNow = await DialogService.ShowConfirmCoreAsync(
                    this,
                    LocalizedStrings.Get("ResetDoneDialogTitle"),
                    LocalizedStrings.Get("ResetDoneDialogBody"),
                    LocalizedStrings.Get("ResetDoneDialogRestart"),
                    LocalizedStrings.Get("ResetDoneDialogLater"),
                    ContentDialogButton.Primary);

                if (restartNow)
                {
                    await RequestAppRestartAsync();
                }
            });
        }

        /// <summary>
        /// Asks the OS to terminate and relaunch this app. On success the process is torn down
        /// before this call returns, so any code after the await here only runs if the restart
        /// could NOT be started (e.g. the app isn't in the foreground) - in which case we just
        /// leave the (already-reset) app running and let the user restart it manually.
        /// </summary>
        private async Task RequestAppRestartAsync()
        {
            try
            {
                var failureReason = await Windows.ApplicationModel.Core.CoreApplication.RequestRestartAsync("");
                Debug.WriteLine($"MainPage: App restart request did not restart the app - {failureReason}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: App restart request threw - {ex.Message}");
            }
        }

        private void RefreshTileButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateLiveTile();
        }

        private void ClearTileButton_Click(object sender, RoutedEventArgs e)
        {
            LiveTileService.ClearTile();
            LiveTileService.ClearBadge();
        }

        /// <summary>
        /// Turns the tile/taskbar badge on or off. The service clears an already-showing badge when
        /// this is switched off, so the change is visible without waiting for the next tile update.
        /// </summary>
        private void TileBadgeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            LiveTileService.BadgeEnabled = TileBadgeToggle.IsOn;
        }
        // ── Settings row expand/collapse ──

        private void StorageHeader_Tapped(object sender, RoutedEventArgs e)
        {
            ToggleSettingsRow(StorageContent, StorageChevronRotation, AppConstants.SettingSettingsStorageExpanded);
        }

        private void TileHeader_Tapped(object sender, RoutedEventArgs e)
        {
            ToggleSettingsRow(TileContent, TileChevronRotation, AppConstants.SettingSettingsTileExpanded);
        }

        private void WelcomeHeader_Tapped(object sender, RoutedEventArgs e)
        {
            ToggleSettingsRow(WelcomeContent, WelcomeChevronRotation, AppConstants.SettingSettingsWelcomeExpanded);
        }

        private void AboutHeader_Tapped(object sender, RoutedEventArgs e)
        {
            ToggleSettingsRow(AboutContent, AboutChevronRotation, AppConstants.SettingSettingsAboutExpanded);
        }

        /// <summary>
        /// Toggle settings row with state persistence
        /// </summary>
        private void ToggleSettingsRow(StackPanel content, RotateTransform chevronTransform, string settingKey = null)
        {
            var isExpanded = content.Visibility == Visibility.Collapsed;

            if (isExpanded)
            {
                content.Visibility = Visibility.Visible;
                chevronTransform.Angle = 90;
            }
            else
            {
                content.Visibility = Visibility.Collapsed;
                chevronTransform.Angle = 0;
            }

            // Save state if key is provided
            if (!string.IsNullOrEmpty(settingKey))
            {
                try
                {
                    ApplicationData.Current.LocalSettings.Values[settingKey] = isExpanded;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MainPage: Failed to save panel state - {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Restore settings panel expanded/collapsed states
        /// </summary>
        private void RestoreSettingsPanelStates()
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;

                // Restore each panel state
                RestorePanelState(AppConstants.SettingSettingsAppearanceExpanded, AppearanceContent, AppearanceChevronRotation);
                RestorePanelState(AppConstants.SettingSettingsStorageExpanded, StorageContent, StorageChevronRotation);
                RestorePanelState(AppConstants.SettingSettingsTileExpanded, TileContent, TileChevronRotation);
                RestorePanelState(AppConstants.SettingSettingsWelcomeExpanded, WelcomeContent, WelcomeChevronRotation);
                RestorePanelState(AppConstants.SettingSettingsAboutExpanded, AboutContent, AboutChevronRotation);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to restore panel states - {ex.Message}");
            }
        }

        /// <summary>
        /// Restore individual panel state
        /// </summary>
        private void RestorePanelState(string settingKey, StackPanel content, RotateTransform chevronTransform)
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values.ContainsKey(settingKey))
                {
                    var isExpanded = Convert.ToBoolean(localSettings.Values[settingKey]);
                    if (isExpanded)
                    {
                        content.Visibility = Visibility.Visible;
                        chevronTransform.Angle = 90;
                    }
                    else
                    {
                        content.Visibility = Visibility.Collapsed;
                        chevronTransform.Angle = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to restore {settingKey} - {ex.Message}");
            }
        }

        /// <summary>
        /// Shows the first-run welcome dialog, or does nothing if another dialog already holds
        /// the gate.
        /// </summary>
        private async Task ShowWelcomeDialogAsync()
        {
            await DialogService.RunExclusiveAsync(ShowWelcomeDialogCoreAsync);
        }

        /// <summary>
        /// The welcome dialog itself, without taking the dialog gate - the caller does that, so
        /// this can also be used inside a RunExclusiveAsync body without deadlocking on a
        /// semaphore that is not reentrant.
        /// </summary>
        private async Task ShowWelcomeDialogCoreAsync()
        {
            // Content comes from markup (WelcomeDialogContentTemplate in MainPage.xaml) but
            // the dialog itself is built fresh each time - see the note on the template for
            // why a reused ContentDialog instance stops animating on its second showing.
            var contentTemplate = Resources["WelcomeDialogContentTemplate"] as DataTemplate;
            if (contentTemplate == null) return;

            var dialogContent = contentTemplate.LoadContent() as FrameworkElement;
            if (dialogContent == null) return;

            // x:Name inside a DataTemplate is not a page field; it is resolved against the
            // stamped copy's own namescope.
            var dontShowCheckBox = dialogContent.FindName("WelcomeDontShowCheckBox") as CheckBox;

            ContentDialog welcomeDialog = new ContentDialog();
            welcomeDialog.Title = LocalizedStrings.Get("WelcomeDialogTitle");
            welcomeDialog.Content = dialogContent;
            welcomeDialog.CloseButtonText = LocalizedStrings.Get("WelcomeDialogDismiss");
            welcomeDialog.DefaultButton = ContentDialogButton.Close;
            DialogService.ApplyXamlRoot(welcomeDialog, this);

            await welcomeDialog.ShowAsync();

            if (dontShowCheckBox != null && dontShowCheckBox.IsChecked.GetValueOrDefault(false))
            {
                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingHideWelcomeDialog] = true;
            }
        }

        /// <summary>
        /// Clears the "don't show again" flag and re-shows the welcome dialog. The flag is only
        /// flipped once the dialog actually opens, so a click that lands while another dialog is
        /// up changes nothing rather than silently un-hiding the dialog for next launch.
        /// </summary>
        private async void ResetWelcomeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Clearing the flag happens *inside* the gated body, so a click that lands while
                // another dialog is open leaves the setting exactly as it was rather than
                // un-hiding the dialog for the next launch without ever showing it.
                await DialogService.RunExclusiveAsync(async () =>
                {
                    ApplicationData.Current.LocalSettings.Values[AppConstants.SettingHideWelcomeDialog] = false;
                    await ShowWelcomeDialogCoreAsync();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Reset welcome failed – {ex.Message}");
            }
        }

    }
}
