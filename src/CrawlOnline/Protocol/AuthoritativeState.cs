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

    [Flags]
    public enum WorldSnapshotFlags : byte
    {
        None = 0,
        GameInProgress = 1,
        HasCurrentRoom = 2
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

    [Flags]
    public enum EnemySnapshotFlags : byte
    {
        None = 0,
        Active = 1,
        Alive = 2,
        AiControlled = 4
    }

    public struct EnemySnapshot
    {
        public uint Id;
        public ulong ArchetypeHash;
        public EnemySnapshotFlags Flags;
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
        public uint[] LastInputSequences = new uint[4];
        public WorldSnapshotFlags Flags;
        public int Level;
        public int RoomX;
        public int RoomY;
        public int RoomDepth;
        public uint TransitionGeneration;
        public ulong RandomStateHash;
        public uint[] RandomStateWords = new uint[4];
        public PlayerSnapshot[] Players = new PlayerSnapshot[0];
        public EnemySnapshot[] Enemies = new EnemySnapshot[0];
    }

    public struct SnapshotAcknowledgement
    {
        public ulong SessionNonce;
        public uint Sequence;
    }
}
