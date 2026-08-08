using System.Collections.ObjectModel;

namespace Fort.ind_UWP
{
    /// <summary>
    /// One alphabetical bucket of games – "A" through "Z", plus "#" for every title that starts
    /// with a digit or anything else outside A–Z. Bound through a CollectionViewSource with
    /// IsSourceGrouped=True and ItemsPath="Items", so this type is deliberately NOT a collection
    /// itself: keeping Items strongly typed means no (SearchItem) casts anywhere in the page.
    /// </summary>
    public sealed class GameGroup
    {

        public GameGroup(string key)
        {
            this.Key = key;
            this.Items = new ObservableCollection<SearchItem>();
        }

        /// <summary>
        /// Header text and jump-grid label: a single letter, or "#".
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Observable so the filter can replace the rows in a group without swapping the
        /// collection instance the CollectionViewSource is watching.
        /// </summary>
        public ObservableCollection<SearchItem> Items { get; }

        /// <summary>
        /// Narrator fallback for the jump tile if a template ever omits an automation name.
        /// </summary>
        public override string ToString()
        {
            return Key;
        }

    }
}
