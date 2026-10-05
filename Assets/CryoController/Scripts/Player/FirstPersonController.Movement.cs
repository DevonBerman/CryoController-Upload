using UnityEngine;

namespace CryoController
{
    // FirstPersonController: walking, sprinting, jumping, gravity, slope sliding, the ground probe and pushing
    // rigidbodies. The settings, state and frame loop are in FirstPersonController.cs.
    public partial class FirstPersonController
    {
        // Downward speed while grounded (m/s): the minimum, and a cap for the slope-scaled value. Keeping a
        // downward push while grounded is what makes CharacterController.isGrounded reliable.
        private const float GroundStickSpeed = 2f;
        private const float MaxGroundStickSpeed = 20f;

        // Slack (metres) allowed between where the ground probe hits and where a capsule resting on that surface would
        // touch it, covering the skin width and small steps down.
        private const float GroundProbeMargin = 0.2f;

        // The smallest normal.y a CharacterController contact needs to count as ground under the player (about 72
        // degrees); flatter contacts are walls.
        private const float GroundContactMinNormalY = 0.3f;

        // While airborne, the stored velocity is replaced by the measured one when the measured speed squared falls
        // below this fraction of it (about a 5 % speed loss): a wall took the momentum, float noise did not.
        private const float BlockedVelocityFraction = 0.9f;

        // ------------------------------------------------------------------ Movement

        private void UpdateMovement()
        {
            float dt = Time.deltaTime;

            // isGrounded reflects the previous Move(); it is reliable because we keep pushing down while grounded. After
            // a teleport it describes where the player was, so the first frame counts as airborne.
            bool grounded = controller.isGrounded && !groundedStale;
            groundedStale = false;

            // ---- Moving ground: whatever the player stood on may have moved since last frame. Ride it before anything
            // else, so the rest of the frame starts from where the ground is now. The Move calls of this frame report
            // their contacts; the most floor-like one is the ground for the next frame.
            frameGroundCollider = null;
            frameGroundNormalY = GroundContactMinNormalY;
            RideGround(dt);

            // A grounded capsule may be resting on a surface too steep to walk on. CharacterController refuses to
            // climb such slopes but happily lets the player stand on them, so sliding is handled here.
            float slopeAngle = 0f;
            Vector3 groundNormal = Vector3.up;
            if (grounded && verticalVelocity <= 0f && ProbeGround(out groundNormal))
            {
                slopeAngle = Vector3.Angle(groundNormal, Vector3.up);
            }
            bool steep = slopeAngle > controller.slopeLimit;
            IsSliding = steep;

            // ---- Horizontal: input is relative to where the body faces; clamped so diagonals are not faster.
            Vector2 input = ControlsActive ? Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f) : Vector2.zero;
            bool sprintHeld = ControlsActive && sprintAction != null && sprintAction.IsPressed();

            // ---- Power slide: a crouch pressed while sprinting on the ground (IsSprinting is still last frame's here,
            // before the crouch that just began cancels it) keeps the momentum and drops low.
            bool crouchPressed = ControlsActive && !PressesSuppressed && crouchAction != null && crouchAction.WasPressedThisFrame();
            // It needs real speed (measured, so sprinting into a wall does not count) or it would end on the same frame.
            Vector3 movedPlanar = new Vector3(measuredVelocity.x, 0f, measuredVelocity.z);
            if (powerSlide && !IsPowerSliding && IsSprinting && crouchPressed && grounded && !steep && verticalVelocity <= 0f
                && movedPlanar.magnitude + powerSlideBoost > powerSlideMinSpeed)
            {
                StartPowerSlide();
            }
            if (IsPowerSliding && (!grounded || steep || !crouchHeld))
            {
                EndPowerSlide();   // in the air, on a steep slope or standing up: the momentum carries on as it is
            }

            IsSprinting = sprintHeld && SprintAllowed && !IsCrouching && !steep && !IsZooming && !IsPowerSliding && input.sqrMagnitude > 0f;

            float speed = walkSpeed;
            if (IsCrouching)
            {
                speed = crouchSpeed;
            }
            else if (IsSprinting)
            {
                speed = sprintSpeed;
            }
            if (steep)
            {
                speed *= slideControl;
            }
            Vector3 targetVelocity = (transform.right * input.x + transform.forward * input.y) * speed;
            bool hasInput = input.sqrMagnitude > 0f;

            // ---- Ladder: inside a ladder's zone, walking into it while facing the rungs (from below its top, so
            // walking about on the platform it leads to never grabs it) or being airborne (falling into it, jumping up
            // along it) grabs it, unless the player just let go.
            if (ladderInReach != null && Time.time >= ladderCooldownUntil && ladderInReach.Reaches(controller))
            {
                bool walkingIn = input.y > MantleForwardInput && !IsPowerSliding
                    && Vector3.Dot(transform.forward, ladderInReach.Forward) > 0.5f
                    && Feet.y < ladderInReach.TopY - LadderTopExit;
                if (!grounded || walkingIn)
                {
                    StartClimb(ladderInReach);
                    return;
                }
            }

            // ---- Mantle: pushing forward at a ledge too high to step onto climbs it. Low ledges are climbed straight
            // from the ground once the player is up against them; higher ones need the player to be in the air (a jump at
            // the wall, or a fall past it).
            if (mantleMaxHeight > 0f && !steep && !IsPowerSliding && input.y > MantleForwardInput
                && TryFindLedge(transform.forward, out Vector3 ledgeFeet, out float ledgeHeight, out float wallGap,
                                out Transform ledge)
                && (!grounded || (ledgeHeight <= autoMantleHeight && wallGap <= controller.skinWidth + AutoMantleReach)))
            {
                StartMantle(ledgeFeet, ledgeHeight, ledge);
                return;
            }

            // Accelerate towards the target rather than snapping to it. On the ground that is quick in both
            // directions; in the air only input changes the velocity, so a running jump keeps its momentum and
            // letting go of the stick mid-air does not stop the player dead.
            if (IsPowerSliding)
            {
                UpdatePowerSlide(input, dt, slopeAngle, groundNormal);
            }
            else if (grounded)
            {
                float rate = hasInput ? groundAcceleration : groundDeceleration;
                horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, rate * dt);
            }
            else if (hasInput)
            {
                // Air control steers relative to the ground the player jumped from, so a jump on a moving platform
                // is not pulled off it by holding the stick.
                horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity + airCarry, airControl * dt);
            }

            // ---- Slide: accelerate downhill like gravity along the slope; off the slope the momentum bleeds away.
            if (steep)
            {
                slideDirection = Vector3.ProjectOnPlane(Vector3.down, groundNormal).normalized;
                slideSpeed = Mathf.Min(slideSpeed + gravity * Mathf.Sin(slopeAngle * Mathf.Deg2Rad) * dt, maxSlideSpeed);
            }
            else
            {
                slideDirection = Vector3.ProjectOnPlane(slideDirection, Vector3.up).normalized;
                slideSpeed = Mathf.MoveTowards(slideSpeed, 0f, gravity * dt);
            }
            Vector3 slideVelocity = slideDirection * slideSpeed;

            // ---- Vertical
            // "Standing" excludes a grounded flag seen while still rising (jumping against a slope), which must
            // neither re-stick nor count as ground for another jump, and a steep slope, which is not a place to
            // stand or jump from.
            bool standing = grounded && !steep && verticalVelocity <= 0f;
            if (standing)
            {
                // Push down hard enough to follow the steepest walkable slope at the current speed, so isGrounded
                // does not flicker (and jumps are not eaten) when running downhill. On flat ground the push is
                // absorbed by the floor.
                float slopeTan = Mathf.Tan(controller.slopeLimit * Mathf.Deg2Rad);
                float stick = Mathf.Clamp(horizontalVelocity.magnitude * slopeTan, GroundStickSpeed, MaxGroundStickSpeed);
                verticalVelocity = -stick;
                lastGroundedTime = Time.time;
                airJumpsUsed = 0;
            }
            else if (steep)
            {
                // Hug the slope with the minimum push; gravity's real effect is the slide velocity above.
                verticalVelocity = -GroundStickSpeed;
            }
            else if (!grounded && wasGrounded && verticalVelocity < -GroundStickSpeed)
            {
                // Just walked off an edge: do not carry the ground-stick push into the fall.
                verticalVelocity = -GroundStickSpeed;
            }

            // A press is remembered (jump buffer) and honoured on the first frame a jump is possible: while standing,
            // or for a short grace period after leaving the ground (coyote time). Jumping consumes both stamps, so a
            // coyote jump cannot be repeated mid-air and a buffered press cannot fire twice.
            bool jumpPressed = ControlsActive && jumpAction != null && !PressesSuppressed && jumpAction.WasPressedThisFrame();
            if (jumpPressed)
            {
                jumpRequestedTime = Time.time;
            }
            bool jumpRequested = Time.time - jumpRequestedTime <= jumpBufferTime;
            bool canJump = verticalVelocity <= 0f && (standing || Time.time - lastGroundedTime <= coyoteTime);
            if (jumpRequested && canJump)
            {
                verticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);   // v = sqrt(2 g h)
                lastGroundedTime = float.NegativeInfinity;
                jumpRequestedTime = float.NegativeInfinity;
                if (IsPowerSliding)
                {
                    EndPowerSlide();   // a jump out of a slide keeps its speed
                }
                Jumped?.Invoke();
            }
            else if (jumpPressed && !grounded && !steep && airJumpsUsed < airJumps)
            {
                // An air jump: a fresh press in mid-air once coyote time is over. It cancels a fall, so it works the
                // same whether the player is still rising or already falling, but never slows a faster rise (off a
                // launch pad, say). The press is used up, so it is not also a buffered jump on landing.
                airJumpsUsed++;
                verticalVelocity = Mathf.Max(verticalVelocity, Mathf.Sqrt(2f * gravity * airJumpHeight));
                jumpRequestedTime = float.NegativeInfinity;
                Jumped?.Invoke();
            }

            // Move by the average of this frame's start and end velocity, so a jump peaks at jumpHeight regardless
            // of frame rate (plain "apply gravity, then move" falls 5-10 % short and varies with dt).
            float previousVerticalVelocity = verticalVelocity;
            verticalVelocity -= gravity * dt;
            float verticalMove = 0.5f * (previousVerticalVelocity + verticalVelocity) * dt;

            // ---- Integrate. Move() sweeps, so it never tunnels through thin colliders.
            Vector3 positionBefore = transform.position;
            float heightBefore = positionBefore.y;
            CollisionFlags flags = controller.Move((horizontalVelocity + slideVelocity) * dt + Vector3.up * verticalMove);
            measuredVelocity = dt > 0f ? (transform.position - positionBefore) / dt : Vector3.zero;
            Ground = controller.isGrounded ? frameGroundCollider : null;   // before Landed fires, so listeners can ask what was landed on

            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
            {
                verticalVelocity = 0f;   // head bump: stop rising instead of hanging under the ceiling
            }

            if (!grounded && controller.isGrounded && verticalVelocity < 0f)
            {
                // The ground was reached somewhere during the frame: the average of the frame's start and end speed
                // matches the fall at any frame rate, where the end speed alone overstates it at low ones.
                OnLanded(-0.5f * (previousVerticalVelocity + verticalVelocity));
            }
            else if (smoothSteps && grounded && controller.isGrounded)
            {
                // A rise within one frame that no walkable slope could produce at this speed is the controller
                // stepping up a stair or ledge; the camera eases up instead of snapping with the capsule.
                float rise = transform.position.y - heightBefore;
                float slopeRise = horizontalVelocity.magnitude * Mathf.Tan(controller.slopeLimit * Mathf.Deg2Rad) * dt;
                if (rise > slopeRise + StepRiseTolerance)
                {
                    stepSmoothOffset = Mathf.Max(stepSmoothOffset - rise, -StepSmoothMax);
                }
            }

            if (!grounded || IsPowerSliding)
            {
                // Momentum into a wall is lost, not stored: adopt the measured velocity when the controller was clearly
                // blocked, so the player does not cling to the wall or spring off it after landing, and a slide into a
                // wall ends there rather than grinding against it.
                Vector3 measured = Vector3.ProjectOnPlane(measuredVelocity, Vector3.up);
                if (measured.sqrMagnitude < horizontalVelocity.sqrMagnitude * BlockedVelocityFraction)
                {
                    horizontalVelocity = measured;
                    if (IsPowerSliding)
                    {
                        EndPowerSlide();
                    }
                }
            }

            wasGrounded = grounded;
            IsGrounded = controller.isGrounded;
            UpdateGroundContact();
        }

        private bool ProbeGround(out Vector3 normal)
        {
            // A ray straight down from the capsule center finds the surface under the player's axis. It starts inside
            // our own capsule, which raycasts ignore. The hit only counts if it lies about where a capsule resting on
            // that surface would touch it: a ledge edge reports the flat ledge top (not steep), and ground far below
            // the feet is ignored, so neither is mistaken for a slope to slide down.
            normal = Vector3.up;
            Vector3 origin = transform.TransformPoint(controller.center);
            float maxDistance = controller.height * 0.5f + controller.radius * 3f;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxDistance, EnvironmentMask,
                                 QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            float cosine = Vector3.Dot(hit.normal, Vector3.up);
            if (cosine < 0.05f)
            {
                return false;   // (near) vertical: a wall, not ground
            }

            // Vertical distance from the center of a resting capsule to the surface: the straight part of the capsule
            // plus the bottom sphere's radius stretched by the slope (r / cos).
            float restingDistance = controller.height * 0.5f - controller.radius + controller.radius / cosine;
            if (hit.distance > restingDistance + controller.skinWidth + GroundProbeMargin)
            {
                return false;
            }

            normal = hit.normal;
            return true;
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // The most floor-like contact of this frame's moves is the ground the player stands on.
            if (hit.normal.y > frameGroundNormalY)
            {
                frameGroundNormalY = hit.normal.y;
                frameGroundCollider = hit.collider;
            }

            // Called by CharacterController.Move for every collider touched, so the push is scaled by deltaTime to
            // make it a steady force rather than a per-frame impulse that would depend on frame rate. The ground being
            // stood on is never pushed: riding it moves sideways into it, and shoving it would move the player too.
            Rigidbody body = hit.collider.attachedRigidbody;
            if (!IsPushable(body) || riding || hit.collider == Ground)
            {
                return;
            }

            // Only sideways contacts push: hits from above are the floor we stand on, and hits from below are
            // ceilings, and shoving either would make the controller fight its own support.
            if (hit.moveDirection.y < -0.3f || hit.moveDirection.y > 0.3f)
            {
                return;
            }

            Vector3 pushDirection = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
            if (pushDirection.sqrMagnitude < 0.001f)
            {
                return;
            }

            body.AddForceAtPosition(pushDirection.normalized * (pushForce * Time.deltaTime), hit.point, ForceMode.Impulse);
        }
    }
}
