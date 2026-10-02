using System;
using System.Text;

namespace CrawlOnline.Protocol
{
    public struct StableHash64
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;
        private ulong value;
        private bool initialized;

        public ulong Value
        {
            get { return initialized ? value : OffsetBasis; }
        }

        public void AddByte(byte item)
        {
            EnsureInitialized();
            value = (value ^ item) * Prime;
        }

        public void AddBoolean(bool item)
        {
            AddByte(item ? (byte)1 : (byte)0);
        }

        public void AddInt32(int item)
        {
            unchecked
            {
                AddByte((byte)item);
                AddByte((byte)(item >> 8));
                AddByte((byte)(item >> 16));
                AddByte((byte)(item >> 24));
            }
        }

        public void AddUInt32(uint item)
        {
            unchecked
            {
                AddInt32((int)item);
            }
        }

        public void AddQuantized(float item, float unitsPerStep)
        {
            if (float.IsNaN(item) || float.IsInfinity(item))
            {
                throw new ArgumentOutOfRangeException("item", "State values must be finite.");
            }
            if (unitsPerStep <= 0f || float.IsNaN(unitsPerStep) || float.IsInfinity(unitsPerStep))
            {
                throw new ArgumentOutOfRangeException("unitsPerStep");
            }

            double scaled = Math.Round(item * unitsPerStep, MidpointRounding.AwayFromZero);
            if (scaled < int.MinValue || scaled > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException("item", "Quantized state value is outside Int32 range.");
            }

            AddInt32((int)scaled);
        }

        public void AddString(string item)
        {
            if (item == null)
            {
                AddInt32(-1);
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(item);
            AddInt32(bytes.Length);
            for (int i = 0; i < bytes.Length; i++)
            {
                AddByte(bytes[i]);
            }
        }

        private void EnsureInitialized()
        {
            if (!initialized)
            {
                value = OffsetBasis;
                initialized = true;
            }
        }
    }
}
