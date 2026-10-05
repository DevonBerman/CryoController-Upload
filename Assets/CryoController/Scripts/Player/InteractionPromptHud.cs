using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CryoController
{
    /// <summary>
    /// On-screen feedback for <see cref="PlayerInteractor"/> and <see cref="PlayerCarry"/>: a centre dot that rings
    /// what can be used, the current prompt with its button as a key cap, a hold-progress bar, the drop, throw and
    /// inspect buttons while carrying, and while inspecting the object's description and how to turn it, finish and
    /// drop it. The buttons follow the device in use and any rebinding. Drawn with IMGUI so it needs no UI setup, and
    /// scaled with the screen height; replace it with a proper UI that listens to the interactor's FocusChanged event
    /// when the game gets one.
    /// </summary>
    [RequireComponent(typeof(PlayerInteractor))]
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Interaction Prompt HUD")]
    public class InteractionPromptHud : MonoBehaviour
    {
        // Sizes for a 1080-line screen, scaled with the real one: the hold bar, the gap between a key cap and its label,
        // the gap between parts, and the padding inside the prompt's backing.
        private const float HoldBarWidth = 160f;
        private const float HoldBarHeight = 4f;
        private const float KeyGap = 8f;
        private const float PartGap = 26f;
        private const float PadX = 16f;
        private const float PadY = 8f;

        // The prompt sits this far down the screen (a share of its height). While inspecting, the object fills the
        // middle of the view, so the text moves down to these heights and the description is at most this share of the
        // screen wide.
        private const float PromptTop = 0.58f;
        private const float MessageTop = 0.66f;
        private const float InspectPromptTop = 0.86f;
        private const float InspectDescriptionBottom = 0.83f;
        private const float DescriptionWidth = 0.6f;

        [Tooltip("Button shown before the prompt, e.g. F. Empty shows the Interact binding of the device in use " +
                 "(keyboard and mouse, or gamepad), which follows rebinding.")]
        [SerializeField] private string keyHint = "";
        [Tooltip("Size of the prompt and description text, in pixels on a 1080-line screen; it scales with the screen " +
                 "height.")]
        [SerializeField, Min(8)] private int fontSize = 22;
        [Tooltip("Colour of the prompt, the description, the crosshair dot and the ring round it while something can be used.")]
        [SerializeField] private Color textColor = Color.white;
        [Tooltip("Colour of the hold bar.")]
        [SerializeField] private Color accentColor = new Color(1f, 0.6f, 0.2f, 1f);
        [Tooltip("Draw a small dot at the screen centre so the player can see what they are aiming at.")]
        [SerializeField] private bool showCrosshair = true;

        private PlayerInteractor interactor;
        private FirstPersonController controller;
        private GUIStyle promptStyle;
        private GUIStyle keyStyle;
        private GUIStyle descriptionStyle;

        // Binding texts by action and scheme. GetBindingDisplayString allocates and OnGUI runs several times a frame,
        // so they are kept until the bindings change.
        private readonly Dictionary<(InputAction, string), string> hints = new Dictionary<(InputAction, string), string>();

        // The parts of the prompt being drawn: up to three key caps, each followed by its label.
        private readonly string[] partKeys = new string[3];
        private readonly string[] partLabels = new string[3];
        private int partCount;

        private void Awake()
        {
            interactor = GetComponent<PlayerInteractor>();
            controller = GetComponent<FirstPersonController>();
        }

        private void OnEnable()
        {
            InputSystem.onActionChange += OnActionChange;
        }

        private void OnDisable()
        {
            InputSystem.onActionChange -= OnActionChange;
        }

        private void OnActionChange(object target, InputActionChange change)
        {
            if (change == InputActionChange.BoundControlsChanged)
            {
                hints.Clear();   // rebound, or devices changed
            }
        }

        private void OnGUI()
        {
            if (!controller.InputEnabled || interactor.Busy || Event.current.type != EventType.Repaint)
            {
                // A menu, a dialogue or a note is up: no crosshair or prompt over it. And nothing here takes input.
                return;
            }

            float scale = HudDrawing.Scale;
            EnsureStyles(scale);
            PlayerCarry carry = interactor.Carry;
            bool inspecting = carry != null && carry.IsInspecting;
            bool carrying = carry != null && carry.IsCarrying;

            if (showCrosshair && !inspecting)
            {
                Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                if (interactor.Current != null && !carrying)
                {
                    // A light ring on a dark one, so it shows on any surface.
                    float ring = 22f * scale;
                    float width = Mathf.Max(1.5f, 2f * scale);
                    float edge = Mathf.Max(1f, scale);
                    HudDrawing.Outline(new Rect(center.x - ring * 0.5f - edge, center.y - ring * 0.5f - edge, ring + edge * 2f,
                                                ring + edge * 2f), new Color(0f, 0f, 0f, 0.45f * textColor.a), width + edge * 2f,
                                       ring * 0.5f + edge);
                    HudDrawing.Outline(new Rect(center.x - ring * 0.5f, center.y - ring * 0.5f, ring, ring),
                                       HudDrawing.Fade(textColor, 0.9f), width, ring * 0.5f);
                }
                HudDrawing.Circle(center, 7f * scale, new Color(0f, 0f, 0f, 0.35f * textColor.a));
                HudDrawing.Circle(center, 4f * scale, HudDrawing.Fade(textColor, 0.9f));
            }

            string message = interactor.Message;
            if (!string.IsNullOrEmpty(message) && !inspecting)
            {
                DrawMessage(message, Screen.height * MessageTop, scale);
            }

            partCount = 0;
            if (inspecting)
            {
                if (controller.OnScreenControls)
                {
                    AddPart("", "Drag to turn");
                }
                else
                {
                    AddPart(controller.UsingGamepad || controller.ControllerOnly ? "Right stick" : "Mouse", "Turn");
                }
                AddPart(Hint(carry.InspectAction, null), "Done");
                AddPart(Hint(interactor.InteractAction, keyHint), "Drop");
            }
            else if (carrying)
            {
                AddPart(Hint(interactor.InteractAction, keyHint), "Drop");
                if (carry.CanThrow)
                {
                    AddPart(Hint(carry.ThrowAction, null), "Throw");
                }
                if (carry.CanInspect)
                {
                    AddPart(Hint(carry.InspectAction, null), "Inspect");
                }
            }
            else
            {
                string prompt = interactor.CurrentPrompt;
                if (string.IsNullOrEmpty(prompt))
                {
                    return;
                }
                AddPart(Hint(interactor.InteractAction, keyHint), prompt);
            }

            Rect backing = DrawPrompt(Screen.height * (inspecting ? InspectPromptTop : PromptTop), scale);

            string description = inspecting && carry.Carried != null ? carry.Carried.Description : null;
            if (!string.IsNullOrEmpty(description))
            {
                DrawDescription(description, scale);
            }

            float progress = interactor.HoldProgress;
            if (progress > 0f && progress < 1f)
            {
                float width = HoldBarWidth * scale;
                Rect bar = new Rect(Screen.width * 0.5f - width * 0.5f, backing.yMax + 8f * scale, width, HoldBarHeight * scale);
                HudDrawing.Bar(bar, progress, accentColor, new Color(0f, 0f, 0f, 0.45f));
            }
        }

        private void AddPart(string key, string label)
        {
            partKeys[partCount] = key;
            partLabels[partCount] = label;
            partCount++;
        }

        // The parts in a row on a rounded backing, centred across the screen at the height. Returns the backing.
        private Rect DrawPrompt(float top, float scale)
        {
            float width = 0f;
            for (int i = 0; i < partCount; i++)
            {
                float cap = HudDrawing.KeyCapWidth(partKeys[i], keyStyle);
                width += (cap > 0f ? cap + KeyGap * scale : 0f) + HudDrawing.Measure(promptStyle, partLabels[i]).x;
                if (i > 0)
                {
                    width += PartGap * scale;
                }
            }
            float height = Mathf.Round(promptStyle.fontSize * 1.55f) + PadY * 2f * scale;
            Rect backing = new Rect(Mathf.Round((Screen.width - width) * 0.5f - PadX * scale), Mathf.Round(top),
                                    width + PadX * 2f * scale, height);
            HudDrawing.Box(backing, new Color(0f, 0f, 0f, 0.45f), height * 0.5f);

            float x = backing.x + PadX * scale;
            float centerY = backing.center.y;
            Color capFill = new Color(1f, 1f, 1f, 0.12f);
            Color capBorder = HudDrawing.Fade(textColor, 0.6f);
            for (int i = 0; i < partCount; i++)
            {
                if (i > 0)
                {
                    x += PartGap * scale;
                }
                float cap = HudDrawing.KeyCap(x, centerY, partKeys[i], keyStyle, textColor, capFill, capBorder);
                if (cap > 0f)
                {
                    x += cap + KeyGap * scale;
                }
                float labelWidth = HudDrawing.Measure(promptStyle, partLabels[i]).x;
                HudDrawing.Text(new Rect(x, backing.y, labelWidth + 4f * scale, backing.height), partLabels[i], promptStyle,
                                textColor, shadow: false);
                x += labelWidth;
            }
            return backing;
        }

        // A message from the interactor on a rounded backing, fading out at the end of its time.
        private void DrawMessage(string message, float top, float scale)
        {
            float fade = Mathf.Clamp01((1f - interactor.MessageAge) * 5f);
            float width = HudDrawing.Measure(promptStyle, message).x;
            float height = Mathf.Round(promptStyle.fontSize * 1.55f) + PadY * 2f * scale;
            Rect backing = new Rect(Mathf.Round((Screen.width - width) * 0.5f - PadX * scale), Mathf.Round(top),
                                    width + PadX * 2f * scale, height);
            HudDrawing.Box(backing, new Color(0f, 0f, 0f, 0.45f * fade), height * 0.5f);
            HudDrawing.Text(new Rect(backing.x + PadX * scale, backing.y, width + 4f * scale, backing.height), message,
                            promptStyle, HudDrawing.Fade(textColor, fade), shadow: false);
        }

        // The inspected object's description on a rounded card above the prompt.
        private void DrawDescription(string description, float scale)
        {
            float pad = 16f * scale;
            float width = Mathf.Min(Screen.width * DescriptionWidth, 900f * scale);
            float textHeight = HudDrawing.MeasureHeight(descriptionStyle, description, width - pad * 2f);
            Rect card = new Rect(Mathf.Round((Screen.width - width) * 0.5f),
                                 Mathf.Round(Screen.height * InspectDescriptionBottom - textHeight - pad * 2f),
                                 width, textHeight + pad * 2f);
            HudDrawing.Box(card, new Color(0f, 0f, 0f, 0.5f), 12f * scale);
            HudDrawing.Text(new Rect(card.x + pad, card.y + pad, card.width - pad * 2f, textHeight), description,
                            descriptionStyle, textColor);
        }

        private void EnsureStyles(float scale)
        {
            if (promptStyle == null)
            {
                // GUI.skin is only available inside OnGUI.
                promptStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Overflow,
                    padding = new RectOffset(0, 0, 0, 0),
                };
                keyStyle = new GUIStyle(promptStyle) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                descriptionStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, wordWrap = true };
            }
            int size = Mathf.RoundToInt(fontSize * scale);
            promptStyle.fontSize = size;
            keyStyle.fontSize = Mathf.RoundToInt(size * 0.8f);
            descriptionStyle.fontSize = Mathf.RoundToInt(size * 0.9f);
        }

        // The button for the action on the device in use ("F", "LMB", "Y"), or the fixed text when one is set.
        private string Hint(InputAction action, string fixedText)
        {
            if (!string.IsNullOrEmpty(fixedText))
            {
                return fixedText;
            }
            if (action == null || controller.OnScreenControls)
            {
                return "";   // on-screen controls have their own buttons to tap
            }

            bool gamepad = controller.UsingGamepad || controller.ControllerOnly;
            string scheme = gamepad ? InputRebinder.GamepadScheme : InputRebinder.KeyboardScheme;
            if (!hints.TryGetValue((action, scheme), out string hint))
            {
                hint = InputRebinder.BindingDisplay(action, scheme) ?? "";
                hints[(action, scheme)] = hint;
            }
            return hint;
        }
    }
}
