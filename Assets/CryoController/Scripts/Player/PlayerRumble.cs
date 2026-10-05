using UnityEngine;
using UnityEngine.InputSystem;

namespace CryoController
{
    /// <summary>
    /// Gamepad rumble for the player: a pulse on landing that grows with the impact, short pulses on a mantle and a throw
    /// (with <see cref="PlayerCarry"/>), and a light buzz while power sliding. Only the gamepad the player is actually
    /// using rumbles, never while input is off (a menu, a cutscene), the game is paused or the window is out of focus
    /// (unless the Input System's Background Behavior is Ignore Focus, so input still reaches the game), and the motors
    /// are stopped when the component is disabled.
    /// </summary>
    [RequireComponent(typeof(FirstPersonController))]
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Player Rumble")]
    public class PlayerRumble : MonoBehaviour
    {
        // Landings below this impact speed (m/s; the controller's landing dip starts here too) do not rumble.
        private const float SoftLandingSpeed = 4f;

        [Header("Landing")]
        [Tooltip("Motor strength, 0 to 1, at a landing of Hard Landing Speed or more. Softer landings rumble less. 0 disables.")]
        [SerializeField, Range(0f, 1f)] private float landingStrength = 0.6f;
        [Tooltip("Impact speed, in m/s, at which a landing rumbles at full Landing Strength. A 1.2 m jump lands at about 6.9 m/s.")]
        [SerializeField, Min(4.1f)] private float hardLandingSpeed = 12f;
        [Tooltip("Seconds a landing rumble lasts.")]
        [SerializeField, Min(0f)] private float landingDuration = 0.18f;

        [Header("Actions")]
        [Tooltip("Motor strength of the pulse when a mantle starts. 0 disables.")]
        [SerializeField, Range(0f, 1f)] private float mantleStrength = 0.2f;
        [Tooltip("Motor strength of the pulse when a carried object is thrown. 0 disables.")]
        [SerializeField, Range(0f, 1f)] private float throwStrength = 0.35f;
        [Tooltip("Seconds a mantle or throw pulse lasts.")]
        [SerializeField, Min(0f)] private float pulseDuration = 0.12f;
        [Tooltip("Motor strength while power sliding. 0 disables.")]
        [SerializeField, Range(0f, 1f)] private float slideStrength = 0.15f;

        /// <summary>The low-frequency (heavy) motor speed being sent, 0 to 1.</summary>
        public float LowFrequency { get; private set; }

        /// <summary>The high-frequency (light) motor speed being sent, 0 to 1.</summary>
        public float HighFrequency { get; private set; }

        private FirstPersonController controller;
        private PlayerCarry carry;   // optional
        private Gamepad rumbling;    // the pad the motors were last set on
        private float pulseLow;
        private float pulseHigh;
        private float pulseEnd;      // Time.unscaledTime the current pulse ends

        private void Awake()
        {
            controller = GetComponent<FirstPersonController>();
            carry = GetComponent<PlayerCarry>();
        }

        private void OnEnable()
        {
            controller.Landed += OnLanded;
            controller.Mantled += OnMantled;
            if (carry != null)
            {
                carry.Thrown += OnThrown;
            }
        }

        private void OnDisable()
        {
            controller.Landed -= OnLanded;
            controller.Mantled -= OnMantled;
            if (carry != null)
            {
                carry.Thrown -= OnThrown;
            }
            Stop();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                Stop();
            }
        }

        /// <summary>
        /// Starts a pulse on the gamepad in use. One that overlaps a pulse still running adds to it: each motor runs at
        /// the stronger of the two until the later one ends.
        /// </summary>
        public void Pulse(float low, float high, float seconds)
        {
            if (seconds <= 0f)
            {
                return;
            }
            float now = Time.unscaledTime;
            bool running = now < pulseEnd;
            pulseLow = Mathf.Max(running ? pulseLow : 0f, Mathf.Clamp01(low));
            pulseHigh = Mathf.Max(running ? pulseHigh : 0f, Mathf.Clamp01(high));
            pulseEnd = Mathf.Max(running ? pulseEnd : 0f, now + seconds);
        }

        private void OnLanded(float impactSpeed)
        {
            float strength = landingStrength * Mathf.InverseLerp(SoftLandingSpeed, hardLandingSpeed, impactSpeed);
            if (strength > 0f)
            {
                Pulse(strength, strength * 0.6f, landingDuration);
            }
        }

        private void OnMantled(float height)
        {
            if (mantleStrength > 0f)
            {
                Pulse(mantleStrength, mantleStrength * 0.5f, pulseDuration);
            }
        }

        private void OnThrown(Carryable thrown)
        {
            if (throwStrength > 0f)
            {
                Pulse(throwStrength * 0.5f, throwStrength, pulseDuration);
            }
        }

        private void Update()
        {
            Gamepad pad = controller.LastInputDevice as Gamepad;
            if (pad != rumbling)
            {
                Stop();   // the player switched devices: the old pad stops
                rumbling = pad;
            }

            float low = 0f;
            float high = 0f;
            if (pad != null && controller.InputEnabled && Time.timeScale > 0f && GetsInput())
            {
                if (Time.unscaledTime < pulseEnd)
                {
                    low = pulseLow;
                    high = pulseHigh;
                }
                if (controller.IsPowerSliding && slideStrength > 0f)
                {
                    low = Mathf.Max(low, slideStrength);
                    high = Mathf.Max(high, slideStrength * 0.5f);
                }
            }
            SetMotors(low, high);
        }

        // False while the window is out of focus and the Input System holds input back from it (its default).
        private static bool GetsInput()
        {
            return Application.isFocused
                || InputSystem.settings.backgroundBehavior == InputSettings.BackgroundBehavior.IgnoreFocus;
        }

        private void SetMotors(float low, float high)
        {
            if (rumbling == null || !rumbling.added)
            {
                LowFrequency = 0f;   // no pad to send to: nothing is rumbling
                HighFrequency = 0f;
                return;
            }
            if (Mathf.Approximately(low, LowFrequency) && Mathf.Approximately(high, HighFrequency))
            {
                return;   // only send when it changes
            }
            LowFrequency = low;
            HighFrequency = high;
            rumbling.SetMotorSpeeds(low, high);
        }

        private void Stop()
        {
            if (rumbling != null && rumbling.added)
            {
                rumbling.SetMotorSpeeds(0f, 0f);
            }
            LowFrequency = 0f;
            HighFrequency = 0f;
        }
    }
}
