using UnityEngine;
using UnityEngine.Events;

namespace CryoController
{
    /// <summary>
    /// A door, gate, hatch or drawer the player opens and closes with Interact. A hinged door swings round this object's
    /// up axis, so put this on the hinge (or on a model whose pivot is the hinge) with the door's collider on it or on a
    /// child; a sliding one moves along this object's forward axis, for drawers and sliding doors. It moves a kinematic
    /// Rigidbody, so loose props are pushed out of the way, and it waits rather than pushing into a player. It can be
    /// locked to a key: a player whose <see cref="PlayerKeys"/> holds that key unlocks it by using it.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Door")]
    public class Door : MonoBehaviour, IHoldInteractable, ISaveable
    {
        /// <summary>How the door moves.</summary>
        public enum DoorMotion
        {
            Hinged,
            Sliding,
        }

        private const float BlockMargin = 0.01f;   // a collider this close to a player counts as touching it (m)

        [Tooltip("Hinged swings round this object's up axis; Sliding moves along its forward axis (drawers, sliding doors).")]
        [SerializeField] private DoorMotion motion = DoorMotion.Hinged;
        [Tooltip("Hinged: how far it swings open, in degrees. With Swing Away off, the sign picks the direction " +
                 "(positive turns clockwise seen from above).")]
        [SerializeField, Range(-180f, 180f)] private float openAngle = 95f;
        [Tooltip("Hinged: swing away from the player who opens it, whichever side they are on.")]
        [SerializeField] private bool swingAway = true;
        [Tooltip("Sliding: how far it moves along its forward axis when open, in metres. Negative moves it back.")]
        [SerializeField] private float openDistance = 0.4f;
        [Tooltip("Seconds it takes to open or close fully.")]
        [SerializeField, Min(0.05f)] private float openTime = 0.7f;
        [Tooltip("Open when the scene starts.")]
        [SerializeField] private bool startOpen;
        [Tooltip("Seconds Interact must be held to open or close it. 0 uses it on a press.")]
        [SerializeField, Min(0f)] private float holdDuration;

        [Header("Lock")]
        [Tooltip("A locked door doesn't open. Using it tries the lock, and unlocks and opens it if the player has the key.")]
        [SerializeField] private bool locked;
        [Tooltip("The key that unlocks it, as Player Keys and Key Pickup name it. Empty: no key does; call Unlock() from a " +
                 "script instead.")]
        [SerializeField] private string keyId = "";
        [Tooltip("Take the key from the player when it unlocks the door.")]
        [SerializeField] private bool consumeKey;
        [Tooltip("Shown to the player when they try it locked without the key. Empty shows nothing.")]
        [SerializeField] private string lockedMessage = "It's locked.";

        [Header("Prompts")]
        [Tooltip("Prompt while closed.")]
        [SerializeField] private string openPrompt = "Open";
        [Tooltip("Prompt while open.")]
        [SerializeField] private string closePrompt = "Close";
        [Tooltip("Prompt while locked and the player doesn't have the key.")]
        [SerializeField] private string lockedPrompt = "Locked";
        [Tooltip("Prompt while locked and the player has the key.")]
        [SerializeField] private string unlockPrompt = "Unlock";

        [Header("Events")]
        [Tooltip("Invoked when it starts to open.")]
        [SerializeField] private UnityEvent onOpening;
        [Tooltip("Invoked when it starts to close.")]
        [SerializeField] private UnityEvent onClosing;
        [Tooltip("Invoked when it unlocks.")]
        [SerializeField] private UnityEvent onUnlocked;
        [Tooltip("Invoked when someone tries it while it is locked and they don't have the key.")]
        [SerializeField] private UnityEvent onLockedTried;

        /// <summary>Whether it is open or opening (false: closed or closing).</summary>
        public bool IsOpen { get; private set; }

        /// <summary>How far open it is right now, from 0 (closed) to 1 (fully open).</summary>
        public float OpenAmount { get; private set; }

        /// <summary>True while it is moving, or waiting for a player in its way.</summary>
        public bool IsMoving => OpenAmount != (IsOpen ? 1f : 0f);

        /// <summary>True while a player is in its way and it is waiting for them to move.</summary>
        public bool IsBlocked { get; private set; }

        /// <summary>Whether it is locked.</summary>
        public bool IsLocked => locked;

        /// <summary>The key that unlocks it; empty when no key does.</summary>
        public string KeyId => keyId;

        /// <summary>Raised when it starts to open.</summary>
        public event System.Action Opening;

        /// <summary>Raised when it starts to close.</summary>
        public event System.Action Closing;

        /// <summary>Raised when it has finished opening.</summary>
        public event System.Action Opened;

        /// <summary>Raised when it has finished closing.</summary>
        public event System.Action Closed;

        /// <summary>Raised when it unlocks.</summary>
        public event System.Action Unlocked;

        /// <summary>Raised when someone tries it while it is locked and they don't have the key.</summary>
        public event System.Action LockedTried;

        private Rigidbody body;
        private Collider[] colliders;
        private readonly Collider[] overlaps = new Collider[16];
        private Vector3 closedLocalPosition;
        private Quaternion closedLocalRotation;
        private float swingSign = 1f;          // which way a hinged door swings this time
        private PlayerInteractor lastAsking;   // the interactor that last asked CanInteract, for the prompt
        private string pendingState;            // a save restored before Awake, applied there

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            colliders = GetComponentsInChildren<Collider>(true);
            closedLocalPosition = transform.localPosition;
            closedLocalRotation = transform.localRotation;
            swingSign = openAngle < 0f ? -1f : 1f;
            if (pendingState != null)
            {
                RestoreState(pendingState);
                pendingState = null;
            }
            else if (startOpen)
            {
                SetOpen(true, instant: true);
            }
        }

        // ------------------------------------------------------------------ Saving

        [System.Serializable]
        private struct SavedState
        {
            public bool open;
            public bool locked;
            public float swing;
        }

        /// <summary>For <see cref="SaveSystem"/>: open or shut, locked or not, and which way it swung.</summary>
        public string CaptureState()
        {
            return JsonUtility.ToJson(new SavedState { open = IsOpen, locked = locked, swing = swingSign });
        }

        /// <summary>For <see cref="SaveSystem"/>: puts it back at once, without raising its events.</summary>
        public void RestoreState(string state)
        {
            if (body == null)
            {
                pendingState = state;   // switched off since the scene loaded: Awake applies it
                return;
            }
            SavedState saved = JsonUtility.FromJson<SavedState>(state);
            locked = saved.locked;
            if (saved.swing != 0f)
            {
                swingSign = Mathf.Sign(saved.swing);
            }
            IsOpen = saved.open;
            OpenAmount = saved.open ? 1f : 0f;
            IsBlocked = false;
            ApplyPose(Pose(OpenAmount), teleport: true);
        }

        // ------------------------------------------------------------------ Opening and closing

        /// <summary>Opens or closes it, moving over Open Time, or at once when instant. Ignores the lock.</summary>
        public void SetOpen(bool open, bool instant = false)
        {
            if (open != IsOpen)
            {
                IsOpen = open;
                if (open)
                {
                    Opening?.Invoke();
                    onOpening?.Invoke();
                }
                else
                {
                    Closing?.Invoke();
                    onClosing?.Invoke();
                }
            }
            if (instant)
            {
                OpenAmount = open ? 1f : 0f;
                IsBlocked = false;
                ApplyPose(Pose(OpenAmount), teleport: true);
                (open ? Opened : Closed)?.Invoke();
            }
        }

        /// <summary>Opens it, swinging away from the point when it is hinged and Swing Away is on.</summary>
        public void Open(Vector3 awayFrom)
        {
            if (!IsOpen && OpenAmount <= 0f)
            {
                ChooseSwing(awayFrom);
            }
            SetOpen(true);
        }

        /// <summary>Unlocks it without a key.</summary>
        public void Unlock()
        {
            if (!locked)
            {
                return;
            }
            locked = false;
            Unlocked?.Invoke();
            onUnlocked?.Invoke();
        }

        /// <summary>Locks it. It stays where it is: lock a closed door to keep it shut.</summary>
        public void Lock()
        {
            locked = true;
        }

        private void ChooseSwing(Vector3 awayFrom)
        {
            if (motion != DoorMotion.Hinged || !swingAway)
            {
                swingSign = openAngle < 0f ? -1f : 1f;
                return;
            }
            // Turning +Y moves a leaf that reaches along +X towards -Z: so the sign that takes the leaf away from the
            // player's side of the closed door depends on which side of the hinge the leaf is.
            Vector3 local = Quaternion.Inverse(ClosedWorldRotation()) * (awayFrom - transform.position);
            float leafSide = LeafCenterLocal().x >= 0f ? 1f : -1f;
            float playerSide = local.z >= 0f ? 1f : -1f;
            swingSign = leafSide * playerSide;
        }

        // Where the door's colliders are, on average, in its own closed space.
        private Vector3 LeafCenterLocal()
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (Collider c in colliders)
            {
                if (c != null && !c.isTrigger)
                {
                    sum += transform.InverseTransformPoint(c.bounds.center);
                    count++;
                }
            }
            return count > 0 ? sum / count : Vector3.right;
        }

        private void FixedUpdate()
        {
            float target = IsOpen ? 1f : 0f;
            if (OpenAmount == target)
            {
                IsBlocked = false;
                return;
            }
            float next = Mathf.MoveTowards(OpenAmount, target, Time.fixedDeltaTime / openTime);
            (Vector3 position, Quaternion rotation) pose = Pose(next);
            IsBlocked = BlocksAPlayer(pose.position, pose.rotation);
            if (IsBlocked)
            {
                return;   // wait for them to move
            }
            OpenAmount = next;
            ApplyPose(pose, teleport: false);
            if (OpenAmount == target)
            {
                (IsOpen ? Opened : Closed)?.Invoke();
            }
        }

        // The door's world pose at an open amount, eased so it starts and stops gently.
        private (Vector3 position, Quaternion rotation) Pose(float amount)
        {
            float eased = Mathf.SmoothStep(0f, 1f, amount);
            Quaternion closed = ClosedWorldRotation();
            Vector3 closedPosition = transform.parent != null ? transform.parent.TransformPoint(closedLocalPosition) : closedLocalPosition;
            if (motion == DoorMotion.Hinged)
            {
                return (closedPosition, closed * Quaternion.Euler(0f, swingSign * Mathf.Abs(openAngle) * eased, 0f));
            }
            return (closedPosition + closed * Vector3.forward * (openDistance * eased), closed);
        }

        private Quaternion ClosedWorldRotation()
        {
            return transform.parent != null ? transform.parent.rotation * closedLocalRotation : closedLocalRotation;
        }

        private void ApplyPose((Vector3 position, Quaternion rotation) pose, bool teleport)
        {
            if (teleport || !Application.isPlaying)
            {
                transform.SetPositionAndRotation(pose.position, pose.rotation);
                body.position = pose.position;
                body.rotation = pose.rotation;
            }
            else
            {
                body.MovePosition(pose.position);
                body.MoveRotation(pose.rotation);
            }
        }

        // Whether any of the door's colliders, moved to the pose, would overlap a CharacterController: a player, which
        // a kinematic body would push into rather than move.
        private bool BlocksAPlayer(Vector3 position, Quaternion rotation)
        {
            // Each collider keeps its place on the door, so its pose at the next step is the door's next pose times
            // where it sits on the door now.
            Matrix4x4 next = Matrix4x4.TRS(position, rotation, transform.lossyScale);
            Matrix4x4 toDoor = transform.worldToLocalMatrix;
            foreach (Collider c in colliders)
            {
                if (c == null || !c.enabled || c.isTrigger)
                {
                    continue;
                }
                int count;
                if (c is BoxCollider box)
                {
                    Matrix4x4 world = next * toDoor * box.transform.localToWorldMatrix;
                    Vector3 scale = box.transform.lossyScale;
                    Vector3 half = new Vector3(Mathf.Abs(box.size.x * scale.x), Mathf.Abs(box.size.y * scale.y),
                                               Mathf.Abs(box.size.z * scale.z)) * 0.5f + Vector3.one * BlockMargin;
                    count = Physics.OverlapBoxNonAlloc(world.MultiplyPoint3x4(box.center), half, overlaps, world.rotation,
                                                       Physics.AllLayers, QueryTriggerInteraction.Ignore);
                }
                else
                {
                    // Other shapes: their bounds, moved with the door.
                    Bounds bounds = c.bounds;
                    count = Physics.OverlapBoxNonAlloc(next.MultiplyPoint3x4(toDoor.MultiplyPoint3x4(bounds.center)), bounds.extents, overlaps,
                                                       Quaternion.identity, Physics.AllLayers, QueryTriggerInteraction.Ignore);
                }
                for (int i = 0; i < count; i++)
                {
                    if (overlaps[i] is CharacterController)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ IInteractable

        /// <summary>The prompt for the player last aiming at it: Open or Close, or Locked or Unlock when locked.</summary>
        public string Prompt
        {
            get
            {
                if (locked)
                {
                    return HasKey(lastAsking) ? unlockPrompt : lockedPrompt;
                }
                return IsOpen ? closePrompt : openPrompt;
            }
        }

        /// <summary>Seconds Interact must be held: Hold Duration, or 0 when locked without the key (it only tries the lock).</summary>
        public float HoldDuration => locked && !HasKey(lastAsking) ? 0f : holdDuration;

        public bool CanInteract(PlayerInteractor interactor)
        {
            lastAsking = interactor;
            return isActiveAndEnabled;
        }

        public void Interact(PlayerInteractor interactor)
        {
            Vector3 from = interactor != null ? interactor.transform.position : transform.position - transform.forward;
            if (locked)
            {
                if (!HasKey(interactor))
                {
                    LockedTried?.Invoke();
                    onLockedTried?.Invoke();
                    if (interactor != null && !string.IsNullOrEmpty(lockedMessage))
                    {
                        interactor.Notify(lockedMessage);
                    }
                    return;
                }
                if (consumeKey)
                {
                    interactor.GetComponent<PlayerKeys>().Remove(keyId);
                }
                Unlock();
                Open(from);
                return;
            }
            if (IsOpen)
            {
                SetOpen(false);
            }
            else
            {
                Open(from);
            }
        }

        void IInteractable.OnFocusChanged(bool focused)
        {
        }

        private bool HasKey(PlayerInteractor interactor)
        {
            if (interactor == null || string.IsNullOrEmpty(keyId))
            {
                return false;
            }
            PlayerKeys keys = interactor.GetComponent<PlayerKeys>();
            return keys != null && keys.Has(keyId);
        }

        private void OnDrawGizmosSelected()
        {
            // The way it opens: the swing's arc at the leaf's reach (yellow), or the slide (yellow line).
            Gizmos.color = Color.yellow;
            if (motion == DoorMotion.Sliding)
            {
                Gizmos.DrawLine(transform.position, transform.position + transform.forward * openDistance);
                return;
            }
            Collider[] shapes = colliders != null ? colliders : GetComponentsInChildren<Collider>();
            float reach = 1f;
            foreach (Collider c in shapes)
            {
                if (c != null && !c.isTrigger)
                {
                    reach = Mathf.Max(0.2f, Vector3.Distance(transform.position, c.bounds.center) * 2f);
                    break;
                }
            }
            Vector3 previous = transform.position + transform.right * reach;
            for (int i = 1; i <= 12; i++)
            {
                float angle = openAngle * i / 12f;
                Vector3 point = transform.position + Quaternion.AngleAxis(angle, transform.up) * transform.right * reach;
                Gizmos.DrawLine(previous, point);
                previous = point;
            }
        }
    }
}
