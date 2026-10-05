using UnityEngine;

namespace CryoController
{
    /// <summary>
    /// Plays footstep, jump and landing sounds from <see cref="FirstPersonController"/>'s events. Footsteps are quieter
    /// while crouching and louder while sprinting; landings scale with impact speed. Each list picks a random clip that
    /// differs from the previous one and every sound gets a little pitch variation, so repeats do not sound mechanical.
    /// Leave a list empty to silence that sound. Needs no wiring: an AudioSource is added at runtime if none is set.
    /// Ground with its own sounds is described by <see cref="FootstepSurface"/> assets listed here.
    /// </summary>
    [RequireComponent(typeof(FirstPersonController))]
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Player Audio")]
    public class PlayerAudio : MonoBehaviour
    {
        // Landed reports nothing slower than this (m/s); it is the soft end of the landing volume range.
        private const float SoftLandingSpeed = 2.5f;

        [Header("Output")]
        [Tooltip("AudioSource all sounds play through, for example one routed to a mixer group. Left empty, a 2D " +
                 "AudioSource is added to this object when the game starts.")]
        [SerializeField] private AudioSource source;

        [Header("Footsteps")]
        [Tooltip("Clips to choose from on each footstep. Leave empty for no footstep sounds.")]
        [SerializeField] private AudioClip[] footstepClips;
        [Tooltip("Footstep volume while walking.")]
        [SerializeField, Range(0f, 1f)] private float footstepVolume = 0.6f;
        [Tooltip("Footstep volume multiplier while crouching.")]
        [SerializeField, Range(0f, 1f)] private float crouchVolumeScale = 0.4f;
        [Tooltip("Footstep volume multiplier while sprinting.")]
        [SerializeField, Range(0f, 2f)] private float sprintVolumeScale = 1.3f;

        [Header("Jump and landing")]
        [Tooltip("Clips to choose from when a jump starts.")]
        [SerializeField] private AudioClip[] jumpClips;
        [Tooltip("Volume of the jump sound.")]
        [SerializeField, Range(0f, 1f)] private float jumpVolume = 0.7f;
        [Tooltip("Clips to choose from on landing.")]
        [SerializeField] private AudioClip[] landClips;
        [Tooltip("Landing volume at the softest reported landing (about 2.5 m/s, stepping off a stair).")]
        [SerializeField, Range(0f, 1f)] private float landVolumeSoft = 0.3f;
        [Tooltip("Landing volume at Hard Landing Speed and above.")]
        [SerializeField, Range(0f, 1f)] private float landVolumeHard = 1f;
        [Tooltip("Impact speed, in m/s, at which a landing is at full volume. A 1.2 m jump lands at about 6.9 m/s, " +
                 "a 3 m fall at about 11 m/s.")]
        [SerializeField, Min(0.1f)] private float hardLandingSpeed = 12f;

        [Header("Power slide")]
        [Tooltip("Clips to choose from when a power slide starts.")]
        [SerializeField] private AudioClip[] slideClips;
        [Tooltip("Volume of the power slide sound.")]
        [SerializeField, Range(0f, 1f)] private float slideVolume = 0.6f;

        [Header("Variation")]
        [Tooltip("Random pitch variation applied to every sound, as a fraction (0.05 = plus or minus 5 %).")]
        [SerializeField, Range(0f, 0.5f)] private float pitchVariation = 0.05f;

        [Header("Surfaces")]
        [Tooltip("Surfaces with their own sounds, recognised by a Footstep Surface Marker on the ground object, its " +
                 "physics material, its tag or the terrain layer under the player. The first match wins; ground that " +
                 "matches none plays the clips above.")]
        [SerializeField] private FootstepSurface[] surfaces;
        [Tooltip("Surface for the rung footsteps while climbing a ladder. Empty plays the default footsteps.")]
        [SerializeField] private FootstepSurface ladderSurface;

        private FirstPersonController controller;
        private int lastFootstepClip = -1;
        private int lastJumpClip = -1;
        private int lastLandClip = -1;
        private int lastSlideClip = -1;

        private void Awake()
        {
            controller = GetComponent<FirstPersonController>();

            if (source == null)
            {
                source = GetComponent<AudioSource>();
            }
            if (source == null)
            {
                source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;   // the player's own sounds are heard flat, not positioned in the world
            }
        }

        private void OnEnable()
        {
            controller.Footstep += OnFootstep;
            controller.Jumped += OnJumped;
            controller.Landed += OnLanded;
            controller.PowerSlideStarted += OnPowerSlide;
        }

        private void OnDisable()
        {
            controller.Footstep -= OnFootstep;
            controller.Jumped -= OnJumped;
            controller.Landed -= OnLanded;
            controller.PowerSlideStarted -= OnPowerSlide;
        }

        private void OnFootstep()
        {
            float volume = footstepVolume;
            if (controller.IsCrouching)
            {
                volume *= crouchVolumeScale;
            }
            else if (controller.IsSprinting)
            {
                volume *= sprintVolumeScale;
            }

            FootstepSurface surface = ResolveSurface();
            AudioClip[] clips = footstepClips;
            if (surface != null)
            {
                volume *= surface.VolumeScale;
                if (surface.FootstepClips != null && surface.FootstepClips.Length > 0)
                {
                    clips = surface.FootstepClips;
                }
            }
            Play(clips, volume, ref lastFootstepClip);
        }

        private void OnJumped()
        {
            Play(jumpClips, jumpVolume, ref lastJumpClip);
        }

        private void OnPowerSlide()
        {
            FootstepSurface surface = ResolveSurface();
            Play(slideClips, slideVolume * (surface != null ? surface.VolumeScale : 1f), ref lastSlideClip);
        }

        private void OnLanded(float impactSpeed)
        {
            float hardness = Mathf.InverseLerp(SoftLandingSpeed, hardLandingSpeed, impactSpeed);
            float volume = Mathf.Lerp(landVolumeSoft, landVolumeHard, hardness);

            FootstepSurface surface = ResolveSurface();
            AudioClip[] clips = landClips;
            if (surface != null)
            {
                volume *= surface.VolumeScale;
                if (surface.LandClips != null && surface.LandClips.Length > 0)
                {
                    clips = surface.LandClips;
                }
            }
            Play(clips, volume, ref lastLandClip);
        }

        /// <summary>The surface under the player right now (the ladder surface while climbing), or null for the defaults.</summary>
        private FootstepSurface ResolveSurface()
        {
            if (controller.IsClimbing)
            {
                return ladderSurface;
            }

            Collider ground = controller.Ground;
            if (ground == null)
            {
                return null;
            }

            FootstepSurfaceMarker marker = ground.GetComponentInParent<FootstepSurfaceMarker>();
            if (marker != null)
            {
                return marker.Surface;
            }

            if (surfaces == null)
            {
                return null;
            }
            TerrainLayer terrainLayer = null;
            bool terrainSampled = false;
            foreach (FootstepSurface surface in surfaces)
            {
                if (surface == null)
                {
                    continue;
                }
                if (surface.UsesTerrainLayers && !terrainSampled)
                {
                    terrainLayer = DominantTerrainLayer(ground, transform.position);   // once, and only when a surface needs it
                    terrainSampled = true;
                }
                if (surface.Matches(ground, terrainLayer))
                {
                    return surface;
                }
            }
            return null;
        }

        /// <summary>The terrain layer with the most weight at the position, or null when the ground is not a terrain.</summary>
        private static TerrainLayer DominantTerrainLayer(Collider ground, Vector3 position)
        {
            if (!(ground is TerrainCollider))
            {
                return null;
            }
            Terrain terrain = ground.GetComponent<Terrain>();
            TerrainData data = terrain != null ? terrain.terrainData : null;
            if (data == null || data.terrainLayers == null || data.terrainLayers.Length == 0)
            {
                return null;
            }

            Vector3 local = position - terrain.GetPosition();
            int x = Mathf.Clamp(Mathf.FloorToInt(local.x / data.size.x * data.alphamapWidth), 0, data.alphamapWidth - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(local.z / data.size.z * data.alphamapHeight), 0, data.alphamapHeight - 1);
            float[,,] weights = data.GetAlphamaps(x, z, 1, 1);

            int best = -1;
            float bestWeight = 0f;
            int layers = Mathf.Min(weights.GetLength(2), data.terrainLayers.Length);
            for (int i = 0; i < layers; i++)
            {
                if (weights[0, 0, i] > bestWeight)
                {
                    bestWeight = weights[0, 0, i];
                    best = i;
                }
            }
            return best >= 0 ? data.terrainLayers[best] : null;
        }

        private void Play(AudioClip[] clips, float volume, ref int lastIndex)
        {
            if (clips == null || clips.Length == 0 || volume <= 0f)
            {
                return;
            }

            // Pick a different clip than last time when there is a choice.
            int index = Random.Range(0, clips.Length);
            if (clips.Length > 1 && index == lastIndex)
            {
                index = (index + 1 + Random.Range(0, clips.Length - 1)) % clips.Length;
            }
            lastIndex = index;

            AudioClip clip = clips[index];
            if (clip == null)
            {
                return;
            }

            // The pitch belongs to the source, so it also nudges a one-shot still playing from a moment ago; with the
            // small variations meant here that is inaudible, and it keeps this to a single AudioSource.
            source.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
            source.PlayOneShot(clip, Mathf.Clamp01(volume));
        }
    }
}
