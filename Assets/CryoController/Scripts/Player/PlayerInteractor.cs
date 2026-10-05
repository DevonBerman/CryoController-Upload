using UnityEngine;
using UnityEngine.InputSystem;

namespace CryoController
{
    /// <summary>
    /// Lets the player use <see cref="IInteractable"/> objects. Each frame a ray is cast from the camera along its view
    /// direction, and if it hits no interactable a thin sphere is swept the same way; the interactable hit (and allowed)
    /// becomes <see cref="Current"/>, and a completed Player/Interact input uses it. In the shipped actions Interact is a plain press. An object that implements
    /// <see cref="IHoldInteractable"/> with a Hold Duration above zero needs the button held that long instead, timed
    /// here, and <see cref="HoldProgress"/> reports how far along that is. For other objects the action's own
    /// interaction decides what "completed" means, so a Hold added to the action in the asset applies to them. Input
    /// pauses with the controller's InputEnabled.
    /// </summary>
    [RequireComponent(typeof(FirstPersonController))]
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Player Interactor")]
    public class PlayerInteractor : MonoBehaviour
    {
        private const string InteractActionPath = "Player/Interact";

        [Tooltip("How far from the camera the player can reach, in metres.")]
        [SerializeField, Min(0.1f)] private float interactRange = 2.5f;
        [Tooltip("Radius of the sphere swept from the camera when the ray under the crosshair hits nothing usable, in " +
                 "metres. A little thickness makes small objects easier to aim at; 0 casts the ray only.")]
        [SerializeField, Min(0f)] private float interactRadius = 0.15f;
        [Tooltip("Layers the sweep hits. Anything hit blocks the reach, so a wall in front of an object hides it.")]
        [SerializeField] private LayerMask interactLayers = Physics.DefaultRaycastLayers;

        /// <summary>The interactable the player is aiming at and may use, or null.</summary>
        public IInteractable Current { get; private set; }

        /// <summary>Prompt of <see cref="Current"/>, or null when there is nothing to use.</summary>
        public string CurrentPrompt => Current != null ? Current.Prompt : null;

        /// <summary>0..1 progress of a hold-to-interact input on the current object; 0 when not holding.</summary>
        public float HoldProgress { get; private set; }

        /// <summary>Raised when <see cref="Current"/> changes; the argument may be null.</summary>
        public event System.Action<IInteractable> FocusChanged;

        /// <summary>Raised after an interactable has been used.</summary>
        public event System.Action<IInteractable> Interacted;

        /// <summary>The Interact action (on the controller's actions), for prompts and components sharing the press.</summary>
        public InputAction InteractAction => interactAction;

        /// <summary>The carrier on this player, or null; while it carries something, Interact belongs to it.</summary>
        public PlayerCarry Carry { get; private set; }

        /// <summary>
        /// The message from the last <see cref="Notify"/> while it is showing ("It's locked."), or null. The prompt HUD
        /// draws it.
        /// </summary>
        public string Message => Time.unscaledTime < messageUntil ? message : null;

        /// <summary>How far through its time the current message is, from 0 (just shown) to 1 (gone).</summary>
        public float MessageAge => messageSeconds > 0f ? Mathf.Clamp01(1f - (messageUntil - Time.unscaledTime) / messageSeconds) : 1f;

        /// <summary>Raised by <see cref="Notify"/>, with the message.</summary>
        public event System.Action<string> Notified;

        /// <summary>
        /// While true nothing is focused or used: another component has the Interact button, such as
        /// <see cref="PlayerReader"/> while a note is open.
        /// </summary>
        public bool Busy { get; set; }

        private FirstPersonController controller;
        private Transform eyes;
        private bool ignoreUntilReleased;   // see IgnoreCurrentPress
        private InputAction interactAction;
        private InteractableHighlight focusedHighlight;
        private bool ownsAction;   // the action is on the controller's private copy, which nothing else enables
        private string message;
        private float messageUntil;   // Time.unscaledTime the message goes
        private float messageSeconds;

        // A hold timed here for an IHoldInteractable: what it is on, when it started (game time), and whether it has
        // been used, so one hold uses it once.
        private IInteractable holdTarget;
        private float holdStart;
        private bool holdUsed;

        private void Awake()
        {
            controller = GetComponent<FirstPersonController>();
            Carry = GetComponent<PlayerCarry>();

            Camera playerCamera = GetComponentInChildren<Camera>(true);
            eyes = playerCamera != null ? playerCamera.transform : transform;
        }

        private void Start()
        {
            // After every Awake, so the controller has resolved (and possibly copied) its actions whatever the order.
            InputActionAsset asset = controller.Actions != null ? controller.Actions : InputSystem.actions;
            if (asset == null)
            {
                Debug.LogError("PlayerInteractor: no input actions available. Disabling.", this);
                enabled = false;
                return;
            }

            interactAction = asset.FindAction(InteractActionPath, throwIfNotFound: false);
            if (interactAction == null)
            {
                Debug.LogError($"PlayerInteractor: action '{InteractActionPath}' not found in '{asset.name}'. Disabling.", this);
                enabled = false;
                return;
            }
            ownsAction = asset != InputSystem.actions;
            if (ownsAction && isActiveAndEnabled)
            {
                interactAction.Enable();   // OnEnable ran before Start, with nothing to enable yet
            }
        }

        private void OnEnable()
        {
            if (interactAction != null && ownsAction)
            {
                interactAction.Enable();
            }
        }

        private void OnDisable()
        {
            if (interactAction != null && ownsAction)
            {
                interactAction.Disable();
            }

            SetFocus(null);
            HoldProgress = 0f;
        }

        private void Update()
        {
            if (interactAction == null)
            {
                return;   // Start failed; the error is in the console.
            }

            if (ignoreUntilReleased)
            {
                // Still ignored on the release frame itself: Tap, Slow Tap and release-only presses perform then.
                if (interactAction.IsPressed() || interactAction.WasReleasedThisFrame())
                {
                    SetFocus(null);
                    HoldProgress = 0f;
                    return;
                }
                ignoreUntilReleased = false;
            }

            if (!controller.InputEnabled || Busy || (Carry != null && Carry.IsCarrying))
            {
                SetFocus(null);
                HoldProgress = 0f;
                return;
            }

            IInteractable found = Probe();
            if (found != null && !found.CanInteract(this))
            {
                found = null;
            }
            IInteractable before = Current;
            SetFocus(found);

            // A hold belongs to what it started on: aiming at something else mid-hold starts over with a new press,
            // rather than finishing the old hold on the new object.
            if (Current != null && !ReferenceEquals(before, Current) && interactAction.IsInProgress()
                && !interactAction.WasPressedThisFrame())
            {
                IgnoreCurrentPress();
                return;
            }

            float holdDuration = Current is IHoldInteractable holdable ? holdable.HoldDuration : 0f;
            if (holdDuration > 0f)
            {
                // Timed here, whatever interaction the action has, from the press on this object.
                if (interactAction.WasPressedThisFrame())
                {
                    holdTarget = Current;
                    holdStart = Time.time;
                    holdUsed = false;
                }
                bool holding = interactAction.IsPressed() && ReferenceEquals(holdTarget, Current) && !holdUsed;
                HoldProgress = holding ? Mathf.Clamp01((Time.time - holdStart) / holdDuration) : 0f;
                if (holding && HoldProgress >= 1f)
                {
                    holdUsed = true;
                    HoldProgress = 0f;
                    Use();
                }
                return;
            }
            holdTarget = null;

            HoldProgress = Current != null && interactAction.IsInProgress() ? interactAction.GetTimeoutCompletionPercentage() : 0f;
            if (Current != null && interactAction.WasPerformedThisFrame())
            {
                Use();
            }
        }

        private void Use()
        {
            // Kept aside: the interaction may switch this component off or refocus it before the event goes out.
            IInteractable target = Current;
            target.Interact(this);
            Interacted?.Invoke(target);
        }

        /// <summary>
        /// Shows the player a short message for a few seconds, such as "It's locked." or "Picked up the key." A new
        /// message replaces the one showing.
        /// </summary>
        public void Notify(string text, float seconds = 2.5f)
        {
            message = text;
            messageSeconds = Mathf.Max(0.1f, seconds);
            messageUntil = Time.unscaledTime + messageSeconds;
            Notified?.Invoke(text);
        }

        /// <summary>
        /// Ignores the Interact press in progress: nothing is focused or used until the button is released. For a
        /// component that uses the same press for something else, such as <see cref="PlayerCarry"/> dropping an object.
        /// </summary>
        public void IgnoreCurrentPress()
        {
            ignoreUntilReleased = true;
            holdTarget = null;
            SetFocus(null);
            HoldProgress = 0f;
        }

        private IInteractable Probe()
        {
            // A thin ray first, so a small object right under the crosshair wins over a bigger one beside it (a key in an
            // open drawer). Then the sweep, which makes small and distant objects easier to aim at. Both start inside the
            // player's own capsule, which casts ignore, so they cannot hit the player.
            Vector3 origin = eyes.position;
            Vector3 direction = eyes.forward;
            bool rayHit = Physics.Raycast(origin, direction, out RaycastHit ray, interactRange, interactLayers,
                                          QueryTriggerInteraction.Ignore);
            if (rayHit)
            {
                IInteractable aimed = ray.collider.GetComponentInParent<IInteractable>();
                if (aimed != null)
                {
                    return aimed;
                }
            }
            if (interactRadius <= 0f
                || !Physics.SphereCast(origin, interactRadius, direction, out RaycastHit hit, interactRange, interactLayers,
                                       QueryTriggerInteraction.Ignore))
            {
                return null;
            }
            return hit.collider.GetComponentInParent<IInteractable>();
        }

        private void SetFocus(IInteractable next)
        {
            // A focused MonoBehaviour may have been destroyed since last frame; Unity's fake null shows through the
            // Object comparison, and such an object gets no farewell callback. Losing it is still a change of focus.
            bool lostDestroyed = false;
            if (Current is Object currentObject && currentObject == null)
            {
                Current = null;
                lostDestroyed = true;
            }

            if (ReferenceEquals(next, Current) && !lostDestroyed)
            {
                return;
            }

            Current?.OnFocusChanged(false);
            // The highlight is kept separately, so it still goes out when the interactable was destroyed but its
            // highlight (on a parent) was not.
            if (focusedHighlight != null)
            {
                focusedHighlight.SetHighlighted(false);
            }
            Current = next;
            Current?.OnFocusChanged(true);
            focusedHighlight = FindHighlight(Current);
            if (focusedHighlight != null)
            {
                focusedHighlight.SetHighlighted(true);
            }
            FocusChanged?.Invoke(Current);
        }

        // An Interactable Highlight on the interactable's object or a parent of it, if there is one. Looked up only when
        // focus changes.
        private static InteractableHighlight FindHighlight(IInteractable target)
        {
            return target is Component component && component != null
                ? component.GetComponentInParent<InteractableHighlight>()
                : null;
        }
    }
}
