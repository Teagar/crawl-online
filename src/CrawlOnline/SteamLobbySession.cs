using System;
using BepInEx.Logging;
using CrawlOnline.Protocol;
using Steamworks;

namespace CrawlOnline
{
    internal sealed class SteamLobbySession : IDisposable
    {
        private const int Channel = 7;
        private const int MaxPacketSize = 64 * 1024;
        private readonly ManualLogSource log;
        private readonly byte[] receiveBuffer = new byte[MaxPacketSize];
        private readonly CallResult<LobbyCreated_t> lobbyCreated;
        private readonly CallResult<LobbyEnter_t> lobbyEntered;
        private readonly Callback<GameLobbyJoinRequested_t> joinRequested;
        private readonly Callback<P2PSessionRequest_t> sessionRequested;
        private CSteamID lobbyId = CSteamID.Nil;
        private CSteamID ownerId = CSteamID.Nil;

        public SteamLobbySession(ManualLogSource logSource)
        {
            log = logSource;
            lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            lobbyEntered = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
            joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
            sessionRequested = Callback<P2PSessionRequest_t>.Create(OnSessionRequested);
            SteamNetworking.AllowP2PPacketRelay(true);
        }

        public bool InLobby
        {
            get { return lobbyId != CSteamID.Nil; }
        }

        public void Host()
        {
            if (InLobby)
            {
                log.LogWarning("Already in lobby " + lobbyId);
                return;
            }

            log.LogInfo("Creating friends-only Crawl Online lobby...");
            lobbyCreated.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 4));
        }

        public void OpenInviteDialog()
        {
            if (!InLobby)
            {
                log.LogWarning("Create or join a lobby before inviting friends.");
                return;
            }

            SteamFriends.ActivateGameOverlayInviteDialog(lobbyId);
        }

        public void Leave()
        {
            if (!InLobby)
            {
                return;
            }

            SteamMatchmaking.LeaveLobby(lobbyId);
            log.LogInfo("Left Crawl Online lobby " + lobbyId);
            lobbyId = CSteamID.Nil;
            ownerId = CSteamID.Nil;
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

        public void Dispose()
        {
            Leave();
            lobbyCreated.Cancel();
            lobbyEntered.Cancel();
            joinRequested.Unregister();
            sessionRequested.Unregister();
        }

        private void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
        {
            if (ioFailure || result.m_eResult != EResult.k_EResultOK)
            {
                log.LogError("Lobby creation failed: " + result.m_eResult + ", IO failure=" + ioFailure);
                return;
            }

            lobbyId = new CSteamID(result.m_ulSteamIDLobby);
            ownerId = SteamUser.GetSteamID();
            SteamMatchmaking.SetLobbyData(lobbyId, "crawl-online-protocol", PacketCodec.ProtocolVersion.ToString());
            SteamMatchmaking.SetLobbyData(lobbyId, "crawl-online-build", CrawlOnlineRuntime.Version);
            log.LogInfo("Hosting lobby " + lobbyId + ". Press F7 to invite friends.");
        }

        private void OnJoinRequested(GameLobbyJoinRequested_t request)
        {
            log.LogInfo("Joining invited lobby " + request.m_steamIDLobby);
            lobbyEntered.Set(SteamMatchmaking.JoinLobby(request.m_steamIDLobby));
        }

        private void OnLobbyEntered(LobbyEnter_t result, bool ioFailure)
        {
            if (ioFailure || result.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                log.LogError("Lobby join failed: response=" + result.m_EChatRoomEnterResponse + ", IO failure=" + ioFailure);
                return;
            }

            lobbyId = new CSteamID(result.m_ulSteamIDLobby);
            ownerId = SteamMatchmaking.GetLobbyOwner(lobbyId);
            string protocol = SteamMatchmaking.GetLobbyData(lobbyId, "crawl-online-protocol");
            if (protocol != PacketCodec.ProtocolVersion.ToString())
            {
                log.LogError("Incompatible lobby protocol: " + protocol);
                Leave();
                return;
            }

            log.LogInfo("Joined lobby " + lobbyId + ", owner=" + ownerId);
            Send(ownerId, PacketCodec.EncodeControl(PacketType.Hello), EP2PSend.k_EP2PSendReliable);
        }

        private void OnSessionRequested(P2PSessionRequest_t request)
        {
            if (!InLobby || !IsLobbyMember(request.m_steamIDRemote))
            {
                log.LogWarning("Rejected P2P request from non-member " + request.m_steamIDRemote);
                return;
            }

            SteamNetworking.AcceptP2PSessionWithUser(request.m_steamIDRemote);
            log.LogInfo("Accepted P2P session from " + request.m_steamIDRemote);
        }

        private void HandlePacket(CSteamID remote, byte[] packet)
        {
            PacketType type;
            if (!IsLobbyMember(remote) || !PacketCodec.TryReadType(packet, out type))
            {
                log.LogWarning("Rejected invalid packet from " + remote);
                return;
            }

            if (type == PacketType.Hello && SteamUser.GetSteamID() == ownerId)
            {
                log.LogInfo("Peer handshake from " + remote);
                Send(remote, PacketCodec.EncodeControl(PacketType.HelloAccepted), EP2PSend.k_EP2PSendReliable);
            }
            else if (type == PacketType.HelloAccepted)
            {
                log.LogInfo("Host accepted Crawl Online handshake.");
            }
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

        private void Send(CSteamID remote, byte[] data, EP2PSend mode)
        {
            if (!SteamNetworking.SendP2PPacket(remote, data, (uint)data.Length, mode, Channel))
            {
                log.LogError("Failed to queue P2P packet for " + remote);
            }
        }
    }
}
