using CrawlOnline.Protocol;
using Xunit;

namespace CrawlOnline.Protocol.Tests;

public sealed class PacketCodecTests
{
    [Fact]
    public void InputPacketRoundTrips()
    {
        var expected = new InputFrame
        {
            Tick = uint.MaxValue - 7,
            PlayerId = 3,
            MoveX = short.MinValue,
            MoveY = short.MaxValue,
            HeldButtons = 0b0000_0111,
            DownButtons = 0b0000_0010,
            UpButtons = 0b0000_0100
        };

        byte[] encoded = PacketCodec.EncodeInput(expected);

        Assert.Equal(PacketCodec.InputPacketSize, encoded.Length);
        Assert.True(PacketCodec.TryDecodeInput(encoded, out InputFrame actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RejectsWrongMagicVersionAndLength()
    {
        byte[] packet = PacketCodec.EncodeInput(new InputFrame());
        packet[0] ^= 0xFF;
        Assert.False(PacketCodec.TryDecodeInput(packet, out _));

        packet = PacketCodec.EncodeInput(new InputFrame());
        packet[4]++;
        Assert.False(PacketCodec.TryDecodeInput(packet, out _));

        Assert.False(PacketCodec.TryDecodeInput(new byte[2], out _));
    }

    [Fact]
    public void ReadsControlPacketType()
    {
        byte[] packet = PacketCodec.EncodeControl(PacketType.Hello);
        Assert.True(PacketCodec.TryReadType(packet, out PacketType type));
        Assert.Equal(PacketType.Hello, type);
    }
}
