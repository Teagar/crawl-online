namespace CrawlOnline.Protocol
{
    public enum HandshakeRejectReason : byte
    {
        None = 0,
        InvalidIdentity = 1,
        InvalidSession = 2,
        InvalidSlot = 3,
        LobbyFull = 4,
        StaleAttempt = 5,
        NotLobbyMember = 6,
        IncompatibleCapabilities = 7,
        IncompatibleGameBuild = 8
    }

    public static class SessionCapabilities
    {
        public const uint AuthoritativeSnapshots = 1;
        public const uint Reconnect = 2;
        public const uint Current = AuthoritativeSnapshots | Reconnect;
    }

    public struct SessionHello
    {
        public ulong SessionNonce;
        public ulong SenderId;
        public uint Attempt;
        public byte RequestedSlot;
        public uint Capabilities;
        public GameBuildFingerprint GameBuild;
    }

    public struct SessionAccepted
    {
        public ulong SessionNonce;
        public ulong HostId;
        public uint Attempt;
        public byte AssignedSlot;
        public byte MaxPlayers;
        public uint Capabilities;
        public GameBuildFingerprint GameBuild;
    }

    public struct SessionRejected
    {
        public ulong SessionNonce;
        public uint Attempt;
        public HandshakeRejectReason Reason;
    }
}
