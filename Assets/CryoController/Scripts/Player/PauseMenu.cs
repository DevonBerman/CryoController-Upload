using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CryoController
{
    /// <summary>
    /// Pause menu for the player. Escape or a gamepad's Start pauses the game (time scale 0, audio paused, player input
    /// off, cursor freed) and shows a menu with Resume, Options (the look and camera preferences, when a
    /// <see cref="PlayerPreferences"/> component is present), Controls (rebinding) and Quit. It works with the mouse,
    /// the keyboard (arrows or W/S, Enter or Space, Escape) and a gamepad (D-pad or left stick, A, B). The menu is
    /// drawn with IMGUI, scaled with the screen height, so it needs no UI setup; a real UI can replace the drawing and
    /// keep <see cref="Pause"/>, <see cref="Resume"/>, <see cref="IsPaused"/> and the events.
    /// </summary>
    [RequireComponent(typeof(FirstPersonController))]
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Pause Menu")]
    // Before the controller: both react to Escape, and the pause must note that the cursor was locked before the
    // controller releases it, so resuming locks it again.
    [DefaultExecutionOrder(-10)]
    public class PauseMenu : MonoBehaviour
    {
        // Keyboard and gamepad navigation: seconds a held direction waits before repeating, seconds between repeats,
        // stick deflection that counts as a direction, and left/right presses across a slider's whole range.
        private const float RepeatDelay = 0.4f;
        private const float RepeatInterval = 0.1f;
        private const float StickThreshold = 0.5f;
        private const int SliderSteps = 40;

#if UNITY_WEBGL && !UNITY_EDITOR
        // Frames the browser must have held the pointer lock before losing it counts as Escape: a lock that was asked
        // for but not yet granted (before the first click) can read as locked for a frame.
        private const int LockSettleFrames = 3;
#endif

        [Tooltip("Pause automatically when the game window loses focus. Builds only: the Editor loses focus all the time.")]
        [SerializeField] private bool pauseOnFocusLoss = true;
        [Tooltip("Show a Quit button. In the Editor it stops Play Mode. Never shown in a web build: a page can't quit.")]
        [SerializeField] private bool showQuitButton = true;
        [Tooltip("Save slot for Save and Load items on the main page, which save and load the scene with Save System. " +
                 "Empty shows neither.")]
        [SerializeField] private string saveSlot = "";
        [Tooltip("Size of the menu text, in pixels on a 1080-line screen; it scales with the screen height.")]
        [SerializeField, Min(8)] private int fontSize = 26;
        [Tooltip("Colour of the menu's panel. The game behind it is dimmed.")]
        [SerializeField] private Color panelColor = new Color(0.08f, 0.09f, 0.11f, 0.95f);
        [Tooltip("Colour of the menu text.")]
        [SerializeField] private Color textColor = Color.white;
        [Tooltip("Highlight behind the selected item.")]
        [SerializeField] private Color highlightColor = new Color(1f, 1f, 1f, 0.08f);
        [Tooltip("Colour of the title's underline, the selected item's marker, switches that are on and slider fills.")]
        [SerializeField] private Color accentColor = new Color(1f, 0.6f, 0.2f, 1f);
        [Tooltip("Actions the player may rebind on the Controls page, by name or Map/Name. Their Keyboard&Mouse and " +
                 "Gamepad bindings are listed; stick and pointer bindings are not rebindable.")]
        [SerializeField] private string[] rebindableActions = { "Move", "Jump", "Sprint", "Crouch", "Interact", "Zoom", "LeanLeft", "LeanRight", "Throw", "Inspect", "Flashlight" };

        /// <summary>True while the game is paused by this menu.</summary>
        public bool IsPaused { get; private set; }

        /// <summary>Raised when the game pauses.</summary>
        public event System.Action Paused;

        /// <summary>Raised when the game resumes.</summary>
        public event System.Action Resumed;

        private enum Page { Main, Options, Controls }

        private enum Kind { Button, Slider, Toggle, Binding }

        // One row of the current page. The keyboard and gamepad drive these; the mouse works on the IMGUI controls.
        private class Item
        {
            public Kind Kind;
            public string Label;
            public System.Action Activate;        // Enter, A or a click
            public System.Action<int> Adjust;     // left (-1) or right (+1)
            public bool AdjustRepeats;            // a held direction keeps adjusting (sliders); otherwise once per press
            public System.Func<string> Value;     // text on the right: a slider's value, a binding, the device
            public System.Func<float> Get;        // sliders
            public System.Action<float> Set;
            public float Min, Max;
            public System.Func<bool> GetToggle;   // toggles
            public System.Action<bool> SetToggle;
        }

        private FirstPersonController controller;
        private PlayerPreferences preferences;   // optional: without it there is no Options page and bindings are not saved
        private readonly InputRebinder rebinder = new InputRebinder();
        private bool mouseBlocked;      // see Update
        private int mouseBusyFrame;     // the last frame a mouse button was held (or a rebind ran) while blocked
        private readonly List<Item> items = new List<Item>();
        private Page page;
        private int selected;
        private string scheme = InputRebinder.KeyboardScheme;   // control scheme shown on the Controls page
        private bool usingGamepad;                 // the last menu input came from a gamepad
        private bool backPressed;                  // gamepad B went down while paused; see Update
        private bool navigationArmed;              // false on the first frame of a pause: keys held from the game do not navigate
        private int verticalHeld;                  // direction held last frame, for repeats
        private int horizontalHeld;
        private float verticalRepeatAt;            // Time.unscaledTime of the next repeat
        private float horizontalRepeatAt;
        private float previousTimeScale = 1f;
        private bool inputWasEnabled = true;       // the controller's InputEnabled before the pause, restored on resume
        private bool cursorWasLocked = true;       // whether the cursor was locked before the pause, restored on resume
#if UNITY_WEBGL && !UNITY_EDITOR
        private int lockedFrames;                  // frames in a row the browser has held the pointer lock
#endif
        private GUIStyle titleStyle;
        private GUIStyle labelStyle;
        private GUIStyle valueStyle;
        private GUIStyle hintStyle;
        private GUIStyle keyStyle;
        private GUIStyle sliderStyle;      // invisible: the slider's track and knob are drawn separately
        private GUIStyle sliderThumbStyle;
        private Vector2 lastMousePosition; // a moving mouse selects the item under it

        private void Awake()
        {
            controller = GetComponent<FirstPersonController>();
            preferences = GetComponent<PlayerPreferences>();
        }

        private void OnDisable()
        {
            if (IsPaused)
            {
                Resume();   // never leave the game frozen because this component went away
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && pauseOnFocusLoss && !Application.isEditor && !IsPaused)
            {
                Pause();
            }
        }

        private void Update()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // In a browser, Escape frees the pointer and never reaches the game, so losing the lock is the pause.
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                lockedFrames++;
            }
            else
            {
                bool lost = lockedFrames >= LockSettleFrames;
                lockedFrames = 0;
                if (lost && !IsPaused && controller.InputEnabled)
                {
                    Pause();
                    cursorWasLocked = true;   // resuming locks it again
                    return;
                }
            }
#endif

            // A click that binds a mouse button must not also press the menu button under the cursor: IMGUI buttons fire
            // on release. So from the start of a rebind until the mouse buttons have been up for a frame, OnGUI swallows
            // mouse events.
            if (rebinder.IsRebinding)
            {
                mouseBlocked = true;
            }
            if (mouseBlocked)
            {
                Mouse mouse = Mouse.current;
                bool held = mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed);
                if (held || rebinder.IsRebinding)
                {
                    mouseBusyFrame = Time.frameCount;
                }
                else if (Time.frameCount > mouseBusyFrame + 1)
                {
                    mouseBlocked = false;
                }
            }

            // Read the devices directly, like the controller's cursor handling: the actions asset has no pause or menu
            // actions, and input keeps flowing while the time scale is zero.
            if (rebinder.IsRebinding || Time.frameCount == rebinder.LastFinishedFrame)
            {
                return;   // the press that binds, or cancels, is not a menu press
            }

            Keyboard keyboard = Keyboard.current;
            Gamepad gamepad = Gamepad.current;
            bool toggle = (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                          || (gamepad != null && gamepad.startButton.wasPressedThisFrame);
            if (toggle)
            {
                TogglePause();
                return;
            }
            if (!IsPaused)
            {
                return;
            }

            // Gamepad "back" is B, which the default actions also bind to Crouch. The menu acts on the release of a
            // press made while paused, so the press that closes the menu never reaches the controller as a crouch.
            if (gamepad != null)
            {
                if (gamepad.buttonEast.wasPressedThisFrame)
                {
                    backPressed = true;
                    usingGamepad = true;
                }
                else if (backPressed && gamepad.buttonEast.wasReleasedThisFrame)
                {
                    backPressed = false;
                    Back();
                    return;
                }
            }

            Navigate(keyboard, gamepad);
        }

        private void Navigate(Keyboard keyboard, Gamepad gamepad)
        {
            int vertical = 0;
            int horizontal = 0;
            bool activate = false;

            if (keyboard != null)
            {
                bool up = keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed;
                bool down = keyboard.downArrowKey.isPressed || keyboard.sKey.isPressed;
                bool left = keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed;
                bool right = keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed;
                vertical = (down ? 1 : 0) - (up ? 1 : 0);
                horizontal = (right ? 1 : 0) - (left ? 1 : 0);
                activate = keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame;
                if (vertical != 0 || horizontal != 0 || activate)
                {
                    usingGamepad = false;
                }
            }

            if (gamepad != null)
            {
                Vector2 stick = gamepad.leftStick.ReadValue();
                bool up = gamepad.dpad.up.isPressed || stick.y > StickThreshold;
                bool down = gamepad.dpad.down.isPressed || stick.y < -StickThreshold;
                bool left = gamepad.dpad.left.isPressed || stick.x < -StickThreshold;
                bool right = gamepad.dpad.right.isPressed || stick.x > StickThreshold;
                int padVertical = (down ? 1 : 0) - (up ? 1 : 0);
                int padHorizontal = (right ? 1 : 0) - (left ? 1 : 0);
                bool padActivate = gamepad.buttonSouth.wasPressedThisFrame;
                if (padVertical != 0 || padHorizontal != 0 || padActivate)
                {
                    usingGamepad = true;
                    vertical = padVertical;
                    horizontal = padHorizontal;
                    activate |= padActivate;
                }
            }

            if (!navigationArmed)
            {
                // A key still held from the game (W while walking into the pause) is not a menu press: it has to be
                // released and pressed again, so it never repeats either.
                navigationArmed = true;
                verticalHeld = vertical;
                horizontalHeld = horizontal;
                verticalRepeatAt = float.PositiveInfinity;
                horizontalRepeatAt = float.PositiveInfinity;
                return;
            }

            if (items.Count == 0)
            {
                return;
            }
            if (Repeat(vertical, ref verticalHeld, ref verticalRepeatAt, out _))
            {
                selected = (selected + vertical + items.Count) % items.Count;
            }
            if (Repeat(horizontal, ref horizontalHeld, ref horizontalRepeatAt, out bool firstPress))
            {
                Item item = items[selected];
                if (item.AdjustRepeats || firstPress)
                {
                    item.Adjust?.Invoke(horizontal);
                }
            }
            if (activate)
            {
                items[selected].Activate?.Invoke();
            }
        }

        // A direction fires when it starts and then repeats while it is held. Unscaled time: the game is paused.
        private static bool Repeat(int direction, ref int held, ref float repeatAt, out bool firstPress)
        {
            bool fire = false;
            firstPress = false;
            if (direction != 0)
            {
                if (direction != held)
                {
                    fire = true;
                    firstPress = true;
                    repeatAt = Time.unscaledTime + RepeatDelay;
                }
                else if (Time.unscaledTime >= repeatAt)
                {
                    fire = true;
                    repeatAt = Time.unscaledTime + RepeatInterval;
                }
            }
            held = direction;
            return fire;
        }

        /// <summary>Pauses the game and shows the menu.</summary>
        public void Pause()
        {
            if (IsPaused)
            {
                return;
            }

            IsPaused = true;
            backPressed = false;
            navigationArmed = false;
            previousTimeScale = Time.timeScale;
            inputWasEnabled = controller.InputEnabled;   // a cutscene may already have it off: resume restores, not sets
            cursorWasLocked = Cursor.lockState == CursorLockMode.Locked;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            controller.InputEnabled = false;
            controller.UnlockCursor();
            ShowPage(Page.Main);
            Paused?.Invoke();
        }

        /// <summary>Hides the menu and resumes the game. Preferences changed on the menu's pages are saved.</summary>
        public void Resume()
        {
            if (!IsPaused)
            {
                return;
            }

            ShowPage(Page.Main);
            IsPaused = false;
            backPressed = false;
            Time.timeScale = previousTimeScale;
            AudioListener.pause = false;
            controller.InputEnabled = inputWasEnabled;
            controller.SuppressPresses();   // the press that resumed (Space, A, B) is not also a jump or crouch
            if (cursorWasLocked)
            {
                controller.LockCursor();
            }
            Resumed?.Invoke();
        }

        public void TogglePause()
        {
            if (IsPaused)
            {
                Resume();
            }
            else
            {
                Pause();
            }
        }

        /// <summary>Leaves the current page for the main menu, or resumes from the main menu (gamepad B).</summary>
        public void Back()
        {
            if (page != Page.Main)
            {
                ShowPage(Page.Main);
            }
            else
            {
                Resume();
            }
        }

        private void ShowPage(Page next)
        {
            rebinder.Cancel();
            if (page != Page.Main && page != next && preferences != null)
            {
                preferences.Save();   // leaving Options or Controls keeps what was changed there
            }
            page = next;
            selected = 0;
            BuildItems();
        }

        private void BuildItems()
        {
            items.Clear();
            switch (page)
            {
                case Page.Main:
                    Button("Resume", Resume);
                    if (!string.IsNullOrEmpty(saveSlot))
                    {
                        Button("Save", () =>
                        {
                            SaveSystem.Save(saveSlot);
                            Resume();
                            Notify("Game saved.");
                        });
                        if (SaveSystem.HasSave(saveSlot))
                        {
                            Button("Load", () =>
                            {
                                bool loaded = SaveSystem.Load(saveSlot);
                                Resume();
                                Notify(loaded ? "Game loaded." : "That save is for another scene.");
                            });
                        }
                    }
                    if (preferences != null)
                    {
                        Button("Options", () => ShowPage(Page.Options));
                    }
                    Button("Controls", () =>
                    {
                        scheme = usingGamepad ? InputRebinder.GamepadScheme : InputRebinder.KeyboardScheme;
                        ShowPage(Page.Controls);
                    });
                    if (showQuitButton && Application.platform != RuntimePlatform.WebGLPlayer)   // a web page can't quit
                    {
                        Button("Quit", Quit);
                    }
                    break;

                case Page.Options:
                    Slider("Mouse sensitivity", () => preferences.MouseSensitivity, v => preferences.MouseSensitivity = v, 0.01f, 0.2f, "0.000");
                    Slider("Stick sensitivity", () => preferences.StickSensitivity, v => preferences.StickSensitivity = v, 50f, 400f, "0");
                    Slider("Field of view", () => preferences.FieldOfView, v => preferences.FieldOfView = v, 50f, 110f, "0");
                    Toggle("Invert vertical look", () => preferences.InvertY, v => preferences.InvertY = v);
                    Toggle("Reduce camera motion", () => preferences.ReduceCameraMotion, v => preferences.ReduceCameraMotion = v);
                    Toggle("Controller only", () => preferences.ControllerOnly, v => preferences.ControllerOnly = v);
                    Button("Reset to defaults", () => preferences.ResetToDefaults());
                    Button("Back", () => ShowPage(Page.Main));
                    break;

                case Page.Controls:
                    rebinder.Refresh(controller.Actions, rebindableActions, scheme);
                    items.Add(new Item
                    {
                        Kind = Kind.Binding,
                        Label = "Device",
                        Value = () => scheme == InputRebinder.KeyboardScheme ? "Keyboard and mouse" : "Gamepad",
                        Activate = SwitchScheme,
                        Adjust = direction => SwitchScheme(),
                    });
                    foreach (InputRebinder.Row row in rebinder.Rows)
                    {
                        InputRebinder.Row captured = row;
                        items.Add(new Item
                        {
                            Kind = Kind.Binding,
                            Label = captured.Label,
                            Value = () => rebinder.Rebinding == captured ? "..." : captured.Display,
                            Activate = () => rebinder.StartRebind(captured, scheme, null),
                        });
                    }
                    Button("Reset bindings", () =>
                    {
                        if (preferences != null)
                        {
                            preferences.ResetBindings();
                        }
                        else if (controller.Actions != null)
                        {
                            controller.Actions.RemoveAllBindingOverrides();
                        }
                        int keep = selected;
                        BuildItems();   // the rows show the bindings they were built with
                        selected = Mathf.Min(keep, items.Count - 1);
                    });
                    Button("Back", () => ShowPage(Page.Main));
                    break;
            }
        }

        private void SwitchScheme()
        {
            int keep = selected;
            scheme = scheme == InputRebinder.KeyboardScheme ? InputRebinder.GamepadScheme : InputRebinder.KeyboardScheme;
            BuildItems();
            selected = Mathf.Min(keep, items.Count - 1);
        }

        private void Button(string label, System.Action activate)
        {
            items.Add(new Item { Kind = Kind.Button, Label = label, Activate = activate });
        }

        private void Slider(string label, System.Func<float> get, System.Action<float> set, float min, float max, string format)
        {
            items.Add(new Item
            {
                Kind = Kind.Slider,
                Label = label,
                Get = get,
                Set = set,
                Min = min,
                Max = max,
                Value = () => get().ToString(format),
                Adjust = direction => set(Mathf.Clamp(get() + direction * (max - min) / SliderSteps, min, max)),
                AdjustRepeats = true,
            });
        }

        private void Toggle(string label, System.Func<bool> get, System.Action<bool> set)
        {
            items.Add(new Item
            {
                Kind = Kind.Toggle,
                Label = label,
                GetToggle = get,
                SetToggle = set,
                Activate = () => set(!get()),
                Adjust = direction => set(!get()),
            });
        }

        private void Notify(string message)
        {
            PlayerInteractor interactor = GetComponent<PlayerInteractor>();
            if (interactor != null)
            {
                interactor.Notify(message);
            }
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ Drawing

        // Sizes for a 1080-line screen, scaled with the real one.
        private const float Padding = 36f;
        private const float RowGap = 6f;
        private const float RowInset = 20f;

        private void OnGUI()
        {
            if (!IsPaused)
            {
                return;
            }
            // See Update; and with Controller Only on the mouse doesn't work the menu either (the hidden pointer's clicks
            // would land on whatever is under it).
            bool mouseAllowed = !mouseBlocked && !controller.ControllerOnly;
            if (!mouseAllowed && (Event.current.isMouse || Event.current.type == EventType.ScrollWheel))
            {
                Event.current.Use();
            }

            float scale = HudDrawing.Scale;
            EnsureStyles(scale);

            // The game, dimmed, then the panel.
            HudDrawing.Box(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.45f));

            float width = (page == Page.Controls ? 680f : page == Page.Options ? 600f : 480f) * scale;
            float padding = Padding * scale;
            float gap = RowGap * scale;
            float titleHeight = titleStyle.fontSize * 1.3f;
            float header = titleHeight + 22f * scale;
            float footer = hintStyle.fontSize * 2.6f + (page == Page.Controls ? hintStyle.fontSize * 1.8f : 0f);

            // Rows as tall as they like, or shorter to fit the screen.
            float rowHeight = (page == Page.Main ? 56f : 48f) * scale;
            float units = 0f;
            foreach (Item item in items)
            {
                units += item.Kind == Kind.Slider ? 1.6f : 1f;
            }
            float room = Screen.height * 0.92f - header - footer - padding * 2f - gap * items.Count;
            if (units > 0f && rowHeight * units > room)
            {
                rowHeight = Mathf.Max(30f * scale, room / units);
            }
            float listHeight = rowHeight * units + gap * items.Count;
            Rect panel = new Rect(Mathf.Round((Screen.width - width) * 0.5f), 0f, width,
                                  padding * 2f + header + listHeight + footer);
            panel.y = Mathf.Round((Screen.height - panel.height) * 0.5f);
            HudDrawing.Box(panel, panelColor, 18f * scale);
            HudDrawing.Outline(panel, new Color(1f, 1f, 1f, 0.06f), Mathf.Max(1f, scale), 18f * scale);

            float left = panel.x + padding;
            float inner = panel.width - padding * 2f;
            float top = panel.y + padding;
            string title = page == Page.Options ? "Options" : page == Page.Controls ? "Controls" : "Paused";
            HudDrawing.Text(new Rect(left, top, inner, titleHeight), title, titleStyle, textColor, shadow: false);
            HudDrawing.Box(new Rect(left, top + titleHeight + 6f * scale, 44f * scale, 4f * scale), accentColor, 2f * scale);
            top += header;

            Vector2 mouse = Event.current.mousePosition;
            bool mouseMoved = Event.current.type == EventType.Repaint && mouse != lastMousePosition;
            if (Event.current.type == EventType.Repaint)
            {
                lastMousePosition = mouse;
            }

            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                Rect row = new Rect(left, top, inner, item.Kind == Kind.Slider ? rowHeight * 1.6f : rowHeight);
                if (mouseAllowed && mouseMoved && !rebinder.IsRebinding && row.Contains(mouse))
                {
                    selected = i;
                }
                if (i == selected)
                {
                    HudDrawing.Box(row, highlightColor, 10f * scale);
                    HudDrawing.Box(new Rect(row.x, row.y + row.height * 0.2f, 4f * scale, row.height * 0.6f), accentColor, 2f * scale);
                }
                DrawItem(item, row, scale);
                top += row.height + gap;
            }

            float hintTop = panel.yMax - padding - footer + hintStyle.fontSize * 0.8f;
            if (page == Page.Controls)
            {
                string hint = rebinder.IsRebinding
                    ? "Press the new key or button. " + InputRebinder.CancelName(scheme) + " cancels."
                    : "Select a binding and press it to change it. A key another action uses swaps the two.";
                HudDrawing.Text(new Rect(left, hintTop, inner, hintStyle.fontSize * 1.6f), hint, hintStyle,
                                HudDrawing.Fade(textColor, rebinder.IsRebinding ? 1f : 0.6f), shadow: false);
                hintTop += hintStyle.fontSize * 1.8f;
            }
            DrawFooter(left, hintTop + hintStyle.fontSize * 0.8f, scale);
        }

        // The buttons that select and go back, for the device in use.
        private void DrawFooter(float left, float centerY, float scale)
        {
            bool gamepad = usingGamepad || controller.ControllerOnly;
            Color muted = HudDrawing.Fade(textColor, 0.6f);
            Color capFill = new Color(1f, 1f, 1f, 0.08f);
            Color capBorder = HudDrawing.Fade(textColor, 0.35f);
            float x = left;
            x += HudDrawing.KeyCap(x, centerY, gamepad ? "A" : "Enter", keyStyle, textColor, capFill, capBorder) + 8f * scale;
            x += DrawHintLabel(x, centerY, "Select", muted) + 24f * scale;
            x += HudDrawing.KeyCap(x, centerY, gamepad ? "B" : "Esc", keyStyle, textColor, capFill, capBorder) + 8f * scale;
            DrawHintLabel(x, centerY, page == Page.Main ? "Resume" : "Back", muted);
        }

        private float DrawHintLabel(float x, float centerY, string text, Color color)
        {
            float width = HudDrawing.Measure(hintStyle, text).x;
            float height = hintStyle.fontSize * 1.6f;
            HudDrawing.Text(new Rect(x, centerY - height * 0.5f, width + 4f, height), text, hintStyle, color, shadow: false);
            return width;
        }

        private void DrawItem(Item item, Rect rect, float scale)
        {
            float inset = RowInset * scale;
            Rect labelRect = new Rect(rect.x + inset, rect.y, rect.width * 0.6f, rect.height);
            switch (item.Kind)
            {
                case Kind.Button:
                    if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                    {
                        item.Activate?.Invoke();
                    }
                    HudDrawing.Text(labelRect, item.Label, labelStyle, textColor, shadow: false);
                    break;

                case Kind.Toggle:
                {
                    if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                    {
                        item.SetToggle(!item.GetToggle());
                    }
                    HudDrawing.Text(labelRect, item.Label, labelStyle, textColor, shadow: false);
                    bool on = item.GetToggle();
                    float trackHeight = Mathf.Round(Mathf.Min(26f * scale, rect.height * 0.6f));
                    float trackWidth = Mathf.Round(trackHeight * 1.9f);
                    Rect track = new Rect(rect.xMax - inset - trackWidth, rect.center.y - trackHeight * 0.5f, trackWidth, trackHeight);
                    HudDrawing.Box(track, on ? accentColor : new Color(1f, 1f, 1f, 0.18f), trackHeight * 0.5f);
                    float knob = trackHeight - 6f * scale;
                    float knobX = on ? track.xMax - trackHeight * 0.5f : track.x + trackHeight * 0.5f;
                    HudDrawing.Circle(new Vector2(knobX, track.center.y), knob, Color.white);
                    break;
                }

                case Kind.Slider:
                {
                    float labelHeight = rect.height * 0.55f;
                    HudDrawing.Text(new Rect(rect.x + inset, rect.y, rect.width * 0.6f, labelHeight), item.Label, labelStyle,
                                    textColor, shadow: false);
                    HudDrawing.Text(new Rect(rect.x + rect.width * 0.5f, rect.y, rect.width * 0.5f - inset, labelHeight),
                                    item.Value(), valueStyle, accentColor, shadow: false);
                    float trackHeight = Mathf.Max(3f, 6f * scale);
                    Rect track = new Rect(rect.x + inset, rect.y + labelHeight + (rect.height - labelHeight) * 0.35f,
                                          rect.width - inset * 2f, trackHeight);
                    float value = item.Get();
                    float fraction = Mathf.InverseLerp(item.Min, item.Max, value);
                    HudDrawing.Bar(track, fraction, accentColor, new Color(1f, 1f, 1f, 0.15f));
                    HudDrawing.Circle(new Vector2(track.x + track.width * fraction, track.center.y), 18f * scale, Color.white);

                    // The mouse drags an invisible slider laid over the drawn one.
                    float grab = 24f * scale;
                    sliderThumbStyle.fixedWidth = grab;
                    sliderThumbStyle.fixedHeight = grab;
                    Rect hit = new Rect(track.x - grab * 0.5f, track.center.y - grab * 0.5f, track.width + grab, grab);
                    float result = GUI.HorizontalSlider(hit, value, item.Min, item.Max, sliderStyle, sliderThumbStyle);
                    if (!Mathf.Approximately(result, value))
                    {
                        item.Set(result);
                    }
                    break;
                }

                case Kind.Binding:
                {
                    if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                    {
                        item.Activate?.Invoke();
                    }
                    HudDrawing.Text(labelRect, item.Label, labelStyle, textColor, shadow: false);
                    string value = item.Value();
                    if (value == "...")
                    {
                        float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 6f);
                        HudDrawing.Text(new Rect(rect.x + rect.width * 0.4f, rect.y, rect.width * 0.6f - inset, rect.height),
                                        "Press a key or button", valueStyle, HudDrawing.Fade(accentColor, pulse), shadow: false);
                    }
                    else
                    {
                        float capWidth = HudDrawing.KeyCapWidth(value, keyStyle);
                        HudDrawing.KeyCap(rect.xMax - inset - capWidth, rect.center.y, value, keyStyle, textColor,
                                          new Color(1f, 1f, 1f, 0.08f), HudDrawing.Fade(textColor, 0.35f));
                    }
                    break;
                }
            }
        }

        private void EnsureStyles(float scale)
        {
            // GUI.skin is only available inside OnGUI, and the sizes follow the screen.
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontStyle = FontStyle.Bold,
                    padding = new RectOffset(0, 0, 0, 0),
                    clipping = TextClipping.Overflow,
                };
                labelStyle = new GUIStyle(titleStyle) { fontStyle = FontStyle.Normal };
                valueStyle = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleRight };
                hintStyle = new GUIStyle(labelStyle) { wordWrap = true };
                keyStyle = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                sliderStyle = new GUIStyle();
                sliderThumbStyle = new GUIStyle();
            }

            int size = Mathf.RoundToInt(fontSize * scale);
            titleStyle.fontSize = Mathf.RoundToInt(size * 1.5f);
            labelStyle.fontSize = size;
            valueStyle.fontSize = size;
            hintStyle.fontSize = Mathf.RoundToInt(size * 0.7f);
            keyStyle.fontSize = Mathf.RoundToInt(size * 0.7f);
        }
    }
}
