using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace CryoController
{
    /// <summary>
    /// On-screen controls for touchscreens: a stick on the left half that appears where the thumb lands, look by dragging
    /// on the right, and buttons for jump, crouch, use, sprint, throw, inspect and pause. The stick and buttons drive a
    /// virtual gamepad the player's actions already read, so everything that works with a gamepad works with them; the
    /// look drag turns the view directly. The virtual gamepad only exists while the controls show, and under the pause
    /// menu only the Pause button stays, to resume. Drawn with IMGUI and scaled with the screen height.
    /// </summary>
    [RequireComponent(typeof(FirstPersonController))]
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Touch Controls")]
    public class TouchControls : MonoBehaviour
    {
        /// <summary>When the controls show.</summary>
        public enum ShowMode
        {
            /// <summary>On phones and tablets, and anywhere else once the touchscreen is touched.</summary>
            Automatic,
            Always,
            Never,
        }

        /// <summary>The on-screen buttons.</summary>
        public enum Button
        {
            Jump,
            Crouch,
            Interact,
            Sprint,
            Throw,
            Inspect,
            Pause,
        }

        // Sizes for a 1080-line screen, scaled with the real one.
        private const float Margin = 48f;

        [Tooltip("When the controls show: on phones and tablets and wherever the touchscreen is touched, always, or never.")]
        [SerializeField] private ShowMode show = ShowMode.Automatic;
        [Tooltip("Degrees the view turns per pixel dragged on a 1080-line screen.")]
        [SerializeField, Min(0.01f)] private float lookSensitivity = 0.18f;
        [Tooltip("How far the stick moves from where the thumb landed to full speed, in pixels on a 1080-line screen.")]
        [SerializeField, Min(10f)] private float stickRadius = 110f;
        [Tooltip("Diameter of the buttons, in pixels on a 1080-line screen.")]
        [SerializeField, Min(30f)] private float buttonSize = 120f;
        [Tooltip("How solid the controls are drawn, from 0 (invisible) to 1.")]
        [SerializeField, Range(0f, 1f)] private float opacity = 0.6f;

        /// <summary>True while the controls are on screen: all of them, or only Pause while the game is paused.</summary>
        public bool IsShowing { get; private set; }

        /// <summary>The stick's value, -1 to 1 on each axis.</summary>
        public Vector2 Stick { get; private set; }

        private FirstPersonController controller;
        private PlayerCarry carry;
        private PauseMenu pauseMenu;
        private Gamepad pad;
        private bool touched;               // the touchscreen has been touched since the controls were enabled
        private int stickFinger = -1;       // touch ids holding the stick, the look drag and each button
        private int lookFinger = -1;
        private Vector2 stickOrigin;
        private Vector2 lookLast;
        private readonly int[] buttonFingers = { -1, -1, -1, -1, -1, -1, -1 };
        private bool sprintLatched;         // Sprint is a toggle on screen: it stays on until tapped again or the stick lets go
        private bool pauseOnly;             // the game is paused: only the Pause button shows, to resume
        private GUIStyle labelStyle;
        private Vector2 sentStick;          // the virtual pad's state last sent
        private uint sentButtons;
        private float sentTrigger;
        private bool sentOnce;

        private static readonly string[] Labels = { "Jump", "Crouch", "Use", "Sprint", "Throw", "Inspect", "II" };

        private void Awake()
        {
            controller = GetComponent<FirstPersonController>();
            carry = GetComponent<PlayerCarry>();
            pauseMenu = GetComponent<PauseMenu>();
        }

        private void OnDisable()
        {
            SetShowing(false);
            touched = false;
        }

        /// <summary>
        /// Where a finger starts the stick: the left 40 % of the screen, in pixels from the bottom left (the Input System's
        /// touch positions). A finger elsewhere, not on a button, looks.
        /// </summary>
        public Rect StickArea => new Rect(0f, 0f, Screen.width * 0.4f, Screen.height);

        /// <summary>Where a button is on screen, in pixels from the bottom left (the Input System's touch positions).</summary>
        public Rect ButtonScreenRect(Button button)
        {
            float scale = HudDrawing.Scale;
            float size = buttonSize * scale;
            float margin = Margin * scale;
            float right = Screen.width - margin;
            float bottom = margin;
            Vector2 center;
            switch (button)
            {
                case Button.Jump: center = new Vector2(right - size * 0.5f, bottom + size * 0.5f); break;
                case Button.Crouch: center = new Vector2(right - size * 1.75f, bottom + size * 0.35f); break;
                case Button.Interact: center = new Vector2(right - size * 0.6f, bottom + size * 1.75f); break;
                case Button.Sprint: center = new Vector2(right - size * 3f, bottom + size * 0.35f); break;
                case Button.Throw: center = new Vector2(right - size * 1.85f, bottom + size * 1.6f); break;
                case Button.Inspect: center = new Vector2(right - size * 1.2f, bottom + size * 2.85f); break;
                default:
                    float small = size * 0.6f;
                    return new Rect(Screen.width - margin - small, Screen.height - margin - small, small, small);
            }
            float s = button == Button.Jump ? size : size * 0.8f;
            return new Rect(center.x - s * 0.5f, center.y - s * 0.5f, s, s);
        }

        // Buttons that only make sense while carrying something show only then, and only Pause while paused.
        private bool Available(Button button)
        {
            if (pauseOnly)
            {
                return button == Button.Pause;
            }
            bool carrying = carry != null && carry.IsCarrying;
            switch (button)
            {
                case Button.Throw: return carrying && carry.CanThrow;
                case Button.Inspect: return carrying && carry.CanInspect;
                case Button.Pause: return pauseMenu != null;
                default: return true;
            }
        }

        private void Update()
        {
            Touchscreen screen = Touchscreen.current;
            if (screen != null && !touched && screen.primaryTouch.press.isPressed)
            {
                touched = true;
            }
            bool wanted = show == ShowMode.Always
                          || (show == ShowMode.Automatic && (Application.isMobilePlatform || touched));
            bool paused = pauseMenu != null && pauseMenu.IsPaused;
            bool nowPauseOnly = wanted && !controller.InputEnabled && paused;
            if (nowPauseOnly != pauseOnly)
            {
                pauseOnly = nowPauseOnly;
                ReleaseAll();   // a finger on a control when the game pauses doesn't keep it held
            }
            SetShowing(wanted && (controller.InputEnabled || pauseOnly));
            if (!IsShowing)
            {
                return;
            }

            if (screen != null)
            {
                foreach (TouchControl touch in screen.touches)
                {
                    Track(touch);
                }
            }
            if (stickFinger < 0)
            {
                Stick = Vector2.zero;
                sprintLatched = false;
            }
            Send();
        }

        private void Track(TouchControl touch)
        {
            int id = touch.touchId.ReadValue();
            UnityEngine.InputSystem.TouchPhase phase = touch.phase.ReadValue();
            Vector2 position = touch.position.ReadValue();
            bool ended = phase == UnityEngine.InputSystem.TouchPhase.Ended || phase == UnityEngine.InputSystem.TouchPhase.Canceled;
            if (phase == UnityEngine.InputSystem.TouchPhase.None)
            {
                return;
            }

            if (!ended && !Holding(id))
            {
                // Only a finger that lands now: one still down from before (across a pause) doesn't press again. It is
                // taken from where it landed, even if it has already moved since (events can arrive together).
                if (!touch.press.wasPressedThisFrame)
                {
                    return;
                }
                Begin(id, touch.startPosition.ReadValue());
                lookLast = position;
            }
            if (ended)
            {
                Release(id);
                return;
            }

            if (id == stickFinger)
            {
                float radius = stickRadius * HudDrawing.Scale;
                Stick = Vector2.ClampMagnitude((position - stickOrigin) / radius, 1f);
            }
            else if (id == lookFinger)
            {
                Vector2 delta = position - lookLast;
                lookLast = position;
                controller.AddLook(delta * (lookSensitivity / HudDrawing.Scale));
            }
        }

        private bool Holding(int id)
        {
            if (id == stickFinger || id == lookFinger)
            {
                return true;
            }
            foreach (int finger in buttonFingers)
            {
                if (finger == id)
                {
                    return true;
                }
            }
            return false;
        }

        private void Begin(int id, Vector2 position)
        {
            for (int i = 0; i < buttonFingers.Length; i++)
            {
                Button button = (Button)i;
                if (Available(button) && Contains(ButtonScreenRect(button), position, 1.15f))
                {
                    buttonFingers[i] = id;
                    if (button == Button.Sprint)
                    {
                        sprintLatched = !sprintLatched;
                    }
                    return;
                }
            }
            if (pauseOnly)
            {
                return;   // only the Pause button works under the menu
            }
            if (StickArea.Contains(position) && stickFinger < 0)
            {
                stickFinger = id;
                stickOrigin = position;
                Stick = Vector2.zero;
            }
            else if (lookFinger < 0)
            {
                lookFinger = id;
                lookLast = position;
            }
        }

        // Whether the point is in the rect grown by the factor about its centre: buttons are easier to hit than to see.
        private static bool Contains(Rect rect, Vector2 point, float grow)
        {
            Vector2 half = rect.size * (0.5f * grow);
            Vector2 offset = point - rect.center;
            return Mathf.Abs(offset.x) <= half.x && Mathf.Abs(offset.y) <= half.y;
        }

        private void Release(int id)
        {
            if (id == stickFinger)
            {
                stickFinger = -1;
                Stick = Vector2.zero;
            }
            if (id == lookFinger)
            {
                lookFinger = -1;
            }
            for (int i = 0; i < buttonFingers.Length; i++)
            {
                if (buttonFingers[i] == id)
                {
                    buttonFingers[i] = -1;
                }
            }
        }

        private void ReleaseAll()
        {
            stickFinger = -1;
            lookFinger = -1;
            for (int i = 0; i < buttonFingers.Length; i++)
            {
                buttonFingers[i] = -1;
            }
            Stick = Vector2.zero;
            sprintLatched = false;
            Send();
        }

        private bool Pressed(Button button)
        {
            return buttonFingers[(int)button] >= 0;
        }

        // The virtual gamepad's state: the stick, and the buttons the shipped actions bind on a gamepad.
        private void Send()
        {
            if (pad == null || !pad.added)
            {
                return;
            }
            var state = new GamepadState { leftStick = Stick };
            state = state.WithButton(GamepadButton.South, Pressed(Button.Jump));
            state = state.WithButton(GamepadButton.East, Pressed(Button.Crouch));
            state = state.WithButton(GamepadButton.North, Pressed(Button.Interact));
            state = state.WithButton(GamepadButton.LeftStick, sprintLatched && Stick.sqrMagnitude > 0f);
            state = state.WithButton(GamepadButton.DpadUp, Pressed(Button.Inspect));
            state = state.WithButton(GamepadButton.Start, Pressed(Button.Pause));
            state.rightTrigger = Pressed(Button.Throw) ? 1f : 0f;
            if (state.leftStick == sentStick && state.buttons == sentButtons && state.rightTrigger == sentTrigger && sentOnce)
            {
                return;   // nothing changed: no event, so an idle virtual pad doesn't keep becoming the current gamepad
            }
            sentStick = state.leftStick;
            sentButtons = state.buttons;
            sentTrigger = state.rightTrigger;
            sentOnce = true;
            InputSystem.QueueStateEvent(pad, state);
        }

        // The virtual gamepad exists only while the controls show, so it never counts as a connected gamepad otherwise.
        private void SetShowing(bool showing)
        {
            if (IsShowing == showing)
            {
                return;
            }
            IsShowing = showing;
            if (showing)
            {
                pad = InputSystem.AddDevice<Gamepad>("CryoController Touch Gamepad");
                sentOnce = false;
            }
            else
            {
                stickFinger = -1;
                lookFinger = -1;
                for (int i = 0; i < buttonFingers.Length; i++)
                {
                    buttonFingers[i] = -1;
                }
                Stick = Vector2.zero;
                sprintLatched = false;
                pauseOnly = false;
                if (pad != null && pad.added)
                {
                    InputSystem.RemoveDevice(pad);
                }
                pad = null;
            }
            if (controller != null)
            {
                controller.OnScreenControls = showing;
            }
        }

        // ------------------------------------------------------------------ Drawing

        private void OnGUI()
        {
            if (!IsShowing || opacity <= 0f || Event.current.type != EventType.Repaint)
            {
                return;
            }
            GUI.depth = -10;   // over the pause menu, for the Pause button that resumes
            float scale = HudDrawing.Scale;
            if (labelStyle == null)
            {
                // GUI.skin is only available inside OnGUI.
                labelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    padding = new RectOffset(0, 0, 0, 0),
                    clipping = TextClipping.Overflow,
                };
            }
            labelStyle.fontSize = Mathf.RoundToInt(20f * scale);
            Color fill = new Color(0f, 0f, 0f, 0.35f * opacity);
            Color line = new Color(1f, 1f, 1f, 0.7f * opacity);

            // The stick: where the thumb landed, or a hint of where it goes.
            float radius = stickRadius * scale;
            if (pauseOnly)
            {
                radius = 0f;   // no stick under the menu
            }
            Vector2 origin = stickFinger >= 0 ? stickOrigin : new Vector2(Margin * scale + radius * 1.3f, Margin * scale + radius * 1.3f);
            Vector2 guiOrigin = new Vector2(origin.x, Screen.height - origin.y);
            float baseSize = radius * 2f;
            Rect baseRect = new Rect(guiOrigin.x - radius, guiOrigin.y - radius, baseSize, baseSize);
            if (radius > 0f)
            {
                HudDrawing.Box(baseRect, fill, radius);
                HudDrawing.Outline(baseRect, line, Mathf.Max(1.5f, 2f * scale), radius);
                Vector2 knob = guiOrigin + new Vector2(Stick.x, -Stick.y) * radius;
                HudDrawing.Circle(knob, radius * 0.8f, new Color(1f, 1f, 1f, (stickFinger >= 0 ? 0.55f : 0.3f) * opacity));
            }

            for (int i = 0; i < buttonFingers.Length; i++)
            {
                Button button = (Button)i;
                if (!Available(button))
                {
                    continue;
                }
                Rect screenRect = ButtonScreenRect(button);
                Rect rect = new Rect(screenRect.x, Screen.height - screenRect.yMax, screenRect.width, screenRect.height);
                bool on = Pressed(button) || (button == Button.Sprint && sprintLatched);
                HudDrawing.Box(rect, on ? new Color(1f, 1f, 1f, 0.45f * opacity) : fill, rect.width * 0.5f);
                HudDrawing.Outline(rect, line, Mathf.Max(1.5f, 2f * scale), rect.width * 0.5f);
                labelStyle.normal.textColor = new Color(1f, 1f, 1f, opacity + 0.3f);
                GUI.Label(rect, Labels[i], labelStyle);
            }
        }
    }
}
