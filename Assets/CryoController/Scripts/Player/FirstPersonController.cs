using UnityEngine;
using UnityEngine.InputSystem;

namespace CryoController
{
    /// <summary>
    /// First-person player controller: walk, sprint and crouch with acceleration and air control, jump with coyote time
    /// and a jump buffer, gravity, sliding down steep slopes, mantling onto ledges, climbing <see cref="Ladder"/>s,
    /// pushing rigidbodies, mouse look (optional smoothing) and gamepad look (response curve, acceleration), head bob and
    /// camera feel (landing dip, eased steps, sprint field of view).
    /// Built on <see cref="CharacterController"/> and driven by the Input System's project-wide actions
    /// (Player/Move, Look, Jump, Sprint, Crouch). Escape frees the cursor and a left click locks it again.
    ///
    /// Needs no Inspector wiring. Put it on a GameObject whose CharacterController has its pivot at the feet
    /// (center.y = height / 2) and a child Camera placed at eye height: standing height and eye height are read
    /// from that setup when the game starts. Everything runs in Update because CharacterController is not a
    /// rigidbody. This component owns the body's yaw, the camera's local position, pitch and field of view.
    ///
    /// Other systems can subscribe to <see cref="Jumped"/>, <see cref="Landed"/>, <see cref="Footstep"/> and
    /// <see cref="Mantled"/> for audio and effects, read the Is* properties, pause player input with
    /// <see cref="InputEnabled"/> (or only movement and look with <see cref="MoveAndLookEnabled"/>), veto sprinting
    /// with <see cref="SprintAllowed"/> (see PlayerStamina), change the look and camera settings at runtime (see
    /// PlayerPreferences) and swap the whole movement feel with <see cref="Profile"/> (see <see cref="MovementProfile"/>).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/First Person Controller")]
    public partial class FirstPersonController : MonoBehaviour
    {
        // The class is split by topic. This file holds the settings, the public state and events, the Unity messages
        // that run each frame, and the public helpers. The rest is in FirstPersonController.Input.cs, .Look.cs,
        // .Movement.cs, .MovingGround.cs, .Crouch.cs, .Mantle.cs, .Ladders.cs, .Lean.cs, .Camera.cs and .Profile.cs.

        // Look input is ignored for this many frames after the cursor is locked or the window regains focus,
        // because the OS can report one large pointer delta when the cursor is warped to the window center.
        private const int LookSuppressFrameCount = 2;

        // Longest mouse smoothing window (frames); also the size of the delta history.
        private const int MaxMouseSmoothing = 8;

        // Capsule heights closer than this are treated as equal (metres).
        private const float HeightEpsilon = 0.001f;

        // How far the CharacterController center may be from height / 2 before the pivot layout is questioned (metres).
        private const float PivotTolerance = 0.01f;

        [Header("Input")]
        [Tooltip("Optional. Leave empty to use the project-wide actions asset (InputSystem.actions). Either way the " +
                 "controller works on a private runtime copy of the asset: the actions it uses (Move, Look, Jump, " +
                 "Sprint, Crouch) are enabled on the copy while the component is enabled, and rebinding changes the " +
                 "copy, never the asset in the project.")]
        [SerializeField] private InputActionAsset actionsOverride;

        [Header("Profile")]
        [Tooltip("Optional. A Movement Profile's settings replace this component's movement, acceleration, jump and " +
                 "gravity, slope, mantling, ladder, crouch speed, power slide, head bob, camera feel, zoom and lean " +
                 "settings while the game runs. Swap it at runtime through Profile; empty uses the settings below.")]
        [SerializeField] private MovementProfile profile;

        [Header("Movement (metres per second)")]
        [Tooltip("Speed while walking.")]
        [SerializeField, Min(0f)] private float walkSpeed = 4f;
        [Tooltip("Speed while Sprint is held. Sprinting is not possible while crouched.")]
        [SerializeField, Min(0f)] private float sprintSpeed = 7f;
        [Tooltip("Speed while crouched.")]
        [SerializeField, Min(0f)] private float crouchSpeed = 2f;

        [Header("Acceleration (metres per second squared)")]
        [Tooltip("How quickly the player reaches the target speed on the ground. Higher is snappier.")]
        [SerializeField, Min(0.01f)] private float groundAcceleration = 40f;
        [Tooltip("How quickly the player stops on the ground when there is no input.")]
        [SerializeField, Min(0.01f)] private float groundDeceleration = 60f;
        [Tooltip("How quickly movement input can change the velocity while airborne. Momentum from the ground is kept " +
                 "and there is no air drag; lower values give less air control.")]
        [SerializeField, Min(0f)] private float airControl = 6f;

        [Header("Jump and gravity")]
        [Tooltip("Peak height of a jump, in metres.")]
        [SerializeField, Min(0f)] private float jumpHeight = 1.2f;
        [Tooltip("Downward acceleration in m/s^2, as a positive number. Stronger than real gravity (9.81) feels less floaty.")]
        [SerializeField, Min(0f)] private float gravity = 20f;
        [Tooltip("Coyote time: seconds after walking off a ledge during which a jump is still accepted, so a press that " +
                 "lands a frame late does not fall flat. 0 disables it.")]
        [SerializeField, Min(0f)] private float coyoteTime = 0.12f;
        [Tooltip("Jump buffer: seconds before landing during which a jump press is remembered and performed the moment " +
                 "the player can jump again. 0 disables it (the press then only counts on a frame the player is grounded).")]
        [SerializeField, Min(0f)] private float jumpBufferTime = 0.12f;
        [Tooltip("Extra jumps in mid-air before landing: 1 is a double jump. A press in the air after coyote time uses one. " +
                 "0 disables.")]
        [SerializeField, Min(0)] private int airJumps;
        [Tooltip("How high an air jump rises from where it starts, in metres. It cancels any fall speed but never " +
                 "slows a faster rise.")]
        [SerializeField, Min(0f)] private float airJumpHeight = 1f;

        [Header("Slopes")]
        [Tooltip("Top speed, in m/s, when sliding down a surface steeper than the CharacterController's Slope Limit. " +
                 "The slide accelerates like gravity along the slope and the player cannot jump while sliding.")]
        [SerializeField, Min(0f)] private float maxSlideSpeed = 8f;
        [Tooltip("How much movement input still steers while sliding: 0 = none, 1 = full walking control.")]
        [SerializeField, Range(0f, 1f)] private float slideControl = 0.25f;

        [Header("Mantling")]
        [Tooltip("Ledges up to this high above the feet, in metres, can be climbed by pushing forward at them while " +
                 "jumping or falling past them. 0 disables mantling.")]
        [SerializeField, Min(0f)] private float mantleMaxHeight = 1.6f;
        [Tooltip("Ledges up to this high are climbed just by walking into them while pushing forward, no jump needed. " +
                 "Ledges below the CharacterController's Step Offset are simply stepped up.")]
        [SerializeField, Min(0f)] private float autoMantleHeight = 0.9f;
        [Tooltip("Seconds a climb takes for a ledge at Mantle Max Height; lower ledges are proportionally quicker.")]
        [SerializeField, Min(0.05f)] private float mantleDuration = 0.45f;

        [Header("Ladders")]
        [Tooltip("Climbing speed on ladders, in metres per second.")]
        [SerializeField, Min(0f)] private float climbSpeed = 3f;
        [Tooltip("Speed the player is pushed away from a ladder when jumping off it, in metres per second.")]
        [SerializeField, Min(0f)] private float ladderDismountSpeed = 3f;

        [Header("Swimming")]
        [Tooltip("Swimming speed in a Water Volume, in metres per second. Sprint swims faster, by Sprint Speed over Walk " +
                 "Speed. 0 never swims: water is walked through.")]
        [SerializeField, Min(0f)] private float swimSpeed = 3f;
        [Tooltip("How quickly the swimming speed changes, in metres per second squared. Water is slower to start and " +
                 "stop in than the ground.")]
        [SerializeField, Min(0.1f)] private float swimAcceleration = 6f;
        [Tooltip("How deep the water must be at the player's feet before they swim, in metres. Shallower water is waded " +
                 "through.")]
        [SerializeField, Min(0.1f)] private float swimDepth = 1.1f;
        [Tooltip("How far the eyes stay above the surface while floating, in metres.")]
        [SerializeField] private float floatHeight = 0.15f;
        [Tooltip("The highest edge above the water the player can climb out onto by swimming into it, in metres.")]
        [SerializeField, Min(0f)] private float climbOutHeight = 0.7f;

        [Header("Look")]
        [Tooltip("Degrees of rotation per pixel of mouse (or touch) movement.")]
        [SerializeField, Min(0f)] private float mouseSensitivity = 0.05f;
        [Tooltip("Frames of mouse smoothing: the look delta is averaged over this many frames. 0 is raw input and the " +
                 "best default; 2-4 softens jitter at low frame rates at the cost of a little lag.")]
        [SerializeField, Range(0, MaxMouseSmoothing)] private int mouseSmoothing;
        [Tooltip("Degrees per second at full gamepad stick deflection.")]
        [SerializeField, Min(0f)] private float stickSensitivity = 150f;
        [Tooltip("How far up or down the camera can look, in degrees.")]
        [SerializeField, Range(0f, 89f)] private float maxPitch = 85f;
        [Tooltip("Invert vertical look.")]
        [SerializeField] private bool invertY;
        [Tooltip("Gamepad stick response curve exponent: 1 is linear, 2 keeps small deflections slow for fine aiming " +
                 "while the edge of the stick still gives full speed.")]
        [SerializeField, Range(1f, 4f)] private float stickCurveExponent = 2f;
        [Tooltip("Vertical stick look speed relative to horizontal; gamepads usually want less.")]
        [SerializeField, Range(0.1f, 2f)] private float stickVerticalScale = 0.7f;
        [Tooltip("Turn speed multiplier reached when the stick is held at full deflection, so a full push swings the " +
                 "view around without making small aims twitchy. 1 disables.")]
        [SerializeField, Range(1f, 4f)] private float stickAccelerationBoost = 2f;
        [Tooltip("Seconds of full deflection before the boost starts ramping up.")]
        [SerializeField, Min(0f)] private float stickAccelerationDelay = 0.25f;
        [Tooltip("Seconds over which the boost ramps from 1 to Stick Acceleration Boost once it starts.")]
        [SerializeField, Min(0.01f)] private float stickAccelerationRamp = 0.75f;

        [Header("Crouch")]
        [Tooltip("Capsule height while crouched, in metres. Standing height is read from the CharacterController. " +
                 "Clamped at runtime to at least twice the capsule radius.")]
        [SerializeField, Min(0f)] private float crouchHeight = 1f;
        [Tooltip("How fast the capsule shrinks or grows, in metres per second.")]
        [SerializeField, Min(0.01f)] private float crouchTransitionSpeed = 6f;
        [Tooltip("Off: crouch while Crouch is held. On: each press toggles crouching. Toggling off under a ceiling keeps " +
                 "the player crouched until there is room to stand, then stands up without another press.")]
        [SerializeField] private bool crouchToggle;

        [Header("Power slide")]
        [Tooltip("Crouching while sprinting on the ground starts a slide: the player drops low and keeps their momentum, " +
                 "slowing by Power Slide Friction. Off: crouching while sprinting just crouches.")]
        [SerializeField] private bool powerSlide = true;
        [Tooltip("Speed added when a slide starts, in m/s, so it feels like a push off.")]
        [SerializeField, Min(0f)] private float powerSlideBoost = 2f;
        [Tooltip("Deceleration while sliding on flat ground, in m/s^2. Slopes add or take away gravity's share.")]
        [SerializeField, Min(0.1f)] private float powerSlideFriction = 6f;
        [Tooltip("The slide ends below this speed, in m/s, leaving the player crouched.")]
        [SerializeField, Min(0f)] private float powerSlideMinSpeed = 2.5f;
        [Tooltip("How fast a slide turns at full sideways input, in degrees per second.")]
        [SerializeField, Min(0f)] private float powerSlideSteering = 45f;

        [Header("Head bob")]
        [Tooltip("Bob the camera while walking on the ground. Fades in and out with movement.")]
        [SerializeField] private bool headBob = true;
        [Tooltip("Vertical bob amplitude, in metres. One bounce per step.")]
        [SerializeField, Min(0f)] private float headBobAmplitude = 0.03f;
        [Tooltip("Sideways sway amplitude, in metres. One sway left and right per two steps (a figure of eight).")]
        [SerializeField, Min(0f)] private float headBobSway = 0.02f;
        [Tooltip("Distance travelled per step, in metres; also the interval of the Footstep event. Bob speed follows " +
                 "movement speed, so sprinting bobs faster and crouching slower. Shorter steps bob faster.")]
        [SerializeField, Min(0.01f)] private float headBobStepLength = 2f;

        [Header("Camera feel")]
        [Tooltip("Camera dip on landing, in metres per m/s of impact speed above 4 m/s (about a 0.4 m drop; a 1.2 m " +
                 "jump lands at 6.9 m/s). 0 disables.")]
        [SerializeField, Min(0f)] private float landingDip = 0.015f;
        [Tooltip("Ease the camera up over stairs and ledges instead of snapping to the new height when the " +
                 "CharacterController steps up.")]
        [SerializeField] private bool smoothSteps = true;
        [Tooltip("Field of view added while sprinting, in degrees. 0 disables and leaves the field of view to other " +
                 "scripts.")]
        [SerializeField, Min(0f)] private float sprintFovBoost = 6f;
        [Tooltip("Camera roll at walking speed sideways, in degrees, leaning into the strafe. 0 disables.")]
        [SerializeField, Range(0f, 10f)] private float strafeTilt = 1.5f;
        [Tooltip("Comfort setting for players prone to motion sickness: turns off head bob, the landing dip, strafe tilt, " +
                 "the lean's roll and the sprint field-of-view boost, and scales camera shakes by Reduced Motion Shake. " +
                 "Movement itself is unchanged. Players can switch it in the pause menu's options.")]
        [SerializeField] private bool reduceCameraMotion;

        [Header("Camera shake")]
        [Tooltip("Camera rotation at full shake, in degrees: pitch and yaw swing up to this, roll up to half of it. " +
                 "Shakes come from AddCameraShake (and Player Fall Damage). 0 disables the rotation.")]
        [SerializeField, Min(0f)] private float shakeAngle = 3f;
        [Tooltip("Camera movement at full shake, in metres. 0 disables the movement.")]
        [SerializeField, Min(0f)] private float shakeOffset = 0.03f;
        [Tooltip("How fast a shake jitters, in cycles per second: low is a slow sway, high a rattle.")]
        [SerializeField, Min(0.1f)] private float shakeFrequency = 15f;
        [Tooltip("How fast a shake dies away: the shake amount lost per second, where 1 is a full shake.")]
        [SerializeField, Min(0.01f)] private float shakeRecovery = 1.5f;
        [Tooltip("How much of a shake is left with Reduce Camera Motion on: 0 none, 1 all of it. Raise it if your shakes " +
                 "tell the player something they need to know.")]
        [SerializeField, Range(0f, 1f)] private float reducedMotionShake;

        [Header("Zoom")]
        [Tooltip("Field of view while Zoom is held, as a fraction of the normal one: 0.6 narrows it to 60 %. Look " +
                 "sensitivity scales down with it and sprinting stops. Needs a Player/Zoom action (the shipped actions " +
                 "bind the right mouse button and the left trigger). 1 disables.")]
        [SerializeField, Range(0.1f, 1f)] private float zoomFovScale = 0.6f;
        [Tooltip("Seconds the zoom takes to blend in or out.")]
        [SerializeField, Min(0.01f)] private float zoomTime = 0.15f;

        [Header("Lean")]
        [Tooltip("How far the head moves sideways at full lean, in metres, to peek round corners. Needs Player/LeanLeft " +
                 "and Player/LeanRight actions (the shipped actions bind Q and E, and the gamepad's shoulder buttons). " +
                 "The head never goes into a wall. 0 disables.")]
        [SerializeField, Min(0f)] private float leanDistance = 0.35f;
        [Tooltip("Camera roll at full lean, in degrees. The head swings about a pivot low in the body, so it also dips a little.")]
        [SerializeField, Range(0f, 30f)] private float leanAngle = 12f;
        [Tooltip("Seconds a lean takes to reach its side.")]
        [SerializeField, Min(0.01f)] private float leanTime = 0.12f;
        [Tooltip("Off: lean while the key is held. On: each press leans to that side, or back upright if already there.")]
        [SerializeField] private bool leanToggle;

        [Header("Physics")]
        [Tooltip("Force in newtons applied to non-kinematic rigidbodies the player walks into, so props can be shoved " +
                 "out of the way. 0 disables pushing (loose props can then be climbed). Things the player stands on are " +
                 "never pushed.")]
        [SerializeField, Min(0f)] private float pushForce = 40f;
        [Tooltip("Layers the controller's own checks treat as solid: the ground under the player, ledges to mantle, " +
                 "headroom to stand up, walls to lean against, and what holds a carried object back. Leave out layers of " +
                 "things that should not count, such as foliage. The CharacterController itself still collides by the " +
                 "physics layer matrix. Ignore Raycast is always left out, since carried objects sit on it.")]
        [SerializeField] private LayerMask environmentLayers = Physics.DefaultRaycastLayers;

        [Header("Cursor")]
        [Tooltip("Lock and hide the cursor while this component is enabled. Escape releases it and a left click locks " +
                 "it again. Mouse look only works while the cursor is locked.")]
        [SerializeField] private bool manageCursor = true;
        [Tooltip("Play with a gamepad only: the mouse and other pointers are ignored (no mouse look, no clicks, in the " +
                 "game and the pause menu) and the pointer stays hidden. The keyboard still works. Players can switch it " +
                 "in the pause menu's options.")]
        [SerializeField] private bool controllerOnly;

        /// <summary>True when the CharacterController touched the ground during its last move.</summary>
        public bool IsGrounded { get; private set; }

        /// <summary>True while the capsule is shorter than its standing height (crouch held, or blocked by a ceiling).</summary>
        public bool IsCrouching { get; private set; }

        /// <summary>True while sprinting (Sprint held, moving, not crouched).</summary>
        public bool IsSprinting { get; private set; }

        /// <summary>True while sliding down a surface steeper than the CharacterController's slope limit.</summary>
        public bool IsSliding { get; private set; }

        /// <summary>True while climbing onto a ledge; movement input and gravity are suspended for the climb.</summary>
        public bool IsMantling { get; private set; }

        /// <summary>True while on a ladder.</summary>
        public bool IsClimbing { get; private set; }

        /// <summary>True while Zoom is held and zooming is possible.</summary>
        public bool IsZooming { get; private set; }

        /// <summary>True while the player is power sliding (crouched while sprinting, keeping the momentum).</summary>
        public bool IsPowerSliding { get; private set; }

        /// <summary>Air jumps left before the player lands again.</summary>
        public int AirJumpsLeft => Mathf.Max(0, airJumps - airJumpsUsed);

        /// <summary>
        /// How much the camera is shaking, from 0 (still) to 1 (a full shake). <see cref="AddCameraShake"/> raises it and
        /// it dies away by Shake Recovery per second.
        /// </summary>
        public float CameraShake { get; private set; }

        /// <summary>How far the player leans: -1 fully left, 0 upright, 1 fully right.</summary>
        public float LeanAmount { get; private set; }

        /// <summary>True when the last move or look input came from a gamepad; for button prompts.</summary>
        public bool UsingGamepad => LastInputDevice is Gamepad;

        /// <summary>The device the last move or look input came from, or null before any; for rumble and prompts.</summary>
        public InputDevice LastInputDevice { get; private set; }

        /// <summary>
        /// While false, movement, look, jump, sprint and crouch input is ignored (a toggled crouch stays as it is);
        /// gravity, sliding and cursor handling continue. For menus, dialogue and cutscenes that keep the player in
        /// the world. Disable the component instead to freeze the player completely.
        /// </summary>
        public bool InputEnabled { get; set; } = true;

        /// <summary>
        /// While false, movement and look input is ignored as with <see cref="InputEnabled"/> (jump, sprint, crouch, zoom
        /// and lean too), but the game carries on around it: the cursor, prompts and other components still work. For a
        /// moment the player holds still with control elsewhere, such as <see cref="PlayerCarry"/> turning an inspected
        /// object with the look input.
        /// </summary>
        public bool MoveAndLookEnabled { get; set; } = true;

        // Whether movement and look input count this frame.
        private bool ControlsActive => InputEnabled && MoveAndLookEnabled;

        /// <summary>Raised when a jump starts.</summary>
        public event System.Action Jumped;

        /// <summary>
        /// Raised when the player lands after being airborne, with the downward speed at impact in m/s. Stepping off
        /// a stair lands at 3-4 m/s, a 1.2 m jump at about 6.9 m/s, a 3 m fall at about 11 m/s. Drops shorter than
        /// about 0.15 m are not reported.
        /// </summary>
        public event System.Action<float> Landed;

        /// <summary>Raised each time a foot lands while walking, sprinting or crouching on the ground.</summary>
        public event System.Action Footstep;

        /// <summary>Raised when a climb onto a ledge starts, with the ledge height above the feet in metres.</summary>
        public event System.Action<float> Mantled;

        /// <summary>Raised when a power slide starts.</summary>
        public event System.Action PowerSlideStarted;

        /// <summary>Raised when a power slide ends, whatever ended it.</summary>
        public event System.Action PowerSlideEnded;

        /// <summary>World velocity in metres per second, everything included: movement, climbs and moving ground.</summary>
        public Vector3 Velocity { get; private set; }

        /// <summary>The collider the player is standing on, or null in the air, on a ladder or mid-mantle.</summary>
        public Collider Ground { get; private set; }

        /// <summary>
        /// Velocity of the ground under the player, in metres per second: non-zero on a moving platform. It is part of
        /// <see cref="Velocity"/>; subtract it for the player's own movement.
        /// </summary>
        public Vector3 GroundVelocity { get; private set; }

        /// <summary>
        /// The input actions in use: a private runtime copy of Actions Override or of the project-wide asset, so that
        /// enabling and rebinding never touch the asset in the project. Available after Awake.
        /// </summary>
        public InputActionAsset Actions { get; private set; }

        /// <summary>Set false (by a stamina system, for example) to stop sprinting; true by default.</summary>
        public bool SprintAllowed { get; set; } = true;

        /// <summary>Mouse look sensitivity in degrees per pixel. Runtime access for options menus.</summary>
        public float MouseSensitivity
        {
            get { return mouseSensitivity; }
            set { mouseSensitivity = Mathf.Max(0f, value); }
        }

        /// <summary>Gamepad look sensitivity in degrees per second at full deflection.</summary>
        public float StickSensitivity
        {
            get { return stickSensitivity; }
            set { stickSensitivity = Mathf.Max(0f, value); }
        }

        /// <summary>Invert vertical look.</summary>
        public bool InvertY
        {
            get { return invertY; }
            set { invertY = value; }
        }

        /// <summary>Layers the controller's own checks treat as solid; see the Environment Layers tooltip.</summary>
        public LayerMask EnvironmentLayers
        {
            get { return environmentLayers; }
            set { environmentLayers = value; }
        }

        // The mask every environment check uses: Environment Layers without the layer carried objects sit on.
        internal int EnvironmentMask => environmentLayers.value & ~(1 << PlayerCarry.CarriedLayer);

        /// <summary>
        /// Comfort setting: no head bob, landing dip, strafe tilt, lean roll or sprint field-of-view boost, and camera
        /// shakes scaled by Reduced Motion Shake. The camera settles to rest over a moment when it is switched on.
        /// </summary>
        public bool ReduceCameraMotion
        {
            get { return reduceCameraMotion; }
            set { reduceCameraMotion = value; }
        }

        /// <summary>
        /// Gamepad-only play: the mouse and other pointers are left out of <see cref="Actions"/> (no mouse look and no
        /// clicks for any component using them), the pause menu ignores them too, and the pointer stays hidden while this
        /// component manages the cursor. The keyboard still works.
        /// </summary>
        public bool ControllerOnly
        {
            get { return controllerOnly; }
            set
            {
                controllerOnly = value;
                ApplyControllerOnly();
            }
        }

        /// <summary>The camera's base field of view in degrees; the sprint boost is added on top of it.</summary>
        public float FieldOfView
        {
            get { return baseFieldOfView; }
            set
            {
                float previous = baseFieldOfView;
                baseFieldOfView = Mathf.Clamp(value, 1f, 179f);
                unzoomedFieldOfView += baseFieldOfView - previous;   // a new base shows at once; blends continue from it
                if (playerCamera != null && !DrivesFieldOfView)
                {
                    playerCamera.fieldOfView = baseFieldOfView;   // otherwise UpdateCamera applies it
                }
            }
        }

        // Components found automatically in Awake.
        private CharacterController controller;
        private Transform cameraTransform;

        // Actions looked up by name from the actions asset.
        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction jumpAction;
        private InputAction sprintAction;
        private InputAction crouchAction;
        private InputAction zoomAction;   // optional
        private InputAction leanLeftAction;   // optional
        private InputAction leanRightAction;  // optional
        private InputActionAsset runtimeActions; // the private copy of the actions asset, destroyed with this component

        private MovementProfile authoredSettings; // the settings as authored on this component, for Profile = null
        private MovementProfile appliedProfile;   // the profile last applied (null: the authored settings)
        private int appliedProfileVersion;        // its Version when applied, to notice Inspector edits in Play Mode

        private float standingHeight;     // CharacterController height as set in the scene
        private float eyeOffsetFromTop;   // distance from the top of the capsule down to the camera
        private float yaw;                // body rotation around the up axis, in degrees
        private float pitch;              // camera tilt in degrees (negative = looking up)
        private int lookSuppressFrames;   // see LookSuppressFrameCount
        private float stickFullTime;      // seconds the look stick has been held at full deflection (for the boost)
        private readonly Vector2[] mouseDeltaHistory = new Vector2[MaxMouseSmoothing];   // ring buffer for smoothing
        private int mouseHistoryIndex;    // next slot to write
        private int mouseHistoryCount;    // slots filled so far, up to MaxMouseSmoothing
        private float verticalVelocity;   // m/s, negative = falling
        private bool wasGrounded;         // grounded state at the start of the previous UpdateMovement
        private bool cursorLockedByUs;    // so OnDisable only releases a lock this component took
        private bool crouchToggled;       // crouch-toggle mode: whether the player currently wants to be crouched
        private bool crouchHeld;          // the crouch UpdateCrouch settled on this frame (wanted, or forced by a ceiling)
        private Vector3 eyeLocalPosition; // camera position without head bob; crouching moves it, head bob is added on top
        private float bobPhase;           // head bob position in steps, wrapped to a two-step cycle
        private float bobWeight;          // 0..1 fade of the head bob offset
        private Camera playerCamera;
        private float baseFieldOfView;    // the camera's authored field of view; the sprint boost is added to it
        private float unzoomedFieldOfView; // base plus the blended sprint boost, before the zoom scales it
        private float zoomBlend;          // 0..1, how far the zoom is in
        private float roll;               // camera roll from the strafe tilt, in degrees
        private int leanToggled;          // lean-toggle mode: -1 left, 0 upright, 1 right
        private float leanVelocity;       // SmoothDamp state of LeanAmount
        private Vector3 leanOffset;       // camera offset of the lean, in the body's space
        private float leanRoll;           // camera roll of the lean, in degrees
        private float landingDipOffset;   // vertical camera offset of the landing dip spring (metres, <= 0)
        private float landingDipVelocity; // its velocity (m/s)
        private static int shakeSeedCount; // players started so far, to give each its own stretch of shake noise
        private float shakeSeed;          // where this player's shake noise starts, so two players shake differently
        private float shakeTime;          // position along the shake noise, in shake cycles
        private Vector3 shakeEuler;       // camera rotation of the shake this frame (pitch, yaw, roll), in degrees
        private Vector3 shakePosition;    // camera offset of the shake this frame, in the camera's parent space
        private Vector2 kickOffset;       // camera kick this frame (pitch, yaw), in degrees; see AddCameraKick
        private Vector2 kickVelocity;     // its rate of change, in degrees per second
        private float stepSmoothOffset;   // vertical camera lag after stepping up (metres, <= 0), eased back to 0
        private float slideSpeed;         // m/s along slideDirection; builds up on steep slopes, bleeds off elsewhere
        private Vector3 slideDirection;   // downhill direction of the last steep slope, flattened once off it
        private Vector3 horizontalVelocity; // m/s, world space; accelerates towards the input-driven target velocity
        private Vector3 mantleStart;      // feet position when the climb began
        private Vector3 mantleEnd;        // feet position on the ledge
        private Transform mantleLedge;    // what the ledge belongs to, so the climb follows it when it moves
        private Vector3 mantleEndLocal;   // mantleEnd in mantleLedge's space
        private bool groundedStale;       // the CharacterController's grounded flag predates a teleport: ignore it once
        private InputDevice lastLookDevice; // the device of the last look input, for the frames between mouse deltas
        private bool droveFieldOfView;    // UpdateCamera wrote the field of view last frame
        private float mantleTime;         // seconds into the climb
        private float mantleTotal;        // seconds the climb takes
        private readonly Collider[] overlapBuffer = new Collider[8];   // for the room check on top of a ledge
        private Ladder ladderInReach;     // the ladder whose zone the capsule is in, climbing or not
        private WaterVolume waterInReach; // the water volume the capsule is in, swimming or not
        private bool onScreenControls;    // see OnScreenControls
        private Ladder ladder;            // the ladder being climbed
        private float ladderCooldownUntil; // Time.time before which a ladder is not grabbed again
        private float climbedSinceRung;   // metres climbed since the last rung footstep
        private Vector3 measuredVelocity; // m/s, world: what the movement Move actually achieved this frame, moving ground excluded
        private Vector3 previousPosition; // at the end of the last Update, for Velocity
        private Collider frameGroundCollider; // the most floor-like contact among this frame's Move calls, or null
        private float frameGroundNormalY; // its normal.y
        private Transform groundTransform; // the ground being ridden, or null; see RideGround
        private Vector3 groundLocalPoint; // the player's position in the ground's local space after the last move
        private Vector3 groundPosition;   // the ground's pose after the last move: unchanged means it did not move
        private Quaternion groundRotation;
        private bool riding;              // inside RideGround's Move: its contacts are the ground, never something to push
        private bool groundChanged;       // the ground is new since the last ride: its carry is not yet measured
        private Vector3 airCarry;         // m/s, planar: what the ground was carrying when the player left it

        // Time.time stamps for coyote time and jump buffering. NegativeInfinity means "never", so no jump can be
        // accepted before the player has stood on the ground once, and a consumed request cannot fire twice.
        private float lastGroundedTime = float.NegativeInfinity;
        private float jumpRequestedTime = float.NegativeInfinity;
        private int airJumpsUsed;          // since the player last stood on the ground or grabbed a ladder

        // ------------------------------------------------------------------ Unity lifecycle

        // With domain reloading off (Enter Play Mode Settings), statics keep their values from the last play: each play
        // starts counting players afresh, as it does with a reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            shakeSeedCount = 0;
        }

        private void Awake()
        {
            InitializeProfile();
            shakeSeed = shakeSeedCount++ % 1024 * 1.618f;
            controller = GetComponent<CharacterController>();

            Camera childCamera = GetComponentInChildren<Camera>(true);
            if (childCamera == null)
            {
                Debug.LogError("FirstPersonController: needs a Camera on a child object (the player's eyes). Disabling.", this);
                enabled = false;
                return;
            }
            if (childCamera.transform == transform)
            {
                // GetComponentInChildren also searches this object; a camera here would have its yaw overwritten by pitch.
                Debug.LogError("FirstPersonController: the Camera must be on a child object, not on the player itself. Disabling.", this);
                enabled = false;
                return;
            }
            cameraTransform = childCamera.transform;
            eyeLocalPosition = cameraTransform.localPosition;
            playerCamera = childCamera;
            baseFieldOfView = childCamera.fieldOfView;
            unzoomedFieldOfView = baseFieldOfView;

            // Remember how the scene set us up, so crouching can restore it. The eye offset is measured from the
            // capsule's actual top, so it stays right even if the pivot is not at the feet.
            standingHeight = controller.height;
            float standingTop = controller.center.y + standingHeight * 0.5f;
            eyeOffsetFromTop = standingTop - cameraTransform.localPosition.y;
            if (Mathf.Abs(controller.center.y - standingHeight * 0.5f) > PivotTolerance)
            {
                Debug.LogWarning("FirstPersonController: expects the CharacterController pivot at the feet (center.y = height / 2). " +
                                 "The first crouch will move the capsule to that layout.", this);
            }

            // Start from the orientation authored in the scene. The body only ever yaws; the camera only pitches.
            yaw = transform.eulerAngles.y;
            pitch = Mathf.Clamp(NormalizeAngle(cameraTransform.localEulerAngles.x), -maxPitch, maxPitch);
            ApplyRotation();
            previousPosition = transform.position;

            if (!ResolveActions())
            {
                enabled = false;
            }
            FilterPointers();
#if UNITY_WEBGL && !UNITY_EDITOR
            if (manageCursor)
            {
                // In a browser, Escape frees the pointer and never reaches the game. A sticky lock would go on reporting
                // the cursor as locked; without it lockState follows the browser, so look stops while the mouse is free
                // and a click locks it again.
                WebGLInput.stickyCursorLock = false;
            }
#endif
        }

        private void OnEnable()
        {
            if (moveAction == null || lookAction == null)
            {
                return;   // Awake failed and logged why; a re-enabled component stays inert.
            }

            SetOwnedActionsEnabled(true);
            InputSystem.onDeviceChange += OnDeviceChange;
            lookSuppressFrames = LookSuppressFrameCount;

            // Something (a cutscene, a Timeline) may have turned or moved the player while this was off: carry on from
            // there rather than snapping back, and without a velocity spike on the first frame.
            yaw = transform.eulerAngles.y;
            pitch = Mathf.Clamp(NormalizeAngle(cameraTransform.localEulerAngles.x), -maxPitch, maxPitch);
            previousPosition = transform.position;
            mouseHistoryCount = 0;

            // The capsule keeps its height across a disable / enable cycle, so the toggle follows the capsule rather
            // than popping the player up or leaving the two out of step.
            crouchToggled = controller.height < standingHeight - HeightEpsilon;

            if (manageCursor)
            {
                LockCursor();
            }
        }

        private void OnDisable()
        {
            SetOwnedActionsEnabled(false);
            InputSystem.onDeviceChange -= OnDeviceChange;

            if (cursorLockedByUs)
            {
                UnlockCursor();
            }

            if (IsMantling)
            {
                EndMantle();   // never leave the CharacterController switched off; first, as it marks the player grounded
            }

            // Do not carry motion or pending jumps across a disable / enable cycle (pause menus, cutscenes).
            verticalVelocity = 0f;
            wasGrounded = false;
            IsGrounded = false;
            IsSprinting = false;
            IsSliding = false;
            EndPowerSlide();
            slideSpeed = 0f;
            stickFullTime = 0f;
            horizontalVelocity = Vector3.zero;
            measuredVelocity = Vector3.zero;
            Velocity = Vector3.zero;
            ClearGround();
            airJumpsUsed = 0;
            lastGroundedTime = float.NegativeInfinity;
            jumpRequestedTime = float.NegativeInfinity;
            if (IsClimbing)
            {
                IsClimbing = false;
                ladder = null;
            }
            IsSwimming = false;
            IsUnderwater = false;

            // Leave the camera at its resting position and field of view rather than frozen mid-bob or mid-dip.
            bobPhase = 0f;
            bobWeight = 0f;
            landingDipOffset = 0f;
            landingDipVelocity = 0f;
            stepSmoothOffset = 0f;
            zoomBlend = 0f;
            IsZooming = false;
            roll = 0f;
            LeanAmount = 0f;
            leanVelocity = 0f;
            leanToggled = 0;
            leanOffset = Vector3.zero;
            leanRoll = 0f;
            CameraShake = 0f;
            shakeEuler = Vector3.zero;
            shakePosition = Vector3.zero;
            kickOffset = Vector2.zero;
            kickVelocity = Vector2.zero;
            if (cameraTransform != null)
            {
                cameraTransform.localPosition = eyeLocalPosition;
                ApplyRotation();
                if (DrivesFieldOfView)
                {
                    unzoomedFieldOfView = baseFieldOfView;
                    playerCamera.fieldOfView = baseFieldOfView;
                }
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                lookSuppressFrames = LookSuppressFrameCount;
            }
        }

        private void NoteDevice(InputAction action)
        {
            InputDevice device = action.activeControl?.device;   // null while the action is not actuated
            if (device != null)
            {
                LastInputDevice = device;
            }
        }

        private void OnDestroy()
        {
            if (runtimeActions != null)
            {
                Destroy(runtimeActions);
            }
            if (authoredSettings != null)
            {
                Destroy(authoredSettings);
            }
        }

        /// <summary>
        /// True for the frames right after the cursor locks or the window regains focus. A button press then belongs to
        /// the click or key that did it (closing a menu, focusing the window), not to the game: resuming with Space does
        /// not jump. Components reading their own presses should skip them while this is true.
        /// </summary>
        public bool PressesSuppressed => lookSuppressFrames > 0;

        /// <summary>
        /// Starts a <see cref="PressesSuppressed"/> window now, as locking the cursor does: for a menu closing on a press
        /// that must not also act in the game.
        /// </summary>
        public void SuppressPresses()
        {
            lookSuppressFrames = LookSuppressFrameCount;
        }

        private void Update()
        {
#if UNITY_EDITOR
            FollowProfileEdits();
#endif
            if (moveAction == null || lookAction == null)
            {
                return;   // Awake failed; errors are already in the console.
            }

            UpdateCursor();
            UpdateZoom();
            if (InputEnabled)
            {
                NoteDevice(moveAction);
                NoteDevice(lookAction);
            }
            if (ControlsActive)
            {
                UpdateLook();
            }
            else
            {
                mouseHistoryCount = 0;   // mouse smoothing starts afresh when input comes back, without old deltas
                if (InputEnabled && lookSuppressFrames > 0)
                {
                    lookSuppressFrames--;   // UpdateLook counts it down; presses must come back while look is held too
                }
            }
            // Order matters: UpdateMovement uses the crouch state and capsule height set by UpdateCrouch, and
            // UpdateCamera uses the grounded state, velocity, landing and step-up detected by UpdateMovement.
            UpdateCrouch();
            if (IsMantling)
            {
                UpdateMantle();
            }
            else if (IsClimbing)
            {
                UpdateClimb();
            }
            else
            {
                if (!IsSwimming && ShouldSwim())
                {
                    StartSwimming();
                }
                if (IsSwimming)
                {
                    UpdateSwim();
                }
                else
                {
                    UpdateMovement();
                }
            }
            IsUnderwater = waterInReach != null && waterInReach.IsUnder(cameraTransform.position);
            UpdateLean();
            UpdateCamera();

            // Everything that moved the player this frame, for Velocity. A paused frame keeps the last value.
            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                Velocity = (transform.position - previousPosition) / dt;
            }
            previousPosition = transform.position;
        }

        // ------------------------------------------------------------------ Public helpers

        /// <summary>
        /// Moves the player instantly and resets its motion. CharacterController caches its position, so it is
        /// switched off around the transform write.
        /// </summary>
        public void Teleport(Vector3 position, float yawDegrees)
        {
            if (IsMantling)
            {
                EndMantle();
            }
            if (IsClimbing)
            {
                IsClimbing = false;
                ladder = null;
            }

            bool controllerWasEnabled = controller.enabled;
            controller.enabled = false;
            yaw = Mathf.Repeat(yawDegrees, 360f);
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            controller.enabled = controllerWasEnabled;
            // A trigger never reports the exit of a collider switched off inside it; switching back on inside one
            // reports a fresh entry. And the grounded flag is from before the move.
            ladderInReach = null;
            waterInReach = null;
            if (IsSwimming)
            {
                StopSwimming();
            }
            groundedStale = true;

            verticalVelocity = 0f;
            wasGrounded = false;
            IsGrounded = false;
            IsSliding = false;
            EndPowerSlide();
            slideSpeed = 0f;
            horizontalVelocity = Vector3.zero;
            measuredVelocity = Vector3.zero;
            Velocity = Vector3.zero;
            previousPosition = transform.position;
            ClearGround();
            airJumpsUsed = 0;
            lastGroundedTime = float.NegativeInfinity;
            jumpRequestedTime = float.NegativeInfinity;
        }

        private static float NormalizeAngle(float degrees)
        {
            // Unity reports Euler angles in 0..360; look math wants -180..180.
            return Mathf.Repeat(degrees + 180f, 360f) - 180f;
        }

        private void OnDrawGizmosSelected()
        {
            // Editor aid: the ground probe ray (cyan) and the headroom sweep from the capsule's current top sphere to
            // where the standing top would be (yellow).
            CharacterController capsule = controller != null ? controller : GetComponent<CharacterController>();
            if (capsule == null)
            {
                return;
            }

            Vector3 center = transform.TransformPoint(capsule.center);
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(center, center + Vector3.down * (capsule.height * 0.5f + capsule.radius * 3f));

            float standing = Application.isPlaying && standingHeight > 0f ? standingHeight : capsule.height;
            Vector3 topSphereCenter = center + Vector3.up * (capsule.height * 0.5f - capsule.radius);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(topSphereCenter, capsule.radius);
            Gizmos.DrawWireSphere(topSphereCenter + Vector3.up * (standing - capsule.height + capsule.skinWidth), capsule.radius);
        }
    }
}
