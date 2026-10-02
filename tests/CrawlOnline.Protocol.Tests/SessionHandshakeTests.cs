using CrawlOnline.Protocol;
using Xunit;

namespace CrawlOnline.Protocol.Tests;

public sealed class SessionHandshakeTests
{
    [Fact]
    public void HandshakePacketsRoundTrip()
    {
        var hello = new SessionHello
        {
            SessionNonce = 0x1122334455667788UL,
            SenderId = 76561198000000001UL,
            Attempt = 7,
            RequestedSlot = byte.MaxValue,
            Capabilities = SessionCapabilities.Current
        };
        Assert.True(PacketCodec.TryDecodeHello(PacketCodec.EncodeHello(hello), out SessionHello decodedHello));
        Assert.Equal(hello.SessionNonce, decodedHello.SessionNonce);
        Assert.Equal(hello.SenderId, decodedHello.SenderId);
        Assert.Equal(hello.Attempt, decodedHello.Attempt);
        Assert.Equal(hello.RequestedSlot, decodedHello.RequestedSlot);
        Assert.Equal(hello.Capabilities, decodedHello.Capabilities);

        var accepted = new SessionAccepted
        {
            SessionNonce = hello.SessionNonce,
            HostId = 76561198000000002UL,
            Attempt = hello.Attempt,
            AssignedSlot = 2,
            MaxPlayers = 4,
            Capabilities = SessionCapabilities.Current
        };
        Assert.True(PacketCodec.TryDecodeAccepted(PacketCodec.EncodeAccepted(accepted), out SessionAccepted decodedAccepted));
        Assert.Equal(accepted.SessionNonce, decodedAccepted.SessionNonce);
        Assert.Equal(accepted.HostId, decodedAccepted.HostId);
        Assert.Equal(accepted.Attempt, decodedAccepted.Attempt);
        Assert.Equal(accepted.AssignedSlot, decodedAccepted.AssignedSlot);

        var rejected = new SessionRejected
        {
            SessionNonce = hello.SessionNonce,
            Attempt = hello.Attempt,
            Reason = HandshakeRejectReason.LobbyFull
        };
        Assert.True(PacketCodec.TryDecodeRejected(PacketCodec.EncodeRejected(rejected), out SessionRejected decodedRejected));
        Assert.Equal(rejected.Reason, decodedRejected.Reason);
    }

    [Fact]
    public void HostAllocatesSlotsAndRejectsFifthPlayer()
    {
        var roster = new SessionRoster(100, 500, 4);
        for (ulong peer = 101; peer <= 103; peer++)
        {
            Assert.True(roster.TryAccept(peer, Hello(peer, 500, 1), out SessionAccepted accepted, out _));
            Assert.Equal((byte)(peer - 100), accepted.AssignedSlot);
        }

        Assert.False(roster.TryAccept(104, Hello(104, 500, 1), out _, out SessionRejected rejected));
        Assert.Equal(HandshakeRejectReason.LobbyFull, rejected.Reason);
    }

    [Fact]
    public void HostRejectsSpoofedStaleAndIncompatibleHello()
    {
        var roster = new SessionRoster(100, 500, 4);
        SessionHello spoofed = Hello(999, 500, 1);
        Assert.False(roster.TryAccept(101, spoofed, out _, out SessionRejected identity));
        Assert.Equal(HandshakeRejectReason.InvalidIdentity, identity.Reason);

        SessionHello incompatible = Hello(101, 500, 1);
        incompatible.Capabilities = 0;
        Assert.False(roster.TryAccept(101, incompatible, out _, out SessionRejected capabilities));
        Assert.Equal(HandshakeRejectReason.IncompatibleCapabilities, capabilities.Reason);

        Assert.True(roster.TryAccept(101, Hello(101, 500, 2), out _, out _));
        Assert.True(roster.MarkDisconnected(101));
        Assert.False(roster.TryAccept(101, Hello(101, 500, 2), out _, out SessionRejected stale));
        Assert.Equal(HandshakeRejectReason.StaleAttempt, stale.Reason);
    }

    [Fact]
    public void ReconnectRetainsSlotWithNewAttempt()
    {
        var roster = new SessionRoster(100, 500, 4);
        Assert.True(roster.TryAccept(101, Hello(101, 500, 1), out SessionAccepted first, out _));
        Assert.True(roster.MarkDisconnected(101));
        Assert.True(roster.TryAccept(101, Hello(101, 500, 2), out SessionAccepted reconnected, out _));
        Assert.Equal(first.AssignedSlot, reconnected.AssignedSlot);
        Assert.Equal((uint)2, reconnected.Attempt);
    }

    [Fact]
    public void DisconnectedPeerCannotAuthorizeGameplayUntilNewHello()
    {
        var roster = new SessionRoster(100, 500, 4);
        Assert.True(roster.TryAccept(101, Hello(101, 500, 1), out SessionAccepted first, out _));
        Assert.True(roster.TryGetSlot(101, out byte connectedSlot));
        Assert.Equal(first.AssignedSlot, connectedSlot);

        Assert.True(roster.MarkDisconnected(101));
        Assert.False(roster.TryGetSlot(101, out _));

        Assert.True(roster.TryAccept(101, Hello(101, 500, 2), out _, out _));
        Assert.True(roster.TryGetSlot(101, out byte reconnectedSlot));
        Assert.Equal(first.AssignedSlot, reconnectedSlot);
    }

    [Fact]
    public void ClientValidatesHostNonceAttemptSlotAndCapability()
    {
        var accepted = new SessionAccepted
        {
            SessionNonce = 500,
            HostId = 100,
            Attempt = 3,
            AssignedSlot = 1,
            MaxPlayers = 4,
            Capabilities = SessionCapabilities.Current
        };
        Assert.True(SessionRoster.ValidateAcceptance(accepted, 100, 500, 3));
        accepted.SessionNonce++;
        Assert.False(SessionRoster.ValidateAcceptance(accepted, 100, 500, 3));
    }

    private static SessionHello Hello(ulong sender, ulong nonce, uint attempt)
    {
        return new SessionHello
        {
            SenderId = sender,
            SessionNonce = nonce,
            Attempt = attempt,
            RequestedSlot = byte.MaxValue,
            Capabilities = SessionCapabilities.Current
        };
    }
}
