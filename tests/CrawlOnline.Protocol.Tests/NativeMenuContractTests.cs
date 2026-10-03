using CrawlOnline;
using Xunit;

namespace CrawlOnline.Protocol.Tests;

public sealed class NativeMenuContractTests
{
    [Theory]
    [InlineData(NativeMenuCancelContract.BackMessage)]
    [InlineData(NativeMenuCancelContract.CancelMessage)]
    public void NativeControllerCancelMessagesRouteToOnlineBack(string message)
    {
        Assert.True(NativeMenuCancelContract.IsCancelMessage(message));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("MsgCrawlOnlineBack")]
    [InlineData("MsgStart")]
    public void OnlyNativeCancelMessagesRouteToOnlineBack(string message)
    {
        Assert.False(NativeMenuCancelContract.IsCancelMessage(message));
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
