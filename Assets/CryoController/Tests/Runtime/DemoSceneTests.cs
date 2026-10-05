using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace CryoController.Tests
{
    /// <summary>
    /// Plays the demo scene in whichever render pipeline the project uses, so it is checked in every pipeline the
    /// project is tested in.
    /// </summary>
    public class DemoSceneTests
    {
        private const string DemoSceneGuid = "8124e5870f4fd4c779e7a5f994e84ad1";

        private readonly List<string> packageMessages = new List<string>();

        // Errors, warnings and exceptions from the package's own code. A render pipeline's complaints about the machine
        // (HDRP without a supported GPU, say) are not the demo's, so they are left out, as are frames of this test.
        private void OnLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Log && FromPackage(message, stackTrace))
            {
                packageMessages.Add($"{type}: {message}");
            }
        }

        private static bool FromPackage(string message, string stackTrace)
        {
            // The package's own log prefix, or a path inside the package (not the project folder, which shares its name).
            if (message.Contains("CryoController:") || message.Contains("Assets/CryoController/"))
            {
                return true;
            }
            foreach (string line in stackTrace.Split('\n'))
            {
                if (line.Contains("CryoController.") && !line.Contains("CryoController.Tests"))
                {
                    return true;
                }
            }
            return false;
        }

        [UnityTest]
        public IEnumerator DemoScenePlays()
        {
#if UNITY_EDITOR
            string path = AssetDatabase.GUIDToAssetPath(DemoSceneGuid);
            if (string.IsNullOrEmpty(path))
            {
                Assert.Ignore("The demo scene is not in this project.");
            }

            packageMessages.Clear();
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += OnLog;
            Scene scene = default(Scene);
            try
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
                scene = SceneManager.GetSceneByPath(path);
                Assert.IsTrue(scene.isLoaded, "the demo scene loaded");

                FirstPersonController player = null;
                var renderers = new List<Renderer>();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    player = player != null ? player : root.GetComponentInChildren<FirstPersonController>(true);
                    renderers.AddRange(root.GetComponentsInChildren<Renderer>(true));
                }
                Assert.IsNotNull(player, "the demo has a player");

                yield return new WaitForSeconds(1f);
                Assert.IsTrue(player.isActiveAndEnabled, "the player is still enabled (it switches itself off on a setup error)");
                Assert.IsTrue(player.IsGrounded, "the player stands on the level");

                // Every level renderer uses the version of its demo material made for the active pipeline.
                string suffix = MaterialSuffix();
                int demoMaterials = 0;
                foreach (Renderer renderer in renderers)
                {
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material != null && material.name.StartsWith("Demo "))
                        {
                            demoMaterials++;
                            StringAssert.EndsWith(suffix, material.name, $"{renderer.name} has the {suffix} material");
                        }
                    }
                }
                Assert.Greater(demoMaterials, 0, "the level uses the demo materials");

                Assert.IsEmpty(packageMessages, "the package logged nothing wrong:\n" + string.Join("\n", packageMessages));

                yield return SceneManager.UnloadSceneAsync(scene);
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
                LogAssert.ignoreFailingMessages = false;
                if (scene.IsValid() && scene.isLoaded)
                {
                    SceneManager.UnloadSceneAsync(scene);
                }
            }
#else
            Assert.Ignore("Loads the demo scene through the Editor, so it runs in the Editor only.");
            yield break;
#endif
        }

        // The demo's materials are named for their pipeline: "Demo Surface (URP)" and so on.
        private static string MaterialSuffix()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null)
            {
                return "(Built-in)";
            }
            return pipeline.renderPipelineShaderTag == "HDRenderPipeline" ? "(HDRP)" : "(URP)";
        }
    }
}
