using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CryoController
{
    /// <summary>
    /// Lists the bindings of an actions asset that a player may change, for one control scheme, and rebinds one of
    /// them interactively: the next key or button pressed on that scheme's devices becomes the binding. The overrides
    /// live in the asset; <see cref="PlayerPreferences"/> saves and restores them. <see cref="PauseMenu"/>'s Controls
    /// page is built on this; a custom menu can use it the same way.
    /// </summary>
    public class InputRebinder
    {
        /// <summary>One binding a player can change.</summary>
        public class Row
        {
            public string Label;          // "Jump", "Move Up"
            public InputAction Action;
            public int BindingIndex;

            /// <summary>The binding's current control, for display: "Space", "Button South". Updated after a rebind.</summary>
            public string Display { get; private set; }

            public void RefreshDisplay()
            {
                Display = Readable(Action.GetBindingDisplayString(BindingIndex, InputBinding.DisplayStringOptions.DontIncludeInteractions),
                                   Action.bindings[BindingIndex].effectivePath);
            }
        }

        /// <summary>Control scheme names as in the shipped actions asset (and Unity's project templates).</summary>
        public const string KeyboardScheme = "Keyboard&Mouse";
        public const string GamepadScheme = "Gamepad";

        /// <summary>Seconds a rebind waits for a press before it gives up.</summary>
        public const float Timeout = 6f;

        /// <summary>The bindings listed by the last <see cref="Refresh"/>.</summary>
        public List<Row> Rows { get; } = new List<Row>();

        /// <summary>The row being rebound right now, or null.</summary>
        public Row Rebinding { get; private set; }

        public bool IsRebinding => operation != null;

        /// <summary>
        /// Time.frameCount of the frame the last rebind finished. The press that finished it is still "pressed this
        /// frame", so a menu ignores its input on this frame.
        /// </summary>
        public int LastFinishedFrame { get; private set; } = -1;

        private InputActionRebindingExtensions.RebindingOperation operation;
        private bool wasEnabled;
        private string previousPath;   // the rebinding row's control before the rebind, for a swap

        /// <summary>
        /// Fills <see cref="Rows"/> with the bindings of the named actions ("Jump" or "Player/Jump") in one control
        /// scheme: one row per button binding, and one per part of a composite (Move Up, Move Down, ...). Bindings of
        /// sticks and pointers are left out; they are not sensibly rebound by pressing something.
        /// </summary>
        public void Refresh(InputActionAsset asset, IEnumerable<string> actionNames, string scheme)
        {
            Rows.Clear();
            if (asset == null || actionNames == null)
            {
                return;
            }

            foreach (string actionName in actionNames)
            {
                InputAction action = asset.FindAction(actionName, throwIfNotFound: false);
                if (action == null)
                {
                    continue;
                }

                var parts = new HashSet<string>();
                bool listed = false;
                var bindings = action.bindings;
                for (int i = 0; i < bindings.Count; i++)
                {
                    InputBinding binding = bindings[i];
                    if (binding.isComposite || !InScheme(binding, scheme))
                    {
                        continue;
                    }

                    if (binding.isPartOfComposite)
                    {
                        // One row per part: the first binding of "up" (W), not also the second (Up Arrow).
                        string part = (binding.name ?? "").ToLowerInvariant();
                        if (parts.Add(part))
                        {
                            Add(Nicify(action.name) + " " + Capitalize(part), action, i);
                        }
                    }
                    else if (!listed && action.type == InputActionType.Button)
                    {
                        listed = true;
                        Add(Nicify(action.name), action, i);
                    }
                }
            }
        }

        private void Add(string label, InputAction action, int bindingIndex)
        {
            Row row = new Row { Label = label, Action = action, BindingIndex = bindingIndex };
            row.RefreshDisplay();
            Rows.Add(row);
        }

        /// <summary>
        /// The action's binding in a control scheme, for a button prompt: "F", "LMB", "Y". Falls back to all its bindings
        /// when none is in the scheme; empty for a null action.
        /// </summary>
        public static string BindingDisplay(InputAction action, string scheme)
        {
            if (action == null)
            {
                return "";
            }
            const InputBinding.DisplayStringOptions options = InputBinding.DisplayStringOptions.DontIncludeInteractions;
            string text = action.GetBindingDisplayString(InputBinding.MaskByGroup(scheme), options);
            if (string.IsNullOrEmpty(text))
            {
                return Readable(action.GetBindingDisplayString(options), null);
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                // The key's own name is blank (Space in some layouts): name it after its control instead.
                foreach (InputBinding binding in action.bindings)
                {
                    if (!binding.isComposite && InScheme(binding, scheme))
                    {
                        return Readable(text, binding.effectivePath);
                    }
                }
            }
            return text;
        }

        /// <summary>
        /// A binding's display string, or the control's name when the string is blank: some keyboard layouts name the
        /// Space key " ".
        /// </summary>
        public static string Readable(string display, string path)
        {
            if (!string.IsNullOrWhiteSpace(display) || string.IsNullOrEmpty(path))
            {
                return display;
            }
            return InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
        }

        /// <summary>"LeanLeft" as "Lean Left".</summary>
        public static string Nicify(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }
            var text = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && char.IsLower(name[i - 1]))
                {
                    text.Append(' ');
                }
                text.Append(name[i]);
            }
            return text.ToString();
        }

        /// <summary>The control that cancels a rebind on the scheme's devices, for a hint: "Escape" or "Start".</summary>
        public static string CancelName(string scheme)
        {
            return scheme == GamepadScheme ? "Start" : "Escape";
        }

        /// <summary>
        /// Starts listening for the control to bind the row to. Only the scheme's devices count: keyboard and mouse,
        /// or gamepad. Escape (keyboard) or Start (gamepad) cancels, so does <see cref="Timeout"/>. A control another
        /// row already uses is swapped: that row takes this row's old control, so one press never does two things. The
        /// action is disabled while listening and enabled again afterwards if it was enabled before. Finished is called
        /// on completion and on cancel.
        /// </summary>
        public void StartRebind(Row row, string scheme, Action finished)
        {
            Cancel();
            Rebinding = row;
            previousPath = row.Action.bindings[row.BindingIndex].effectivePath;
            wasEnabled = row.Action.enabled;
            row.Action.Disable();   // rebinding needs the action off

            operation = row.Action.PerformInteractiveRebinding(row.BindingIndex)
                .WithControlsExcluding("<Pointer>/position")
                .WithControlsExcluding("<Pointer>/delta")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithControlsExcluding("<Mouse>/scroll/*")
                .WithControlsExcluding("<Gamepad>/start")
                .WithControlsExcluding("<Keyboard>/escape")
                .WithTimeout(Timeout)
                .OnMatchWaitForAnother(0.05f)
                .OnComplete(op =>
                {
                    SwapDuplicates(row, op.selectedControl);
                    Finish(finished);
                })
                .OnCancel(op => Finish(finished));

            // The cancel control is on the scheme's own devices, so a gamepad-only player is never stuck waiting.
            if (scheme == GamepadScheme)
            {
                operation.WithControlsHavingToMatchPath("<Gamepad>").WithCancelingThrough("<Gamepad>/start");
            }
            else
            {
                operation.WithControlsHavingToMatchPath("<Keyboard>").WithControlsHavingToMatchPath("<Mouse>")
                         .WithCancelingThrough("<Keyboard>/escape");
            }

            operation.Start();
        }

        /// <summary>Stops a rebind in progress, leaving the binding as it was.</summary>
        public void Cancel()
        {
            if (operation != null)
            {
                operation.Cancel();   // calls OnCancel, which cleans up
            }
        }

        // Every other row bound to the control the row was just rebound to takes the row's old control instead.
        private void SwapDuplicates(Row row, InputControl control)
        {
            if (control == null || string.IsNullOrEmpty(previousPath))
            {
                return;
            }
            foreach (Row other in Rows)
            {
                if (other == row)
                {
                    continue;
                }
                string otherPath = other.Action.bindings[other.BindingIndex].effectivePath;
                if (!string.IsNullOrEmpty(otherPath) && InputControlPath.Matches(otherPath, control))
                {
                    other.Action.ApplyBindingOverride(other.BindingIndex, previousPath);
                    other.RefreshDisplay();
                }
            }
        }

        private void Finish(Action finished)
        {
            if (operation != null)
            {
                operation.Dispose();
                operation = null;
            }
            if (Rebinding != null)
            {
                Rebinding.RefreshDisplay();
                if (wasEnabled)
                {
                    Rebinding.Action.Enable();
                }
            }
            Rebinding = null;
            LastFinishedFrame = Time.frameCount;
            finished?.Invoke();
        }

        /// <summary>Whether the binding belongs to the control scheme (its groups list the scheme).</summary>
        public static bool InScheme(InputBinding binding, string scheme)
        {
            if (string.IsNullOrEmpty(binding.groups))
            {
                return false;
            }
            foreach (string group in binding.groups.Split(';'))
            {
                if (group == scheme)
                {
                    return true;
                }
            }
            return false;
        }

        private static string Capitalize(string text)
        {
            return string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }
    }
}
