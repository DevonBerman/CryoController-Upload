using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace CryoController
{
    // FirstPersonController: resolving the input actions from the asset in use, enabling them, controller-only play, and
    // managing the cursor. The settings, state and frame loop are in FirstPersonController.cs.
    public partial class FirstPersonController
    {
        // Action paths (Map/Action) in the actions asset: the shipped Input/First Person Actions, and Unity's project
        // templates' project-wide asset, both have them.
        private const string MoveActionPath = "Player/Move";
        private const string LookActionPath = "Player/Look";
        private const string JumpActionPath = "Player/Jump";
        private const string SprintActionPath = "Player/Sprint";
        private const string CrouchActionPath = "Player/Crouch";
        private const string ZoomActionPath = "Player/Zoom";
        private const string LeanLeftActionPath = "Player/LeanLeft";
        private const string LeanRightActionPath = "Player/LeanRight";

        // ------------------------------------------------------------------ Input

        private bool ResolveActions()
        {
            InputActionAsset asset = actionsOverride != null ? actionsOverride : InputSystem.actions;
            if (asset == null)
            {
                Debug.LogError("FirstPersonController: no input actions available. Set a project-wide actions asset " +
                               "(Edit > Project Settings > Input System Package) or assign Actions Override. Disabling.", this);
                return false;
            }

            // A private copy, whichever asset it is: enabling and rebinding happen on the copy, never on the asset in
            // the project (where they would outlive Play Mode in the Editor) nor on the project-wide asset that every
            // other script shares. The Input System keeps the original project-wide asset enabled for those scripts.
            runtimeActions = Instantiate(asset);
            runtimeActions.name = asset.name;
            asset = runtimeActions;
            Actions = asset;

            moveAction = ResolveAction(asset, MoveActionPath, required: true);
            lookAction = ResolveAction(asset, LookActionPath, required: true);
            jumpAction = ResolveAction(asset, JumpActionPath, required: false);
            sprintAction = ResolveAction(asset, SprintActionPath, required: false);
            crouchAction = ResolveAction(asset, CrouchActionPath, required: false);
            // Zoom and lean are looked up whatever their settings, as a movement profile can switch them on later; a
            // missing one is only reported while its feature is on.
            zoomAction = ResolveAction(asset, ZoomActionPath, required: false, report: zoomFovScale < 1f);
            leanLeftAction = ResolveAction(asset, LeanLeftActionPath, required: false, report: leanDistance > 0f);
            leanRightAction = ResolveAction(asset, LeanRightActionPath, required: false, report: leanDistance > 0f);

            return moveAction != null && lookAction != null;
        }

        private InputAction ResolveAction(InputActionAsset asset, string path, bool required, bool report = true)
        {
            InputAction action = asset.FindAction(path, throwIfNotFound: false);
            if (action == null && report)
            {
                if (required)
                {
                    Debug.LogError($"FirstPersonController: required action '{path}' not found in '{asset.name}'. Disabling.", this);
                }
                else
                {
                    Debug.LogWarning($"FirstPersonController: optional action '{path}' not found in '{asset.name}'; that feature is off.", this);
                }
            }
            return action;
        }

        private void SetOwnedActionsEnabled(bool enable)
        {
            SetActionEnabled(moveAction, enable);
            SetActionEnabled(lookAction, enable);
            SetActionEnabled(jumpAction, enable);
            SetActionEnabled(sprintAction, enable);
            SetActionEnabled(crouchAction, enable);
            SetActionEnabled(zoomAction, enable);
            SetActionEnabled(leanLeftAction, enable);
            SetActionEnabled(leanRightAction, enable);
        }

        private static void SetActionEnabled(InputAction action, bool enable)
        {
            if (action == null)
            {
                return;
            }

            if (enable)
            {
                action.Enable();
            }
            else
            {
                action.Disable();
            }
        }

        // ------------------------------------------------------------------ On-screen controls

        /// <summary>
        /// True while on-screen controls such as <see cref="TouchControls"/> drive the player. The touchscreen itself is
        /// then left out of the actions, so a finger on a control isn't also read as a look drag or a tap, and prompts
        /// show no button. Set by the on-screen controls.
        /// </summary>
        public bool OnScreenControls
        {
            get => onScreenControls;
            set
            {
                if (onScreenControls == value)
                {
                    return;
                }
                onScreenControls = value;
                FilterPointers();
            }
        }

        /// <summary>
        /// Turns the view by these angles in degrees (x right, y up), as a look input that isn't an action: a touch
        /// drag, a script. Ignored while input is off; scaled down while zoomed, like the other look input.
        /// </summary>
        public void AddLook(Vector2 degrees)
        {
            if (!ControlsActive || cameraTransform == null)
            {
                return;
            }
            degrees *= ZoomFactor;
            yaw = Mathf.Repeat(yaw + degrees.x, 360f);
            pitch = Mathf.Clamp(pitch - degrees.y, -maxPitch, maxPitch);
            ApplyRotation();
        }

        // ------------------------------------------------------------------ Controller only

        // ControllerOnly switched at runtime: the actions, and the pointer if this component manages the cursor.
        private void ApplyControllerOnly()
        {
            FilterPointers();
            if (manageCursor)
            {
                // Off again: shown only while free (a menu is up), as LockCursor and UnlockCursor leave it.
                Cursor.visible = !controllerOnly && Cursor.lockState != CursorLockMode.Locked;
            }
        }

        // Leaves pointers (mouse, pen, touch) out of the actions while ControllerOnly is on, so nothing that reads them
        // sees a pointer; and the touchscreen while on-screen controls stand in for it.
        private void FilterPointers()
        {
            if (Actions == null)
            {
                return;
            }
            if (!controllerOnly && !onScreenControls)
            {
                Actions.devices = null;   // every device
                return;
            }
            var devices = new List<InputDevice>();
            foreach (InputDevice device in InputSystem.devices)
            {
                if (controllerOnly ? !(device is Pointer) : !(device is Touchscreen))
                {
                    devices.Add(device);
                }
            }
            Actions.devices = new ReadOnlyArray<InputDevice>(devices.ToArray());
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            // A gamepad plugged in mid-game has to join the list.
            if ((controllerOnly || onScreenControls) && (change == InputDeviceChange.Added || change == InputDeviceChange.Removed))
            {
                FilterPointers();
            }
        }

        // ------------------------------------------------------------------ Cursor

        private void UpdateCursor()
        {
            // With input paused a menu or dialogue owns the cursor: neither Escape nor a click on it changes the lock.
            if (!manageCursor || !InputEnabled)
            {
                return;
            }

            if (controllerOnly)
            {
                // No pointer to free or catch: it just stays hidden, whatever the platform did with the lock.
                Cursor.visible = false;
                return;
            }

            // Releasing and re-locking the cursor is window management rather than gameplay, so the keys are read
            // from the devices directly instead of through the actions asset (which has no pause / menu action yet).
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                // The Editor releases the cursor on Escape by itself; setting the state here keeps lockState truthful.
                // The press that just re-locked the cursor (a pause menu closing on Escape) is not also a release: the
                // lock sets the look suppression, which covers that frame.
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && lookSuppressFrames == 0)
                {
                    UnlockCursor();
                }
            }
            else
            {
                // Only a mouse click re-locks: the cursor is a mouse concept, and the Editor refuses to lock it again
                // until the Game view is clicked anyway, so a keyboard or gamepad press would only pretend to.
                Mouse mouse = Mouse.current;
                if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                {
                    LockCursor();
                }
            }
        }

        /// <summary>Locks and hides the cursor so mouse look works.</summary>
        public void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            cursorLockedByUs = true;
            lookSuppressFrames = LookSuppressFrameCount;
            mouseHistoryCount = 0;   // the smoothing window starts fresh after a lock
        }

        /// <summary>
        /// Releases and shows the cursor; mouse look pauses until it is locked again. With <see cref="ControllerOnly"/> on
        /// the cursor stays hidden.
        /// </summary>
        public void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = !controllerOnly;
            cursorLockedByUs = false;
        }
    }
}
