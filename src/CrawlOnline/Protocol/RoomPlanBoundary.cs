namespace CrawlOnline.Protocol
{
    public static class RoomPlanBoundary
    {
        public static bool TryTakeSpecialFallback(ulong[] specialArchetypes, ref int specialIndex,
            ref bool awaitingSpecialCoroutine, out ulong archetype)
        {
            archetype = 0;
            if (awaitingSpecialCoroutine || specialArchetypes == null ||
                specialIndex >= specialArchetypes.Length) return false;
            archetype = specialArchetypes[specialIndex++];
            awaitingSpecialCoroutine = true;
            return true;
        }

        public static bool CompleteSpecialCoroutine(ref bool awaitingSpecialCoroutine)
        {
            if (!awaitingSpecialCoroutine) return false;
            awaitingSpecialCoroutine = false;
            return true;
        }
    }
}
