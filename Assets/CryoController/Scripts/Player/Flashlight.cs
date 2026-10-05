using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace CryoController
{
    /// <summary>
    /// A light the player switches on and off with the Player/Flashlight action (L, or down on the D-pad, in the shipped
    /// actions). Put it on a child of the player's camera, or on a Held Item, with the Light to switch on it or a child.
    /// It saves whether it is on.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Flashlight")]
    public class Flashlight : MonoBehaviour, ISaveable
    {
        private const string FlashlightActionPath = "Player/Flashlight";

        [Tooltip("The light to switch. Empty uses the first Light on this object or its children.")]
        [SerializeField] private Light lamp;
        [Tooltip("On when the scene starts.")]
        [SerializeField] private bool startOn;
        [Tooltip("Invoked when it switches on.")]
        [SerializeField] private UnityEvent onSwitchedOn;
        [Tooltip("Invoked when it switches off.")]
        [SerializeField] private UnityEvent onSwitchedOff;

        /// <summary>Whether the light is on.</summary>
        public bool IsOn { get; private set; }

        /// <summary>Raised when it switches, with whether it is now on.</summary>
        public event System.Action<bool> Switched;

        private FirstPersonController player;
        private InputAction action;
        private bool ownsAction;   // the action is on the player's private copy, which nothing else enables

        private void Awake()
        {
            player = GetComponentInParent<FirstPersonController>();
            if (lamp == null)
            {
                lamp = GetComponentInChildren<Light>(true);
            }
            SetOn(startOn, notify: false);
        }

        private void Start()
        {
            // After every Awake, so the player has resolved its actions.
            InputActionAsset asset = player != null && player.Actions != null ? player.Actions : InputSystem.actions;
            action = asset != null ? asset.FindAction(FlashlightActionPath, throwIfNotFound: false) : null;
            ownsAction = action != null && asset != InputSystem.actions;
            if (ownsAction && isActiveAndEnabled)
            {
                action.Enable();
            }
        }

        private void OnEnable()
        {
            if (action != null && ownsAction)
            {
                action.Enable();
            }
        }

        private void OnDisable()
        {
            if (action != null && ownsAction)
            {
                action.Disable();
            }
        }

        private void Update()
        {
            if (action == null || (player != null && (!player.InputEnabled || player.PressesSuppressed)))
            {
                return;
            }
            if (action.WasPressedThisFrame())
            {
                Toggle();
            }
        }

        /// <summary>Switches the light on or off.</summary>
        public void SetOn(bool on)
        {
            SetOn(on, notify: true);
        }

        /// <summary>Switches the light over.</summary>
        public void Toggle()
        {
            SetOn(!IsOn);
        }

        private void SetOn(bool on, bool notify)
        {
            bool changed = on != IsOn;
            IsOn = on;
            if (lamp != null)
            {
                lamp.enabled = on;
            }
            if (!notify || !changed)
            {
                return;
            }
            Switched?.Invoke(on);
            (on ? onSwitchedOn : onSwitchedOff)?.Invoke();
        }

        [System.Serializable]
        private struct SavedState
        {
            public bool on;
        }

        /// <summary>For <see cref="SaveSystem"/>: whether it is on.</summary>
        public string CaptureState()
        {
            return JsonUtility.ToJson(new SavedState { on = IsOn });
        }

        /// <summary>For <see cref="SaveSystem"/>.</summary>
        public void RestoreState(string state)
        {
            SetOn(JsonUtility.FromJson<SavedState>(state).on, notify: false);
        }
    }
}
