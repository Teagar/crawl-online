using System;

namespace CrawlOnline.Protocol
{
    public static class AuthoritativeCodec
    {
        private const uint Magic = 0x434F4E4C;
        private const int SnapshotHeaderSize = 142;
        private const int PlayerSize = 28;
        private const int EnemySize = 40;
        private const int MaximumEnemies = 256;
        private const int MaximumRoomArchetypes = 512;
        private const int MaximumSpecialRoomArchetypes = 8;
        private const int MaximumRoomCollisionDecisions = 512;
        private const int MaximumRoomSelectionDecisions = 512;
        private const int MaximumRoomLayoutStates = 64;
        private const int RoomLayoutSize = 32;
        public const int SessionInputPacketSize = 30;
        public const int InputEventPacketSize = 25;
        public const int SnapshotAckPacketSize = 18;
        public const int PregameStatePacketSize = 19;
        public const int MaximumSnapshotPacketSize = SnapshotHeaderSize +
            (MaximumRoomArchetypes + MaximumSpecialRoomArchetypes) * 8 +
            MaximumRoomCollisionDecisions * 17 +
            MaximumRoomSelectionDecisions * 16 +
            MaximumRoomLayoutStates * RoomLayoutSize +
            PlayerSize * 4 + EnemySize * MaximumEnemies;

        public static byte[] EncodeInput(SessionInputFrame frame)
        {
            byte[] data = CreatePacket(PacketType.SessionInput, SessionInputPacketSize);
            WriteUInt64(data, 6, frame.SessionNonce);
            WriteUInt32(data, 14, frame.Sequence);
            data[18] = frame.Input.PlayerId;
            data[19] = frame.Input.HeldButtons;
            data[20] = frame.Input.DownButtons;
            data[21] = frame.Input.UpButtons;
            WriteUInt32(data, 22, frame.Input.Tick);
            WriteInt16(data, 26, frame.Input.MoveX);
            WriteInt16(data, 28, frame.Input.MoveY);
            return data;
        }

        public static bool TryDecodeInput(byte[] data, out SessionInputFrame frame)
        {
            frame = new SessionInputFrame();
            if (!HasHeader(data, PacketType.SessionInput, SessionInputPacketSize)) return false;
            frame.SessionNonce = ReadUInt64(data, 6);
            frame.Sequence = ReadUInt32(data, 14);
            frame.Input.PlayerId = data[18];
            frame.Input.HeldButtons = data[19];
            frame.Input.DownButtons = data[20];
            frame.Input.UpButtons = data[21];
            frame.Input.Tick = ReadUInt32(data, 22);
            frame.Input.MoveX = ReadInt16(data, 26);
            frame.Input.MoveY = ReadInt16(data, 28);
            return true;
        }

        public static byte[] EncodeInputEvent(SessionInputFrame frame)
        {
            byte[] data = CreatePacket(PacketType.InputEvent, InputEventPacketSize);
            WriteUInt64(data, 6, frame.SessionNonce);
            WriteUInt32(data, 14, frame.Sequence);
            data[18] = frame.Input.PlayerId;
            data[19] = frame.Input.DownButtons;
            data[20] = frame.Input.UpButtons;
            WriteUInt32(data, 21, frame.Input.Tick);
            return data;
        }

        public static bool TryDecodeInputEvent(byte[] data, out SessionInputFrame frame)
        {
            frame = new SessionInputFrame();
            if (!HasHeader(data, PacketType.InputEvent, InputEventPacketSize)) return false;
            frame.SessionNonce = ReadUInt64(data, 6);
            frame.Sequence = ReadUInt32(data, 14);
            frame.Input.PlayerId = data[18];
            frame.Input.DownButtons = data[19];
            frame.Input.UpButtons = data[20];
            frame.Input.Tick = ReadUInt32(data, 21);
            return frame.Input.DownButtons != 0 || frame.Input.UpButtons != 0;
        }

        public static byte[] EncodeSnapshot(WorldSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            if (snapshot.Players == null || snapshot.Players.Length > 4)
                throw new ArgumentOutOfRangeException("snapshot", "Snapshots support zero to four player slots.");
            if (snapshot.LastInputSequences == null || snapshot.LastInputSequences.Length != 4)
                throw new ArgumentException("Snapshots require one input sequence per player slot.", "snapshot");
            if (snapshot.Enemies == null || snapshot.Enemies.Length > MaximumEnemies)
                throw new ArgumentOutOfRangeException("snapshot", "Snapshots support at most 256 active enemies.");
            if (snapshot.RandomStateWords == null || snapshot.RandomStateWords.Length != 4)
                throw new ArgumentException("Snapshots require the four Unity random-state words.", "snapshot");
            if (snapshot.GameStartRandomStateWords == null || snapshot.GameStartRandomStateWords.Length != 4)
                throw new ArgumentException("Snapshots require the four game-start random-state words.", "snapshot");
            if (snapshot.RoomContentRandomStateWords == null || snapshot.RoomContentRandomStateWords.Length != 4)
                throw new ArgumentException("Snapshots require the four room-content random-state words.", "snapshot");
            if (snapshot.RoomArchetypeHashes == null || snapshot.RoomArchetypeHashes.Length > MaximumRoomArchetypes)
                throw new ArgumentOutOfRangeException("snapshot", "Snapshots support at most 512 room selections.");
            for (int i = 0; i < snapshot.RoomArchetypeHashes.Length; i++)
                if (snapshot.RoomArchetypeHashes[i] == 0)
                    throw new ArgumentException("Room archetype hashes must be non-zero.", "snapshot");
            if (snapshot.SpecialRoomArchetypeHashes == null ||
                snapshot.SpecialRoomArchetypeHashes.Length > MaximumSpecialRoomArchetypes)
                throw new ArgumentOutOfRangeException("snapshot", "Snapshots support at most 8 special rooms.");
            for (int i = 0; i < snapshot.SpecialRoomArchetypeHashes.Length; i++)
                if (snapshot.SpecialRoomArchetypeHashes[i] == 0)
                    throw new ArgumentException("Special room archetype hashes must be non-zero.", "snapshot");
            if (snapshot.RoomCollisionDecisions == null ||
                snapshot.RoomCollisionDecisions.Length > MaximumRoomCollisionDecisions)
                throw new ArgumentOutOfRangeException("snapshot", "Snapshots support at most 512 room collisions.");
            for (int i = 0; i < snapshot.RoomCollisionDecisions.Length; i++)
                if (snapshot.RoomCollisionDecisions[i].Key == 0 ||
                    snapshot.RoomCollisionDecisions[i].ArchetypeHash == 0)
                    throw new ArgumentException("Room collision hashes must be non-zero.", "snapshot");
            if (snapshot.RoomSelectionDecisions == null ||
                snapshot.RoomSelectionDecisions.Length > MaximumRoomSelectionDecisions)
                throw new ArgumentOutOfRangeException("snapshot", "Snapshots support at most 512 keyed room selections.");
            for (int i = 0; i < snapshot.RoomSelectionDecisions.Length; i++)
                if (snapshot.RoomSelectionDecisions[i].Key == 0 ||
                    snapshot.RoomSelectionDecisions[i].ArchetypeHash == 0)
                    throw new ArgumentException("Keyed room selection hashes must be non-zero.", "snapshot");
            if (snapshot.RoomLayout == null || snapshot.RoomLayout.Length > MaximumRoomLayoutStates)
                throw new ArgumentOutOfRangeException("snapshot", "Snapshots support at most 64 room layouts.");
            for (int i = 0; i < snapshot.RoomLayout.Length; i++)
                if (snapshot.RoomLayout[i].ArchetypeHash == 0 || snapshot.RoomLayout[i].ConnectionHash == 0)
                    throw new ArgumentException("Room layout hashes must be non-zero.", "snapshot");
            ValidatePlayerOrder(snapshot.Players);
            ValidateEnemyOrder(snapshot.Enemies);

            byte[] data = CreatePacket(PacketType.Snapshot,
                SnapshotHeaderSize + (snapshot.RoomArchetypeHashes.Length +
                snapshot.SpecialRoomArchetypeHashes.Length) * 8 +
                snapshot.RoomCollisionDecisions.Length * 17 +
                snapshot.RoomSelectionDecisions.Length * 16 +
                snapshot.RoomLayout.Length * RoomLayoutSize +
                snapshot.Players.Length * PlayerSize + snapshot.Enemies.Length * EnemySize);
            WriteUInt64(data, 6, snapshot.SessionNonce);
            WriteUInt32(data, 14, snapshot.Sequence);
            WriteUInt32(data, 18, snapshot.HostTick);
            for (int i = 0; i < 4; i++) WriteUInt32(data, 22 + i * 4, snapshot.LastInputSequences[i]);
            data[38] = (byte)snapshot.Flags;
            WriteInt32(data, 39, snapshot.Level);
            WriteInt32(data, 43, snapshot.RoomX);
            WriteInt32(data, 47, snapshot.RoomY);
            WriteInt32(data, 51, snapshot.RoomDepth);
            WriteUInt32(data, 55, snapshot.TransitionGeneration);
            WriteUInt64(data, 59, snapshot.RandomStateHash);
            for (int i = 0; i < 4; i++) WriteUInt32(data, 67 + i * 4, snapshot.RandomStateWords[i]);
            WriteUInt32(data, 83, snapshot.GameStartGeneration);
            for (int i = 0; i < 4; i++) WriteUInt32(data, 87 + i * 4, snapshot.GameStartRandomStateWords[i]);
            WriteInt32(data, 103, snapshot.RoomIndex);
            data[107] = (byte)snapshot.Players.Length;
            WriteUInt16(data, 108, (ushort)snapshot.Enemies.Length);
            WriteUInt16(data, 110, (ushort)snapshot.RoomArchetypeHashes.Length);
            data[112] = (byte)snapshot.SpecialRoomArchetypeHashes.Length;
            WriteUInt16(data, 113, (ushort)snapshot.RoomCollisionDecisions.Length);
            data[115] = (byte)snapshot.RoomLayout.Length;
            WriteUInt16(data, 116, (ushort)snapshot.RoomSelectionDecisions.Length);
            WriteUInt32(data, 118, snapshot.RoomContentGeneration);
            WriteInt32(data, 122, snapshot.RoomContentRoomIndex);
            for (int i = 0; i < 4; i++) WriteUInt32(data, 126 + i * 4,
                snapshot.RoomContentRandomStateWords[i]);
            int offset = SnapshotHeaderSize;
            for (int i = 0; i < snapshot.RoomArchetypeHashes.Length; i++)
            {
                WriteUInt64(data, offset, snapshot.RoomArchetypeHashes[i]);
                offset += 8;
            }
            for (int i = 0; i < snapshot.SpecialRoomArchetypeHashes.Length; i++)
            {
                WriteUInt64(data, offset, snapshot.SpecialRoomArchetypeHashes[i]);
                offset += 8;
            }
            for (int i = 0; i < snapshot.RoomCollisionDecisions.Length; i++)
            {
                WriteUInt64(data, offset, snapshot.RoomCollisionDecisions[i].Key);
                WriteUInt64(data, offset + 8, snapshot.RoomCollisionDecisions[i].ArchetypeHash);
                data[offset + 16] = snapshot.RoomCollisionDecisions[i].Collided ? (byte)1 : (byte)0;
                offset += 17;
            }
            for (int i = 0; i < snapshot.RoomSelectionDecisions.Length; i++)
            {
                WriteUInt64(data, offset, snapshot.RoomSelectionDecisions[i].Key);
                WriteUInt64(data, offset + 8, snapshot.RoomSelectionDecisions[i].ArchetypeHash);
                offset += 16;
            }
            for (int i = 0; i < snapshot.RoomLayout.Length; i++)
            {
                RoomLayoutState room = snapshot.RoomLayout[i];
                WriteUInt64(data, offset, room.ArchetypeHash);
                WriteUInt64(data, offset + 8, room.ConnectionHash);
                WriteInt32(data, offset + 16, room.Depth);
                WriteInt32(data, offset + 20, room.PositionX);
                WriteInt32(data, offset + 24, room.PositionY);
                WriteInt32(data, offset + 28, room.RotationZ);
                offset += RoomLayoutSize;
            }
            for (int i = 0; i < snapshot.Players.Length; i++)
            {
                PlayerSnapshot player = snapshot.Players[i];
                data[offset] = player.Slot;
                data[offset + 1] = (byte)player.Flags;
                WriteInt16(data, offset + 2, player.State);
                WriteInt32(data, offset + 4, player.PositionX);
                WriteInt32(data, offset + 8, player.PositionY);
                WriteInt32(data, offset + 12, player.VelocityX);
                WriteInt32(data, offset + 16, player.VelocityY);
                WriteInt32(data, offset + 20, player.HealthCurrent);
                WriteInt32(data, offset + 24, player.HealthMaximum);
                offset += PlayerSize;
            }
            for (int i = 0; i < snapshot.Enemies.Length; i++)
            {
                EnemySnapshot enemy = snapshot.Enemies[i];
                WriteUInt32(data, offset, enemy.Id);
                WriteUInt64(data, offset + 4, enemy.ArchetypeHash);
                data[offset + 12] = enemy.OwnerSlot;
                data[offset + 13] = (byte)enemy.Flags;
                WriteInt16(data, offset + 14, enemy.State);
                WriteInt32(data, offset + 16, enemy.PositionX);
                WriteInt32(data, offset + 20, enemy.PositionY);
                WriteInt32(data, offset + 24, enemy.VelocityX);
                WriteInt32(data, offset + 28, enemy.VelocityY);
                WriteInt32(data, offset + 32, enemy.HealthCurrent);
                WriteInt32(data, offset + 36, enemy.HealthMaximum);
                offset += EnemySize;
            }
            return data;
        }

        public static bool TryDecodeSnapshot(byte[] data, out WorldSnapshot snapshot)
        {
            snapshot = null;
            if (data == null || data.Length < SnapshotHeaderSize ||
                !HasHeaderPrefix(data, PacketType.Snapshot)) return false;
            int count = data[107];
            int enemyCount = ReadUInt16(data, 108);
            int roomCount = ReadUInt16(data, 110);
            int specialRoomCount = data[112];
            int collisionCount = ReadUInt16(data, 113);
            int roomLayoutCount = data[115];
            int keyedSelectionCount = ReadUInt16(data, 116);
            if (count > 4 || enemyCount > MaximumEnemies || roomCount > MaximumRoomArchetypes ||
                specialRoomCount > MaximumSpecialRoomArchetypes ||
                collisionCount > MaximumRoomCollisionDecisions ||
                roomLayoutCount > MaximumRoomLayoutStates ||
                keyedSelectionCount > MaximumRoomSelectionDecisions ||
                data.Length != SnapshotHeaderSize + (roomCount + specialRoomCount) * 8 +
                    collisionCount * 17 +
                    keyedSelectionCount * 16 +
                    roomLayoutCount * RoomLayoutSize +
                    count * PlayerSize + enemyCount * EnemySize ||
                (data[38] & ~(byte)(WorldSnapshotFlags.GameInProgress | WorldSnapshotFlags.HasCurrentRoom)) != 0)
                return false;

            var decoded = new WorldSnapshot
            {
                SessionNonce = ReadUInt64(data, 6),
                Sequence = ReadUInt32(data, 14),
                HostTick = ReadUInt32(data, 18),
                LastInputSequences = new uint[4],
                Flags = (WorldSnapshotFlags)data[38],
                Level = ReadInt32(data, 39),
                RoomX = ReadInt32(data, 43),
                RoomY = ReadInt32(data, 47),
                RoomDepth = ReadInt32(data, 51),
                TransitionGeneration = ReadUInt32(data, 55),
                RandomStateHash = ReadUInt64(data, 59),
                RandomStateWords = new uint[4],
                GameStartGeneration = ReadUInt32(data, 83),
                GameStartRandomStateWords = new uint[4],
                RoomContentGeneration = ReadUInt32(data, 118),
                RoomContentRoomIndex = ReadInt32(data, 122),
                RoomContentRandomStateWords = new uint[4],
                RoomIndex = ReadInt32(data, 103),
                RoomArchetypeHashes = new ulong[roomCount],
                SpecialRoomArchetypeHashes = new ulong[specialRoomCount],
                RoomCollisionDecisions = new RoomCollisionDecision[collisionCount],
                RoomSelectionDecisions = new RoomSelectionDecision[keyedSelectionCount],
                RoomLayout = new RoomLayoutState[roomLayoutCount],
                Players = new PlayerSnapshot[count],
                Enemies = new EnemySnapshot[enemyCount]
            };
            for (int i = 0; i < 4; i++) decoded.LastInputSequences[i] = ReadUInt32(data, 22 + i * 4);
            for (int i = 0; i < 4; i++) decoded.RandomStateWords[i] = ReadUInt32(data, 67 + i * 4);
            for (int i = 0; i < 4; i++) decoded.GameStartRandomStateWords[i] = ReadUInt32(data, 87 + i * 4);
            for (int i = 0; i < 4; i++) decoded.RoomContentRandomStateWords[i] = ReadUInt32(data, 126 + i * 4);
            int offset = SnapshotHeaderSize;
            for (int i = 0; i < roomCount; i++)
            {
                decoded.RoomArchetypeHashes[i] = ReadUInt64(data, offset);
                if (decoded.RoomArchetypeHashes[i] == 0) return false;
                offset += 8;
            }
            for (int i = 0; i < specialRoomCount; i++)
            {
                decoded.SpecialRoomArchetypeHashes[i] = ReadUInt64(data, offset);
                if (decoded.SpecialRoomArchetypeHashes[i] == 0) return false;
                offset += 8;
            }
            for (int i = 0; i < collisionCount; i++)
            {
                ulong key = ReadUInt64(data, offset);
                ulong archetypeHash = ReadUInt64(data, offset + 8);
                byte collided = data[offset + 16];
                if (key == 0 || archetypeHash == 0 || collided > 1) return false;
                decoded.RoomCollisionDecisions[i] = new RoomCollisionDecision
                {
                    Key = key,
                    ArchetypeHash = archetypeHash,
                    Collided = collided != 0
                };
                offset += 17;
            }
            for (int i = 0; i < keyedSelectionCount; i++)
            {
                ulong key = ReadUInt64(data, offset);
                ulong archetypeHash = ReadUInt64(data, offset + 8);
                if (key == 0 || archetypeHash == 0) return false;
                decoded.RoomSelectionDecisions[i] = new RoomSelectionDecision
                {
                    Key = key,
                    ArchetypeHash = archetypeHash
                };
                offset += 16;
            }
            for (int i = 0; i < roomLayoutCount; i++)
            {
                ulong archetypeHash = ReadUInt64(data, offset);
                ulong connectionHash = ReadUInt64(data, offset + 8);
                if (archetypeHash == 0 || connectionHash == 0) return false;
                decoded.RoomLayout[i] = new RoomLayoutState
                {
                    ArchetypeHash = archetypeHash,
                    ConnectionHash = connectionHash,
                    Depth = ReadInt32(data, offset + 16),
                    PositionX = ReadInt32(data, offset + 20),
                    PositionY = ReadInt32(data, offset + 24),
                    RotationZ = ReadInt32(data, offset + 28)
                };
                offset += RoomLayoutSize;
            }
            byte previousSlot = 0;
            int heroCount = 0;
            for (int i = 0; i < count; i++)
            {
                PlayerSnapshot player = new PlayerSnapshot
                {
                    Slot = data[offset],
                    Flags = (PlayerSnapshotFlags)data[offset + 1],
                    State = ReadInt16(data, offset + 2),
                    PositionX = ReadInt32(data, offset + 4),
                    PositionY = ReadInt32(data, offset + 8),
                    VelocityX = ReadInt32(data, offset + 12),
                    VelocityY = ReadInt32(data, offset + 16),
                    HealthCurrent = ReadInt32(data, offset + 20),
                    HealthMaximum = ReadInt32(data, offset + 24)
                };
                const PlayerSnapshotFlags KnownPlayerFlags = PlayerSnapshotFlags.Active | PlayerSnapshotFlags.Hero |
                    PlayerSnapshotFlags.Alive | PlayerSnapshotFlags.Bot | PlayerSnapshotFlags.Present;
                if (player.Slot > 3 || (player.Flags & ~KnownPlayerFlags) != 0 ||
                    (i > 0 && player.Slot <= previousSlot)) return false;
                if ((player.Flags & PlayerSnapshotFlags.Hero) != 0 && ++heroCount > 1) return false;
                decoded.Players[i] = player;
                previousSlot = player.Slot;
                offset += PlayerSize;
            }
            uint previousEnemyId = 0;
            for (int i = 0; i < enemyCount; i++)
            {
                EnemySnapshot enemy = new EnemySnapshot
                {
                    Id = ReadUInt32(data, offset),
                    ArchetypeHash = ReadUInt64(data, offset + 4),
                    OwnerSlot = data[offset + 12],
                    Flags = (EnemySnapshotFlags)data[offset + 13],
                    State = ReadInt16(data, offset + 14),
                    PositionX = ReadInt32(data, offset + 16),
                    PositionY = ReadInt32(data, offset + 20),
                    VelocityX = ReadInt32(data, offset + 24),
                    VelocityY = ReadInt32(data, offset + 28),
                    HealthCurrent = ReadInt32(data, offset + 32),
                    HealthMaximum = ReadInt32(data, offset + 36)
                };
                const EnemySnapshotFlags KnownEnemyFlags = EnemySnapshotFlags.Active |
                    EnemySnapshotFlags.Alive | EnemySnapshotFlags.AiControlled;
                if (enemy.Id == 0 || enemy.ArchetypeHash == 0 ||
                    (enemy.OwnerSlot != byte.MaxValue && enemy.OwnerSlot > 3) ||
                    (enemy.Flags & ~KnownEnemyFlags) != 0 ||
                    (i > 0 && enemy.Id <= previousEnemyId)) return false;
                decoded.Enemies[i] = enemy;
                previousEnemyId = enemy.Id;
                offset += EnemySize;
            }
            snapshot = decoded;
            return true;
        }

        public static byte[] EncodeAcknowledgement(SnapshotAcknowledgement acknowledgement)
        {
            byte[] data = CreatePacket(PacketType.SnapshotAck, SnapshotAckPacketSize);
            WriteUInt64(data, 6, acknowledgement.SessionNonce);
            WriteUInt32(data, 14, acknowledgement.Sequence);
            return data;
        }

        public static bool TryDecodeAcknowledgement(byte[] data, out SnapshotAcknowledgement acknowledgement)
        {
            acknowledgement = new SnapshotAcknowledgement();
            if (!HasHeader(data, PacketType.SnapshotAck, SnapshotAckPacketSize)) return false;
            acknowledgement.SessionNonce = ReadUInt64(data, 6);
            acknowledgement.Sequence = ReadUInt32(data, 14);
            return true;
        }

        public static byte[] EncodePregameState(SessionPregameState state)
        {
            byte[] data = CreatePacket(PacketType.PregameState, PregameStatePacketSize);
            WriteUInt64(data, 6, state.SessionNonce);
            WriteUInt32(data, 14, state.Sequence);
            data[18] = (byte)state.Phase;
            return data;
        }

        public static bool TryDecodePregameState(byte[] data, out SessionPregameState state)
        {
            state = new SessionPregameState();
            if (!HasHeader(data, PacketType.PregameState, PregameStatePacketSize)) return false;
            state.SessionNonce = ReadUInt64(data, 6);
            state.Sequence = ReadUInt32(data, 14);
            state.Phase = (PregamePhase)data[18];
            return Enum.IsDefined(typeof(PregamePhase), state.Phase);
        }

        private static void ValidatePlayerOrder(PlayerSnapshot[] players)
        {
            int heroCount = 0;
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i].Slot > 3 || (i > 0 && players[i].Slot <= players[i - 1].Slot))
                    throw new ArgumentException("Player snapshots must be sorted by unique slot.", "players");
                if ((players[i].Flags & PlayerSnapshotFlags.Hero) != 0 && ++heroCount > 1)
                    throw new ArgumentException("A snapshot cannot contain more than one hero.", "players");
            }
        }

        private static void ValidateEnemyOrder(EnemySnapshot[] enemies)
        {
            for (int i = 0; i < enemies.Length; i++)
            {
                if (enemies[i].Id == 0 || enemies[i].ArchetypeHash == 0 ||
                    (enemies[i].OwnerSlot != byte.MaxValue && enemies[i].OwnerSlot > 3) ||
                    (i > 0 && enemies[i].Id <= enemies[i - 1].Id))
                    throw new ArgumentException("Enemy snapshots must be sorted by unique non-zero ID and have an archetype.", "enemies");
            }
        }

        private static byte[] CreatePacket(PacketType type, int size)
        {
            byte[] data = new byte[size];
            WriteUInt32(data, 0, Magic);
            data[4] = PacketCodec.ProtocolVersion;
            data[5] = (byte)type;
            return data;
        }

        private static bool HasHeader(byte[] data, PacketType type, int size)
        {
            return data != null && data.Length == size && HasHeaderPrefix(data, type);
        }

        private static bool HasHeaderPrefix(byte[] data, PacketType type)
        {
            return data.Length >= 6 && ReadUInt32(data, 0) == Magic &&
                   data[4] == PacketCodec.ProtocolVersion && data[5] == (byte)type;
        }

        private static void WriteInt16(byte[] data, int offset, short value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
        }

        private static short ReadInt16(byte[] data, int offset)
        {
            return (short)(data[offset] | data[offset + 1] << 8);
        }

        private static void WriteUInt16(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | data[offset + 1] << 8);
        }

        private static void WriteInt32(byte[] data, int offset, int value)
        {
            unchecked { WriteUInt32(data, offset, (uint)value); }
        }

        private static int ReadInt32(byte[] data, int offset)
        {
            unchecked { return (int)ReadUInt32(data, offset); }
        }

        private static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset] | data[offset + 1] << 8 |
                          data[offset + 2] << 16 | data[offset + 3] << 24);
        }

        private static void WriteUInt64(byte[] data, int offset, ulong value)
        {
            for (int i = 0; i < 8; i++) data[offset + i] = (byte)(value >> (i * 8));
        }

        private static ulong ReadUInt64(byte[] data, int offset)
        {
            ulong value = 0;
            for (int i = 0; i < 8; i++) value |= (ulong)data[offset + i] << (i * 8);
            return value;
        }
    }
}
