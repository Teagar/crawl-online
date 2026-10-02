using BepInEx.Logging;
using CrawlOnline.Determinism;
using CrawlOnline.Authoritative;
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

        public CrawlOnlineRuntime(ManualLogSource logSource)
        {
            log = logSource;
            session = new SteamLobbySession(log);
            synchronizer = new AuthoritativeSynchronizer(session, log);
            harness = DeterminismHarness.TryCreate(log);
            hud = new SessionHud();
            log.LogInfo("Ready: F8 host, F7 invite, F9 leave, F5 help, F6 HUD");
        }

        public void Tick()
        {
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
    }
}
