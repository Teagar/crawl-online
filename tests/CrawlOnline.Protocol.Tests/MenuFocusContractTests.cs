using CrawlOnline.Menu;
using Xunit;

namespace CrawlOnline.Protocol.Tests;

public sealed class MenuFocusContractTests
{
    [Fact]
    public void IncludesOnlyRelevantMemberMetadata()
    {
        string contract = MenuFocusContract.Describe(typeof(FocusFixture));

        Assert.Contains("field=SelectedColor:System.Int32", contract);
        Assert.Contains("property=ControllerState:System.String", contract);
        Assert.Contains("method=UpdateHighlight(System.Int32):System.Void", contract);
        Assert.DoesNotContain("IgnoredValue", contract);
        Assert.DoesNotContain("Ignore", contract);
    }

    private sealed class FocusFixture
    {
        public int SelectedColor;
        public int IgnoredValue;
        public string ControllerState { get; private set; }

        public FocusFixture()
        {
            SelectedColor = 1;
            IgnoredValue = 2;
            ControllerState = string.Empty;
        }

        private void UpdateHighlight(int value)
        {
        }

        private void Ignore()
        {
        }
    }
}
