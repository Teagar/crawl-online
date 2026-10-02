using System;
using System.IO;

namespace CrawlOnline.Protocol
{
    public enum TraceRecordType : byte
    {
        Input = 1,
        StateHash = 2,
        FrameTiming = 3
    }

    public struct TraceRecord
    {
        public TraceRecordType Type;
        public InputFrame Input;
        public StateHashRecord StateHash;
        public FrameTimingRecord FrameTiming;
    }

    public sealed class TraceWriter : IDisposable
    {
        private readonly Stream stream;
        private readonly bool leaveOpen;

        public TraceWriter(Stream output, TraceHeader header, bool keepOpen)
        {
            if (output == null || !output.CanWrite)
            {
                throw new ArgumentException("A writable stream is required.", "output");
            }

            stream = output;
            leaveOpen = keepOpen;
            Write(TraceCodec.EncodeHeader(header));
        }

        public void WriteInput(InputFrame frame)
        {
            stream.WriteByte((byte)TraceRecordType.Input);
            Write(PacketCodec.EncodeInput(frame));
        }

        public void WriteStateHash(StateHashRecord record)
        {
            Write(TraceCodec.EncodeStateHash(record));
        }

        public void WriteFrameTiming(FrameTimingRecord record)
        {
            Write(TraceCodec.EncodeFrameTiming(record));
        }

        public void Flush()
        {
            stream.Flush();
        }

        public void Dispose()
        {
            stream.Flush();
            if (!leaveOpen)
            {
                stream.Dispose();
            }
        }

        private void Write(byte[] data)
        {
            stream.Write(data, 0, data.Length);
        }
    }

    public sealed class TraceReader : IDisposable
    {
        private readonly Stream stream;
        private readonly bool leaveOpen;

        public TraceReader(Stream input, bool keepOpen)
        {
            if (input == null || !input.CanRead)
            {
                throw new ArgumentException("A readable stream is required.", "input");
            }

            stream = input;
            leaveOpen = keepOpen;
            byte[] header = ReadExact(TraceCodec.HeaderSize);
            TraceHeader parsed;
            if (!TraceCodec.TryDecodeHeader(header, out parsed))
            {
                throw new IOException("Invalid Crawl Online trace header.");
            }

            Header = parsed;
        }

        public TraceHeader Header { get; private set; }

        public bool TryRead(out TraceRecord record)
        {
            record = new TraceRecord();
            int marker = stream.ReadByte();
            if (marker < 0)
            {
                return false;
            }

            if (marker == (byte)TraceRecordType.Input)
            {
                byte[] encodedInput;
                if (!TryReadExact(PacketCodec.InputPacketSize, out encodedInput))
                {
                    return false;
                }
                InputFrame input;
                if (!PacketCodec.TryDecodeInput(encodedInput, out input))
                {
                    throw new IOException("Invalid input trace record.");
                }

                record.Type = TraceRecordType.Input;
                record.Input = input;
                return true;
            }

            if (marker == (byte)TraceRecordType.StateHash)
            {
                byte[] encoded = new byte[TraceCodec.StateHashRecordSize];
                encoded[0] = (byte)marker;
                byte[] remainder;
                if (!TryReadExact(TraceCodec.StateHashRecordSize - 1, out remainder))
                {
                    return false;
                }
                Buffer.BlockCopy(remainder, 0, encoded, 1, remainder.Length);
                StateHashRecord stateHash;
                if (!TraceCodec.TryDecodeStateHash(encoded, out stateHash))
                {
                    throw new IOException("Invalid state hash trace record.");
                }

                record.Type = TraceRecordType.StateHash;
                record.StateHash = stateHash;
                return true;
            }

            if (marker == (byte)TraceRecordType.FrameTiming)
            {
                byte[] encoded = new byte[TraceCodec.FrameTimingRecordSize];
                encoded[0] = (byte)marker;
                byte[] remainder;
                if (!TryReadExact(TraceCodec.FrameTimingRecordSize - 1, out remainder))
                {
                    return false;
                }
                Buffer.BlockCopy(remainder, 0, encoded, 1, remainder.Length);
                FrameTimingRecord frameTiming;
                if (!TraceCodec.TryDecodeFrameTiming(encoded, out frameTiming))
                {
                    throw new IOException("Invalid frame timing trace record.");
                }

                record.Type = TraceRecordType.FrameTiming;
                record.FrameTiming = frameTiming;
                return true;
            }

            throw new IOException("Unknown trace record type " + marker + ".");
        }

        public void Dispose()
        {
            if (!leaveOpen)
            {
                stream.Dispose();
            }
        }

        private byte[] ReadExact(int count)
        {
            byte[] data = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(data, offset, count - offset);
                if (read == 0)
                {
                    throw new EndOfStreamException("Trace ended in the middle of a record.");
                }

                offset += read;
            }

            return data;
        }

        private bool TryReadExact(int count, out byte[] data)
        {
            data = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(data, offset, count - offset);
                if (read == 0)
                {
                    data = null;
                    return false;
                }
                offset += read;
            }
            return true;
        }
    }
}
