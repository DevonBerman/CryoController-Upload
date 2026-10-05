using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace CryoController.Tests
{
    public class TouchControlsTests
    {
        private TestRig rig;
        private Touchscreen screen;
        private InputActionAsset actions;

        [SetUp]
        public void SetUp()
        {
            rig = new TestRig();
            screen = InputSystem.AddDevice<Touchscreen>();

            // The touch controls drive a virtual gamepad of their own, so this player reads any gamepad, as the shipped
            // actions do.
            actions = rig.Track(ScriptableObject.CreateInstance<InputActionAsset>());
            InputActionMap map = actions.AddActionMap("Player");
            map.AddAction("Move", InputActionType.Value, "<Gamepad>/leftStick");
            map.AddAction("Look", InputActionType.Value, "<Gamepad>/rightStick");
            map.AddAction("Jump", InputActionType.Button, "<Gamepad>/buttonSouth");
            map.AddAction("Crouch", InputActionType.Button, "<Gamepad>/buttonEast");
            map.AddAction("Sprint", InputActionType.Button, "<Gamepad>/leftStickPress");
            map.AddAction("Interact", InputActionType.Button, "<Gamepad>/buttonNorth");
        }

        [TearDown]
        public void TearDown()
        {
            if (screen != null && screen.added)
            {
                InputSystem.RemoveDevice(screen);
            }
            rig?.Dispose();
        }

        private FirstPersonController CreatePlayer(out TouchControls touch, bool pauseMenu = false,
                                                   TouchControls.ShowMode show = TouchControls.ShowMode.Always)
        {
            TouchControls added = null;
            FirstPersonController player = rig.CreatePlayer(Vector3.zero, go =>
            {
                TestRig.SetField(go.GetComponent<FirstPersonController>(), "actionsOverride", actions);
                if (pauseMenu)
                {
                    go.AddComponent<PauseMenu>();
                }
                added = go.AddComponent<TouchControls>();
                TestRig.SetField(added, "show", show);
            });
            touch = added;
            return player;
        }

        private void Touch(int id, TouchPhase phase, Vector2 position)
        {
            InputSystem.QueueStateEvent(screen, new TouchState { touchId = id, phase = phase, position = position });
        }

        private static float Scale => Mathf.Max(0.5f, Screen.height / 1080f);

        // The controls are laid out on the screen; a run without one (a headless build server) can't place a touch.
        private static void RequireScreen()
        {
            if (Screen.width < 200 || Screen.height < 200)
            {
                Assert.Ignore($"Needs a screen to lay the controls out on; this run's is {Screen.width}x{Screen.height}.");
            }
        }

        [UnityTest]
        public IEnumerator StickMovesThePlayer()
        {
            rig.CreateGround();
            FirstPersonController player = CreatePlayer(out TouchControls touch);
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(touch.IsShowing, "showing");
            Assert.IsTrue(player.OnScreenControls, "the player knows on-screen controls drive it");

            RequireScreen();
            Vector2 start = touch.StickArea.position + touch.StickArea.size * new Vector2(0.4f, 0.3f);
            Touch(1, TouchPhase.Began, start);
            yield return null;
            Touch(1, TouchPhase.Moved, start + Vector2.up * (110f * Scale));   // the full Stick Radius forward
            yield return new WaitForSeconds(0.8f);
            Assert.AreEqual(1f, touch.Stick.y, 0.01f, "the stick is pushed all the way");
            Assert.Greater(player.transform.position.z, 1f, "walked forward");

            Touch(1, TouchPhase.Ended, start);
            yield return null;
            Assert.AreEqual(Vector2.zero, touch.Stick, "the stick lets go with the thumb");
        }

        [UnityTest]
        public IEnumerator DraggingOnTheRightTurnsTheView()
        {
            rig.CreateGround();
            FirstPersonController player = CreatePlayer(out TouchControls touch);
            yield return new WaitForSeconds(0.3f);
            float yawBefore = player.Yaw;

            RequireScreen();
            Vector2 start = new Vector2(Screen.width * 0.7f, Screen.height * 0.8f);   // right of the stick area, above the buttons
            Touch(2, TouchPhase.Began, start);
            yield return null;
            for (int i = 1; i <= 10; i++)
            {
                Touch(2, TouchPhase.Moved, start + Vector2.right * (10f * i));
                yield return null;
            }
            Touch(2, TouchPhase.Ended, start + Vector2.right * 100f);
            yield return null;

            float turned = Mathf.DeltaAngle(yawBefore, player.Yaw);
            Assert.AreEqual(100f * 0.18f / Scale, turned, 1f, "turned by the drag times Look Sensitivity");
            Assert.AreEqual(0f, player.transform.position.z, 0.05f, "without walking");
        }

        [UnityTest]
        public IEnumerator JumpButtonJumps()
        {
            rig.CreateGround();
            FirstPersonController player = CreatePlayer(out TouchControls touch);
            int jumps = 0;
            player.Jumped += () => jumps++;
            yield return new WaitForSeconds(0.3f);

            RequireScreen();
            Vector2 jump = touch.ButtonScreenRect(TouchControls.Button.Jump).center;
            Touch(3, TouchPhase.Began, jump);
            yield return null;
            yield return null;
            Touch(3, TouchPhase.Ended, jump);
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(1, jumps, "a tap on Jump jumps");
        }

        [UnityTest]
        public IEnumerator AddsItsGamepadOnlyWhileShowing()
        {
            rig.CreateGround();
            int before = Gamepad.all.Count;
            CreatePlayer(out TouchControls touch, show: TouchControls.ShowMode.Never);
            yield return null;
            Assert.IsFalse(touch.IsShowing, "hidden");
            Assert.AreEqual(before, Gamepad.all.Count, "no virtual gamepad while hidden");

            TestRig.SetField(touch, "show", TouchControls.ShowMode.Always);
            yield return null;
            Assert.IsTrue(touch.IsShowing, "showing");
            Assert.AreEqual(before + 1, Gamepad.all.Count, "its gamepad while showing");

            touch.enabled = false;
            yield return null;
            Assert.AreEqual(before, Gamepad.all.Count, "gone again");
        }

        [UnityTest]
        public IEnumerator PauseButtonPausesAndResumes()
        {
            rig.CreateGround();
            FirstPersonController player = CreatePlayer(out TouchControls touch, pauseMenu: true);
            PauseMenu menu = player.GetComponent<PauseMenu>();
            yield return new WaitForSeconds(0.3f);
            RequireScreen();
            Vector2 pause = touch.ButtonScreenRect(TouchControls.Button.Pause).center;

            Touch(4, TouchPhase.Began, pause);
            yield return null;
            yield return null;
            Assert.IsTrue(menu.IsPaused, "paused");
            yield return null;
            Assert.IsTrue(menu.IsPaused, "the finger still on the button doesn't resume at once");
            Assert.IsTrue(touch.IsShowing, "the Pause button stays under the menu");
            Touch(4, TouchPhase.Ended, pause);
            yield return null;
            yield return null;

            Touch(5, TouchPhase.Began, pause);
            yield return null;
            yield return null;
            Touch(5, TouchPhase.Ended, pause);
            yield return null;
            yield return null;
            Assert.IsFalse(menu.IsPaused, "a second tap resumes");
        }

        [UnityTest]
        public IEnumerator TheTouchscreenItselfIsLeftOut()
        {
            rig.CreateGround();
            FirstPersonController player = CreatePlayer(out TouchControls touch);
            yield return new WaitForSeconds(0.3f);

            CollectionAssert.DoesNotContain(player.Actions.devices.Value.ToArray(), screen, "the actions don't read the touchscreen");
            touch.enabled = false;
            yield return null;
            Assert.IsFalse(player.OnScreenControls, "off again with the controls");
            Assert.IsNull(player.Actions.devices, "every device again");
        }
    }
}
