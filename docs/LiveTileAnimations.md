# Live Tile Animations Guide

## Overview
custom tile animiations for fort.uwps app icon
## Available Animations

### 1. **FadeIn**
- Smooth fade-in effect
- Best for: General news updates
- Default animation

### 2. **SlideUp**
- Content slides up from bottom
- Best for: New features, announcements

### 3. **SlideDown**
- Content slides down from top
- Best for: Important updates

### 4. **SlideLeft**
- Content slides in from right
- Best for: Event notifications

### 5. **SlideRight**
- Content slides in from left
- Best for: Status updates

## how they shoould be wrote 

When you add multiple news items in `MainPage.xaml.vb`:

```vb
Dim newsItems As New List(Of NewsItem) From {
    New NewsItem("Title 1", "Message 1", "tag1"),  ' Uses FadeIn
    New NewsItem("Title 2", "Message 2", "tag2"),  ' Uses SlideUp
    New NewsItem("Title 3", "Message 3", "tag3"),  ' Uses SlideDown
    New NewsItem("Title 4", "Message 4", "tag4"),  ' Uses SlideLeft
    New NewsItem("Title 5", "Message 5", "tag5")   ' Uses SlideRight
}
```

The animations **automatically cycle** - each news item gets a different animation effect!

## sizes used:

- **Small (71x71)**: Shows emoji icon centered
- **Medium (150x150)**: Shows title + 3 lines of text
- **Wide (310x150)**: Shows title + 2 lines of text
- **Large (310x310)**: Shows title + 6 lines of text + app name

## Customization

To use a specific animation for a single tile update:

```vb
LiveTileService.UpdateTileWithNews("Title", "Message", "name", TileAnimation.SlideUp)
```