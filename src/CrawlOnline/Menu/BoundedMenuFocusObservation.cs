namespace CrawlOnline.Menu
{
    internal sealed class BoundedMenuFocusObservation
    {
        public const int SampleCount = 6;
        private int remaining;

        public void Begin()
        {
            remaining = SampleCount;
        }

        public bool TryTakeSample()
        {
            if (remaining <= 0) return false;
            remaining--;
            return true;
        }
    }
}
