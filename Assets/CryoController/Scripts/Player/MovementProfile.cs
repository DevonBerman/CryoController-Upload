using UnityEngine;

namespace CryoController
{
    /// <summary>
    /// A set of movement and camera-feel settings for <see cref="FirstPersonController"/>: speeds, acceleration,
    /// jumping and gravity, slopes, mantling, ladders, crouch speed, power slides, head bob, camera feel, zoom and lean.
    /// Assign one to the controller's Profile to use its settings instead of the controller's own, and swap profiles at
    /// runtime (<see cref="FirstPersonController.Profile"/>) for states such as wounded, carrying something heavy or
    /// wading. Player preferences (look sensitivity, invert Y, field of view, comfort) and the capsule's setup are not
    /// part of a profile.
    ///
    /// Create one with Assets > Create > CryoController > Movement Profile, or from a controller's current settings with
    /// Save Settings As Movement Profile in its component menu. The package ships Default, Realistic, Arcade and
    /// Tactical profiles in its Profiles folder.
    /// </summary>
    [CreateAssetMenu(fileName = "Movement Profile", menuName = "CryoController/Movement Profile")]
    public class MovementProfile : ScriptableObject
    {
        // The fields match the controller's settings of the same names; FirstPersonController.TransferProfile copies
        // them both ways. Their defaults are the controller's.
        [Header("Movement (metres per second)")]
        [Tooltip("Speed while walking.")]
        [SerializeField, Min(0f)] internal float walkSpeed = 4f;
        [Tooltip("Speed while Sprint is held. Sprinting is not possible while crouched.")]
        [SerializeField, Min(0f)] internal float sprintSpeed = 7f;
        [Tooltip("Speed while crouched.")]
        [SerializeField, Min(0f)] internal float crouchSpeed = 2f;

        [Header("Acceleration (metres per second squared)")]
        [Tooltip("How quickly the player reaches the target speed on the ground. Higher is snappier.")]
        [SerializeField, Min(0.01f)] internal float groundAcceleration = 40f;
        [Tooltip("How quickly the player stops on the ground when there is no input.")]
        [SerializeField, Min(0.01f)] internal float groundDeceleration = 60f;
        [Tooltip("How quickly movement input can change the velocity while airborne. Momentum from the ground is kept " +
                 "and there is no air drag; lower values give less air control.")]
        [SerializeField, Min(0f)] internal float airControl = 6f;

        [Header("Jump and gravity")]
        [Tooltip("Peak height of a jump, in metres.")]
        [SerializeField, Min(0f)] internal float jumpHeight = 1.2f;
        [Tooltip("Downward acceleration in m/s^2, as a positive number. Stronger than real gravity (9.81) feels less floaty.")]
        [SerializeField, Min(0f)] internal float gravity = 20f;
        [Tooltip("Coyote time: seconds after walking off a ledge during which a jump is still accepted, so a press that " +
                 "lands a frame late does not fall flat. 0 disables it.")]
        [SerializeField, Min(0f)] internal float coyoteTime = 0.12f;
        [Tooltip("Jump buffer: seconds before landing during which a jump press is remembered and performed the moment " +
                 "the player can jump again. 0 disables it (the press then only counts on a frame the player is grounded).")]
        [SerializeField, Min(0f)] internal float jumpBufferTime = 0.12f;
        [Tooltip("Extra jumps in mid-air before landing: 1 is a double jump. A press in the air after coyote time uses one. " +
                 "0 disables.")]
        [SerializeField, Min(0)] internal int airJumps;
        [Tooltip("How high an air jump rises from where it starts, in metres. It cancels any fall speed but never " +
                 "slows a faster rise.")]
        [SerializeField, Min(0f)] internal float airJumpHeight = 1f;

        [Header("Slopes")]
        [Tooltip("Top speed, in m/s, when sliding down a surface steeper than the CharacterController's Slope Limit. " +
                 "The slide accelerates like gravity along the slope and the player cannot jump while sliding.")]
        [SerializeField, Min(0f)] internal float maxSlideSpeed = 8f;
        [Tooltip("How much movement input still steers while sliding: 0 = none, 1 = full walking control.")]
        [SerializeField, Range(0f, 1f)] internal float slideControl = 0.25f;

        [Header("Mantling")]
        [Tooltip("Ledges up to this high above the feet, in metres, can be climbed by pushing forward at them while " +
                 "jumping or falling past them. 0 disables mantling.")]
        [SerializeField, Min(0f)] internal float mantleMaxHeight = 1.6f;
        [Tooltip("Ledges up to this high are climbed just by walking into them while pushing forward, no jump needed. " +
                 "Ledges below the CharacterController's Step Offset are simply stepped up.")]
        [SerializeField, Min(0f)] internal float autoMantleHeight = 0.9f;
        [Tooltip("Seconds a climb takes for a ledge at Mantle Max Height; lower ledges are proportionally quicker.")]
        [SerializeField, Min(0.05f)] internal float mantleDuration = 0.45f;

        [Header("Ladders")]
        [Tooltip("Climbing speed on ladders, in metres per second.")]
        [SerializeField, Min(0f)] internal float climbSpeed = 3f;
        [Tooltip("Speed the player is pushed away from a ladder when jumping off it, in metres per second.")]
        [SerializeField, Min(0f)] internal float ladderDismountSpeed = 3f;

        [Header("Crouch")]
        [Tooltip("How fast the capsule shrinks or grows, in metres per second.")]
        [SerializeField, Min(0.01f)] internal float crouchTransitionSpeed = 6f;

        [Header("Power slide")]
        [Tooltip("Crouching while sprinting on the ground starts a slide: the player drops low and keeps their momentum, " +
                 "slowing by Power Slide Friction. Off: crouching while sprinting just crouches.")]
        [SerializeField] internal bool powerSlide = true;
        [Tooltip("Speed added when a slide starts, in m/s, so it feels like a push off.")]
        [SerializeField, Min(0f)] internal float powerSlideBoost = 2f;
        [Tooltip("Deceleration while sliding on flat ground, in m/s^2. Slopes add or take away gravity's share.")]
        [SerializeField, Min(0.1f)] internal float powerSlideFriction = 6f;
        [Tooltip("The slide ends below this speed, in m/s, leaving the player crouched.")]
        [SerializeField, Min(0f)] internal float powerSlideMinSpeed = 2.5f;
        [Tooltip("How fast a slide turns at full sideways input, in degrees per second.")]
        [SerializeField, Min(0f)] internal float powerSlideSteering = 45f;

        [Header("Head bob")]
        [Tooltip("Bob the camera while walking on the ground. Fades in and out with movement.")]
        [SerializeField] internal bool headBob = true;
        [Tooltip("Vertical bob amplitude, in metres. One bounce per step.")]
        [SerializeField, Min(0f)] internal float headBobAmplitude = 0.03f;
        [Tooltip("Sideways sway amplitude, in metres. One sway left and right per two steps (a figure of eight).")]
        [SerializeField, Min(0f)] internal float headBobSway = 0.02f;
        [Tooltip("Distance travelled per step, in metres; also the interval of the Footstep event. Bob speed follows " +
                 "movement speed, so sprinting bobs faster and crouching slower. Shorter steps bob faster.")]
        [SerializeField, Min(0.01f)] internal float headBobStepLength = 2f;

        [Header("Camera feel")]
        [Tooltip("Camera dip on landing, in metres per m/s of impact speed above 4 m/s (about a 0.4 m drop; a 1.2 m " +
                 "jump lands at 6.9 m/s). 0 disables.")]
        [SerializeField, Min(0f)] internal float landingDip = 0.015f;
        [Tooltip("Ease the camera up over stairs and ledges instead of snapping to the new height when the " +
                 "CharacterController steps up.")]
        [SerializeField] internal bool smoothSteps = true;
        [Tooltip("Field of view added while sprinting, in degrees. 0 disables and leaves the field of view to other " +
                 "scripts.")]
        [SerializeField, Min(0f)] internal float sprintFovBoost = 6f;
        [Tooltip("Camera roll at walking speed sideways, in degrees, leaning into the strafe. 0 disables.")]
        [SerializeField, Range(0f, 10f)] internal float strafeTilt = 1.5f;

        [Header("Zoom")]
        [Tooltip("Field of view while Zoom is held, as a fraction of the normal one: 0.6 narrows it to 60 %. Look " +
                 "sensitivity scales down with it and sprinting stops. Needs a Player/Zoom action (the shipped actions " +
                 "bind the right mouse button and the left trigger). 1 disables.")]
        [SerializeField, Range(0.1f, 1f)] internal float zoomFovScale = 0.6f;
        [Tooltip("Seconds the zoom takes to blend in or out.")]
        [SerializeField, Min(0.01f)] internal float zoomTime = 0.15f;

        [Header("Lean")]
        [Tooltip("How far the head moves sideways at full lean, in metres, to peek round corners. Needs Player/LeanLeft " +
                 "and Player/LeanRight actions (the shipped actions bind Q and E, and the gamepad's shoulder buttons). " +
                 "The head never goes into a wall. 0 disables.")]
        [SerializeField, Min(0f)] internal float leanDistance = 0.35f;
        [Tooltip("Camera roll at full lean, in degrees. The head swings about a pivot low in the body, so it also dips a little.")]
        [SerializeField, Range(0f, 30f)] internal float leanAngle = 12f;
        [Tooltip("Seconds a lean takes to reach its side.")]
        [SerializeField, Min(0.01f)] internal float leanTime = 0.12f;

        // The settings, for reading. To change them, edit the asset or make another profile.
        public float WalkSpeed => walkSpeed;
        public float SprintSpeed => sprintSpeed;
        public float CrouchSpeed => crouchSpeed;
        public float GroundAcceleration => groundAcceleration;
        public float GroundDeceleration => groundDeceleration;
        public float AirControl => airControl;
        public float JumpHeight => jumpHeight;
        public float Gravity => gravity;
        public float CoyoteTime => coyoteTime;
        public float JumpBufferTime => jumpBufferTime;
        public int AirJumps => airJumps;
        public float AirJumpHeight => airJumpHeight;
        public float MaxSlideSpeed => maxSlideSpeed;
        public float SlideControl => slideControl;
        public float MantleMaxHeight => mantleMaxHeight;
        public float AutoMantleHeight => autoMantleHeight;
        public float MantleDuration => mantleDuration;
        public float ClimbSpeed => climbSpeed;
        public float LadderDismountSpeed => ladderDismountSpeed;
        public float CrouchTransitionSpeed => crouchTransitionSpeed;
        public bool PowerSlide => powerSlide;
        public float PowerSlideBoost => powerSlideBoost;
        public float PowerSlideFriction => powerSlideFriction;
        public float PowerSlideMinSpeed => powerSlideMinSpeed;
        public float PowerSlideSteering => powerSlideSteering;
        public bool HeadBob => headBob;
        public float HeadBobAmplitude => headBobAmplitude;
        public float HeadBobSway => headBobSway;
        public float HeadBobStepLength => headBobStepLength;
        public float LandingDip => landingDip;
        public bool SmoothSteps => smoothSteps;
        public float SprintFovBoost => sprintFovBoost;
        public float StrafeTilt => strafeTilt;
        public float ZoomFovScale => zoomFovScale;
        public float ZoomTime => zoomTime;
        public float LeanDistance => leanDistance;
        public float LeanAngle => leanAngle;
        public float LeanTime => leanTime;

        // Bumped whenever the profile is edited in the Inspector, so a controller using it in Play Mode picks the
        // change up.
        internal int Version { get; private set; }

        /// <summary>
        /// A new profile holding the controller's current settings: to save them as an asset, or to start a variant from.
        /// </summary>
        public static MovementProfile CreateFrom(FirstPersonController controller)
        {
            MovementProfile profile = CreateInstance<MovementProfile>();
            profile.name = controller.name + " Movement";
            controller.TransferProfile(profile, false);
            return profile;
        }

        private void OnValidate()
        {
            Version++;
        }
    }
}
