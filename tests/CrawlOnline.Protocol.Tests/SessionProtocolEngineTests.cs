using System.Collections.Generic;
using CrawlOnline.Protocol;
using Xunit;

namespace CrawlOnline.Protocol.Tests;

public sealed class SessionProtocolEngineTests
{
    private static readonly GameBuildFingerprint WindowsBuild =
        GameBuildFingerprint.Parse("e93e8fb49fd3c3ebe622d0f9f9557c1e4dd475c2a277be19e2c05cbb1f05f61e");

    [Fact]
    public void HostAndClientUseTheSameTransportAgnosticHandshakeAndGameplayPath()
    {
        var membership = new FakeMembership(100, 101);
        var hostTransport = new FakeTransport();
        var clientTransport = new FakeTransport();
        var host = new SessionProtocolEngine(hostTransport, membership, WindowsBuild);
        var client = new SessionProtocolEngine(clientTransport, membership, WindowsBuild);
        host.StartHost(100, 500, 4);

        Assert.True(client.StartClient(101, 100, 500, byte.MaxValue));
        SentPacket hello = Assert.Single(clientTransport.Sent);
        Assert.Equal(SessionDelivery.Reliable, hello.Delivery);
        Assert.Equal(SessionPacketResult.HandshakeAccepted, host.HandlePacket(101, hello.Data));

        SentPacket accepted = Assert.Single(hostTransport.Sent);
        Assert.Equal(SessionPacketResult.LocalAuthenticated, client.HandlePacket(100, accepted.Data));
        Assert.Equal((byte)1, client.LocalSlot);
        Assert.Equal(new byte[] { 1 }, host.GetConnectedPeerSlots());

        SessionInputFrame receivedInput = new SessionInputFrame();
        host.InputReceived += input => receivedInput = input;
        Assert.True(client.SendLocalInput(new InputFrame
        {
            Tick = 12,
            MoveX = 123,
            DownButtons = 1
        }));
        Assert.Equal(3, clientTransport.Sent.Count);
        Assert.Equal(SessionDelivery.UnreliableNoDelay, clientTransport.Sent[1].Delivery);
        Assert.Equal(SessionDelivery.Reliable, clientTransport.Sent[2].Delivery);
        Assert.Equal(SessionPacketResult.InputAccepted,
            host.HandlePacket(101, clientTransport.Sent[1].Data));
        Assert.Equal((byte)1, receivedInput.Input.PlayerId);
        Assert.Equal((short)123, receivedInput.Input.MoveX);
        Assert.Equal(SessionPacketResult.InputEventAccepted,
            host.HandlePacket(101, clientTransport.Sent[2].Data));

        WorldSnapshot receivedSnapshot = null;
        client.SnapshotReceived += snapshot => receivedSnapshot = snapshot;
        Assert.True(host.BroadcastSnapshot(Snapshot()));
        SentPacket snapshotPacket = hostTransport.Sent[1];
        Assert.Equal(SessionDelivery.Unreliable, snapshotPacket.Delivery);
        Assert.Equal(SessionPacketResult.SnapshotAccepted,
            client.HandlePacket(100, snapshotPacket.Data));
        Assert.NotNull(receivedSnapshot);
        Assert.True(client.AcknowledgeSnapshot(receivedSnapshot.Sequence));
        Assert.Equal(SessionPacketResult.AcknowledgementAccepted,
            host.HandlePacket(101, clientTransport.Sent[3].Data));
    }

    [Fact]
    public void MembershipAndTransportIdentityRemainFailClosed()
    {
        var membership = new FakeMembership(100, 101);
        var transport = new FakeTransport();
        var host = new SessionProtocolEngine(transport, membership, WindowsBuild);
        host.StartHost(100, 500, 4);
        byte[] validHello = PacketCodec.EncodeHello(new SessionHello
        {
            SessionNonce = 500,
            SenderId = 101,
            Attempt = 1,
            RequestedSlot = byte.MaxValue,
            Capabilities = SessionCapabilities.Current,
            GameBuild = WindowsBuild
        });

        Assert.Equal(SessionPacketResult.Rejected, host.HandlePacket(102, validHello));
        Assert.Empty(transport.Sent);
        Assert.Empty(host.GetConnectedPeerSlots());

        membership.Add(102);
        Assert.Equal(SessionPacketResult.HandshakeRejected, host.HandlePacket(102, validHello));
        Assert.Empty(host.GetConnectedPeerSlots());
        Assert.True(PacketCodec.TryDecodeRejected(Assert.Single(transport.Sent).Data,
            out SessionRejected rejected));
        Assert.Equal(HandshakeRejectReason.InvalidIdentity, rejected.Reason);
    }

    [Fact]
    public void DisconnectAndPeerRemovalClearAuthorization()
    {
        var membership = new FakeMembership(100, 101);
        var hostTransport = new FakeTransport();
        var clientTransport = new FakeTransport();
        var host = new SessionProtocolEngine(hostTransport, membership, WindowsBuild);
        var client = new SessionProtocolEngine(clientTransport, membership, WindowsBuild);
        host.StartHost(100, 500, 4);
        client.StartClient(101, 100, 500, byte.MaxValue);
        host.HandlePacket(101, clientTransport.Sent[0].Data);
        client.HandlePacket(100, hostTransport.Sent[0].Data);

        Assert.True(client.SendDisconnect(100));
        Assert.Equal(SessionPacketResult.PeerDisconnected,
            host.HandlePacket(101, clientTransport.Sent[1].Data));
        Assert.Empty(host.GetConnectedPeerSlots());
        Assert.Contains((ulong)101, hostTransport.Closed);

        host.RemovePeer(101);
        Assert.Empty(host.GetConnectedPeerIds());
    }

    private static WorldSnapshot Snapshot()
    {
        return new WorldSnapshot
        {
            LastInputSequences = new uint[4],
            RandomStateWords = new uint[4]
        };
    }

    private sealed class FakeMembership : ISessionMembership
    {
        private readonly HashSet<ulong> members = new HashSet<ulong>();

        public FakeMembership(params ulong[] initial)
        {
            foreach (ulong member in initial) members.Add(member);
        }

        public bool Contains(ulong peerId) => members.Contains(peerId);
        public void Add(ulong peerId) => members.Add(peerId);
    }

    private sealed class FakeTransport : ISessionPacketTransport
    {
        public readonly List<SentPacket> Sent = new List<SentPacket>();
        public readonly List<ulong> Closed = new List<ulong>();

        public bool Send(ulong peerId, byte[] packet, SessionDelivery delivery)
        {
            Sent.Add(new SentPacket(peerId, packet, delivery));
            return true;
        }

        public void Close(ulong peerId)
        {
            Closed.Add(peerId);
        }
    }

    private sealed class SentPacket
    {
        public SentPacket(ulong peerId, byte[] data, SessionDelivery delivery)
        {
            PeerId = peerId;
            Data = data;
            Delivery = delivery;
        }

        public ulong PeerId { get; }
        public byte[] Data { get; }
        public SessionDelivery Delivery { get; }
    }
}
