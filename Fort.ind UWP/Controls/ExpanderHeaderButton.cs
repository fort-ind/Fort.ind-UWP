using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    /// <summary>
    /// The clickable header of a Settings section. A plain <see cref="Button"/> announces only
    /// "button": the expanded state lives in the content panel's Visibility and in a rotated
    /// chevron, neither of which reaches UI Automation. This exposes the ExpandCollapse pattern
    /// so a screen reader says "collapsed"/"expanded" and can toggle the section directly.
    /// Keep <see cref="IsExpanded"/> in step with the panel - it is the only thing the peer reads.
    /// </summary>
    public sealed class ExpanderHeaderButton : Button
    {
        /// <summary>Identifies the <see cref="IsExpanded"/> dependency property.</summary>
        public static readonly DependencyProperty IsExpandedProperty =
            DependencyProperty.Register(
                "IsExpanded",
                typeof(bool),
                typeof(ExpanderHeaderButton),
                new PropertyMetadata(false, OnIsExpandedChanged));

        /// <summary>Whether the section this header controls is currently showing its content.</summary>
        public bool IsExpanded
        {
            get { return (bool)GetValue(IsExpandedProperty); }
            set { SetValue(IsExpandedProperty, value); }
        }

        /// <inheritdoc/>
        protected override AutomationPeer OnCreateAutomationPeer()
        {
            return new ExpanderHeaderButtonAutomationPeer(this);
        }

        private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var button = d as ExpanderHeaderButton;
            if (button == null) return;

            // FromElement, not CreatePeerForElement: with no assistive technology attached no peer
            // exists, and there is nothing to notify.
            var peer = FrameworkElementAutomationPeer.FromElement(button) as ExpanderHeaderButtonAutomationPeer;
            if (peer == null) return;

            peer.RaiseExpandCollapseStateChanged((bool)e.OldValue, (bool)e.NewValue);
        }
    }

    /// <summary>
    /// Adds <see cref="IExpandCollapseProvider"/> to the button peer. Invoke is inherited from
    /// <see cref="ButtonAutomationPeer"/> and still works, so the header keeps its Invoke pattern
    /// as well - that is what Narrator's Enter goes through.
    /// </summary>
    public sealed class ExpanderHeaderButtonAutomationPeer : ButtonAutomationPeer, IExpandCollapseProvider
    {
        /// <summary>Creates a peer for the given header button.</summary>
        public ExpanderHeaderButtonAutomationPeer(ExpanderHeaderButton owner)
            : base(owner)
        {
        }

        /// <summary>The section's current expanded state.</summary>
        public ExpandCollapseState ExpandCollapseState
        {
            get
            {
                var owner = Owner as ExpanderHeaderButton;
                return owner != null && owner.IsExpanded
                       ? ExpandCollapseState.Expanded
                       : ExpandCollapseState.Collapsed;
            }
        }

        /// <summary>Expands the section, if it is not already expanded.</summary>
        public void Expand()
        {
            var owner = Owner as ExpanderHeaderButton;
            if (owner == null || owner.IsExpanded) return;

            // The Click handler is the one place that knows how to move the panel, the chevron and
            // the saved LocalSettings state together, so route through it rather than duplicating.
            Invoke();
        }

        /// <summary>Collapses the section, if it is not already collapsed.</summary>
        public void Collapse()
        {
            var owner = Owner as ExpanderHeaderButton;
            if (owner == null || !owner.IsExpanded) return;

            Invoke();
        }

        /// <inheritdoc/>
        protected override object GetPatternCore(PatternInterface patternInterface)
        {
            if (patternInterface == PatternInterface.ExpandCollapse) return this;
            return base.GetPatternCore(patternInterface);
        }

        internal void RaiseExpandCollapseStateChanged(bool oldValue, bool newValue)
        {
            RaisePropertyChangedEvent(
                ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                oldValue ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed,
                newValue ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
        }
    }
}
