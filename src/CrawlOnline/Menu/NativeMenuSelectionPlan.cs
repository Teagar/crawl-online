using System;

namespace CrawlOnline.Menu
{
    internal sealed class NativeMenuSelectionPlan
    {
        private NativeMenuSelectionPlan(int count, int selectedIndex)
        {
            Count = count;
            SelectedIndex = selectedIndex;
        }

        public int Count { get; private set; }
        public int SelectedIndex { get; private set; }

        public static NativeMenuSelectionPlan Create(int count, int selectedIndex)
        {
            if (count <= 0 || selectedIndex < 0 || selectedIndex >= count)
                throw new ArgumentOutOfRangeException();
            return new NativeMenuSelectionPlan(count, selectedIndex);
        }

        public bool IsSelected(int index)
        {
            return index == SelectedIndex;
        }
    }
}
