using CrawlOnline.Protocol;
using System.IO;
using Xunit;

namespace CrawlOnline.Protocol.Tests;

public sealed class TraceCodecTests
{
    [Fact]
    public void HeaderRoundTrips()
    {
        var expected = new TraceHeader
        {
            RandomSeed = -123456,
            FixedTicksPerSecond = 50,
            PlayerCount = 4,
            StateHashInterval = 25
        };

        byte[] encoded = TraceCodec.EncodeHeader(expected);

        Assert.True(TraceCodec.TryDecodeHeader(encoded, out TraceHeader actual));
        Assert.Equal(expected.RandomSeed, actual.RandomSeed);
        Assert.Equal(expected.FixedTicksPerSecond, actual.FixedTicksPerSecond);
        Assert.Equal(expected.PlayerCount, actual.PlayerCount);
        Assert.Equal(expected.StateHashInterval, actual.StateHashInterval);
    }

    [Fact]
    public void StateHashRoundTrips()
    {
        var expected = new StateHashRecord
        {
            Tick = uint.MaxValue,
            ExactHash = ulong.MaxValue - 9,
            QuantizedHash = 0x1122334455667788UL
        };

        byte[] encoded = TraceCodec.EncodeStateHash(expected);

        Assert.True(TraceCodec.TryDecodeStateHash(encoded, out StateHashRecord actual));
        Assert.Equal(expected.Tick, actual.Tick);
        Assert.Equal(expected.ExactHash, actual.ExactHash);
        Assert.Equal(expected.QuantizedHash, actual.QuantizedHash);
    }

    [Fact]
    public void StableHashQuantizesAndIsOrderSensitive()
    {
        var first = new StableHash64();
        first.AddInt32(7);
        first.AddQuantized(1.23449f, 1000f);
        first.AddString("room");

        var equivalent = new StableHash64();
        equivalent.AddInt32(7);
        equivalent.AddQuantized(1.2344f, 1000f);
        equivalent.AddString("room");

        var reordered = new StableHash64();
        reordered.AddString("room");
        reordered.AddInt32(7);
        reordered.AddQuantized(1.2344f, 1000f);

        Assert.Equal(first.Value, equivalent.Value);
        Assert.NotEqual(first.Value, reordered.Value);
    }

    [Fact]
    public void TraceStreamRoundTripsMixedRecords()
    {
        var stream = new MemoryStream();
        var header = new TraceHeader
        {
            RandomSeed = 42,
            FixedTicksPerSecond = 50,
            PlayerCount = 2,
            StateHashInterval = 10
        };
        var input = new InputFrame
        {
            Tick = 3,
            PlayerId = 1,
            MoveX = -4,
            MoveY = 5,
            HeldButtons = 2,
            DownButtons = 2
        };
        var stateHash = new StateHashRecord
        {
            Tick = 10,
            ExactHash = 0x1122334455667788UL,
            QuantizedHash = 0x8877665544332211UL
        };
        var timing = new FrameTimingRecord
        {
            Tick = 3,
            UnityFrameCount = 100,
            DeltaMicroseconds = 16667,
            FixedDeltaMicroseconds = 20000,
            FixedSteps = 1
        };

        using (var writer = new TraceWriter(stream, header, true))
        {
            writer.WriteInput(input);
            writer.WriteFrameTiming(timing);
            writer.WriteStateHash(stateHash);
        }

        stream.Position = 0;
        using var reader = new TraceReader(stream, true);
        Assert.Equal(header.RandomSeed, reader.Header.RandomSeed);

        Assert.True(reader.TryRead(out TraceRecord first));
        Assert.Equal(TraceRecordType.Input, first.Type);
        Assert.Equal(input, first.Input);

        Assert.True(reader.TryRead(out TraceRecord second));
        Assert.Equal(TraceRecordType.FrameTiming, second.Type);
        Assert.Equal(timing.UnityFrameCount, second.FrameTiming.UnityFrameCount);
        Assert.Equal(timing.FixedSteps, second.FrameTiming.FixedSteps);

        Assert.True(reader.TryRead(out TraceRecord third));
        Assert.Equal(TraceRecordType.StateHash, third.Type);
        Assert.Equal(stateHash.Tick, third.StateHash.Tick);
        Assert.Equal(stateHash.ExactHash, third.StateHash.ExactHash);
        Assert.Equal(stateHash.QuantizedHash, third.StateHash.QuantizedHash);
        Assert.False(reader.TryRead(out _));
    }

    [Fact]
    public void TraceReaderIgnoresRecordInterruptedByShutdown()
    {
        var stream = new MemoryStream();
        var header = new TraceHeader
        {
            RandomSeed = 1,
            FixedTicksPerSecond = 50,
            PlayerCount = 1,
            StateHashInterval = 30
        };
        byte[] headerBytes = TraceCodec.EncodeHeader(header);
        stream.Write(headerBytes);
        stream.WriteByte((byte)TraceRecordType.StateHash);
        stream.WriteByte(0x12);
        stream.Position = 0;

        using var reader = new TraceReader(stream, true);
        Assert.False(reader.TryRead(out _));
    }
}
