using UnityEngine;

namespace CryoController
{
    // FirstPersonController: swimming in a Water Volume. The settings, state and frame loop are in
    // FirstPersonController.cs.
    public partial class FirstPersonController
    {
        // Swimming: how much shallower than Swim Depth the water must get before the player wades again (m), the speed
        // a still player drifts up at (m/s), the fastest fall kept on entering the water (m/s), and how near the
        // floating depth (m) counts as at the surface for climbing out.
        private const float SwimExitMargin = 0.15f;
        private const float BuoyancySpeed = 0.6f;
        private const float WaterEntryMaxFall = 3f;
        private const float SurfaceReach = 0.35f;

        // How the upward speed eases off near the float line: the most it can be, per metre below the line (1/s).
        private const float SurfaceSpring = 3f;

        /// <summary>True while swimming in a <see cref="WaterVolume"/>.</summary>
        public bool IsSwimming { get; private set; }

        /// <summary>True while the camera is under the surface of a <see cref="WaterVolume"/>.</summary>
        public bool IsUnderwater { get; private set; }

        /// <summary>The water volume the player is in, swimming or wading, or null.</summary>
        public WaterVolume Water => waterInReach;

        /// <summary>Raised when the player starts swimming.</summary>
        public event System.Action StartedSwimming;

        /// <summary>Raised when the player stops swimming: wading out, climbing out, or leaving the water.</summary>
        public event System.Action StoppedSwimming;

        /// <summary>Called by a <see cref="WaterVolume"/> when the player enters it.</summary>
        public void EnterWater(WaterVolume water)
        {
            waterInReach = water;
        }

        /// <summary>Called by a <see cref="WaterVolume"/> when the player leaves it; stops swimming in it.</summary>
        public void ExitWater(WaterVolume water)
        {
            if (waterInReach != water)
            {
                return;
            }
            waterInReach = null;
            if (IsSwimming)
            {
                StopSwimming();
            }
        }

        // How deep the water is at the feet; 0 out of the water.
        private float WaterDepth()
        {
            return waterInReach != null ? waterInReach.SurfaceY - Feet.y : 0f;
        }

        private bool ShouldSwim()
        {
            return swimSpeed > 0f && waterInReach != null && waterInReach.Reaches(controller) && WaterDepth() > swimDepth;
        }

        private void StartSwimming()
        {
            IsSwimming = true;
            EndPowerSlide();
            IsSprinting = false;
            IsSliding = false;
            slideSpeed = 0f;
            wasGrounded = false;
            IsGrounded = false;
            ClearGround();
            airJumpsUsed = 0;
            jumpRequestedTime = float.NegativeInfinity;
            // The water takes the fall: no landing, and no more than a little of the speed carries on down.
            verticalVelocity = Mathf.Max(verticalVelocity, -WaterEntryMaxFall);
            StartedSwimming?.Invoke();
        }

        private void StopSwimming()
        {
            IsSwimming = false;
            lastGroundedTime = float.NegativeInfinity;
            StoppedSwimming?.Invoke();
        }

        private void UpdateSwim()
        {
            float dt = Time.deltaTime;
            if (waterInReach == null || !waterInReach.Reaches(controller))
            {
                ExitWater(waterInReach);   // switched off or gone: fall out of it
                return;
            }
            float depth = WaterDepth();
            if (depth < swimDepth - SwimExitMargin)
            {
                StopSwimming();   // shallow enough to stand: wade from the next frame
                return;
            }

            Vector2 input = ControlsActive ? Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f) : Vector2.zero;
            bool up = ControlsActive && jumpAction != null && jumpAction.IsPressed();
            bool down = ControlsActive && crouchAction != null && crouchAction.IsPressed();
            bool fast = ControlsActive && sprintAction != null && sprintAction.IsPressed() && walkSpeed > 0f;
            float speed = swimSpeed * (fast ? Mathf.Max(1f, sprintSpeed / walkSpeed) : 1f);

            // Swim where the camera looks: looking down while swimming forward dives, looking up rises.
            Vector3 forward = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
            Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            Vector3 target = (forward * input.y + right * input.x) * speed;
            if (up)
            {
                target.y = speed;
            }
            else if (down)
            {
                target.y = -speed;
            }
            else if (input.sqrMagnitude <= 0f)
            {
                target.y = BuoyancySpeed;   // a still player drifts up to the surface
            }

            // At the surface the player floats with the eyes Float Height above the water, and swims no higher. The speed
            // up eases off on the way, so they arrive at the float line rather than bob past it, and above it (thrown up
            // by a wave of speed) they settle back down.
            float floatingDepth = eyeLocalPosition.y - floatHeight;
            target.y = Mathf.Min(target.y, (depth - floatingDepth) * SurfaceSpring);
            target += waterInReach.Flow;

            // Climbing out: at the surface, swimming into an edge just above the water climbs onto it.
            if (input.y > MantleForwardInput && depth <= floatingDepth + SurfaceReach
                && TryFindLedge(transform.forward, out Vector3 ledgeFeet, out float ledgeHeight, out _, out Transform ledge,
                                depth + climbOutHeight)
                && ledgeHeight >= depth - controller.stepOffset)
            {
                StopSwimming();
                StartMantle(ledgeFeet, ledgeHeight, ledge);
                return;
            }

            Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity;
            velocity = Vector3.MoveTowards(velocity, target, swimAcceleration * dt);
            horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
            verticalVelocity = velocity.y;

            Vector3 positionBefore = transform.position;
            CollisionFlags flags = controller.Move(velocity * dt);
            measuredVelocity = dt > 0f ? (transform.position - positionBefore) / dt : Vector3.zero;
            if ((flags & CollisionFlags.Below) != 0 && verticalVelocity < 0f)
            {
                verticalVelocity = 0f;   // on the bottom
            }
            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
            {
                verticalVelocity = 0f;
            }
            IsSprinting = false;
            IsGrounded = false;
            wasGrounded = false;
            Ground = null;
        }
    }
}
