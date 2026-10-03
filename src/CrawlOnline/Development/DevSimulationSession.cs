#if CRAWLONLINE_DEV_SIMULATION
using System;
using BepInEx.Logging;
using CrawlOnline.Online;
using CrawlOnline.Protocol;

namespace CrawlOnline.Development
{
    internal sealed class DevSimulationSession : IOnlineSession
    {
        private const uint SimulationSeed = 293780;
        private const ulong SimulationNonce = 0x53494d554c415445UL;
        private readonly ManualLogSource log;
        private readonly GameBuildFingerprint gameBuild;
        private HeadlessSessionHarness harness;
        private HeadlessSessionPeer[] peers = new HeadlessSessionPeer[0];
        private SessionHudStatus status = SessionHudStatus.Offline;
        private string message = "SIMULATION ready — no Steam lobby will be created.";
        private int tick;
        private int controlledPeer = 2;

        public DevSimulationSession(ManualLogSource logSource, GameBuildFingerprint localGameBuild)
        {
            log = logSource;
            gameBuild = localGameBuild;
        }

        public event Action<SessionInputFrame> InputReceived;
        public event Action<SessionInputFrame> InputEventReceived;
        public event Action<WorldSnapshot> SnapshotReceived { add { } remove { } }
        public event Action<long, ulong[]> FriendLobbiesDiscovered;
        public event Action<ulong> LobbyJoinRequested { add { } remove { } }

        public bool InLobby { get { return harness != null; } }
        public bool IsAuthoritativeHost { get { return InLobby; } }
        public byte LocalSlot { get { return InLobby ? (byte)0 : byte.MaxValue; } }
        public ulong SessionNonce { get { return InLobby ? SimulationNonce : 0; } }

        public void Host()
        {
            if (InLobby) return;
            harness = new HeadlessSessionHarness(SimulationSeed, SimulationNonce, gameBuild,
                new SessionNetworkProfile { MinimumLatencyTicks = 1, MaximumLatencyTicks = 3 });
            harness.HostInputReceived += OnInputReceived;
            harness.HostInputEventReceived += OnInputEventReceived;
            peers = new[] { harness.AddClient(2), harness.AddClient(3), harness.AddClient(4) };
            for (int i = 0; i < peers.Length; i++) harness.Connect(peers[i]);
            tick = 0;
            controlledPeer = 2;
            status = SessionHudStatus.WaitingForPeers;
            message = "SIMULATION seed 293780 — connecting three in-memory peers.";
            log.LogWarning("SIMULATION started: in-memory transport only; no Steam lobby, P2P packet, " +
                "save, achievement, or game-world mutation is permitted.");
        }

        public void Poll()
        {
            if (!InLobby) return;
            tick++;
            harness.Advance(1);
            byte[] slots = harness.ConnectedSlots();
            if (slots.Length == 3 && status != SessionHudStatus.Connected)
            {
                status = SessionHudStatus.Connected;
                message = "SIMULATION — 3 ghosts connected. F7 drops/reconnects one peer.";
                log.LogInfo("SIMULATION peers authenticated in slots 1-3; Steam transport was not used.");
            }
            if (tick % 30 == 0)
            {
                for (int i = 0; i < peers.Length; i++)
                {
                    if (peers[i].IsAuthenticated)
                    {
                        harness.SendInput(peers[i], new InputFrame
                        {
                            Tick = (uint)tick,
                            MoveX = (short)((i - 1) * 100),
                            DownButtons = tick % 120 == 0 ? (byte)1 : (byte)0
                        });
                    }
                }
            }
            if (tick % 6 == 0)
            {
                harness.BroadcastSnapshot(new WorldSnapshot
                {
                    HostTick = (uint)tick,
                    LastInputSequences = new uint[4],
                    RandomStateWords = new uint[4]
                });
            }
        }

        public void OpenInviteDialog()
        {
            if (!InLobby) return;
            HeadlessSessionPeer peer = FindPeer((ulong)controlledPeer);
            if (peer == null) return;
            bool connected = false;
            byte[] slots = harness.ConnectedSlots();
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] == peer.Slot) connected = true;
            if (connected)
            {
                harness.Drop(peer);
                message = "SIMULATION — ghost " + controlledPeer + " dropped. F7 reconnects it.";
                log.LogInfo("SIMULATION dropped in-memory peer " + controlledPeer + ".");
            }
            else
            {
                harness.Reconnect(peer);
                message = "SIMULATION — reconnecting ghost " + controlledPeer + ".";
                log.LogInfo("SIMULATION reconnect requested for in-memory peer " + controlledPeer + ".");
                controlledPeer++;
                if (controlledPeer > 4) controlledPeer = 2;
            }
        }

        public byte[] GetConnectedPeerSlots()
        {
            return InLobby ? harness.ConnectedSlots() : new byte[0];
        }

        public SessionHudState GetHudState()
        {
            bool[] slots = new bool[4];
            if (InLobby)
            {
                slots[0] = true;
                byte[] connected = harness.ConnectedSlots();
                for (int i = 0; i < connected.Length; i++)
                    if (connected[i] < slots.Length) slots[connected[i]] = true;
            }
            return new SessionHudState(status, InLobby, InLobby ? 0 : -1, slots, message, true);
        }

        public bool SendLocalInput(InputFrame input) { return false; }
        public bool BroadcastSnapshot(WorldSnapshot snapshot)
        {
            return InLobby && harness.BroadcastSnapshot(snapshot);
        }
        public bool AcknowledgeSnapshot(uint sequence) { return false; }

        public void CancelHostOrLeave() { Leave(); }
        public void CancelJoinOrLeave() { Leave(); }

        public void Leave()
        {
            if (harness != null)
            {
                harness.HostInputReceived -= OnInputReceived;
                harness.HostInputEventReceived -= OnInputEventReceived;
            }
            harness = null;
            peers = new HeadlessSessionPeer[0];
            status = SessionHudStatus.Offline;
            message = "SIMULATION stopped — no Steam lobby was created.";
            log.LogInfo("SIMULATION stopped; in-memory endpoints discarded.");
        }

        public void DiscoverFriendLobbies(long operation)
        {
            status = SessionHudStatus.Error;
            message = "Friend discovery is disabled in SIMULATION.";
            Action<long, ulong[]> callback = FriendLobbiesDiscovered;
            if (callback != null) callback(operation, new ulong[0]);
        }

        public void JoinDiscoveredLobby(ulong lobbyId, long operation)
        {
            status = SessionHudStatus.Error;
            message = "Steam lobby join is disabled in SIMULATION.";
        }

        public void Dispose() { Leave(); }

        private HeadlessSessionPeer FindPeer(ulong id)
        {
            for (int i = 0; i < peers.Length; i++) if (peers[i].Id == id) return peers[i];
            return null;
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
    }
}
#endif
