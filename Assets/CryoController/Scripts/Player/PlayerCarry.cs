using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CryoController
{
    /// <summary>
    /// Picks up, carries, drops and throws <see cref="Carryable"/> rigidbodies. Aiming at one shows its prompt through
    /// <see cref="PlayerInteractor"/> and completing Interact picks it up. The object floats in front of the camera,
    /// steered there by its velocity every physics step, so it still collides with the world and knocks other things
    /// about. Interact again puts it down; Throw throws it (the shipped actions bind the left mouse button and the right
    /// trigger; an asset without a Throw action uses Attack). Inspect (R and the D-pad's up) brings an
    /// <see cref="Carryable.Inspectable"/> object close and holds the player still while the look input turns the object
    /// over; Inspect again goes back to carrying. It is let go when it gets stuck too far from where it should be, or when
    /// the player grabs a ladder.
    /// </summary>
    [RequireComponent(typeof(PlayerInteractor))]
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Player Carry")]
    public class PlayerCarry : MonoBehaviour
    {
        private const string ThrowActionPath = "Player/Throw";
        private const string FallbackThrowActionPath = "Player/Attack";
        private const string InspectActionPath = "Player/Inspect";
        private const string LookActionPath = "Player/Look";

        /// <summary>
        /// The layer a carried object's colliders are on: the built-in Ignore Raycast layer, which the controller's
        /// ground, ledge, headroom and lean probes and the interaction sweep all leave out, so a box held at chest height
        /// is not a ledge to mantle onto.
        /// </summary>
        public const int CarriedLayer = 2;

        // A newly picked up object (off the floor, say) is given this long, in seconds, to reach the hold point before
        // Break Distance applies.
        private const float ArrivalTime = 1f;

        // On a drop the object keeps at most this much speed (m/s) beyond the player's, so a quick turn cannot fling it.
        private const float MaxDropExtraSpeed = 2f;

        // With something in the way, the object is held no closer to the camera than this (m).
        private const float MinHoldDistance = 0.25f;

        [Tooltip("Heaviest object the player can pick up, in kg.")]
        [SerializeField, Min(0f)] private float maxMass = 20f;
        [Tooltip("Gap between the camera and a carried object, in metres. Bigger objects are held further out by their size.")]
        [SerializeField, Min(0.1f)] private float holdDistance = 0.6f;
        [Tooltip("How stiffly the object follows the hold point, per second: the share of the gap closed each second, " +
                 "roughly. Higher is snappier; capped at one physics step.")]
        [SerializeField, Min(1f)] private float holdResponsiveness = 15f;
        [Tooltip("Top speed at which a carried object moves towards the hold point, in m/s, on top of the player's own.")]
        [SerializeField, Min(0.1f)] private float maxHoldSpeed = 15f;
        [Tooltip("A carried object that gets further than this from its hold point, in metres, once it has reached it " +
                 "(stuck behind something while the player moved on), is dropped. A newly picked up object has a second " +
                 "to get there.")]
        [SerializeField, Min(0.1f)] private float breakDistance = 1f;
        [Tooltip("Impulse of a throw, in newton seconds: a 1 kg object leaves at this many m/s, heavier ones slower.")]
        [SerializeField, Min(0f)] private float throwImpulse = 12f;
        [Tooltip("Fastest a thrown object leaves the hands, in m/s, however light it is.")]
        [SerializeField, Min(0f)] private float maxThrowSpeed = 15f;
        [Tooltip("Gap between the camera and an object being inspected, in metres, in place of Hold Distance: closer, " +
                 "so it fills more of the view.")]
        [SerializeField, Min(0.05f)] private float inspectDistance = 0.35f;
        [Tooltip("Degrees an inspected object turns per pixel of mouse movement.")]
        [SerializeField, Min(0f)] private float inspectMouseSpeed = 0.3f;
        [Tooltip("Degrees per second an inspected object turns at full stick deflection.")]
        [SerializeField, Min(0f)] private float inspectStickSpeed = 180f;

        /// <summary>True while something is carried.</summary>
        public bool IsCarrying => carried != null;

        /// <summary>The object carried, or null.</summary>
        public Carryable Carried => carried;

        /// <summary>Whether the carried object can be thrown right now.</summary>
        public bool CanThrow => carried != null && carried.Throwable && throwAction != null && throwImpulse > 0f;

        /// <summary>The action that throws: Player/Throw, or Player/Attack when the asset has no Throw. May be null.</summary>
        public InputAction ThrowAction => throwAction;

        /// <summary>True while the carried object is being inspected; the player holds still meanwhile.</summary>
        public bool IsInspecting => inspecting;

        /// <summary>Whether the carried object can be inspected right now.</summary>
        public bool CanInspect => carried != null && carried.Inspectable && inspectAction != null;

        /// <summary>The action that starts and ends an inspection: Player/Inspect. May be null.</summary>
        public InputAction InspectAction => inspectAction;

        /// <summary>Raised after an object is picked up.</summary>
        public event System.Action<Carryable> PickedUp;

        /// <summary>Raised after an object is let go, dropped or thrown.</summary>
        public event System.Action<Carryable> Dropped;

        /// <summary>Raised after an object is thrown, after <see cref="Dropped"/>.</summary>
        public event System.Action<Carryable> Thrown;

        /// <summary>Raised when an inspection starts.</summary>
        public event System.Action<Carryable> InspectionStarted;

        /// <summary>Raised when an inspection ends, whatever ended it; before <see cref="Dropped"/> when a drop did.</summary>
        public event System.Action<Carryable> InspectionEnded;

        // Colliders let go of while still overlapping the player: their collision with the capsule comes back once
        // they are clear, so a drop never lands inside the player.
        private class PendingCollisions
        {
            public Rigidbody Body;
            public Collider[] Colliders;
        }

        private FirstPersonController controller;
        private PlayerInteractor interactor;
        private CharacterController capsule;
        private Transform eyes;
        private InputAction throwAction;
        private InputAction inspectAction;
        private InputAction lookAction;
        private Carryable carried;
        private Rigidbody body;
        private Collider[] colliders;
        private int[] originalLayers;
        private bool originalUseGravity;
        private RigidbodyInterpolation originalInterpolation;
        private CollisionDetectionMode originalCollisionDetection;
        private Quaternion heldRotation;   // the object's rotation relative to the player's facing
        private float heldRadius;          // half the object's diagonal: how much further out than holdDistance it sits
        private int pickedUpFrame = -1;
        private float pickedUpTime;        // Time.time of the pick up
        private bool arrived;              // the object has been within Break Distance of its hold point
        private bool inspecting;
        private bool moveAndLookWasEnabled = true;   // the controller's MoveAndLookEnabled before the inspection
        private readonly List<PendingCollisions> pending = new List<PendingCollisions>();

        private void Awake()
        {
            controller = GetComponent<FirstPersonController>();
            interactor = GetComponent<PlayerInteractor>();
            capsule = GetComponent<CharacterController>();
            Camera playerCamera = GetComponentInChildren<Camera>(true);
            eyes = playerCamera != null ? playerCamera.transform : transform;
        }

        private void Start()
        {
            // After every Awake, so the controller has made its private copy of the actions.
            InputActionAsset asset = controller.Actions;
            if (asset != null)
            {
                throwAction = asset.FindAction(ThrowActionPath, throwIfNotFound: false)
                              ?? asset.FindAction(FallbackThrowActionPath, throwIfNotFound: false);
                inspectAction = asset.FindAction(InspectActionPath, throwIfNotFound: false);
                lookAction = asset.FindAction(LookActionPath, throwIfNotFound: false);   // the controller enables it
            }
            if (throwAction == null)
            {
                Debug.LogWarning("PlayerCarry: no Player/Throw or Player/Attack action; carried objects can only be dropped.", this);
            }
            if (inspectAction == null)
            {
                Debug.LogWarning("PlayerCarry: no Player/Inspect action; carried objects can't be inspected.", this);
            }
            if (isActiveAndEnabled)
            {
                // OnEnable ran before Start, with nothing to enable yet.
                throwAction?.Enable();
                inspectAction?.Enable();
            }
        }

        private void OnEnable()
        {
            throwAction?.Enable();
            inspectAction?.Enable();
        }

        private void OnDisable()
        {
            Drop();
            throwAction?.Disable();
            inspectAction?.Disable();
            RestorePendingCollisions(force: true);
        }

        /// <summary>Whether the object can be picked up now: light enough, not kinematic, and not what the player stands on.</summary>
        public bool CanPickUp(Carryable item)
        {
            if (!isActiveAndEnabled || carried != null || item == null || item.Body == null || item.IsCarried)
            {
                return false;   // (another carrier's object would be read back with its carried state as the original)
            }
            Rigidbody itemBody = item.Body;
            if (itemBody.isKinematic || itemBody.mass > maxMass || controller.IsClimbing || controller.IsMantling)
            {
                return false;
            }
            // Never the thing underfoot: with its collision against the player ignored, the player would fall through it.
            Collider ground = controller.Ground;
            return ground == null || ground.attachedRigidbody != itemBody;
        }

        /// <summary>Picks the object up. Returns false when <see cref="CanPickUp"/> says no.</summary>
        public bool PickUp(Carryable item)
        {
            if (!CanPickUp(item))
            {
                return false;
            }

            carried = item;
            body = item.Body;
            pending.RemoveAll(p => p.Body == body);   // picked up again before it was clear of the player

            // Only the colliders that move with this body; a child with its own Rigidbody is a separate object.
            var own = new List<Collider>();
            foreach (Collider c in body.GetComponentsInChildren<Collider>())
            {
                if (c.attachedRigidbody == body && !c.isTrigger)
                {
                    own.Add(c);
                }
            }
            colliders = own.ToArray();

            // All layers are read before any is changed: two colliders on one object share its layer.
            originalLayers = new int[colliders.Length];
            for (int i = 0; i < colliders.Length; i++)
            {
                originalLayers[i] = colliders[i].gameObject.layer;
            }
            Bounds bounds = new Bounds(body.worldCenterOfMass, Vector3.zero);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].gameObject.layer = CarriedLayer;
                SetIgnored(colliders[i], true);
                bounds.Encapsulate(colliders[i].bounds);
            }
            heldRadius = bounds.extents.magnitude;

            originalUseGravity = body.useGravity;
            originalInterpolation = body.interpolation;
            originalCollisionDetection = body.collisionDetectionMode;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;              // smooth in front of the camera
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;   // swung fast, still collides

            heldRotation = Quaternion.Inverse(transform.rotation) * body.rotation;
            pickedUpFrame = Time.frameCount;
            pickedUpTime = Time.time;
            arrived = false;

            item.NotifyPickedUp(this);
            PickedUp?.Invoke(item);
            return true;
        }

        /// <summary>Puts the carried object down where it is.</summary>
        public void Drop()
        {
            Release(false);
        }

        /// <summary>Throws the carried object along the view, if it can be thrown; otherwise drops it.</summary>
        public void Throw()
        {
            Release(carried != null && carried.Throwable && throwImpulse > 0f);
        }

        /// <summary>
        /// Starts inspecting the carried object: it comes closer, the player holds still, and the look input turns the
        /// object. Returns false when <see cref="CanInspect"/> says no or an inspection is already under way.
        /// </summary>
        public bool StartInspecting()
        {
            if (inspecting || !CanInspect)
            {
                return false;
            }

            inspecting = true;
            moveAndLookWasEnabled = controller.MoveAndLookEnabled;
            controller.MoveAndLookEnabled = false;
            Carryable item = carried;   // kept aside: an On Inspected handler may drop it before the event goes out
            item.NotifyInspected();
            InspectionStarted?.Invoke(item);
            return true;
        }

        /// <summary>Ends an inspection: the object goes back to the carry position, turned as the player left it.</summary>
        public void StopInspecting()
        {
            if (!inspecting)
            {
                return;
            }

            inspecting = false;
            controller.MoveAndLookEnabled = moveAndLookWasEnabled;
            InspectionEnded?.Invoke(carried);
        }

        private void Update()
        {
            if (carried == null)
            {
                return;
            }
            if (controller.IsClimbing)
            {
                Drop();   // hands on the rungs
                return;
            }
            // Input is off in menus; right after a cursor lock the press belongs to the click that locked it; and the
            // press that picked something up (an Interact without a Hold) is not also the press that drops it.
            if (!controller.InputEnabled || controller.PressesSuppressed || Time.frameCount == pickedUpFrame)
            {
                return;
            }

            if (inspectAction != null && inspectAction.WasPressedThisFrame())
            {
                if (inspecting)
                {
                    StopInspecting();
                }
                else
                {
                    StartInspecting();
                }
                return;
            }
            if (inspecting)
            {
                TurnInspected();
            }

            InputAction interact = interactor.InteractAction;
            if (interact != null && interact.WasPressedThisFrame())
            {
                interactor.IgnoreCurrentPress();   // the press that drops is not also an interaction with what is behind
                Drop();
                return;
            }

            // Nothing is thrown mid-inspection: the object is right in front of the face, and the hands are busy.
            if (CanThrow && !inspecting && throwAction.WasPressedThisFrame())
            {
                // A click on a freed cursor re-locks it; it does not throw.
                bool freeMouse = throwAction.activeControl?.device is Mouse && Cursor.lockState != CursorLockMode.Locked;
                if (!freeMouse)
                {
                    Throw();
                }
            }
        }

        private void FixedUpdate()
        {
            RestorePendingCollisions(force: false);
            if (carried == null)
            {
                return;
            }
            if (body == null || body.isKinematic || !carried.isActiveAndEnabled)
            {
                Drop();   // destroyed, switched off or made kinematic by something else
                return;
            }

            // Unity forgets an ignored pair when either collider is switched off and on, and the controller switches
            // the capsule off for mantles and teleports, so the ignore is renewed every step while carrying.
            for (int i = 0; i < colliders.Length; i++)
            {
                SetIgnored(colliders[i], true);
            }

            Vector3 error = HoldPoint() - body.worldCenterOfMass;
            if (error.magnitude <= breakDistance)
            {
                arrived = true;
            }
            else if (arrived || Time.time - pickedUpTime > ArrivalTime)
            {
                Drop();   // stuck behind something while the player moved on, or never got to the hands at all
                return;
            }

            // Move with the player (feed-forward) and close the remaining gap at the responsiveness, never overshooting
            // within one physics step.
            float stiffness = Mathf.Min(holdResponsiveness, 1f / Time.fixedDeltaTime);
            body.linearVelocity = controller.Velocity + Vector3.ClampMagnitude(error * stiffness, maxHoldSpeed);

            // Keep the orientation it had relative to where the player faces.
            Quaternion turn = transform.rotation * heldRotation * Quaternion.Inverse(body.rotation);
            turn.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f)
            {
                angle -= 360f;
            }
            body.angularVelocity = float.IsInfinity(axis.x) || Mathf.Abs(angle) < 0.01f
                ? Vector3.zero
                : axis * (angle * Mathf.Deg2Rad * stiffness);
        }

        // Turns the inspected object by the look input, about the view's axes: the side facing the player follows the
        // input, so right turns it to the right and up tips it up.
        private void TurnInspected()
        {
            if (lookAction == null)
            {
                return;
            }
            Vector2 look = lookAction.ReadValue<Vector2>();
            if (look == Vector2.zero)
            {
                return;
            }

            InputDevice device = lookAction.activeControl?.device;
            if (device is Pointer)
            {
                if (device is Mouse && Cursor.lockState != CursorLockMode.Locked)
                {
                    return;   // a free mouse is pointing at things, not turning the object
                }
                look *= inspectMouseSpeed;   // pixels per frame, already frame-rate independent
            }
            else
            {
                look *= inspectStickSpeed * Time.unscaledDeltaTime;   // unscaled, like the controller's stick look
            }

            Quaternion target = transform.rotation * heldRotation;
            target = Quaternion.AngleAxis(-look.x, eyes.up) * Quaternion.AngleAxis(look.y, eyes.right) * target;
            heldRotation = Quaternion.Inverse(transform.rotation) * target;
        }

        private Vector3 HoldPoint()
        {
            // In front of the camera, pulled in when something on the controller's Environment Layers is in the way (the
            // carried object itself is on a layer those always leave out), so walking up to a wall presses the object to
            // it instead of dropping it.
            float distance = (inspecting ? inspectDistance : holdDistance) + heldRadius;
            float probeRadius = Mathf.Min(heldRadius, 0.25f);
            if (Physics.SphereCast(eyes.position, probeRadius, eyes.forward, out RaycastHit hit, distance,
                                   controller.EnvironmentMask, QueryTriggerInteraction.Ignore))
            {
                distance = Mathf.Max(MinHoldDistance, hit.distance + probeRadius - heldRadius);
            }
            return eyes.position + eyes.forward * distance;
        }

        private void Release(bool thrown)
        {
            if (carried == null)
            {
                return;
            }

            StopInspecting();   // first, so the player can move again and the event still has the object

            Carryable item = carried;
            Rigidbody released = body;
            carried = null;
            body = null;

            if (released != null)
            {
                released.useGravity = originalUseGravity;
                released.interpolation = originalInterpolation;
                released.collisionDetectionMode = originalCollisionDetection;

                // A body made kinematic by something else (a socket snapping it in) keeps its velocity to itself.
                if (!released.isKinematic)
                {
                    Vector3 playerVelocity = controller.Velocity;
                    released.linearVelocity = playerVelocity + Vector3.ClampMagnitude(released.linearVelocity - playerVelocity, MaxDropExtraSpeed);
                    if (thrown)
                    {
                        float speed = Mathf.Min(throwImpulse / Mathf.Max(released.mass, 0.01f), maxThrowSpeed);
                        released.linearVelocity += eyes.forward * speed;
                    }
                }
            }

            // Restored in reverse, so an object with several colliders ends on the layer read first.
            for (int i = colliders.Length - 1; i >= 0; i--)
            {
                if (colliders[i] != null)
                {
                    colliders[i].gameObject.layer = originalLayers[i];
                }
            }
            pending.Add(new PendingCollisions { Body = released, Colliders = colliders });
            colliders = null;
            RestorePendingCollisions(force: false);

            if (item != null)
            {
                item.NotifyReleased(thrown);
            }
            Dropped?.Invoke(item);
            if (thrown)
            {
                Thrown?.Invoke(item);
            }
        }

        private void RestorePendingCollisions(bool force)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Collider[] list = pending[i].Colliders;
                if (!force && OverlapsPlayer(list))
                {
                    continue;
                }
                foreach (Collider c in list)
                {
                    SetIgnored(c, false);
                }
                pending.RemoveAt(i);
            }
        }

        // Unity refuses (with an error) to set the ignore while either collider is switched off, and forgets it when
        // either is switched off and on, so an inactive pair is skipped: there is nothing to undo, and a carried
        // object's ignore is renewed every step once the capsule is back (after a mantle or a teleport).
        private void SetIgnored(Collider other, bool ignore)
        {
            if (IsActive(capsule) && IsActive(other))
            {
                Physics.IgnoreCollision(capsule, other, ignore);
            }
        }

        private static bool IsActive(Collider c)
        {
            return c != null && c.enabled && c.gameObject.activeInHierarchy;
        }

        private bool OverlapsPlayer(Collider[] list)
        {
            if (capsule == null)
            {
                return false;   // the player is being destroyed
            }
            Bounds player = capsule.bounds;
            player.Expand(capsule.skinWidth * 2f);
            foreach (Collider c in list)
            {
                if (IsActive(c) && c.bounds.Intersects(player))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
