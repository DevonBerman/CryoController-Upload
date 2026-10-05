using UnityEngine;

namespace CryoController
{
    /// <summary>
    /// The footstep and landing sounds of one kind of ground (wood, metal, gravel), and how to recognise that ground:
    /// a <see cref="FootstepSurfaceMarker"/> on the object, its physics material, its tag, or the terrain layer painted
    /// under the player. <see cref="PlayerAudio"/> holds the list of surfaces and picks the first that matches.
    /// </summary>
    [CreateAssetMenu(fileName = "Footstep Surface", menuName = "CryoController/Footstep Surface")]
    public class FootstepSurface : ScriptableObject
    {
        [Header("Sounds")]
        [Tooltip("Footstep clips on this surface. Empty plays the Player Audio defaults.")]
        [SerializeField] private AudioClip[] footstepClips;
        [Tooltip("Landing clips on this surface. Empty plays the Player Audio defaults.")]
        [SerializeField] private AudioClip[] landClips;
        [Tooltip("Volume multiplier for footsteps and landings on this surface.")]
        [SerializeField, Range(0f, 2f)] private float volumeScale = 1f;

        [Header("Matching")]
        [Tooltip("Ground colliders with one of these physics materials are this surface.")]
        [SerializeField] private PhysicsMaterial[] physicsMaterials;
        [Tooltip("Ground objects with one of these tags are this surface.")]
        [SerializeField] private string[] tags;
        [Tooltip("Terrain painted mostly with one of these layers, where the player stands, is this surface.")]
        [SerializeField] private TerrainLayer[] terrainLayers;

        public AudioClip[] FootstepClips => footstepClips;
        public AudioClip[] LandClips => landClips;
        public float VolumeScale => volumeScale;

        /// <summary>True when this surface is recognised by terrain layers, so the caller needs to sample the terrain.</summary>
        public bool UsesTerrainLayers => terrainLayers != null && terrainLayers.Length > 0;

        /// <summary>Whether the ground collider (with the terrain layer under the player, if it is a terrain) is this surface.</summary>
        public bool Matches(Collider ground, TerrainLayer terrainLayer)
        {
            if (physicsMaterials != null && ground.sharedMaterial != null)
            {
                foreach (PhysicsMaterial material in physicsMaterials)
                {
                    if (material == ground.sharedMaterial)
                    {
                        return true;
                    }
                }
            }
            if (tags != null)
            {
                foreach (string tag in tags)
                {
                    if (!string.IsNullOrEmpty(tag) && ground.tag == tag)
                    {
                        return true;
                    }
                }
            }
            if (terrainLayer != null && terrainLayers != null)
            {
                foreach (TerrainLayer layer in terrainLayers)
                {
                    if (layer == terrainLayer)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
