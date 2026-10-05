using UnityEngine;

namespace CryoController
{
    /// <summary>
    /// A climbable ladder for <see cref="FirstPersonController"/>. Put it on an object with a trigger Collider covering
    /// the space in front of the rungs: walking into that space while facing the ladder, or falling into it, starts a
    /// climb. The object's up axis is the climb direction and its forward axis must point from the rungs into the wall,
    /// towards the platform the player steps onto at the top.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Ladder")]
    public class Ladder : MonoBehaviour
    {
        private Collider zone;

        /// <summary>Climb direction.</summary>
        public Vector3 Up => transform.up;

        /// <summary>From the rungs into the wall, towards the exit platform at the top.</summary>
        public Vector3 Forward => transform.forward;

        /// <summary>World height of the top of the climbable zone.</summary>
        public float TopY => zone != null ? zone.bounds.max.y : transform.position.y;

        /// <summary>
        /// False while this component, its object or its zone collider is switched off: the ladder can't be grabbed,
        /// and a player climbing it lets go.
        /// </summary>
        public bool IsClimbable => isActiveAndEnabled && zone != null && zone.enabled;

        // Whether the capsule really is in the zone. The zone's enter and exit messages can be missed (a collider
        // switched off inside a trigger never reports leaving it), so the player checks before grabbing.
        internal bool Reaches(Collider capsule)
        {
            return IsClimbable && capsule != null && zone.bounds.Intersects(capsule.bounds);
        }

        private void Awake()
        {
            zone = GetComponent<Collider>();
            zone.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            FirstPersonController player = other.GetComponent<FirstPersonController>();
            if (player != null)
            {
                player.EnterLadderZone(this);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            FirstPersonController player = other.GetComponent<FirstPersonController>();
            if (player != null)
            {
                player.ExitLadderZone(this);
            }
        }

        private void OnDrawGizmosSelected()
        {
            // Climb axis (green) and the direction into the wall (blue), so a misrotated ladder is obvious.
            Gizmos.color = Color.green;
            Gizmos.DrawRay(transform.position, transform.up * 2f);
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(transform.position, transform.forward);
        }
    }
}
