using System;

namespace CrawlOnline
{
    // Pure presentation rules shared by the Unity HUD and headless tests.
    public static class SessionHudPresentation
    {
        public const float CompactWidth = 260f;
        public const float CompactHeight = 36f;
        public const float ExpandedWidth = 280f;
        public const float ExpandedHeight = 58f;

        public static int ScaleForHeight(int screenHeight)
        {
            int scale = screenHeight / 540;
            if (scale < 1) return 1;
            return scale > 3 ? 3 : scale;
        }

        public static string StatusLabel(SessionHudStatus status)
        {
            switch (status)
            {
                case SessionHudStatus.CreatingLobby: return "CREATING";
                case SessionHudStatus.WaitingForPeers: return "WAITING";
                case SessionHudStatus.Authenticating: return "LINKING";
                case SessionHudStatus.Connected: return "CONNECTED";
                case SessionHudStatus.Error: return "ERROR";
                default: return "OFFLINE";
            }
        }

        public static string RosterLabel(SessionHudState state)
        {
            if (state == null) return "PLAYERS  - - - -   0/4";
            string text = "PLAYERS  ";
            for (int slot = 0; slot < 4; slot++)
            {
                if (slot > 0) text += " ";
                text += state.IsSlotConnected(slot) ? (slot + 1).ToString() : "-";
            }
            return text + "   " + state.ConnectedPlayerCount + "/4";
        }

        public static string ContextLabel(SessionHudState state, string notice)
        {
            if (!string.IsNullOrEmpty(notice))
            {
                string normalizedNotice = NativeText(notice);
                if (normalizedNotice == "RETURN TO THE MAIN MENU BEFORE HOSTING OR JOINING ONLINE.")
                    return "RETURN TO MENU FOR ONLINE";
                return CompactText(normalizedNotice, 30);
            }
            if (state == null) return string.Empty;
            if (state.Status == SessionHudStatus.Error || state.Status == SessionHudStatus.Offline)
                return CompactText(NativeText(state.Message), 30);
            if (state.IsHost) return "HOST";
            return state.LocalSlot < 0 ? "CLIENT / PENDING" : "CLIENT";
        }

        public static string NativeText(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\u2026", "...").ToUpperInvariant();
        }

        private static string CompactText(string value, int maximumLength)
        {
            if (value.Length <= maximumLength) return value;
            return value.Substring(0, maximumLength - 3).TrimEnd() + "...";
        }
    }
}
