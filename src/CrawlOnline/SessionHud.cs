using UnityEngine;

namespace CrawlOnline
{
    // Original, deliberately small IMGUI overlay. It uses only the game's loaded
    // Unity GUI skin at runtime; no Crawl assets, fonts, or copied layout data ship here.
    internal sealed class SessionHud
    {
        private const float Width = 278f;
        private bool minimized;
        private bool tutorialVisible = true;

        public void ToggleMinimized()
        {
            minimized = !minimized;
        }

        public void Draw(SessionHudState state)
        {
            if (state == null) return;

            float x = 12f;
            float height = minimized ? 25f : tutorialVisible ? 185f : 126f;
            float y = Mathf.Max(12f, Screen.height - height - 12f);
            Rect panel = new Rect(x, y, Width, height);
            GUI.Box(panel, "Crawl Online");

            if (GUI.Button(new Rect(x + Width - 31f, y + 3f, 24f, 20f), minimized ? "+" : "–"))
            {
                minimized = !minimized;
                return;
            }
            if (minimized) return;

            GUI.Label(new Rect(x + 11f, y + 29f, 120f, 20f), StatusLabel(state.Status));
            GUI.Label(new Rect(x + 135f, y + 29f, 132f, 20f), state.RoleLabel);
            GUI.Label(new Rect(x + 11f, y + 50f, 250f, 20f), "ROSTER  " + RosterLabel(state));
            GUI.Label(new Rect(x + 11f, y + 73f, 250f, 34f), state.Message);

            if (tutorialVisible)
            {
                GUI.Label(new Rect(x + 11f, y + 108f, 250f, 42f),
                    "F8 host  •  F7 invite  •  F9 leave\nF6 minimizes this panel. Gameplay input stays untouched.");
                if (GUI.Button(new Rect(x + 184f, y + 156f, 78f, 20f), "Got it"))
                {
                    tutorialVisible = false;
                }
            }
        }

        private static string StatusLabel(SessionHudStatus status)
        {
            switch (status)
            {
                case SessionHudStatus.CreatingLobby: return "CREATING";
                case SessionHudStatus.WaitingForPeers: return "WAITING";
                case SessionHudStatus.Authenticating: return "AUTH";
                case SessionHudStatus.Connected: return "CONNECTED";
                case SessionHudStatus.Error: return "ERROR";
                default: return "OFFLINE";
            }
        }

        private static string RosterLabel(SessionHudState state)
        {
            string text = string.Empty;
            for (int slot = 0; slot < 4; slot++)
            {
                if (slot > 0) text += "  ";
                text += state.IsSlotConnected(slot) ? (slot + 1).ToString() : "·";
            }
            return text + "    " + state.ConnectedPlayerCount + "/4";
        }
    }
}
