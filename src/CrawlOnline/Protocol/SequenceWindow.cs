using System;
using System.Collections.Generic;

namespace CrawlOnline.Protocol
{
    public sealed class SequenceWindow
    {
        private readonly ulong sessionNonce;
        private readonly uint[] lastInput = new uint[4];
        private readonly bool[] hasInput = new bool[4];
        private readonly uint[] lastInputEvent = new uint[4];
        private readonly bool[] hasInputEvent = new bool[4];
        private uint lastSnapshot;
        private bool hasSnapshot;
        private uint lastPregameState;
        private bool hasPregameState;

        public SequenceWindow(ulong nonce)
        {
            if (nonce == 0) throw new ArgumentOutOfRangeException("nonce");
            sessionNonce = nonce;
        }

        public bool TryAcceptInput(SessionInputFrame frame)
        {
            byte slot = frame.Input.PlayerId;
            if (frame.SessionNonce != sessionNonce || slot > 3 ||
                (hasInput[slot] && !IsNewer(frame.Sequence, lastInput[slot]))) return false;
            lastInput[slot] = frame.Sequence;
            hasInput[slot] = true;
            return true;
        }

        public bool TryAcceptSnapshot(WorldSnapshot snapshot)
        {
            if (snapshot == null || snapshot.SessionNonce != sessionNonce ||
                (hasSnapshot && !IsNewer(snapshot.Sequence, lastSnapshot))) return false;
            lastSnapshot = snapshot.Sequence;
            hasSnapshot = true;
            return true;
        }

        public bool TryAcceptInputEvent(SessionInputFrame frame)
        {
            byte slot = frame.Input.PlayerId;
            if (frame.SessionNonce != sessionNonce || slot > 3 ||
                (hasInputEvent[slot] && !IsNewer(frame.Sequence, lastInputEvent[slot]))) return false;
            lastInputEvent[slot] = frame.Sequence;
            hasInputEvent[slot] = true;
            return true;
        }

        public bool TryAcceptPregameState(SessionPregameState state)
        {
            if (state.SessionNonce != sessionNonce || state.Sequence == 0 ||
                (hasPregameState && !IsNewer(state.Sequence, lastPregameState))) return false;
            lastPregameState = state.Sequence;
            hasPregameState = true;
            return true;
        }

        public void ResetInput(byte slot)
        {
            if (slot > 3) throw new ArgumentOutOfRangeException("slot");
            hasInput[slot] = false;
            lastInput[slot] = 0;
            hasInputEvent[slot] = false;
            lastInputEvent[slot] = 0;
        }

        public void ResetSnapshots()
        {
            hasSnapshot = false;
            lastSnapshot = 0;
            hasPregameState = false;
            lastPregameState = 0;
        }

        public static bool IsNewer(uint candidate, uint previous)
        {
            uint difference = unchecked(candidate - previous);
            return difference != 0 && difference < 0x80000000U;
        }
    }

    public sealed class SnapshotHistory
    {
        private readonly int capacity;
        private readonly LinkedList<WorldSnapshot> snapshots = new LinkedList<WorldSnapshot>();

        public SnapshotHistory(int maximumSnapshots)
        {
            if (maximumSnapshots < 1) throw new ArgumentOutOfRangeException("maximumSnapshots");
            capacity = maximumSnapshots;
        }

        public int Count { get { return snapshots.Count; } }

        public void Add(WorldSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            if (snapshots.Last != null && !SequenceWindow.IsNewer(snapshot.Sequence, snapshots.Last.Value.Sequence))
                throw new InvalidOperationException("Snapshot sequence must advance monotonically.");
            snapshots.AddLast(snapshot);
            while (snapshots.Count > capacity) snapshots.RemoveFirst();
        }

        public void Acknowledge(uint sequence)
        {
            while (snapshots.First != null &&
                   (snapshots.First.Value.Sequence == sequence ||
                    SequenceWindow.IsNewer(sequence, snapshots.First.Value.Sequence)))
            {
                snapshots.RemoveFirst();
            }
        }
    }
}
