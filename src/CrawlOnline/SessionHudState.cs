using System;

namespace CrawlOnline
{
    public enum SessionHudStatus
    {
        Offline,
        CreatingLobby,
        WaitingForPeers,
        Authenticating,
        Connected,
        Error
    }

    // A deliberately identity-free projection of SteamLobbySession for the HUD and tests.
    // Slot zero is the host; an unset local slot is represented by -1.
    public sealed class SessionHudState
    {
        private readonly bool[] connectedSlots;

        public SessionHudState(SessionHudStatus status, bool isHost, int localSlot,
            bool[] slots, string message)
        {
            Status = status;
            IsHost = isHost;
            LocalSlot = localSlot;
            Message = message ?? string.Empty;
            connectedSlots = new bool[4];
            if (slots != null)
            {
                Array.Copy(slots, connectedSlots, Math.Min(slots.Length, connectedSlots.Length));
            }
        }

        public SessionHudStatus Status { get; private set; }
        public bool IsHost { get; private set; }
        public int LocalSlot { get; private set; }
        public string Message { get; private set; }

        public bool IsSlotConnected(int slot)
        {
            return slot >= 0 && slot < connectedSlots.Length && connectedSlots[slot];
        }

        public int ConnectedPlayerCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < connectedSlots.Length; i++) if (connectedSlots[i]) count++;
                return count;
            }
        }

        public string RoleLabel
        {
            get
            {
                if (Status == SessionHudStatus.Offline || Status == SessionHudStatus.Error) return "--";
                if (IsHost) return "HOST / slot 0";
                return LocalSlot >= 0 ? "CLIENT / slot " + LocalSlot : "CLIENT / pending";
            }
        }
    }
}
