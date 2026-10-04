using System;

namespace CrawlOnline.Menu
{
    internal sealed class NativeMenuVisualSelectionPlan
    {
        private NativeMenuVisualSelectionPlan(int count, int selectedIndex)
        {
            Count = count;
            SelectedIndex = selectedIndex;
        }

        public int Count { get; private set; }
        public int SelectedIndex { get; private set; }

        // The OnSelect(bool) parameter's semantic name is unavailable through public metadata.
        // false is the conservative reconstruction request: synchronize selection without
        // requesting optional audible or animated feedback from the newly materialized item.
        public bool OnSelectArgument { get { return false; } }

        public static NativeMenuVisualSelectionPlan Create(int count, int selectedIndex)
        {
            if (count <= 0 || selectedIndex < 0 || selectedIndex >= count)
                throw new ArgumentOutOfRangeException();
            return new NativeMenuVisualSelectionPlan(count, selectedIndex);
        }

        public bool IsSelected(int index)
        {
            return index == SelectedIndex;
        }
    }
}
