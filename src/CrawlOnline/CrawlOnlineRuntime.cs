using BepInEx.Logging;
using CrawlOnline.Determinism;
using CrawlOnline.Authoritative;
using CrawlOnline.Diagnostics;
using CrawlOnline.Menu;
using CrawlOnline.Online;
using UnityEngine;

namespace CrawlOnline
{
    public sealed class CrawlOnlineRuntime
    {
        public const string Version = "0.1.0";
        private readonly ManualLogSource log;
        private SteamLobbySession session;
        private DeterminismHarness harness;
        private AuthoritativeSynchronizer synchronizer;
        private SessionHud hud;
        private MenuContractProbe menuProbe;
        private NativeMainMenuIntegration nativeMenu;
        private OnlineFlowStateMachine onlineFlow;

        public CrawlOnlineRuntime(ManualLogSource logSource)
        {
            log = logSource;
            session = new SteamLobbySession(log);
            synchronizer = new AuthoritativeSynchronizer(session, log);
            harness = DeterminismHarness.TryCreate(log);
            hud = new SessionHud();
            menuProbe = MenuContractProbe.TryCreate(log);
            onlineFlow = new OnlineFlowStateMachine();
            nativeMenu = new NativeMainMenuIntegration(log, OnNativeOnlineSelected,
                OnNativeHostSelected, OnNativeJoinSelected, OnNativeOnlineBack);
            log.LogInfo("Ready: F8 host, F7 invite, F9 leave, F5 help, F6 HUD");
        }

        public void Tick()
        {
            if (menuProbe != null) menuProbe.Tick();
            if (nativeMenu != null) nativeMenu.Tick();
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
                session.Host();
            }
            else if (Input.GetKeyDown(KeyCode.F7))
            {
                session.OpenInviteDialog();
            }
            else if (Input.GetKeyDown(KeyCode.F9))
            {
                session.Leave();
            }

            session.Poll();
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
            if (hud != null && session != null) hud.Draw(session.GetHudState());
        }

        private bool OnNativeOnlineSelected()
        {
            OnlineFlowTransition transition = onlineFlow.Dispatch(OnlineFlowCommand.OpenOnline);
            if (transition.Accepted) log.LogInfo("Native ONLINE menu action selected.");
            return transition.Accepted;
        }

        private void OnNativeHostSelected()
        {
            log.LogInfo("HOST GAME selected; Steam hosting integration is not active yet.");
        }

        private void OnNativeJoinSelected()
        {
            log.LogInfo("JOIN FRIEND selected; Steam join integration is not active yet.");
        }

        private void OnNativeOnlineBack()
        {
            OnlineFlowTransition transition = onlineFlow.Dispatch(OnlineFlowCommand.Back);
            if (transition.Accepted) log.LogInfo("Returned from native Online submenu.");
        }
    }
}
