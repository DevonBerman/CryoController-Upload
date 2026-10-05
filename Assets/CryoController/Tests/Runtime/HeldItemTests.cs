using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace CryoController.Tests
{
    public class HeldItemTests
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

        // A player holding a flashlight in view: a held item under the camera with a light.
        private FirstPersonController CreatePlayer(out HeldItem item, out Flashlight flashlight)
        {
            FirstPersonController player = rig.CreatePlayer(Vector3.zero, go =>
            {
                go.AddComponent<PlayerInteractor>();
                go.AddComponent<PlayerCarry>();
                Transform eyes = go.GetComponentInChildren<Camera>(true).transform;
                var held = new GameObject("Held Flashlight");
                held.transform.SetParent(eyes, false);
                held.transform.localPosition = new Vector3(0.25f, -0.25f, 0.5f);
                held.AddComponent<HeldItem>();
                var lamp = new GameObject("Lamp");
                lamp.transform.SetParent(held.transform, false);
                lamp.AddComponent<Light>().type = LightType.Spot;
                held.AddComponent<Flashlight>();
            });
            item = player.GetComponentInChildren<HeldItem>();
            flashlight = player.GetComponentInChildren<Flashlight>();
            return player;
        }

        [UnityTest]
        public IEnumerator LagsBehindATurnThenCatchesUp()
        {
            rig.CreateGround();
            FirstPersonController player = CreatePlayer(out HeldItem item, out _);
            yield return new WaitForSeconds(0.3f);
            Quaternion rest = item.transform.localRotation;

            player.Yaw += 20f;
            yield return null;
            Assert.Greater(Quaternion.Angle(rest, item.transform.localRotation), 2f, "lags behind the turn");
            yield return new WaitForSeconds(1f);
            Assert.Less(Quaternion.Angle(rest, item.transform.localRotation), 0.5f, "back at rest");
        }

        [UnityTest]
        public IEnumerator LowersWhileCarrying()
        {
            rig.CreateGround();
            FirstPersonController player = CreatePlayer(out HeldItem item, out _);
            GameObject box = rig.CreateBox("Box", new Vector3(0f, 0.2f, 1.5f), Vector3.one * 0.4f).gameObject;
            box.AddComponent<Rigidbody>();
            Carryable carryable = box.AddComponent<Carryable>();
            yield return new WaitForSeconds(0.3f);
            Vector3 rest = item.transform.localPosition;

            player.GetComponent<PlayerCarry>().PickUp(carryable);
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(1f, item.Lowered, 0.01f, "lowered while the hands carry something");
            Assert.Less(item.transform.localPosition.y, rest.y - 0.2f, "out of the way");

            player.GetComponent<PlayerCarry>().Drop();
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(0f, item.Lowered, 0.01f, "raised again");
        }

        [UnityTest]
        public IEnumerator FlashlightSwitchesWithItsActionAndSaves()
        {
            rig.CreateGround();
            CreatePlayer(out _, out Flashlight flashlight);
            Light lamp = flashlight.GetComponentInChildren<Light>();
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(flashlight.IsOn, "starts off");
            Assert.IsFalse(lamp.enabled, "the light is off");

            yield return rig.Press(GamepadButton.DpadDown);
            Assert.IsTrue(flashlight.IsOn, "on");
            Assert.IsTrue(lamp.enabled, "the light is on");
            string saved = flashlight.CaptureState();

            yield return rig.Press(GamepadButton.DpadDown);
            Assert.IsFalse(flashlight.IsOn, "off again");
            flashlight.RestoreState(saved);
            Assert.IsTrue(lamp.enabled, "on again from the save");
        }
    }
}
