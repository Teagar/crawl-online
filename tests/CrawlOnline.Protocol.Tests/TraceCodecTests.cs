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
        var expected = new StateHashRecord { Tick = uint.MaxValue, Hash = ulong.MaxValue - 9 };

        byte[] encoded = TraceCodec.EncodeStateHash(expected);

        Assert.True(TraceCodec.TryDecodeStateHash(encoded, out StateHashRecord actual));
        Assert.Equal(expected.Tick, actual.Tick);
        Assert.Equal(expected.Hash, actual.Hash);
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
        var input = new InputFrame { Tick = 3, PlayerId = 1, MoveX = -4, MoveY = 5, Buttons = 2 };
        var stateHash = new StateHashRecord { Tick = 10, Hash = 0x1122334455667788UL };

        using (var writer = new TraceWriter(stream, header, true))
        {
            writer.WriteInput(input);
            writer.WriteStateHash(stateHash);
        }

        stream.Position = 0;
        using var reader = new TraceReader(stream, true);
        Assert.Equal(header.RandomSeed, reader.Header.RandomSeed);

        Assert.True(reader.TryRead(out TraceRecord first));
        Assert.Equal(TraceRecordType.Input, first.Type);
        Assert.Equal(input, first.Input);

        Assert.True(reader.TryRead(out TraceRecord second));
        Assert.Equal(TraceRecordType.StateHash, second.Type);
        Assert.Equal(stateHash.Tick, second.StateHash.Tick);
        Assert.Equal(stateHash.Hash, second.StateHash.Hash);
        Assert.False(reader.TryRead(out _));
    }
}
