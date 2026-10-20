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
        EndSession = 10,
        PregameStateAccepted = 11
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
        private readonly Dictionary<ulong, uint> peerPendingPlanSequences = new Dictionary<ulong, uint>();
        private readonly Dictionary<ulong, uint> peerPendingPlanGenerations = new Dictionary<ulong, uint>();
        private readonly Dictionary<ulong, uint> peerDeliveredPlanGenerations = new Dictionary<ulong, uint>();
        private SessionRoster roster;
        private SequenceWindow receiveWindow;
        private ulong localId;
        private ulong hostId;
        private ulong sessionNonce;
        private uint localAttempt;
        private uint localInputSequence;
        private uint localInputEventSequence;
        private uint snapshotSequence;
        private uint pregameStateSequence;
        private byte requestedSlot = byte.MaxValue;
        private byte localSlot = byte.MaxValue;

        public event Action<SessionInputFrame> InputReceived;
        public event Action<SessionInputFrame> InputEventReceived;
        public event Action<WorldSnapshot> SnapshotReceived;
        public event Action<SessionPregameState> PregameStateReceived;

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
        public bool HasPendingMapPlanAcknowledgements
        {
            get { return IsHost && peerPendingPlanSequences.Count != 0; }
        }
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
            this.requestedSlot = requestedSlot;
            localSlot = byte.MaxValue;
            receiveWindow = new SequenceWindow(nonce);
            localAttempt = 1;
            return transport.Send(hostId, PacketCodec.EncodeHello(new SessionHello
            {
                SessionNonce = nonce,
                SenderId = clientId,
                Attempt = localAttempt,
                RequestedSlot = this.requestedSlot,
                Capabilities = SessionCapabilities.Current,
                GameBuild = gameBuild
            }), SessionDelivery.Reliable);
        }

        public bool ReconnectClient()
        {
            if (!IsActive || IsHost || localId == 0 || hostId == 0) return false;
            if (receiveWindow != null) receiveWindow.ResetSnapshots();
            localAttempt = Next(localAttempt);
            return transport.Send(hostId, PacketCodec.EncodeHello(new SessionHello
            {
                SessionNonce = sessionNonce,
                SenderId = localId,
                Attempt = localAttempt,
                RequestedSlot = localSlot == byte.MaxValue ? requestedSlot : localSlot,
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
            ulong[] peers = roster.GetConnectedPeerIds();
            bool queued = true;
            for (int i = 0; i < peers.Length; i++)
            {
                if (peerPendingPlanSequences.ContainsKey(peers[i])) continue;
                bool hasMapPlan = snapshot.GameStartGeneration != 0 &&
                    ((snapshot.RoomArchetypeHashes != null && snapshot.RoomArchetypeHashes.Length != 0) ||
                     (snapshot.SpecialRoomArchetypeHashes != null && snapshot.SpecialRoomArchetypeHashes.Length != 0) ||
                     (snapshot.RoomCollisionDecisions != null && snapshot.RoomCollisionDecisions.Length != 0) ||
                     (snapshot.RoomSelectionDecisions != null && snapshot.RoomSelectionDecisions.Length != 0));
                uint deliveredGeneration;
                bool needsMapPlan = hasMapPlan &&
                    (!peerDeliveredPlanGenerations.TryGetValue(peers[i], out deliveredGeneration) ||
                     deliveredGeneration != snapshot.GameStartGeneration);
                WorldSnapshot outgoing = needsMapPlan ? snapshot : WithoutMapPlan(snapshot);
                byte[] packet = AuthoritativeCodec.EncodeSnapshot(outgoing);
                SnapshotHistory history;
                if (!peerSnapshotHistory.TryGetValue(peers[i], out history))
                {
                    history = new SnapshotHistory(32);
                    peerSnapshotHistory.Add(peers[i], history);
                }
                history.Add(snapshot);
                SessionDelivery delivery = needsMapPlan ? SessionDelivery.Reliable : SessionDelivery.Unreliable;
                bool sent = transport.Send(peers[i], packet, delivery);
                queued &= sent;
                if (sent && needsMapPlan)
                {
                    peerPendingPlanSequences[peers[i]] = snapshot.Sequence;
                    peerPendingPlanGenerations[peers[i]] = snapshot.GameStartGeneration;
                }
            }
            return queued;
        }

        public bool BroadcastPregameState(PregamePhase phase)
        {
            if (!IsHost || roster == null || !Enum.IsDefined(typeof(PregamePhase), phase)) return false;
            pregameStateSequence = Next(pregameStateSequence);
            var state = new SessionPregameState
            {
                SessionNonce = sessionNonce,
                Sequence = pregameStateSequence,
                Phase = phase
            };
            ulong[] peers = roster.GetConnectedPeerIds();
            bool queued = true;
            for (int i = 0; i < peers.Length; i++)
                queued &= transport.Send(peers[i], AuthoritativeCodec.EncodePregameState(state),
                    SessionDelivery.Reliable);
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
                    if (receiveWindow != null) receiveWindow.ResetInput(accepted.AssignedSlot);
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
            if (type == PacketType.PregameState && senderId == hostId && !IsHost &&
                localSlot != byte.MaxValue && receiveWindow != null)
            {
                SessionPregameState state;
                if (!AuthoritativeCodec.TryDecodePregameState(packet, out state) ||
                    !receiveWindow.TryAcceptPregameState(state)) return SessionPacketResult.Rejected;
                Action<SessionPregameState> callback = PregameStateReceived;
                if (callback != null) callback(state);
                return SessionPacketResult.PregameStateAccepted;
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
                    uint pendingSequence;
                    if (peerPendingPlanSequences.TryGetValue(senderId, out pendingSequence) &&
                        (acknowledgement.Sequence == pendingSequence ||
                         SequenceWindow.IsNewer(acknowledgement.Sequence, pendingSequence)))
                    {
                        uint generation;
                        if (peerPendingPlanGenerations.TryGetValue(senderId, out generation))
                            peerDeliveredPlanGenerations[senderId] = generation;
                        peerPendingPlanSequences.Remove(senderId);
                        peerPendingPlanGenerations.Remove(senderId);
                    }
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
            peerPendingPlanSequences.Remove(peerId);
            peerPendingPlanGenerations.Remove(peerId);
            peerDeliveredPlanGenerations.Remove(peerId);
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
            pregameStateSequence = 0;
            requestedSlot = byte.MaxValue;
            localSlot = byte.MaxValue;
            peerAcknowledgements.Clear();
            peerSnapshotHistory.Clear();
            peerPendingPlanSequences.Clear();
            peerPendingPlanGenerations.Clear();
            peerDeliveredPlanGenerations.Clear();
        }

        private static WorldSnapshot WithoutMapPlan(WorldSnapshot snapshot)
        {
            return new WorldSnapshot
            {
                SessionNonce = snapshot.SessionNonce,
                Sequence = snapshot.Sequence,
                HostTick = snapshot.HostTick,
                LastInputSequences = snapshot.LastInputSequences,
                Flags = snapshot.Flags,
                Level = snapshot.Level,
                RoomX = snapshot.RoomX,
                RoomY = snapshot.RoomY,
                RoomDepth = snapshot.RoomDepth,
                RoomIndex = snapshot.RoomIndex,
                TransitionGeneration = snapshot.TransitionGeneration,
                RandomStateHash = snapshot.RandomStateHash,
                RandomStateWords = snapshot.RandomStateWords,
                GameStartGeneration = snapshot.GameStartGeneration,
                GameStartRandomStateWords = snapshot.GameStartRandomStateWords,
                RoomContentGeneration = snapshot.RoomContentGeneration,
                RoomContentRoomIndex = snapshot.RoomContentRoomIndex,
                RoomContentRandomStateWords = snapshot.RoomContentRandomStateWords,
                RoomArchetypeHashes = new ulong[0],
                SpecialRoomArchetypeHashes = new ulong[0],
                RoomCollisionDecisions = new RoomCollisionDecision[0],
                RoomSelectionDecisions = new RoomSelectionDecision[0],
                RoomLayout = new RoomLayoutState[0],
                Players = snapshot.Players,
                Enemies = snapshot.Enemies
            };
        }

        private static uint Next(uint sequence)
        {
            sequence++;
            return sequence == 0 ? 1 : sequence;
        }
    }
}
