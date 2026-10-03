using System;
using CrawlOnline.Protocol;
using Xunit;

namespace CrawlOnline.Protocol.Tests;

public sealed class ProtocolRobustnessTests
{
    private static readonly GameBuildFingerprint WindowsBuild =
        GameBuildFingerprint.Parse("e93e8fb49fd3c3ebe622d0f9f9557c1e4dd475c2a277be19e2c05cbb1f05f61e");

    [Fact]
    public void StructuredPacketsRejectNullTruncatedAndTrailingData()
    {
        AssertRejectsWrongLengths(PacketCodec.EncodeInput(new InputFrame()),
            data => PacketCodec.TryDecodeInput(data, out _));
        AssertRejectsWrongLengths(PacketCodec.EncodeHello(new SessionHello { GameBuild = WindowsBuild }),
            data => PacketCodec.TryDecodeHello(data, out _));
        AssertRejectsWrongLengths(PacketCodec.EncodeAccepted(new SessionAccepted { GameBuild = WindowsBuild }),
            data => PacketCodec.TryDecodeAccepted(data, out _));
        AssertRejectsWrongLengths(PacketCodec.EncodeRejected(new SessionRejected
        {
            Reason = HandshakeRejectReason.InvalidSession
        }), data => PacketCodec.TryDecodeRejected(data, out _));
        AssertRejectsWrongLengths(AuthoritativeCodec.EncodeInput(new SessionInputFrame()),
            data => AuthoritativeCodec.TryDecodeInput(data, out _));
        AssertRejectsWrongLengths(AuthoritativeCodec.EncodeInputEvent(new SessionInputFrame
        {
            Input = new InputFrame { DownButtons = 1 }
        }), data => AuthoritativeCodec.TryDecodeInputEvent(data, out _));
        AssertRejectsWrongLengths(AuthoritativeCodec.EncodeAcknowledgement(new SnapshotAcknowledgement()),
            data => AuthoritativeCodec.TryDecodeAcknowledgement(data, out _));
    }

    [Fact]
    public void EveryCodecRejectsCorruptedHeader()
    {
        AssertRejectsCorruptedHeader(PacketCodec.EncodeInput(new InputFrame()),
            data => PacketCodec.TryDecodeInput(data, out _));
        AssertRejectsCorruptedHeader(PacketCodec.EncodeHello(new SessionHello { GameBuild = WindowsBuild }),
            data => PacketCodec.TryDecodeHello(data, out _));
        AssertRejectsCorruptedHeader(PacketCodec.EncodeAccepted(new SessionAccepted { GameBuild = WindowsBuild }),
            data => PacketCodec.TryDecodeAccepted(data, out _));
        AssertRejectsCorruptedHeader(PacketCodec.EncodeRejected(new SessionRejected
        {
            Reason = HandshakeRejectReason.LobbyFull
        }), data => PacketCodec.TryDecodeRejected(data, out _));
        AssertRejectsCorruptedHeader(AuthoritativeCodec.EncodeInput(new SessionInputFrame()),
            data => AuthoritativeCodec.TryDecodeInput(data, out _));
        AssertRejectsCorruptedHeader(AuthoritativeCodec.EncodeInputEvent(new SessionInputFrame
        {
            Input = new InputFrame { UpButtons = 1 }
        }), data => AuthoritativeCodec.TryDecodeInputEvent(data, out _));
        AssertRejectsCorruptedHeader(AuthoritativeCodec.EncodeSnapshot(EmptySnapshot()),
            data => AuthoritativeCodec.TryDecodeSnapshot(data, out _));
        AssertRejectsCorruptedHeader(AuthoritativeCodec.EncodeAcknowledgement(new SnapshotAcknowledgement()),
            data => AuthoritativeCodec.TryDecodeAcknowledgement(data, out _));
    }

    [Fact]
    public void SnapshotRejectsImpossibleCountsAndPacketSizes()
    {
        byte[] encoded = AuthoritativeCodec.EncodeSnapshot(EmptySnapshot());

        byte[] tooManyPlayers = (byte[])encoded.Clone();
        tooManyPlayers[83] = 5;
        Assert.False(AuthoritativeCodec.TryDecodeSnapshot(tooManyPlayers, out _));

        byte[] tooManyEnemies = (byte[])encoded.Clone();
        tooManyEnemies[84] = 1;
        tooManyEnemies[85] = 1;
        Assert.False(AuthoritativeCodec.TryDecodeSnapshot(tooManyEnemies, out _));

        Assert.False(AuthoritativeCodec.TryDecodeSnapshot(encoded[..^1], out _));
        Assert.False(AuthoritativeCodec.TryDecodeSnapshot(AppendByte(encoded), out _));
        Assert.False(AuthoritativeCodec.TryDecodeSnapshot(
            new byte[AuthoritativeCodec.MaximumSnapshotPacketSize + 1], out _));
    }

    [Fact]
    public void FixedSeedFuzzNeverEscapesAnyDecoder()
    {
        var random = new Random(0x293780);
        for (int iteration = 0; iteration < 4_000; iteration++)
        {
            int length = random.Next(0, AuthoritativeCodec.MaximumSnapshotPacketSize + 128);
            var data = new byte[length];
            random.NextBytes(data);

            Exception exception = Record.Exception(() => DecodeEveryPacketShape(data));
            Assert.Null(exception);
        }

        Exception nullException = Record.Exception(() => DecodeEveryPacketShape(null));
        Assert.Null(nullException);
    }

    [Fact]
    public void UnknownPacketTypesAndRejectReasonsFailClosed()
    {
        byte[] packet = PacketCodec.EncodeControl(PacketType.Disconnect);
        packet[5] = byte.MaxValue;
        Assert.False(PacketCodec.TryReadType(packet, out _));

        byte[] rejected = PacketCodec.EncodeRejected(new SessionRejected
        {
            Reason = HandshakeRejectReason.InvalidSession
        });
        rejected[^1] = byte.MaxValue;
        Assert.False(PacketCodec.TryDecodeRejected(rejected, out _));
    }

    private static void AssertRejectsWrongLengths(byte[] valid, Func<byte[], bool> decode)
    {
        Assert.False(decode(null));
        Assert.False(decode(Array.Empty<byte>()));
        Assert.False(decode(valid[..^1]));
        Assert.False(decode(AppendByte(valid)));
    }

    private static void AssertRejectsCorruptedHeader(byte[] valid, Func<byte[], bool> decode)
    {
        for (int index = 0; index < 6; index++)
        {
            byte[] corrupted = (byte[])valid.Clone();
            corrupted[index] ^= 0x80;
            Assert.False(decode(corrupted));
        }
    }

    private static byte[] AppendByte(byte[] source)
    {
        var result = new byte[source.Length + 1];
        Buffer.BlockCopy(source, 0, result, 0, source.Length);
        return result;
    }

    private static void DecodeEveryPacketShape(byte[] data)
    {
        PacketCodec.TryReadType(data!, out _);
        PacketCodec.TryDecodeInput(data!, out _);
        PacketCodec.TryDecodeHello(data!, out _);
        PacketCodec.TryDecodeAccepted(data!, out _);
        PacketCodec.TryDecodeRejected(data!, out _);
        AuthoritativeCodec.TryDecodeInput(data!, out _);
        AuthoritativeCodec.TryDecodeInputEvent(data!, out _);
        AuthoritativeCodec.TryDecodeSnapshot(data!, out _);
        AuthoritativeCodec.TryDecodeAcknowledgement(data!, out _);
    }

    private static WorldSnapshot EmptySnapshot()
    {
        return new WorldSnapshot
        {
            SessionNonce = 1,
            LastInputSequences = new uint[4],
            RandomStateWords = new uint[4]
        };
    }
}
