using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CryoController.Tests
{
    public class CameraShakeTests
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

        // The largest angle the camera turns away from rest over a moment.
        private static IEnumerator LargestTurn(Transform eyes, Quaternion rest, float seconds, System.Action<float> result)
        {
            float largest = 0f;
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                largest = Mathf.Max(largest, Quaternion.Angle(rest, eyes.localRotation));
                yield return null;
            }
            result(largest);
        }

        [UnityTest]
        public IEnumerator ShakesTheViewThenSettles()
        {
            rig.CreateGround();
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            yield return new WaitForSeconds(0.3f);
            Transform eyes = player.GetComponentInChildren<Camera>(true).transform;
            Quaternion rest = eyes.localRotation;
            Vector3 restPosition = eyes.localPosition;

            player.AddCameraShake(1f);
            Assert.AreEqual(1f, player.CameraShake, 0.001f, "a full shake");
            float largest = 0f;
            yield return LargestTurn(eyes, rest, 0.3f, value => largest = value);
            Assert.Greater(largest, 0.3f, "the view shook");

            yield return TestRig.WaitUntil(() => player.CameraShake <= 0f, 1.5f);
            yield return null;
            Assert.AreEqual(0f, player.CameraShake, "the shake died away");
            Assert.Less(Quaternion.Angle(rest, eyes.localRotation), 0.01f, "the view is back where the player was looking");
            Assert.Less(Vector3.Distance(restPosition, eyes.localPosition), 0.001f, "and the camera is back in place");
        }

        [UnityTest]
        public IEnumerator ReducedCameraMotionKeepsTheViewStill()
        {
            rig.CreateGround();
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            player.ReduceCameraMotion = true;   // Reduced Motion Shake is 0 by default
            yield return new WaitForSeconds(0.3f);
            Transform eyes = player.GetComponentInChildren<Camera>(true).transform;
            Quaternion rest = eyes.localRotation;

            player.AddCameraShake(1f);
            float largest = 0f;
            yield return LargestTurn(eyes, rest, 0.3f, value => largest = value);

            Assert.Less(largest, 0.01f, "no shake with reduced camera motion");
        }
    }
}
