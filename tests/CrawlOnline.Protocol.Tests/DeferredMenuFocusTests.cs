using CrawlOnline.Menu;
using Xunit;

namespace CrawlOnline.Protocol.Tests;

public sealed class DeferredMenuFocusTests
{
    [Fact]
    public void OnlyTheLatestReconstructedMenuMayReapplyFocus()
    {
        var focus = new DeferredMenuFocus();

        int mainMenu = focus.Request();
        int hostWaiting = focus.Request();
        int restoredMainMenu = focus.Request();

        Assert.False(focus.IsCurrent(mainMenu));
        Assert.False(focus.IsCurrent(hostWaiting));
        Assert.True(focus.IsCurrent(restoredMainMenu));
    }
}
