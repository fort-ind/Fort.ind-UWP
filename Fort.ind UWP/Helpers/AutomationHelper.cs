using System;
using System.Diagnostics;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;

namespace Fort.ind_UWP
{
    /// <summary>
    /// Raises UI Automation notifications for changes that move no focus and add no focusable
    /// element, and are therefore silent to a screen reader - the search box swapping its
    /// suggestion list being the case in this app. The accessibility docs are explicit that a
    /// region refreshed in place needs an announcement of its own, or assistive technology never
    /// learns anything changed.
    /// </summary>
    public static class AutomationHelper
    {
        /// <summary>
        /// Announces a status the user did not directly ask for - a result count landing after a
        /// debounce, say. Yields to speech already in progress, and a later announcement carrying
        /// the same <paramref name="activityId"/> supersedes this one rather than queueing behind
        /// it, so a fast typist hears the count for what they typed last.
        /// </summary>
        public static void AnnounceStatus(UIElement source, string message, string activityId)
        {
            try
            {
                if (source == null || string.IsNullOrEmpty(message)) return;

                // FromElement returns a peer only once something has already built one, which for
                // a plain layout element is never - so fall back to creating it.
                var peer = FrameworkElementAutomationPeer.FromElement(source)
                           ?? FrameworkElementAutomationPeer.CreatePeerForElement(source);
                if (peer == null) return;

                peer.RaiseNotificationEvent(AutomationNotificationKind.Other,
                                            AutomationNotificationProcessing.MostRecent,
                                            message,
                                            activityId ?? "");
            }
            catch (Exception ex)
            {
                // An announcement failing must never take out the search that triggered it.
                Debug.WriteLine($"AutomationHelper: notification failed - {ex.Message}");
            }
        }
    }
}
