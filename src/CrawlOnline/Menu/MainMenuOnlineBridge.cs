using System;
using UnityEngine;

namespace CrawlOnline.Menu
{
    internal sealed class MainMenuOnlineBridge : MonoBehaviour
    {
        private Action selected;

        public void Initialise(Action callback)
        {
            selected = callback;
        }

        // Invoked by the legitimate menu's existing message dispatch.
        public void MsgCrawlOnline()
        {
            if (selected != null) selected();
        }

        private void OnDestroy()
        {
            selected = null;
        }
    }
}
