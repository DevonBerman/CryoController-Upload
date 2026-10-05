using UnityEditor;
using UnityEngine;

namespace CryoController.Editor
{
    /// <summary>
    /// Save Settings As Movement Profile, in the First Person Controller's component menu (the ⋮ button or a right
    /// click on its header): saves the controller's current movement and camera-feel settings as a new
    /// <see cref="MovementProfile"/> asset. In Play Mode that is the settings in use, so a feel tuned while playing can be
    /// kept.
    /// </summary>
    public static class MovementProfileMenu
    {
        [MenuItem("CONTEXT/FirstPersonController/Save Settings As Movement Profile...")]
        private static void SaveAsProfile(MenuCommand command)
        {
            var controller = (FirstPersonController)command.context;
            string path = EditorUtility.SaveFilePanelInProject("Save Movement Profile", controller.name + " Movement",
                                                               "asset", "Where to save the new Movement Profile.");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            MovementProfile profile = MovementProfile.CreateFrom(controller);
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(profile);
            Selection.activeObject = profile;
        }
    }
}
