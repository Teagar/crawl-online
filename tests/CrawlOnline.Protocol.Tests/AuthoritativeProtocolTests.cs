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
        expected.Enemies = new[]
        {
            new EnemySnapshot
            {
                Id = 7,
                ArchetypeHash = 0x1234567890abcdefUL,
                Flags = EnemySnapshotFlags.Active | EnemySnapshotFlags.Alive | EnemySnapshotFlags.AiControlled,
                State = 4,
                PositionX = 500,
                PositionY = -600,
                VelocityX = 70,
                VelocityY = -80,
                HealthCurrent = 900,
                HealthMaximum = 1000
            }
        };

        byte[] encoded = AuthoritativeCodec.EncodeSnapshot(expected);

        Assert.True(AuthoritativeCodec.TryDecodeSnapshot(encoded, out WorldSnapshot actual));
        Assert.Equal(expected.SessionNonce, actual.SessionNonce);
        Assert.Equal(expected.Sequence, actual.Sequence);
        Assert.Equal(expected.HostTick, actual.HostTick);
        Assert.Equal(expected.LastInputSequences, actual.LastInputSequences);
        Assert.Equal(expected.Flags, actual.Flags);
        Assert.Equal(expected.Level, actual.Level);
        Assert.Equal(expected.RoomX, actual.RoomX);
        Assert.Equal(expected.RandomStateHash, actual.RandomStateHash);
        Assert.Equal(expected.RandomStateWords, actual.RandomStateWords);
        Assert.Equal(2, actual.Players.Length);
        Assert.Equal(expected.Players[0].PositionX, actual.Players[0].PositionX);
        Assert.Equal(expected.Players[0].Flags, actual.Players[0].Flags);
        Assert.Equal((byte)2, actual.Players[1].Slot);
        Assert.Single(actual.Enemies);
        Assert.Equal(expected.Enemies[0].Id, actual.Enemies[0].Id);
        Assert.Equal(expected.Enemies[0].ArchetypeHash, actual.Enemies[0].ArchetypeHash);
        Assert.Equal(expected.Enemies[0].HealthCurrent, actual.Enemies[0].HealthCurrent);
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
    public void SnapshotRejectsMultipleHeroes()
    {
        WorldSnapshot snapshot = Snapshot(1);
        snapshot.Players = new[]
        {
            new PlayerSnapshot { Slot = 0, Flags = PlayerSnapshotFlags.Hero },
            new PlayerSnapshot { Slot = 1, Flags = PlayerSnapshotFlags.Hero }
        };

        Assert.Throws<ArgumentException>(() => AuthoritativeCodec.EncodeSnapshot(snapshot));
    }

    [Fact]
    public void SnapshotRejectsUnknownWorldFlags()
    {
        byte[] encoded = AuthoritativeCodec.EncodeSnapshot(Snapshot(1));
        encoded[38] = 0x80;

        Assert.False(AuthoritativeCodec.TryDecodeSnapshot(encoded, out _));
    }

    [Fact]
    public void SnapshotRejectsDuplicateEnemyIds()
    {
        WorldSnapshot snapshot = Snapshot(1);
        snapshot.Enemies = new[]
        {
            new EnemySnapshot { Id = 4, ArchetypeHash = 1 },
            new EnemySnapshot { Id = 4, ArchetypeHash = 2 }
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

    [Fact]
    public void InputEdgesWaitForFirstConsumerAndAppearForOneRenderFrame()
    {
        var buffer = new InputFrameBuffer();
        buffer.Set(new InputFrame { DownButtons = 1 }, 10);
        buffer.Set(new InputFrame { DownButtons = 2 }, 11);

        Assert.Equal((byte)3, buffer.GetDown(12));
        Assert.Equal((byte)3, buffer.GetDown(12));
        Assert.Equal((byte)0, buffer.GetDown(13));
    }

    [Fact]
    public void InputEdgesReceivedAfterPresentationCarryIntoNextFrame()
    {
        var buffer = new InputFrameBuffer();
        buffer.Set(new InputFrame { DownButtons = 1 }, 20);
        Assert.Equal((byte)1, buffer.GetDown(20));

        buffer.Set(new InputFrame { DownButtons = 2 }, 20);
        Assert.Equal((byte)3, buffer.GetDown(20));
        buffer.Set(new InputFrame(), 21);
        Assert.Equal((byte)0, buffer.GetDown(21));
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
            LastInputSequences = new uint[] { 10, 20, 90, 40 },
            Flags = WorldSnapshotFlags.GameInProgress | WorldSnapshotFlags.HasCurrentRoom,
            Level = 3,
            RoomX = -12000,
            RoomY = 44000,
            RoomDepth = 5,
            TransitionGeneration = 2,
            RandomStateHash = 0x8877665544332211UL,
            RandomStateWords = new uint[] { 1, 2, uint.MaxValue, 4 }
        };
    }
}
