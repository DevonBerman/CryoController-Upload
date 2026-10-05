using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace CryoController.Tests
{
    public class SwimTests
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

        // Water over the ground from z = -1 to 7, with its surface at the height.
        private WaterVolume CreateWater(float surface)
        {
            BoxCollider zone = rig.CreateBox("Water", new Vector3(0f, surface * 0.5f, 3f), new Vector3(10f, surface, 8f));
            return zone.gameObject.AddComponent<WaterVolume>();
        }

        private static float Eyes(FirstPersonController player)
        {
            return player.GetComponentInChildren<Camera>(true).transform.position.y;
        }

        [UnityTest]
        public IEnumerator FloatsAtTheSurfaceOfDeepWater()
        {
            rig.CreateGround();
            CreateWater(2f);
            FirstPersonController player = rig.CreatePlayer(new Vector3(0f, 0f, 3f));
            yield return TestRig.WaitUntil(() => player.IsSwimming, 1f);
            Assert.IsTrue(player.IsSwimming, "swimming in water deeper than Swim Depth");

            yield return new WaitForSeconds(3f);
            Assert.AreEqual(2.15f, Eyes(player), 0.1f, "floating with the eyes just above the surface");
            Assert.IsFalse(player.IsUnderwater, "the camera is above the water");
        }

        [UnityTest]
        public IEnumerator CrouchDivesAndJumpRises()
        {
            rig.CreateGround();
            CreateWater(3f);
            FirstPersonController player = rig.CreatePlayer(new Vector3(0f, 0.5f, 3f));
            yield return new WaitForSeconds(2.5f);
            float floating = player.transform.position.y;

            rig.Hold(GamepadButton.East);   // Crouch
            yield return new WaitForSeconds(1f);
            rig.Hold(GamepadButton.East, false);
            Assert.Less(player.transform.position.y, floating - 1f, "dived");
            Assert.IsTrue(player.IsUnderwater, "the camera went under");
            Assert.IsFalse(player.IsCrouching, "the capsule stays at full height in water");

            rig.Hold(GamepadButton.South);   // Jump
            yield return new WaitForSeconds(1.5f);
            rig.Hold(GamepadButton.South, false);
            Assert.AreEqual(floating, player.transform.position.y, 0.15f, "rose back to the surface, and no higher");
        }

        [UnityTest]
        public IEnumerator FallingIntoWaterDoesntLand()
        {
            rig.CreateGround();
            CreateWater(2.5f);
            FirstPersonController player = rig.CreatePlayer(new Vector3(0f, 12f, 3f));
            float hardest = 0f;
            player.Landed += speed => hardest = Mathf.Max(hardest, speed);
            yield return TestRig.WaitUntil(() => player.IsSwimming, 3f);
            Assert.IsTrue(player.IsSwimming, "caught by the water");
            yield return new WaitForSeconds(2f);
            Assert.Less(hardest, 6f, "no hard landing on the bottom");
        }

        [UnityTest]
        public IEnumerator ShallowWaterIsWaded()
        {
            rig.CreateGround();
            CreateWater(0.7f);
            FirstPersonController player = rig.CreatePlayer(new Vector3(0f, 0f, 3f));
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(player.IsSwimming, "knee-deep water is walked through");
            Assert.IsNotNull(player.Water, "but the player knows it is in water");

            rig.SetMove(Vector2.up);
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(player.IsGrounded, "walking on the bottom");
        }

        [UnityTest]
        public IEnumerator SwimsOutOntoAnEdge()
        {
            rig.CreateGround();
            CreateWater(2f);   // reaches z = 7
            rig.CreateBox("Deck", new Vector3(0f, 1.1f, 9f), new Vector3(10f, 2.2f, 4f));   // its face at z = 7, 0.2 m above the water
            FirstPersonController player = rig.CreatePlayer(new Vector3(0f, 0f, 4.5f));
            yield return new WaitForSeconds(2.5f);
            Assert.IsTrue(player.IsSwimming, "swimming");

            rig.SetMove(Vector2.up);
            yield return TestRig.WaitUntil(() => !player.IsSwimming && !player.IsMantling && player.transform.position.z > 7.2f, 4f);
            rig.SetMove(Vector2.zero);
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(player.IsSwimming, "out of the water");
            Assert.AreEqual(2.2f, player.transform.position.y, 0.15f, "standing on the deck");
        }
    }
}
