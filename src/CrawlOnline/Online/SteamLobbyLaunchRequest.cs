using System;
using System.Globalization;

namespace CrawlOnline.Online
{
    public static class SteamLobbyLaunchRequest
    {
        private const string ConnectLobbyArgument = "+connect_lobby";

        public static bool TryParse(string[] arguments, out ulong lobbyId)
        {
            lobbyId = 0;
            if (arguments == null) return false;

            bool found = false;
            for (int i = 0; i < arguments.Length; i++)
            {
                if (!string.Equals(arguments[i], ConnectLobbyArgument,
                    StringComparison.OrdinalIgnoreCase)) continue;
                if (found || i + 1 >= arguments.Length)
                {
                    lobbyId = 0;
                    return false;
                }

                ulong candidate;
                if (!ulong.TryParse(arguments[++i], NumberStyles.None,
                    CultureInfo.InvariantCulture, out candidate) || candidate == 0)
                {
                    lobbyId = 0;
                    return false;
                }
                lobbyId = candidate;
                found = true;
            }
            return found;
        }
    }
}
