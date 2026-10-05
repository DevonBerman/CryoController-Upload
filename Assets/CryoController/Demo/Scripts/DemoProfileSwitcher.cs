using UnityEngine;
using UnityEngine.InputSystem;

namespace CryoController.Demo
{
    /// <summary>
    /// Demo only: cycles the player through movement profiles with the Previous and Next actions (1 and 2, or the
    /// D-pad's left and right), and shows which one is in use. Swapping the feel is one line:
    /// <c>player.Profile = profile</c>.
    /// </summary>
    [AddComponentMenu("CryoController/Demo/Demo Profile Switcher")]
    public class DemoProfileSwitcher : MonoBehaviour
    {
        private const string PreviousActionPath = "Player/Previous";
        private const string NextActionPath = "Player/Next";

        [Tooltip("The profiles to cycle through, in order.")]
        [SerializeField] private MovementProfile[] profiles;
        [Tooltip("Row of the demo's on-screen text this uses.")]
        [SerializeField, Min(0)] private int row;

        private FirstPersonController player;
        private InputAction previous;
        private InputAction next;
        private bool enabledPrevious;   // enabled here on the player's private copy of the actions, so disabled here too
        private bool enabledNext;
        private string keysText;        // cached: binding texts allocate, and OnGUI runs several times a frame
        private bool keysForGamepad;
        private string label;           // the row's text, rebuilt when the profile or the keys change
        private MovementProfile labelProfile;

        private void Start()
        {
            player = FindAnyObjectByType<FirstPersonController>();
            if (player == null || player.Actions == null || profiles == null || profiles.Length == 0)
            {
                enabled = false;
                return;
            }
            previous = player.Actions.FindAction(PreviousActionPath);
            next = player.Actions.FindAction(NextActionPath);
            EnableActions();
        }

        private void OnEnable()
        {
            EnableActions();
        }

        private void OnDisable()
        {
            if (enabledPrevious && previous != null)
            {
                previous.Disable();
            }
            if (enabledNext && next != null)
            {
                next.Disable();
            }
            enabledPrevious = false;
            enabledNext = false;
        }

        private void EnableActions()
        {
            if (previous != null && !previous.enabled)
            {
                previous.Enable();
                enabledPrevious = true;
            }
            if (next != null && !next.enabled)
            {
                next.Enable();
                enabledNext = true;
            }
        }

        private void Update()
        {
            if (!player.InputEnabled)
            {
                return;   // a menu is up
            }
            if (next != null && next.WasPressedThisFrame())
            {
                Step(1);
            }
            else if (previous != null && previous.WasPressedThisFrame())
            {
                Step(-1);
            }
        }

        private void Step(int direction)
        {
            int index = System.Array.IndexOf(profiles, player.Profile);   // -1 for the controller's own settings
            index = index < 0 ? (direction > 0 ? 0 : profiles.Length - 1)
                              : (index + direction + profiles.Length) % profiles.Length;
            player.Profile = profiles[index];
        }

        private void OnGUI()
        {
            if (player == null || !player.InputEnabled)
            {
                return;
            }
            bool gamepad = player.UsingGamepad || player.ControllerOnly;
            if (keysText == null || keysForGamepad != gamepad)
            {
                keysForGamepad = gamepad;
                InputBinding scheme = InputBinding.MaskByGroup(keysForGamepad ? "Gamepad" : "Keyboard&Mouse");
                string back = previous != null ? previous.GetBindingDisplayString(scheme) : "";
                string forward = next != null ? next.GetBindingDisplayString(scheme) : "";
                keysText = $"{back} / {forward}";
                label = null;
            }
            if (label == null || labelProfile != player.Profile)
            {
                labelProfile = player.Profile;
                string current = labelProfile != null ? labelProfile.name : "the controller's own settings";
                label = $"Movement profile: {current}   ({keysText} to switch)";
            }
            DemoHud.Label(row, label);
        }
    }
}
