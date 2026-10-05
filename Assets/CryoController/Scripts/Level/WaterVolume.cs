using UnityEngine;

namespace CryoController
{
    /// <summary>
    /// Water the player swims in. Put it on an object with a trigger Collider filling the water: the top of the
    /// collider's bounds is the surface. A <see cref="FirstPersonController"/> inside it swims once the water at its feet
    /// is deeper than its Swim Depth, and wades through shallower water. The water's look (a surface mesh and material)
    /// is up to you; this tints the screen while the camera is under the surface, in any render pipeline.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Water Volume")]
    public class WaterVolume : MonoBehaviour
    {
        [Tooltip("A current that carries a swimming player along, in metres per second, in world space.")]
        [SerializeField] private Vector3 flow;
        [Tooltip("Laid over the screen while the camera is under the surface. An alpha of 0 shows nothing.")]
        [SerializeField] private Color underwaterTint = new Color(0.08f, 0.32f, 0.42f, 0.5f);

        private Collider zone;

        /// <summary>World height of the water's surface: the top of the collider's bounds.</summary>
        public float SurfaceY => zone != null ? zone.bounds.max.y : transform.position.y;

        /// <summary>The current that carries a swimming player, in metres per second.</summary>
        public Vector3 Flow => flow;

        private void Awake()
        {
            zone = GetComponent<Collider>();
            zone.isTrigger = true;
        }

        /// <summary>Whether the point is inside the water: below the surface and within the collider's bounds.</summary>
        public bool IsUnder(Vector3 point)
        {
            if (!isActiveAndEnabled || zone == null || !zone.enabled)
            {
                return false;
            }
            Bounds bounds = zone.bounds;
            return point.y < bounds.max.y && point.y > bounds.min.y && point.x > bounds.min.x && point.x < bounds.max.x
                   && point.z > bounds.min.z && point.z < bounds.max.z;
        }

        // Whether the capsule really is in the water. Trigger messages can be missed (a collider switched off inside a
        // trigger never reports leaving it), so the player checks every frame it swims.
        internal bool Reaches(Collider capsule)
        {
            return isActiveAndEnabled && zone != null && zone.enabled && capsule != null && zone.bounds.Intersects(capsule.bounds);
        }

        private void OnTriggerEnter(Collider other)
        {
            FirstPersonController player = other.GetComponent<FirstPersonController>();
            if (player != null)
            {
                player.EnterWater(this);
            }
        }

        private void OnTriggerStay(Collider other)
        {
            // A player that left another volume while still in this one (two overlapping pools) picks this one up again.
            FirstPersonController player = other.GetComponent<FirstPersonController>();
            if (player != null && player.Water == null)
            {
                player.EnterWater(this);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            FirstPersonController player = other.GetComponent<FirstPersonController>();
            if (player != null)
            {
                player.ExitWater(this);
            }
        }

        private void OnGUI()
        {
            if (underwaterTint.a <= 0f || Event.current.type != EventType.Repaint)
            {
                return;
            }
            Camera view = Camera.main;
            if (view != null && IsUnder(view.transform.position))
            {
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture, ScaleMode.StretchToFill,
                                true, 0f, underwaterTint, 0f, 0f);
            }
        }

        private void OnDrawGizmosSelected()
        {
            // The surface (cyan), so a misplaced collider is obvious.
            Collider shape = zone != null ? zone : GetComponent<Collider>();
            if (shape == null)
            {
                return;
            }
            Bounds bounds = shape.bounds;
            Gizmos.color = new Color(0f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireCube(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z), new Vector3(bounds.size.x, 0f, bounds.size.z));
        }
    }
}
