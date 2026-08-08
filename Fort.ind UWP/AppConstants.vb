Imports Windows.Foundation.Metadata
Imports Windows.UI.Xaml
Imports Windows.UI.Xaml.Controls

''' <summary>
''' Centralized app constants to avoid repeated literals and drift.
''' </summary>
Public NotInheritable Class AppConstants

    Private Sub New()
    End Sub

    ''' <summary>
    ''' UIElement.XamlRoot was added in Windows 10 1903 (10.0.18362.0). This app's
    ''' TargetPlatformMinVersion is 1809 (10.0.17763.0), where the property doesn't exist -
    ''' reading or setting it throws, which every ContentDialog call site swallows in a
    ''' try/catch, so on 1809 dialogs silently never appear. A single-window UWP app shows
    ''' ContentDialogs fine with XamlRoot left unset, so just skip it when unsupported.
    ''' </summary>
    Private Shared ReadOnly s_xamlRootSupported As Boolean =
        ApiInformation.IsPropertyPresent("Windows.UI.Xaml.UIElement", "XamlRoot")

    Public Shared Sub ApplyXamlRoot(dialog As ContentDialog, owner As UIElement)
        If s_xamlRootSupported Then
            dialog.XamlRoot = owner.XamlRoot
        End If
    End Sub

    ''' <summary>
    ''' Parses a string into a web URI, accepting only http/https.
    '''
    ''' Every URL the app launches originates outside the code: the bundled sitemap, the
    ''' plain-text URL cache in LocalFolder, or profile JSON returned by the instance. Handing
    ''' any of those straight to Launcher.LaunchUriAsync means one click can invoke *any*
    ''' registered protocol on the machine - "ms-settings:", "file:", "shell:", or another
    ''' installed app's custom scheme - because Uri.TryCreate(..., Absolute) happily accepts
    ''' all of them. A browsable link is the only thing any of these call sites ever intends,
    ''' so anything else is rejected here rather than at each call site.
    ''' Returns Nothing if the value is not a well-formed http/https URL.
    ''' </summary>
    Public Shared Function TryCreateWebUri(value As String) As Uri
        If String.IsNullOrWhiteSpace(value) Then Return Nothing

        Dim uri As Uri = Nothing
        If Not Uri.TryCreate(value, UriKind.Absolute, uri) Then Return Nothing

        If Not String.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) AndAlso
           Not String.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) Then
            Return Nothing
        End If

        If String.IsNullOrEmpty(uri.Host) Then Return Nothing

        Return uri
    End Function

    ''' <summary>
    ''' Opens a URL in the user's browser, but only if it is a well-formed http/https URL.
    ''' See <see cref="TryCreateWebUri"/> for why the scheme is checked. Returns False (and
    ''' launches nothing) for anything else.
    ''' </summary>
    Public Shared Async Function LaunchWebUriAsync(value As String) As Task(Of Boolean)
        Dim uri = TryCreateWebUri(value)
        If uri Is Nothing Then
            Debug.WriteLine($"AppConstants: refused to launch non-web URI - {value}")
            Return False
        End If

        Return Await Windows.System.Launcher.LaunchUriAsync(uri)
    End Function

    ' Search categories
    Public Const CategoryMenu As String = "Menu"
    Public Const CategorySettings As String = "Settings"
    Public Const CategoryProfile As String = "Profile"
    Public Const CategoryGames As String = "Games"
    Public Const CategorySocial As String = "Social"
    Public Const CategoryEmulators As String = "Emulators"
    Public Const CategoryApps As String = "Apps"
    Public Const CategoryExtras As String = "Extras"
    Public Const CategoryLabsAndBetas As String = "Labs & Betas"
    Public Const CategoryFortWebsite As String = "fort1nd.com"

    ' Navigation tags
    Public Const NavigationLatestNews As String = "LatestNews"
    Public Const NavigationGames As String = "Games"
    Public Const NavigationBetas As String = "Betas"
    Public Const NavigationProfile As String = "Profile"
    Public Const NavigationSocial As String = "Social"
    Public Const NavigationSettings As String = "Settings"

    ' Theme values
    Public Const ThemeDefault As String = "Default"
    Public Const ThemeLight As String = "Light"
    Public Const ThemeDark As String = "Dark"

    ' LocalSettings keys
    Public Const SettingHideWelcomeDialog As String = "HideWelcomeDialog"
    Public Const SettingAppTheme As String = "AppTheme"
    Public Const SettingAppTintColor As String = "AppTintColor"
    ' Remembers the last color picked in the custom-tint dialog so the custom swatch keeps
    ' showing it even while a preset is the active tint.
    Public Const SettingAppCustomTintColor As String = "AppCustomTintColor"
    Public Const SettingSettingsAppearanceExpanded As String = "SettingsAppearanceExpanded"
    Public Const SettingSettingsStorageExpanded As String = "SettingsStorageExpanded"
    Public Const SettingSettingsTileExpanded As String = "SettingsTileExpanded"
    ' Whether the app may show a badge on its tile / taskbar icon. Absent means "on".
    Public Const SettingShowTileBadge As String = "ShowTileBadge"
    Public Const SettingSettingsWelcomeExpanded As String = "SettingsWelcomeExpanded"
    Public Const SettingSettingsAboutExpanded As String = "SettingsAboutExpanded"

    ' Search behavior
    Public Const SearchDebounceMilliseconds As Integer = 300
    Public Const SearchSuggestionLimit As Integer = 15

    ' Sitemap cache
    Public Const SitemapCacheFileName As String = "sitemap_urls.cache"
    Public Const SitemapCacheTimestampKey As String = "SitemapCacheUnixSeconds"
    Public Const SitemapCacheAppVersionKey As String = "SitemapCacheAppVersion"
    Public Const SitemapCacheTtlHours As Integer = 24

    ' Release channel suffix appended after the numeric version (e.g. "0.5.0 Beta")
    Public Const VersionChannel As String = " "

    ''' <summary>
    ''' The app version pulled from the package manifest, formatted as "Major.Minor.Build".
    ''' Falls back to a static string if the package identity is unavailable (e.g. unpackaged).
    ''' Single source of truth so the About screen never drifts from the manifest.
    '''
    ''' Resolved once and cached: Package.Current is a cross-process call, and this is read on
    ''' the startup path (twice per sitemap cache check, plus the About row).
    ''' </summary>
    Public Shared ReadOnly Property AppVersionDisplay As String
        Get
            Return s_appVersionDisplay
        End Get
    End Property

    Private Shared ReadOnly s_appVersionDisplay As String = ResolveAppVersionDisplay()

    Private Shared Function ResolveAppVersionDisplay() As String
        Try
            Dim v = Windows.ApplicationModel.Package.Current.Id.Version
            Return $"{v.Major}.{v.Minor}.{v.Build} {VersionChannel}"
        Catch
            Return $"2.0.10 {VersionChannel}"
        End Try
    End Function

End Class