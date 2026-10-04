namespace CrawlOnline.Menu
{
    internal sealed class DeferredMenuFocus
    {
        private int generation;

        public int Request()
        {
            generation++;
            return generation;
        }

        public bool IsCurrent(int request)
        {
            return request == generation;
        }
    }
}
