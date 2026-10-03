using CrawlOnline.Online;
using Xunit;

namespace CrawlOnline.Protocol.Tests;

public sealed class OnlineFlowStateMachineTests
{
    [Fact]
    public void CampaignContextRefusesHostOrJoinWithoutMutatingTheFlow()
    {
        var flow = new OnlineFlowStateMachine();
        long operation = flow.Operation;

        Assert.False(OnlineSessionAccessPolicy.CanStartOrJoin(false));
        Assert.Equal(OnlineFlowState.Offline, flow.State);
        Assert.Equal(operation, flow.Operation);
        Assert.Equal("Return to the main menu before hosting or joining online.",
            OnlineSessionAccessPolicy.MainMenuRequiredMessage);
    }

    [Fact]
    public void HostFlowReachesConnectedState()
    {
        var flow = OpenMenu();
        OnlineFlowTransition host = flow.Dispatch(OnlineFlowCommand.Host);

        Assert.Equal(OnlineFlowState.CreatingLobby, host.State);
        Assert.True(flow.DispatchAsync(OnlineFlowCommand.LobbyCreated, host.Operation, null).Accepted);
        Assert.Equal(OnlineFlowState.WaitingForPlayers, flow.State);
        Assert.True(flow.Dispatch(OnlineFlowCommand.Leave).Accepted);
        Assert.Equal(OnlineFlowState.Leaving, flow.State);
        Assert.True(flow.DispatchAsync(OnlineFlowCommand.CleanupComplete, flow.Operation, null).Accepted);
        Assert.Equal(OnlineFlowState.OnlineMenu, flow.State);
    }

    [Fact]
    public void JoinFlowAuthenticatesThenLeaves()
    {
        var flow = OpenMenu();
        long operation = flow.Dispatch(OnlineFlowCommand.JoinFriend).Operation;

        Assert.True(flow.DispatchAsync(OnlineFlowCommand.FriendFound, operation, null).Accepted);
        Assert.True(flow.DispatchAsync(OnlineFlowCommand.TransportConnected, operation, null).Accepted);
        Assert.True(flow.DispatchAsync(OnlineFlowCommand.Authenticated, operation, null).Accepted);
        Assert.Equal(OnlineFlowState.Connected, flow.State);
        Assert.True(flow.Dispatch(OnlineFlowCommand.Back).Accepted);
        Assert.Equal(OnlineFlowState.Leaving, flow.State);
    }

    [Fact]
    public void BackInvalidatesLateCallbacks()
    {
        var flow = OpenMenu();
        long cancelledOperation = flow.Dispatch(OnlineFlowCommand.JoinFriend).Operation;

        OnlineFlowTransition cancel = flow.Dispatch(OnlineFlowCommand.Back);

        Assert.Equal(OnlineFlowState.Leaving, cancel.State);
        Assert.NotEqual(cancelledOperation, cancel.Operation);
        Assert.False(flow.DispatchAsync(OnlineFlowCommand.FriendFound, cancelledOperation, null).Accepted);
        Assert.Equal(OnlineFlowState.Leaving, flow.State);
    }

    [Fact]
    public void EveryLateJoinCallbackIsRejectedAfterCleanupAndFreshOperation()
    {
        var flow = OpenMenu();
        long cancelledOperation = flow.Dispatch(OnlineFlowCommand.JoinFriend).Operation;
        OnlineFlowTransition leaving = flow.Dispatch(OnlineFlowCommand.Back);
        Assert.True(flow.DispatchAsync(OnlineFlowCommand.CleanupComplete, leaving.Operation, null).Accepted);

        long freshOperation = flow.Dispatch(OnlineFlowCommand.JoinFriend).Operation;
        OnlineFlowState freshState = flow.State;
        foreach (OnlineFlowCommand callback in new[]
        {
            OnlineFlowCommand.FriendFound,
            OnlineFlowCommand.TransportConnected,
            OnlineFlowCommand.Authenticated,
            OnlineFlowCommand.Fail,
            OnlineFlowCommand.Timeout,
            OnlineFlowCommand.HostLeft,
            OnlineFlowCommand.CleanupComplete
        })
        {
            Assert.False(flow.DispatchAsync(callback, cancelledOperation, "stale").Accepted);
            Assert.Equal(freshState, flow.State);
            Assert.Equal(freshOperation, flow.Operation);
        }
    }

    [Fact]
    public void CancellingPendingHostRejectsItsLobbyCallbackAndAllowsFreshHost()
    {
        var flow = OpenMenu();
        long cancelledOperation = flow.Dispatch(OnlineFlowCommand.Host).Operation;

        OnlineFlowTransition cancel = flow.Dispatch(OnlineFlowCommand.Back);

        Assert.Equal(OnlineFlowState.Leaving, cancel.State);
        Assert.True(cancel.Operation > cancelledOperation);
        Assert.False(flow.DispatchAsync(OnlineFlowCommand.LobbyCreated, cancelledOperation, null).Accepted);
        Assert.True(flow.DispatchAsync(OnlineFlowCommand.CleanupComplete, cancel.Operation, null).Accepted);

        OnlineFlowTransition freshHost = flow.Dispatch(OnlineFlowCommand.Host);
        Assert.True(freshHost.Accepted);
        Assert.Equal(OnlineFlowState.CreatingLobby, freshHost.State);
        Assert.True(freshHost.Operation > cancel.Operation);
    }

    [Theory]
    [InlineData(OnlineFlowCommand.Fail)]
    [InlineData(OnlineFlowCommand.Timeout)]
    public void RecoverableFailuresCanRetryWithANewOperation(OnlineFlowCommand failure)
    {
        var flow = OpenMenu();
        long failedOperation = flow.Dispatch(OnlineFlowCommand.Host).Operation;

        Assert.True(flow.DispatchAsync(failure, failedOperation, "Actionable failure").Accepted);
        Assert.Equal(OnlineFlowState.RecoverableError, flow.State);
        Assert.Equal("Actionable failure", flow.Message);

        OnlineFlowTransition retry = flow.Dispatch(OnlineFlowCommand.Retry);
        Assert.Equal(OnlineFlowState.CreatingLobby, retry.State);
        Assert.True(retry.Operation > failedOperation);
        Assert.False(flow.DispatchAsync(OnlineFlowCommand.LobbyCreated, failedOperation, null).Accepted);
    }

    [Fact]
    public void HostLeavingIsRecoverable()
    {
        var flow = OpenMenu();
        long operation = flow.Dispatch(OnlineFlowCommand.JoinFriend).Operation;
        flow.DispatchAsync(OnlineFlowCommand.FriendFound, operation, null);
        flow.DispatchAsync(OnlineFlowCommand.TransportConnected, operation, null);
        flow.DispatchAsync(OnlineFlowCommand.Authenticated, operation, null);

        OnlineFlowTransition result = flow.DispatchAsync(OnlineFlowCommand.HostLeft, operation, null);

        Assert.True(result.Accepted);
        Assert.Equal(OnlineFlowState.RecoverableError, result.State);
        Assert.Contains("host left", result.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IncompatibleVersionFailsClosedDuringAuthentication()
    {
        var flow = OpenMenu();
        long operation = flow.Dispatch(OnlineFlowCommand.JoinFriend).Operation;
        flow.DispatchAsync(OnlineFlowCommand.FriendFound, operation, null);
        flow.DispatchAsync(OnlineFlowCommand.TransportConnected, operation, null);

        OnlineFlowTransition result = flow.DispatchAsync(OnlineFlowCommand.Fail, operation,
            "Incompatible protocol version.");

        Assert.True(result.Accepted);
        Assert.Equal(OnlineFlowState.RecoverableError, result.State);
        Assert.Equal("Incompatible protocol version.", result.Message);
        Assert.False(flow.DispatchAsync(OnlineFlowCommand.Authenticated, operation, null).Accepted);
    }

    [Fact]
    public void InvalidCommandsNeverMutateStateOrOperation()
    {
        var flow = new OnlineFlowStateMachine();
        long operation = flow.Operation;
        string message = flow.Message;

        foreach (OnlineFlowCommand command in System.Enum.GetValues<OnlineFlowCommand>())
        {
            if (command == OnlineFlowCommand.OpenOnline) continue;
            OnlineFlowTransition result = flow.Dispatch(command);
            Assert.False(result.Accepted);
            Assert.Equal(OnlineFlowState.Offline, flow.State);
            Assert.Equal(operation, flow.Operation);
            Assert.Equal(message, flow.Message);
        }
    }

    [Fact]
    public void EveryCommandIsDefinedForEveryState()
    {
        foreach (OnlineFlowState state in System.Enum.GetValues<OnlineFlowState>())
        {
            foreach (OnlineFlowCommand command in System.Enum.GetValues<OnlineFlowCommand>())
            {
                OnlineFlowStateMachine flow = Reach(state);
                long operation = flow.Operation;
                OnlineFlowTransition result = IsAsync(command)
                    ? flow.DispatchAsync(command, operation, null)
                    : flow.Dispatch(command);

                Assert.Equal(IsExpected(state, command), result.Accepted);
                if (!result.Accepted)
                {
                    Assert.Equal(state, flow.State);
                    Assert.Equal(operation, flow.Operation);
                }
            }
        }
    }

    [Fact]
    public void ErrorBackReturnsToMenuAndMenuBackReturnsOffline()
    {
        var flow = OpenMenu();
        long operation = flow.Dispatch(OnlineFlowCommand.JoinFriend).Operation;
        flow.DispatchAsync(OnlineFlowCommand.Timeout, operation, null);

        Assert.Equal(OnlineFlowState.OnlineMenu, flow.Dispatch(OnlineFlowCommand.Back).State);
        Assert.Equal(OnlineFlowState.Offline, flow.Dispatch(OnlineFlowCommand.Back).State);
    }

    private static OnlineFlowStateMachine OpenMenu()
    {
        var flow = new OnlineFlowStateMachine();
        Assert.True(flow.Dispatch(OnlineFlowCommand.OpenOnline).Accepted);
        return flow;
    }

    private static OnlineFlowStateMachine Reach(OnlineFlowState state)
    {
        var flow = new OnlineFlowStateMachine();
        if (state == OnlineFlowState.Offline) return flow;
        flow.Dispatch(OnlineFlowCommand.OpenOnline);
        if (state == OnlineFlowState.OnlineMenu) return flow;

        if (state == OnlineFlowState.CreatingLobby || state == OnlineFlowState.WaitingForPlayers ||
            state == OnlineFlowState.RecoverableError || state == OnlineFlowState.Leaving)
        {
            long operation = flow.Dispatch(OnlineFlowCommand.Host).Operation;
            if (state == OnlineFlowState.CreatingLobby) return flow;
            flow.DispatchAsync(OnlineFlowCommand.LobbyCreated, operation, null);
            if (state == OnlineFlowState.WaitingForPlayers) return flow;
            if (state == OnlineFlowState.RecoverableError)
            {
                flow.DispatchAsync(OnlineFlowCommand.Fail, operation, null);
                return flow;
            }
            flow.Dispatch(OnlineFlowCommand.Leave);
            return flow;
        }

        long joinOperation = flow.Dispatch(OnlineFlowCommand.JoinFriend).Operation;
        if (state == OnlineFlowState.DiscoveringFriend) return flow;
        flow.DispatchAsync(OnlineFlowCommand.FriendFound, joinOperation, null);
        if (state == OnlineFlowState.JoiningLobby) return flow;
        flow.DispatchAsync(OnlineFlowCommand.TransportConnected, joinOperation, null);
        if (state == OnlineFlowState.Authenticating) return flow;
        flow.DispatchAsync(OnlineFlowCommand.Authenticated, joinOperation, null);
        return flow;
    }

    private static bool IsAsync(OnlineFlowCommand command)
    {
        return command == OnlineFlowCommand.LobbyCreated ||
            command == OnlineFlowCommand.FriendFound ||
            command == OnlineFlowCommand.TransportConnected ||
            command == OnlineFlowCommand.Authenticated ||
            command == OnlineFlowCommand.Fail ||
            command == OnlineFlowCommand.Timeout ||
            command == OnlineFlowCommand.HostLeft ||
            command == OnlineFlowCommand.CleanupComplete;
    }

    private static bool IsExpected(OnlineFlowState state, OnlineFlowCommand command)
    {
        if (command == OnlineFlowCommand.OpenOnline) return state == OnlineFlowState.Offline;
        if (command == OnlineFlowCommand.Host || command == OnlineFlowCommand.JoinFriend)
            return state == OnlineFlowState.OnlineMenu;
        if (command == OnlineFlowCommand.LobbyCreated) return state == OnlineFlowState.CreatingLobby;
        if (command == OnlineFlowCommand.FriendFound) return state == OnlineFlowState.DiscoveringFriend;
        if (command == OnlineFlowCommand.TransportConnected) return state == OnlineFlowState.JoiningLobby;
        if (command == OnlineFlowCommand.Authenticated) return state == OnlineFlowState.Authenticating;
        if (command == OnlineFlowCommand.Fail || command == OnlineFlowCommand.Timeout)
            return state == OnlineFlowState.CreatingLobby || state == OnlineFlowState.WaitingForPlayers ||
                state == OnlineFlowState.DiscoveringFriend || state == OnlineFlowState.JoiningLobby ||
                state == OnlineFlowState.Authenticating;
        if (command == OnlineFlowCommand.HostLeft)
            return state == OnlineFlowState.WaitingForPlayers || state == OnlineFlowState.Connected;
        if (command == OnlineFlowCommand.Back)
            return state == OnlineFlowState.OnlineMenu || state == OnlineFlowState.CreatingLobby ||
                state == OnlineFlowState.WaitingForPlayers || state == OnlineFlowState.DiscoveringFriend ||
                state == OnlineFlowState.JoiningLobby || state == OnlineFlowState.Authenticating ||
                state == OnlineFlowState.Connected || state == OnlineFlowState.RecoverableError;
        if (command == OnlineFlowCommand.Retry) return state == OnlineFlowState.RecoverableError;
        if (command == OnlineFlowCommand.Leave)
            return state == OnlineFlowState.WaitingForPlayers || state == OnlineFlowState.Connected;
        if (command == OnlineFlowCommand.CleanupComplete) return state == OnlineFlowState.Leaving;
        return false;
    }
}
