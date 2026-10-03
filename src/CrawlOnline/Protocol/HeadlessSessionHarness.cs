using System;
using System.Collections.Generic;
using System.Text;

namespace CrawlOnline.Protocol
{
    public sealed class HeadlessSessionReport
    {
        public uint Seed;
        public long Tick;
        public string LastState;
        public string[] Timeline;

        public override string ToString()
        {
            var text = new StringBuilder();
            text.Append("seed=").Append(Seed).Append(" tick=").Append(Tick)
                .Append(" state=").Append(LastState ?? string.Empty);
            if (Timeline != null)
            {
                for (int i = 0; i < Timeline.Length; i++)
                    text.AppendLine().Append(Timeline[i]);
            }
            return text.ToString();
        }
    }

    public sealed class HeadlessSessionScenarioException : Exception
    {
        public readonly HeadlessSessionReport Report;

        public HeadlessSessionScenarioException(string message, HeadlessSessionReport report)
            : base(message + Environment.NewLine + report)
        {
            Report = report;
        }
    }

    public sealed class HeadlessSessionPeer
    {
        internal readonly InMemorySessionEndpoint Endpoint;
        internal readonly SessionProtocolEngine Engine;
        internal SessionPacketResult LastResult;
        internal int AcceptedInputs;
        internal int AcceptedInputEvents;
        internal int AcceptedSnapshots;

        internal HeadlessSessionPeer(ulong id, InMemorySessionEndpoint endpoint,
            SessionProtocolEngine engine)
        {
            Id = id;
            Endpoint = endpoint;
            Engine = engine;
            LastResult = SessionPacketResult.Rejected;
        }

        public ulong Id { get; private set; }
        public byte Slot { get { return Engine.LocalSlot; } }
        public bool IsAuthenticated { get { return Engine.IsAuthenticated; } }
        public SessionPacketResult LastPacketResult { get { return LastResult; } }
        public int InputCount { get { return AcceptedInputs; } }
        public int InputEventCount { get { return AcceptedInputEvents; } }
        public int SnapshotCount { get { return AcceptedSnapshots; } }
    }

    public sealed class HeadlessSessionHarness
    {
        private const int TimelineCapacity = 128;
        private readonly uint seed;
        private readonly ulong nonce;
        private readonly GameBuildFingerprint hostBuild;
        private readonly InMemorySessionNetwork network;
        private readonly Dictionary<ulong, HeadlessSessionPeer> peers =
            new Dictionary<ulong, HeadlessSessionPeer>();
        private readonly Queue<string> timeline = new Queue<string>();
        private readonly HeadlessSessionPeer host;

        public event Action<SessionInputFrame> HostInputReceived;
        public event Action<SessionInputFrame> HostInputEventReceived;

        public HeadlessSessionHarness(uint scenarioSeed, ulong sessionNonce,
            GameBuildFingerprint authoritativeBuild, SessionNetworkProfile defaultProfile)
        {
            if (scenarioSeed == 0) throw new ArgumentOutOfRangeException("scenarioSeed");
            if (sessionNonce == 0) throw new ArgumentOutOfRangeException("sessionNonce");
            if (authoritativeBuild.IsEmpty) throw new ArgumentOutOfRangeException("authoritativeBuild");
            seed = scenarioSeed;
            nonce = sessionNonce;
            hostBuild = authoritativeBuild;
            network = new InMemorySessionNetwork(seed, defaultProfile ?? new SessionNetworkProfile(),
                1024, 64 * 1024, 256);
            host = CreatePeer(1, hostBuild);
            host.Engine.InputReceived += delegate(SessionInputFrame input)
            {
                Action<SessionInputFrame> callback = HostInputReceived;
                if (callback != null) callback(input);
            };
            host.Engine.InputEventReceived += delegate(SessionInputFrame input)
            {
                Action<SessionInputFrame> callback = HostInputEventReceived;
                if (callback != null) callback(input);
            };
            host.Engine.StartHost(host.Id, nonce, 4);
            Record("host started id=1");
        }

        public uint Seed { get { return seed; } }
        public long Tick { get { return network.Tick; } }
        public int QueuedPacketCount { get { return network.QueuedPacketCount; } }
        public HeadlessSessionPeer Host { get { return host; } }

        public HeadlessSessionPeer AddClient(ulong peerId)
        {
            return AddClient(peerId, hostBuild);
        }

        public HeadlessSessionPeer AddClient(ulong peerId, GameBuildFingerprint build)
        {
            if (peerId == host.Id) throw new ArgumentOutOfRangeException("peerId");
            HeadlessSessionPeer peer = CreatePeer(peerId, build);
            Record("client added id=" + peerId);
            return peer;
        }

        public bool Connect(HeadlessSessionPeer peer)
        {
            return Connect(peer, nonce, byte.MaxValue);
        }

        public bool Connect(HeadlessSessionPeer peer, ulong requestedNonce, byte requestedSlot)
        {
            EnsureClient(peer);
            bool queued = peer.Engine.StartClient(peer.Id, host.Id, requestedNonce, requestedSlot);
            Record("connect id=" + peer.Id + " nonce=" + requestedNonce + " queued=" + queued);
            return queued;
        }

        public void Advance(int ticks)
        {
            network.Advance(ticks);
            Record("advance ticks=" + ticks + " queue=" + network.QueuedPacketCount);
        }

        public bool SendInput(HeadlessSessionPeer peer, InputFrame input)
        {
            EnsureClient(peer);
            bool queued = peer.Engine.SendLocalInput(input);
            Record("input id=" + peer.Id + " tick=" + input.Tick + " queued=" + queued);
            return queued;
        }

        public bool BroadcastSnapshot(WorldSnapshot snapshot)
        {
            bool queued = host.Engine.BroadcastSnapshot(snapshot);
            Record("snapshot queued=" + queued);
            return queued;
        }

        public bool AcknowledgeLatest(HeadlessSessionPeer peer, uint sequence)
        {
            EnsureClient(peer);
            bool queued = peer.Engine.AcknowledgeSnapshot(sequence);
            Record("ack id=" + peer.Id + " sequence=" + sequence + " queued=" + queued);
            return queued;
        }

        public void Drop(HeadlessSessionPeer peer)
        {
            EnsureClient(peer);
            network.Close(host.Id, peer.Id);
            host.Engine.MarkTransportFailed(peer.Id);
            Record("drop id=" + peer.Id);
        }

        public bool Reconnect(HeadlessSessionPeer peer)
        {
            EnsureClient(peer);
            network.Reconnect(host.Id, peer.Id);
            bool queued = peer.Engine.ReconnectClient();
            Record("reconnect id=" + peer.Id + " attempt=" + peer.Engine.LocalAttempt +
                " queued=" + queued);
            return queued;
        }

        public bool HostLeft(HeadlessSessionPeer peer)
        {
            EnsureClient(peer);
            bool queued = host.Engine.SendDisconnect(peer.Id);
            Record("host left peer=" + peer.Id + " queued=" + queued);
            return queued;
        }

        public void SetProfile(ulong senderId, ulong peerId, SessionDelivery delivery,
            SessionNetworkProfile profile)
        {
            network.SetProfile(senderId, peerId, delivery, profile);
        }

        public bool SendRaw(HeadlessSessionPeer sender, ulong peerId, byte[] packet,
            SessionDelivery delivery)
        {
            if (sender == null || !peers.ContainsKey(sender.Id))
                throw new ArgumentException("Unknown sender.", "sender");
            bool queued = sender.Endpoint.Send(peerId, packet, delivery);
            Record("raw sender=" + sender.Id + " peer=" + peerId + " queued=" + queued);
            return queued;
        }

        public byte[] ConnectedSlots()
        {
            return host.Engine.GetConnectedPeerSlots();
        }

        public HeadlessSessionReport GetReport()
        {
            return new HeadlessSessionReport
            {
                Seed = seed,
                Tick = network.Tick,
                LastState = BuildState(),
                Timeline = timeline.ToArray()
            };
        }

        public void Verify(bool condition, string message)
        {
            if (!condition) throw new HeadlessSessionScenarioException(message, GetReport());
        }

        private HeadlessSessionPeer CreatePeer(ulong id, GameBuildFingerprint build)
        {
            if (id == 0) throw new ArgumentOutOfRangeException("id");
            if (peers.ContainsKey(id)) throw new InvalidOperationException("Peer already exists.");
            InMemorySessionEndpoint endpoint = network.CreateEndpoint(id);
            var engine = new SessionProtocolEngine(endpoint, endpoint, build);
            var peer = new HeadlessSessionPeer(id, endpoint, engine);
            endpoint.PacketReceived += delegate(ulong senderId, byte[] packet)
            {
                peer.LastResult = engine.HandlePacket(senderId, packet);
                Record("packet " + senderId + "->" + id + " result=" + peer.LastResult);
                if (peer.LastResult == SessionPacketResult.EndSession) engine.Reset();
            };
            engine.InputReceived += delegate { peer.AcceptedInputs++; };
            engine.InputEventReceived += delegate { peer.AcceptedInputEvents++; };
            engine.SnapshotReceived += delegate { peer.AcceptedSnapshots++; };
            peers.Add(id, peer);
            return peer;
        }

        private void EnsureClient(HeadlessSessionPeer peer)
        {
            if (peer == null || peer == host || !peers.ContainsKey(peer.Id))
                throw new ArgumentException("Unknown client.", "peer");
        }

        private void Record(string value)
        {
            while (timeline.Count >= TimelineCapacity) timeline.Dequeue();
            timeline.Enqueue(network.Tick + ":" + value);
        }

        private string BuildState()
        {
            var text = new StringBuilder();
            text.Append("slots=");
            byte[] slots = ConnectedSlots();
            for (int i = 0; i < slots.Length; i++)
            {
                if (i != 0) text.Append(',');
                text.Append(slots[i]);
            }
            text.Append(" queue=").Append(network.QueuedPacketCount);
            var ids = new List<ulong>(peers.Keys);
            ids.Sort();
            for (int i = 0; i < ids.Count; i++)
            {
                HeadlessSessionPeer peer = peers[ids[i]];
                text.Append(" peer").Append(peer.Id).Append('=')
                    .Append(peer.Engine.IsAuthenticated ? "auth" : "unauth")
                    .Append('/').Append(peer.Engine.LocalSlot)
                    .Append('/').Append(peer.LastResult);
            }
            return text.ToString();
        }
    }
}
