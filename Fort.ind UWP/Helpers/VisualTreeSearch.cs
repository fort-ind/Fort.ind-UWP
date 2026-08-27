using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace Fort.ind_UWP
{
    /// <summary>
    /// Walks an applied control template. Named <c>VisualTreeSearch</c> rather than
    /// <c>VisualTreeHelper</c> so it does not shadow <see cref="VisualTreeHelper"/>, which it
    /// is built on.
    /// </summary>
    public static class VisualTreeSearch
    {

        /// <summary>
        /// Depth-first search for a named element inside a control's applied template. Template
        /// parts are not page fields, so they cannot be reached by x:Name from code-behind.
        /// </summary>
        public static FrameworkElement FindDescendantByName(DependencyObject root, string name)
        {
            if (root == null) return null;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);

                var element = child as FrameworkElement;
                if (element != null && element.Name == name) return element;

                var found = FindDescendantByName(child, name);
                if (found != null) return found;
            }

            return null;
        }

    }
}
