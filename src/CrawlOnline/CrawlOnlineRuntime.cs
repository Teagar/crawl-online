using BepInEx.Logging;
using UnityEngine;

namespace CrawlOnline
{
    public sealed class CrawlOnlineRuntime
    {
        public const string Version = "0.1.0";
        private readonly ManualLogSource log;
        private SteamLobbySession session;

        public CrawlOnlineRuntime(ManualLogSource logSource)
        {
            log = logSource;
            session = new SteamLobbySession(log);
            log.LogInfo("Ready: F8 host, F7 invite, F9 leave");
        }

        public void Tick()
        {
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
            if (session != null)
            {
                session.Dispose();
                session = null;
            }
        }
    }
}
