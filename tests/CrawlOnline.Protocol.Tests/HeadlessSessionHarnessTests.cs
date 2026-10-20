using System;
using CrawlOnline.Protocol;
using Xunit;

namespace CrawlOnline.Protocol.Tests;

public sealed class HeadlessSessionHarnessTests
{
    private static readonly GameBuildFingerprint WindowsBuild =
        GameBuildFingerprint.Parse("e93e8fb49fd3c3ebe622d0f9f9557c1e4dd475c2a277be19e2c05cbb1f05f61e");
    private static readonly GameBuildFingerprint UnsupportedBuild =
        GameBuildFingerprint.Parse("f93e8fb49fd3c3ebe622d0f9f9557c1e4dd475c2a277be19e2c05cbb1f05f61e");

    [Fact]
    public void HostAndThreePeersAuthenticateAndExchangeRealGameplayPackets()
    {
        HeadlessSessionHarness harness = Harness(1001);
        HeadlessSessionPeer first = harness.AddClient(2);
        HeadlessSessionPeer second = harness.AddClient(3);
        HeadlessSessionPeer third = harness.AddClient(4);
        Assert.False(harness.SendInput(first, Input(0, 1)));

        Assert.True(harness.Connect(first));
        Assert.True(harness.Connect(second));
        Assert.True(harness.Connect(third));
        harness.Advance(10);

        Assert.Equal(new byte[] { 1, 2, 3 }, harness.ConnectedSlots());
        byte[] assigned = { first.Slot, second.Slot, third.Slot };
        Array.Sort(assigned);
        Assert.Equal(new byte[] { 1, 2, 3 }, assigned);
        Assert.True(first.IsAuthenticated);
        Assert.True(second.IsAuthenticated);
        Assert.True(third.IsAuthenticated);

        Assert.True(harness.SendInput(first, Input(11, 1)));
        Assert.True(harness.SendInput(second, Input(12, 2)));
        Assert.True(harness.SendInput(third, Input(13, 4)));
        harness.Advance(10);
        Assert.Equal(3, harness.Host.InputCount);
        Assert.Equal(3, harness.Host.InputEventCount);

        Assert.True(harness.BroadcastSnapshot(Snapshot()));
        harness.Advance(10);
        Assert.Equal(1, first.SnapshotCount);
        Assert.Equal(1, second.SnapshotCount);
        Assert.Equal(1, third.SnapshotCount);
    }

    [Fact]
    public void PregamePhaseAndIndependentKeyboardFramesReachAssignedSlots()
    {
        HeadlessSessionHarness harness = Harness(1010);
        HeadlessSessionPeer first = harness.AddClient(2);
        HeadlessSessionPeer second = harness.AddClient(3);
        Assert.True(harness.Connect(first));
        Assert.True(harness.Connect(second));
        harness.Advance(10);

        Assert.True(harness.BroadcastPregameState(PregamePhase.ConfiguringPlayers));
        Assert.True(harness.SendInput(first, new InputFrame { Tick = 20, MoveY = 1000 }));
        Assert.True(harness.SendInput(second, new InputFrame { Tick = 20, MoveX = -1000 }));
        harness.Advance(10);

        Assert.Equal(PregamePhase.ConfiguringPlayers, first.PregamePhase);
        Assert.Equal(PregamePhase.ConfiguringPlayers, second.PregamePhase);
        Assert.Equal(1, first.PregameStateCount);
        Assert.Equal(1, second.PregameStateCount);
        Assert.Equal(2, harness.Host.InputCount);
        Assert.Equal(new byte[] { 1, 2 }, harness.ConnectedSlots());
    }

    [Fact]
    public void FullLobbyInvalidBuildNonceAndIdentityFailClosed()
    {
        HeadlessSessionHarness harness = Harness(1002);
        for (ulong id = 2; id <= 4; id++) Assert.True(harness.Connect(harness.AddClient(id)));
        harness.Advance(10);
        Assert.Equal(new byte[] { 1, 2, 3 }, harness.ConnectedSlots());

        HeadlessSessionPeer full = harness.AddClient(5);
        Assert.True(harness.Connect(full));
        harness.Advance(10);
        Assert.Equal(SessionPacketResult.LocalRejected, full.LastPacketResult);
        Assert.False(full.IsAuthenticated);

        HeadlessSessionPeer invalidBuild = harness.AddClient(6, UnsupportedBuild);
        Assert.True(harness.Connect(invalidBuild));
        harness.Advance(10);
        Assert.Equal(SessionPacketResult.LocalRejected, invalidBuild.LastPacketResult);
        Assert.False(invalidBuild.IsAuthenticated);

        HeadlessSessionPeer invalidNonce = harness.AddClient(7);
        Assert.True(harness.Connect(invalidNonce, 0x9999, byte.MaxValue));
        harness.Advance(10);
        Assert.False(invalidNonce.IsAuthenticated);

        HeadlessSessionPeer spoof = harness.AddClient(8);
        byte[] spoofedHello = PacketCodec.EncodeHello(new SessionHello
        {
            SessionNonce = 0x12345678,
            SenderId = 999,
            Attempt = 1,
            RequestedSlot = byte.MaxValue,
            Capabilities = SessionCapabilities.Current,
            GameBuild = WindowsBuild
        });
        Assert.True(harness.SendRaw(spoof, harness.Host.Id, spoofedHello, SessionDelivery.Reliable));
        harness.Advance(10);

        Assert.Equal(new byte[] { 1, 2, 3 }, harness.ConnectedSlots());
        Assert.Equal(SessionPacketResult.HandshakeRejected, harness.Host.LastPacketResult);
        Assert.True(harness.QueuedPacketCount <= 1024);
    }

    [Fact]
    public void RequestedSlotDoesNotAuthenticateClientBeforeAcceptance()
    {
        HeadlessSessionHarness harness = Harness(1006);
        HeadlessSessionPeer peer = harness.AddClient(2);

        Assert.True(harness.Connect(peer, 0x12345678, 2));
        Assert.False(peer.IsAuthenticated);
        Assert.False(harness.SendInput(peer, Input(1, 1)));
        harness.Advance(10);

        Assert.True(peer.IsAuthenticated);
        Assert.Equal((byte)2, peer.Slot);
    }

    [Fact]
    public void LossDelayDuplicationReorderingAndCorruptionRemainBounded()
    {
        HeadlessSessionHarness harness = Harness(1003);
        HeadlessSessionPeer peer = harness.AddClient(2);
        harness.Connect(peer);
        harness.Advance(20);
        Assert.True(peer.IsAuthenticated);

        harness.SetProfile(peer.Id, harness.Host.Id, SessionDelivery.UnreliableNoDelay,
            new SessionNetworkProfile
            {
                MinimumLatencyTicks = 1,
                MaximumLatencyTicks = 8,
                UnreliableLossPercent = 100
            });
        Assert.True(harness.SendInput(peer, Input(1, 1)));
        harness.Advance(20);
        Assert.Equal(0, harness.Host.InputCount);
        Assert.Equal(1, harness.Host.InputEventCount);

        harness.SetProfile(harness.Host.Id, peer.Id, SessionDelivery.Unreliable,
            new SessionNetworkProfile
            {
                MinimumLatencyTicks = 1,
                MaximumLatencyTicks = 8,
                UnreliableDuplicatePercent = 100,
                UnreliableCorruptionPercent = 100,
                BandwidthPacketsPerTick = 2
            });
        for (int i = 0; i < 40; i++) Assert.True(harness.BroadcastSnapshot(Snapshot()));
        harness.Advance(100);

        Assert.InRange(peer.SnapshotCount, 0, 40);
        Assert.Equal(0, harness.QueuedPacketCount);
        Assert.Contains("seed=1003", harness.GetReport().ToString());
    }

    [Fact]
    public void PeerCanReconnectToItsOriginalSlotAndContinueSequence()
    {
        HeadlessSessionHarness harness = Harness(1004);
        HeadlessSessionPeer peer = harness.AddClient(2);
        harness.Connect(peer);
        harness.Advance(10);
        Assert.True(harness.SendInput(peer, Input(1, 0)));
        harness.Advance(10);
        Assert.Equal(1, harness.Host.InputCount);

        harness.Drop(peer);
        Assert.Empty(harness.ConnectedSlots());
        Assert.False(harness.SendInput(peer, Input(2, 0)));
        Assert.True(harness.Reconnect(peer));
        harness.Advance(10);

        Assert.Equal((byte)1, peer.Slot);
        Assert.Equal(new byte[] { 1 }, harness.ConnectedSlots());
        Assert.True(harness.SendInput(peer, Input(3, 0)));
        harness.Advance(10);
        Assert.Equal(2, harness.Host.InputCount);
    }

    [Fact]
    public void HostLeftEndsEveryClientSession()
    {
        HeadlessSessionHarness harness = Harness(1005);
        HeadlessSessionPeer[] peers =
        {
            harness.AddClient(2), harness.AddClient(3), harness.AddClient(4)
        };
        for (int i = 0; i < peers.Length; i++) harness.Connect(peers[i]);
        harness.Advance(10);

        for (int i = 0; i < peers.Length; i++) Assert.True(harness.HostLeft(peers[i]));
        harness.Advance(10);

        for (int i = 0; i < peers.Length; i++)
        {
            Assert.Equal(SessionPacketResult.EndSession, peers[i].LastPacketResult);
            Assert.False(peers[i].IsAuthenticated);
        }
    }

    [Fact]
    public void FailureDiagnosticsIncludeSeedTimelineAndLastState()
    {
        HeadlessSessionHarness harness = Harness(424242);
        harness.AddClient(2);

        HeadlessSessionScenarioException error = Assert.Throws<HeadlessSessionScenarioException>(
            () => harness.Verify(false, "intentional failure"));

        Assert.Equal((uint)424242, error.Report.Seed);
        Assert.NotEmpty(error.Report.Timeline);
        Assert.Contains("slots=", error.Report.LastState);
        Assert.Contains("seed=424242", error.Message);
        Assert.Contains("client added id=2", error.Message);
    }

    [Fact]
    public void SameSeedAndScenarioProduceIdenticalReports()
    {
        Assert.Equal(RunReportedScenario(777).ToString(), RunReportedScenario(777).ToString());
    }

    [Theory]
    [InlineData(9001u)]
    [InlineData(9002u)]
    [InlineData(9003u)]
    public void LongRunningAdverseSessionRemainsBoundedAndRecovers(uint seed)
    {
        HeadlessSessionHarness harness = new HeadlessSessionHarness(seed, 0x12345678,
            WindowsBuild, new SessionNetworkProfile
            {
                MinimumLatencyTicks = 1,
                MaximumLatencyTicks = 7,
                UnreliableLossPercent = 8,
                UnreliableDuplicatePercent = 6,
                UnreliableCorruptionPercent = 2,
                BandwidthPacketsPerTick = 12
            });
        HeadlessSessionPeer[] peers =
        {
            harness.AddClient(2), harness.AddClient(3), harness.AddClient(4)
        };
        foreach (HeadlessSessionPeer peer in peers) Assert.True(harness.Connect(peer));
        harness.Advance(32);
        Assert.All(peers, peer => Assert.True(peer.IsAuthenticated));

        for (uint tick = 1; tick <= 12000; tick++)
        {
            for (int index = 0; index < peers.Length; index++)
            {
                byte edge = tick % 97 == (uint)index ? (byte)(1 << index) : (byte)0;
                Assert.True(harness.SendInput(peers[index], Input(tick, edge)));
            }
            if (tick % 3 == 0) Assert.True(harness.BroadcastSnapshot(Snapshot()));

            if (tick == 4000 || tick == 8000)
            {
                HeadlessSessionPeer peer = peers[tick == 4000 ? 0 : 2];
                harness.Drop(peer);
                Assert.False(harness.SendInput(peer, Input(tick, 0)));
                Assert.True(harness.Reconnect(peer));
            }

            harness.Advance(1);
            Assert.InRange(harness.QueuedPacketCount, 0, 1024);
        }

        harness.Advance(128);
        Assert.Equal(0, harness.QueuedPacketCount);
        Assert.Equal(new byte[] { 1, 2, 3 }, harness.ConnectedSlots());
        Assert.All(peers, peer => Assert.True(peer.IsAuthenticated));
        Assert.True(harness.Host.InputCount > 500, harness.GetReport() +
            " inputs=" + harness.Host.InputCount);
        Assert.All(peers, peer => Assert.True(peer.SnapshotCount > 0,
            harness.GetReport() + " snapshots=" + peer.SnapshotCount));

        var clean = new SessionNetworkProfile();
        foreach (HeadlessSessionPeer peer in peers)
        {
            foreach (SessionDelivery delivery in Enum.GetValues<SessionDelivery>())
            {
                harness.SetProfile(peer.Id, harness.Host.Id, delivery, clean);
                harness.SetProfile(harness.Host.Id, peer.Id, delivery, clean);
            }
            harness.Drop(peer);
            Assert.True(harness.Reconnect(peer));
        }
        harness.Advance(16);
        Assert.All(peers, peer => Assert.True(peer.IsAuthenticated));

        int inputCountBeforeRecovery = harness.Host.InputCount;
        int[] snapshotsBeforeRecovery = Array.ConvertAll(peers, peer => peer.SnapshotCount);
        for (uint tick = 12001; tick <= 12100; tick++)
        {
            foreach (HeadlessSessionPeer peer in peers)
                Assert.True(harness.SendInput(peer, Input(tick, 0)));
            Assert.True(harness.BroadcastSnapshot(Snapshot()));
            harness.Advance(1);
        }
        harness.Advance(16);

        Assert.Equal(inputCountBeforeRecovery + 300, harness.Host.InputCount);
        for (int index = 0; index < peers.Length; index++)
            Assert.Equal(snapshotsBeforeRecovery[index] + 100, peers[index].SnapshotCount);
        Assert.Equal(0, harness.QueuedPacketCount);
        Assert.Contains("seed=" + seed, harness.GetReport().ToString());
    }

    private static HeadlessSessionHarness Harness(uint seed)
    {
        return new HeadlessSessionHarness(seed, 0x12345678, WindowsBuild,
            new SessionNetworkProfile
            {
                MinimumLatencyTicks = 0,
                MaximumLatencyTicks = 3
            });
    }

    private static HeadlessSessionReport RunReportedScenario(uint seed)
    {
        HeadlessSessionHarness harness = Harness(seed);
        HeadlessSessionPeer first = harness.AddClient(2);
        HeadlessSessionPeer second = harness.AddClient(3);
        harness.Connect(first);
        harness.Connect(second);
        harness.Advance(20);
        harness.SendInput(first, Input(1, 1));
        harness.SendInput(second, Input(2, 2));
        harness.BroadcastSnapshot(Snapshot());
        harness.Advance(20);
        return harness.GetReport();
    }

    private static InputFrame Input(uint tick, byte edge)
    {
        return new InputFrame
        {
            Tick = tick,
            MoveX = 100,
            DownButtons = edge
        };
    }

    private static WorldSnapshot Snapshot()
    {
        return new WorldSnapshot
        {
            LastInputSequences = new uint[4],
            RandomStateWords = new uint[4]
        };
    }
}
