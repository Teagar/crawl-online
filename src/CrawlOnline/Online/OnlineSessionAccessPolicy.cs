namespace CrawlOnline.Online
{
    // Keeps the session boundary independent of the reflected menu implementation.
    // Starting or joining a lobby is only safe before a local campaign has started.
    public static class OnlineSessionAccessPolicy
    {
        public const string MainMenuRequiredMessage =
            "Return to the main menu before hosting or joining online.";

        public static bool CanStartOrJoin(bool isActiveMainMenu)
        {
            return isActiveMainMenu;
        }
    }
}
