using UnityEngine;
using UnityEngine.InputSystem;

namespace CryoController
{
    /// <summary>
    /// Runtime access to the player's look and camera preferences (mouse and stick sensitivity, invert Y, field of view,
    /// reduced camera motion, controller only) and the input binding overrides, saved with PlayerPrefs. An options menu sets the properties (and rebinds through
    /// <see cref="InputRebinder"/>) and calls <see cref="Save"/>; saved values load on start, <see cref="ResetToDefaults"/>
    /// restores the values authored on the controller and <see cref="ResetBindings"/> the bindings of the actions asset.
    /// </summary>
    [RequireComponent(typeof(FirstPersonController))]
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Player Preferences")]
    public class PlayerPreferences : MonoBehaviour
    {
        /// <summary>The PlayerPrefs keys the preferences are saved under.</summary>
        public const string MouseSensitivityKey = "Player.MouseSensitivity";
        public const string StickSensitivityKey = "Player.StickSensitivity";
        public const string InvertYKey = "Player.InvertY";
        public const string FieldOfViewKey = "Player.FieldOfView";
        public const string ReduceCameraMotionKey = "Player.ReduceCameraMotion";
        public const string ControllerOnlyKey = "Player.ControllerOnly";
        public const string BindingsKey = "Player.Bindings";

        [Tooltip("Apply saved preferences when the game starts.")]
        [SerializeField] private bool loadOnStart = true;

        private FirstPersonController controller;
        private float defaultMouseSensitivity;
        private float defaultStickSensitivity;
        private bool defaultInvertY;
        private float defaultFieldOfView;
        private bool defaultReduceCameraMotion;
        private bool defaultControllerOnly;
        private bool defaultsCaptured;

        public float MouseSensitivity
        {
            get { return controller.MouseSensitivity; }
            set { CaptureDefaults(); controller.MouseSensitivity = value; }
        }

        public float StickSensitivity
        {
            get { return controller.StickSensitivity; }
            set { CaptureDefaults(); controller.StickSensitivity = value; }
        }

        public bool InvertY
        {
            get { return controller.InvertY; }
            set { CaptureDefaults(); controller.InvertY = value; }
        }

        public float FieldOfView
        {
            get { return controller.FieldOfView; }
            set { CaptureDefaults(); controller.FieldOfView = value; }
        }

        /// <summary>Comfort setting: see <see cref="FirstPersonController.ReduceCameraMotion"/>.</summary>
        public bool ReduceCameraMotion
        {
            get { return controller.ReduceCameraMotion; }
            set { CaptureDefaults(); controller.ReduceCameraMotion = value; }
        }

        /// <summary>Gamepad-only play: see <see cref="FirstPersonController.ControllerOnly"/>.</summary>
        public bool ControllerOnly
        {
            get { return controller.ControllerOnly; }
            set { CaptureDefaults(); controller.ControllerOnly = value; }
        }

        private void Awake()
        {
            controller = GetComponent<FirstPersonController>();
        }

        private void Start()
        {
            CaptureDefaults();

            if (loadOnStart)
            {
                Load();
            }
        }

        // The values authored on the controller, kept before anything changes them, for ResetToDefaults. Not in Awake:
        // the controller reads its camera's field of view in its own Awake, and Awake order between components on one
        // object is not guaranteed. Start, or the first change or reset if that comes earlier (this component switched
        // off, say), is after it.
        private void CaptureDefaults()
        {
            if (defaultsCaptured)
            {
                return;
            }
            defaultsCaptured = true;
            defaultMouseSensitivity = controller.MouseSensitivity;
            defaultStickSensitivity = controller.StickSensitivity;
            defaultInvertY = controller.InvertY;
            defaultFieldOfView = controller.FieldOfView;
            defaultReduceCameraMotion = controller.ReduceCameraMotion;
            defaultControllerOnly = controller.ControllerOnly;
        }

        /// <summary>Applies any saved preferences; values never saved keep their current setting.</summary>
        public void Load()
        {
            CaptureDefaults();
            if (PlayerPrefs.HasKey(MouseSensitivityKey))
            {
                MouseSensitivity = PlayerPrefs.GetFloat(MouseSensitivityKey);
            }
            if (PlayerPrefs.HasKey(StickSensitivityKey))
            {
                StickSensitivity = PlayerPrefs.GetFloat(StickSensitivityKey);
            }
            if (PlayerPrefs.HasKey(InvertYKey))
            {
                InvertY = PlayerPrefs.GetInt(InvertYKey) != 0;
            }
            if (PlayerPrefs.HasKey(FieldOfViewKey))
            {
                FieldOfView = PlayerPrefs.GetFloat(FieldOfViewKey);
            }
            if (PlayerPrefs.HasKey(ReduceCameraMotionKey))
            {
                ReduceCameraMotion = PlayerPrefs.GetInt(ReduceCameraMotionKey) != 0;
            }
            if (PlayerPrefs.HasKey(ControllerOnlyKey))
            {
                ControllerOnly = PlayerPrefs.GetInt(ControllerOnlyKey) != 0;
            }
            LoadBindings();
        }

        /// <summary>Saves the current preferences.</summary>
        public void Save()
        {
            PlayerPrefs.SetFloat(MouseSensitivityKey, MouseSensitivity);
            PlayerPrefs.SetFloat(StickSensitivityKey, StickSensitivity);
            PlayerPrefs.SetInt(InvertYKey, InvertY ? 1 : 0);
            PlayerPrefs.SetFloat(FieldOfViewKey, FieldOfView);
            PlayerPrefs.SetInt(ReduceCameraMotionKey, ReduceCameraMotion ? 1 : 0);
            PlayerPrefs.SetInt(ControllerOnlyKey, ControllerOnly ? 1 : 0);
            WriteBindings();
            PlayerPrefs.Save();
        }

        /// <summary>Applies saved binding overrides to the controller's actions asset.</summary>
        public void LoadBindings()
        {
            if (controller.Actions != null && PlayerPrefs.HasKey(BindingsKey))
            {
                controller.Actions.LoadBindingOverridesFromJson(PlayerPrefs.GetString(BindingsKey));
            }
        }

        /// <summary>Saves the binding overrides of the controller's actions asset (what the player rebound).</summary>
        public void SaveBindings()
        {
            WriteBindings();
            PlayerPrefs.Save();
        }

        private void WriteBindings()
        {
            if (controller.Actions != null)
            {
                PlayerPrefs.SetString(BindingsKey, controller.Actions.SaveBindingOverridesAsJson());
            }
        }

        /// <summary>Removes every binding override and forgets the saved ones.</summary>
        public void ResetBindings()
        {
            if (controller.Actions != null)
            {
                controller.Actions.RemoveAllBindingOverrides();
            }
            PlayerPrefs.DeleteKey(BindingsKey);
            PlayerPrefs.Save();
        }

        /// <summary>Restores the values authored on the controller and forgets the saved ones.</summary>
        public void ResetToDefaults()
        {
            CaptureDefaults();
            MouseSensitivity = defaultMouseSensitivity;
            StickSensitivity = defaultStickSensitivity;
            InvertY = defaultInvertY;
            FieldOfView = defaultFieldOfView;
            ReduceCameraMotion = defaultReduceCameraMotion;
            ControllerOnly = defaultControllerOnly;

            PlayerPrefs.DeleteKey(MouseSensitivityKey);
            PlayerPrefs.DeleteKey(StickSensitivityKey);
            PlayerPrefs.DeleteKey(InvertYKey);
            PlayerPrefs.DeleteKey(FieldOfViewKey);
            PlayerPrefs.DeleteKey(ReduceCameraMotionKey);
            PlayerPrefs.DeleteKey(ControllerOnlyKey);
            PlayerPrefs.Save();
        }
    }
}
