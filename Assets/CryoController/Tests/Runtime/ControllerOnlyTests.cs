using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace CryoController.Tests
{
    public class ControllerOnlyTests
    {
        private static int s_MouseCount;

        private TestRig rig;
        private Mouse mouse;

        [SetUp]
        public void SetUp()
        {
            rig = new TestRig();
        }

        [TearDown]
        public void TearDown()
        {
            if (mouse != null && mouse.added)
            {
                InputSystem.RemoveDevice(mouse);
            }
            rig?.Dispose();
        }

        [UnityTest]
        public IEnumerator IgnoresTheMouseButNotTheGamepad()
        {
            // Jump is also bound to a virtual mouse's left button, which only a test can press.
            string name = "CryoControllerTestMouse" + ++s_MouseCount;
            mouse = InputSystem.AddDevice<Mouse>(name);
            rig.Actions.FindAction("Player/Jump").AddBinding("/" + name + "/leftButton", groups: "Keyboard&Mouse");
            rig.CreateGround();
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            int jumps = 0;
            player.Jumped += () => jumps++;
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(player.IsGrounded, "on the ground");

            player.ControllerOnly = true;
            yield return Click();
            yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(0, jumps, "the mouse button does nothing with Controller Only on");

            yield return rig.Press(GamepadButton.South);
            Assert.AreEqual(1, jumps, "the gamepad still jumps");
            yield return TestRig.WaitUntil(() => player.IsGrounded, 3f);
            yield return new WaitForSeconds(0.2f);

            player.ControllerOnly = false;
            yield return Click();
            Assert.AreEqual(2, jumps, "with it off, the mouse button jumps again");
        }

        private IEnumerator Click()
        {
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left, false));
            yield return null;
        }
    }
}
