using System;

namespace CrawlOnline.Protocol
{
    public struct InputFrame : IEquatable<InputFrame>
    {
        public uint Tick;
        public byte PlayerId;
        public short MoveX;
        public short MoveY;
        public byte HeldButtons;
        public byte DownButtons;
        public byte UpButtons;

        public bool Equals(InputFrame other)
        {
            return Tick == other.Tick &&
                   PlayerId == other.PlayerId &&
                   MoveX == other.MoveX &&
                   MoveY == other.MoveY &&
                   HeldButtons == other.HeldButtons &&
                   DownButtons == other.DownButtons &&
                   UpButtons == other.UpButtons;
        }

        public override bool Equals(object obj)
        {
            return obj is InputFrame && Equals((InputFrame)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Tick;
                hash = (hash * 397) ^ PlayerId;
                hash = (hash * 397) ^ MoveX;
                hash = (hash * 397) ^ MoveY;
                hash = (hash * 397) ^ HeldButtons;
                hash = (hash * 397) ^ DownButtons;
                hash = (hash * 397) ^ UpButtons;
                return hash;
            }
        }
    }
}
