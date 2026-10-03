using System;
using System.Collections.Generic;

namespace CrawlOnline.Protocol
{
    public sealed class SessionNetworkProfile
    {
        public int MinimumLatencyTicks;
        public int MaximumLatencyTicks;
        public int UnreliableLossPercent;
        public int UnreliableDuplicatePercent;
        public int UnreliableCorruptionPercent;
        public int BandwidthPacketsPerTick;

        public SessionNetworkProfile Clone()
        {
            return (SessionNetworkProfile)MemberwiseClone();
        }

        internal void Validate()
        {
            if (MinimumLatencyTicks < 0 || MaximumLatencyTicks < MinimumLatencyTicks)
                throw new ArgumentOutOfRangeException("MinimumLatencyTicks");
            ValidatePercent(UnreliableLossPercent, "UnreliableLossPercent");
            ValidatePercent(UnreliableDuplicatePercent, "UnreliableDuplicatePercent");
            ValidatePercent(UnreliableCorruptionPercent, "UnreliableCorruptionPercent");
            if (BandwidthPacketsPerTick < 0)
                throw new ArgumentOutOfRangeException("BandwidthPacketsPerTick");
        }

        private static void ValidatePercent(int value, string name)
        {
            if (value < 0 || value > 100) throw new ArgumentOutOfRangeException(name);
        }
    }

    public sealed class InMemorySessionEndpoint : ISessionPacketTransport, ISessionMembership
    {
        private readonly InMemorySessionNetwork network;
        private readonly ulong localId;

        internal InMemorySessionEndpoint(InMemorySessionNetwork owner, ulong id)
        {
            network = owner;
            localId = id;
        }

        public ulong LocalId { get { return localId; } }
        public event Action<ulong, byte[]> PacketReceived;

        public bool Send(ulong peerId, byte[] packet, SessionDelivery delivery)
        {
            return network.Enqueue(localId, peerId, packet, delivery);
        }

        public void Close(ulong peerId)
        {
            network.Close(localId, peerId);
        }

        public bool Contains(ulong peerId)
        {
            return network.Contains(peerId);
        }

        internal void Deliver(ulong senderId, byte[] packet)
        {
            Action<ulong, byte[]> callback = PacketReceived;
            if (callback != null) callback(senderId, packet);
        }
    }

    public sealed class InMemorySessionNetwork
    {
        private sealed class QueuedPacket
        {
            public long Ordinal;
            public long DeliveryTick;
            public ulong SenderId;
            public ulong PeerId;
            public SessionDelivery Delivery;
            public byte[] Data;
        }

        private sealed class ScheduledLinkChange
        {
            public long Tick;
            public long Ordinal;
            public ulong FirstPeerId;
            public ulong SecondPeerId;
            public bool Connected;
        }

        private struct RouteKey : IEquatable<RouteKey>
        {
            public ulong SenderId;
            public ulong PeerId;

            public bool Equals(RouteKey other)
            {
                return SenderId == other.SenderId && PeerId == other.PeerId;
            }

            public override bool Equals(object value)
            {
                return value is RouteKey && Equals((RouteKey)value);
            }

            public override int GetHashCode()
            {
                unchecked { return SenderId.GetHashCode() * 397 ^ PeerId.GetHashCode(); }
            }
        }

        private struct LinkKey : IEquatable<LinkKey>
        {
            public ulong Low;
            public ulong High;

            public bool Equals(LinkKey other) { return Low == other.Low && High == other.High; }
            public override bool Equals(object value)
            {
                return value is LinkKey && Equals((LinkKey)value);
            }
            public override int GetHashCode()
            {
                unchecked { return Low.GetHashCode() * 397 ^ High.GetHashCode(); }
            }
        }

        private struct ChannelKey : IEquatable<ChannelKey>
        {
            public ulong SenderId;
            public ulong PeerId;
            public SessionDelivery Delivery;

            public bool Equals(ChannelKey other)
            {
                return SenderId == other.SenderId && PeerId == other.PeerId &&
                    Delivery == other.Delivery;
            }

            public override bool Equals(object value)
            {
                return value is ChannelKey && Equals((ChannelKey)value);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (SenderId.GetHashCode() * 397 ^ PeerId.GetHashCode()) * 397 ^
                        Delivery.GetHashCode();
                }
            }
        }

        private sealed class StableRandom
        {
            private uint state;

            public StableRandom(uint seed) { state = seed == 0 ? 0x6d2b79f5U : seed; }

            public uint Next()
            {
                uint value = state;
                value ^= value << 13;
                value ^= value >> 17;
                value ^= value << 5;
                state = value;
                return value;
            }

            public int Range(int minimum, int maximumInclusive)
            {
                if (minimum == maximumInclusive) return minimum;
                uint width = (uint)(maximumInclusive - minimum + 1);
                return minimum + (int)(Next() % width);
            }

            public bool Percent(int percent)
            {
                return percent > 0 && Next() % 100 < percent;
            }
        }

        private readonly Dictionary<ulong, InMemorySessionEndpoint> endpoints =
            new Dictionary<ulong, InMemorySessionEndpoint>();
        private readonly Dictionary<RouteKey, SessionNetworkProfile> profiles =
            new Dictionary<RouteKey, SessionNetworkProfile>();
        private readonly Dictionary<ChannelKey, SessionNetworkProfile> channelProfiles =
            new Dictionary<ChannelKey, SessionNetworkProfile>();
        private readonly Dictionary<RouteKey, long> lastReliableDelivery =
            new Dictionary<RouteKey, long>();
        private readonly HashSet<LinkKey> closedLinks = new HashSet<LinkKey>();
        private readonly List<QueuedPacket> queue = new List<QueuedPacket>();
        private readonly List<ScheduledLinkChange> scheduledLinkChanges =
            new List<ScheduledLinkChange>();
        private readonly SessionNetworkProfile defaultProfile;
        private readonly StableRandom random;
        private readonly int maximumQueuedPackets;
        private readonly int maximumPacketSize;
        private readonly int maximumDeliveriesPerTick;
        private long ordinal;
        private long tick;

        public InMemorySessionNetwork(uint seed, SessionNetworkProfile profile,
            int maxQueuedPackets, int maxPacketSize, int maxDeliveriesPerTick)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            profile.Validate();
            if (maxQueuedPackets < 1) throw new ArgumentOutOfRangeException("maxQueuedPackets");
            if (maxPacketSize < 6) throw new ArgumentOutOfRangeException("maxPacketSize");
            if (maxDeliveriesPerTick < 1) throw new ArgumentOutOfRangeException("maxDeliveriesPerTick");
            random = new StableRandom(seed);
            defaultProfile = profile.Clone();
            maximumQueuedPackets = maxQueuedPackets;
            maximumPacketSize = maxPacketSize;
            maximumDeliveriesPerTick = maxDeliveriesPerTick;
        }

        public long Tick { get { return tick; } }
        public int QueuedPacketCount { get { return queue.Count; } }

        public InMemorySessionEndpoint CreateEndpoint(ulong peerId)
        {
            if (peerId == 0) throw new ArgumentOutOfRangeException("peerId");
            if (endpoints.ContainsKey(peerId)) throw new InvalidOperationException("Endpoint already exists.");
            var endpoint = new InMemorySessionEndpoint(this, peerId);
            endpoints.Add(peerId, endpoint);
            return endpoint;
        }

        public void RemoveEndpoint(ulong peerId)
        {
            endpoints.Remove(peerId);
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                if (queue[i].SenderId == peerId || queue[i].PeerId == peerId) queue.RemoveAt(i);
            }
        }

        public void SetProfile(ulong senderId, ulong peerId, SessionNetworkProfile profile)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            profile.Validate();
            profiles[Route(senderId, peerId)] = profile.Clone();
        }

        public void SetProfile(ulong senderId, ulong peerId, SessionDelivery delivery,
            SessionNetworkProfile profile)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            profile.Validate();
            channelProfiles[Channel(senderId, peerId, delivery)] = profile.Clone();
        }

        public void Close(ulong firstPeerId, ulong secondPeerId)
        {
            if (firstPeerId == 0 || secondPeerId == 0 || firstPeerId == secondPeerId) return;
            closedLinks.Add(Link(firstPeerId, secondPeerId));
        }

        public void Reconnect(ulong firstPeerId, ulong secondPeerId)
        {
            closedLinks.Remove(Link(firstPeerId, secondPeerId));
        }

        public void ScheduleDisconnect(ulong firstPeerId, ulong secondPeerId, long atTick)
        {
            ScheduleLink(firstPeerId, secondPeerId, atTick, false);
        }

        public void ScheduleReconnect(ulong firstPeerId, ulong secondPeerId, long atTick)
        {
            ScheduleLink(firstPeerId, secondPeerId, atTick, true);
        }

        public void Advance(int ticks)
        {
            if (ticks < 0) throw new ArgumentOutOfRangeException("ticks");
            for (int i = 0; i < ticks; i++)
            {
                tick++;
                ApplyScheduledLinkChanges();
                DeliverCurrentTick();
            }
        }

        internal bool Contains(ulong peerId)
        {
            return endpoints.ContainsKey(peerId);
        }

        internal bool Enqueue(ulong senderId, ulong peerId, byte[] packet, SessionDelivery delivery)
        {
            if (packet == null || packet.Length < 6 || packet.Length > maximumPacketSize ||
                !endpoints.ContainsKey(senderId) || !endpoints.ContainsKey(peerId) ||
                closedLinks.Contains(Link(senderId, peerId))) return false;
            SessionNetworkProfile profile = GetProfile(senderId, peerId, delivery);
            bool reliable = delivery == SessionDelivery.Reliable;
            if (!reliable && random.Percent(profile.UnreliableLossPercent)) return true;
            bool duplicate = !reliable && random.Percent(profile.UnreliableDuplicatePercent);
            int requiredCapacity = duplicate ? 2 : 1;
            if (queue.Count + requiredCapacity > maximumQueuedPackets) return false;
            QueueOne(senderId, peerId, packet, delivery, profile, reliable);
            if (duplicate)
                QueueOne(senderId, peerId, packet, delivery, profile, false);
            return true;
        }

        private void ScheduleLink(ulong firstPeerId, ulong secondPeerId, long atTick, bool connected)
        {
            if (firstPeerId == 0 || secondPeerId == 0 || firstPeerId == secondPeerId)
                throw new ArgumentOutOfRangeException("firstPeerId");
            if (atTick <= tick) throw new ArgumentOutOfRangeException("atTick");
            scheduledLinkChanges.Add(new ScheduledLinkChange
            {
                Tick = atTick,
                Ordinal = ++ordinal,
                FirstPeerId = firstPeerId,
                SecondPeerId = secondPeerId,
                Connected = connected
            });
        }

        private void ApplyScheduledLinkChanges()
        {
            scheduledLinkChanges.Sort(delegate(ScheduledLinkChange left, ScheduledLinkChange right)
            {
                int tickOrder = left.Tick.CompareTo(right.Tick);
                return tickOrder != 0 ? tickOrder : left.Ordinal.CompareTo(right.Ordinal);
            });
            while (scheduledLinkChanges.Count > 0 && scheduledLinkChanges[0].Tick <= tick)
            {
                ScheduledLinkChange change = scheduledLinkChanges[0];
                scheduledLinkChanges.RemoveAt(0);
                if (change.Connected) Reconnect(change.FirstPeerId, change.SecondPeerId);
                else Close(change.FirstPeerId, change.SecondPeerId);
            }
        }

        private void QueueOne(ulong senderId, ulong peerId, byte[] packet,
            SessionDelivery delivery, SessionNetworkProfile profile, bool reliable)
        {
            byte[] copy = (byte[])packet.Clone();
            if (!reliable && random.Percent(profile.UnreliableCorruptionPercent))
            {
                int index = random.Range(0, copy.Length - 1);
                copy[index] ^= (byte)(1 << random.Range(0, 7));
            }
            long due = tick + profile.MinimumLatencyTicks;
            if (profile.MaximumLatencyTicks > profile.MinimumLatencyTicks)
                due = tick + random.Range(profile.MinimumLatencyTicks, profile.MaximumLatencyTicks);
            RouteKey route = Route(senderId, peerId);
            if (reliable)
            {
                long previous;
                if (lastReliableDelivery.TryGetValue(route, out previous) && due < previous) due = previous;
                lastReliableDelivery[route] = due;
            }
            queue.Add(new QueuedPacket
            {
                Ordinal = ++ordinal,
                DeliveryTick = due,
                SenderId = senderId,
                PeerId = peerId,
                Delivery = delivery,
                Data = copy
            });
        }

        private void DeliverCurrentTick()
        {
            queue.Sort(ComparePackets);
            int delivered = 0;
            var channelDeliveries = new Dictionary<ChannelKey, int>();
            for (int i = 0; i < queue.Count && delivered < maximumDeliveriesPerTick;)
            {
                QueuedPacket packet = queue[i];
                if (packet.DeliveryTick > tick) break;
                ChannelKey channel = Channel(packet.SenderId, packet.PeerId, packet.Delivery);
                SessionNetworkProfile profile = GetProfile(packet.SenderId, packet.PeerId,
                    packet.Delivery);
                int channelCount;
                channelDeliveries.TryGetValue(channel, out channelCount);
                if (profile.BandwidthPacketsPerTick > 0 &&
                    channelCount >= profile.BandwidthPacketsPerTick)
                {
                    i++;
                    continue;
                }
                queue.RemoveAt(i);
                InMemorySessionEndpoint endpoint;
                if (endpoints.TryGetValue(packet.PeerId, out endpoint))
                    endpoint.Deliver(packet.SenderId, packet.Data);
                channelDeliveries[channel] = channelCount + 1;
                delivered++;
            }
        }

        private SessionNetworkProfile GetProfile(ulong senderId, ulong peerId,
            SessionDelivery delivery)
        {
            SessionNetworkProfile profile;
            if (channelProfiles.TryGetValue(Channel(senderId, peerId, delivery), out profile))
                return profile;
            return profiles.TryGetValue(Route(senderId, peerId), out profile)
                ? profile : defaultProfile;
        }

        private static int ComparePackets(QueuedPacket left, QueuedPacket right)
        {
            int tickOrder = left.DeliveryTick.CompareTo(right.DeliveryTick);
            return tickOrder != 0 ? tickOrder : left.Ordinal.CompareTo(right.Ordinal);
        }

        private static RouteKey Route(ulong senderId, ulong peerId)
        {
            return new RouteKey { SenderId = senderId, PeerId = peerId };
        }

        private static ChannelKey Channel(ulong senderId, ulong peerId, SessionDelivery delivery)
        {
            return new ChannelKey
            {
                SenderId = senderId,
                PeerId = peerId,
                Delivery = delivery
            };
        }

        private static LinkKey Link(ulong firstPeerId, ulong secondPeerId)
        {
            return firstPeerId < secondPeerId
                ? new LinkKey { Low = firstPeerId, High = secondPeerId }
                : new LinkKey { Low = secondPeerId, High = firstPeerId };
        }
    }
}
