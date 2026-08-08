Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Text
Imports Windows.UI.Notifications
Imports Windows.Data.Xml.Dom
Imports Microsoft.Toolkit.Uwp.Notifications

''' <summary>
''' Service for managing Live Tile updates with news and notifications
''' </summary>
Public Class LiveTileService

    ''' <summary>
    ''' Updates the Live Tile with the latest news
    ''' </summary>
    Public Shared Sub UpdateTileWithNews(title As String, message As String, Optional branding As String = "name", Optional animationType As TileAnimation = TileAnimation.FadeIn)
        Try
            ' Create the tile notification content
            Dim tileXml = CreateTileXml(title, message, branding, animationType)

            ' Create and send the notification
            Dim tileNotification As New TileNotification(tileXml)
            TileUpdateManager.CreateTileUpdaterForApplication().Update(tileNotification)
        Catch ex As Exception
            Debug.WriteLine($"LiveTileService: UpdateTileWithNews failed – {ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' Updates the Live Tile with multiple news items that cycle
    ''' </summary>
    Public Shared Sub UpdateTileWithMultipleNews(newsItems As List(Of NewsItem))
        If newsItems Is Nothing OrElse newsItems.Count = 0 Then Return

        Try
            ' Enable notification queue to show multiple tiles
            Dim tileUpdater = TileUpdateManager.CreateTileUpdaterForApplication()
            tileUpdater.EnableNotificationQueue(True)

            ' Clear existing notifications
            tileUpdater.Clear()

            ' Animation types to cycle through
            Dim animations As TileAnimation() = {
                TileAnimation.FadeIn,
                TileAnimation.SlideUp,
                TileAnimation.SlideDown,
                TileAnimation.SlideLeft,
                TileAnimation.SlideRight
            }

            ' Add each news item (max 5 in queue)
            For i = 0 To Math.Min(newsItems.Count - 1, 4)
                Dim item = newsItems(i)
                If item Is Nothing Then Continue For

                Dim animation = animations(i Mod animations.Length)
                Dim tileXml = CreateTileXml(item.Title, item.Message, "name", animation)
                Dim tileNotification As New TileNotification(tileXml)
                tileNotification.Tag = If(String.IsNullOrWhiteSpace(item.Tag), $"news{i}", item.Tag)
                tileUpdater.Update(tileNotification)
            Next
        Catch ex As Exception
            Debug.WriteLine($"LiveTileService: UpdateTileWithMultipleNews failed – {ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' Builds the adaptive tile content for all four tile sizes with the requested animation
    ''' style, using the Notifications library's typed object model instead of hand-written XML
    ''' strings - removes the need to hand-escape every field into a raw XML string.
    ''' </summary>
    Private Shared Function CreateTileXml(title As String, message As String, branding As String, Optional animation As TileAnimation = TileAnimation.FadeIn) As XmlDocument
        Dim titleStyle = GetTitleTextStyle(animation)
        Dim safeBranding = ParseBranding(branding)
        Dim safeTitle = SanitizeText(title)
        Dim safeMessage = SanitizeText(message)
        Dim smallTileText = SanitizeText(GetTileMonogram(title, branding))

        Dim content As New TileContent()
        content.Visual = New TileVisual()
        content.Visual.DisplayName = "Fort.ind"
        content.Visual.Branding = safeBranding

        ' Small tile: centered monogram
        Dim smallText As New AdaptiveText()
        smallText.Text = smallTileText
        smallText.HintStyle = AdaptiveTextStyle.Caption
        smallText.HintAlign = AdaptiveTextAlign.Center

        Dim smallContent As New TileBindingContentAdaptive()
        smallContent.TextStacking = TileTextStacking.Center
        smallContent.Children.Add(smallText)

        content.Visual.TileSmall = New TileBinding()
        content.Visual.TileSmall.Content = smallContent

        ' Medium and wide tiles: title + wrapped message in a group/subgroup, differing only in
        ' whether the title itself wraps and how many message lines are allowed
        content.Visual.TileMedium = New TileBinding()
        content.Visual.TileMedium.Content = BuildTitleMessageGroup(safeTitle, titleStyle, True, safeMessage, AdaptiveTextStyle.CaptionSubtle, 3)

        content.Visual.TileWide = New TileBinding()
        content.Visual.TileWide.Content = BuildTitleMessageGroup(safeTitle, titleStyle, False, safeMessage, AdaptiveTextStyle.Body, 2)

        ' Large tile: centered title in a group, message and static branding line below it
        Dim largeContent As New TileBindingContentAdaptive()
        largeContent.TextStacking = TileTextStacking.Center

        Dim largeTitleText As New AdaptiveText()
        largeTitleText.Text = safeTitle
        largeTitleText.HintStyle = titleStyle
        largeTitleText.HintAlign = AdaptiveTextAlign.Center

        Dim largeSubgroup As New AdaptiveSubgroup()
        largeSubgroup.Children.Add(largeTitleText)

        Dim largeGroup As New AdaptiveGroup()
        largeGroup.Children.Add(largeSubgroup)

        Dim largeMessageText As New AdaptiveText()
        largeMessageText.Text = safeMessage
        largeMessageText.HintStyle = AdaptiveTextStyle.Body
        largeMessageText.HintWrap = True
        largeMessageText.HintMaxLines = 6
        largeMessageText.HintAlign = AdaptiveTextAlign.Center

        Dim largeBrandingText As New AdaptiveText()
        largeBrandingText.Text = "Fort.ind Desktop"
        largeBrandingText.HintStyle = AdaptiveTextStyle.CaptionSubtle
        largeBrandingText.HintAlign = AdaptiveTextAlign.Center

        largeContent.Children.Add(largeGroup)
        largeContent.Children.Add(largeMessageText)
        largeContent.Children.Add(largeBrandingText)

        content.Visual.TileLarge = New TileBinding()
        content.Visual.TileLarge.Content = largeContent

        Return content.GetXml()
    End Function

    ''' <summary>
    ''' Builds a group/subgroup containing a styled title line and a wrapped message line -
    ''' shared by the medium and wide tile bindings, which only differ in title wrap and
    ''' message max-line settings.
    ''' </summary>
    Private Shared Function BuildTitleMessageGroup(title As String, titleStyle As AdaptiveTextStyle, wrapTitle As Boolean, message As String, messageStyle As AdaptiveTextStyle, messageMaxLines As Integer) As TileBindingContentAdaptive
        Dim titleText As New AdaptiveText()
        titleText.Text = title
        titleText.HintStyle = titleStyle
        titleText.HintWrap = wrapTitle

        Dim messageText As New AdaptiveText()
        messageText.Text = message
        messageText.HintStyle = messageStyle
        messageText.HintWrap = True
        messageText.HintMaxLines = messageMaxLines

        Dim subgroup As New AdaptiveSubgroup()
        subgroup.Children.Add(titleText)
        subgroup.Children.Add(messageText)

        Dim group As New AdaptiveGroup()
        group.Children.Add(subgroup)

        Dim result As New TileBindingContentAdaptive()
        result.Children.Add(group)
        Return result
    End Function

    ''' <summary>
    ''' Maps the tile's requested animation to the AdaptiveText style used for the title line.
    ''' </summary>
    Private Shared Function GetTitleTextStyle(animation As TileAnimation) As AdaptiveTextStyle
        Select Case animation
            Case TileAnimation.FadeIn
                Return AdaptiveTextStyle.CaptionSubtle
            Case TileAnimation.SlideUp
                Return AdaptiveTextStyle.Base
            Case TileAnimation.SlideDown
                Return AdaptiveTextStyle.Body
            Case TileAnimation.SlideLeft
                Return AdaptiveTextStyle.BodySubtle
            Case TileAnimation.SlideRight
                Return AdaptiveTextStyle.Subtitle
            Case Else
                Return AdaptiveTextStyle.Default
        End Select
    End Function

    ''' <summary>
    ''' Maps the branding string (always "name" from current call sites, but kept as a
    ''' parameter for compatibility) to the TileBranding enum the Notifications library expects.
    ''' </summary>
    Private Shared Function ParseBranding(branding As String) As TileBranding
        Select Case If(branding, "").Trim().ToLowerInvariant()
            Case "none"
                Return TileBranding.None
            Case "logo"
                Return TileBranding.Logo
            Case "nameandlogo"
                Return TileBranding.NameAndLogo
            Case Else
                Return TileBranding.Name
        End Select
    End Function

    ''' <summary>
    ''' Strips characters that aren't legal in XML 1.0 (the Notifications library still
    ''' serializes to XML under the hood) while passing valid surrogate pairs (e.g. emoji)
    ''' through unchanged. Entity escaping (&amp;, &lt;, etc.) is handled by the library itself,
    ''' so this only needs to guard against characters that would make the XML invalid outright.
    ''' </summary>
    Private Shared Function SanitizeText(text As String) As String
        If String.IsNullOrEmpty(text) Then Return ""

        Dim sanitized As New StringBuilder(text.Length)
        Dim i As Integer = 0
        While i < text.Length
            Dim ch As Char = text(i)

            If Char.IsHighSurrogate(ch) AndAlso i + 1 < text.Length AndAlso Char.IsLowSurrogate(text(i + 1)) Then
                sanitized.Append(ch)
                sanitized.Append(text(i + 1))
                i += 2
                Continue While
            End If

            If (ch = vbTab) OrElse (ch = vbLf) OrElse (ch = vbCr) OrElse
               (ch >= ChrW(&H20) AndAlso ch <= ChrW(&HD7FF)) OrElse
               (ch >= ChrW(&HE000) AndAlso ch <= ChrW(&HFFFD)) Then
                sanitized.Append(ch)
            End If

            i += 1
        End While

        Return sanitized.ToString()
    End Function

    ''' <summary>
    ''' Whether the app may put a badge on its tile and taskbar icon. Backed by LocalSettings so
    ''' the choice survives a restart, and enforced here rather than at each call site so no
    ''' caller can bypass it. Absent (or unreadable) means enabled, which is how the app behaved
    ''' before this setting existed. ClearBadge deliberately ignores this - clearing is always
    ''' allowed.
    ''' </summary>
    Public Shared Property BadgeEnabled As Boolean
        Get
            Try
                Dim stored = Windows.Storage.ApplicationData.Current.LocalSettings.Values(AppConstants.SettingShowTileBadge)
                If stored Is Nothing Then Return True
                Return Convert.ToBoolean(stored)
            Catch ex As Exception
                Debug.WriteLine($"LiveTileService: BadgeEnabled read failed – {ex.GetType().Name}: {ex.Message}")
                Return True
            End Try
        End Get
        Set(value As Boolean)
            Try
                Windows.Storage.ApplicationData.Current.LocalSettings.Values(AppConstants.SettingShowTileBadge) = value
            Catch ex As Exception
                Debug.WriteLine($"LiveTileService: BadgeEnabled write failed – {ex.GetType().Name}: {ex.Message}")
            End Try

            ' Switching it off takes effect on the tile now, rather than leaving the badge
            ' already on screen sitting there until something else happens to clear it.
            If Not value Then ClearBadge()
        End Set
    End Property

    ''' <summary>
    ''' Updates the badge on the tile (shows a number or glyph)
    ''' </summary>
    Public Shared Sub UpdateBadge(count As Integer)
        Try
            If Not BadgeEnabled Then Return

            If count <= 0 Then
                ClearBadge()
                Return
            End If

            Dim clampedCount = Math.Min(count, 99)
            Dim badgeXml = $"<badge value=""{clampedCount}""/>"
            Dim badgeDoc As New XmlDocument()
            badgeDoc.LoadXml(badgeXml)

            Dim badgeNotification As New BadgeNotification(badgeDoc)
            BadgeUpdateManager.CreateBadgeUpdaterForApplication().Update(badgeNotification)
        Catch ex As Exception
            Debug.WriteLine($"LiveTileService: UpdateBadge failed – {ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' Updates the badge with a glyph (icon)
    ''' </summary>
    Public Shared Sub UpdateBadgeGlyph(glyph As String)
        If String.IsNullOrWhiteSpace(glyph) Then Return

        Try
            If Not BadgeEnabled Then Return

            Dim normalizedGlyph = glyph.Trim()
            If Not IsSupportedBadgeGlyph(normalizedGlyph) Then
                Debug.WriteLine($"LiveTileService: UpdateBadgeGlyph skipped unsupported glyph '{normalizedGlyph}'.")
                Return
            End If

            ' Available glyphs: none, activity, alarm, alert, attention, available, away, busy,
            ' error, newMessage, paused, playing, unavailable
            Dim badgeXml = $"<badge value=""{normalizedGlyph}""/>"
            Dim badgeDoc As New XmlDocument()
            badgeDoc.LoadXml(badgeXml)

            Dim badgeNotification As New BadgeNotification(badgeDoc)
            BadgeUpdateManager.CreateBadgeUpdaterForApplication().Update(badgeNotification)
        Catch ex As Exception
            Debug.WriteLine($"LiveTileService: UpdateBadgeGlyph failed – {ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' Shows a Windows toast notification with a title and message.
    ''' Returns False (and writes to Debug output) if notifications are blocked or an error occurs.
    ''' </summary>
    Public Shared Function SendToast(title As String, message As String) As Boolean
        Try
            Dim notifier = ToastNotificationManager.CreateToastNotifier()
            If notifier.Setting <> NotificationSetting.Enabled Then
                Debug.WriteLine($"LiveTileService: Toast suppressed – NotificationSetting is {notifier.Setting}. " &
                                "Enable notifications for this app in Windows Settings > System > Notifications.")
                Return False
            End If

            Dim toastContent = New ToastContentBuilder().
                AddText(SanitizeText(title)).
                AddText(SanitizeText(message)).
                GetToastContent()

            Dim toast As New ToastNotification(toastContent.GetXml())
            notifier.Show(toast)
            Return True
        Catch ex As Exception
            Debug.WriteLine($"LiveTileService: SendToast failed – {ex.GetType().Name}: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Clears the Live Tile back to default
    ''' </summary>
    Public Shared Sub ClearTile()
        Try
            TileUpdateManager.CreateTileUpdaterForApplication().Clear()
        Catch ex As Exception
            Debug.WriteLine($"LiveTileService: ClearTile failed – {ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' Clears the badge
    ''' </summary>
    Public Shared Sub ClearBadge()
        Try
            BadgeUpdateManager.CreateBadgeUpdaterForApplication().Clear()
        Catch ex As Exception
            Debug.WriteLine($"LiveTileService: ClearBadge failed – {ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub

    Private Shared Function GetTileMonogram(primaryText As String, fallbackText As String) As String
        Dim source = If(String.IsNullOrWhiteSpace(primaryText), fallbackText, primaryText)
        If String.IsNullOrWhiteSpace(source) Then Return "FI"

        Dim trimmed = source.Trim()
        If trimmed.Length <= 2 Then Return trimmed.ToUpperInvariant()
        Return trimmed.Substring(0, 2).ToUpperInvariant()
    End Function

    Private Shared Function IsSupportedBadgeGlyph(glyph As String) As Boolean
        Select Case glyph.ToLowerInvariant()
            Case "none", "activity", "alarm", "alert", "attention", "available", "away", "busy", "error", "newmessage", "paused", "playing", "unavailable"
                Return True
            Case Else
                Return False
        End Select
    End Function

End Class

''' <summary>
''' Tile animation types
''' </summary>
Public Enum TileAnimation
    FadeIn
    SlideUp
    SlideDown
    SlideLeft
    SlideRight
End Enum

''' <summary>
''' Represents a news item for the Live Tile
''' </summary>
Public Class NewsItem
    Public Property Title As String
    Public Property Message As String
    Public Property Tag As String
    Public Property Timestamp As DateTime

    Public Sub New(title As String, message As String, Optional tag As String = Nothing)
        Me.Title = title
        Me.Message = message
        Me.Tag = tag
        Me.Timestamp = DateTime.Now
    End Sub
End Class
