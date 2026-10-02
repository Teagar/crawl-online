using System;

namespace CrawlOnline.Protocol
{
    [Flags]
    public enum PlayerSnapshotFlags : byte
    {
        None = 0,
        Active = 1,
        Hero = 2,
        Alive = 4,
        Bot = 8,
        Present = 16
    }

    public struct SessionInputFrame
    {
        public ulong SessionNonce;
        public uint Sequence;
        public InputFrame Input;
    }

    public struct PlayerSnapshot
    {
        public byte Slot;
        public PlayerSnapshotFlags Flags;
        public short State;
        public int PositionX;
        public int PositionY;
        public int VelocityX;
        public int VelocityY;
        public int HealthCurrent;
        public int HealthMaximum;
    }

    public sealed class WorldSnapshot
    {
        public ulong SessionNonce;
        public uint Sequence;
        public uint HostTick;
        public uint LastInputSequence;
        public int Level;
        public int RoomX;
        public int RoomY;
        public int RoomDepth;
        public uint TransitionGeneration;
        public ulong RandomStateHash;
        public PlayerSnapshot[] Players = new PlayerSnapshot[0];
    }

    public struct SnapshotAcknowledgement
    {
        public ulong SessionNonce;
        public uint Sequence;
    }
}
