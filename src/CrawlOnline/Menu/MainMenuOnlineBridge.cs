using System;
using System.Collections;
using BepInEx.Logging;
using UnityEngine;

namespace CrawlOnline.Menu
{
    internal sealed class MainMenuOnlineBridge : MonoBehaviour
    {
        private Action selected;
        private Action host;
        private Action join;
        private Action back;
        private Action invite;
        private Action cancel;
        private Action refresh;
        private Action<int> friend;
        private const string FocusProbeEnvironmentVariable = "CRAWL_ONLINE_MENU_FOCUS_PROBE";
        private readonly bool focusProbeEnabled = string.Equals(
            Environment.GetEnvironmentVariable(FocusProbeEnvironmentVariable), "1",
            StringComparison.Ordinal);
        private ManualLogSource log;
        private bool focusProbeAnnounced;

        public void Initialise(Action selectedCallback, Action hostCallback, Action joinCallback,
            Action backCallback, Action inviteCallback, Action cancelCallback)
        {
            selected = selectedCallback;
            host = hostCallback;
            join = joinCallback;
            back = backCallback;
            invite = inviteCallback;
            cancel = cancelCallback;
        }

        public void InitialiseFocusProbe(ManualLogSource logSource)
        {
            log = logSource;
            if (focusProbeEnabled && !focusProbeAnnounced)
            {
                focusProbeAnnounced = true;
                log.LogWarning("Menu focus probe enabled; it records six sanitized menu snapshots per selection request.");
            }
        }

        public void InitialiseJoin(Action refreshCallback, Action<int> friendCallback)
        {
            refresh = refreshCallback;
            friend = friendCallback;
        }

        public void ObserveFocus(string transition, Func<string> snapshot)
        {
            if (!focusProbeEnabled || snapshot == null || log == null) return;
            StartCoroutine(CaptureFocus(transition, snapshot));
        }

        private IEnumerator CaptureFocus(string transition, Func<string> snapshot)
        {
            var observation = new BoundedMenuFocusObservation();
            observation.Begin();
            int frame = 0;
            while (observation.TryTakeSample())
            {
                try
                {
                    log.LogInfo("MENU_FOCUS transition=" + transition + " frame=" + frame + " " + snapshot());
                }
                catch (Exception exception)
                {
                    log.LogWarning("MENU_FOCUS transition=" + transition + " frame=" + frame +
                        " observation failed: " + exception.GetType().Name + ".");
                    yield break;
                }
                frame++;
                yield return null;
            }
        }

        // Invoked by the legitimate menu's existing message dispatch.
        public void MsgCrawlOnline()
        {
            if (selected != null) selected();
        }

        public void MsgCrawlOnlineHost()
        {
            if (host != null) host();
        }

        public void MsgCrawlOnlineJoin()
        {
            if (join != null) join();
        }

        public void MsgCrawlOnlineBack()
        {
            if (back != null) back();
        }

        public void MsgCrawlOnlineInvite()
        {
            if (invite != null) invite();
        }

        public void MsgCrawlOnlineCancel()
        {
            if (cancel != null) cancel();
        }

        public void MsgCrawlOnlineRefresh()
        {
            if (refresh != null) refresh();
        }

        public void MsgCrawlOnlineFriend0()
        {
            if (friend != null) friend(0);
        }

        public void MsgCrawlOnlineFriend1()
        {
            if (friend != null) friend(1);
        }

        public void MsgCrawlOnlineFriend2()
        {
            if (friend != null) friend(2);
        }

        public void MsgCrawlOnlineNoop()
        {
        }

        private void OnDestroy()
        {
            selected = null;
            host = null;
            join = null;
            back = null;
            invite = null;
            cancel = null;
            refresh = null;
            friend = null;
            log = null;
        }
    }
}
