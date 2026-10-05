using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CryoController.Tests
{
    public class CameraApiTests
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

        private static Transform Eyes(FirstPersonController player)
        {
            return player.GetComponentInChildren<Camera>(true).transform;
        }

        [UnityTest]
        public IEnumerator PitchAndYawTurnTheView()
        {
            rig.CreateGround();
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            yield return null;

            player.Yaw = 90f;
            player.Pitch = 30f;
            yield return null;

            Assert.AreEqual(1f, Vector3.Dot(player.transform.forward, Vector3.right), 0.01f, "facing +x");
            Assert.AreEqual(-Mathf.Sin(30f * Mathf.Deg2Rad), Eyes(player).forward.y, 0.01f, "looking 30 degrees down");

            player.Pitch = 120f;
            Assert.LessOrEqual(player.Pitch, 89f, "pitch is clamped to Max Pitch");
            player.Yaw = -90f;
            Assert.AreEqual(270f, player.Yaw, 0.01f, "yaw wraps to 0..360");
        }

        [UnityTest]
        public IEnumerator LookAtFacesAPoint()
        {
            rig.CreateGround();
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            yield return null;

            Vector3 target = new Vector3(4f, 0.5f, -3f);
            player.LookAt(target);
            yield return null;

            Vector3 wanted = (target - Eyes(player).position).normalized;
            Assert.Greater(Vector3.Dot(Eyes(player).forward, wanted), 0.999f, "the camera points at the target");
        }

        [UnityTest]
        public IEnumerator CameraKickSpringsBack()
        {
            rig.CreateGround();
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            yield return new WaitForSeconds(0.3f);
            float restingY = Eyes(player).forward.y;

            player.AddCameraKick(new Vector2(5f, 0f));
            float highest = 0f;
            float end = Time.time + 0.4f;
            while (Time.time < end)
            {
                yield return null;
                float up = Mathf.Asin(Mathf.Clamp(Eyes(player).forward.y, -1f, 1f)) * Mathf.Rad2Deg
                           - Mathf.Asin(restingY) * Mathf.Rad2Deg;
                highest = Mathf.Max(highest, up);
            }
            Assert.AreEqual(5f, highest, 0.5f, "the view kicked up by about 5 degrees");
            Assert.AreEqual(0f, player.Pitch, 0.01f, "the kick doesn't move where the player aims");

            yield return new WaitForSeconds(1f);
            Assert.AreEqual(restingY, Eyes(player).forward.y, 0.002f, "the view settled back");
        }
    }
}
