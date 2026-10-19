using CrawlOnline.Online;
using Xunit;

namespace CrawlOnline.Protocol.Tests;

public sealed class SteamLobbyLaunchRequestTests
{
    [Fact]
    public void ParsesSteamConnectLobbyArgument()
    {
        Assert.True(SteamLobbyLaunchRequest.TryParse(
            new[] { "Crawl.exe", "+connect_lobby", "42", "-screen-width", "960" },
            out ulong lobby));
        Assert.Equal(42UL, lobby);
    }

    [Theory]
    [InlineData()]
    [InlineData("Crawl.exe")]
    [InlineData("Crawl.exe", "+connect_lobby")]
    [InlineData("Crawl.exe", "+connect_lobby", "0")]
    [InlineData("Crawl.exe", "+connect_lobby", "-1")]
    [InlineData("Crawl.exe", "+connect_lobby", "not-an-id")]
    [InlineData("Crawl.exe", "+connect_lobby", "1", "+connect_lobby", "2")]
    public void RejectsMissingMalformedOrAmbiguousRequests(params string[] arguments)
    {
        Assert.False(SteamLobbyLaunchRequest.TryParse(arguments, out ulong lobby));
        Assert.Equal(0UL, lobby);
    }
}
