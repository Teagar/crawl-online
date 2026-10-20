using System;
using System.Text;
using CrawlOnline.Protocol;

namespace CrawlOnline
{
    public sealed class PregameLobbyPlan
    {
        public PregameLobbyPlan(string[] labels, string[] messages, bool[] enabled,
            int selectedIndex, string signature)
        {
            Labels = labels;
            Messages = messages;
            Enabled = enabled;
            SelectedIndex = selectedIndex;
            Signature = signature;
        }

        public string[] Labels { get; private set; }
        public string[] Messages { get; private set; }
        public bool[] Enabled { get; private set; }
        public int SelectedIndex { get; private set; }
        public string Signature { get; private set; }
    }

    public static class PregameLobbyPresentation
    {
        public static PregameLobbyPlan Create(SessionHudState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            var labels = new System.Collections.Generic.List<string>();
            var messages = new System.Collections.Generic.List<string>();
            var enabled = new System.Collections.Generic.List<bool>();

            // Crawl's native menu collapses disabled items onto the same visual row.
            // Keep these inert through the no-op message, but enabled so its own layout
            // gives the two compact roster rows distinct positions.
            Add(labels, messages, enabled,
                PhaseLabel(state.PregamePhase) + "  /  " + SlotLabel(state, 0),
                "MsgCrawlOnlineNoop", true);
            Add(labels, messages, enabled,
                SlotLabel(state, 1) + "  /  " + SlotLabel(state, 2) + "  /  " + SlotLabel(state, 3),
                "MsgCrawlOnlineNoop", true);

            int selected;
            if (state.IsHost)
            {
                if (state.ConnectedPlayerCount > 1)
                {
                    selected = labels.Count;
                    Add(labels, messages, enabled, "START GAME", "MsgCrawlOnlineStart", true);
                }
                else
                {
                    Add(labels, messages, enabled, "WAITING FOR PLAYER", "MsgCrawlOnlineNoop", true);
                    selected = labels.Count;
                }
                Add(labels, messages, enabled, "INVITE FRIENDS", "MsgCrawlOnlineInvite", true);
                Add(labels, messages, enabled, "LEAVE LOBBY", "MsgCrawlOnlineCancel", true);
            }
            else
            {
                Add(labels, messages, enabled,
                    state.PregamePhase == PregamePhase.WaitingForHost ? "WAITING FOR HOST" : PhaseLabel(state.PregamePhase),
                    "MsgCrawlOnlineNoop", true);
                selected = labels.Count;
                Add(labels, messages, enabled, "LEAVE LOBBY", "MsgCrawlOnlineCancel", true);
            }

            string signature = string.Join("\n", labels.ToArray()) + "|" + selected;
            return new PregameLobbyPlan(labels.ToArray(), messages.ToArray(), enabled.ToArray(), selected, signature);
        }

        public static string SanitizeName(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value) || maximumLength <= 0) return string.Empty;
            string upper = value.Trim().ToUpperInvariant();
            var result = new StringBuilder(Math.Min(upper.Length, maximumLength));
            bool pendingSpace = false;
            for (int i = 0; i < upper.Length && result.Length < maximumLength; i++)
            {
                char c = upper[i];
                bool allowed = (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                    c == '_' || c == '-' || c == '.';
                if (allowed)
                {
                    if (pendingSpace && result.Length > 0 && result.Length < maximumLength) result.Append(' ');
                    pendingSpace = false;
                    if (result.Length < maximumLength) result.Append(c);
                }
                else if (char.IsWhiteSpace(c)) pendingSpace = true;
            }
            return result.ToString().Trim();
        }

        private static string PhaseLabel(PregamePhase phase)
        {
            if (phase == PregamePhase.ConfiguringPlayers) return "SETTING UP PLAYERS";
            if (phase == PregamePhase.StartingGame) return "STARTING GAME";
            return "ONLINE LOBBY";
        }

        private static string SlotLabel(SessionHudState state, int slot)
        {
            string prefix = "P" + (slot + 1) + " ";
            if (!state.IsSlotConnected(slot)) return prefix + "EMPTY";
            string name = SanitizeName(state.GetSlotName(slot), 10);
            if (name.Length == 0) name = "PLAYER" + (slot + 1);
            if (slot == 0) return prefix + name + " HOST" + (state.LocalSlot == 0 ? "/YOU" : string.Empty);
            return prefix + name + (state.LocalSlot == slot ? " YOU" : " ON");
        }

        private static void Add(System.Collections.Generic.List<string> labels,
            System.Collections.Generic.List<string> messages,
            System.Collections.Generic.List<bool> enabled, string label, string message, bool isEnabled)
        {
            labels.Add(label);
            messages.Add(message);
            enabled.Add(isEnabled);
        }
    }
}
