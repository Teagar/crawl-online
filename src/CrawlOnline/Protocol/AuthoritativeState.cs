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
        public byte OwnerSlot;
        public EnemySnapshotFlags Flags;
        public short State;
        public int PositionX;
        public int PositionY;
        public int VelocityX;
        public int VelocityY;
        public int HealthCurrent;
        public int HealthMaximum;
    }

    public struct RoomCollisionDecision
    {
        public ulong Key;
        public ulong ArchetypeHash;
        public bool Collided;
    }

    public struct RoomSelectionDecision
    {
        public ulong Key;
        public ulong ArchetypeHash;
    }

    public struct RoomLayoutState
    {
        public ulong ArchetypeHash;
        public ulong ConnectionHash;
        public int Depth;
        public int PositionX;
        public int PositionY;
        public int RotationZ;
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
        public int RoomIndex = -1;
        public uint TransitionGeneration;
        public ulong RandomStateHash;
        public uint[] RandomStateWords = new uint[4];
        public uint GameStartGeneration;
        public uint[] GameStartRandomStateWords = new uint[4];
        public uint RoomContentGeneration;
        public int RoomContentRoomIndex = -1;
        public uint[] RoomContentRandomStateWords = new uint[4];
        public ulong[] RoomArchetypeHashes = new ulong[0];
        public ulong[] SpecialRoomArchetypeHashes = new ulong[0];
        public RoomCollisionDecision[] RoomCollisionDecisions = new RoomCollisionDecision[0];
        public RoomSelectionDecision[] RoomSelectionDecisions = new RoomSelectionDecision[0];
        public RoomLayoutState[] RoomLayout = new RoomLayoutState[0];
        public PlayerSnapshot[] Players = new PlayerSnapshot[0];
        public EnemySnapshot[] Enemies = new EnemySnapshot[0];
    }

    public struct SnapshotAcknowledgement
    {
        public ulong SessionNonce;
        public uint Sequence;
    }
}
