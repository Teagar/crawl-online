using System.Collections.Generic;
using CrawlOnline.Protocol;
using Xunit;

namespace CrawlOnline.Protocol.Tests;

public sealed class InMemorySessionNetworkTests
{
    [Fact]
    public void SameSeedAndScriptProduceTheSameTimeline()
    {
        Assert.Equal(RunTimeline(293780), RunTimeline(293780));
    }

    [Fact]
    public void DirectionalProfilesCanDropMovementWithoutDroppingReliableEdges()
    {
        var network = Network(7, Profile());
        InMemorySessionEndpoint sender = network.CreateEndpoint(1);
        InMemorySessionEndpoint receiver = network.CreateEndpoint(2);
        var received = new List<byte>();
        receiver.PacketReceived += (_, packet) => received.Add(packet[6]);
        network.SetProfile(1, 2, SessionDelivery.UnreliableNoDelay, new SessionNetworkProfile
        {
            UnreliableLossPercent = 100
        });

        Assert.True(sender.Send(2, Packet(1), SessionDelivery.UnreliableNoDelay));
        Assert.True(sender.Send(2, Packet(2), SessionDelivery.Reliable));
        network.Advance(1);

        Assert.Equal(new byte[] { 2 }, received);
    }

    [Fact]
    public void BandwidthCanBeLimitedPerDirectionAndChannel()
    {
        var network = Network(7, Profile());
        InMemorySessionEndpoint sender = network.CreateEndpoint(1);
        InMemorySessionEndpoint receiver = network.CreateEndpoint(2);
        var received = new List<byte>();
        receiver.PacketReceived += (_, packet) => received.Add(packet[6]);
        network.SetProfile(1, 2, SessionDelivery.Unreliable, new SessionNetworkProfile
        {
            BandwidthPacketsPerTick = 1
        });

        Assert.True(sender.Send(2, Packet(1), SessionDelivery.Unreliable));
        Assert.True(sender.Send(2, Packet(2), SessionDelivery.Unreliable));
        Assert.True(sender.Send(2, Packet(3), SessionDelivery.Reliable));
        network.Advance(1);

        Assert.Equal(new byte[] { 1, 3 }, received);
        Assert.Equal(1, network.QueuedPacketCount);
        network.Advance(1);
        Assert.Equal(new byte[] { 1, 3, 2 }, received);
    }

    [Fact]
    public void ReliablePacketsPreserveOrderAcrossJitter()
    {
        var network = Network(99, new SessionNetworkProfile
        {
            MinimumLatencyTicks = 0,
            MaximumLatencyTicks = 8
        });
        InMemorySessionEndpoint sender = network.CreateEndpoint(1);
        InMemorySessionEndpoint receiver = network.CreateEndpoint(2);
        var received = new List<byte>();
        receiver.PacketReceived += (_, packet) => received.Add(packet[6]);

        for (byte value = 1; value <= 20; value++)
            Assert.True(sender.Send(2, Packet(value), SessionDelivery.Reliable));
        network.Advance(20);

        Assert.Equal(20, received.Count);
        for (byte value = 1; value <= 20; value++) Assert.Equal(value, received[value - 1]);
    }

    [Fact]
    public void UnreliableJitterCanReorderAndDuplicatePackets()
    {
        var network = Network(293780, new SessionNetworkProfile
        {
            MinimumLatencyTicks = 0,
            MaximumLatencyTicks = 8,
            UnreliableDuplicatePercent = 100
        });
        InMemorySessionEndpoint sender = network.CreateEndpoint(1);
        InMemorySessionEndpoint receiver = network.CreateEndpoint(2);
        var received = new List<byte>();
        receiver.PacketReceived += (_, packet) => received.Add(packet[6]);

        for (byte value = 0; value < 12; value++)
            Assert.True(sender.Send(2, Packet(value), SessionDelivery.Unreliable));
        network.Advance(20);

        Assert.Equal(24, received.Count);
        bool reordered = false;
        for (int i = 1; i < received.Count; i++)
        {
            if (received[i] < received[i - 1]) { reordered = true; break; }
        }
        Assert.True(reordered);
    }

    [Fact]
    public void QueuePacketAndBandwidthLimitsFailClosed()
    {
        var network = new InMemorySessionNetwork(1, Profile(), 2, 32, 1);
        InMemorySessionEndpoint sender = network.CreateEndpoint(1);
        InMemorySessionEndpoint receiver = network.CreateEndpoint(2);
        int received = 0;
        receiver.PacketReceived += (_, __) => received++;

        Assert.False(sender.Send(2, new byte[33], SessionDelivery.Reliable));
        Assert.True(sender.Send(2, Packet(1), SessionDelivery.Reliable));
        Assert.True(sender.Send(2, Packet(2), SessionDelivery.Reliable));
        Assert.False(sender.Send(2, Packet(3), SessionDelivery.Reliable));
        Assert.Equal(2, network.QueuedPacketCount);

        network.Advance(1);
        Assert.Equal(1, received);
        Assert.Equal(1, network.QueuedPacketCount);
        network.Advance(1);
        Assert.Equal(2, received);
    }

    [Fact]
    public void CloseAndReconnectControlBothDirectionsWithoutDiscardingQueuedPackets()
    {
        var network = Network(1, new SessionNetworkProfile { MinimumLatencyTicks = 2, MaximumLatencyTicks = 2 });
        InMemorySessionEndpoint first = network.CreateEndpoint(1);
        InMemorySessionEndpoint second = network.CreateEndpoint(2);
        int received = 0;
        second.PacketReceived += (_, __) => received++;

        Assert.True(first.Send(2, Packet(1), SessionDelivery.Reliable));
        first.Close(2);
        Assert.False(first.Send(2, Packet(2), SessionDelivery.Reliable));
        Assert.False(second.Send(1, Packet(3), SessionDelivery.Reliable));
        network.Advance(2);
        Assert.Equal(1, received);

        network.Reconnect(1, 2);
        Assert.True(first.Send(2, Packet(4), SessionDelivery.Reliable));
    }

    [Fact]
    public void ScheduledDisconnectAndReconnectAreAppliedAtExactTicks()
    {
        var network = Network(1, Profile());
        InMemorySessionEndpoint first = network.CreateEndpoint(1);
        network.CreateEndpoint(2);
        network.ScheduleDisconnect(1, 2, 2);
        network.ScheduleReconnect(1, 2, 4);

        Assert.True(first.Send(2, Packet(1), SessionDelivery.Reliable));
        network.Advance(1);
        Assert.True(first.Send(2, Packet(2), SessionDelivery.Reliable));
        network.Advance(1);
        Assert.False(first.Send(2, Packet(3), SessionDelivery.Reliable));
        network.Advance(2);
        Assert.True(first.Send(2, Packet(4), SessionDelivery.Reliable));
    }

    [Fact]
    public void CorruptionIsDeterministicAndNeverMutatesCallerBuffer()
    {
        var network = Network(42, new SessionNetworkProfile { UnreliableCorruptionPercent = 100 });
        InMemorySessionEndpoint sender = network.CreateEndpoint(1);
        InMemorySessionEndpoint receiver = network.CreateEndpoint(2);
        byte[] original = Packet(9);
        byte[] delivered = null;
        receiver.PacketReceived += (_, packet) => delivered = packet;

        Assert.True(sender.Send(2, original, SessionDelivery.Unreliable));
        network.Advance(1);

        Assert.Equal((byte)9, original[6]);
        Assert.NotNull(delivered);
        Assert.NotEqual(original, delivered);
    }

    private static List<string> RunTimeline(uint seed)
    {
        var profile = new SessionNetworkProfile
        {
            MinimumLatencyTicks = 1,
            MaximumLatencyTicks = 5,
            UnreliableLossPercent = 20,
            UnreliableDuplicatePercent = 30
        };
        var network = Network(seed, profile);
        InMemorySessionEndpoint first = network.CreateEndpoint(1);
        InMemorySessionEndpoint second = network.CreateEndpoint(2);
        var timeline = new List<string>();
        second.PacketReceived += (sender, packet) =>
            timeline.Add(network.Tick + ":" + sender + ":" + packet[6]);
        for (byte value = 0; value < 40; value++)
            first.Send(2, Packet(value), SessionDelivery.Unreliable);
        network.Advance(20);
        return timeline;
    }

    private static InMemorySessionNetwork Network(uint seed, SessionNetworkProfile profile)
    {
        return new InMemorySessionNetwork(seed, profile, 256, 1024, 256);
    }

    private static SessionNetworkProfile Profile()
    {
        return new SessionNetworkProfile();
    }

    private static byte[] Packet(byte value)
    {
        return new byte[] { 0x4c, 0x4e, 0x4f, 0x43, PacketCodec.ProtocolVersion, 6, value };
    }
}
