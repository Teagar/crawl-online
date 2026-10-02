using System;
using CrawlOnline.Protocol;
using Xunit;

namespace CrawlOnline.Protocol.Tests;

public sealed class AuthoritativeProtocolTests
{
    [Fact]
    public void SessionInputRoundTripsWithNonceAndSequence()
    {
        var expected = new SessionInputFrame
        {
            SessionNonce = 0x1122334455667788UL,
            Sequence = uint.MaxValue - 2,
            Input = new InputFrame
            {
                Tick = 55,
                PlayerId = 3,
                MoveX = short.MinValue,
                MoveY = short.MaxValue,
                HeldButtons = 7,
                DownButtons = 1,
                UpButtons = 2
            }
        };

        Assert.True(AuthoritativeCodec.TryDecodeInput(
            AuthoritativeCodec.EncodeInput(expected), out SessionInputFrame actual));
        Assert.Equal(expected.SessionNonce, actual.SessionNonce);
        Assert.Equal(expected.Sequence, actual.Sequence);
        Assert.Equal(expected.Input, actual.Input);
    }

    [Fact]
    public void SnapshotRoundTripsCanonicalPlayers()
    {
        WorldSnapshot expected = Snapshot(9);
        expected.Players = new[]
        {
            new PlayerSnapshot
            {
                Slot = 0,
                Flags = PlayerSnapshotFlags.Active | PlayerSnapshotFlags.Hero |
                        PlayerSnapshotFlags.Alive | PlayerSnapshotFlags.Present,
                State = -2,
                PositionX = -12345,
                PositionY = 67890,
                VelocityX = -33,
                VelocityY = 44,
                HealthCurrent = 9500,
                HealthMaximum = 10000
            },
            new PlayerSnapshot { Slot = 2, Flags = PlayerSnapshotFlags.Active }
        };

        byte[] encoded = AuthoritativeCodec.EncodeSnapshot(expected);

        Assert.True(AuthoritativeCodec.TryDecodeSnapshot(encoded, out WorldSnapshot actual));
        Assert.Equal(expected.SessionNonce, actual.SessionNonce);
        Assert.Equal(expected.Sequence, actual.Sequence);
        Assert.Equal(expected.HostTick, actual.HostTick);
        Assert.Equal(expected.LastInputSequence, actual.LastInputSequence);
        Assert.Equal(expected.Level, actual.Level);
        Assert.Equal(expected.RoomX, actual.RoomX);
        Assert.Equal(expected.RandomStateHash, actual.RandomStateHash);
        Assert.Equal(2, actual.Players.Length);
        Assert.Equal(expected.Players[0].PositionX, actual.Players[0].PositionX);
        Assert.Equal(expected.Players[0].Flags, actual.Players[0].Flags);
        Assert.Equal((byte)2, actual.Players[1].Slot);
    }

    [Fact]
    public void SnapshotRejectsDuplicateOrUnsortedSlots()
    {
        WorldSnapshot snapshot = Snapshot(1);
        snapshot.Players = new[]
        {
            new PlayerSnapshot { Slot = 2 },
            new PlayerSnapshot { Slot = 1 }
        };
        Assert.Throws<ArgumentException>(() => AuthoritativeCodec.EncodeSnapshot(snapshot));
    }

    [Fact]
    public void AcknowledgementRoundTrips()
    {
        var expected = new SnapshotAcknowledgement
        {
            SessionNonce = 77,
            Sequence = uint.MaxValue
        };
        Assert.True(AuthoritativeCodec.TryDecodeAcknowledgement(
            AuthoritativeCodec.EncodeAcknowledgement(expected), out SnapshotAcknowledgement actual));
        Assert.Equal(expected.SessionNonce, actual.SessionNonce);
        Assert.Equal(expected.Sequence, actual.Sequence);
    }

    [Fact]
    public void SequenceWindowRejectsWrongSessionDuplicateAndStalePackets()
    {
        var window = new SequenceWindow(77);
        SessionInputFrame input = Input(77, 10, 1);
        Assert.True(window.TryAcceptInput(input));
        Assert.False(window.TryAcceptInput(input));
        input.Sequence = 9;
        Assert.False(window.TryAcceptInput(input));
        input.Sequence = 11;
        Assert.True(window.TryAcceptInput(input));
        input.SessionNonce = 88;
        input.Sequence = 12;
        Assert.False(window.TryAcceptInput(input));
    }

    [Fact]
    public void SequenceComparisonSupportsUInt32Wrap()
    {
        Assert.True(SequenceWindow.IsNewer(0, uint.MaxValue));
        Assert.False(SequenceWindow.IsNewer(uint.MaxValue, 0));
    }

    [Fact]
    public void SnapshotHistoryIsBoundedAndAcknowledged()
    {
        var history = new SnapshotHistory(3);
        history.Add(Snapshot(1));
        history.Add(Snapshot(2));
        history.Add(Snapshot(3));
        history.Add(Snapshot(4));
        Assert.Equal(3, history.Count);

        history.Acknowledge(3);
        Assert.Equal(1, history.Count);
        history.Acknowledge(4);
        Assert.Equal(0, history.Count);
    }

    private static SessionInputFrame Input(ulong nonce, uint sequence, byte slot)
    {
        return new SessionInputFrame
        {
            SessionNonce = nonce,
            Sequence = sequence,
            Input = new InputFrame { PlayerId = slot }
        };
    }

    private static WorldSnapshot Snapshot(uint sequence)
    {
        return new WorldSnapshot
        {
            SessionNonce = 77,
            Sequence = sequence,
            HostTick = 100,
            LastInputSequence = 90,
            Level = 3,
            RoomX = -12000,
            RoomY = 44000,
            RoomDepth = 5,
            TransitionGeneration = 2,
            RandomStateHash = 0x8877665544332211UL
        };
    }
}
