using UnityEngine;

namespace CryoController
{
    // FirstPersonController: camera feel (head bob, the landing dip, step smoothing, strafe tilt, camera shake and the
    // field of view). The settings, state and frame loop are in FirstPersonController.cs.
    public partial class FirstPersonController
    {
        // Head bob fades in and out over this long (seconds) so starting and stopping never snap the camera, and it
        // only runs above this measured ground speed (m/s), so pushing into a wall does not bob.
        private const float HeadBobFadeTime = 0.15f;
        private const float HeadBobMinSpeed = 0.1f;

        // Landings slower than this (m/s; a drop of about 0.15 m, such as settling onto the ground at scene start) are
        // not reported through the Landed event.
        private const float LandedEventMinSpeed = 2.5f;

        // Landing dip: impacts slower than the threshold (m/s; about a 0.4 m drop) do not dip, the dip depth is capped
        // (m), and the camera recovers as a critically damped spring of this angular frequency (rad/s).
        private const float LandingDipThreshold = 4f;
        private const float LandingDipMax = 0.2f;
        private const float LandingDipFrequency = 16f;

        // Step smoothing: a one-frame rise beyond what a slope at the slope limit could produce (plus this tolerance,
        // m) is a step up; the camera catches up with this time constant (s), and the lag is capped (m).
        private const float StepRiseTolerance = 0.01f;
        private const float StepSmoothTime = 0.06f;
        private const float StepSmoothMax = 0.5f;

        // Field of view blend rate for the sprint boost (degrees per second), and the time constant of the strafe tilt (s).
        private const float SprintFovBlendSpeed = 40f;
        private const float StrafeTiltSmoothTime = 0.1f;

        // ------------------------------------------------------------------ Camera feel

        private void OnLanded(float impactSpeed)
        {
            if (impactSpeed >= LandedEventMinSpeed)
            {
                Landed?.Invoke(impactSpeed);
            }

            // Kick the landing dip spring. A critically damped spring released from rest with velocity v peaks at
            // v / (w e), so the kick is sized for the wanted depth exactly.
            float depth = Mathf.Min((impactSpeed - LandingDipThreshold) * landingDip, LandingDipMax);
            if (depth > 0f && !reduceCameraMotion)
            {
                landingDipVelocity -= depth * LandingDipFrequency * 2.7182818f;
            }
        }

        private void UpdateCamera()
        {
            float dt = Time.deltaTime;

            // ---- Head bob. Measured ground speed, not input: pushing against a wall or falling does not bob, and
            // neither does riding a moving platform (its motion is not in measuredVelocity).
            Vector3 planarVelocity = measuredVelocity;
            planarVelocity.y = 0f;
            float speed = planarVelocity.magnitude;
            bool stepping = IsGrounded && !IsSliding && !IsPowerSliding && !IsMantling && !IsClimbing && speed > HeadBobMinSpeed;
            bool bobbing = headBob && stepping && !reduceCameraMotion;

            bobWeight = Mathf.MoveTowards(bobWeight, bobbing ? 1f : 0f, dt / HeadBobFadeTime);
            if (stepping)
            {
                // Advance by steps taken, wrapped to the two-step figure-of-eight cycle. A foot lands at the bottom of
                // each bounce (phase 0.75 and 1.75), which is where Footstep fires, even with the bob switched off.
                float advanced = bobPhase + speed * dt / headBobStepLength;
                if (Mathf.FloorToInt(advanced + 0.25f) != Mathf.FloorToInt(bobPhase + 0.25f))
                {
                    Footstep?.Invoke();
                }
                bobPhase = Mathf.Repeat(advanced, 2f);
            }
            else if (bobWeight <= 0f)
            {
                bobPhase = 0f;   // stopped and faded out: the next bob and footstep cycle starts from rest
            }

            // One vertical bounce per step, one sideways sway per two steps; the fade weight scales the whole offset.
            Vector3 offset = new Vector3(
                Mathf.Sin(bobPhase * Mathf.PI) * headBobSway,
                Mathf.Sin(bobPhase * 2f * Mathf.PI) * headBobAmplitude,
                0f) * bobWeight;

            // ---- Landing dip: a critically damped spring (stiffness w^2, damping 2w) pulled down by OnLanded, stepped
            // with its closed-form solution so the dip depth is exact at any frame rate (plain Euler halves it).
            float w = LandingDipFrequency;
            float decay = Mathf.Exp(-w * dt);
            float k = landingDipVelocity + w * landingDipOffset;
            landingDipOffset = (landingDipOffset + k * dt) * decay;
            landingDipVelocity = (landingDipVelocity - k * w * dt) * decay;

            // ---- Step smoothing: the lag set by UpdateMovement eases back to zero.
            stepSmoothOffset = Mathf.Lerp(stepSmoothOffset, 0f, 1f - Mathf.Exp(-dt / StepSmoothTime));

            offset.y += landingDipOffset + stepSmoothOffset;
            UpdateShake(dt);
            UpdateKick(dt);
            cameraTransform.localPosition = eyeLocalPosition + offset + leanOffset + shakePosition;

            // ---- Strafe tilt: lean into sideways movement, from the measured velocity so a wall stops the lean too.
            // A positive roll tilts the head left, so moving right rolls negative.
            float targetRoll = 0f;
            if (strafeTilt > 0f && IsGrounded && walkSpeed > 0f && !reduceCameraMotion)
            {
                float lateral = Vector3.Dot(planarVelocity, transform.right);
                targetRoll = -strafeTilt * Mathf.Clamp(lateral / walkSpeed, -1f, 1f);
            }
            roll = Mathf.Lerp(roll, targetRoll, 1f - Mathf.Exp(-dt / StrafeTiltSmoothTime));
            ApplyRotation();   // the strafe roll, the lean roll and the shake

            // ---- Field of view: the sprint boost blends in and out, the zoom scales the result. Unscaled time, so a
            // field of view set from a pause menu (time scale 0) shows at once. With neither in use the field of view
            // is left alone for other scripts.
            if (DrivesFieldOfView)
            {
                bool boosted = (IsSprinting || IsPowerSliding) && !reduceCameraMotion;
                float target = baseFieldOfView + (boosted ? sprintFovBoost : 0f);
                unzoomedFieldOfView = Mathf.MoveTowards(unzoomedFieldOfView, target, SprintFovBlendSpeed * Time.unscaledDeltaTime);
                playerCamera.fieldOfView = unzoomedFieldOfView * ZoomFactor;
                droveFieldOfView = true;
            }
            else if (droveFieldOfView)
            {
                // The boost and the zoom were both switched off (a movement profile, say): put the base field of view
                // back once, rather than leaving it mid-boost, and leave it to other scripts from here on.
                droveFieldOfView = false;
                zoomBlend = 0f;
                unzoomedFieldOfView = baseFieldOfView;
                playerCamera.fieldOfView = baseFieldOfView;
            }
        }

        // ------------------------------------------------------------------ Camera shake

        /// <summary>
        /// Shakes the camera: adds to <see cref="CameraShake"/>, which is capped at 1 and dies away by Shake Recovery
        /// per second. The camera moves by the square of it, so 0.2 is a tremor and 1 a blast, and shakes add up. The
        /// view swings around where the player is looking without changing it, and settles back exactly.
        /// </summary>
        public void AddCameraShake(float amount)
        {
            CameraShake = Mathf.Clamp01(CameraShake + Mathf.Max(0f, amount));
        }

        private void UpdateShake(float dt)
        {
            CameraShake = Mathf.MoveTowards(CameraShake, 0f, shakeRecovery * dt);
            float strength = CameraShake * CameraShake * (reduceCameraMotion ? reducedMotionShake : 1f);
            if (strength <= 0f)
            {
                shakeEuler = Vector3.zero;
                shakePosition = Vector3.zero;
                return;
            }

            // Smooth noise rather than random jumps, so a shake reads as a shudder at any frame rate. Each axis reads
            // its own row of the noise, so they move independently.
            shakeTime += dt * shakeFrequency;
            float angle = shakeAngle * strength;
            shakeEuler = new Vector3(ShakeNoise(0) * angle, ShakeNoise(1) * angle, ShakeNoise(2) * angle * 0.5f);
            shakePosition = new Vector3(ShakeNoise(3), ShakeNoise(4), 0f) * (shakeOffset * strength);
        }

        // Smooth noise from -1 to 1 along this player's stretch of one row of the Perlin field.
        private float ShakeNoise(int row)
        {
            return Mathf.PerlinNoise(shakeSeed + shakeTime, row * 7.31f + 0.5f) * 2f - 1f;
        }
    }
}
