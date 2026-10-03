using System;
using BepInEx.Logging;
using CrawlOnline.Determinism;
using CrawlOnline.Authoritative;
using CrawlOnline.Diagnostics;
using CrawlOnline.Menu;
using CrawlOnline.Online;
using CrawlOnline.Protocol;
#if CRAWLONLINE_DEV_SIMULATION
using CrawlOnline.Development;
#endif
using UnityEngine;

namespace CrawlOnline
{
    public sealed class CrawlOnlineRuntime
    {
        public const string Version = "0.2.0-alpha.1";
        private readonly ManualLogSource log;
        private IOnlineSession session;
        private DeterminismHarness harness;
        private AuthoritativeSynchronizer synchronizer;
        private SessionHud hud;
        private MenuContractProbe menuProbe;
        private NativeMainMenuIntegration nativeMenu;
        private OnlineFlowStateMachine onlineFlow;
        private long onlineOperation;
        private ulong[] friendLobbies = new ulong[0];
#if CRAWLONLINE_DEV_SIMULATION
        private bool simulationMode;
#endif

        public CrawlOnlineRuntime(ManualLogSource logSource, string gameAssemblySha256)
        {
            log = logSource;
            InitialiseSession(GameBuildFingerprint.Parse(gameAssemblySha256), false);
        }

#if CRAWLONLINE_DEV_SIMULATION
        public CrawlOnlineRuntime(ManualLogSource logSource, string gameAssemblySha256,
            bool enableLocalSimulation)
        {
            log = logSource;
            simulationMode = enableLocalSimulation;
            InitialiseSession(GameBuildFingerprint.Parse(gameAssemblySha256), enableLocalSimulation);
        }
#endif

        private void InitialiseSession(GameBuildFingerprint gameBuild, bool enableLocalSimulation)
        {
#if CRAWLONLINE_DEV_SIMULATION
            simulationMode = enableLocalSimulation;
            session = enableLocalSimulation
                ? (IOnlineSession)new DevSimulationSession(log, gameBuild)
                : new SteamLobbySession(log, gameBuild);
#else
            if (enableLocalSimulation)
                throw new InvalidOperationException("Local simulation is unavailable in this build.");
            session = new SteamLobbySession(log, gameBuild);
#endif
            session.FriendLobbiesDiscovered += OnFriendLobbiesDiscovered;
            session.LobbyJoinRequested += OnLobbyJoinRequested;
#if CRAWLONLINE_DEV_SIMULATION
            if (!enableLocalSimulation) synchronizer = new AuthoritativeSynchronizer(session, log);
#else
            synchronizer = new AuthoritativeSynchronizer(session, log);
#endif
            harness = DeterminismHarness.TryCreate(log);
            hud = new SessionHud();
            menuProbe = MenuContractProbe.TryCreate(log);
            onlineFlow = new OnlineFlowStateMachine();
            nativeMenu = new NativeMainMenuIntegration(log, OnNativeOnlineSelected,
                OnNativeHostSelected, OnNativeJoinSelected, OnNativeOnlineBack,
                OnNativeInviteSelected, OnNativeCancelSelected, OnNativeJoinRefresh,
                OnNativeFriendSelected, enableLocalSimulation);
            log.LogInfo(enableLocalSimulation
                ? "SIMULATION ready: F8 start, F7 drop/reconnect, F9 stop; Steam lobby APIs disabled."
                : "Ready: F8 host, F7 invite, F9 leave, F5 help, F6 HUD");
        }

        public void Tick()
        {
            if (menuProbe != null) menuProbe.Tick();
            if (nativeMenu != null) nativeMenu.Tick();
#if CRAWLONLINE_DEV_SIMULATION
            if (simulationMode && session.InLobby &&
                (nativeMenu == null || !nativeMenu.IsActiveMainMenu))
            {
                log.LogWarning("SIMULATION left the safe main-menu scope and was stopped before game mutation.");
                session.Leave();
            }
#endif
            if (Input.GetKeyDown(KeyCode.F5))
            {
                hud.ToggleTutorial();
            }
            if (Input.GetKeyDown(KeyCode.F6))
            {
                hud.ToggleMinimized();
            }
            if (Input.GetKeyDown(KeyCode.F8))
            {
                if (onlineFlow.State == OnlineFlowState.OnlineMenu ||
                    onlineFlow.State == OnlineFlowState.RecoverableError)
                    OnNativeHostSelected();
                else if (CanStartOrJoinFromMenu())
                    session.Host();
                else
                    RefuseSessionStartOutsideMenu();
            }
            else if (Input.GetKeyDown(KeyCode.F7))
            {
                session.OpenInviteDialog();
            }
            else if (Input.GetKeyDown(KeyCode.F9))
            {
                if (onlineFlow.State == OnlineFlowState.CreatingLobby ||
                    onlineFlow.State == OnlineFlowState.WaitingForPlayers ||
                    onlineFlow.State == OnlineFlowState.RecoverableError)
                    OnNativeCancelSelected();
                else
                    session.Leave();
            }

            session.Poll();
            UpdateOnlineFlow();
        }

        public void Shutdown()
        {
            if (synchronizer != null)
            {
                synchronizer.Dispose();
                synchronizer = null;
            }
            if (session != null)
            {
                session.FriendLobbiesDiscovered -= OnFriendLobbiesDiscovered;
                session.LobbyJoinRequested -= OnLobbyJoinRequested;
                session.Dispose();
                session = null;
            }
            if (harness != null)
            {
                harness.Dispose();
                harness = null;
            }
            hud = null;
            menuProbe = null;
            if (nativeMenu != null)
            {
                nativeMenu.Dispose();
                nativeMenu = null;
            }
            onlineFlow = null;
        }

        public void FixedTick()
        {
            if (harness != null) harness.FixedTick();
        }

        public void LateTick()
        {
            if (harness != null) harness.LateTick();
            if (synchronizer != null) synchronizer.LateTick();
        }

        public void DrawHud()
        {
            if (hud != null && session != null)
                hud.Draw(session.GetHudState(), nativeMenu != null && nativeMenu.IsActiveMainMenu,
                    onlineFlow != null && onlineFlow.State != OnlineFlowState.Offline);
        }

        private bool OnNativeOnlineSelected()
        {
            OnlineFlowTransition transition = onlineFlow.Dispatch(OnlineFlowCommand.OpenOnline);
            if (transition.Accepted) log.LogInfo("Native ONLINE menu action selected.");
            return transition.Accepted;
        }

        private void OnNativeHostSelected()
        {
            if (!CanStartOrJoinFromMenu())
            {
                RefuseSessionStartOutsideMenu();
                return;
            }
            OnlineFlowTransition transition = onlineFlow.State == OnlineFlowState.RecoverableError
                ? onlineFlow.Dispatch(OnlineFlowCommand.Retry)
                : onlineFlow.Dispatch(OnlineFlowCommand.Host);
            if (!transition.Accepted) return;
            onlineOperation = transition.Operation;
            session.Host();
        }

        private void OnNativeJoinSelected()
        {
            if (!CanStartOrJoinFromMenu())
            {
                RefuseSessionStartOutsideMenu();
                return;
            }
            OnlineFlowTransition transition = onlineFlow.Dispatch(OnlineFlowCommand.JoinFriend);
            if (!transition.Accepted) return;
            onlineOperation = transition.Operation;
            friendLobbies = new ulong[0];
            nativeMenu.ShowFriendSearch();
            session.DiscoverFriendLobbies(onlineOperation);
        }

        private void OnNativeOnlineBack()
        {
            OnlineFlowTransition transition = onlineFlow.Dispatch(OnlineFlowCommand.Back);
            if (!transition.Accepted) return;
            if (transition.State == OnlineFlowState.Leaving)
            {
                onlineOperation = transition.Operation;
                session.CancelHostOrLeave();
                return;
            }
            if (transition.State == OnlineFlowState.OnlineMenu)
            {
                session.CancelHostOrLeave();
                onlineFlow.Dispatch(OnlineFlowCommand.Back);
            }
            log.LogInfo("Returned from native Online submenu.");
        }

        private void OnNativeInviteSelected()
        {
            if (onlineFlow.State == OnlineFlowState.WaitingForPlayers) session.OpenInviteDialog();
        }

        private void OnNativeCancelSelected()
        {
            OnlineFlowTransition transition = onlineFlow.Dispatch(OnlineFlowCommand.Back);
            if (!transition.Accepted) return;
            bool joinOperation = transition.PreviousState == OnlineFlowState.DiscoveringFriend ||
                transition.PreviousState == OnlineFlowState.JoiningLobby ||
                transition.PreviousState == OnlineFlowState.Authenticating ||
                transition.PreviousState == OnlineFlowState.Connected;
            if (transition.State == OnlineFlowState.OnlineMenu)
            {
                if (joinOperation) session.CancelJoinOrLeave();
                else session.CancelHostOrLeave();
                nativeMenu.CloseSessionMenu();
                return;
            }
            onlineOperation = transition.Operation;
            if (joinOperation) session.CancelJoinOrLeave();
            else session.CancelHostOrLeave();
        }

        private void OnNativeJoinRefresh()
        {
            if (onlineFlow.State == OnlineFlowState.RecoverableError)
            {
                OnlineFlowTransition retry = onlineFlow.Dispatch(OnlineFlowCommand.Retry);
                if (!retry.Accepted) return;
                onlineOperation = retry.Operation;
            }
            if (onlineFlow.State != OnlineFlowState.DiscoveringFriend) return;
            friendLobbies = new ulong[0];
            nativeMenu.ShowFriendSearch();
            session.DiscoverFriendLobbies(onlineOperation);
        }

        private void OnNativeFriendSelected(int index)
        {
            if (index < 0 || index >= friendLobbies.Length) return;
            OnlineFlowTransition found = onlineFlow.DispatchAsync(
                OnlineFlowCommand.FriendFound, onlineOperation, null);
            if (!found.Accepted) return;
            nativeMenu.ShowFriendJoining();
            session.JoinDiscoveredLobby(friendLobbies[index], onlineOperation);
        }

        private void OnFriendLobbiesDiscovered(long operation, ulong[] lobbyIds)
        {
            if (operation != onlineOperation || onlineFlow.State != OnlineFlowState.DiscoveringFriend)
                return;
            SessionHudState state = session.GetHudState();
            if (state.Status == SessionHudStatus.Error)
            {
                if (onlineFlow.DispatchAsync(OnlineFlowCommand.Fail, operation, state.Message).Accepted)
                    nativeMenu.ShowFriendSearchError();
                return;
            }
            friendLobbies = FriendLobbyList.Normalize(lobbyIds, 3);
            nativeMenu.ShowFriendLobbies(friendLobbies.Length);
        }

        private void OnLobbyJoinRequested(ulong lobbyId)
        {
            if (lobbyId == 0 || nativeMenu == null || !nativeMenu.CanAcceptExternalJoin ||
                !CanStartOrJoinFromMenu())
            {
                log.LogWarning("Steam lobby invite ignored outside the active main menu.");
                RefuseSessionStartOutsideMenu();
                return;
            }
            if (onlineFlow.State != OnlineFlowState.Offline &&
                onlineFlow.State != OnlineFlowState.OnlineMenu)
            {
                log.LogWarning("Steam lobby invite ignored while another Online operation is active.");
                return;
            }
            if (onlineFlow.State == OnlineFlowState.Offline && !nativeMenu.OpenForExternalJoin())
                return;
            OnlineFlowTransition discovery = onlineFlow.Dispatch(OnlineFlowCommand.JoinFriend);
            if (!discovery.Accepted) return;
            onlineOperation = discovery.Operation;
            friendLobbies = new[] { lobbyId };
            OnlineFlowTransition found = onlineFlow.DispatchAsync(
                OnlineFlowCommand.FriendFound, onlineOperation, null);
            if (!found.Accepted) return;
            nativeMenu.ShowFriendJoining();
            session.JoinDiscoveredLobby(lobbyId, onlineOperation);
            log.LogInfo("Accepted a Steam lobby invite from the active main menu.");
        }

        private bool CanStartOrJoinFromMenu()
        {
            return OnlineSessionAccessPolicy.CanStartOrJoin(
                nativeMenu != null && nativeMenu.IsActiveMainMenu);
        }

        private void RefuseSessionStartOutsideMenu()
        {
            log.LogWarning(OnlineSessionAccessPolicy.MainMenuRequiredMessage);
            if (hud != null) hud.ShowNotice(OnlineSessionAccessPolicy.MainMenuRequiredMessage);
        }

        private void UpdateOnlineFlow()
        {
            if (onlineFlow == null || session == null) return;
            SessionHudState state = session.GetHudState();
            if (onlineFlow.State == OnlineFlowState.CreatingLobby &&
                state.Status == SessionHudStatus.WaitingForPeers)
            {
                OnlineFlowTransition transition = onlineFlow.DispatchAsync(
                    OnlineFlowCommand.LobbyCreated, onlineOperation, null);
                if (transition.Accepted) nativeMenu.ShowHostWaiting();
            }
            else if ((onlineFlow.State == OnlineFlowState.CreatingLobby ||
                      onlineFlow.State == OnlineFlowState.WaitingForPlayers ||
                      onlineFlow.State == OnlineFlowState.DiscoveringFriend ||
                      onlineFlow.State == OnlineFlowState.JoiningLobby ||
                      onlineFlow.State == OnlineFlowState.Authenticating) &&
                     state.Status == SessionHudStatus.Error)
            {
                bool joining = onlineFlow.State == OnlineFlowState.DiscoveringFriend ||
                    onlineFlow.State == OnlineFlowState.JoiningLobby ||
                    onlineFlow.State == OnlineFlowState.Authenticating;
                if (onlineFlow.DispatchAsync(OnlineFlowCommand.Fail, onlineOperation, state.Message).Accepted && joining)
                    nativeMenu.ShowFriendSearchError();
            }
            else if (onlineFlow.State == OnlineFlowState.JoiningLobby &&
                     (state.Status == SessionHudStatus.Authenticating ||
                      state.Status == SessionHudStatus.Connected))
            {
                OnlineFlowTransition transport = onlineFlow.DispatchAsync(
                    OnlineFlowCommand.TransportConnected, onlineOperation, null);
                if (transport.Accepted && state.Status == SessionHudStatus.Connected)
                {
                    OnlineFlowTransition authenticated = onlineFlow.DispatchAsync(
                        OnlineFlowCommand.Authenticated, onlineOperation, null);
                    if (authenticated.Accepted) nativeMenu.ShowFriendConnected();
                }
            }
            else if (onlineFlow.State == OnlineFlowState.Authenticating &&
                     state.Status == SessionHudStatus.Connected)
            {
                OnlineFlowTransition authenticated = onlineFlow.DispatchAsync(
                    OnlineFlowCommand.Authenticated, onlineOperation, null);
                if (authenticated.Accepted) nativeMenu.ShowFriendConnected();
            }
            else if (onlineFlow.State == OnlineFlowState.Leaving &&
                     state.Status == SessionHudStatus.Offline)
            {
                OnlineFlowTransition transition = onlineFlow.DispatchAsync(
                    OnlineFlowCommand.CleanupComplete, onlineOperation, null);
                if (transition.Accepted)
                {
                    nativeMenu.CloseSessionMenu();
                    if (onlineFlow.State == OnlineFlowState.OnlineMenu)
                        onlineFlow.Dispatch(OnlineFlowCommand.Back);
                }
            }
            else if (onlineFlow.State == OnlineFlowState.WaitingForPlayers &&
                     state.Status == SessionHudStatus.Offline)
            {
                OnlineFlowTransition leave = onlineFlow.Dispatch(OnlineFlowCommand.Leave);
                if (!leave.Accepted) return;
                onlineOperation = leave.Operation;
                OnlineFlowTransition cleanup = onlineFlow.DispatchAsync(
                    OnlineFlowCommand.CleanupComplete, onlineOperation, null);
                if (cleanup.Accepted)
                {
                    nativeMenu.CloseSessionMenu();
                    if (onlineFlow.State == OnlineFlowState.OnlineMenu)
                        onlineFlow.Dispatch(OnlineFlowCommand.Back);
                }
            }
        }
    }
}
