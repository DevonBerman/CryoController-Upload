using UnityEngine;

namespace CryoController
{
    /// <summary>
    /// Sprint stamina for <see cref="FirstPersonController"/>: sprinting drains it, resting refills it after a short
    /// delay, and running dry blocks sprinting until some has come back so it cannot stutter on and off at zero. Draws a
    /// small bar while not full. Other systems can read <see cref="Stamina"/> / <see cref="Normalized"/> and subscribe to
    /// <see cref="Exhausted"/> / <see cref="Recovered"/>.
    /// </summary>
    [RequireComponent(typeof(FirstPersonController))]
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Player Stamina")]
    public class PlayerStamina : MonoBehaviour, ISaveable
    {
        // The bar's size and its gap from the bottom of the screen, for a 1080-line screen and scaled with the real one.
        private const float BarWidth = 220f;
        private const float BarHeight = 6f;
        private const float BarBottomMargin = 48f;

        [Tooltip("Stamina points when full.")]
        [SerializeField, Min(1f)] private float maxStamina = 100f;
        [Tooltip("Points drained per second of sprinting. 20 gives 5 s of sprint from full.")]
        [SerializeField, Min(0f)] private float sprintDrainPerSecond = 20f;
        [Tooltip("Points regained per second while not sprinting.")]
        [SerializeField, Min(0f)] private float regenPerSecond = 25f;
        [Tooltip("Seconds after sprinting stops before stamina starts to regenerate.")]
        [SerializeField, Min(0f)] private float regenDelay = 1f;
        [Tooltip("After running dry, sprinting stays blocked until stamina is back above this fraction of the maximum.")]
        [SerializeField, Range(0f, 1f)] private float recoverFraction = 0.25f;
        [Tooltip("Draw a small stamina bar at the bottom of the screen while stamina is not full.")]
        [SerializeField] private bool showBar = true;
        [Tooltip("Bar colour while stamina is draining or regenerating.")]
        [SerializeField] private Color barColor = Color.white;
        [Tooltip("Bar colour while sprinting is blocked after running dry.")]
        [SerializeField] private Color exhaustedColor = new Color(1f, 0.4f, 0.3f, 1f);

        /// <summary>Current stamina points.</summary>
        public float Stamina { get; private set; }

        /// <summary>Stamina points when full.</summary>
        public float MaxStamina => maxStamina;

        /// <summary>Stamina as a 0..1 fraction of the maximum.</summary>
        public float Normalized => maxStamina > 0f ? Stamina / maxStamina : 0f;

        /// <summary>True from running dry until stamina has recovered past Recover Fraction.</summary>
        public bool IsExhausted { get; private set; }

        /// <summary>Raised when stamina runs out and sprinting is blocked.</summary>
        public event System.Action Exhausted;

        /// <summary>Raised when sprinting is allowed again after exhaustion.</summary>
        public event System.Action Recovered;

        private FirstPersonController controller;
        private float regenTimer;
        private bool vetoing;   // this component has set SprintAllowed false

        private void Awake()
        {
            controller = GetComponent<FirstPersonController>();
            Stamina = maxStamina;
        }

        private void OnDisable()
        {
            if (vetoing)
            {
                controller.SprintAllowed = true;   // do not leave sprinting blocked when this system is switched off
                vetoing = false;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (controller.IsSprinting)
            {
                Stamina = Mathf.Max(0f, Stamina - sprintDrainPerSecond * dt);
                regenTimer = regenDelay;
                if (Stamina <= 0f && !IsExhausted)
                {
                    IsExhausted = true;
                    Exhausted?.Invoke();
                }
            }
            else
            {
                regenTimer -= dt;
                if (regenTimer <= 0f)
                {
                    Stamina = Mathf.Min(maxStamina, Stamina + regenPerSecond * dt);
                }
            }

            if (IsExhausted && Normalized >= recoverFraction)
            {
                IsExhausted = false;
                Recovered?.Invoke();
            }

            // Only on a change, so a veto of your own set in between (heavy armour, a sprained ankle) stays in place.
            bool veto = IsExhausted || Stamina <= 0f;
            if (veto != vetoing)
            {
                vetoing = veto;
                controller.SprintAllowed = !veto;
            }
        }

        [System.Serializable]
        private struct SavedState
        {
            public float stamina;
            public bool exhausted;
        }

        /// <summary>For <see cref="SaveSystem"/>: the stamina and whether sprinting is blocked.</summary>
        public string CaptureState()
        {
            return JsonUtility.ToJson(new SavedState { stamina = Stamina, exhausted = IsExhausted });
        }

        /// <summary>For <see cref="SaveSystem"/>.</summary>
        public void RestoreState(string state)
        {
            SavedState saved = JsonUtility.FromJson<SavedState>(state);
            Stamina = Mathf.Clamp(saved.stamina, 0f, maxStamina);
            IsExhausted = saved.exhausted;
        }

        /// <summary>Refills stamina and clears exhaustion, for pickups or checkpoints.</summary>
        public void Refill()
        {
            Stamina = maxStamina;
            if (IsExhausted)
            {
                IsExhausted = false;
                Recovered?.Invoke();
            }
        }

        private void OnGUI()
        {
            if (!showBar || Stamina >= maxStamina || !controller.InputEnabled)
            {
                return;   // nothing to show, or a menu is up
            }

            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            float scale = HudDrawing.Scale;
            float width = BarWidth * scale;
            float height = Mathf.Max(3f, BarHeight * scale);
            Rect bar = new Rect(Mathf.Round((Screen.width - width) * 0.5f), Mathf.Round(Screen.height - BarBottomMargin * scale),
                                width, height);
            Color color = IsExhausted ? exhaustedColor : barColor;
            float pad = 2f * scale;
            HudDrawing.Box(new Rect(bar.x - pad, bar.y - pad, bar.width + pad * 2f, bar.height + pad * 2f),
                           new Color(0f, 0f, 0f, 0.45f), (bar.height + pad * 2f) * 0.5f);
            HudDrawing.Bar(bar, Normalized, color, HudDrawing.Fade(color, 0.25f));
        }
    }
}
