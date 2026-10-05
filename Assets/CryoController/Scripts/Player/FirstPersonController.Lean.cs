using UnityEngine;

namespace CryoController
{
    // FirstPersonController: leaning and peeking, with the head kept out of walls. The settings, state and frame loop are
    // in FirstPersonController.cs.
    public partial class FirstPersonController
    {
        // Lean: radius of the sweep that keeps the head out of walls (m; wider than the camera's near plane), the gap
        // kept from them (m), and the smallest roll (degrees) that still swings the head about a pivot.
        private const float LeanProbeRadius = 0.2f;
        private const float LeanWallMargin = 0.05f;
        private const float LeanMinPivotAngle = 0.5f;

        // ------------------------------------------------------------------ Lean

        private void UpdateLean()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;   // paused: SmoothDamp would divide by the zero step
            }

            bool hasActions = leanLeftAction != null || leanRightAction != null;
            bool canLean = leanDistance > 0f && ControlsActive && hasActions && !IsSprinting && !IsPowerSliding && !IsMantling
                           && !IsClimbing;
            float target = 0f;
            if (leanToggle)
            {
                if (canLean && !PressesSuppressed)
                {
                    if (leanLeftAction != null && leanLeftAction.WasPressedThisFrame())
                    {
                        leanToggled = leanToggled == -1 ? 0 : -1;
                    }
                    if (leanRightAction != null && leanRightAction.WasPressedThisFrame())
                    {
                        leanToggled = leanToggled == 1 ? 0 : 1;
                    }
                }
                if (!canLean)
                {
                    leanToggled = 0;   // sprinting, sliding, climbing or a dialogue straightens up for good
                }
                target = leanToggled;
            }
            else if (canLean)
            {
                target = (leanRightAction != null && leanRightAction.IsPressed() ? 1f : 0f)
                         - (leanLeftAction != null && leanLeftAction.IsPressed() ? 1f : 0f);
            }

            float amount = Mathf.SmoothDamp(LeanAmount, target, ref leanVelocity, leanTime);
            if (Mathf.Abs(amount) < 0.0001f && target == 0f)
            {
                amount = 0f;
                leanVelocity = 0f;
            }
            LeanAmount = LimitLeanByWalls(amount);

            LeanPose(LeanAmount, out float lateral, out float drop);
            leanOffset = new Vector3(lateral, -drop, 0f);
            // Leaning left (negative) rolls the head left (positive roll). Reduced camera motion keeps the peek, not the roll.
            leanRoll = reduceCameraMotion ? 0f : -leanAngle * LeanAmount;
        }

        // Sideways offset and dip of the head at a lean amount: it swings about a pivot below the eyes, placed so a full
        // lean moves it leanDistance sideways while rolling leanAngle.
        private void LeanPose(float amount, out float lateral, out float drop)
        {
            if (leanAngle < LeanMinPivotAngle)
            {
                lateral = leanDistance * amount;
                drop = 0f;
                return;
            }
            float pivot = leanDistance / Mathf.Sin(leanAngle * Mathf.Deg2Rad);
            float angle = leanAngle * amount * Mathf.Deg2Rad;
            lateral = pivot * Mathf.Sin(angle);
            drop = pivot * (1f - Mathf.Cos(angle));
        }

        private float LimitLeanByWalls(float amount)
        {
            if (amount == 0f)
            {
                return 0f;
            }

            // Sweep from the upright eye towards the lean side. The sweep starts inside our own capsule, which casts
            // ignore, and the capsule keeps walls further away than the sweep's radius, so it never starts in a wall.
            LeanPose(amount, out float lateral, out _);
            float wanted = Mathf.Abs(lateral);
            Vector3 eye = cameraTransform.parent.TransformPoint(eyeLocalPosition);   // the camera may sit under a head object
            Vector3 side = transform.right * Mathf.Sign(amount);
            if (Physics.SphereCast(eye, LeanProbeRadius, side, out RaycastHit hit, wanted + LeanWallMargin,
                                   EnvironmentMask, QueryTriggerInteraction.Ignore))
            {
                float allowed = Mathf.Max(0f, hit.distance - LeanWallMargin);
                if (allowed < wanted)
                {
                    amount *= allowed / wanted;   // the pose is close to linear in the amount over a lean's range
                    leanVelocity = 0f;            // held at the wall, not building speed to spring past it later
                }
            }
            return amount;
        }
    }
}
