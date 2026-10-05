using UnityEngine;

namespace CryoController
{
    /// <summary>
    /// Moves a platform back and forth along a line and/or turns it, with a kinematic Rigidbody, the way a lift, a
    /// walkway or a turntable would. <see cref="FirstPersonController"/> rides anything that moves, however it is
    /// moved (this component, an Animator, a script writing the transform); this is a ready-made mover for the demo
    /// and for blocking out levels.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Moving Platform")]
    public class MovingPlatform : MonoBehaviour
    {
        [Tooltip("Where the platform travels to, relative to where it starts, in metres. Zero for a platform that only turns.")]
        [SerializeField] private Vector3 travel = new Vector3(0f, 0f, 6f);
        [Tooltip("Travel speed in metres per second.")]
        [SerializeField, Min(0f)] private float speed = 2f;
        [Tooltip("Seconds the platform waits at each end of its travel.")]
        [SerializeField, Min(0f)] private float waitAtEnds = 1f;
        [Tooltip("Turn speed around the platform's up axis, in degrees per second. Zero for no turning.")]
        [SerializeField] private float turnSpeed;

        private Rigidbody body;
        private Vector3 startPosition;
        private Quaternion startRotation;
        private float progress;      // 0..1 along the travel
        private int direction = 1;   // +1 out, -1 back
        private float waitUntil;     // Time.time at which the next leg starts
        private float angle;         // degrees turned so far

        private void Awake()
        {
            // Kinematic and interpolated: the physics step moves it, and the pose seen in Update (what the player
            // rides) is smooth at any frame rate.
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            startPosition = transform.position;
            startRotation = transform.rotation;
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            float length = travel.magnitude;
            if (length > 0f && speed > 0f && Time.time >= waitUntil)
            {
                progress += direction * speed * dt / length;
                if (progress >= 1f || progress <= 0f)
                {
                    progress = Mathf.Clamp01(progress);
                    direction = -direction;
                    waitUntil = Time.time + waitAtEnds;
                }
            }
            angle = Mathf.Repeat(angle + turnSpeed * dt, 360f);

            body.MovePosition(startPosition + travel * progress);
            body.MoveRotation(startRotation * Quaternion.Euler(0f, angle, 0f));
        }

        private void OnDrawGizmosSelected()
        {
            // The travel, from the position authored in the scene (or the start position once playing).
            Vector3 from = Application.isPlaying ? startPosition : transform.position;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(from, from + travel);
            Gizmos.DrawWireCube(from + travel, transform.lossyScale);
        }
    }
}
