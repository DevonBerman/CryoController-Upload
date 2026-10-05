using UnityEngine;
using UnityEngine.Events;

namespace CryoController
{
    /// <summary>
    /// A rigidbody the player can pick up, carry, inspect, drop and throw with <see cref="PlayerCarry"/>. It is an
    /// <see cref="IInteractable"/>, so aiming at it shows its prompt and completing Interact picks it up. Needs a
    /// non-kinematic Rigidbody no heavier than the carrier's Max Mass, and a Collider on this object or a child.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Carryable")]
    public class Carryable : MonoBehaviour, IInteractable, ISaveable
    {
        [Tooltip("Shown in the interaction prompt.")]
        [SerializeField] private string prompt = "Pick up";
        [Tooltip("Whether the player can throw this, or only put it down.")]
        [SerializeField] private bool throwable = true;
        [Tooltip("Whether the player can inspect this while carrying it: Inspect brings it close and the look input " +
                 "turns it over.")]
        [SerializeField] private bool inspectable = true;
        [Tooltip("Shown on screen while the player inspects this. Empty shows nothing.")]
        [SerializeField, TextArea(2, 4)] private string description = "";
        [Tooltip("Invoked when the player picks this up.")]
        [SerializeField] private UnityEvent onPickedUp;
        [Tooltip("Invoked when the player lets go of this, dropped or thrown.")]
        [SerializeField] private UnityEvent onDropped;
        [Tooltip("Invoked when the player throws this, after On Dropped.")]
        [SerializeField] private UnityEvent onThrown;
        [Tooltip("Invoked when the player starts inspecting this.")]
        [SerializeField] private UnityEvent onInspected;

        /// <summary>The Rigidbody that is carried.</summary>
        public Rigidbody Body { get; private set; }

        /// <summary>Whether the player can throw this, or only put it down.</summary>
        public bool Throwable => throwable;

        /// <summary>Whether the player can inspect this while carrying it.</summary>
        public bool Inspectable => inspectable;

        /// <summary>Text shown while the player inspects this; empty for none. Override for text that changes.</summary>
        public virtual string Description => description;

        /// <summary>The carrier holding this right now, or null.</summary>
        public PlayerCarry Carrier { get; private set; }

        public bool IsCarried => Carrier != null;

        public virtual string Prompt => prompt;

        private void Awake()
        {
            Body = GetComponent<Rigidbody>();
        }

        private void OnDisable()
        {
            if (Carrier != null)
            {
                Carrier.Drop();   // a carried object that is switched off or destroyed is let go cleanly
            }
        }

        public virtual bool CanInteract(PlayerInteractor interactor)
        {
            return isActiveAndEnabled && !IsCarried && interactor.Carry != null && interactor.Carry.CanPickUp(this);
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (interactor.Carry != null)
            {
                interactor.Carry.PickUp(this);
            }
        }

        /// <summary>Called when the player starts or stops aiming at this object; override for a highlight.</summary>
        public virtual void OnFocusChanged(bool focused)
        {
        }

        [System.Serializable]
        private struct SavedState
        {
            public bool active;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 velocity;
            public Vector3 angularVelocity;
        }

        /// <summary>For <see cref="SaveSystem"/>: where it is and how it moves, and whether it is switched on.</summary>
        public string CaptureState()
        {
            Rigidbody body = Body != null ? Body : GetComponent<Rigidbody>();
            return JsonUtility.ToJson(new SavedState
            {
                active = gameObject.activeSelf,
                position = transform.position,
                rotation = transform.rotation,
                velocity = body != null && !body.isKinematic ? body.linearVelocity : Vector3.zero,
                angularVelocity = body != null && !body.isKinematic ? body.angularVelocity : Vector3.zero,
            });
        }

        /// <summary>For <see cref="SaveSystem"/>: a carried object is put down first.</summary>
        public void RestoreState(string state)
        {
            SavedState saved = JsonUtility.FromJson<SavedState>(state);
            if (Carrier != null)
            {
                Carrier.Drop();
            }
            gameObject.SetActive(saved.active);
            transform.SetPositionAndRotation(saved.position, saved.rotation);
            Rigidbody body = Body != null ? Body : GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = saved.position;
                body.rotation = saved.rotation;
                if (!body.isKinematic)
                {
                    body.linearVelocity = saved.velocity;
                    body.angularVelocity = saved.angularVelocity;
                }
            }
        }

        internal void NotifyPickedUp(PlayerCarry carrier)
        {
            Carrier = carrier;
            onPickedUp?.Invoke();
        }

        internal void NotifyInspected()
        {
            onInspected?.Invoke();
        }

        internal void NotifyReleased(bool thrown)
        {
            Carrier = null;
            onDropped?.Invoke();
            if (thrown)
            {
                onThrown?.Invoke();
            }
        }
    }
}
