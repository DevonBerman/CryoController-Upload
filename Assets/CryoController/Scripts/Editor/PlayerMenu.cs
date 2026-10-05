using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CryoController.Editor
{
    /// <summary>
    /// GameObject > CryoController > First Person Player: drops the Player prefab into the open scene, on the ground
    /// under the Scene view's pivot, and points out any other AudioListener in the scene.
    /// </summary>
    public static class PlayerMenu
    {
        // The Player prefab, by GUID, so the package folder can be moved or renamed.
        private const string PlayerPrefabGuid = "829e985716bc4bb288ddcee1fbe9a05e";

        [MenuItem("GameObject/CryoController/First Person Player", false, 10)]
        private static void AddPlayer(MenuCommand command)
        {
            // From the Hierarchy's context menu the item runs once per selected object: add one player, under the active one.
            if (command.context != null && command.context != Selection.activeObject)
            {
                return;
            }

            string path = AssetDatabase.GUIDToAssetPath(PlayerPrefabGuid);
            GameObject prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError("CryoController: the Player prefab was not found. Reimport the package.");
                return;
            }

            // The ground is found before the player exists, so the ray cannot hit the player's own capsule.
            Vector3 pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            Vector3 position = OnGround(pivot);

            GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            GameObjectUtility.SetParentAndAlign(player, command.context as GameObject);   // under a right-clicked object
            player.transform.position = position;
            Undo.RegisterCreatedObjectUndo(player, "Add First Person Player");
            Selection.activeGameObject = player;

            WarnAboutOtherListeners(player);
        }

        private static Vector3 OnGround(Vector3 near)
        {
            // The feet go onto whatever is just below the pivot; with nothing there the pivot itself will do.
            if (Physics.Raycast(near + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 100f, Physics.DefaultRaycastLayers,
                                QueryTriggerInteraction.Ignore))
            {
                return hit.point + Vector3.up * 0.05f;
            }
            return near;
        }

        private static void WarnAboutOtherListeners(GameObject player)
        {
            // Every loaded scene's objects, walked directly: FindObjectsByType's overloads differ between Unity 6
            // versions, and one of them is deprecated in the later ones.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (AudioListener listener in root.GetComponentsInChildren<AudioListener>())
                    {
                        if (!listener.transform.IsChildOf(player.transform))
                        {
                            Debug.LogWarning($"CryoController: '{listener.gameObject.name}' also has an " +
                                             "AudioListener and the player's camera has one. Remove or disable the other, usually " +
                                             "the scene's Main Camera.", listener);
                            return;
                        }
                    }
                }
            }
        }
    }
}
