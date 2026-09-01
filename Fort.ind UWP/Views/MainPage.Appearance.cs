using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Fort.ind_UWP
{
    public sealed partial class MainPage : Page
    {
        private void LoadAppearanceSettings()
        {
            _loadingSettings = true;
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;

                // ?? on top of ContainsKey: the key can be present with a null value, and ToString()
                // on that throws - out of the MainPage constructor, which fails the Navigate that
                // created the page and takes the app down through OnNavigationFailed.
                string theme = AppConstants.ThemeDefault;
                if (localSettings.Values.ContainsKey(AppConstants.SettingAppTheme))
                {
                    theme = localSettings.Values[AppConstants.SettingAppTheme]?.ToString() ?? AppConstants.ThemeDefault;
                }
                switch (theme)
                {
                    case AppConstants.ThemeLight: ThemeLightRadio.IsChecked = true; break;
                    case AppConstants.ThemeDark: ThemeDarkRadio.IsChecked = true; break;
                    default: ThemeSystemRadio.IsChecked = true; break;
                }
                ApplyTheme(theme);

                string tintTag = AppConstants.ThemeDefault;
                if (localSettings.Values.ContainsKey(AppConstants.SettingAppTintColor))
                {
                    tintTag = localSettings.Values[AppConstants.SettingAppTintColor]?.ToString() ?? AppConstants.ThemeDefault;
                }
                TintCustomButton.ClearValue(Control.BackgroundProperty);
                TintCustomIcon.Visibility = Visibility.Visible;

                ApplyTintColor(tintTag);
                UpdateTintSelection(tintTag);

                var rememberedCustom = localSettings.Values[AppConstants.SettingAppCustomTintColor] as string;
                if (TintCustomIcon.Visibility == Visibility.Visible && !string.IsNullOrEmpty(rememberedCustom))
                {
                    ShowCustomSwatchColor(rememberedCustom);
                }

                TileBadgeToggle.IsOn = LiveTileService.BadgeEnabled;

                RestoreSettingsPanelStates();
            }
            catch (Exception ex)
            {
                // This runs from the MainPage constructor, where an escaping exception fails the
                // Navigate that created the page and takes the whole app down through
                // OnNavigationFailed - a settings value that will not read is not worth that. The
                // app comes up with whatever appearance was already applied instead.
                Debug.WriteLine($"MainPage: LoadAppearanceSettings failed - {ex.Message}");
            }
            finally
            {
                _loadingSettings = false;
            }
        }

        private void ApplyTheme(string theme)
        {
            var rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null) return;
            switch (theme)
            {
                case AppConstants.ThemeLight: rootFrame.RequestedTheme = ElementTheme.Light; break;
                case AppConstants.ThemeDark: rootFrame.RequestedTheme = ElementTheme.Dark; break;
                default: rootFrame.RequestedTheme = ElementTheme.Default; break;
            }
            if (!_loadingSettings)
            {
                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTheme] = theme;
            }
            UpdateTitleBarColors();
            if (!_loadingSettings)
            {
                var savedTint = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTintColor]?.ToString();
                if (string.IsNullOrEmpty(savedTint)) savedTint = AppConstants.ThemeDefault;
                ApplyTintColor(savedTint);
                UpdateTintSelection(savedTint);
            }
        }

        private void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            try
            {
                UpdateTitleBarColors();
                var savedTint = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTintColor]?.ToString();
                if (string.IsNullOrEmpty(savedTint)) savedTint = AppConstants.ThemeDefault;
                ApplyTintColor(savedTint);
                UpdateTintSelection(savedTint);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: OnActualThemeChanged failed – {ex.Message}");
            }
        }

        private AcrylicBrush _surfaceBrush;

        private static readonly Color s_surfaceTintDark = Color.FromArgb(255, 0x2B, 0x2B, 0x2B);
        private static readonly Color s_surfaceTintLight = Colors.White;
        private static readonly Color s_surfaceFallbackLight = Color.FromArgb(255, 0xF2, 0xF2, 0xF2);

        private void ApplyTintColor(string colorTag)
        {
            // Normalise an unusable tag up front, before anything can persist it. The catch below
            // used to swallow the parse failure and the write at the bottom then stored the bad tag
            // anyway - so one corrupt value made every subsequent launch fail in exactly the same
            // way, silently, with the window coming up untinted and no way to notice why.
            if (!IsUsableTintTag(colorTag))
            {
                Debug.WriteLine($"MainPage: tint tag '{colorTag}' is not a colour; using the default surface");
                colorTag = AppConstants.ThemeDefault;
            }

            try
            {
                var isDark = IsEffectiveThemeDark();

                Color tint;
                Color fallback;
                double tintOpacity;

                if (string.IsNullOrEmpty(colorTag) || colorTag == AppConstants.ThemeDefault)
                {
                    tint = isDark ? s_surfaceTintDark : s_surfaceTintLight;
                    fallback = isDark ? s_surfaceTintDark : s_surfaceFallbackLight;
                    tintOpacity = 0.8;
                }
                else
                {
                    tint = isDark ? ColorHelper.HexToColor(colorTag) : ColorHelper.ForLightTheme(colorTag);
                    fallback = tint;
                    tintOpacity = isDark ? 0.8 : 0.85;
                }

                if (_surfaceBrush == null)
                {
                    _surfaceBrush = new AcrylicBrush()
                    {
                        BackgroundSource = AcrylicBackgroundSource.HostBackdrop
                    };
                }
                _surfaceBrush.TintColor = tint;
                _surfaceBrush.TintOpacity = tintOpacity;
                _surfaceBrush.FallbackColor = fallback;

                if (!ReferenceEquals(RootGrid.Background, _surfaceBrush))
                {
                    RootGrid.Background = _surfaceBrush;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: ApplyTintColor failed – {ex.Message}");
            }

            if (!_loadingSettings)
            {
                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTintColor] = colorTag;
            }
        }

        /// <summary>
        /// True for the sentinel "Default" and for any tag that really parses as a colour.
        /// </summary>
        private static bool IsUsableTintTag(string colorTag)
        {
            if (string.IsNullOrEmpty(colorTag) || colorTag == AppConstants.ThemeDefault) return true;

            Color ignored;
            return ColorHelper.TryHexToColor(colorTag, out ignored);
        }

        private static bool IsEffectiveThemeDark()
        {
            var rootFrame = Window.Current.Content as Frame;
            var effTheme = rootFrame != null ? rootFrame.RequestedTheme : ElementTheme.Default;
            return effTheme == ElementTheme.Default
                   ? Application.Current.RequestedTheme == ApplicationTheme.Dark
                   : effTheme == ElementTheme.Dark;
        }

        private Dictionary<Button, string> _swatchBaseNames;

        private string BaseSwatchName(Button btn)
        {
            if (_swatchBaseNames == null)
            {
                _swatchBaseNames = new Dictionary<Button, string>();
            }

            string name;
            if (!_swatchBaseNames.TryGetValue(btn, out name))
            {
                name = Windows.UI.Xaml.Automation.AutomationProperties.GetName(btn) ?? "";
                _swatchBaseNames[btn] = name;
            }

            return name;
        }

        private Button[] _tintPresetSwatches;

        private Button[] TintPresetSwatches
        {
            get
            {
                if (_tintPresetSwatches == null)
                {
                    _tintPresetSwatches = new Button[] { TintDefaultButton, TintBlueButton, TintPurpleButton, TintGreenButton,
                                                         TintRedButton, TintSlateButton, TintTealButton, TintBronzeButton,
                                                         TintRoseButton, TintOliveButton, TintGraphiteButton };
                }
                return _tintPresetSwatches;
            }
        }

        private static readonly SolidColorBrush s_restBrushDark = new SolidColorBrush(Colors.Transparent);
        private static readonly SolidColorBrush s_restBrushLight = new SolidColorBrush(Color.FromArgb(0x22, 0, 0, 0));

        private static readonly SolidColorBrush s_selectedBrushDark = new SolidColorBrush(Colors.White);
        private static readonly SolidColorBrush s_selectedBrushLight = new SolidColorBrush(Colors.Black);

        private Dictionary<Button, Brush> _swatchChipsDark;
        private Dictionary<Button, Brush> _swatchChipsLight;

        private void UpdateSwatchChipColors(bool isDark)
        {
            if (_swatchChipsDark == null)
            {
                _swatchChipsDark = new Dictionary<Button, Brush>();
                _swatchChipsLight = new Dictionary<Button, Brush>();
                foreach (var btn in TintPresetSwatches)
                {
                    var tag = btn.Tag?.ToString() ?? "";
                    var lightHex = ColorHelper.TryGetLightPreset(tag);
                    if (lightHex == null) continue;
                    _swatchChipsDark[btn] = btn.Background;
                    _swatchChipsLight[btn] = new SolidColorBrush(ColorHelper.HexToColor(lightHex));
                }
            }

            var chips = isDark ? _swatchChipsDark : _swatchChipsLight;
            foreach (var pair in chips)
            {
                pair.Key.Background = pair.Value;
            }
        }

        private void UpdateTintSelection(string selectedTag)
        {
            selectedTag = string.IsNullOrEmpty(selectedTag) ? AppConstants.ThemeDefault : selectedTag;

            var isDark = IsEffectiveThemeDark();
            var restBrush = isDark ? s_restBrushDark : s_restBrushLight;
            UpdateSwatchChipColors(isDark);

            Button sel = null;
            foreach (var btn in TintPresetSwatches)
            {
                btn.BorderBrush = restBrush;
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(btn, BaseSwatchName(btn));
                var tag = btn.Tag?.ToString() ?? "";
                if (string.Equals(tag, selectedTag, StringComparison.OrdinalIgnoreCase)) sel = btn;
            }

            TintCustomButton.BorderBrush = restBrush;
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(TintCustomButton, BaseSwatchName(TintCustomButton));
            if (sel == null)
            {
                sel = TintCustomButton;
                ShowCustomSwatchColor(selectedTag);
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(
                    TintCustomButton,
                    LocalizedStrings.Format("TintCustomSwatchWithColorFormat",
                                            BaseSwatchName(TintCustomButton), selectedTag));
            }
            else if (TintCustomIcon.Visibility == Visibility.Collapsed)
            {
                var remembered = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppCustomTintColor] as string;
                if (!string.IsNullOrEmpty(remembered))
                {
                    ShowCustomSwatchColor(remembered);
                }
            }

            if (sel != null)
            {
                sel.BorderBrush = isDark ? s_selectedBrushDark : s_selectedBrushLight;
                var selBaseName = Windows.UI.Xaml.Automation.AutomationProperties.GetName(sel);
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(
                    sel, LocalizedStrings.Format("TintSwatchSelectedSuffixFormat", selBaseName));
            }
        }

        private void ShowCustomSwatchColor(string hex)
        {
            try
            {
                Color parsed;
                if (!ColorHelper.TryHexToColor(hex, out parsed))
                {
                    // Leave the swatch showing its "pick a colour" glyph rather than painting it
                    // with something arbitrary.
                    Debug.WriteLine($"MainPage: custom swatch colour '{hex}' is not a colour");
                    return;
                }

                var c = IsEffectiveThemeDark() ? parsed : ColorHelper.LightenForLightTheme(parsed);
                TintCustomButton.Background = new SolidColorBrush(c);
                TintCustomIcon.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: ShowCustomSwatchColor failed – {ex.Message}");
            }
        }

        private void AppearanceHeader_Tapped(object sender, RoutedEventArgs e)
        {
            ToggleSettingsRow(AppearanceContent, AppearanceChevronRotation, AppConstants.SettingSettingsAppearanceExpanded);
        }

        private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            var radio = sender as RadioButton;
            if (radio != null)
            {
                ApplyTheme(radio.Tag.ToString());
            }
        }

        private void TintColorButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null)
            {
                var tag = btn.Tag?.ToString() ?? "Default";
                ApplyTintColor(tag);
                UpdateTintSelection(tag);
            }
        }

        private async void CustomTintButton_Click(object sender, RoutedEventArgs e)
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            string previousTag = localSettings.Values[AppConstants.SettingAppTintColor]?.ToString()
                                 ?? AppConstants.ThemeDefault;

            await DialogService.RunExclusiveAsync(async () =>
            {
                try
                {
                    string seed = previousTag;
                    if (seed == AppConstants.ThemeDefault || ColorHelper.TryGetLightPreset(seed) != null)
                    {
                        seed = localSettings.Values[AppConstants.SettingAppCustomTintColor]?.ToString() ?? "#1E3A5F";
                    }

                    Color seedColor;
                    if (!ColorHelper.TryHexToColor(seed, out seedColor))
                    {
                        seedColor = ColorHelper.HexToColor("#1E3A5F");
                    }

                    ColorPicker picker = new ColorPicker()
                    {
                        IsAlphaEnabled = false,
                        IsHexInputVisible = true,
                        IsColorChannelTextInputVisible = true,
                        Color = seedColor
                    };

                    ContentDialog dialog = new ContentDialog()
                    {
                        Title = LocalizedStrings.Get("CustomTintDialogTitle"),
                        Content = picker,
                        PrimaryButtonText = LocalizedStrings.Get("CustomTintDialogApply"),
                        CloseButtonText = LocalizedStrings.Get("DialogCancel"),
                        DefaultButton = ContentDialogButton.Primary
                    };
                    DialogService.ApplyXamlRoot(dialog, this);

                    TypedEventHandler<ColorPicker, ColorChangedEventArgs> previewHandler =
                        (s, args) => ApplyTintColorPreview(ColorHelper.ColorToHex(args.NewColor));
                    picker.ColorChanged += previewHandler;

                    var result = await dialog.ShowAsync();
                    picker.ColorChanged -= previewHandler;

                    if (result == ContentDialogResult.Primary)
                    {
                        var hex = ColorHelper.ColorToHex(picker.Color);
                        localSettings.Values[AppConstants.SettingAppCustomTintColor] = hex;
                        ApplyTintColor(hex);
                        UpdateTintSelection(hex);
                    }
                    else
                    {
                        ApplyTintColor(previousTag);
                        UpdateTintSelection(previousTag);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MainPage: Custom tint dialog failed – {ex.Message}");
                    ApplyTintColor(previousTag);
                    UpdateTintSelection(previousTag);
                }
            });
        }

        private void ApplyTintColorPreview(string hex)
        {
            var wasLoading = _loadingSettings;
            _loadingSettings = true;
            try
            {
                ApplyTintColor(hex);
            }
            finally
            {
                _loadingSettings = wasLoading;
            }
        }
    }
}
