using System;
using System.Collections.Generic;

namespace CrawlOnline.Protocol
{
    public enum SessionDelivery : byte
    {
        Unreliable = 0,
        UnreliableNoDelay = 1,
        Reliable = 2
    }

    public interface ISessionPacketTransport
    {
        bool Send(ulong peerId, byte[] packet, SessionDelivery delivery);
        void Close(ulong peerId);
    }

    public interface ISessionMembership
    {
        bool Contains(ulong peerId);
    }

    public enum SessionPacketResult : byte
    {
        Rejected = 0,
        HandshakeAccepted = 1,
        HandshakeRejected = 2,
        LocalAuthenticated = 3,
        LocalRejected = 4,
        InputAccepted = 5,
        InputEventAccepted = 6,
        SnapshotAccepted = 7,
        AcknowledgementAccepted = 8,
        PeerDisconnected = 9,
        EndSession = 10
    }

    public sealed class SessionProtocolEngine
    {
        private readonly ISessionPacketTransport transport;
        private readonly ISessionMembership membership;
        private readonly GameBuildFingerprint gameBuild;
        private readonly Dictionary<ulong, uint> peerAcknowledgements =
            new Dictionary<ulong, uint>();
        private readonly Dictionary<ulong, SnapshotHistory> peerSnapshotHistory =
            new Dictionary<ulong, SnapshotHistory>();
        private SessionRoster roster;
        private SequenceWindow receiveWindow;
        private ulong localId;
        private ulong hostId;
        private ulong sessionNonce;
        private uint localAttempt;
        private uint localInputSequence;
        private uint localInputEventSequence;
        private uint snapshotSequence;
        private byte localSlot = byte.MaxValue;

        public event Action<SessionInputFrame> InputReceived;
        public event Action<SessionInputFrame> InputEventReceived;
        public event Action<WorldSnapshot> SnapshotReceived;

        public SessionProtocolEngine(ISessionPacketTransport packetTransport,
            ISessionMembership sessionMembership, GameBuildFingerprint localGameBuild)
        {
            if (packetTransport == null) throw new ArgumentNullException("packetTransport");
            if (sessionMembership == null) throw new ArgumentNullException("sessionMembership");
            if (localGameBuild.IsEmpty) throw new ArgumentOutOfRangeException("localGameBuild");
            transport = packetTransport;
            membership = sessionMembership;
            gameBuild = localGameBuild;
        }

        public bool IsActive { get { return sessionNonce != 0; } }
        public bool IsHost { get { return IsActive && localId == hostId; } }
        public bool IsAuthenticated { get { return IsHost || localSlot != byte.MaxValue; } }
        public ulong SessionNonce { get { return sessionNonce; } }
        public ulong HostId { get { return hostId; } }
        public byte LocalSlot { get { return localSlot; } }
        public uint LocalAttempt { get { return localAttempt; } }

        public void StartHost(ulong authoritativeHostId, ulong nonce, byte maxPlayers)
        {
            if (authoritativeHostId == 0) throw new ArgumentOutOfRangeException("authoritativeHostId");
            Reset();
            localId = authoritativeHostId;
            hostId = authoritativeHostId;
            sessionNonce = nonce;
            localSlot = 0;
            roster = new SessionRoster(hostId, nonce, maxPlayers, gameBuild);
            receiveWindow = new SequenceWindow(nonce);
        }

        public bool StartClient(ulong clientId, ulong authoritativeHostId, ulong nonce,
            byte requestedSlot)
        {
            if (clientId == 0 || authoritativeHostId == 0 || clientId == authoritativeHostId)
                throw new ArgumentOutOfRangeException("clientId");
            Reset();
            localId = clientId;
            hostId = authoritativeHostId;
            sessionNonce = nonce;
            localSlot = requestedSlot;
            receiveWindow = new SequenceWindow(nonce);
            localAttempt = 1;
            return transport.Send(hostId, PacketCodec.EncodeHello(new SessionHello
            {
                SessionNonce = nonce,
                SenderId = clientId,
                Attempt = localAttempt,
                RequestedSlot = requestedSlot,
                Capabilities = SessionCapabilities.Current,
                GameBuild = gameBuild
            }), SessionDelivery.Reliable);
        }

        public byte[] GetConnectedPeerSlots()
        {
            return roster == null ? new byte[0] : roster.GetConnectedPeerSlots();
        }

        public bool TryGetPeerSlot(ulong peerId, out byte slot)
        {
            if (roster != null) return roster.TryGetSlot(peerId, out slot);
            slot = byte.MaxValue;
            return false;
        }

        public ulong[] GetConnectedPeerIds()
        {
            return roster == null ? new ulong[0] : roster.GetConnectedPeerIds();
        }

        public bool SendLocalInput(InputFrame input)
        {
            if (!IsActive || IsHost || localSlot == byte.MaxValue) return false;
            input.PlayerId = localSlot;
            byte downButtons = input.DownButtons;
            byte upButtons = input.UpButtons;
            input.DownButtons = 0;
            input.UpButtons = 0;
            localInputSequence = Next(localInputSequence);
            var sessionInput = new SessionInputFrame
            {
                SessionNonce = sessionNonce,
                Sequence = localInputSequence,
                Input = input
            };
            bool queued = transport.Send(hostId, AuthoritativeCodec.EncodeInput(sessionInput),
                SessionDelivery.UnreliableNoDelay);
            if (downButtons != 0 || upButtons != 0)
            {
                localInputEventSequence = Next(localInputEventSequence);
                sessionInput.Sequence = localInputEventSequence;
                sessionInput.Input.DownButtons = downButtons;
                sessionInput.Input.UpButtons = upButtons;
                queued &= transport.Send(hostId, AuthoritativeCodec.EncodeInputEvent(sessionInput),
                    SessionDelivery.Reliable);
            }
            return queued;
        }

        public bool BroadcastSnapshot(WorldSnapshot snapshot)
        {
            if (!IsHost || roster == null || snapshot == null) return false;
            snapshotSequence = Next(snapshotSequence);
            snapshot.SessionNonce = sessionNonce;
            snapshot.Sequence = snapshotSequence;
            byte[] packet = AuthoritativeCodec.EncodeSnapshot(snapshot);
            ulong[] peers = roster.GetConnectedPeerIds();
            bool queued = true;
            for (int i = 0; i < peers.Length; i++)
            {
                SnapshotHistory history;
                if (!peerSnapshotHistory.TryGetValue(peers[i], out history))
                {
                    history = new SnapshotHistory(32);
                    peerSnapshotHistory.Add(peers[i], history);
                }
                history.Add(snapshot);
                queued &= transport.Send(peers[i], packet, SessionDelivery.Unreliable);
            }
            return queued;
        }

        public bool AcknowledgeSnapshot(uint sequence)
        {
            if (!IsActive || IsHost || localSlot == byte.MaxValue || sequence == 0) return false;
            return transport.Send(hostId, AuthoritativeCodec.EncodeAcknowledgement(
                new SnapshotAcknowledgement { SessionNonce = sessionNonce, Sequence = sequence }),
                SessionDelivery.Reliable);
        }

        public bool SendDisconnect(ulong peerId)
        {
            if (!IsActive || peerId == 0 || peerId == localId) return false;
            bool sent = transport.Send(peerId, PacketCodec.EncodeControl(PacketType.Disconnect),
                SessionDelivery.Reliable);
            transport.Close(peerId);
            return sent;
        }

        public SessionPacketResult HandlePacket(ulong senderId, byte[] packet)
        {
            PacketType type;
            if (!IsActive || senderId == 0 || !membership.Contains(senderId) ||
                !PacketCodec.TryReadType(packet, out type)) return SessionPacketResult.Rejected;

            if (type == PacketType.Hello && IsHost)
            {
                SessionHello hello;
                if (!PacketCodec.TryDecodeHello(packet, out hello) || roster == null)
                    return SessionPacketResult.Rejected;
                SessionAccepted accepted;
                SessionRejected rejected;
                if (roster.TryAccept(senderId, hello, out accepted, out rejected))
                {
                    if (!transport.Send(senderId, PacketCodec.EncodeAccepted(accepted),
                        SessionDelivery.Reliable)) return SessionPacketResult.Rejected;
                    return SessionPacketResult.HandshakeAccepted;
                }
                transport.Send(senderId, PacketCodec.EncodeRejected(rejected), SessionDelivery.Reliable);
                return SessionPacketResult.HandshakeRejected;
            }
            if (type == PacketType.HelloAccepted && senderId == hostId && !IsHost)
            {
                SessionAccepted accepted;
                if (!PacketCodec.TryDecodeAccepted(packet, out accepted) ||
                    !SessionRoster.ValidateAcceptance(accepted, hostId, sessionNonce, localAttempt, gameBuild))
                    return SessionPacketResult.EndSession;
                localSlot = accepted.AssignedSlot;
                return SessionPacketResult.LocalAuthenticated;
            }
            if (type == PacketType.HelloRejected && senderId == hostId && !IsHost)
            {
                SessionRejected rejected;
                if (PacketCodec.TryDecodeRejected(packet, out rejected) &&
                    rejected.SessionNonce == sessionNonce && rejected.Attempt == localAttempt)
                    return SessionPacketResult.LocalRejected;
                return SessionPacketResult.Rejected;
            }
            if (type == PacketType.Disconnect)
            {
                transport.Close(senderId);
                if (IsHost && roster != null)
                {
                    roster.MarkDisconnected(senderId);
                    return SessionPacketResult.PeerDisconnected;
                }
                return senderId == hostId ? SessionPacketResult.EndSession : SessionPacketResult.Rejected;
            }
            if (type == PacketType.SessionInput && IsHost && roster != null && receiveWindow != null)
            {
                SessionInputFrame input;
                byte assignedSlot;
                if (!AuthoritativeCodec.TryDecodeInput(packet, out input) ||
                    !roster.TryGetSlot(senderId, out assignedSlot) ||
                    input.Input.PlayerId != assignedSlot || !receiveWindow.TryAcceptInput(input))
                    return SessionPacketResult.Rejected;
                Action<SessionInputFrame> callback = InputReceived;
                if (callback != null) callback(input);
                return SessionPacketResult.InputAccepted;
            }
            if (type == PacketType.InputEvent && IsHost && roster != null && receiveWindow != null)
            {
                SessionInputFrame inputEvent;
                byte assignedSlot;
                if (!AuthoritativeCodec.TryDecodeInputEvent(packet, out inputEvent) ||
                    !roster.TryGetSlot(senderId, out assignedSlot) ||
                    inputEvent.Input.PlayerId != assignedSlot ||
                    !receiveWindow.TryAcceptInputEvent(inputEvent))
                    return SessionPacketResult.Rejected;
                Action<SessionInputFrame> callback = InputEventReceived;
                if (callback != null) callback(inputEvent);
                return SessionPacketResult.InputEventAccepted;
            }
            if (type == PacketType.Snapshot && senderId == hostId && !IsHost &&
                localSlot != byte.MaxValue && receiveWindow != null)
            {
                WorldSnapshot snapshot;
                if (!AuthoritativeCodec.TryDecodeSnapshot(packet, out snapshot) ||
                    !receiveWindow.TryAcceptSnapshot(snapshot)) return SessionPacketResult.Rejected;
                Action<WorldSnapshot> callback = SnapshotReceived;
                if (callback != null) callback(snapshot);
                return SessionPacketResult.SnapshotAccepted;
            }
            if (type == PacketType.SnapshotAck && IsHost && roster != null)
            {
                SnapshotAcknowledgement acknowledgement;
                byte ignoredSlot;
                if (!AuthoritativeCodec.TryDecodeAcknowledgement(packet, out acknowledgement) ||
                    acknowledgement.SessionNonce != sessionNonce ||
                    !roster.TryGetSlot(senderId, out ignoredSlot)) return SessionPacketResult.Rejected;
                uint previous;
                if (!peerAcknowledgements.TryGetValue(senderId, out previous) ||
                    SequenceWindow.IsNewer(acknowledgement.Sequence, previous))
                {
                    peerAcknowledgements[senderId] = acknowledgement.Sequence;
                    SnapshotHistory history;
                    if (peerSnapshotHistory.TryGetValue(senderId, out history))
                        history.Acknowledge(acknowledgement.Sequence);
                }
                return SessionPacketResult.AcknowledgementAccepted;
            }
            return SessionPacketResult.Rejected;
        }

        public void MarkTransportFailed(ulong peerId)
        {
            if (peerId == 0) return;
            transport.Close(peerId);
            if (IsHost && roster != null) roster.MarkDisconnected(peerId);
        }

        public void RemovePeer(ulong peerId)
        {
            if (peerId == 0) return;
            transport.Close(peerId);
            if (roster != null) roster.Remove(peerId);
            peerAcknowledgements.Remove(peerId);
            peerSnapshotHistory.Remove(peerId);
        }

        public void Reset()
        {
            roster = null;
            receiveWindow = null;
            localId = 0;
            hostId = 0;
            sessionNonce = 0;
            localAttempt = 0;
            localInputSequence = 0;
            localInputEventSequence = 0;
            snapshotSequence = 0;
            localSlot = byte.MaxValue;
            peerAcknowledgements.Clear();
            peerSnapshotHistory.Clear();
        }

        private static uint Next(uint sequence)
        {
            sequence++;
            return sequence == 0 ? 1 : sequence;
        }
    }
}
