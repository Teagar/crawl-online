using CrawlOnline.Menu;
using Xunit;

namespace CrawlOnline.Protocol.Tests;

public sealed class BoundedMenuFocusObservationTests
{
    [Fact]
    public void ObservationEndsAfterItsFixedBound()
    {
        var observation = new BoundedMenuFocusObservation();
        observation.Begin();

        for (int sample = 0; sample < BoundedMenuFocusObservation.SampleCount; sample++)
            Assert.True(observation.TryTakeSample());

        Assert.False(observation.TryTakeSample());
    }
}
