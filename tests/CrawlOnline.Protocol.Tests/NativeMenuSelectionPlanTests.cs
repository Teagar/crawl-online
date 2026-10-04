using System;
using CrawlOnline.Menu;
using Xunit;

namespace CrawlOnline.Protocol.Tests;

public sealed class NativeMenuSelectionPlanTests
{
    [Fact]
    public void SelectsExactlyTheRequestedMaterializedItem()
    {
        var plan = NativeMenuSelectionPlan.Create(3, 1);

        Assert.False(plan.IsSelected(0));
        Assert.True(plan.IsSelected(1));
        Assert.False(plan.IsSelected(2));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, -1)]
    [InlineData(2, 2)]
    public void RejectsAnInvalidMaterializedSelection(int count, int selected)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeMenuSelectionPlan.Create(count, selected));
    }
}
