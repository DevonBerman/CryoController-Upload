using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace CryoController.Tests
{
    public class LeanTests
    {
        private TestRig rig;

        [SetUp]
        public void SetUp()
        {
            rig = new TestRig();
        }

        [TearDown]
        public void TearDown()
        {
            rig?.Dispose();
        }

        [UnityTest]
        public IEnumerator LeansTheHeadSidewaysAndBack()
        {
            rig.CreateGround();
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            Transform eyes = player.GetComponentInChildren<Camera>(true).transform;
            yield return new WaitForSeconds(0.3f);

            rig.Hold(GamepadButton.RightShoulder);
            yield return new WaitForSeconds(0.8f);
            Assert.AreEqual(1f, player.LeanAmount, 0.05f, "fully leaned right");
            Assert.AreEqual(0.35f, eyes.localPosition.x, 0.03f, "head moved the default Lean Distance to the right");
            Assert.AreEqual(-12f, Mathf.DeltaAngle(0f, eyes.localEulerAngles.z), 1f, "rolled the default Lean Angle, head to the right");

            rig.Hold(GamepadButton.RightShoulder, false);
            yield return new WaitForSeconds(0.8f);
            Assert.AreEqual(0f, player.LeanAmount, 0.05f, "upright again");
            Assert.AreEqual(0f, eyes.localPosition.x, 0.02f, "head centred again");
        }

        [UnityTest]
        public IEnumerator ReducedCameraMotionKeepsThePeekButNotTheRoll()
        {
            rig.CreateGround();
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            player.ReduceCameraMotion = true;
            Transform eyes = player.GetComponentInChildren<Camera>(true).transform;
            yield return new WaitForSeconds(0.3f);

            rig.Hold(GamepadButton.RightShoulder);
            yield return new WaitForSeconds(0.8f);

            Assert.AreEqual(0.35f, eyes.localPosition.x, 0.03f, "the head still moves aside to peek");
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, eyes.localEulerAngles.z), 0.1f, "without rolling");
        }

        [UnityTest]
        public IEnumerator LeanStopsShortOfAWall()
        {
            rig.CreateGround();
            rig.CreateBox("Wall", new Vector3(0.75f, 1f, 0f), new Vector3(0.5f, 4f, 4f));   // near face 0.5 m to the right
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            Transform eyes = player.GetComponentInChildren<Camera>(true).transform;
            yield return new WaitForSeconds(0.3f);

            rig.Hold(GamepadButton.RightShoulder);
            yield return new WaitForSeconds(0.8f);

            Assert.Less(eyes.localPosition.x, 0.27f, "stopped before the wall");
            Assert.Greater(eyes.localPosition.x, 0.1f, "but leaned as far as there was room for");
            Assert.Less(player.LeanAmount, 0.9f, "not a full lean");
        }
    }
}
