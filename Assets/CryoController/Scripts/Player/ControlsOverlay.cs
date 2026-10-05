using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CryoController
{
    /// <summary>
    /// A panel of the controls in the bottom-left corner: each action with its keys as key caps, for keyboard and mouse
    /// or for a gamepad, whichever is in use, following any rebinding. A key lights up while its action is held, so the
    /// panel also shows what is being pressed in a recording. It shows for a while when the scene starts, and the Toggle
    /// Key or the gamepad's View button shows and hides it. Hidden under menus and while on-screen controls show.
    /// </summary>
    [RequireComponent(typeof(FirstPersonController))]
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Controls Overlay")]
    public class ControlsOverlay : MonoBehaviour
    {
        /// <summary>One line of the panel: what it does, and the keys or buttons that do it.</summary>
        public class Row
        {
            public string Label;
            public readonly List<string> Keys = new List<string>();
            internal InputAction Action;   // what lights the keys up
            internal InputAction Second;   // what lights the second key up, when it is another action (Lean)
            internal int Part = -1;        // for Move's keys: up, left, down, right; -1 for the whole action
        }

        // Sizes for a 1080-line screen, scaled with the real one.
        private const float Margin = 20f;
        private const float Padding = 16f;
        private const float RowHeight = 34f;
        private const float FadeTime = 0.25f;

        [Tooltip("Key that shows and hides the panel. The gamepad's View (Select) button does too.")]
        [SerializeField] private Key toggleKey = Key.F1;
        [Tooltip("Seconds the panel shows for when the scene starts. 0 starts it hidden.")]
        [SerializeField, Min(0f)] private float showAtStart = 12f;
        [Tooltip("Size of the text, in pixels on a 1080-line screen; it scales with the screen height.")]
        [SerializeField, Min(8)] private int fontSize = 18;
        [Tooltip("Colour of a key while its action is held.")]
        [SerializeField] private Color accentColor = new Color(1f, 0.6f, 0.2f, 1f);

        /// <summary>True while the panel is meant to show (it still hides under menus and on-screen controls).</summary>
        public bool IsVisible { get; private set; }

        /// <summary>The rows for the device in use, for a panel of your own. Rebuilt when the bindings or the device change.</summary>
        public IReadOnlyList<Row> Rows
        {
            get
            {
                Refresh();
                return rows;
            }
        }

        private FirstPersonController controller;
        private PlayerInteractor interactor;
        private readonly List<Row> rows = new List<Row>();
        private bool rowsForGamepad;
        private bool rowsStale = true;
        private float hideAt = float.PositiveInfinity;   // Time.unscaledTime the start-up showing ends
        private float fade;                               // 0..1, how far the panel has faded in
        private GUIStyle labelStyle;
        private GUIStyle keyStyle;
        private GUIStyle titleStyle;
        private string title;

        private void Awake()
        {
            controller = GetComponent<FirstPersonController>();
            interactor = GetComponent<PlayerInteractor>();
        }

        private void OnEnable()
        {
            InputSystem.onActionChange += OnActionChange;
            if (showAtStart > 0f)
            {
                IsVisible = true;
                hideAt = Time.unscaledTime + showAtStart;
            }
        }

        private void OnDisable()
        {
            InputSystem.onActionChange -= OnActionChange;
        }

        private void OnActionChange(object target, InputActionChange change)
        {
            if (change == InputActionChange.BoundControlsChanged)
            {
                rowsStale = true;   // rebound, or devices changed
            }
        }

        /// <summary>Shows the panel until hidden.</summary>
        public void Show()
        {
            IsVisible = true;
            hideAt = float.PositiveInfinity;
        }

        /// <summary>Hides the panel.</summary>
        public void Hide()
        {
            IsVisible = false;
        }

        /// <summary>Shows the panel if hidden, hides it if showing.</summary>
        public void Toggle()
        {
            if (IsVisible)
            {
                Hide();
            }
            else
            {
                Show();
            }
        }

        private void Update()
        {
            if (Time.unscaledTime >= hideAt)
            {
                hideAt = float.PositiveInfinity;
                IsVisible = false;
            }
            // Read from the devices like Escape for the pause menu: showing help isn't a gameplay action.
            if (controller.InputEnabled)
            {
                Keyboard keyboard = Keyboard.current;
                Gamepad gamepad = Gamepad.current;
                if ((keyboard != null && toggleKey != Key.None && keyboard[toggleKey].wasPressedThisFrame)
                    || (gamepad != null && gamepad.selectButton.wasPressedThisFrame))
                {
                    Toggle();
                }
            }
            fade = Mathf.MoveTowards(fade, Showing ? 1f : 0f, Time.unscaledDeltaTime / FadeTime);
        }

        private bool Showing => IsVisible && controller.InputEnabled && !controller.OnScreenControls
                                && (interactor == null || !interactor.Busy);

        // ------------------------------------------------------------------ Rows

        private void Refresh()
        {
            bool gamepad = controller.UsingGamepad || controller.ControllerOnly;
            if (!rowsStale && gamepad == rowsForGamepad)
            {
                return;
            }
            rowsStale = false;
            rowsForGamepad = gamepad;
            rows.Clear();
            InputActionAsset actions = controller.Actions;
            if (actions == null)
            {
                return;
            }
            string scheme = gamepad ? InputRebinder.GamepadScheme : InputRebinder.KeyboardScheme;
            title = "Controls";

            InputAction move = actions.FindAction("Player/Move", throwIfNotFound: false);
            if (move != null)
            {
                if (gamepad)
                {
                    AddRow("Move", move, "Left stick");
                }
                else
                {
                    AddMoveRow(move, scheme);
                }
            }
            InputAction look = actions.FindAction("Player/Look", throwIfNotFound: false);
            if (look != null)
            {
                AddRow("Look", look, gamepad ? "Right stick" : "Mouse");
            }
            AddActionRow(actions, "Jump", "Jump", scheme);
            AddActionRow(actions, "Sprint", "Sprint", scheme);
            AddActionRow(actions, "Crouch", "Crouch, slide", scheme);
            AddActionRow(actions, "Interact", "Use", scheme);
            AddActionRow(actions, "Throw", "Throw", scheme);
            AddActionRow(actions, "Inspect", "Inspect", scheme);
            AddActionRow(actions, "Zoom", "Zoom", scheme);
            if (GetComponentInChildren<Flashlight>(true) != null)
            {
                AddActionRow(actions, "Flashlight", "Flashlight", scheme);
            }
            InputAction leanLeft = actions.FindAction("Player/LeanLeft", throwIfNotFound: false);
            InputAction leanRight = actions.FindAction("Player/LeanRight", throwIfNotFound: false);
            if (leanLeft != null && leanRight != null)
            {
                Row lean = AddRow("Lean", leanLeft, InputRebinder.BindingDisplay(leanLeft, scheme));
                lean.Keys.Add(InputRebinder.BindingDisplay(leanRight, scheme));
                lean.Second = leanRight;
            }
            if (GetComponent<PauseMenu>() != null)
            {
                AddRow("Pause", null, gamepad ? "Start" : "Esc");
            }
            AddRow("Show or hide this", null, gamepad ? "View" : toggleKey.ToString());
        }

        private void AddActionRow(InputActionAsset actions, string name, string label, string scheme)
        {
            InputAction action = actions.FindAction("Player/" + name, throwIfNotFound: false);
            if (action != null)
            {
                string key = InputRebinder.BindingDisplay(action, scheme);
                if (!string.IsNullOrEmpty(key))
                {
                    AddRow(label, action, key);
                }
            }
        }

        private Row AddRow(string label, InputAction action, string key)
        {
            var row = new Row { Label = label, Action = action };
            row.Keys.Add(key);
            rows.Add(row);
            return row;
        }

        // Keyboard Move: one key cap per direction (the first binding of each part of the composite), as W A S D.
        private void AddMoveRow(InputAction move, string scheme)
        {
            string[] parts = { "up", "left", "down", "right" };
            var keys = new string[4];
            var bindings = move.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                InputBinding binding = bindings[i];
                if (!binding.isPartOfComposite || !InputRebinder.InScheme(binding, scheme))
                {
                    continue;
                }
                int part = System.Array.IndexOf(parts, (binding.name ?? "").ToLowerInvariant());
                if (part >= 0 && keys[part] == null)
                {
                    keys[part] = InputRebinder.Readable(
                        move.GetBindingDisplayString(i, InputBinding.DisplayStringOptions.DontIncludeInteractions),
                        binding.effectivePath);
                }
            }
            for (int part = 0; part < 4; part++)
            {
                if (keys[part] == null)
                {
                    continue;
                }
                if (rows.Count == 0 || rows[rows.Count - 1].Label != "Move")
                {
                    rows.Add(new Row { Label = "Move" });
                }
                rows[rows.Count - 1].Keys.Add(keys[part]);
            }
            if (rows.Count > 0 && rows[rows.Count - 1].Label == "Move")
            {
                rows[rows.Count - 1].Action = move;
            }
        }

        // Whether a row's key is held: a button action's press, the stick or keys pushed that way for Move, any movement
        // for Look, and each lean direction on its own.
        private bool Held(Row row, int key)
        {
            InputAction action = row.Action;
            if (action == null || !controller.InputEnabled)
            {
                return false;
            }
            if (row.Label == "Move" && row.Keys.Count == 4)
            {
                Vector2 value = action.ReadValue<Vector2>();
                switch (key)
                {
                    case 0: return value.y > 0.3f;
                    case 1: return value.x < -0.3f;
                    case 2: return value.y < -0.3f;
                    default: return value.x > 0.3f;
                }
            }
            if (key == 1 && row.Second != null)
            {
                return row.Second.IsPressed();
            }
            return action.type == InputActionType.Value ? action.IsInProgress() : action.IsPressed();
        }

        // ------------------------------------------------------------------ Drawing

        private void OnGUI()
        {
            if (fade <= 0f || Event.current.type != EventType.Repaint)
            {
                return;
            }
            Refresh();
            if (rows.Count == 0)
            {
                return;
            }

            float scale = HudDrawing.Scale;
            EnsureStyles(scale);
            float padding = Padding * scale;
            float rowHeight = RowHeight * scale;
            float labelWidth = 0f;
            foreach (Row row in rows)
            {
                labelWidth = Mathf.Max(labelWidth, HudDrawing.Measure(labelStyle, row.Label).x);
            }
            float keysWidth = 0f;
            foreach (Row row in rows)
            {
                float width = 0f;
                foreach (string key in row.Keys)
                {
                    width += HudDrawing.KeyCapWidth(key, keyStyle) + 6f * scale;
                }
                keysWidth = Mathf.Max(keysWidth, width);
            }
            float titleHeight = titleStyle.fontSize * 1.8f;
            float panelWidth = padding * 2f + labelWidth + 18f * scale + keysWidth;
            float height = padding * 2f + titleHeight + rowHeight * rows.Count;
            float margin = Margin * scale;
            Rect panel = new Rect(margin, Screen.height - margin - height, panelWidth, height);
            HudDrawing.Box(panel, new Color(0f, 0f, 0f, 0.5f * fade), 12f * scale);

            Color text = new Color(1f, 1f, 1f, fade);
            HudDrawing.Text(new Rect(panel.x + padding, panel.y + padding, panel.width, titleHeight * 0.8f), title, titleStyle,
                            text, shadow: false);
            float top = panel.y + padding + titleHeight;
            float keysLeft = panel.x + padding + labelWidth + 18f * scale;
            foreach (Row row in rows)
            {
                HudDrawing.Text(new Rect(panel.x + padding, top, labelWidth + 4f, rowHeight), row.Label, labelStyle,
                                new Color(1f, 1f, 1f, 0.75f * fade), shadow: false);
                float x = keysLeft;
                for (int i = 0; i < row.Keys.Count; i++)
                {
                    bool held = Held(row, i);
                    Color fill = held ? HudDrawing.Fade(accentColor, 0.85f * fade) : new Color(1f, 1f, 1f, 0.1f * fade);
                    Color border = held ? HudDrawing.Fade(accentColor, fade) : new Color(1f, 1f, 1f, 0.45f * fade);
                    x += HudDrawing.KeyCap(x, top + rowHeight * 0.5f, row.Keys[i], keyStyle, text, fill, border) + 6f * scale;
                }
                top += rowHeight;
            }
        }

        private void EnsureStyles(float scale)
        {
            if (labelStyle == null)
            {
                // GUI.skin is only available inside OnGUI.
                labelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(0, 0, 0, 0),
                    clipping = TextClipping.Overflow,
                };
                keyStyle = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                titleStyle = new GUIStyle(labelStyle) { fontStyle = FontStyle.Bold };
            }
            int size = Mathf.RoundToInt(fontSize * scale);
            labelStyle.fontSize = size;
            keyStyle.fontSize = Mathf.RoundToInt(size * 0.85f);
            titleStyle.fontSize = Mathf.RoundToInt(size * 1.1f);
        }
    }
}
