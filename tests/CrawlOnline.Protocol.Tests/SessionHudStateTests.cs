using CrawlOnline;
using Xunit;

namespace CrawlOnline.Protocol.Tests;

public sealed class SessionHudStateTests
{
    [Fact]
    public void SimulationStateIsExplicitAndNeverInferredFromSlots()
    {
        var normal = new SessionHudState(SessionHudStatus.Connected, true, 0,
            new[] { true, true, true, true }, "normal");
        var simulation = new SessionHudState(SessionHudStatus.Connected, true, 0,
            new[] { true, true, true, true }, "sim", true);

        Assert.False(normal.IsSimulation);
        Assert.True(simulation.IsSimulation);
    }

    [Fact]
    public void SimulationProjectionCountsHostAndThreeGhosts()
    {
        var simulation = new SessionHudState(SessionHudStatus.Connected, true, 0,
            new[] { true, true, true, true }, "SIMULATION", true);

        Assert.Equal(4, simulation.ConnectedPlayerCount);
        Assert.Equal("SIMULATION", simulation.SimulationLabel);
        Assert.Equal("4/4", simulation.SimulationPlayerCountLabel);
        for (int slot = 0; slot < 4; slot++) Assert.True(simulation.IsSlotConnected(slot));
    }

    [Fact]
    public void HostProjectionShowsOnlyConnectedSlots()
    {
        var state = new SessionHudState(SessionHudStatus.Connected, true, 0,
            new[] { true, false, true, false }, "Peer connected.");

        Assert.Equal("HOST / slot 0", state.RoleLabel);
        Assert.Equal(2, state.ConnectedPlayerCount);
        Assert.True(state.IsSlotConnected(2));
        Assert.False(state.IsSlotConnected(3));
    }

    [Fact]
    public void PendingClientDoesNotExposeAnAssignedSlot()
    {
        var state = new SessionHudState(SessionHudStatus.Authenticating, false, -1,
            new[] { true, false, false, false }, "Authenticating with host…");

        Assert.Equal("CLIENT / pending", state.RoleLabel);
        Assert.Equal(1, state.ConnectedPlayerCount);
        Assert.False(state.IsSlotConnected(-1));
    }

    [Fact]
    public void OfflineProjectionHasNoRoleOrPeers()
    {
        var state = new SessionHudState(SessionHudStatus.Offline, false, -1, null, null);

        Assert.Equal("--", state.RoleLabel);
        Assert.Equal(0, state.ConnectedPlayerCount);
        Assert.Equal(string.Empty, state.Message);
    }
}
