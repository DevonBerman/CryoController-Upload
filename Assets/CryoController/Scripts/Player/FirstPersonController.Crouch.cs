using UnityEngine;

namespace CryoController
{
    // FirstPersonController: crouching, the headroom check and power slides. The settings, state and frame loop are in
    // FirstPersonController.cs.
    public partial class FirstPersonController
    {
        // ------------------------------------------------------------------ Crouch

        private void UpdateCrouch()
        {
            if (IsMantling)
            {
                return;   // the room on the ledge was checked for this height: it does not change mid-climb
            }

            bool wantCrouch = false;
            bool crouchInput = ControlsActive && crouchAction != null;
            if (IsSwimming)
            {
                // In water Crouch dives: the capsule stays at full height, and a toggled crouch waits for dry land.
            }
            else if (crouchToggle)
            {
                // A press under a ceiling still flips the toggle off: the intent to stand is remembered and the
                // headroom check below keeps the capsule short until there is room.
                if (crouchInput && !PressesSuppressed && crouchAction.WasPressedThisFrame())
                {
                    crouchToggled = !crouchToggled;
                }
                wantCrouch = crouchToggled;
            }
            else if (crouchInput)
            {
                wantCrouch = crouchAction.IsPressed();
            }
            else if (InputEnabled && !MoveAndLookEnabled)
            {
                // Held still (inspecting something, say): keep the crouch the player was in, rather than read the
                // button as released and stand up mid-inspection.
                wantCrouch = crouchHeld;
            }

            // Never stand up into geometry. This is checked on every frame the capsule is still short, not only
            // when the button is released, so walking under a ledge mid-rise stops (and reverses) the rise.
            bool belowStandingHeight = controller.height < standingHeight - HeightEpsilon;
            if (!wantCrouch && belowStandingHeight && !HasHeadroom())
            {
                wantCrouch = true;
            }
            crouchHeld = wantCrouch;

            float targetHeight = wantCrouch
                ? Mathf.Clamp(crouchHeight, controller.radius * 2f, standingHeight)
                : standingHeight;
            float newHeight = Mathf.MoveTowards(controller.height, targetHeight, crouchTransitionSpeed * Time.deltaTime);

            if (!Mathf.Approximately(newHeight, controller.height))
            {
                // Resize from the feet up, read the height back (the controller may clamp it) and keep the eyes
                // the same distance below the top of the capsule. UpdateCamera applies the camera position.
                controller.height = newHeight;
                newHeight = controller.height;

                Vector3 center = controller.center;
                center.y = newHeight * 0.5f;
                controller.center = center;

                eyeLocalPosition.y = newHeight - eyeOffsetFromTop;
            }

            IsCrouching = controller.height < standingHeight - HeightEpsilon;
        }

        private bool HasHeadroom()
        {
            // Sweep a sphere the size of the capsule top from its current top-sphere center up to where the standing
            // top would be, plus the skin width the controller keeps between itself and geometry. The sphere starts
            // inside our own capsule, and casts ignore colliders they start inside, so only other objects can block.
            float radius = controller.radius;
            Vector3 topSphereCenter = transform.TransformPoint(controller.center) + Vector3.up * (controller.height * 0.5f - radius);
            float distance = standingHeight - controller.height + controller.skinWidth;
            return !Physics.SphereCast(topSphereCenter, radius, Vector3.up, out _, distance, EnvironmentMask,
                                       QueryTriggerInteraction.Ignore);
        }

        // ------------------------------------------------------------------ Power slide

        private void StartPowerSlide()
        {
            IsPowerSliding = true;
            IsSprinting = false;
            Vector3 direction = horizontalVelocity.normalized;
            horizontalVelocity = direction * (horizontalVelocity.magnitude + powerSlideBoost);
            PowerSlideStarted?.Invoke();
        }

        private void EndPowerSlide()
        {
            if (!IsPowerSliding)
            {
                return;
            }
            IsPowerSliding = false;
            PowerSlideEnded?.Invoke();
        }

        private void UpdatePowerSlide(Vector2 input, float dt, float slopeAngle, Vector3 groundNormal)
        {
            float speed = horizontalVelocity.magnitude;
            if (speed < 0.001f)
            {
                EndPowerSlide();   // stopped dead (Power Slide Min Speed of 0): nothing left to slide with
                return;
            }
            Vector3 direction = horizontalVelocity / speed;

            // Steer a little with sideways input; the slide keeps going where it was going otherwise.
            if (powerSlideSteering > 0f && input.x != 0f)
            {
                direction = Quaternion.AngleAxis(input.x * powerSlideSteering * dt, Vector3.up) * direction;
            }

            // Friction, and gravity's share along the slope: downhill speeds up, uphill slows down faster.
            float acceleration = -powerSlideFriction;
            if (slopeAngle > 0f)
            {
                Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, groundNormal).normalized;
                acceleration += gravity * Mathf.Sin(slopeAngle * Mathf.Deg2Rad) * Vector3.Dot(downhill, direction);
            }
            speed = Mathf.Clamp(speed + acceleration * dt, 0f, Mathf.Max(maxSlideSpeed, sprintSpeed + powerSlideBoost));
            horizontalVelocity = direction * speed;

            if (speed < powerSlideMinSpeed)
            {
                EndPowerSlide();   // crouch-walking from here; the crouch itself is still held
            }
        }
    }
}
