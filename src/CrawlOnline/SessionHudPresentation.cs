using System;

namespace CrawlOnline
{
    // Pure presentation rules shared by the Unity HUD and headless tests.
    public static class SessionHudPresentation
    {
        public const float CompactWidth = 314f;
        public const float CompactHeight = 42f;
        public const float ExpandedWidth = 336f;
        public const float ExpandedHeight = 188f;

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
            if (!string.IsNullOrEmpty(notice)) return NativeText(notice);
            if (state == null) return string.Empty;
            if (state.Status == SessionHudStatus.Error || state.Status == SessionHudStatus.Offline)
                return NativeText(state.Message);
            return NativeText(state.RoleLabel + "  " + RosterLabel(state));
        }

        public static string NativeText(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\u2026", "...").ToUpperInvariant();
        }
    }
}
