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
    internal sealed class SteamLobbySession : IOnlineSession, ISessionPacketTransport, ISessionMembership
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
        private readonly SessionProtocolEngine protocol;
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
            protocol = new SessionProtocolEngine(this, this, localGameBuild);
            protocol.InputReceived += OnInputReceived;
            protocol.InputEventReceived += OnInputEventReceived;
            protocol.SnapshotReceived += OnSnapshotReceived;
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
            get { return InLobby && protocol.IsHost && SteamUser.GetSteamID() == ownerId; }
        }

        public byte LocalSlot
        {
            get { return protocol.LocalSlot; }
        }

        public ulong SessionNonce
        {
            get { return protocol.SessionNonce; }
        }

        public byte[] GetConnectedPeerSlots()
        {
            return protocol.GetConnectedPeerSlots();
        }

        public SessionHudState GetHudState()
        {
            bool[] slots = new bool[4];
            bool isHost = IsAuthoritativeHost;
            int visibleLocalSlot = LocalSlot == byte.MaxValue ? -1 : LocalSlot;
            if (InLobby)
            {
                slots[0] = true;
                if (isHost)
                {
                    byte[] peers = protocol.GetConnectedPeerSlots();
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
                    protocol.SendDisconnect(member.m_SteamID);
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
                HandlePacket(remote.m_SteamID, packet);
            }
        }

        public bool SendLocalInput(InputFrame input)
        {
            return InLobby && !IsAuthoritativeHost && protocol.SendLocalInput(input);
        }

        public bool BroadcastSnapshot(WorldSnapshot snapshot)
        {
            return IsAuthoritativeHost && protocol.BroadcastSnapshot(snapshot);
        }

        public bool AcknowledgeSnapshot(uint sequence)
        {
            return InLobby && !IsAuthoritativeHost && protocol.AcknowledgeSnapshot(sequence);
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
            protocol.StartHost(ownerId.m_SteamID, CreateNonce(), 4);
            SteamMatchmaking.SetLobbyData(lobbyId, LobbyPacketProtocolKey, PacketCodec.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
            SteamMatchmaking.SetLobbyData(lobbyId, LobbySessionProtocolKey, SessionProtocolVersion);
            SteamMatchmaking.SetLobbyData(lobbyId, LobbyBuildKey, CrawlOnlineRuntime.Version);
            SteamMatchmaking.SetLobbyData(lobbyId, LobbyGameBuildKey, gameBuildMetadata);
            SteamMatchmaking.SetLobbyData(lobbyId, LobbyNonceKey, SessionNonce.ToString("x16", CultureInfo.InvariantCulture));
            log.LogInfo("Hosting authoritative lobby " + lobbyId + " as slot 0 with session nonce " +
                        SessionNonce.ToString("x16", CultureInfo.InvariantCulture) +
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
            string packetProtocol = SteamMatchmaking.GetLobbyData(lobbyId, LobbyPacketProtocolKey);
            string sessionProtocol = SteamMatchmaking.GetLobbyData(lobbyId, LobbySessionProtocolKey);
            string build = SteamMatchmaking.GetLobbyData(lobbyId, LobbyBuildKey);
            string remoteGameBuild = SteamMatchmaking.GetLobbyData(lobbyId, LobbyGameBuildKey);
            string nonce = SteamMatchmaking.GetLobbyData(lobbyId, LobbyNonceKey);
            ulong joinedNonce;
            if (packetProtocol != PacketCodec.ProtocolVersion.ToString(CultureInfo.InvariantCulture) ||
                sessionProtocol != SessionProtocolVersion ||
                build != CrawlOnlineRuntime.Version ||
                !ulong.TryParse(nonce, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out joinedNonce) ||
                joinedNonce == 0)
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
            SetHudStatus(SessionHudStatus.Authenticating, "Authenticating with host…");
            if (!protocol.StartClient(SteamUser.GetSteamID().m_SteamID, ownerId.m_SteamID,
                joinedNonce, byte.MaxValue))
            {
                log.LogError("Failed to queue authenticated hello for host " + ownerId);
                SetHudStatus(SessionHudStatus.Error, "Could not contact host. Rejoin the lobby.");
                Leave();
                return;
            }
            log.LogInfo("Sent authenticated hello attempt " + protocol.LocalAttempt + " to host " + ownerId);
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

        private void HandlePacket(ulong remoteId, byte[] packet)
        {
            SessionPacketResult result = protocol.HandlePacket(remoteId, packet);
            CSteamID remote = new CSteamID(remoteId);
            if (result == SessionPacketResult.Rejected)
            {
                log.LogWarning("Rejected invalid packet from " + remote);
                return;
            }
            if (result == SessionPacketResult.HandshakeAccepted)
            {
                byte slot;
                protocol.TryGetPeerSlot(remoteId, out slot);
                log.LogInfo("Authenticated peer " + remote + " as slot " + slot + ".");
                SetHudStatus(SessionHudStatus.Connected, "Peer connected.");
            }
            else if (result == SessionPacketResult.HandshakeRejected)
            {
                log.LogWarning("Rejected peer handshake from " + remote + ".");
            }
            else if (result == SessionPacketResult.LocalAuthenticated)
            {
                log.LogInfo("Host authenticated this peer as slot " + LocalSlot +
                            " with authoritative snapshots enabled.");
                SetHudStatus(SessionHudStatus.Connected, "Connected to host.");
            }
            else if (result == SessionPacketResult.LocalRejected)
            {
                log.LogError("Host rejected the authenticated handshake.");
                SetHudStatus(SessionHudStatus.Error, "Host rejected connection.");
                Leave();
            }
            else if (result == SessionPacketResult.PeerDisconnected)
            {
                log.LogWarning("Peer ended Crawl Online session: " + remote);
            }
            else if (result == SessionPacketResult.EndSession)
            {
                log.LogError("Authoritative peer ended or invalidated the session: " + remote);
                Leave();
            }
        }

        private void OnSessionConnectFailed(P2PSessionConnectFail_t failure)
        {
            CSteamID remote = failure.m_steamIDRemote;
            protocol.MarkTransportFailed(remote.m_SteamID);
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
            protocol.RemovePeer(peer.m_SteamID);
            log.LogInfo("Lobby member departed and P2P state was cleared: " + peer);
            if (peer == ownerId && SteamUser.GetSteamID() != ownerId)
            {
                log.LogError("Authoritative host left the lobby; session ended explicitly.");
                SteamMatchmaking.LeaveLobby(lobbyId);
                ResetSession();
            }
        }

        bool ISessionMembership.Contains(ulong peerId)
        {
            return IsLobbyMember(new CSteamID(peerId));
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

        bool ISessionPacketTransport.Send(ulong peerId, byte[] packet, SessionDelivery delivery)
        {
            EP2PSend mode = delivery == SessionDelivery.Reliable
                ? EP2PSend.k_EP2PSendReliable
                : delivery == SessionDelivery.UnreliableNoDelay
                    ? EP2PSend.k_EP2PSendUnreliableNoDelay
                    : EP2PSend.k_EP2PSendUnreliable;
            return Send(new CSteamID(peerId), packet, mode);
        }

        void ISessionPacketTransport.Close(ulong peerId)
        {
            SteamNetworking.CloseP2PSessionWithUser(new CSteamID(peerId));
        }

        private void ResetSession()
        {
            lobbyId = CSteamID.Nil;
            ownerId = CSteamID.Nil;
            protocol.Reset();
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

        private void OnInputReceived(SessionInputFrame input)
        {
            Action<SessionInputFrame> callback = InputReceived;
            if (callback != null) callback(input);
        }

        private void OnInputEventReceived(SessionInputFrame input)
        {
            Action<SessionInputFrame> callback = InputEventReceived;
            if (callback != null) callback(input);
        }

        private void OnSnapshotReceived(WorldSnapshot snapshot)
        {
            Action<WorldSnapshot> callback = SnapshotReceived;
            if (callback != null) callback(snapshot);
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
