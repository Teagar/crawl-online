namespace CrawlOnline
{
    // Pure decision and one-shot latch used by the reflected native-menu hook.
    // It deliberately has no dependency on Crawl or Unity types.
    public static class NativeMenuAltBackContract
    {
        public static bool ShouldLatch(bool inputDown, bool isAltInput, bool isInstalledMenu,
            bool isSubmenuOpen)
        {
            return inputDown && isAltInput && isInstalledMenu && isSubmenuOpen;
        }
    }

    public sealed class NativeMenuBackLatch
    {
        private bool pending;

        public bool TryLatch(bool inputDown, bool isAltInput, bool isInstalledMenu,
            bool isSubmenuOpen)
        {
            if (!NativeMenuAltBackContract.ShouldLatch(inputDown, isAltInput, isInstalledMenu,
                isSubmenuOpen)) return false;
            pending = true;
            return true;
        }

        public bool TryConsume()
        {
            if (!pending) return false;
            pending = false;
            return true;
        }
    }
}
