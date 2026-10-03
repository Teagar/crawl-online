using CrawlOnline;
using Xunit;

namespace CrawlOnline.Protocol.Tests;

public sealed class NativeMenuContractTests
{
    [Fact]
    public void NativeAltBackRequiresTheConfirmedMenuConditions()
    {
        Assert.True(NativeMenuAltBackContract.ShouldLatch(true, true, true, true));
    }

    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public void NativeAltBackFailsClosedWhenAnyConditionIsMissing(bool inputDown, bool isAlt,
        bool installed, bool submenuOpen)
    {
        Assert.False(NativeMenuAltBackContract.ShouldLatch(inputDown, isAlt, installed, submenuOpen));
    }

    [Fact]
    public void NativeAltBackLatchCoalescesRepeatedPostfixesAndConsumesOnce()
    {
        var latch = new NativeMenuBackLatch();

        Assert.True(latch.TryLatch(true, true, true, true));
        Assert.True(latch.TryLatch(true, true, true, true));
        Assert.True(latch.TryConsume());
        Assert.False(latch.TryConsume());
    }

    [Fact]
    public void InsertsBeforeAdjacentLibraryAnchor()
    {
        NativeMenuPlan plan = NativeMenuContract.Evaluate(new[]
        {
            "MsgStart", "MsgLibrary", "MsgOptions", "MsgCredits", "MsgQuit"
        });

        Assert.Equal(NativeMenuPlanKind.Insert, plan.Kind);
        Assert.Equal(1, plan.Index);
    }

    [Fact]
    public void RecognizesExactlyOneInstalledEntry()
    {
        NativeMenuPlan plan = NativeMenuContract.Evaluate(new[]
        {
            "MsgStart", NativeMenuContract.OnlineMessage, "MsgLibrary", "MsgOptions"
        });

        Assert.Equal(NativeMenuPlanKind.AlreadyInstalled, plan.Kind);
        Assert.Equal(1, plan.Index);
    }

    [Theory]
    [InlineData("MsgStart", "MsgOptions", "MsgLibrary")]
    [InlineData("MsgStart", "MsgStart", "MsgLibrary")]
    [InlineData("MsgStart", "MsgLibrary", "MsgCrawlOnline", "MsgCrawlOnline")]
    [InlineData("MsgStart", "MsgLibrary", "MsgCrawlOnline")]
    [InlineData("MsgLibrary", "MsgStart")]
    public void RejectsAmbiguousOrUnexpectedOrdering(params string[] messages)
    {
        NativeMenuPlan plan = NativeMenuContract.Evaluate(messages);

        Assert.Equal(NativeMenuPlanKind.Unsupported, plan.Kind);
        Assert.Equal(-1, plan.Index);
    }
}
