using System;

namespace CrawlOnline.Protocol
{
    public static class PacketCodec
    {
        private const uint Magic = 0x434F4E4C; // "CONL"
        public const byte ProtocolVersion = 2;
        public const int InputPacketSize = 18;
        public const int HelloPacketSize = 31;
        public const int AcceptedPacketSize = 32;
        public const int RejectedPacketSize = 19;

        public static byte[] EncodeInput(InputFrame frame)
        {
            byte[] data = new byte[InputPacketSize];
            WriteUInt32(data, 0, Magic);
            data[4] = ProtocolVersion;
            data[5] = (byte)PacketType.Input;
            data[6] = frame.PlayerId;
            data[7] = frame.HeldButtons;
            data[8] = frame.DownButtons;
            data[9] = frame.UpButtons;
            WriteUInt32(data, 10, frame.Tick);
            WriteInt16(data, 14, frame.MoveX);
            WriteInt16(data, 16, frame.MoveY);
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
            frame.HeldButtons = data[7];
            frame.DownButtons = data[8];
            frame.UpButtons = data[9];
            frame.Tick = ReadUInt32(data, 10);
            frame.MoveX = ReadInt16(data, 14);
            frame.MoveY = ReadInt16(data, 16);
            return true;
        }

        public static byte[] EncodeControl(PacketType type)
        {
            if (type != PacketType.Disconnect)
            {
                throw new ArgumentException("This packet type requires a structured payload.", "type");
            }

            byte[] data = new byte[6];
            WriteUInt32(data, 0, Magic);
            data[4] = ProtocolVersion;
            data[5] = (byte)type;
            return data;
        }

        public static byte[] EncodeHello(SessionHello hello)
        {
            byte[] data = CreatePacket(PacketType.Hello, HelloPacketSize);
            WriteUInt64(data, 6, hello.SessionNonce);
            WriteUInt64(data, 14, hello.SenderId);
            WriteUInt32(data, 22, hello.Attempt);
            data[26] = hello.RequestedSlot;
            WriteUInt32(data, 27, hello.Capabilities);
            return data;
        }

        public static bool TryDecodeHello(byte[] data, out SessionHello hello)
        {
            hello = new SessionHello();
            if (!HasHeader(data, PacketType.Hello, HelloPacketSize)) return false;
            hello.SessionNonce = ReadUInt64(data, 6);
            hello.SenderId = ReadUInt64(data, 14);
            hello.Attempt = ReadUInt32(data, 22);
            hello.RequestedSlot = data[26];
            hello.Capabilities = ReadUInt32(data, 27);
            return true;
        }

        public static byte[] EncodeAccepted(SessionAccepted accepted)
        {
            byte[] data = CreatePacket(PacketType.HelloAccepted, AcceptedPacketSize);
            WriteUInt64(data, 6, accepted.SessionNonce);
            WriteUInt64(data, 14, accepted.HostId);
            WriteUInt32(data, 22, accepted.Attempt);
            data[26] = accepted.AssignedSlot;
            data[27] = accepted.MaxPlayers;
            WriteUInt32(data, 28, accepted.Capabilities);
            return data;
        }

        public static bool TryDecodeAccepted(byte[] data, out SessionAccepted accepted)
        {
            accepted = new SessionAccepted();
            if (!HasHeader(data, PacketType.HelloAccepted, AcceptedPacketSize)) return false;
            accepted.SessionNonce = ReadUInt64(data, 6);
            accepted.HostId = ReadUInt64(data, 14);
            accepted.Attempt = ReadUInt32(data, 22);
            accepted.AssignedSlot = data[26];
            accepted.MaxPlayers = data[27];
            accepted.Capabilities = ReadUInt32(data, 28);
            return true;
        }

        public static byte[] EncodeRejected(SessionRejected rejected)
        {
            byte[] data = CreatePacket(PacketType.HelloRejected, RejectedPacketSize);
            WriteUInt64(data, 6, rejected.SessionNonce);
            WriteUInt32(data, 14, rejected.Attempt);
            data[18] = (byte)rejected.Reason;
            return data;
        }

        public static bool TryDecodeRejected(byte[] data, out SessionRejected rejected)
        {
            rejected = new SessionRejected();
            if (!HasHeader(data, PacketType.HelloRejected, RejectedPacketSize)) return false;
            rejected.SessionNonce = ReadUInt64(data, 6);
            rejected.Attempt = ReadUInt32(data, 14);
            rejected.Reason = (HandshakeRejectReason)data[18];
            return Enum.IsDefined(typeof(HandshakeRejectReason), rejected.Reason);
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

        private static byte[] CreatePacket(PacketType type, int size)
        {
            byte[] data = new byte[size];
            WriteUInt32(data, 0, Magic);
            data[4] = ProtocolVersion;
            data[5] = (byte)type;
            return data;
        }

        private static bool HasHeader(byte[] data, PacketType type, int size)
        {
            return data != null && data.Length == size &&
                   ReadUInt32(data, 0) == Magic && data[4] == ProtocolVersion && data[5] == (byte)type;
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
