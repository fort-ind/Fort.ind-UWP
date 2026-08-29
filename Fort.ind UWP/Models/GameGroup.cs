using System.Collections.ObjectModel;

namespace Fort.ind_UWP
{
    public sealed class GameGroup
    {
        public GameGroup(string key)
        {
            this.Key = key;
            this.Items = new ObservableCollection<SearchItem>();
        }

        public string Key { get; }

        public ObservableCollection<SearchItem> Items { get; }

        public override string ToString()
        {
            return Key;
        }
    }
}
