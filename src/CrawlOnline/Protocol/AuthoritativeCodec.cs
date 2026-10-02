using System;

namespace CrawlOnline.Protocol
{
    public static class AuthoritativeCodec
    {
        private const uint Magic = 0x434F4E4C;
        private const int SnapshotHeaderSize = 55;
        private const int PlayerSize = 28;
        public const int SessionInputPacketSize = 30;
        public const int SnapshotAckPacketSize = 18;
        public const int MaximumSnapshotPacketSize = SnapshotHeaderSize + PlayerSize * 4;

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

        public static byte[] EncodeSnapshot(WorldSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            if (snapshot.Players == null || snapshot.Players.Length > 4)
                throw new ArgumentOutOfRangeException("snapshot", "Snapshots support zero to four player slots.");
            ValidatePlayerOrder(snapshot.Players);

            byte[] data = CreatePacket(PacketType.Snapshot,
                SnapshotHeaderSize + snapshot.Players.Length * PlayerSize);
            WriteUInt64(data, 6, snapshot.SessionNonce);
            WriteUInt32(data, 14, snapshot.Sequence);
            WriteUInt32(data, 18, snapshot.HostTick);
            WriteUInt32(data, 22, snapshot.LastInputSequence);
            WriteInt32(data, 26, snapshot.Level);
            WriteInt32(data, 30, snapshot.RoomX);
            WriteInt32(data, 34, snapshot.RoomY);
            WriteInt32(data, 38, snapshot.RoomDepth);
            WriteUInt32(data, 42, snapshot.TransitionGeneration);
            WriteUInt64(data, 46, snapshot.RandomStateHash);
            data[54] = (byte)snapshot.Players.Length;
            int offset = SnapshotHeaderSize;
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
            return data;
        }

        public static bool TryDecodeSnapshot(byte[] data, out WorldSnapshot snapshot)
        {
            snapshot = null;
            if (data == null || data.Length < SnapshotHeaderSize ||
                !HasHeaderPrefix(data, PacketType.Snapshot)) return false;
            int count = data[54];
            if (count > 4 || data.Length != SnapshotHeaderSize + count * PlayerSize) return false;

            var decoded = new WorldSnapshot
            {
                SessionNonce = ReadUInt64(data, 6),
                Sequence = ReadUInt32(data, 14),
                HostTick = ReadUInt32(data, 18),
                LastInputSequence = ReadUInt32(data, 22),
                Level = ReadInt32(data, 26),
                RoomX = ReadInt32(data, 30),
                RoomY = ReadInt32(data, 34),
                RoomDepth = ReadInt32(data, 38),
                TransitionGeneration = ReadUInt32(data, 42),
                RandomStateHash = ReadUInt64(data, 46),
                Players = new PlayerSnapshot[count]
            };
            int offset = SnapshotHeaderSize;
            byte previousSlot = 0;
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
                if (player.Slot > 3 || (i > 0 && player.Slot <= previousSlot)) return false;
                decoded.Players[i] = player;
                previousSlot = player.Slot;
                offset += PlayerSize;
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

        private static void ValidatePlayerOrder(PlayerSnapshot[] players)
        {
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i].Slot > 3 || (i > 0 && players[i].Slot <= players[i - 1].Slot))
                    throw new ArgumentException("Player snapshots must be sorted by unique slot.", "players");
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
