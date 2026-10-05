using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace CryoController.Tests
{
    /// <summary>
    /// A minimal world for the play mode tests: colliders to stand on, and a player whose actions are bound only to a
    /// virtual gamepad the test drives. Everything it creates is destroyed in <see cref="Dispose"/>, and the Input
    /// System settings it swaps are put back.
    /// </summary>
    public sealed class TestRig : IDisposable
    {
        /// <summary>The test player's capsule height and eye height, in metres.</summary>
        public const float StandingHeight = 1.8f;
        public const float EyeHeight = 1.6f;
        public const float SkinWidth = 0.08f;   // the capsule rests about this far above what it stands on

        /// <summary>How much game time every frame of a test advances, whatever the frame really takes: 60 fps.</summary>
        public const float FrameTime = 1f / 60f;

        private static int s_DeviceCount;

        private readonly List<Object> created = new List<Object>();
        private readonly InputSettings originalSettings;
        private GamepadState state;

        /// <summary>The virtual gamepad every test action is bound to.</summary>
        public Gamepad Gamepad { get; }

        /// <summary>The actions the test player uses (the controller works on its own copy).</summary>
        public InputActionAsset Actions { get; }

        public TestRig()
        {
            // Game time advances by one 60 fps frame per frame, however long the frame takes. Otherwise a slow or
            // stalling machine (a build server rendering in software stalls for a third of a second at a time) turns
            // jumps, falls and platform rides into a few big steps, and the timing checks miss the moments they test.
            Time.captureDeltaTime = FrameTime;

            // Input has to reach the game whatever has focus: during a test run the Game view rarely does. A fresh
            // settings object is swapped in, so the project's own Input System settings asset is never modified.
            originalSettings = InputSystem.settings;
            try
            {
                InputSettings settings = Track(ScriptableObject.CreateInstance<InputSettings>());
                settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings = settings;

                // The bindings name this device, so a real gamepad plugged in cannot interfere with a test.
                string device = "CryoControllerTestGamepad" + ++s_DeviceCount;
                Gamepad = InputSystem.AddDevice<Gamepad>(device);
                Actions = Track(CreateActions("/" + device));
            }
            catch
            {
                Dispose();   // never leave the project's input settings swapped out
                throw;
            }
        }

        public void Dispose()
        {
            Time.captureDeltaTime = 0f;
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            InputSystem.settings = originalSettings;
            if (Gamepad != null && Gamepad.added)
            {
                InputSystem.RemoveDevice(Gamepad);
            }

            // Immediately, so nothing of this test is left in the physics scene when the next one starts.
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null)
                {
                    Object.DestroyImmediate(created[i]);
                }
            }
            created.Clear();
        }

        /// <summary>Destroys the object when the rig is disposed.</summary>
        public T Track<T>(T obj) where T : Object
        {
            created.Add(obj);
            return obj;
        }

        // ------------------------------------------------------------------ World

        /// <summary>A static box collider.</summary>
        public BoxCollider CreateBox(string name, Vector3 center, Vector3 size)
        {
            GameObject go = Track(new GameObject(name));
            go.transform.position = center;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = size;
            Physics.SyncTransforms();
            return box;
        }

        /// <summary>A flat ground whose top is at y = 0.</summary>
        public BoxCollider CreateGround(float size = 100f)
        {
            return CreateBox("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(size, 1f, size));
        }

        /// <summary>
        /// A player with its feet at the position, facing +z, using the test actions and leaving the cursor alone.
        /// Configure adds companion components before the player wakes up.
        /// </summary>
        public FirstPersonController CreatePlayer(Vector3 feet, Action<GameObject> configure = null)
        {
            GameObject go = Track(new GameObject("Test Player"));
            go.SetActive(false);
            go.transform.position = feet;

            CharacterController capsule = go.AddComponent<CharacterController>();
            capsule.height = StandingHeight;
            capsule.radius = 0.4f;
            capsule.center = new Vector3(0f, StandingHeight * 0.5f, 0f);
            capsule.slopeLimit = 45f;
            capsule.stepOffset = 0.3f;
            capsule.skinWidth = SkinWidth;
            capsule.minMoveDistance = 0f;

            GameObject eyes = new GameObject("Eyes");
            eyes.transform.SetParent(go.transform, false);
            eyes.transform.localPosition = new Vector3(0f, EyeHeight, 0f);
            Camera camera = eyes.AddComponent<Camera>();
            camera.enabled = false;   // the controller needs its transform; nothing needs to render

            FirstPersonController controller = go.AddComponent<FirstPersonController>();
            SetField(controller, "actionsOverride", Actions);
            SetField(controller, "manageCursor", false);
            configure?.Invoke(go);

            go.SetActive(true);
            Physics.SyncTransforms();
            return controller;
        }

        // ------------------------------------------------------------------ Input

        /// <summary>Sets the left stick (Move).</summary>
        public void SetMove(Vector2 value)
        {
            state.leftStick = value;
            Send();
        }

        /// <summary>Sets the right stick (Look).</summary>
        public void SetLook(Vector2 value)
        {
            state.rightStick = value;
            Send();
        }

        /// <summary>Holds or releases a button.</summary>
        public void Hold(GamepadButton button, bool down = true)
        {
            state = state.WithButton(button, down);
            Send();
        }

        /// <summary>Presses a button for one frame.</summary>
        public IEnumerator Press(GamepadButton button)
        {
            Hold(button);
            yield return null;   // the press is processed at the start of this frame
            Hold(button, false);
            yield return null;
        }

        private void Send()
        {
            InputSystem.QueueStateEvent(Gamepad, state);
        }

        // ------------------------------------------------------------------ Helpers

        /// <summary>Waits until the condition holds, or the timeout (seconds of game time) runs out.</summary>
        public static IEnumerator WaitUntil(Func<bool> condition, float timeout)
        {
            float end = Time.time + timeout;
            while (!condition() && Time.time < end)
            {
                yield return null;
            }
        }

        /// <summary>
        /// Waits until both this much game time and this much real time have passed. Game time runs on the fixed test
        /// clock, but highlight fades, rumble and rebinding run on real time, and a machine rendering nothing can run
        /// thousands of frames in a real second.
        /// </summary>
        public static IEnumerator WaitGameAndRealTime(float seconds)
        {
            float gameEnd = Time.time + seconds;
            float realEnd = Time.realtimeSinceStartup + seconds;
            while (Time.time < gameEnd || Time.realtimeSinceStartup < realEnd)
            {
                yield return null;
            }
        }

        /// <summary>Waits until the condition holds, or the timeout (seconds of real time) runs out.</summary>
        public static IEnumerator WaitUntilRealTime(Func<bool> condition, float timeout)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < end)
            {
                yield return null;
            }
        }

        /// <summary>Sets a serialized field the Inspector would set. Fails the test if the field no longer exists.</summary>
        public static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(field, $"{target.GetType().Name} has no field '{name}'");
            field.SetValue(target, value);
        }

        private static InputActionAsset CreateActions(string device)
        {
            InputActionAsset asset = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap map = asset.AddActionMap("Player");
            map.AddAction("Move", InputActionType.Value, device + "/leftStick", groups: "Gamepad");
            map.AddAction("Look", InputActionType.Value, device + "/rightStick", groups: "Gamepad");
            map.AddAction("Jump", InputActionType.Button, device + "/buttonSouth", groups: "Gamepad");
            map.AddAction("Crouch", InputActionType.Button, device + "/buttonEast", groups: "Gamepad");
            map.AddAction("Interact", InputActionType.Button, device + "/buttonNorth", groups: "Gamepad");
            map.AddAction("Throw", InputActionType.Button, device + "/buttonWest", groups: "Gamepad");
            map.AddAction("Sprint", InputActionType.Button, device + "/leftStickPress", groups: "Gamepad");
            map.AddAction("Zoom", InputActionType.Button, device + "/leftTrigger", groups: "Gamepad");
            map.AddAction("LeanLeft", InputActionType.Button, device + "/leftShoulder", groups: "Gamepad");
            map.AddAction("LeanRight", InputActionType.Button, device + "/rightShoulder", groups: "Gamepad");
            map.AddAction("Inspect", InputActionType.Button, device + "/dpad/up", groups: "Gamepad");
            map.AddAction("Flashlight", InputActionType.Button, device + "/dpad/down", groups: "Gamepad");
            return asset;
        }
    }
}
