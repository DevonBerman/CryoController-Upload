using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CryoController.EditorTests
{
    /// <summary>Checks the assets that ship with the package, found by GUID so the package folder can be moved.</summary>
    public class PackageAssetTests
    {
        private const string PackageFolderGuid = "c6b8b56f9d0c40ee99ba3c5937eb9243";
        private const string ActionsGuid = "4312ffc9b6ad4c34afff6912e5fe2bdd";
        private const string PlayerPrefabGuid = "829e985716bc4bb288ddcee1fbe9a05e";
        private const string DemoSceneGuid = "8124e5870f4fd4c779e7a5f994e84ad1";
        private const string InputSystemPackage = "Packages/com.unity.inputsystem/";

        // Every action the package's components look up in the Player map.
        private static readonly string[] UsedActions =
            { "Move", "Look", "Jump", "Sprint", "Crouch", "Interact", "Zoom", "LeanLeft", "LeanRight", "Throw", "Inspect", "Flashlight" };

        private static readonly string[] Schemes = { "Keyboard&Mouse", "Gamepad" };

        private static T Load<T>(string guid) where T : Object
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Assert.IsFalse(string.IsNullOrEmpty(path), $"no asset with GUID {guid}");
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, path);
            return asset;
        }

        [Test]
        public void ShippedActionsBindEveryUsedActionForKeyboardAndGamepad()
        {
            InputActionAsset asset = Load<InputActionAsset>(ActionsGuid);
            foreach (string name in UsedActions)
            {
                InputAction action = asset.FindAction("Player/" + name, throwIfNotFound: false);
                Assert.IsNotNull(action, $"Player/{name} is missing");
                foreach (string scheme in Schemes)
                {
                    Assert.IsTrue(action.bindings.Any(b => !b.isComposite && InputRebinder.InScheme(b, scheme)), $"{name} has no {scheme} binding");
                }
            }
        }

        [Test]
        public void NoControlDrivesTwoActionsInOneScheme()
        {
            InputActionMap map = Load<InputActionAsset>(ActionsGuid).FindActionMap("Player", throwIfNotFound: true);
            var owners = new Dictionary<string, HashSet<string>>();
            foreach (InputAction action in map.actions)
            {
                foreach (InputBinding binding in action.bindings)
                {
                    if (binding.isComposite)
                    {
                        continue;
                    }
                    foreach (string scheme in Schemes.Where(s => InputRebinder.InScheme(binding, s)))
                    {
                        string key = scheme + " " + binding.path;
                        if (!owners.TryGetValue(key, out HashSet<string> actions))
                        {
                            owners[key] = actions = new HashSet<string>();
                        }
                        actions.Add(action.name);
                    }
                }
            }

            foreach (KeyValuePair<string, HashSet<string>> pair in owners)
            {
                // Throw shares the left mouse button with Attack on purpose: with something in hand, the attack button throws.
                if (pair.Value.Count > 1 && !pair.Value.SetEquals(new[] { "Attack", "Throw" }))
                {
                    Assert.Fail($"{pair.Key} drives {string.Join(", ", pair.Value)}");
                }
            }
        }

        [Test]
        public void PlayerPrefabHasEveryComponentAndTheShippedActions()
        {
            GameObject prefab = Load<GameObject>(PlayerPrefabGuid);
            Assert.IsNotNull(prefab.GetComponent<CharacterController>(), "CharacterController");
            FirstPersonController controller = prefab.GetComponent<FirstPersonController>();
            Assert.IsNotNull(controller, "FirstPersonController");
            foreach (System.Type type in new[] { typeof(PlayerAudio), typeof(PlayerInteractor), typeof(InteractionPromptHud),
                                                 typeof(PlayerStamina), typeof(PlayerPreferences), typeof(PauseMenu), typeof(PlayerCarry),
                                                 typeof(PlayerRumble), typeof(PlayerFallDamage), typeof(PlayerKeys), typeof(PlayerReader), typeof(TouchControls), typeof(ControlsOverlay) })
            {
                Assert.IsNotNull(prefab.GetComponent(type), type.Name);
            }

            Camera camera = prefab.GetComponentInChildren<Camera>(true);
            Assert.IsNotNull(camera, "a camera");
            Assert.AreNotEqual(prefab, camera.gameObject, "the camera is on a child");

            var serialized = new SerializedObject(controller);
            Assert.AreEqual(Load<InputActionAsset>(ActionsGuid), serialized.FindProperty("actionsOverride").objectReferenceValue,
                            "the prefab uses the shipped actions");
        }

        [Test]
        public void DemoSceneUsesThePlayerPrefab()
        {
            string scene = AssetDatabase.GUIDToAssetPath(DemoSceneGuid);
            string prefab = AssetDatabase.GUIDToAssetPath(PlayerPrefabGuid);
            CollectionAssert.Contains(AssetDatabase.GetDependencies(scene, false), prefab,
                                      "the demo's player is an instance of the shipped prefab, so the two cannot drift apart");
        }

        [Test]
        public void DemoSceneAndPrefabUseNothingOutsideThePackage()
        {
            string package = AssetDatabase.GUIDToAssetPath(PackageFolderGuid) + "/";
            var outside = new List<string>();
            foreach (string guid in new[] { DemoSceneGuid, PlayerPrefabGuid })
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Assert.IsTrue(path.StartsWith(package), $"{path} is in the package");
                foreach (string dependency in AssetDatabase.GetDependencies(path, true))
                {
                    if (dependency.StartsWith(package))
                    {
                        continue;
                    }
                    // Assets from Packages/ are allowed (the render pipeline's own Lit shader, say), but not their
                    // runtime scripts: a component from a render pipeline package would be a missing script in the
                    // others. Their Editor scripts can't be components; the render pipelines attach one to each of
                    // their materials to track its version. The Input System is required, so all of it is allowed.
                    if (dependency.StartsWith(InputSystemPackage) ||
                        (dependency.StartsWith("Packages/") && dependency.Contains("/Editor/")))
                    {
                        continue;
                    }
                    if (dependency.StartsWith("Assets/") || dependency.EndsWith(".cs"))
                    {
                        outside.Add($"{path} depends on {dependency}");
                    }
                }
            }
            Assert.IsEmpty(outside, "Nothing outside the package but its required packages:\n" + string.Join("\n", outside));
        }
    }
}
