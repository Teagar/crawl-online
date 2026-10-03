using System;
using CrawlOnline.Protocol;

namespace CrawlOnline.Online
{
    internal interface IOnlineSession : IDisposable
    {
        event Action<SessionInputFrame> InputReceived;
        event Action<SessionInputFrame> InputEventReceived;
        event Action<WorldSnapshot> SnapshotReceived;
        event Action<long, ulong[]> FriendLobbiesDiscovered;
        event Action<ulong> LobbyJoinRequested;

        bool InLobby { get; }
        bool IsAuthoritativeHost { get; }
        byte LocalSlot { get; }
        ulong SessionNonce { get; }
        byte[] GetConnectedPeerSlots();
        SessionHudState GetHudState();
        void Host();
        void CancelHostOrLeave();
        void OpenInviteDialog();
        void DiscoverFriendLobbies(long operation);
        void JoinDiscoveredLobby(ulong lobbyId, long operation);
        void CancelJoinOrLeave();
        void Leave();
        void Poll();
        bool SendLocalInput(InputFrame input);
        bool BroadcastSnapshot(WorldSnapshot snapshot);
        bool AcknowledgeSnapshot(uint sequence);
    }
}
