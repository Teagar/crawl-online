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
        private Action refresh;
        private Action<int> friend;

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

        public void InitialiseJoin(Action refreshCallback, Action<int> friendCallback)
        {
            refresh = refreshCallback;
            friend = friendCallback;
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

        // Invoked by the legitimate menu when the controller's cancel action
        // is pressed. The ONLINE submenu has no native BACK row to receive it,
        // so route the native message through the same fail-safe callback.
        public void MsgBack()
        {
            DispatchNativeCancel(NativeMenuCancelContract.BackMessage);
        }

        // Some supported menu states use MsgCancel rather than MsgBack for the
        // same native cancel action. Keep both names bound to the same handler.
        public void MsgCancel()
        {
            DispatchNativeCancel(NativeMenuCancelContract.CancelMessage);
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

        private void DispatchNativeCancel(string message)
        {
            if (NativeMenuCancelContract.IsCancelMessage(message) && back != null) back();
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
        }
    }
}
