using UnityEngine;

namespace CryoController
{
    // Shared IMGUI drawing for the HUD components and the pause menu: rounded panels, key caps, bars and shadowed text.
    // Sizes are given for a 1080-line screen and multiplied by Scale, so the UI keeps its proportions at any resolution.
    internal static class HudDrawing
    {
        /// <summary>The screen height the UI's sizes are given for.</summary>
        public const float ReferenceHeight = 1080f;

        /// <summary>Multiply sizes given for a 1080-line screen by this.</summary>
        public static float Scale => Mathf.Max(0.5f, Screen.height / ReferenceHeight);

        // Reused for measuring: OnGUI runs several times a frame.
        private static readonly GUIContent content = new GUIContent();

        /// <summary>A filled rectangle, with rounded corners when radius is above 0.</summary>
        public static void Box(Rect rect, Color color, float radius = 0f)
        {
            if (color.a <= 0f || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f,
                            Mathf.Min(radius, rect.height * 0.5f, rect.width * 0.5f));
        }

        /// <summary>The outline of a rectangle, with rounded corners when radius is above 0.</summary>
        public static void Outline(Rect rect, Color color, float width, float radius = 0f)
        {
            if (color.a <= 0f || width <= 0f)
            {
                return;
            }
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, width,
                            Mathf.Min(radius, rect.height * 0.5f, rect.width * 0.5f));
        }

        /// <summary>A circle of the diameter, centred on the point.</summary>
        public static void Circle(Vector2 center, float diameter, Color color)
        {
            Box(new Rect(center.x - diameter * 0.5f, center.y - diameter * 0.5f, diameter, diameter), color, diameter * 0.5f);
        }

        /// <summary>A rounded bar filled to the fraction from the left.</summary>
        public static void Bar(Rect rect, float fraction, Color fill, Color back)
        {
            float radius = rect.height * 0.5f;
            Box(rect, back, radius);
            fraction = Mathf.Clamp01(fraction);
            if (fraction > 0f)
            {
                Box(new Rect(rect.x, rect.y, Mathf.Max(rect.height, rect.width * fraction), rect.height), fill, radius);
            }
        }

        /// <summary>The size of the text in the style.</summary>
        public static Vector2 Measure(GUIStyle style, string text)
        {
            content.text = text;
            return style.CalcSize(content);
        }

        /// <summary>Height of the text wrapped to the width.</summary>
        public static float MeasureHeight(GUIStyle style, string text, float width)
        {
            content.text = text;
            return style.CalcHeight(content, width);
        }

        /// <summary>Text in the colour, over a soft shadow that keeps it readable on any background.</summary>
        public static void Text(Rect rect, string text, GUIStyle style, Color color, bool shadow = true)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            if (shadow)
            {
                float offset = Mathf.Max(1f, Scale);
                style.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.55f);
                GUI.Label(new Rect(rect.x + offset, rect.y + offset, rect.width, rect.height), text, style);
            }
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
        }

        /// <summary>
        /// A key cap: the key's name in a rounded box, its left edge at x and centred on y. Returns its width. Nothing is
        /// drawn for an empty key.
        /// </summary>
        public static float KeyCap(float x, float centerY, string key, GUIStyle style, Color text, Color fill, Color border)
        {
            if (string.IsNullOrEmpty(key))
            {
                return 0f;
            }
            float scale = Scale;
            Vector2 size = Measure(style, key);
            float height = Mathf.Round(style.fontSize * 1.55f);
            float width = Mathf.Max(height, size.x + 14f * scale);
            Rect rect = new Rect(x, centerY - height * 0.5f, width, height);
            float radius = 6f * scale;
            Box(rect, fill, radius);
            Outline(rect, border, Mathf.Max(1f, 1.5f * scale), radius);
            style.normal.textColor = text;
            GUI.Label(rect, key, style);
            return width;
        }

        /// <summary>The width <see cref="KeyCap"/> takes for the key, without drawing it.</summary>
        public static float KeyCapWidth(string key, GUIStyle style)
        {
            if (string.IsNullOrEmpty(key))
            {
                return 0f;
            }
            float height = Mathf.Round(style.fontSize * 1.55f);
            return Mathf.Max(height, Measure(style, key).x + 14f * Scale);
        }

        /// <summary>A copy of the colour with its alpha multiplied.</summary>
        public static Color Fade(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, color.a * alpha);
        }
    }
}
