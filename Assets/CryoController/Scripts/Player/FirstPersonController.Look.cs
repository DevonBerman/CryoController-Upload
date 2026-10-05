using UnityEngine;
using UnityEngine.InputSystem;

namespace CryoController
{
    // FirstPersonController: mouse and gamepad look, mouse smoothing, the stick response curve and acceleration, and zoom.
    // The settings, state and frame loop are in FirstPersonController.cs.
    public partial class FirstPersonController
    {
        // Stick deflection (0..1) from which the look stick counts as fully pushed for the acceleration boost.
        private const float StickFullDeflection = 0.9f;

        // Camera kick: the angular frequency (rad/s) of the spring that brings it back. 12 settles in about half a second.
        private const float KickFrequency = 12f;

        // ------------------------------------------------------------------ Look

        // Whether UpdateCamera writes the camera's field of view (sprint boost or zoom in use); otherwise it is left to
        // other scripts and FieldOfView applies directly.
        private bool DrivesFieldOfView => sprintFovBoost > 0f || (zoomAction != null && zoomFovScale < 1f);

        // 1 at rest, zoomFovScale fully zoomed: scales the field of view and the look sensitivity together.
        private float ZoomFactor => Mathf.Lerp(1f, zoomFovScale, zoomBlend);

        private void UpdateZoom()
        {
            IsZooming = zoomFovScale < 1f && ControlsActive && zoomAction != null && zoomAction.IsPressed() && !IsMantling
                        && !IsClimbing;
            // Unscaled time: the zoom is camera feel, and it releases cleanly even if the game pauses mid-zoom.
            zoomBlend = Mathf.MoveTowards(zoomBlend, IsZooming ? 1f : 0f, Time.unscaledDeltaTime / zoomTime);
        }

        private void UpdateLook()
        {
            if (lookSuppressFrames > 0)
            {
                lookSuppressFrames--;
                return;
            }

            Vector2 look = lookAction.ReadValue<Vector2>();
            InputDevice device = lookAction.activeControl?.device;   // null while the action is not actuated
            if (device != null)
            {
                lastLookDevice = device;
            }
            else
            {
                // A frame without movement still belongs to the device last used: mouse smoothing has to hand out the
                // rest of its average on those frames too, which are common with a mouse polled slower than the frame rate.
                device = lastLookDevice;
            }

            if (device is Pointer)
            {
                // Mouse, pen and touch deltas are pixels per frame: already frame-rate independent, so no deltaTime.
                stickFullTime = 0f;
                if (device is Mouse && Cursor.lockState != CursorLockMode.Locked)
                {
                    return;   // the mouse is free (Escape, lost focus): don't spin the view while clicking around
                }
                look *= mouseSensitivity;
                if (mouseSmoothing > 0)
                {
                    look = SmoothMouse(look);
                }
            }
            else
            {
                if (mouseSmoothing > 0)
                {
                    SmoothMouse(Vector2.zero);   // an idle mouse still feeds the average, so a pause does not leave stale deltas
                }

                // Sticks and hat switches are -1..1 and need deltaTime. The response curve reshapes the deflection
                // magnitude (direction is kept) so the centre of the stick is precise and the edge is fast.
                float deflection = Mathf.Min(look.magnitude, 1f);
                if (deflection > 0f)
                {
                    look *= Mathf.Pow(deflection, stickCurveExponent) / look.magnitude;
                }

                // Holding the stick at the edge ramps in a turn-speed boost, so a full push can swing the view around
                // while normal aiming keeps the base sensitivity.
                // Unscaled time: slow motion does not slow aiming (a pause turns input off instead), as with the mouse.
                stickFullTime = deflection >= StickFullDeflection ? stickFullTime + Time.unscaledDeltaTime : 0f;
                float boost = 1f;
                if (stickAccelerationBoost > 1f && stickFullTime > stickAccelerationDelay)
                {
                    float ramp = Mathf.Clamp01((stickFullTime - stickAccelerationDelay) / stickAccelerationRamp);
                    boost = Mathf.Lerp(1f, stickAccelerationBoost, ramp);
                }

                look *= stickSensitivity * boost * Time.unscaledDeltaTime;
                look.y *= stickVerticalScale;
            }

            // Zoomed in, the same hand movement covers the same part of the screen: sensitivity follows the field of view.
            look *= ZoomFactor;

            // Yaw turns the whole body; pitch tilts only the camera, clamped so you can't flip over.
            yaw = Mathf.Repeat(yaw + look.x, 360f);
            pitch = Mathf.Clamp(pitch + (invertY ? look.y : -look.y), -maxPitch, maxPitch);
            ApplyRotation();
        }

        private Vector2 SmoothMouse(Vector2 delta)
        {
            // Average the newest delta with the previous ones over the smoothing window.
            mouseDeltaHistory[mouseHistoryIndex] = delta;
            mouseHistoryIndex = (mouseHistoryIndex + 1) % MaxMouseSmoothing;
            mouseHistoryCount = Mathf.Min(mouseHistoryCount + 1, MaxMouseSmoothing);

            int window = Mathf.Min(mouseSmoothing, mouseHistoryCount);
            Vector2 sum = Vector2.zero;
            for (int i = 1; i <= window; i++)
            {
                sum += mouseDeltaHistory[(mouseHistoryIndex - i + MaxMouseSmoothing) % MaxMouseSmoothing];
            }
            return sum / window;
        }

        // ------------------------------------------------------------------ Look API

        /// <summary>
        /// Where the camera looks up or down, in degrees: positive looks down, negative up. Setting it turns the view at
        /// once, clamped to Max Pitch.
        /// </summary>
        public float Pitch
        {
            get => pitch;
            set
            {
                pitch = Mathf.Clamp(NormalizeAngle(value), -maxPitch, maxPitch);
                ApplyRotation();
            }
        }

        /// <summary>The direction the player faces, in degrees round the up axis (0 is world forward). Setting it turns the player at once.</summary>
        public float Yaw
        {
            get => yaw;
            set
            {
                yaw = Mathf.Repeat(value, 360f);
                ApplyRotation();
            }
        }

        /// <summary>Turns the player and camera at once to look at a point in the world, as far as Max Pitch allows.</summary>
        public void LookAt(Vector3 worldPoint)
        {
            Vector3 eye = cameraTransform != null ? cameraTransform.position : transform.position;
            Vector3 direction = worldPoint - eye;
            if (direction.sqrMagnitude < 1e-6f)
            {
                return;
            }
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude > 1e-6f)
            {
                yaw = Mathf.Repeat(Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, 360f);
            }
            pitch = Mathf.Clamp(-Mathf.Atan2(direction.y, flat.magnitude) * Mathf.Rad2Deg, -maxPitch, maxPitch);
            ApplyRotation();
        }

        /// <summary>
        /// Kicks the camera by these angles in degrees (x up, y right), as a weapon's recoil or a hit does. The kick
        /// springs back to where the player was looking within about half a second, and kicks add up. Aiming input still
        /// works during it, and Reduce Camera Motion scales it down like the shake.
        /// </summary>
        public void AddCameraKick(Vector2 degrees)
        {
            float scale = reduceCameraMotion ? reducedMotionShake : 1f;
            kickVelocity += new Vector2(-degrees.x, degrees.y) * (scale * KickFrequency * 2.7182818f);
        }

        // The kick is a critically damped spring per axis, stepped with its closed-form solution like the landing dip, so
        // a kick of n degrees peaks at n degrees at any frame rate.
        private void UpdateKick(float dt)
        {
            float w = KickFrequency;
            float decay = Mathf.Exp(-w * dt);
            Vector2 k = kickVelocity + w * kickOffset;
            kickOffset = (kickOffset + k * dt) * decay;
            kickVelocity = (kickVelocity - k * w * dt) * decay;
            if (kickOffset.sqrMagnitude < 1e-8f && kickVelocity.sqrMagnitude < 1e-8f)
            {
                kickOffset = Vector2.zero;
                kickVelocity = Vector2.zero;
            }
        }

        private void ApplyRotation()
        {
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            cameraTransform.localRotation = Quaternion.Euler(pitch + shakeEuler.x + kickOffset.x, shakeEuler.y + kickOffset.y,
                                                             roll + leanRoll + shakeEuler.z);
        }
    }
}
