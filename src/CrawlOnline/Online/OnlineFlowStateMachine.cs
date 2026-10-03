using System;

namespace CrawlOnline.Online
{
    public enum OnlineFlowState
    {
        Offline,
        OnlineMenu,
        CreatingLobby,
        WaitingForPlayers,
        DiscoveringFriend,
        JoiningLobby,
        Authenticating,
        Connected,
        RecoverableError,
        Leaving
    }

    public enum OnlineFlowCommand
    {
        OpenOnline,
        Host,
        JoinFriend,
        LobbyCreated,
        FriendFound,
        TransportConnected,
        Authenticated,
        Fail,
        Timeout,
        HostLeft,
        Back,
        Retry,
        Leave,
        CleanupComplete
    }

    public sealed class OnlineFlowTransition
    {
        internal OnlineFlowTransition(bool accepted, OnlineFlowState previousState,
            OnlineFlowState state, long operation, string message)
        {
            Accepted = accepted;
            PreviousState = previousState;
            State = state;
            Operation = operation;
            Message = message ?? string.Empty;
        }

        public bool Accepted { get; private set; }
        public OnlineFlowState PreviousState { get; private set; }
        public OnlineFlowState State { get; private set; }
        public long Operation { get; private set; }
        public string Message { get; private set; }
    }

    public sealed class OnlineFlowStateMachine
    {
        private OnlineFlowCommand? retryIntent;

        public OnlineFlowStateMachine()
        {
            State = OnlineFlowState.Offline;
            Message = "Offline";
        }

        public OnlineFlowState State { get; private set; }
        public long Operation { get; private set; }
        public string Message { get; private set; }

        public OnlineFlowTransition Dispatch(OnlineFlowCommand command)
        {
            OnlineFlowState previous = State;
            switch (command)
            {
                case OnlineFlowCommand.OpenOnline:
                    if (State != OnlineFlowState.Offline) return Rejected(previous);
                    return Move(previous, OnlineFlowState.OnlineMenu, "Choose HOST GAME or JOIN FRIEND.");
                case OnlineFlowCommand.Host:
                    if (State != OnlineFlowState.OnlineMenu) return Rejected(previous);
                    retryIntent = OnlineFlowCommand.Host;
                    return Begin(previous, OnlineFlowState.CreatingLobby, "Creating friends-only lobby...");
                case OnlineFlowCommand.JoinFriend:
                    if (State != OnlineFlowState.OnlineMenu) return Rejected(previous);
                    retryIntent = OnlineFlowCommand.JoinFriend;
                    return Begin(previous, OnlineFlowState.DiscoveringFriend, "Looking for a friend's lobby...");
                case OnlineFlowCommand.Back:
                    return Back(previous);
                case OnlineFlowCommand.Retry:
                    if (State != OnlineFlowState.RecoverableError || !retryIntent.HasValue)
                        return Rejected(previous);
                    return retryIntent.Value == OnlineFlowCommand.Host
                        ? Begin(previous, OnlineFlowState.CreatingLobby, "Creating friends-only lobby...")
                        : Begin(previous, OnlineFlowState.DiscoveringFriend, "Looking for a friend's lobby...");
                case OnlineFlowCommand.Leave:
                    if (State != OnlineFlowState.WaitingForPlayers && State != OnlineFlowState.Connected)
                        return Rejected(previous);
                    return Begin(previous, OnlineFlowState.Leaving, "Leaving online session...");
                default:
                    return Rejected(previous);
            }
        }

        public OnlineFlowTransition DispatchAsync(OnlineFlowCommand command, long operation,
            string detail)
        {
            OnlineFlowState previous = State;
            if (operation != Operation) return Rejected(previous);

            switch (command)
            {
                case OnlineFlowCommand.LobbyCreated:
                    if (State != OnlineFlowState.CreatingLobby) return Rejected(previous);
                    return Move(previous, OnlineFlowState.WaitingForPlayers, "Waiting for players...");
                case OnlineFlowCommand.FriendFound:
                    if (State != OnlineFlowState.DiscoveringFriend) return Rejected(previous);
                    return Move(previous, OnlineFlowState.JoiningLobby, "Joining friend's lobby...");
                case OnlineFlowCommand.TransportConnected:
                    if (State != OnlineFlowState.JoiningLobby) return Rejected(previous);
                    return Move(previous, OnlineFlowState.Authenticating, "Authenticating session...");
                case OnlineFlowCommand.Authenticated:
                    if (State != OnlineFlowState.Authenticating) return Rejected(previous);
                    return Move(previous, OnlineFlowState.Connected, "Connected");
                case OnlineFlowCommand.Fail:
                    if (!CanFail(State)) return Rejected(previous);
                    return Error(previous, detail, "Online operation failed. Try again or go BACK.");
                case OnlineFlowCommand.Timeout:
                    if (!CanFail(State)) return Rejected(previous);
                    return Error(previous, detail, "Online operation timed out. Try again or go BACK.");
                case OnlineFlowCommand.HostLeft:
                    if (State != OnlineFlowState.WaitingForPlayers && State != OnlineFlowState.Connected)
                        return Rejected(previous);
                    return Error(previous, detail, "The host left the session. Try again or go BACK.");
                case OnlineFlowCommand.CleanupComplete:
                    if (State != OnlineFlowState.Leaving) return Rejected(previous);
                    return Move(previous, OnlineFlowState.OnlineMenu, "Choose HOST GAME or JOIN FRIEND.");
                default:
                    return Rejected(previous);
            }
        }

        private OnlineFlowTransition Back(OnlineFlowState previous)
        {
            if (State == OnlineFlowState.OnlineMenu)
            {
                retryIntent = null;
                return Move(previous, OnlineFlowState.Offline, "Offline");
            }
            if (State == OnlineFlowState.RecoverableError)
                return Move(previous, OnlineFlowState.OnlineMenu, "Choose HOST GAME or JOIN FRIEND.");
            if (State == OnlineFlowState.CreatingLobby || State == OnlineFlowState.WaitingForPlayers ||
                State == OnlineFlowState.DiscoveringFriend || State == OnlineFlowState.JoiningLobby ||
                State == OnlineFlowState.Authenticating || State == OnlineFlowState.Connected)
                return Begin(previous, OnlineFlowState.Leaving, "Cancelling online operation...");
            return Rejected(previous);
        }

        private OnlineFlowTransition Error(OnlineFlowState previous, string detail, string fallback)
        {
            string message = string.IsNullOrEmpty(detail) ? fallback : detail;
            return Move(previous, OnlineFlowState.RecoverableError, message);
        }

        private OnlineFlowTransition Begin(OnlineFlowState previous, OnlineFlowState state, string message)
        {
            Operation++;
            return Move(previous, state, message);
        }

        private OnlineFlowTransition Move(OnlineFlowState previous, OnlineFlowState state, string message)
        {
            State = state;
            Message = message;
            return new OnlineFlowTransition(true, previous, State, Operation, Message);
        }

        private OnlineFlowTransition Rejected(OnlineFlowState previous)
        {
            return new OnlineFlowTransition(false, previous, State, Operation, Message);
        }

        private static bool CanFail(OnlineFlowState state)
        {
            return state == OnlineFlowState.CreatingLobby || state == OnlineFlowState.WaitingForPlayers ||
                state == OnlineFlowState.DiscoveringFriend || state == OnlineFlowState.JoiningLobby ||
                state == OnlineFlowState.Authenticating;
        }
    }
}
