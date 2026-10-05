using UnityEngine;

namespace CryoController
{
    /// <summary>
    /// Makes something held in view (a flashlight, a tool, a weapon model) move like it is held: it lags behind as the
    /// view turns and springs back, bobs with the player's steps, and lowers out of sight while the hands are busy
    /// (climbing, mantling, swimming, carrying, reading). Put it on a child of the player's camera holding the model, at
    /// the pose it rests in; it moves that object relative to the pose.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Held Item")]
    public class HeldItem : MonoBehaviour
    {
        [Tooltip("Degrees the item lags per degree the view turns in a frame. 0 holds it rigidly.")]
        [SerializeField, Min(0f)] private float swayAmount = 0.6f;
        [Tooltip("The most the item lags behind, in degrees.")]
        [SerializeField, Min(0f)] private float maxSway = 6f;
        [Tooltip("How quickly the item catches up with the view, per second. Higher is stiffer.")]
        [SerializeField, Min(0.1f)] private float swayRecovery = 8f;
        [Tooltip("How far the item bobs with each step, in metres.")]
        [SerializeField, Min(0f)] private float bobAmount = 0.012f;
        [Tooltip("Metres walked per bob cycle (two steps).")]
        [SerializeField, Min(0.1f)] private float bobStepLength = 2f;
        [Tooltip("How far down the item goes while the hands are busy, in metres. 0 keeps it up.")]
        [SerializeField, Min(0f)] private float lowerDistance = 0.35f;
        [Tooltip("How quickly the item lowers and raises, in fractions of the way per second.")]
        [SerializeField, Min(0.1f)] private float lowerSpeed = 4f;

        /// <summary>From 0 (up, in use) to 1 (fully lowered out of sight).</summary>
        public float Lowered { get; private set; }

        /// <summary>True while the hands are busy and the item is lowering or lowered.</summary>
        public bool IsLowering { get; private set; }

        /// <summary>Lowers the item while true, whatever the player is doing: for a cutscene, or a weapon swap.</summary>
        public bool ForceLowered { get; set; }

        private FirstPersonController player;
        private PlayerCarry carry;
        private PlayerReader reader;
        private Vector3 restPosition;
        private Quaternion restRotation;
        private Vector2 sway;          // the item's lag (pitch, yaw), in degrees
        private float lastYaw;
        private float lastPitch;
        private float bobPhase;
        private float bobWeight;

        private void Awake()
        {
            player = GetComponentInParent<FirstPersonController>();
            if (player != null)
            {
                carry = player.GetComponent<PlayerCarry>();
                reader = player.GetComponent<PlayerReader>();
            }
            restPosition = transform.localPosition;
            restRotation = transform.localRotation;
        }

        private void OnEnable()
        {
            if (player != null)
            {
                lastYaw = player.Yaw;
                lastPitch = player.Pitch;
            }
            sway = Vector2.zero;
        }

        private void OnDisable()
        {
            transform.localPosition = restPosition;
            transform.localRotation = restRotation;
        }

        private void LateUpdate()
        {
            if (player == null)
            {
                return;
            }
            float dt = Time.deltaTime;

            // Sway: the item is pulled behind the turn, then catches up.
            float yawDelta = Mathf.DeltaAngle(lastYaw, player.Yaw);
            float pitchDelta = player.Pitch - lastPitch;
            lastYaw = player.Yaw;
            lastPitch = player.Pitch;
            if (dt > 0f)
            {
                sway += new Vector2(-pitchDelta, -yawDelta) * swayAmount;
                sway = Vector2.ClampMagnitude(sway, maxSway);
                sway = Vector2.Lerp(sway, Vector2.zero, 1f - Mathf.Exp(-swayRecovery * dt));
            }

            // Bob: a figure of eight with the player's own steps on the ground.
            Vector3 planar = player.Velocity - player.GroundVelocity;
            planar.y = 0f;
            float speed = player.IsGrounded ? planar.magnitude : 0f;
            bobWeight = Mathf.MoveTowards(bobWeight, speed > 0.1f && !player.ReduceCameraMotion ? 1f : 0f, dt * 5f);
            bobPhase = Mathf.Repeat(bobPhase + speed * dt / bobStepLength, 1f);
            float angle = bobPhase * Mathf.PI * 2f;
            Vector3 bob = new Vector3(Mathf.Sin(angle) * bobAmount, -Mathf.Abs(Mathf.Cos(angle)) * bobAmount, 0f) * bobWeight;

            // Lowered while the hands are busy.
            IsLowering = ForceLowered || player.IsClimbing || player.IsMantling || player.IsSwimming
                         || (carry != null && carry.IsCarrying) || (reader != null && reader.IsReading);
            Lowered = Mathf.MoveTowards(Lowered, IsLowering ? 1f : 0f, lowerSpeed * dt);
            float lower = Mathf.SmoothStep(0f, 1f, Lowered);

            transform.localPosition = restPosition + bob + Vector3.down * (lowerDistance * lower);
            transform.localRotation = restRotation * Quaternion.Euler(sway.x + 30f * lower, sway.y, 0f);
        }
    }
}
