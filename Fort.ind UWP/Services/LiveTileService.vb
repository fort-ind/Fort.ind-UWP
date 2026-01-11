Imports Windows.UI.Notifications
Imports Windows.Data.Xml.Dom

''' <summary>
''' Service for managing Live Tile updates with news and notifications
''' </summary>
Public Class LiveTileService

    ''' <summary>
    ''' Updates the Live Tile with the latest news
    ''' </summary>
    Public Shared Sub UpdateTileWithNews(title As String, message As String, Optional branding As String = "name")
        ' Create the tile notification content
        Dim tileXml = CreateTileXml(title, message, branding)

        ' Create and send the notification
        Dim tileNotification As New TileNotification(tileXml)
        TileUpdateManager.CreateTileUpdaterForApplication().Update(tileNotification)
    End Sub

    ''' <summary>
    ''' Updates the Live Tile with multiple news items that cycle
    ''' </summary>
    Public Shared Sub UpdateTileWithMultipleNews(newsItems As List(Of NewsItem))
        ' Enable notification queue to show multiple tiles
        Dim tileUpdater = TileUpdateManager.CreateTileUpdaterForApplication()
        tileUpdater.EnableNotificationQueue(True)

        ' Clear existing notifications
        tileUpdater.Clear()

        ' Add each news item (max 5 in queue)
        For i = 0 To Math.Min(newsItems.Count - 1, 4)
            Dim item = newsItems(i)
            Dim tileXml = CreateTileXml(item.Title, item.Message, "name", item.Tag)
            Dim tileNotification As New TileNotification(tileXml)
            tileNotification.Tag = If(item.Tag, $"news{i}")
            tileUpdater.Update(tileNotification)
        Next
    End Sub

    ''' <summary>
    ''' Creates the tile XML for different tile sizes
    ''' </summary>
    Private Shared Function CreateTileXml(title As String, message As String, branding As String, Optional tag As String = Nothing) As XmlDocument
        ' Adaptive tile template supporting all sizes
        Dim tileXmlString = $"
<tile>
    <visual branding=""{branding}"">
        
        <!-- Small Tile (71x71) -->
        <binding template=""TileSmall"">
            <text hint-style=""caption"">Fort.ind</text>
        </binding>
        
        <!-- Medium Tile (150x150) -->
        <binding template=""TileMedium"">
            <text hint-style=""caption"" hint-wrap=""true"">{EscapeXml(title)}</text>
            <text hint-style=""captionSubtle"" hint-wrap=""true"" hint-maxLines=""3"">{EscapeXml(message)}</text>
        </binding>
        
        <!-- Wide Tile (310x150) -->
        <binding template=""TileWide"">
            <text hint-style=""subtitle"">{EscapeXml(title)}</text>
            <text hint-style=""body"" hint-wrap=""true"" hint-maxLines=""2"">{EscapeXml(message)}</text>
        </binding>
        
        <!-- Large Tile (310x310) -->
        <binding template=""TileLarge"">
            <text hint-style=""title"">{EscapeXml(title)}</text>
            <text hint-style=""body"" hint-wrap=""true"" hint-maxLines=""6"">{EscapeXml(message)}</text>
            <text hint-style=""captionSubtle"">Fort.ind Desktop</text>
        </binding>
        
    </visual>
</tile>"

        Dim tileXml As New XmlDocument()
        tileXml.LoadXml(tileXmlString)
        Return tileXml
    End Function

    ''' <summary>
    ''' Escapes special XML characters
    ''' </summary>
    Private Shared Function EscapeXml(text As String) As String
        If String.IsNullOrEmpty(text) Then Return ""
        Return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("""", "&quot;").Replace("'", "&apos;")
    End Function

    ''' <summary>
    ''' Updates the badge on the tile (shows a number or glyph)
    ''' </summary>
    Public Shared Sub UpdateBadge(count As Integer)
        Dim badgeXml = $"<badge value=""{count}""/>"
        Dim badgeDoc As New XmlDocument()
        badgeDoc.LoadXml(badgeXml)

        Dim badgeNotification As New BadgeNotification(badgeDoc)
        BadgeUpdateManager.CreateBadgeUpdaterForApplication().Update(badgeNotification)
    End Sub

    ''' <summary>
    ''' Updates the badge with a glyph (icon)
    ''' </summary>
    Public Shared Sub UpdateBadgeGlyph(glyph As String)
        ' Available glyphs: none, activity, alarm, alert, attention, available, away, busy, 
        ' error, newMessage, paused, playing, unavailable
        Dim badgeXml = $"<badge value=""{glyph}""/>"
        Dim badgeDoc As New XmlDocument()
        badgeDoc.LoadXml(badgeXml)

        Dim badgeNotification As New BadgeNotification(badgeDoc)
        BadgeUpdateManager.CreateBadgeUpdaterForApplication().Update(badgeNotification)
    End Sub

    ''' <summary>
    ''' Clears the Live Tile back to default
    ''' </summary>
    Public Shared Sub ClearTile()
        TileUpdateManager.CreateTileUpdaterForApplication().Clear()
    End Sub

    ''' <summary>
    ''' Clears the badge
    ''' </summary>
    Public Shared Sub ClearBadge()
        BadgeUpdateManager.CreateBadgeUpdaterForApplication().Clear()
    End Sub

End Class

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
