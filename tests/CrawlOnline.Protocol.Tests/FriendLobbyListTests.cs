using CrawlOnline.Online;
using Xunit;

namespace CrawlOnline.Protocol.Tests;

public sealed class FriendLobbyListTests
{
    [Fact]
    public void NormalizationIsStablePrivateAndBounded()
    {
        ulong[] result = FriendLobbyList.Normalize(new ulong[] { 9, 2, 9, 0, 7, 3 }, 3);

        Assert.Equal(new ulong[] { 2, 3, 7 }, result);
    }

    [Fact]
    public void EmptyAndInvalidCandidatesProduceNoRows()
    {
        Assert.Empty(FriendLobbyList.Normalize(null, 3));
        Assert.Empty(FriendLobbyList.Normalize(new ulong[] { 0, 0 }, 3));
        Assert.Empty(FriendLobbyList.Normalize(new ulong[] { 1 }, 0));
    }

    [Theory]
    [InlineData(0, 3, "NO FRIEND GAMES", 1)]
    [InlineData(1, 3, "FRIEND GAME 1", 0)]
    [InlineData(2, 4, "FRIEND GAME 2", 0)]
    [InlineData(9, 5, "FRIEND GAME 3", 0)]
    public void MenuPlanIsBoundedStableAndIdentityFree(int count, int rows,
        string expectedLabel, int selected)
    {
        FriendLobbyMenuPlan plan = FriendLobbyMenuPlan.Create(count);

        Assert.Equal(rows, plan.Labels.Length);
        Assert.Contains(expectedLabel, plan.Labels);
        Assert.Equal(selected, plan.SelectedIndex);
        Assert.All(plan.Labels, label => Assert.DoesNotContain("7656", label));
    }
}
