using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace CryoController.Tests
{
    public class ControlsOverlayTests
    {
        private TestRig rig;
        private Keyboard keyboard;

        [SetUp]
        public void SetUp()
        {
            rig = new TestRig();
            keyboard = InputSystem.AddDevice<Keyboard>();
        }

        [TearDown]
        public void TearDown()
        {
            if (keyboard != null && keyboard.added)
            {
                InputSystem.RemoveDevice(keyboard);
            }
            rig?.Dispose();
        }

        // Keyboard and mouse bindings in the shipped asset's schemes, for the rows to show.
        private InputActionAsset KeyboardActions()
        {
            InputActionAsset asset = rig.Track(ScriptableObject.CreateInstance<InputActionAsset>());
            InputActionMap map = asset.AddActionMap("Player");
            InputAction move = map.AddAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w", InputRebinder.KeyboardScheme)
                .With("Down", "<Keyboard>/s", InputRebinder.KeyboardScheme)
                .With("Left", "<Keyboard>/a", InputRebinder.KeyboardScheme)
                .With("Right", "<Keyboard>/d", InputRebinder.KeyboardScheme);
            map.AddAction("Look", InputActionType.Value, "<Mouse>/delta", groups: InputRebinder.KeyboardScheme);
            InputAction jump = map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space", groups: InputRebinder.KeyboardScheme);
            jump.AddBinding("<Gamepad>/buttonSouth", groups: InputRebinder.GamepadScheme);
            return asset;
        }

        private FirstPersonController CreatePlayer(float showAtStart, out ControlsOverlay overlay, InputActionAsset actions = null)
        {
            ControlsOverlay added = null;
            FirstPersonController player = rig.CreatePlayer(Vector3.zero, go =>
            {
                if (actions != null)
                {
                    TestRig.SetField(go.GetComponent<FirstPersonController>(), "actionsOverride", actions);
                }
                added = go.AddComponent<ControlsOverlay>();
                TestRig.SetField(added, "showAtStart", showAtStart);
            });
            overlay = added;
            return player;
        }

        [UnityTest]
        public IEnumerator ShowsAtStartThenHides()
        {
            rig.CreateGround();
            CreatePlayer(0.3f, out ControlsOverlay overlay);
            yield return null;
            Assert.IsTrue(overlay.IsVisible, "showing at the start");
            yield return TestRig.WaitGameAndRealTime(0.6f);
            Assert.IsFalse(overlay.IsVisible, "hidden after Show At Start");
        }

        [UnityTest]
        public IEnumerator ToggleKeyShowsAndHides()
        {
            rig.CreateGround();
            CreatePlayer(0f, out ControlsOverlay overlay);
            yield return null;
            Assert.IsFalse(overlay.IsVisible, "starts hidden with Show At Start at 0");

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F1));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            Assert.IsTrue(overlay.IsVisible, "F1 shows it");

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F1));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            Assert.IsFalse(overlay.IsVisible, "and hides it");
        }

        [UnityTest]
        public IEnumerator RowsShowTheKeysAndFollowRebinding()
        {
            rig.CreateGround();
            FirstPersonController player = CreatePlayer(0f, out ControlsOverlay overlay, KeyboardActions());
            yield return null;

            ControlsOverlay.Row move = overlay.Rows.First(r => r.Label == "Move");
            CollectionAssert.AreEqual(new[] { "W", "A", "S", "D" }, move.Keys, "Move as W A S D");
            Assert.AreEqual("Mouse", overlay.Rows.First(r => r.Label == "Look").Keys[0]);
            Assert.AreEqual("Space", overlay.Rows.First(r => r.Label == "Jump").Keys[0]);

            player.Actions.FindAction("Player/Jump").ApplyBindingOverride(0, "<Keyboard>/j");
            yield return null;
            Assert.AreEqual("J", overlay.Rows.First(r => r.Label == "Jump").Keys[0], "the rebound key");
        }
    }
}
