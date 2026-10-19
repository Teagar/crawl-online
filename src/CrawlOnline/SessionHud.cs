using UnityEngine;

namespace CrawlOnline
{
    // Passive, original overlay built from solid-colour primitives and the GUI font
    // already loaded by the legitimate game. No Crawl art or copied layout ships here.
    internal sealed class SessionHud
    {
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
        private static readonly Color VersionText = new Color(0.36f, 0.30f, 0.29f, 0.94f);
        private static readonly Color VersionShadow = new Color(0.035f, 0.025f, 0.03f, 0.78f);

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
        private GUIStyle watermarkStyle;
        private GUIStyle watermarkCountStyle;
        private Font versionFont;
        private Font nativeFont;
        private int nextFontScanFrame;

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
            int scale = SessionHudPresentation.ScaleForHeight(Screen.height);
            EnsureStyles(scale);

            // Prime the native font while the legitimate menu labels still exist,
            // even though the passive session strip stays hidden over that menu.
            if (nativeMenuActive && !state.IsSimulation) return;
            bool hasNotice = !string.IsNullOrEmpty(notice);
            if (state.Status == SessionHudStatus.Offline && !hasNotice && !onlineOperationActive) return;

            float logicalScreenHeight = Screen.height / (float)scale;
            float logicalScreenWidth = Screen.width / (float)scale;
            float width = detailsVisible && !minimized
                ? SessionHudPresentation.ExpandedWidth : SessionHudPresentation.CompactWidth;
            float height = detailsVisible && !minimized
                ? SessionHudPresentation.ExpandedHeight : SessionHudPresentation.CompactHeight;
            float x = Mathf.Max(10f, Mathf.Min(14f, logicalScreenWidth - width - 10f));
            float y = Mathf.Max(10f, logicalScreenHeight - height - 14f);

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            try
            {
                bool watermarkOnly = nativeMenuActive && state.IsSimulation;
                if (!watermarkOnly)
                {
                    DrawFrame(new Rect(x, y, width, height));
                    if (detailsVisible && !minimized) DrawExpanded(state, x, y, width);
                    else DrawContext(state, x, y, width, hasNotice ? notice : null);
                }
                if (state.IsSimulation)
                    DrawSimulationWatermark(state, logicalScreenWidth, logicalScreenHeight);
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
            DrawLabel(new Rect(x + 17f, y + 8f, 98f, 24f),
                SessionHudPresentation.StatusLabel(state.Status), titleStyle);
            DrawLabel(new Rect(x + 119f, y + 8f, width - 131f, 24f),
                SessionHudPresentation.ContextLabel(state, message), smallStyle);
        }

        private void DrawExpanded(SessionHudState state, float x, float y, float width)
        {
            DrawSolid(new Rect(x + 3f, y + 3f, width - 6f, 31f), PanelTop);
            DrawSolid(new Rect(x + 9f, y + 8f, 4f, 20f), StatusColor(state.Status));
            DrawLabel(new Rect(x + 20f, y + 7f, 175f, 23f), "CRAWL ONLINE", titleStyle);
            DrawLabel(new Rect(x + width - 112f, y + 7f, 98f, 23f),
                SessionHudPresentation.StatusLabel(state.Status), statusStyle);

            DrawSolid(new Rect(x + 10f, y + 42f, width - 20f, 1f), Border);
            DrawLabel(new Rect(x + 12f, y + 50f, 138f, 20f),
                SessionHudPresentation.NativeText(state.RoleLabel), roleStyle);
            DrawLabel(new Rect(x + 154f, y + 50f, width - 166f, 20f),
                SessionHudPresentation.RosterLabel(state), roleStyle);
            DrawLabel(new Rect(x + 12f, y + 76f, width - 24f, 39f),
                SessionHudPresentation.NativeText(state.Message), bodyStyle);

            DrawSolid(new Rect(x + 10f, y + 122f, width - 20f, 1f), Border);
            DrawLabel(new Rect(x + 12f, y + 130f, width - 24f, 18f),
                state.IsSimulation
                    ? "F7  DROP / RECONNECT     F9  STOP"
                    : "F8  HOST     F7  INVITE     F9  LEAVE", smallStyle);
            DrawLabel(new Rect(x + 12f, y + 153f, width - 24f, 18f),
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
            Font fallbackFont = GUI.skin != null ? GUI.skin.font : null;
            Font font = FindNativeInterfaceFont();
            if (font == null) font = fallbackFont;
            Font detectedVersionFont = FindVersionFont();
            if (detectedVersionFont == null) detectedVersionFont = font;
            if (styledScale == scale && styledFont == font && versionFont == detectedVersionFont &&
                titleStyle != null) return;

            styledScale = scale;
            styledFont = font;
            versionFont = detectedVersionFont;
            titleStyle = CreateStyle(font, Text, 15, FontStyle.Normal, TextAnchor.MiddleLeft);
            statusStyle = CreateStyle(font, Text, 12, FontStyle.Normal, TextAnchor.MiddleRight);
            roleStyle = CreateStyle(font, Text, 12, FontStyle.Normal, TextAnchor.MiddleLeft);
            bodyStyle = CreateStyle(font, MutedText, 11, FontStyle.Normal, TextAnchor.UpperLeft);
            bodyStyle.wordWrap = true;
            smallStyle = CreateStyle(font, MutedText, 10, FontStyle.Normal, TextAnchor.MiddleLeft);
            watermarkStyle = CreateStyle(versionFont, VersionText, 36, FontStyle.Normal,
                TextAnchor.LowerLeft);
            watermarkCountStyle = CreateStyle(versionFont, VersionText, 72, FontStyle.Normal,
                TextAnchor.LowerLeft);
        }

        private void DrawSimulationWatermark(SessionHudState state, float screenWidth, float screenHeight)
        {
            Rect title = new Rect(14f, screenHeight - 96f, 510f, 58f);
            Rect count = new Rect(14f, screenHeight - 85f, 260f, 80f);
            DrawWatermarkLabel(title, state.SimulationLabel, watermarkStyle);
            DrawWatermarkLabel(count, state.SimulationPlayerCountLabel, watermarkCountStyle);
        }

        private static void DrawWatermarkLabel(Rect rect, string label, GUIStyle style)
        {
            Color textColor = style.normal.textColor;
            style.normal.textColor = VersionShadow;
            style.hover.textColor = VersionShadow;
            style.active.textColor = VersionShadow;
            style.focused.textColor = VersionShadow;
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), label, style);
            style.normal.textColor = textColor;
            style.hover.textColor = textColor;
            style.active.textColor = textColor;
            style.focused.textColor = textColor;
            GUI.Label(rect, label, style);
        }

        private static Font FindVersionFont()
        {
            UnityEngine.Object[] textMeshes = Resources.FindObjectsOfTypeAll(typeof(TextMesh));
            for (int i = 0; i < textMeshes.Length; i++)
            {
                TextMesh text = textMeshes[i] as TextMesh;
                if (text != null && !string.IsNullOrEmpty(text.text) &&
                    text.text.StartsWith("v1.", System.StringComparison.OrdinalIgnoreCase) &&
                    text.font != null) return text.font;
            }
            return null;
        }

        private Font FindNativeInterfaceFont()
        {
            if (nativeFont != null) return nativeFont;
            if (Time.frameCount < nextFontScanFrame) return null;
            nextFontScanFrame = Time.frameCount + 30;

            UnityEngine.Object[] textMeshes = Resources.FindObjectsOfTypeAll(typeof(TextMesh));
            Font best = null;
            int bestScore = 0;
            for (int i = 0; i < textMeshes.Length; i++)
            {
                TextMesh text = textMeshes[i] as TextMesh;
                if (text == null || text.font == null || string.IsNullOrEmpty(text.text)) continue;
                string label = text.text.Trim().ToUpperInvariant();
                int score = InterfaceFontScore(label);
                if (text.gameObject != null && text.gameObject.activeInHierarchy) score += 20;
                if (score <= bestScore) continue;
                best = text.font;
                bestScore = score;
            }
            if (best != null) nativeFont = best;
            return nativeFont;
        }

        private static int InterfaceFontScore(string label)
        {
            if (label == "ONLINE") return 140;
            if (label == "START GAME" || label == "THE VAULT") return 120;
            if (label == "HOST GAME" || label == "JOIN FRIEND" || label == "BACK") return 110;
            if (label == "OPTIONS" || label == "EXIT" || label == "QUIT") return 90;
            return 0;
        }

        private static void DrawLabel(Rect rect, string label, GUIStyle style)
        {
            Color color = style.normal.textColor;
            SetStyleColor(style, Shadow);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 2f, rect.width, rect.height), label, style);
            SetStyleColor(style, color);
            GUI.Label(rect, label, style);
        }

        private static void SetStyleColor(GUIStyle style, Color color)
        {
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
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

    }
}
