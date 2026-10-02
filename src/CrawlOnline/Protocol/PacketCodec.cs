using System;

namespace CrawlOnline.Protocol
{
    public static class PacketCodec
    {
        private const uint Magic = 0x434F4E4C; // "CONL"
        public const byte ProtocolVersion = 1;
        public const int InputPacketSize = 16;

        public static byte[] EncodeInput(InputFrame frame)
        {
            byte[] data = new byte[InputPacketSize];
            WriteUInt32(data, 0, Magic);
            data[4] = ProtocolVersion;
            data[5] = (byte)PacketType.Input;
            data[6] = frame.PlayerId;
            data[7] = frame.Buttons;
            WriteUInt32(data, 8, frame.Tick);
            WriteInt16(data, 12, frame.MoveX);
            WriteInt16(data, 14, frame.MoveY);
            return data;
        }

        public static bool TryDecodeInput(byte[] data, out InputFrame frame)
        {
            frame = new InputFrame();
            if (data == null || data.Length != InputPacketSize ||
                ReadUInt32(data, 0) != Magic ||
                data[4] != ProtocolVersion ||
                data[5] != (byte)PacketType.Input)
            {
                return false;
            }

            frame.PlayerId = data[6];
            frame.Buttons = data[7];
            frame.Tick = ReadUInt32(data, 8);
            frame.MoveX = ReadInt16(data, 12);
            frame.MoveY = ReadInt16(data, 14);
            return true;
        }

        public static byte[] EncodeControl(PacketType type)
        {
            if (type == PacketType.Input)
            {
                throw new ArgumentException("Input packets require an InputFrame.", "type");
            }

            byte[] data = new byte[6];
            WriteUInt32(data, 0, Magic);
            data[4] = ProtocolVersion;
            data[5] = (byte)type;
            return data;
        }

        public static bool TryReadType(byte[] data, out PacketType type)
        {
            type = 0;
            if (data == null || data.Length < 6 || ReadUInt32(data, 0) != Magic || data[4] != ProtocolVersion)
            {
                return false;
            }

            type = (PacketType)data[5];
            return Enum.IsDefined(typeof(PacketType), type);
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

        private static void WriteInt16(byte[] data, int offset, short value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
        }

        private static short ReadInt16(byte[] data, int offset)
        {
            return (short)(data[offset] | data[offset + 1] << 8);
        }
    }
}
