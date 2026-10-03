using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Collections.Generic;
using BepInEx.Logging;
using CrawlOnline.Online;
using CrawlOnline.Protocol;
using Steamworks;

namespace CrawlOnline
{
    internal sealed class SteamLobbySession : IDisposable
    {
        private const int Channel = 7;
        private const int MaxPacketSize = 64 * 1024;
        private const string SessionProtocolVersion = "5";
        private const string LobbyPacketProtocolKey = "crawl-online-protocol";
        private const string LobbySessionProtocolKey = "crawl-online-session-protocol";
        private const string LobbyBuildKey = "crawl-online-build";
        private const string LobbyGameBuildKey = "crawl-online-game-build";
        private const string LobbyNonceKey = "crawl-online-session-nonce";
        private readonly ManualLogSource log;
        private readonly GameBuildFingerprint gameBuild;
        private readonly string gameBuildMetadata;
        private readonly byte[] receiveBuffer = new byte[MaxPacketSize];
        private readonly CallResult<LobbyCreated_t> lobbyCreated;
        private readonly CallResult<LobbyEnter_t> lobbyEntered;
        private readonly CallResult<LobbyMatchList_t> lobbyMatches;
        private readonly Callback<GameLobbyJoinRequested_t> joinRequested;
        private readonly Callback<P2PSessionRequest_t> sessionRequested;
        private readonly Callback<P2PSessionConnectFail_t> sessionConnectFailed;
        private readonly Callback<LobbyChatUpdate_t> lobbyChatUpdated;
        private CSteamID lobbyId = CSteamID.Nil;
        private CSteamID ownerId = CSteamID.Nil;
        private SessionRoster roster;
        private ulong sessionNonce;
        private uint localAttempt;
        private byte localSlot = byte.MaxValue;
        private SequenceWindow receiveWindow;
        private readonly Dictionary<ulong, uint> peerAcknowledgements = new Dictionary<ulong, uint>();
        private readonly Dictionary<ulong, SnapshotHistory> peerSnapshotHistory = new Dictionary<ulong, SnapshotHistory>();
        private uint localInputSequence;
        private uint localInputEventSequence;
        private uint snapshotSequence;
        private SessionHudStatus hudStatus = SessionHudStatus.Offline;
        private string hudMessage = "Offline — F8 creates a friends-only lobby.";
        private bool cancelPendingHost;
        private bool cancelPendingJoin;
        private bool joinPending;
        private long discoveryOperation;

        public event Action<SessionInputFrame> InputReceived;
        public event Action<SessionInputFrame> InputEventReceived;
        public event Action<WorldSnapshot> SnapshotReceived;
        public event Action<long, ulong[]> FriendLobbiesDiscovered;
        public event Action<ulong> LobbyJoinRequested;

        public SteamLobbySession(ManualLogSource logSource, GameBuildFingerprint localGameBuild)
        {
            if (localGameBuild.IsEmpty) throw new ArgumentOutOfRangeException("localGameBuild");
            log = logSource;
            gameBuild = localGameBuild;
            gameBuildMetadata = localGameBuild.ToString();
            lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            lobbyEntered = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
            lobbyMatches = CallResult<LobbyMatchList_t>.Create(OnLobbyMatchList);
            joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
            sessionRequested = Callback<P2PSessionRequest_t>.Create(OnSessionRequested);
            sessionConnectFailed = Callback<P2PSessionConnectFail_t>.Create(OnSessionConnectFailed);
            lobbyChatUpdated = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdated);
            SteamNetworking.AllowP2PPacketRelay(true);
        }

        public bool InLobby
        {
            get { return lobbyId != CSteamID.Nil; }
        }

        public bool IsAuthoritativeHost
        {
            get { return InLobby && SteamUser.GetSteamID() == ownerId; }
        }

        public byte LocalSlot
        {
            get { return localSlot; }
        }

        public ulong SessionNonce
        {
            get { return sessionNonce; }
        }

        public byte[] GetConnectedPeerSlots()
        {
            return roster == null ? new byte[0] : roster.GetConnectedPeerSlots();
        }

        public SessionHudState GetHudState()
        {
            bool[] slots = new bool[4];
            bool isHost = IsAuthoritativeHost;
            int visibleLocalSlot = localSlot == byte.MaxValue ? -1 : localSlot;
            if (InLobby)
            {
                slots[0] = true;
                if (isHost && roster != null)
                {
                    byte[] peers = roster.GetConnectedPeerSlots();
                    for (int i = 0; i < peers.Length; i++)
                    {
                        if (peers[i] < slots.Length) slots[peers[i]] = true;
                    }
                }
                else if (visibleLocalSlot >= 0 && visibleLocalSlot < slots.Length)
                {
                    slots[visibleLocalSlot] = true;
                }
            }
            return new SessionHudState(hudStatus, isHost, visibleLocalSlot, slots, hudMessage);
        }

        public void Host()
        {
            if (hudStatus == SessionHudStatus.CreatingLobby)
            {
                log.LogWarning("Lobby creation is already pending.");
                return;
            }
            if (InLobby)
            {
                log.LogWarning("Already in lobby " + lobbyId);
                return;
            }

            log.LogInfo("Creating friends-only Crawl Online lobby...");
            cancelPendingHost = false;
            SetHudStatus(SessionHudStatus.CreatingLobby, "Creating friends-only lobby…");
            lobbyCreated.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 4));
        }

        public void CancelHostOrLeave()
        {
            if (!InLobby && hudStatus == SessionHudStatus.CreatingLobby)
            {
                cancelPendingHost = true;
                SetHudStatus(SessionHudStatus.CreatingLobby, "Cancelling lobby creation…");
                log.LogInfo("Lobby creation cancellation requested; waiting for Steam callback cleanup.");
                return;
            }
            Leave();
        }

        public void OpenInviteDialog()
        {
            if (!InLobby)
            {
                log.LogWarning("Create or join a lobby before inviting friends.");
                SetHudStatus(SessionHudStatus.Error, "Create or join a lobby before inviting.");
                return;
            }

            SteamFriends.ActivateGameOverlayInviteDialog(lobbyId);
            log.LogInfo("Opened Steam invite dialog for lobby " + lobbyId + ".");
        }

        public void DiscoverFriendLobbies(long operation)
        {
            if (InLobby)
            {
                log.LogWarning("Leave the current lobby before looking for a friend.");
                return;
            }
            discoveryOperation = operation;
            SteamMatchmaking.AddRequestLobbyListStringFilter(LobbyPacketProtocolKey,
                PacketCodec.ProtocolVersion.ToString(CultureInfo.InvariantCulture),
                ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter(LobbySessionProtocolKey,
                SessionProtocolVersion, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter(LobbyBuildKey,
                CrawlOnlineRuntime.Version, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter(LobbyGameBuildKey,
                gameBuildMetadata, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(20);
            SetHudStatus(SessionHudStatus.Offline, "Looking for compatible friend lobbies…");
            lobbyMatches.Set(SteamMatchmaking.RequestLobbyList());
            log.LogInfo("Searching Steam for compatible friend lobbies.");
        }

        public void JoinDiscoveredLobby(ulong discoveredLobby, long operation)
        {
            if (discoveredLobby == 0 || InLobby) return;
            discoveryOperation = operation;
            cancelPendingJoin = false;
            joinPending = true;
            SetHudStatus(SessionHudStatus.Offline, "Joining friend lobby…");
            lobbyEntered.Set(SteamMatchmaking.JoinLobby(new CSteamID(discoveredLobby)));
        }

        public void CancelJoinOrLeave()
        {
            lobbyMatches.Cancel();
            discoveryOperation = 0;
            if (!InLobby && joinPending)
            {
                cancelPendingJoin = true;
                SetHudStatus(SessionHudStatus.Authenticating, "Cancelling lobby entry…");
                return;
            }
            Leave();
            if (!InLobby) SetHudStatus(SessionHudStatus.Offline, "Offline — F8 creates a friends-only lobby.");
        }

        public void Leave()
        {
            if (!InLobby)
            {
                return;
            }

            int memberCount = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
            CSteamID self = SteamUser.GetSteamID();
            for (int i = 0; i < memberCount; i++)
            {
                CSteamID member = SteamMatchmaking.GetLobbyMemberByIndex(lobbyId, i);
                if (member != self)
                {
                    Send(member, PacketCodec.EncodeControl(PacketType.Disconnect), EP2PSend.k_EP2PSendReliable);
                    SteamNetworking.CloseP2PSessionWithUser(member);
                }
            }
            SteamMatchmaking.LeaveLobby(lobbyId);
            log.LogInfo("Left Crawl Online lobby " + lobbyId);
            ResetSession();
        }

        public void Poll()
        {
            uint available;
            while (SteamNetworking.IsP2PPacketAvailable(out available, Channel))
            {
                if (available > receiveBuffer.Length)
                {
                    log.LogError("Rejected oversized P2P packet: " + available);
                    return;
                }

                uint read;
                CSteamID remote;
                if (!SteamNetworking.ReadP2PPacket(receiveBuffer, available, out read, out remote, Channel))
                {
                    return;
                }

                byte[] packet = new byte[read];
                Buffer.BlockCopy(receiveBuffer, 0, packet, 0, (int)read);
                HandlePacket(remote, packet);
            }
        }

        public bool SendLocalInput(InputFrame input)
        {
            if (!InLobby || IsAuthoritativeHost || localSlot == byte.MaxValue || sessionNonce == 0)
                return false;
            input.PlayerId = localSlot;
            byte downButtons = input.DownButtons;
            byte upButtons = input.UpButtons;
            input.DownButtons = 0;
            input.UpButtons = 0;
            localInputSequence++;
            if (localInputSequence == 0) localInputSequence = 1;
            var sessionInput = new SessionInputFrame
            {
                SessionNonce = sessionNonce,
                Sequence = localInputSequence,
                Input = input
            };
            bool queued = Send(ownerId, AuthoritativeCodec.EncodeInput(sessionInput),
                EP2PSend.k_EP2PSendUnreliableNoDelay);
            if (downButtons != 0 || upButtons != 0)
            {
                localInputEventSequence++;
                if (localInputEventSequence == 0) localInputEventSequence = 1;
                sessionInput.Sequence = localInputEventSequence;
                sessionInput.Input.DownButtons = downButtons;
                sessionInput.Input.UpButtons = upButtons;
                queued &= Send(ownerId, AuthoritativeCodec.EncodeInputEvent(sessionInput),
                    EP2PSend.k_EP2PSendReliable);
            }
            return queued;
        }

        public bool BroadcastSnapshot(WorldSnapshot snapshot)
        {
            if (!IsAuthoritativeHost || roster == null || sessionNonce == 0 || snapshot == null)
                return false;
            snapshotSequence++;
            if (snapshotSequence == 0) snapshotSequence = 1;
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
                queued &= Send(new CSteamID(peers[i]), packet, EP2PSend.k_EP2PSendUnreliable);
            }
            return queued;
        }

        public bool AcknowledgeSnapshot(uint sequence)
        {
            if (!InLobby || IsAuthoritativeHost || localSlot == byte.MaxValue || sessionNonce == 0 || sequence == 0)
                return false;
            return Send(ownerId, AuthoritativeCodec.EncodeAcknowledgement(new SnapshotAcknowledgement
            {
                SessionNonce = sessionNonce,
                Sequence = sequence
            }), EP2PSend.k_EP2PSendReliable);
        }

        public void Dispose()
        {
            Leave();
            lobbyCreated.Cancel();
            lobbyEntered.Cancel();
            lobbyMatches.Cancel();
            joinRequested.Unregister();
            sessionRequested.Unregister();
            sessionConnectFailed.Unregister();
            lobbyChatUpdated.Unregister();
        }

        private void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
        {
            if (cancelPendingHost)
            {
                cancelPendingHost = false;
                if (!ioFailure && result.m_eResult == EResult.k_EResultOK)
                {
                    CSteamID cancelledLobby = new CSteamID(result.m_ulSteamIDLobby);
                    SteamMatchmaking.LeaveLobby(cancelledLobby);
                    log.LogInfo("Discarded cancelled lobby creation before exposing the session.");
                }
                ResetSession();
                return;
            }
            if (ioFailure || result.m_eResult != EResult.k_EResultOK)
            {
                log.LogError("Lobby creation failed: " + result.m_eResult + ", IO failure=" + ioFailure);
                SetHudStatus(SessionHudStatus.Error, "Lobby creation failed. Press F8 to retry.");
                return;
            }

            lobbyId = new CSteamID(result.m_ulSteamIDLobby);
            ownerId = SteamUser.GetSteamID();
            sessionNonce = CreateNonce();
            roster = new SessionRoster(ownerId.m_SteamID, sessionNonce, 4, gameBuild);
            receiveWindow = new SequenceWindow(sessionNonce);
            localSlot = 0;
            SteamMatchmaking.SetLobbyData(lobbyId, LobbyPacketProtocolKey, PacketCodec.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
            SteamMatchmaking.SetLobbyData(lobbyId, LobbySessionProtocolKey, SessionProtocolVersion);
            SteamMatchmaking.SetLobbyData(lobbyId, LobbyBuildKey, CrawlOnlineRuntime.Version);
            SteamMatchmaking.SetLobbyData(lobbyId, LobbyGameBuildKey, gameBuildMetadata);
            SteamMatchmaking.SetLobbyData(lobbyId, LobbyNonceKey, sessionNonce.ToString("x16", CultureInfo.InvariantCulture));
            log.LogInfo("Hosting authoritative lobby " + lobbyId + " as slot 0 with session nonce " +
                        sessionNonce.ToString("x16", CultureInfo.InvariantCulture) +
                        ". Press F7 to invite friends.");
            SetHudStatus(SessionHudStatus.WaitingForPeers, "Lobby ready — F7 invites friends.");
        }

        private void OnLobbyMatchList(LobbyMatchList_t result, bool ioFailure)
        {
            long operation = discoveryOperation;
            if (operation == 0) return;
            if (ioFailure)
            {
                SetHudStatus(SessionHudStatus.Error, "Steam friend-lobby search failed. Try again.");
                Action<long, ulong[]> failed = FriendLobbiesDiscovered;
                if (failed != null) failed(operation, new ulong[0]);
                return;
            }

            var candidates = new List<ulong>();
            for (int i = 0; i < result.m_nLobbiesMatching; i++)
            {
                CSteamID candidate = SteamMatchmaking.GetLobbyByIndex(i);
                CSteamID owner = SteamMatchmaking.GetLobbyOwner(candidate);
                if (candidate != CSteamID.Nil && owner != CSteamID.Nil &&
                    SteamFriends.HasFriend(owner, EFriendFlags.k_EFriendFlagImmediate))
                    candidates.Add(candidate.m_SteamID);
            }
            ulong[] normalized = FriendLobbyList.Normalize(candidates.ToArray(), 3);
            SetHudStatus(SessionHudStatus.Offline, normalized.Length == 0
                ? "No compatible friend lobby found. Ask a friend for an invite."
                : normalized.Length + " compatible friend lobby(s) found.");
            Action<long, ulong[]> callback = FriendLobbiesDiscovered;
            if (callback != null) callback(operation, normalized);
        }

        private void OnJoinRequested(GameLobbyJoinRequested_t request)
        {
            Action<ulong> callback = LobbyJoinRequested;
            if (callback != null) callback(request.m_steamIDLobby.m_SteamID);
        }

        private void OnLobbyEntered(LobbyEnter_t result, bool ioFailure)
        {
            joinPending = false;
            if (cancelPendingJoin)
            {
                cancelPendingJoin = false;
                if (!ioFailure && result.m_EChatRoomEnterResponse ==
                    (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
                    SteamMatchmaking.LeaveLobby(new CSteamID(result.m_ulSteamIDLobby));
                ResetSession();
                return;
            }
            if (ioFailure || result.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                log.LogError("Lobby join failed: response=" + result.m_EChatRoomEnterResponse + ", IO failure=" + ioFailure);
                SetHudStatus(SessionHudStatus.Error, "Could not join lobby. Accept the invite again.");
                return;
            }

            lobbyId = new CSteamID(result.m_ulSteamIDLobby);
            ownerId = SteamMatchmaking.GetLobbyOwner(lobbyId);
            string protocol = SteamMatchmaking.GetLobbyData(lobbyId, LobbyPacketProtocolKey);
            string sessionProtocol = SteamMatchmaking.GetLobbyData(lobbyId, LobbySessionProtocolKey);
            string build = SteamMatchmaking.GetLobbyData(lobbyId, LobbyBuildKey);
            string remoteGameBuild = SteamMatchmaking.GetLobbyData(lobbyId, LobbyGameBuildKey);
            string nonce = SteamMatchmaking.GetLobbyData(lobbyId, LobbyNonceKey);
            if (protocol != PacketCodec.ProtocolVersion.ToString(CultureInfo.InvariantCulture) ||
                sessionProtocol != SessionProtocolVersion ||
                build != CrawlOnlineRuntime.Version ||
                !ulong.TryParse(nonce, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out sessionNonce) ||
                sessionNonce == 0)
            {
                log.LogError("Incompatible or incomplete lobby protocol metadata.");
                SetHudStatus(SessionHudStatus.Error, "Lobby version is incompatible.");
                Leave();
                return;
            }
            if (!GameBuildFingerprint.MatchesMetadata(gameBuild, remoteGameBuild))
            {
                log.LogError("Lobby Crawl game-build fingerprint is incompatible or missing.");
                SetHudStatus(SessionHudStatus.Error, "Crawl build differs. Both players must use the same depot.");
                Leave();
                return;
            }

            log.LogInfo("Joined lobby " + lobbyId + ", owner=" + ownerId);
            receiveWindow = new SequenceWindow(sessionNonce);
            SetHudStatus(SessionHudStatus.Authenticating, "Authenticating with host…");
            SendHello();
        }

        private void OnSessionRequested(P2PSessionRequest_t request)
        {
            if (!InLobby || !IsLobbyMember(request.m_steamIDRemote))
            {
                log.LogWarning("Rejected P2P request from non-member " + request.m_steamIDRemote);
                return;
            }

            SteamNetworking.AcceptP2PSessionWithUser(request.m_steamIDRemote);
            log.LogInfo("Accepted P2P transport from lobby member " + request.m_steamIDRemote + "; awaiting authenticated hello.");
        }

        private void HandlePacket(CSteamID remote, byte[] packet)
        {
            PacketType type;
            if (!IsLobbyMember(remote) || !PacketCodec.TryReadType(packet, out type))
            {
                log.LogWarning("Rejected invalid packet from " + remote);
                return;
            }

            CSteamID self = SteamUser.GetSteamID();
            if (type == PacketType.Hello && self == ownerId)
            {
                SessionHello hello;
                if (!PacketCodec.TryDecodeHello(packet, out hello) || roster == null)
                {
                    log.LogWarning("Rejected malformed peer hello from " + remote);
                    return;
                }
                SessionAccepted accepted;
                SessionRejected rejected;
                if (roster.TryAccept(remote.m_SteamID, hello, out accepted, out rejected))
                {
                    log.LogInfo("Authenticated peer " + remote + " as slot " + accepted.AssignedSlot +
                                " attempt " + accepted.Attempt);
                    Send(remote, PacketCodec.EncodeAccepted(accepted), EP2PSend.k_EP2PSendReliable);
                    SetHudStatus(SessionHudStatus.Connected, "Peer connected.");
                }
                else
                {
                    log.LogWarning("Rejected peer " + remote + ": " + rejected.Reason);
                    Send(remote, PacketCodec.EncodeRejected(rejected), EP2PSend.k_EP2PSendReliable);
                }
            }
            else if (type == PacketType.HelloAccepted && remote == ownerId && self != ownerId)
            {
                SessionAccepted accepted;
                if (!PacketCodec.TryDecodeAccepted(packet, out accepted) ||
                    !SessionRoster.ValidateAcceptance(accepted, ownerId.m_SteamID, sessionNonce, localAttempt,
                        gameBuild))
                {
                    log.LogError("Rejected invalid host acceptance from " + remote);
                    Leave();
                    return;
                }
                localSlot = accepted.AssignedSlot;
                log.LogInfo("Host authenticated this peer as slot " + localSlot +
                             " with authoritative snapshots enabled.");
                SetHudStatus(SessionHudStatus.Connected, "Connected to host.");
            }
            else if (type == PacketType.HelloRejected && remote == ownerId && self != ownerId)
            {
                SessionRejected rejected;
                if (PacketCodec.TryDecodeRejected(packet, out rejected) &&
                    rejected.SessionNonce == sessionNonce && rejected.Attempt == localAttempt)
                {
                    log.LogError("Host rejected handshake: " + rejected.Reason);
                    SetHudStatus(SessionHudStatus.Error, "Host rejected connection: " + rejected.Reason);
                    Leave();
                }
            }
            else if (type == PacketType.Disconnect)
            {
                log.LogWarning("Peer ended Crawl Online session: " + remote);
                SteamNetworking.CloseP2PSessionWithUser(remote);
                if (self == ownerId && roster != null) roster.MarkDisconnected(remote.m_SteamID);
                else if (remote == ownerId) Leave();
            }
            else if (type == PacketType.SessionInput && self == ownerId && roster != null && receiveWindow != null)
            {
                SessionInputFrame input;
                byte assignedSlot;
                if (!AuthoritativeCodec.TryDecodeInput(packet, out input) ||
                    !roster.TryGetSlot(remote.m_SteamID, out assignedSlot) ||
                    input.Input.PlayerId != assignedSlot || !receiveWindow.TryAcceptInput(input))
                {
                    log.LogWarning("Rejected stale or unauthorized input from " + remote);
                    return;
                }
                Action<SessionInputFrame> callback = InputReceived;
                if (callback != null) callback(input);
            }
            else if (type == PacketType.InputEvent && self == ownerId && roster != null && receiveWindow != null)
            {
                SessionInputFrame inputEvent;
                byte assignedSlot;
                if (!AuthoritativeCodec.TryDecodeInputEvent(packet, out inputEvent) ||
                    !roster.TryGetSlot(remote.m_SteamID, out assignedSlot) ||
                    inputEvent.Input.PlayerId != assignedSlot || !receiveWindow.TryAcceptInputEvent(inputEvent))
                {
                    log.LogWarning("Rejected stale or unauthorized input event from " + remote);
                    return;
                }
                Action<SessionInputFrame> callback = InputEventReceived;
                if (callback != null) callback(inputEvent);
            }
            else if (type == PacketType.Snapshot && remote == ownerId && self != ownerId &&
                     localSlot != byte.MaxValue && receiveWindow != null)
            {
                WorldSnapshot snapshot;
                if (!AuthoritativeCodec.TryDecodeSnapshot(packet, out snapshot) ||
                    !receiveWindow.TryAcceptSnapshot(snapshot))
                {
                    log.LogWarning("Rejected stale or invalid authoritative snapshot.");
                    return;
                }
                Action<WorldSnapshot> callback = SnapshotReceived;
                if (callback != null) callback(snapshot);
            }
            else if (type == PacketType.SnapshotAck && self == ownerId && roster != null)
            {
                SnapshotAcknowledgement acknowledgement;
                byte ignoredSlot;
                if (!AuthoritativeCodec.TryDecodeAcknowledgement(packet, out acknowledgement) ||
                    acknowledgement.SessionNonce != sessionNonce ||
                    !roster.TryGetSlot(remote.m_SteamID, out ignoredSlot))
                {
                    log.LogWarning("Rejected invalid snapshot acknowledgement from " + remote);
                    return;
                }
                uint previous;
                if (!peerAcknowledgements.TryGetValue(remote.m_SteamID, out previous) ||
                    SequenceWindow.IsNewer(acknowledgement.Sequence, previous))
                {
                    peerAcknowledgements[remote.m_SteamID] = acknowledgement.Sequence;
                    SnapshotHistory history;
                    if (peerSnapshotHistory.TryGetValue(remote.m_SteamID, out history))
                        history.Acknowledge(acknowledgement.Sequence);
                }
            }
            else
            {
                log.LogWarning("Rejected unexpected packet " + type + " from " + remote);
            }
        }

        private void OnSessionConnectFailed(P2PSessionConnectFail_t failure)
        {
            CSteamID remote = failure.m_steamIDRemote;
            SteamNetworking.CloseP2PSessionWithUser(remote);
            if (SteamUser.GetSteamID() == ownerId && roster != null)
            {
                roster.MarkDisconnected(remote.m_SteamID);
            }
            log.LogError("P2P session failed for " + remote + ": " +
                         (EP2PSessionError)failure.m_eP2PSessionError +
                          ". Rejoin the lobby to establish a fresh authenticated attempt.");
            SetHudStatus(SessionHudStatus.Error, "Connection lost. Rejoin the lobby to reconnect.");
        }

        private void OnLobbyChatUpdated(LobbyChatUpdate_t update)
        {
            if (!InLobby || update.m_ulSteamIDLobby != lobbyId.m_SteamID) return;
            EChatMemberStateChange change = (EChatMemberStateChange)update.m_rgfChatMemberStateChange;
            EChatMemberStateChange departed = EChatMemberStateChange.k_EChatMemberStateChangeLeft |
                                               EChatMemberStateChange.k_EChatMemberStateChangeDisconnected |
                                               EChatMemberStateChange.k_EChatMemberStateChangeKicked |
                                               EChatMemberStateChange.k_EChatMemberStateChangeBanned;
            if ((change & departed) == 0) return;

            CSteamID peer = new CSteamID(update.m_ulSteamIDUserChanged);
            SteamNetworking.CloseP2PSessionWithUser(peer);
            if (roster != null) roster.Remove(peer.m_SteamID);
            peerAcknowledgements.Remove(peer.m_SteamID);
            peerSnapshotHistory.Remove(peer.m_SteamID);
            log.LogInfo("Lobby member departed and P2P state was cleared: " + peer);
            if (peer == ownerId && SteamUser.GetSteamID() != ownerId)
            {
                log.LogError("Authoritative host left the lobby; session ended explicitly.");
                SteamMatchmaking.LeaveLobby(lobbyId);
                ResetSession();
            }
        }

        private void SendHello()
        {
            localAttempt++;
            if (localAttempt == 0) localAttempt = 1;
            var hello = new SessionHello
            {
                SessionNonce = sessionNonce,
                SenderId = SteamUser.GetSteamID().m_SteamID,
                Attempt = localAttempt,
                RequestedSlot = localSlot,
                Capabilities = SessionCapabilities.Current,
                GameBuild = gameBuild
            };
            Send(ownerId, PacketCodec.EncodeHello(hello), EP2PSend.k_EP2PSendReliable);
            log.LogInfo("Sent authenticated hello attempt " + localAttempt + " to host " + ownerId);
        }

        private bool IsLobbyMember(CSteamID steamId)
        {
            int count = InLobby ? SteamMatchmaking.GetNumLobbyMembers(lobbyId) : 0;
            for (int i = 0; i < count; i++)
            {
                if (SteamMatchmaking.GetLobbyMemberByIndex(lobbyId, i) == steamId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool Send(CSteamID remote, byte[] data, EP2PSend mode)
        {
            if (!SteamNetworking.SendP2PPacket(remote, data, (uint)data.Length, mode, Channel))
            {
                log.LogError("Failed to queue P2P packet for " + remote);
                return false;
            }
            return true;
        }

        private void ResetSession()
        {
            lobbyId = CSteamID.Nil;
            ownerId = CSteamID.Nil;
            roster = null;
            sessionNonce = 0;
            localAttempt = 0;
            localSlot = byte.MaxValue;
            receiveWindow = null;
            peerAcknowledgements.Clear();
            peerSnapshotHistory.Clear();
            localInputSequence = 0;
            localInputEventSequence = 0;
            snapshotSequence = 0;
            cancelPendingHost = false;
            cancelPendingJoin = false;
            joinPending = false;
            discoveryOperation = 0;
            if (hudStatus != SessionHudStatus.Error)
                SetHudStatus(SessionHudStatus.Offline, "Offline — F8 creates a friends-only lobby.");
        }

        private void SetHudStatus(SessionHudStatus status, string message)
        {
            hudStatus = status;
            hudMessage = message ?? string.Empty;
        }

        private static ulong CreateNonce()
        {
            byte[] bytes = new byte[8];
            var random = new RNGCryptoServiceProvider();
            ulong value;
            do
            {
                random.GetBytes(bytes);
                value = BitConverter.ToUInt64(bytes, 0);
            }
            while (value == 0);
            return value;
        }
    }
}
