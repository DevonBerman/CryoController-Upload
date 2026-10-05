using UnityEngine;

namespace CryoController
{
    // FirstPersonController: grabbing, climbing and leaving ladders. The settings, state and frame loop are in
    // FirstPersonController.cs.
    public partial class FirstPersonController
    {
        // Ladders: seconds after letting go before a ladder can be grabbed again, how far below the top of the ladder
        // zone the exit onto the platform starts (m), and the climb distance between rung footsteps (m).
        private const float LadderCooldown = 0.4f;
        private const float LadderTopExit = 0.4f;
        private const float LadderRungSpacing = 0.4f;

        // ------------------------------------------------------------------ Ladders

        /// <summary>Called by a <see cref="Ladder"/> when the player enters its zone.</summary>
        public void EnterLadderZone(Ladder zoneLadder)
        {
            ladderInReach = zoneLadder;
        }

        /// <summary>Called by a <see cref="Ladder"/> when the player leaves its zone; lets go if climbing it.</summary>
        public void ExitLadderZone(Ladder zoneLadder)
        {
            if (ladderInReach == zoneLadder)
            {
                ladderInReach = null;
            }
            if (ladder == zoneLadder && IsClimbing)
            {
                StopClimb();
            }
        }

        private void StartClimb(Ladder target)
        {
            IsClimbing = true;
            ladder = target;
            airJumpsUsed = 0;
            climbedSinceRung = 0f;
            verticalVelocity = 0f;
            horizontalVelocity = Vector3.zero;
            slideSpeed = 0f;
            wasGrounded = false;
            IsGrounded = false;
            ClearGround();
            IsSprinting = false;
            IsSliding = false;
            EndPowerSlide();
            jumpRequestedTime = float.NegativeInfinity;
            lastGroundedTime = float.NegativeInfinity;
        }

        private void StopClimb()
        {
            IsClimbing = false;
            ladder = null;
            ladderCooldownUntil = Time.time + LadderCooldown;
        }

        private void UpdateClimb()
        {
            // A ladder destroyed or switched off mid-climb never reports the player leaving its zone: let go and fall.
            if (ladder == null || !ladder.IsClimbable)
            {
                StopClimb();
                return;
            }

            float dt = Time.deltaTime;
            Vector2 input = ControlsActive ? Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f) : Vector2.zero;

            // Jump lets go with a push away from the rungs and a small hop.
            if (ControlsActive && jumpAction != null && !PressesSuppressed && jumpAction.WasPressedThisFrame())
            {
                Vector3 away = -ladder.Forward;
                StopClimb();
                horizontalVelocity = away * ladderDismountSpeed;
                verticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight) * 0.5f;
                Jumped?.Invoke();
                return;
            }

            float climb = input.y;   // forward climbs up, back climbs down

            // Reaching the top: step onto the platform with a mantle. If there is no platform (a ladder to nowhere),
            // the climb simply continues until the zone ends.
            if (climb > 0f && Feet.y >= ladder.TopY - LadderTopExit
                && TryFindLedgeAt(Feet + ladder.Forward * controller.radius, ladder.Forward, -controller.stepOffset,
                                  out Vector3 ledgeFeet, out float ledgeHeight, out Transform ledge))
            {
                StopClimb();
                StartMantle(ledgeFeet, Mathf.Max(ledgeHeight, 0f), ledge);
                return;
            }

            // Reaching the bottom: climbing down onto the ground lets go.
            if (climb < 0f && controller.isGrounded)
            {
                StopClimb();
                return;
            }

            Vector3 motion = ladder.Up * (climb * climbSpeed * dt);
            controller.Move(motion);

            // Rung footsteps.
            climbedSinceRung += Mathf.Abs(climb * climbSpeed * dt);
            if (climbedSinceRung >= LadderRungSpacing)
            {
                climbedSinceRung -= LadderRungSpacing;
                Footstep?.Invoke();
            }

            IsGrounded = false;
            wasGrounded = false;
        }
    }
}
