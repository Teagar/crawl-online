using CrawlOnline;
using CrawlOnline.Protocol;
using Xunit;

namespace CrawlOnline.Protocol.Tests;

public sealed class PregameLobbyPresentationTests
{
    [Fact]
    public void HostWithPeerCanStartAndSeesSanitizedRoster()
    {
        var state = new SessionHudState(SessionHudStatus.Connected, true, 0,
            new[] { true, true, false, false }, "ready", false,
            new[] { "Teagar", "Lúca\nVeryLongPersonaName", null, null },
            PregamePhase.WaitingForHost);

        PregameLobbyPlan plan = PregameLobbyPresentation.Create(state);

        Assert.Contains("ONLINE LOBBY  /  P1 TEAGAR HOST/YOU", plan.Labels);
        Assert.Contains("P2 LCA VERYLO ON  /  P3 EMPTY  /  P4 EMPTY", plan.Labels);
        Assert.Equal("START GAME", plan.Labels[plan.SelectedIndex]);
        Assert.Equal("MsgCrawlOnlineStart", plan.Messages[plan.SelectedIndex]);
        Assert.True(plan.Enabled[plan.SelectedIndex]);
        Assert.DoesNotContain(plan.Labels, label => label.Contains("\n"));
    }

    [Fact]
    public void HostAloneCannotStart()
    {
        var state = new SessionHudState(SessionHudStatus.WaitingForPeers, true, 0,
            new[] { true, false, false, false }, "waiting", false,
            new[] { "HOST", null, null, null }, PregamePhase.WaitingForHost);

        PregameLobbyPlan plan = PregameLobbyPresentation.Create(state);

        Assert.DoesNotContain("START GAME", plan.Labels);
        Assert.Equal("INVITE FRIENDS", plan.Labels[plan.SelectedIndex]);
    }

    [Theory]
    [InlineData(PregamePhase.WaitingForHost, "ONLINE LOBBY")]
    [InlineData(PregamePhase.ConfiguringPlayers, "SETTING UP PLAYERS")]
    [InlineData(PregamePhase.StartingGame, "STARTING GAME")]
    public void ClientSeesPhaseOwnSlotAndLeave(PregamePhase phase, string heading)
    {
        var state = new SessionHudState(SessionHudStatus.Connected, false, 1,
            new[] { true, true, false, false }, "connected", false,
            new[] { "HOST", "CLIENT", null, null }, phase);

        PregameLobbyPlan plan = PregameLobbyPresentation.Create(state);

        Assert.StartsWith(heading + "  /  P1 HOST HOST", plan.Labels[0]);
        Assert.Contains("P2 CLIENT YOU  /  P3 EMPTY  /  P4 EMPTY", plan.Labels);
        Assert.Equal("LEAVE LOBBY", plan.Labels[plan.SelectedIndex]);
    }
}
