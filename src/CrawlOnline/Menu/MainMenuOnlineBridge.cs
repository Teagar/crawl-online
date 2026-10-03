using System;
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

        private void OnDestroy()
        {
            selected = null;
            host = null;
            join = null;
            back = null;
            invite = null;
            cancel = null;
        }
    }
}
