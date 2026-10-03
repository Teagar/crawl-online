using System;

namespace CrawlOnline
{
    public enum NativeMenuPlanKind
    {
        Unsupported,
        Insert,
        AlreadyInstalled
    }

    public sealed class NativeMenuPlan
    {
        public NativeMenuPlan(NativeMenuPlanKind kind, int index)
        {
            Kind = kind;
            Index = index;
        }

        public NativeMenuPlanKind Kind { get; private set; }
        public int Index { get; private set; }
    }

    public static class NativeMenuContract
    {
        public const string StartMessage = "MsgStart";
        public const string LibraryMessage = "MsgLibrary";
        public const string OnlineMessage = "MsgCrawlOnline";

        public static NativeMenuPlan Evaluate(string[] messages)
        {
            if (messages == null) return new NativeMenuPlan(NativeMenuPlanKind.Unsupported, -1);

            bool duplicate;
            int start = FindUnique(messages, StartMessage, out duplicate);
            if (duplicate) return new NativeMenuPlan(NativeMenuPlanKind.Unsupported, -1);
            int library = FindUnique(messages, LibraryMessage, out duplicate);
            if (duplicate) return new NativeMenuPlan(NativeMenuPlanKind.Unsupported, -1);
            int online = FindUnique(messages, OnlineMessage, out duplicate);
            if (duplicate || start < 0 || library < 0)
                return new NativeMenuPlan(NativeMenuPlanKind.Unsupported, -1);

            if (online >= 0)
            {
                bool installed = online == start + 1 && library == online + 1;
                return new NativeMenuPlan(installed ? NativeMenuPlanKind.AlreadyInstalled :
                    NativeMenuPlanKind.Unsupported, installed ? online : -1);
            }

            bool adjacentAnchors = library == start + 1;
            return new NativeMenuPlan(adjacentAnchors ? NativeMenuPlanKind.Insert :
                NativeMenuPlanKind.Unsupported, adjacentAnchors ? library : -1);
        }

        private static int FindUnique(string[] values, string expected, out bool duplicate)
        {
            duplicate = false;
            int found = -1;
            for (int i = 0; i < values.Length; i++)
            {
                if (!string.Equals(values[i], expected, StringComparison.Ordinal)) continue;
                if (found >= 0)
                {
                    duplicate = true;
                    return -1;
                }
                found = i;
            }
            return found;
        }
    }
}
