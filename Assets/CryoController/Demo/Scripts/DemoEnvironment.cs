using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CryoController.Demo
{
    /// <summary>
    /// Makes the demo scene look right in whichever render pipeline the project uses. Every renderer in the scene
    /// gets the version of its material made for the active pipeline (Built-in, URP or HDRP), and the sun gets an
    /// intensity in that pipeline's units. Runs in the Editor too, so the scene is right the moment it opens, and
    /// follows a pipeline change. The materials and the sun's intensity are always saved in their Built-in form, so they
    /// do not change in the file with the pipeline of whoever saved it (a pipeline may still add its own data to a light
    /// or camera that is edited). Demo only: nothing in the package needs it.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Demo/Demo Environment")]
    public class DemoEnvironment : MonoBehaviour
    {
        /// <summary>The render pipelines the demo knows.</summary>
        public enum Pipeline
        {
            BuiltIn,
            Universal,
            HighDefinition,
            Other,
        }

        /// <summary>One look, as a material for each pipeline.</summary>
        [Serializable]
        public class PipelineMaterials
        {
            [Tooltip("The material for the Built-in Render Pipeline, which the scene is saved with.")]
            public Material builtIn;
            [Tooltip("The material for URP.")]
            public Material universal;
            [Tooltip("The material for HDRP.")]
            public Material highDefinition;

            public Material For(Pipeline pipeline)
            {
                switch (pipeline)
                {
                    case Pipeline.BuiltIn: return builtIn;
                    case Pipeline.Universal: return universal;
                    case Pipeline.HighDefinition: return highDefinition;
                    default: return null;
                }
            }

            public bool Contains(Material material)
            {
                return material != null && (material == builtIn || material == universal || material == highDefinition);
            }
        }

        /// <summary>A light whose intensity is given for each pipeline's units.</summary>
        [Serializable]
        public class PipelineLight
        {
            [Tooltip("The light.")]
            public Light light;
            [Tooltip("Intensity in the Built-in pipeline and URP.")]
            public float intensity = 1f;
            [Tooltip("Intensity in HDRP, in the light's own unit there (lumen for a spot or point light).")]
            public float hdrpIntensity = 600f;
        }

        [Tooltip("The level: cubes under this object get the metre grid.")]
        [SerializeField] private Transform level;
        [Tooltip("Each entry is one look in its three pipeline versions. A renderer anywhere in the scene using any of " +
                 "the three gets the one for the active pipeline; other materials are left alone.")]
        [SerializeField] private PipelineMaterials[] materials;
        [Tooltip("The scene's directional light.")]
        [SerializeField] private Light sun;
        [Tooltip("Sun intensity in the Built-in pipeline and URP.")]
        [SerializeField, Min(0f)] private float sunIntensity = 1.2f;
        [Tooltip("Sun intensity in HDRP, in lux.")]
        [SerializeField, Min(0f)] private float hdrpSunIntensity = 130000f;
        [Tooltip("Other lights that need an intensity per pipeline, such as the player's flashlight.")]
        [SerializeField] private PipelineLight[] lights = new PipelineLight[0];
        [Tooltip("Give every cube under the level texture coordinates in metres, lined up across neighbouring objects, " +
                 "so the grid texture shows one square per metre on every face whatever the cube's size.")]
        [SerializeField] private bool metreGrid = true;

#if UNITY_EDITOR
        private RenderPipelineAsset appliedPipeline;   // the pipeline last applied, to notice a change in the Editor
#endif

        // The cubes given a metre-grid mesh, with the mesh each had before, so it can be put back. The generated meshes
        // are never saved: the scene keeps Unity's cube.
        private readonly Dictionary<MeshFilter, Mesh> originalMeshes = new Dictionary<MeshFilter, Mesh>();

        /// <summary>
        /// The render pipeline in use, by the shader tag its asset declares ("UniversalPipeline", "HDRenderPipeline"),
        /// so a project's own subclass of the URP or HDRP asset is recognised too.
        /// </summary>
        public static Pipeline ActivePipeline
        {
            get
            {
                RenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline;
                if (asset == null)
                {
                    return Pipeline.BuiltIn;
                }
                string tag = asset.renderPipelineShaderTag ?? "";
                string type = asset.GetType().Name;
                if (tag == "HDRenderPipeline" || type.Contains("HDRenderPipeline"))
                {
                    return Pipeline.HighDefinition;
                }
                return tag == "UniversalPipeline" || type.Contains("Universal") ? Pipeline.Universal : Pipeline.Other;
            }
        }

        private void OnEnable()
        {
            Apply();
            ApplyMetreGrid();
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += OnSceneSaving;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved += OnSceneSaved;
#endif
        }

        private void OnDisable()
        {
            RestoreMeshes();
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= OnSceneSaving;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved -= OnSceneSaved;
#endif
        }

#if UNITY_EDITOR
        private void Update()
        {
            // In the Editor the pipeline can change while the scene is open (Project Settings, a quality level).
            if (!Application.isPlaying && GraphicsSettings.currentRenderPipeline != appliedPipeline)
            {
                Apply();
            }
        }

        private void OnSceneSaving(Scene scene, string path)
        {
            if (scene == gameObject.scene)
            {
                ApplyFor(Pipeline.BuiltIn);   // materials and sun saved in their Built-in form, whatever pipeline this Editor runs
                RestoreMeshes();              // and Unity's cube, not a generated mesh the file can't hold
            }
        }

        private void OnSceneSaved(Scene scene)
        {
            if (scene == gameObject.scene)
            {
                Apply();
                ApplyMetreGrid();
            }
        }
#endif

        // ------------------------------------------------------------------ Metre grid

        /// <summary>
        /// Gives every cube under the level a mesh whose texture coordinates are in metres along each face, measured in
        /// world space, so the grid lines up across neighbouring objects. Moved or resized cubes keep the grid they had
        /// when this ran; call it again to refresh them.
        /// </summary>
        public void ApplyMetreGrid()
        {
            RestoreMeshes();
            if (!metreGrid || level == null)
            {
                return;
            }
#if UNITY_EDITOR
            // A build loads the scene in the Editor and writes out what it holds: a generated mesh there would be
            // missing from the player. The player makes its own when the scene starts.
            if (UnityEditor.BuildPipeline.isBuildingPlayer)
            {
                return;
            }
#endif

            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            foreach (MeshFilter filter in level.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != cube)
                {
                    continue;
                }
                originalMeshes[filter] = cube;
                filter.sharedMesh = MetreGridCube(filter.transform);
            }
        }

#if UNITY_EDITOR
        // Before a build, every open demo scene gets Unity's cubes back, in case the build writes out a scene that is
        // already open.
        private class BuildHook : UnityEditor.Build.IPreprocessBuildWithReport
        {
            public int callbackOrder => 0;

            public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
            {
                foreach (DemoEnvironment environment in Resources.FindObjectsOfTypeAll<DemoEnvironment>())
                {
                    environment.RestoreMeshes();
                }
            }
        }
#endif

        private void RestoreMeshes()
        {
            foreach (KeyValuePair<MeshFilter, Mesh> pair in originalMeshes)
            {
                if (pair.Key == null)
                {
                    continue;
                }
                Mesh generated = pair.Key.sharedMesh;
                pair.Key.sharedMesh = pair.Value;
                if (generated != null && generated != pair.Value)
                {
                    DestroyImmediate(generated);
                }
            }
            originalMeshes.Clear();
        }

        // Each face of the unit cube: its normal and the two axes along it, in the cube's own space.
        private static readonly Vector3[,] CubeFaces =
        {
            { Vector3.right, Vector3.forward, Vector3.up },
            { Vector3.left, Vector3.back, Vector3.up },
            { Vector3.up, Vector3.right, Vector3.forward },
            { Vector3.down, Vector3.right, Vector3.back },
            { Vector3.forward, Vector3.left, Vector3.up },
            { Vector3.back, Vector3.right, Vector3.up },
        };

        private static Mesh MetreGridCube(Transform cube)
        {
            var vertices = new Vector3[24];
            var normals = new Vector3[24];
            var uvs = new Vector2[24];
            var triangles = new int[36];
            for (int face = 0; face < 6; face++)
            {
                Vector3 normal = CubeFaces[face, 0];
                Vector3 across = CubeFaces[face, 1];
                Vector3 up = CubeFaces[face, 2];
                // The face's axes in the world, so the coordinates are world distances along the face.
                Vector3 worldAcross = cube.TransformDirection(across).normalized;
                Vector3 worldUp = cube.TransformDirection(up).normalized;
                for (int corner = 0; corner < 4; corner++)
                {
                    float a = corner == 1 || corner == 2 ? 0.5f : -0.5f;
                    float b = corner >= 2 ? 0.5f : -0.5f;
                    Vector3 local = normal * 0.5f + across * a + up * b;
                    Vector3 world = cube.TransformPoint(local);
                    int v = face * 4 + corner;
                    vertices[v] = local;
                    normals[v] = normal;
                    uvs[v] = new Vector2(Vector3.Dot(world, worldAcross), Vector3.Dot(world, worldUp));
                }
                int t = face * 6;
                int first = face * 4;
                triangles[t] = first;
                triangles[t + 1] = first + 2;
                triangles[t + 2] = first + 1;
                triangles[t + 3] = first;
                triangles[t + 4] = first + 3;
                triangles[t + 5] = first + 2;
            }

            var mesh = new Mesh
            {
                name = "Metre Grid Cube",
                hideFlags = HideFlags.DontSave,
                vertices = vertices,
                normals = normals,
                uv = uvs,
                triangles = triangles,
            };
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Applies the materials and sun intensity for the active pipeline.</summary>
        public void Apply()
        {
#if UNITY_EDITOR
            appliedPipeline = GraphicsSettings.currentRenderPipeline;
#endif
            ApplyFor(ActivePipeline);
        }

        private void ApplyFor(Pipeline pipeline)
        {
            if (pipeline == Pipeline.Other)
            {
                return;   // a custom pipeline: the scene stays as authored
            }

            if (materials != null && gameObject.scene.IsValid())
            {
                // The whole scene, not only the level: the player's held flashlight uses the demo's materials too.
                var renderers = new List<Renderer>();
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                {
                    renderers.AddRange(root.GetComponentsInChildren<Renderer>(true));
                }
                foreach (Renderer renderer in renderers)
                {
                    Material[] shared = renderer.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < shared.Length; i++)
                    {
                        Material wanted = Replacement(shared[i], pipeline);
                        if (wanted != null && wanted != shared[i])
                        {
                            shared[i] = wanted;
                            changed = true;
                        }
                    }
                    if (changed)
                    {
                        renderer.sharedMaterials = shared;
                    }
                }
            }

            if (sun != null)
            {
                sun.intensity = pipeline == Pipeline.HighDefinition ? hdrpSunIntensity : sunIntensity;
            }
            if (lights != null)
            {
                foreach (PipelineLight entry in lights)
                {
                    if (entry != null && entry.light != null)
                    {
                        entry.light.intensity = pipeline == Pipeline.HighDefinition ? entry.hdrpIntensity : entry.intensity;
                    }
                }
            }
        }

        private Material Replacement(Material current, Pipeline pipeline)
        {
            foreach (PipelineMaterials set in materials)
            {
                if (set != null && set.Contains(current))
                {
                    return set.For(pipeline);
                }
            }
            return null;
        }
    }
}
