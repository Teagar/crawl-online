using CrawlOnline.Protocol;
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
}
