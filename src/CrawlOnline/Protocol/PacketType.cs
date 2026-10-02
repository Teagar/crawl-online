namespace CrawlOnline.Protocol
{
    public enum PacketType : byte
    {
        Hello = 1,
        HelloAccepted = 2,
        Input = 3,
        StateHash = 4,
        Snapshot = 5,
        Disconnect = 6,
        HelloRejected = 7,
        SessionInput = 8,
        SnapshotAck = 9
    }
}
