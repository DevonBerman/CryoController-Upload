using UnityEngine;

namespace CryoController
{
    /// <summary>
    /// Names the <see cref="FootstepSurface"/> of this object and everything under it, for ground that has no physics
    /// material or tag to go by. A marker wins over the matching rules on the surfaces themselves.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Footstep Surface Marker")]
    public class FootstepSurfaceMarker : MonoBehaviour
    {
        [Tooltip("Surface for footsteps and landings on this object and its children.")]
        [SerializeField] private FootstepSurface surface;

        public FootstepSurface Surface => surface;
    }
}
