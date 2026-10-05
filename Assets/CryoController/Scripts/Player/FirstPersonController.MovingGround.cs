using UnityEngine;

namespace CryoController
{
    // FirstPersonController: riding moving ground, and keeping its motion after leaving it. The settings, state and frame
    // loop are in FirstPersonController.cs.
    public partial class FirstPersonController
    {
        // ------------------------------------------------------------------ Moving ground

        private void RideGround(float dt)
        {
            if (groundTransform == null)
            {
                GroundVelocity = Vector3.zero;
                return;
            }

            // Still ground is the common case and costs nothing: the pose is compared, not the position round-tripped
            // through the ground's matrix, which is not exact far from the origin.
            if (groundTransform.position == groundPosition && groundTransform.rotation == groundRotation)
            {
                GroundVelocity = Vector3.zero;
                return;
            }

            // Where the spot the player stood on has gone since the last move, and how far the ground has turned about
            // the vertical: the heading change of the ground's rotation delta, which stays sane when the ground also
            // tilts (Euler yaw does not).
            Vector3 target = groundTransform.TransformPoint(groundLocalPoint);
            Vector3 delta = target - transform.position;
            Vector3 heading = groundTransform.rotation * Quaternion.Inverse(groundRotation) * Vector3.forward;
            heading.y = 0f;
            float turn = heading.sqrMagnitude > 0.0001f ? Vector3.SignedAngle(Vector3.forward, heading, Vector3.up) : 0f;
            if (turn != 0f)
            {
                // Turn with the ground: the body, and the velocity, which is relative to the ground while standing on it.
                yaw = Mathf.Repeat(yaw + turn, 360f);
                ApplyRotation();
                horizontalVelocity = Quaternion.Euler(0f, turn, 0f) * horizontalVelocity;
            }
            riding = true;
            controller.Move(delta);
            riding = false;
            GroundVelocity = dt > 0f ? delta / dt : Vector3.zero;

            if (groundChanged)
            {
                // First ride on this ground: the velocity brought to it is world space, and from here on the ground
                // supplies its own share, so that share comes out of the player's.
                groundChanged = false;
                horizontalVelocity -= new Vector3(GroundVelocity.x, 0f, GroundVelocity.z);
            }
        }

        private void UpdateGroundContact()
        {
            Transform newGround = Ground != null ? Ground.transform : null;

            if (newGround != groundTransform)
            {
                // Changing ground (a landing, a jump or step off, stepping between platforms): what the old ground was
                // carrying becomes the player's own velocity, so a jump on a moving platform lands back on it and
                // stepping off a lift keeps its rise or fall, so a jump on a lift is the same jump relative to the lift
                // whichever way it moves. The new ground's share is measured on the first ride.
                Vector3 carry = new Vector3(GroundVelocity.x, 0f, GroundVelocity.z);
                horizontalVelocity += carry;
                if (newGround == null && GroundVelocity.y != 0f)
                {
                    verticalVelocity = Mathf.Max(verticalVelocity, 0f) + GroundVelocity.y;
                }
                airCarry = newGround == null ? carry : Vector3.zero;
                GroundVelocity = Vector3.zero;
                groundChanged = newGround != null;
            }

            groundTransform = newGround;
            if (groundTransform != null)
            {
                groundLocalPoint = groundTransform.InverseTransformPoint(transform.position);
                groundPosition = groundTransform.position;
                groundRotation = groundTransform.rotation;
            }
        }

        private void ClearGround()
        {
            Ground = null;
            groundTransform = null;
            GroundVelocity = Vector3.zero;
            groundChanged = false;
            airCarry = Vector3.zero;
        }
    }
}
