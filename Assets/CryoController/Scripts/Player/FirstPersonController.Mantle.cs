using UnityEngine;

namespace CryoController
{
    // FirstPersonController: finding ledges and climbing onto them. The settings, state and frame loop are in
    // FirstPersonController.cs.
    public partial class FirstPersonController
    {
        // Mantling: how far in front of the capsule a ledge may be (m), the radius of the wall sweep (m), the largest
        // |normal.y| that still counts as a wall, the smallest normal.y that counts as a ledge top, the shortest climb (s)
        // and the forward input needed to try.
        private const float MantleReach = 0.5f;
        private const float MantleProbeRadius = 0.2f;
        private const float MantleWallMaxNormalY = 0.3f;
        private const float MantleLedgeMinNormalY = 0.7f;
        private const float MantleMinDuration = 0.2f;
        private const float MantleForwardInput = 0.3f;

        // Mantling: gap between the Step Offset and the bottom of the wall sweep (m), so steps the capsule walks up are
        // not taken for ledges.
        private const float MantleKneeClearance = 0.05f;

        // Mantling: how far past the Skin Width a wall may be (m) and still be climbed from the ground just by walking
        // into it. Walls further off are left for the capsule to reach first, so the riser two steps up a staircase is
        // not taken for a ledge.
        private const float AutoMantleReach = 0.1f;

        // ------------------------------------------------------------------ Mantling

        private Vector3 Feet => transform.TransformPoint(controller.center) - Vector3.up * (controller.height * 0.5f);

        // wallGap: how far the wall is from the capsule's surface (m). ledge: the transform of what the ledge top
        // belongs to, so a climb onto something moving can follow it.
        private bool TryFindLedge(Vector3 direction, out Vector3 ledgeFeet, out float ledgeHeight, out float wallGap,
                                  out Transform ledge, float maxHeight = -1f)
        {
            ledgeFeet = Vector3.zero;
            ledgeHeight = 0f;
            wallGap = float.PositiveInfinity;
            ledge = null;

            // A wall in front, facing the player, anywhere from just above step height up to the chest: a capsule sweep,
            // so a knee-high ledge is found as well as a chest-high one. It starts inside our own capsule, which casts
            // ignore. Pushable props (non-kinematic rigidbodies) are pushed, not climbed.
            Vector3 feet = Feet;
            Vector3 chest = feet + Vector3.up * (controller.height * 0.5f);
            Vector3 knee = feet + Vector3.up * (controller.stepOffset + MantleKneeClearance + MantleProbeRadius);
            if (knee.y > chest.y)
            {
                knee = chest;   // crouched: the capsule is too short for two heights
            }
            if (!Physics.CapsuleCast(knee, chest, MantleProbeRadius, direction, out RaycastHit wall, controller.radius + MantleReach,
                                     EnvironmentMask, QueryTriggerInteraction.Ignore)
                || Mathf.Abs(wall.normal.y) > MantleWallMaxNormalY
                || Vector3.Dot(wall.normal, direction) > -0.5f
                || IsPushable(wall.rigidbody))
            {
                return false;
            }

            // The sweep starts on the capsule's axis, so the probe's front is (radius - probe radius) behind the capsule's.
            wallGap = wall.distance - (controller.radius - MantleProbeRadius);
            return TryFindLedgeAt(wall.point, -wall.normal, controller.stepOffset + 0.05f, out ledgeFeet, out ledgeHeight,
                                  out ledge, maxHeight);
        }

        // maxHeight: the highest ledge above the feet that counts; below 0, Mantle Max Height.
        private bool TryFindLedgeAt(Vector3 wallPoint, Vector3 intoWall, float minHeight, out Vector3 ledgeFeet,
                                    out float ledgeHeight, out Transform ledge, float maxHeight = -1f)
        {
            ledgeFeet = Vector3.zero;
            ledgeHeight = 0f;
            ledge = null;
            float highest = maxHeight >= 0f ? maxHeight : mantleMaxHeight;

            float radius = controller.radius;
            Vector3 feet = Feet;

            // The ledge top: a ray down from above the highest climbable height, a capsule-width past the wall face,
            // reaching down to the lowest height of interest. The top must be walkable.
            Vector3 probeTop = wallPoint + intoWall * (radius + 0.05f);
            probeTop.y = feet.y + highest + radius;
            float probeLength = highest + radius - minHeight;
            if (!Physics.Raycast(probeTop, Vector3.down, out RaycastHit top, probeLength, EnvironmentMask,
                                 QueryTriggerInteraction.Ignore)
                || top.normal.y < MantleLedgeMinNormalY
                || IsPushable(top.rigidbody))
            {
                return false;
            }

            ledgeHeight = top.point.y - feet.y;
            ledge = top.collider.transform;
            if (ledgeHeight < minHeight || ledgeHeight > highest)
            {
                return false;
            }

            // 3. Room for the capsule standing on the ledge (anything but ourselves in the way means no).
            ledgeFeet = top.point + Vector3.up * (controller.skinWidth + 0.02f);
            Vector3 bottomSphere = ledgeFeet + Vector3.up * radius;
            Vector3 topSphere = ledgeFeet + Vector3.up * (controller.height - radius);
            int overlaps = Physics.OverlapCapsuleNonAlloc(bottomSphere, topSphere, radius * 0.95f, overlapBuffer,
                                                          EnvironmentMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < overlaps; i++)
            {
                if (overlapBuffer[i] != controller)
                {
                    return false;
                }
            }
            return true;
        }

        // A prop the player shoves around rather than climbs. With Push Force at 0 nothing is pushed, so a loose prop
        // is climbed like any other ledge.
        private bool IsPushable(Rigidbody body)
        {
            return pushForce > 0f && body != null && !body.isKinematic;
        }

        private void StartMantle(Vector3 ledgeFeet, float ledgeHeight, Transform ledge)
        {
            IsMantling = true;
            mantleStart = transform.position;
            mantleEnd = transform.position + (ledgeFeet - Feet);
            mantleLedge = ledge;
            mantleEndLocal = ledge != null ? ledge.InverseTransformPoint(mantleEnd) : mantleEnd;
            mantleTime = 0f;
            float heightFraction = mantleMaxHeight > 0f ? Mathf.Clamp01(ledgeHeight / mantleMaxHeight) : 0f;
            mantleTotal = Mathf.Max(MantleMinDuration, mantleDuration * heightFraction);

            // The climb drives the transform directly: a capsule pushed up along the wall would catch on the edge. A
            // trigger never reports the exit of a collider switched off inside it, so a ladder zone is let go here; one
            // the capsule is still in when switched back on reports a fresh entry.
            controller.enabled = false;
            ladderInReach = null;
            waterInReach = null;
            verticalVelocity = 0f;
            horizontalVelocity = Vector3.zero;
            slideSpeed = 0f;
            IsGrounded = false;
            ClearGround();
            IsSprinting = false;
            IsSliding = false;
            EndPowerSlide();
            jumpRequestedTime = float.NegativeInfinity;
            lastGroundedTime = float.NegativeInfinity;

            Mantled?.Invoke(ledgeHeight);
        }

        private void UpdateMantle()
        {
            mantleTime += Time.deltaTime;
            float t = Mathf.Clamp01(mantleTime / mantleTotal);

            // The end follows the ledge, so a climb onto a lift or a moving platform ends on it, not where it was.
            if (mantleLedge != null)
            {
                mantleEnd = mantleLedge.TransformPoint(mantleEndLocal);
            }

            // Rise first, then move onto the ledge, with the two eased and overlapping so it reads as one motion.
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.7f));
            float advance = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.4f) / 0.6f));
            transform.position = new Vector3(
                Mathf.Lerp(mantleStart.x, mantleEnd.x, advance),
                Mathf.Lerp(mantleStart.y, mantleEnd.y, rise),
                Mathf.Lerp(mantleStart.z, mantleEnd.z, advance));

            if (t >= 1f)
            {
                EndMantle();
            }
        }

        private void EndMantle()
        {
            IsMantling = false;
            mantleLedge = null;
            controller.enabled = true;
            wasGrounded = true;
            IsGrounded = true;
            lastGroundedTime = Time.time;
            airJumpsUsed = 0;

            // A ladder whose zone still overlaps the capsule (the climb just came off one, or its zone reaches the
            // platform) must not be grabbed straight back; the CharacterController's stale grounded flag would allow it.
            ladderCooldownUntil = Time.time + LadderCooldown;
        }
    }
}
