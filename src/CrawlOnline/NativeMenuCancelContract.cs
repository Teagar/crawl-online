namespace CrawlOnline
{
    public static class NativeMenuCancelContract
    {
        public const string BackMessage = "MsgBack";
        public const string CancelMessage = "MsgCancel";

        // The legitimate MenuTextMenu dispatches one of these messages for its
        // controller cancel action; they are distinct from selectable BACK rows.
        public static bool IsCancelMessage(string message)
        {
            return message == BackMessage || message == CancelMessage;
        }
    }
}
