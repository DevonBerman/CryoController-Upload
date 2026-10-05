using UnityEngine;

namespace CryoController.Demo
{
    // Demo only: rows of text and bars in the top-left corner, each on a rounded backing, drawn with IMGUI like the
    // package's own HUD and scaled with the screen height. Each demo component draws on its own rows.
    internal static class DemoHud
    {
        // Sizes for a 1080-line screen, scaled with the real one.
        private const float ReferenceHeight = 1080f;
        private const float FontSize = 20f;
        private const float Margin = 20f;
        private const float RowHeight = 34f;
        private const float RowGap = 6f;
        private const float PadX = 14f;
        private const float BarWidth = 220f;
        private const float BarHeight = 8f;

        private static GUIStyle style;
        private static readonly GUIContent content = new GUIContent();   // reused: OnGUI runs several times a frame

        // With domain reloading off (Enter Play Mode Settings), statics keep their values from the last play: the style is
        // made afresh each time instead.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            style = null;
        }

        private static float Scale => Mathf.Max(0.5f, Screen.height / ReferenceHeight);

        public static void Label(int row, string text)
        {
            if (string.IsNullOrEmpty(text) || Event.current.type != EventType.Repaint)
            {
                return;
            }
            float scale = Scale;
            if (style == null)
            {
                // GUI.skin only exists inside OnGUI.
                style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(0, 0, 0, 0) };
            }
            style.fontSize = Mathf.RoundToInt(FontSize * scale);
            content.text = text;
            Rect rect = RowRect(row, scale, style.CalcSize(content).x + PadX * 2f * scale);
            Rounded(rect, new Color(0f, 0f, 0f, 0.45f), rect.height * 0.5f);
            style.normal.textColor = Color.white;
            GUI.Label(new Rect(rect.x + PadX * scale, rect.y, rect.width, rect.height), text, style);
        }

        public static void Bar(int row, float fraction, Color color)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            float scale = Scale;
            Rect rect = RowRect(row, scale, (BarWidth + PadX * 2f) * scale);
            Rounded(rect, new Color(0f, 0f, 0f, 0.45f), rect.height * 0.5f);
            float height = BarHeight * scale;
            Rect bar = new Rect(rect.x + PadX * scale, rect.center.y - height * 0.5f, BarWidth * scale, height);
            Rounded(bar, new Color(color.r, color.g, color.b, 0.25f), height * 0.5f);
            Rounded(new Rect(bar.x, bar.y, Mathf.Max(height, bar.width * Mathf.Clamp01(fraction)), height), color, height * 0.5f);
        }

        private static void Rounded(Rect rect, Color color, float radius)
        {
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f, radius);
        }

        private static Rect RowRect(int row, float scale, float width)
        {
            return new Rect(Margin * scale, (Margin + row * (RowHeight + RowGap)) * scale, width, RowHeight * scale);
        }
    }
}
