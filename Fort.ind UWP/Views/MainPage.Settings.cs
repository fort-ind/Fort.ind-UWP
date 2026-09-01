using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Fort.ind_UWP
{
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

        private async void ResetAppButton_Click(object sender, RoutedEventArgs e)
        {
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

        private void TileBadgeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            LiveTileService.BadgeEnabled = TileBadgeToggle.IsOn;
        }

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

        private void RestoreSettingsPanelStates()
        {
            try
            {
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

        private void RestorePanelState(string settingKey, StackPanel content, RotateTransform chevronTransform)
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;

                // An absent key means "never set, or just wiped by a reset", and it has to apply
                // the collapsed default rather than return. Returning left the section in whatever
                // state it already had, so a reset - which clears LocalSettings wholesale - said it
                // had restored defaults while every section stayed exactly as the user left it.
                var isExpanded = false;
                if (localSettings.Values.ContainsKey(settingKey))
                {
                    isExpanded = Convert.ToBoolean(localSettings.Values[settingKey]);
                }

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
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to restore {settingKey} - {ex.Message}");
            }
        }

        private async Task ShowWelcomeDialogAsync()
        {
            await DialogService.RunExclusiveAsync(ShowWelcomeDialogCoreAsync);
        }

        private async Task ShowWelcomeDialogCoreAsync()
        {
            var contentTemplate = Resources["WelcomeDialogContentTemplate"] as DataTemplate;
            if (contentTemplate == null) return;

            var dialogContent = contentTemplate.LoadContent() as FrameworkElement;
            if (dialogContent == null) return;

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

        private async void ResetWelcomeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
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
