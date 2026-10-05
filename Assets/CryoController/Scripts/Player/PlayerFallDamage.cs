using UnityEngine;
using UnityEngine.Events;

namespace CryoController
{
    /// <summary>
    /// Turns hard landings into damage for your own health system. Landings faster than Safe Speed hurt, scaling up to
    /// Max Damage at Lethal Speed, and shake the camera to match. The package keeps no health: wire On Fall Damage in
    /// the Inspector, or subscribe to <see cref="FallDamaged"/>, and apply the damage there.
    /// </summary>
    [RequireComponent(typeof(FirstPersonController))]
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Player Fall Damage")]
    public class PlayerFallDamage : MonoBehaviour
    {
        [Tooltip("Landings up to this speed, in m/s, do no harm. With the default gravity (20) 11 m/s is a drop of " +
                 "about 3 m.")]
        [SerializeField, Min(0f)] private float safeSpeed = 11f;
        [Tooltip("Landings at this speed or faster, in m/s, do Max Damage; the damage scales in between. With the " +
                 "default gravity 20 m/s is a drop of about 10 m.")]
        [SerializeField, Min(0f)] private float lethalSpeed = 20f;
        [Tooltip("Damage of a landing at Lethal Speed or faster.")]
        [SerializeField, Min(0f)] private float maxDamage = 100f;
        [Tooltip("Camera shake added by a landing that does Max Damage; landings that do less shake less. 0 disables.")]
        [SerializeField, Range(0f, 1f)] private float cameraShake = 0.6f;
        [Tooltip("Called with the damage of each landing that does any. Wire it to your health system.")]
        [SerializeField] private UnityEvent<float> onFallDamage = new UnityEvent<float>();

        /// <summary>Raised with the damage of each landing that does any, and the impact speed in m/s.</summary>
        public event System.Action<float, float> FallDamaged;

        private FirstPersonController controller;

        private void Awake()
        {
            controller = GetComponent<FirstPersonController>();
        }

        private void OnEnable()
        {
            controller.Landed += OnLanded;
        }

        private void OnDisable()
        {
            controller.Landed -= OnLanded;
        }

        /// <summary>
        /// The damage a landing at this impact speed (m/s) does: none up to Safe Speed, Max Damage from Lethal Speed.
        /// </summary>
        public float DamageFor(float impactSpeed)
        {
            if (impactSpeed <= safeSpeed)
            {
                return 0f;
            }
            if (lethalSpeed <= safeSpeed)
            {
                return maxDamage;   // no ramp: anything past safe is lethal
            }
            return maxDamage * Mathf.Clamp01((impactSpeed - safeSpeed) / (lethalSpeed - safeSpeed));
        }

        private void OnLanded(float impactSpeed)
        {
            float damage = DamageFor(impactSpeed);
            if (damage <= 0f)
            {
                return;
            }

            if (cameraShake > 0f && maxDamage > 0f)
            {
                controller.AddCameraShake(cameraShake * damage / maxDamage);
            }
            FallDamaged?.Invoke(damage, impactSpeed);
            onFallDamage.Invoke(damage);
        }
    }
}
