using UnityEngine;

namespace CrawlOnline
{
    // Passive, original overlay built from solid-colour primitives and the GUI font
    // already loaded by the legitimate game. No Crawl art or copied layout ships here.
    internal sealed class SessionHud
    {
        private const float ExpandedWidth = 320f;
        private const float ExpandedHeight = 184f;
        private const float ContextWidth = 300f;
        private const float ContextHeight = 38f;

        private static readonly Color Shadow = new Color(0.01f, 0.01f, 0.015f, 0.82f);
        private static readonly Color Panel = new Color(0.055f, 0.047f, 0.05f, 0.96f);
        private static readonly Color PanelTop = new Color(0.11f, 0.085f, 0.075f, 0.98f);
        private static readonly Color Border = new Color(0.48f, 0.36f, 0.24f, 1f);
        private static readonly Color BorderLight = new Color(0.72f, 0.57f, 0.36f, 1f);
        private static readonly Color Text = new Color(0.88f, 0.82f, 0.70f, 1f);
        private static readonly Color MutedText = new Color(0.61f, 0.56f, 0.49f, 1f);
        private static readonly Color Good = new Color(0.57f, 0.70f, 0.38f, 1f);
        private static readonly Color Warning = new Color(0.83f, 0.60f, 0.24f, 1f);
        private static readonly Color Bad = new Color(0.72f, 0.22f, 0.18f, 1f);

        private bool minimized;
        private bool detailsVisible;
        private string notice;
        private int styledScale;
        private Font styledFont;
        private GUIStyle titleStyle;
        private GUIStyle statusStyle;
        private GUIStyle roleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle smallStyle;

        public void ToggleMinimized()
        {
            minimized = !minimized;
        }

        public void ToggleTutorial()
        {
            detailsVisible = !detailsVisible;
        }

        public void ShowNotice(string message)
        {
            notice = message;
        }

        public void Draw(SessionHudState state, bool nativeMenuActive, bool onlineOperationActive)
        {
            if (state == null) return;
            if (nativeMenuActive) return;
            bool hasNotice = !string.IsNullOrEmpty(notice);
            if (state.Status == SessionHudStatus.Offline && !hasNotice && !onlineOperationActive) return;

            int scale = Mathf.Clamp(Mathf.FloorToInt(Screen.height / 540f), 1, 2);
            EnsureStyles(scale);

            float logicalScreenHeight = Screen.height / (float)scale;
            float logicalScreenWidth = Screen.width / (float)scale;
            float width = detailsVisible && !minimized ? ExpandedWidth : ContextWidth;
            float height = detailsVisible && !minimized ? ExpandedHeight : ContextHeight;
            float x = Mathf.Max(10f, Mathf.Min(14f, logicalScreenWidth - width - 10f));
            float y = Mathf.Max(10f, logicalScreenHeight - height - 14f);

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            try
            {
                DrawFrame(new Rect(x, y, width, height));
                if (detailsVisible && !minimized) DrawExpanded(state, x, y, width);
                else DrawContext(state, x, y, width, hasNotice ? notice : null);
            }
            finally
            {
                GUI.color = previousColor;
                GUI.matrix = previousMatrix;
            }
        }

        private void DrawContext(SessionHudState state, float x, float y, float width, string message)
        {
            DrawSolid(new Rect(x + 5f, y + 5f, 4f, 28f), StatusColor(state.Status));
            GUI.Label(new Rect(x + 17f, y + 8f, 92f, 22f), StatusLabel(state.Status), titleStyle);
            string context = message;
            if (string.IsNullOrEmpty(context))
                context = state.Status == SessionHudStatus.Error || state.Status == SessionHudStatus.Offline
                    ? state.Message :
                    state.RoleLabel + "  " + RosterLabel(state);
            GUI.Label(new Rect(x + 113f, y + 8f, width - 125f, 22f), context, smallStyle);
        }

        private void DrawExpanded(SessionHudState state, float x, float y, float width)
        {
            DrawSolid(new Rect(x + 3f, y + 3f, width - 6f, 31f), PanelTop);
            DrawSolid(new Rect(x + 9f, y + 8f, 4f, 20f), StatusColor(state.Status));
            GUI.Label(new Rect(x + 20f, y + 7f, 175f, 23f), "CRAWL ONLINE", titleStyle);
            GUI.Label(new Rect(x + width - 102f, y + 7f, 88f, 23f), StatusLabel(state.Status), statusStyle);

            DrawSolid(new Rect(x + 10f, y + 42f, width - 20f, 1f), Border);
            GUI.Label(new Rect(x + 12f, y + 50f, 138f, 20f), state.RoleLabel.ToUpperInvariant(), roleStyle);
            GUI.Label(new Rect(x + 154f, y + 50f, width - 166f, 20f), RosterLabel(state), roleStyle);
            GUI.Label(new Rect(x + 12f, y + 76f, width - 24f, 39f), state.Message, bodyStyle);

            DrawSolid(new Rect(x + 10f, y + 122f, width - 20f, 1f), Border);
            GUI.Label(new Rect(x + 12f, y + 130f, width - 24f, 18f),
                "F8  HOST     F7  INVITE     F9  LEAVE", smallStyle);
            GUI.Label(new Rect(x + 12f, y + 153f, width - 24f, 18f),
                "F5  DETAILS     F6  MINIMIZE", smallStyle);
        }

        private static void DrawFrame(Rect rect)
        {
            DrawSolid(new Rect(rect.x + 4f, rect.y + 5f, rect.width, rect.height), Shadow);
            DrawSolid(rect, Panel);
            DrawOutline(rect, Border, 2f);
            DrawOutline(new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, rect.height - 8f), BorderLight, 1f);

            // Stepped corner marks echo Crawl's block-built interface without copying art.
            DrawSolid(new Rect(rect.x - 2f, rect.y + 8f, 4f, 17f), BorderLight);
            DrawSolid(new Rect(rect.x + 8f, rect.y - 2f, 17f, 4f), BorderLight);
            DrawSolid(new Rect(rect.x + rect.width - 2f, rect.y + rect.height - 25f, 4f, 17f), BorderLight);
            DrawSolid(new Rect(rect.x + rect.width - 25f, rect.y + rect.height - 2f, 17f, 4f), BorderLight);
        }

        private void EnsureStyles(int scale)
        {
            Font font = GUI.skin != null ? GUI.skin.font : null;
            if (styledScale == scale && styledFont == font && titleStyle != null) return;

            styledScale = scale;
            styledFont = font;
            titleStyle = CreateStyle(font, Text, 14, FontStyle.Bold, TextAnchor.MiddleLeft);
            statusStyle = CreateStyle(font, Text, 11, FontStyle.Bold, TextAnchor.MiddleRight);
            roleStyle = CreateStyle(font, Text, 11, FontStyle.Bold, TextAnchor.MiddleLeft);
            bodyStyle = CreateStyle(font, MutedText, 11, FontStyle.Normal, TextAnchor.UpperLeft);
            bodyStyle.wordWrap = true;
            smallStyle = CreateStyle(font, MutedText, 10, FontStyle.Bold, TextAnchor.MiddleLeft);
        }

        private static GUIStyle CreateStyle(Font font, Color color, int size, FontStyle fontStyle, TextAnchor alignment)
        {
            var style = new GUIStyle(GUI.skin.label);
            style.font = font;
            style.fontSize = size;
            style.fontStyle = fontStyle;
            style.alignment = alignment;
            style.clipping = TextClipping.Clip;
            style.wordWrap = false;
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            style.padding = new RectOffset(0, 0, 0, 0);
            return style;
        }

        private static void DrawOutline(Rect rect, Color color, float thickness)
        {
            DrawSolid(new Rect(rect.x, rect.y, rect.width, thickness), color);
            DrawSolid(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            DrawSolid(new Rect(rect.x, rect.y, thickness, rect.height), color);
            DrawSolid(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private static void DrawSolid(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static Color StatusColor(SessionHudStatus status)
        {
            switch (status)
            {
                case SessionHudStatus.Connected: return Good;
                case SessionHudStatus.Error: return Bad;
                case SessionHudStatus.CreatingLobby:
                case SessionHudStatus.Authenticating: return Warning;
                default: return BorderLight;
            }
        }

        private static string StatusLabel(SessionHudStatus status)
        {
            switch (status)
            {
                case SessionHudStatus.CreatingLobby: return "CREATING";
                case SessionHudStatus.WaitingForPeers: return "WAITING";
                case SessionHudStatus.Authenticating: return "LINKING";
                case SessionHudStatus.Connected: return "CONNECTED";
                case SessionHudStatus.Error: return "ERROR";
                default: return "OFFLINE";
            }
        }

        private static string RosterLabel(SessionHudState state)
        {
            string text = "PLAYERS  ";
            for (int slot = 0; slot < 4; slot++)
            {
                if (slot > 0) text += " ";
                text += state.IsSlotConnected(slot) ? (slot + 1).ToString() : "-";
            }
            return text + "   " + state.ConnectedPlayerCount + "/4";
        }
    }
}
