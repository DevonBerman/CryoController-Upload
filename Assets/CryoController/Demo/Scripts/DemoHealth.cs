using UnityEngine;

namespace CryoController.Demo
{
    /// <summary>
    /// Demo only: a simple health bar fed by <see cref="PlayerFallDamage"/>, to show how its damage reaches your own
    /// health system. Health comes back after a moment; at zero the player is put back where they started.
    /// </summary>
    [AddComponentMenu("CryoController/Demo/Demo Health")]
    public class DemoHealth : MonoBehaviour
    {
        [Tooltip("Health when full.")]
        [SerializeField, Min(1f)] private float maxHealth = 100f;
        [Tooltip("Seconds after a hurt before health starts coming back.")]
        [SerializeField, Min(0f)] private float regenDelay = 3f;
        [Tooltip("Health regained per second.")]
        [SerializeField, Min(0f)] private float regenPerSecond = 25f;
        [Tooltip("First row of the demo's on-screen text this uses (it takes two).")]
        [SerializeField, Min(0)] private int row = 1;
        [Tooltip("Shown under the bar until the player gets hurt.")]
        [SerializeField] private string hint = "Climb the ladder up the tower and jump off to try fall damage.";

        private const float MessageTime = 4f;

        private FirstPersonController player;
        private PlayerFallDamage fallDamage;
        private Vector3 spawnPosition;
        private float spawnYaw;
        private float health;
        private float lastHurtTime = float.NegativeInfinity;
        private bool knockedOut;
        private string message;
        private float messageUntil;

        private void Start()
        {
            player = FindAnyObjectByType<FirstPersonController>();
            fallDamage = player != null ? player.GetComponent<PlayerFallDamage>() : null;
            if (fallDamage == null)
            {
                enabled = false;
                return;
            }
            fallDamage.FallDamaged += OnFallDamaged;
            spawnPosition = player.transform.position;
            spawnYaw = player.transform.eulerAngles.y;
            health = maxHealth;
        }

        private void OnDestroy()
        {
            if (fallDamage != null)
            {
                fallDamage.FallDamaged -= OnFallDamaged;
            }
        }

        private void OnFallDamaged(float damage, float impactSpeed)
        {
            health = Mathf.Max(0f, health - damage);
            lastHurtTime = Time.time;
            Show($"Landed at {impactSpeed:0.0} m/s: {damage:0} damage");
            knockedOut = health <= 0f;   // respawned in Update: this runs in the middle of the player's own move
        }

        private void Update()
        {
            if (knockedOut)
            {
                knockedOut = false;
                player.Teleport(spawnPosition, spawnYaw);
                health = maxHealth;
                Show("Knocked out: back to the start");
            }
            else if (Time.time - lastHurtTime >= regenDelay)
            {
                health = Mathf.MoveTowards(health, maxHealth, regenPerSecond * Time.deltaTime);
            }
        }

        private void Show(string text)
        {
            message = text;
            messageUntil = Time.time + MessageTime;
        }

        private void OnGUI()
        {
            if (player == null || !player.InputEnabled)
            {
                return;
            }
            float fraction = health / maxHealth;
            DemoHud.Bar(row, fraction, Color.Lerp(new Color(0.9f, 0.25f, 0.2f), new Color(0.35f, 0.85f, 0.4f), fraction));
            DemoHud.Label(row + 1, Time.time < messageUntil ? message : hint);
        }
    }
}
