using CrawlOnline.Protocol;
using Xunit;

namespace CrawlOnline.Protocol.Tests;

public sealed class RoomPlanBoundaryTests
{
    [Fact]
    public void SpecialFallbackIsConsumedExactlyOnceAtNormalTapeBoundary()
    {
        int index = 0;
        bool awaitingCoroutine = false;

        Assert.True(RoomPlanBoundary.TryTakeSpecialFallback(new[] { 42UL }, ref index,
            ref awaitingCoroutine, out ulong archetype));
        Assert.Equal(42UL, archetype);
        Assert.Equal(1, index);
        Assert.True(awaitingCoroutine);
        Assert.False(RoomPlanBoundary.TryTakeSpecialFallback(new[] { 42UL }, ref index,
            ref awaitingCoroutine, out _));

        Assert.True(RoomPlanBoundary.CompleteSpecialCoroutine(ref awaitingCoroutine));
        Assert.False(awaitingCoroutine);
        Assert.False(RoomPlanBoundary.CompleteSpecialCoroutine(ref awaitingCoroutine));
    }

    [Fact]
    public void MissingSpecialPlanFailsClosed()
    {
        int index = 0;
        bool awaitingCoroutine = false;

        Assert.False(RoomPlanBoundary.TryTakeSpecialFallback(System.Array.Empty<ulong>(), ref index,
            ref awaitingCoroutine, out _));
        Assert.Equal(0, index);
        Assert.False(awaitingCoroutine);
    }
}
