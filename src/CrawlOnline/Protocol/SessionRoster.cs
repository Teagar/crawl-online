using System;
using System.Collections.Generic;

namespace CrawlOnline.Protocol
{
    public sealed class SessionRoster
    {
        private sealed class Peer
        {
            public ulong Id;
            public uint Attempt;
            public byte Slot;
            public bool Connected;
        }

        private readonly ulong hostId;
        private readonly ulong sessionNonce;
        private readonly byte maxPlayers;
        private readonly Dictionary<ulong, Peer> peers = new Dictionary<ulong, Peer>();

        public SessionRoster(ulong authoritativeHostId, ulong nonce, byte playerLimit)
        {
            if (authoritativeHostId == 0) throw new ArgumentOutOfRangeException("authoritativeHostId");
            if (nonce == 0) throw new ArgumentOutOfRangeException("nonce");
            if (playerLimit < 2 || playerLimit > 4) throw new ArgumentOutOfRangeException("playerLimit");
            hostId = authoritativeHostId;
            sessionNonce = nonce;
            maxPlayers = playerLimit;
            peers.Add(hostId, new Peer { Id = hostId, Attempt = 1, Slot = 0, Connected = true });
        }

        public bool TryAccept(ulong transportSenderId, SessionHello hello,
            out SessionAccepted accepted, out SessionRejected rejected)
        {
            accepted = new SessionAccepted();
            rejected = MakeRejection(hello.Attempt, HandshakeRejectReason.None);
            if (transportSenderId == 0 || hello.SenderId != transportSenderId || transportSenderId == hostId)
            {
                rejected.Reason = HandshakeRejectReason.InvalidIdentity;
                return false;
            }
            if (hello.SessionNonce != sessionNonce)
            {
                rejected.Reason = HandshakeRejectReason.InvalidSession;
                return false;
            }
            if (hello.Attempt == 0)
            {
                rejected.Reason = HandshakeRejectReason.StaleAttempt;
                return false;
            }
            if ((hello.Capabilities & SessionCapabilities.AuthoritativeSnapshots) == 0)
            {
                rejected.Reason = HandshakeRejectReason.IncompatibleCapabilities;
                return false;
            }

            Peer existing;
            if (peers.TryGetValue(transportSenderId, out existing))
            {
                if (hello.Attempt < existing.Attempt ||
                    (hello.Attempt == existing.Attempt && !existing.Connected))
                {
                    rejected.Reason = HandshakeRejectReason.StaleAttempt;
                    return false;
                }
                if (hello.Attempt > existing.Attempt)
                {
                    existing.Attempt = hello.Attempt;
                    existing.Connected = true;
                }
                accepted = MakeAcceptance(existing);
                return true;
            }

            byte slot;
            if (!TryAllocateSlot(hello.RequestedSlot, out slot, out rejected.Reason))
            {
                return false;
            }
            var peer = new Peer
            {
                Id = transportSenderId,
                Attempt = hello.Attempt,
                Slot = slot,
                Connected = true
            };
            peers.Add(peer.Id, peer);
            accepted = MakeAcceptance(peer);
            return true;
        }

        public bool MarkDisconnected(ulong peerId)
        {
            Peer peer;
            if (!peers.TryGetValue(peerId, out peer) || peerId == hostId) return false;
            peer.Connected = false;
            return true;
        }

        public bool Remove(ulong peerId)
        {
            return peerId != hostId && peers.Remove(peerId);
        }

        public bool TryGetSlot(ulong peerId, out byte slot)
        {
            Peer peer;
            if (peers.TryGetValue(peerId, out peer))
            {
                slot = peer.Slot;
                return true;
            }
            slot = byte.MaxValue;
            return false;
        }

        public static bool ValidateAcceptance(SessionAccepted accepted, ulong expectedHostId,
            ulong expectedNonce, uint expectedAttempt)
        {
            return accepted.HostId == expectedHostId &&
                   accepted.SessionNonce == expectedNonce &&
                   accepted.Attempt == expectedAttempt &&
                   accepted.MaxPlayers >= 2 && accepted.MaxPlayers <= 4 &&
                   accepted.AssignedSlot < accepted.MaxPlayers &&
                   accepted.AssignedSlot != 0 &&
                   (accepted.Capabilities & SessionCapabilities.AuthoritativeSnapshots) != 0;
        }

        private bool TryAllocateSlot(byte requested, out byte slot, out HandshakeRejectReason reason)
        {
            if (requested != byte.MaxValue)
            {
                if (requested == 0 || requested >= maxPlayers)
                {
                    slot = byte.MaxValue;
                    reason = HandshakeRejectReason.InvalidSlot;
                    return false;
                }
                if (!IsSlotUsed(requested))
                {
                    slot = requested;
                    reason = HandshakeRejectReason.None;
                    return true;
                }
            }

            for (byte candidate = 1; candidate < maxPlayers; candidate++)
            {
                if (!IsSlotUsed(candidate))
                {
                    slot = candidate;
                    reason = HandshakeRejectReason.None;
                    return true;
                }
            }
            slot = byte.MaxValue;
            reason = HandshakeRejectReason.LobbyFull;
            return false;
        }

        private bool IsSlotUsed(byte slot)
        {
            foreach (Peer peer in peers.Values)
            {
                if (peer.Slot == slot) return true;
            }
            return false;
        }

        private SessionAccepted MakeAcceptance(Peer peer)
        {
            return new SessionAccepted
            {
                SessionNonce = sessionNonce,
                HostId = hostId,
                Attempt = peer.Attempt,
                AssignedSlot = peer.Slot,
                MaxPlayers = maxPlayers,
                Capabilities = SessionCapabilities.Current
            };
        }

        private SessionRejected MakeRejection(uint attempt, HandshakeRejectReason reason)
        {
            return new SessionRejected
            {
                SessionNonce = sessionNonce,
                Attempt = attempt,
                Reason = reason
            };
        }
    }
}
