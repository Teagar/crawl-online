using System;

namespace CrawlOnline.Online
{
    public static class FriendLobbyList
    {
        public static ulong[] Normalize(ulong[] lobbyIds, int maximum)
        {
            if (lobbyIds == null || lobbyIds.Length == 0 || maximum <= 0) return new ulong[0];
            ulong[] sorted = (ulong[])lobbyIds.Clone();
            Array.Sort(sorted);
            ulong[] result = new ulong[Math.Min(sorted.Length, maximum)];
            int count = 0;
            ulong previous = 0;
            for (int i = 0; i < sorted.Length && count < result.Length; i++)
            {
                if (sorted[i] == 0 || (count > 0 && sorted[i] == previous)) continue;
                result[count++] = sorted[i];
                previous = sorted[i];
            }
            if (count == result.Length) return result;
            ulong[] trimmed = new ulong[count];
            Array.Copy(result, trimmed, count);
            return trimmed;
        }
    }

    public sealed class FriendLobbyMenuPlan
    {
        private FriendLobbyMenuPlan(string[] labels, string[] messages, int selectedIndex)
        {
            Labels = labels;
            Messages = messages;
            SelectedIndex = selectedIndex;
        }

        public string[] Labels { get; private set; }
        public string[] Messages { get; private set; }
        public int SelectedIndex { get; private set; }

        public static FriendLobbyMenuPlan Create(int count)
        {
            if (count <= 0)
                return new FriendLobbyMenuPlan(
                    new[] { "NO FRIEND GAMES", "REFRESH", "BACK" },
                    new[] { "MsgCrawlOnlineNoop", "MsgCrawlOnlineRefresh", "MsgCrawlOnlineCancel" }, 1);

            int bounded = Math.Min(count, 3);
            string[] labels = new string[bounded + 2];
            string[] messages = new string[bounded + 2];
            for (int i = 0; i < bounded; i++)
            {
                labels[i] = "FRIEND GAME " + (i + 1);
                messages[i] = "MsgCrawlOnlineFriend" + i;
            }
            labels[bounded] = "REFRESH";
            messages[bounded] = "MsgCrawlOnlineRefresh";
            labels[bounded + 1] = "BACK";
            messages[bounded + 1] = "MsgCrawlOnlineCancel";
            return new FriendLobbyMenuPlan(labels, messages, 0);
        }
    }
}
