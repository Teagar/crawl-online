using System;

namespace CrawlOnline.Protocol
{
    public struct TraceHeader
    {
        public int RandomSeed;
        public ushort FixedTicksPerSecond;
        public byte PlayerCount;
        public uint StateHashInterval;
    }

    public struct StateHashRecord
    {
        public uint Tick;
        public ulong Hash;
    }

    public static class TraceCodec
    {
        private const uint Magic = 0x52544F43; // "COTR"
        private const byte Version = 1;
        private const byte StateHashRecordType = 2;
        public const int HeaderSize = 16;
        public const int StateHashRecordSize = 13;

        public static byte[] EncodeHeader(TraceHeader header)
        {
            if (header.PlayerCount < 1 || header.PlayerCount > 4)
            {
                throw new ArgumentOutOfRangeException("header", "PlayerCount must be between 1 and 4.");
            }
            if (header.FixedTicksPerSecond == 0 || header.StateHashInterval == 0)
            {
                throw new ArgumentOutOfRangeException("header", "Tick rate and hash interval must be non-zero.");
            }

            byte[] data = new byte[HeaderSize];
            WriteUInt32(data, 0, Magic);
            data[4] = Version;
            data[5] = header.PlayerCount;
            WriteUInt16(data, 6, header.FixedTicksPerSecond);
            WriteUInt32(data, 8, unchecked((uint)header.RandomSeed));
            WriteUInt32(data, 12, header.StateHashInterval);
            return data;
        }

        public static bool TryDecodeHeader(byte[] data, out TraceHeader header)
        {
            header = new TraceHeader();
            if (data == null || data.Length != HeaderSize || ReadUInt32(data, 0) != Magic || data[4] != Version)
            {
                return false;
            }

            header.PlayerCount = data[5];
            header.FixedTicksPerSecond = ReadUInt16(data, 6);
            header.RandomSeed = unchecked((int)ReadUInt32(data, 8));
            header.StateHashInterval = ReadUInt32(data, 12);
            return header.PlayerCount >= 1 && header.PlayerCount <= 4 &&
                   header.FixedTicksPerSecond != 0 && header.StateHashInterval != 0;
        }

        public static byte[] EncodeStateHash(StateHashRecord record)
        {
            byte[] data = new byte[StateHashRecordSize];
            data[0] = StateHashRecordType;
            WriteUInt32(data, 1, record.Tick);
            WriteUInt64(data, 5, record.Hash);
            return data;
        }

        public static bool TryDecodeStateHash(byte[] data, out StateHashRecord record)
        {
            record = new StateHashRecord();
            if (data == null || data.Length != StateHashRecordSize || data[0] != StateHashRecordType)
            {
                return false;
            }

            record.Tick = ReadUInt32(data, 1);
            record.Hash = ReadUInt64(data, 5);
            return true;
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

        private static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset] |
                          data[offset + 1] << 8 |
                          data[offset + 2] << 16 |
                          data[offset + 3] << 24);
        }

        private static void WriteUInt64(byte[] data, int offset, ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                data[offset + i] = (byte)(value >> (i * 8));
            }
        }

        private static ulong ReadUInt64(byte[] data, int offset)
        {
            ulong value = 0;
            for (int i = 0; i < 8; i++)
            {
                value |= (ulong)data[offset + i] << (i * 8);
            }

            return value;
        }
    }
}
