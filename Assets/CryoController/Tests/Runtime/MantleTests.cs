using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace CryoController.Tests
{
    public class MantleTests
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

        // A block across the player's path whose near face is 1 m ahead.
        private BoxCollider CreateLedge(float height)
        {
            return rig.CreateBox("Ledge", new Vector3(0f, height * 0.5f, 2f), new Vector3(4f, height, 2f));
        }

        [UnityTest]
        public IEnumerator WalksUpOntoAKneeHighLedge()
        {
            rig.CreateGround();
            CreateLedge(0.6f);   // above the Step Offset, below the Auto Mantle Height
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            float climbed = 0f;
            player.Mantled += height => climbed = height;
            yield return new WaitForSeconds(0.3f);

            rig.SetMove(Vector2.up);
            yield return TestRig.WaitUntil(() => climbed > 0f, 2f);
            // Measured from the feet, which rest about a skin width above the ground.
            Assert.AreEqual(0.6f - TestRig.SkinWidth, climbed, 0.08f, "climbed the knee-high ledge without a jump");
            yield return TestRig.WaitUntil(() => !player.IsMantling, 1f);
            rig.SetMove(Vector2.zero);
            yield return new WaitForSeconds(0.2f);

            Assert.IsTrue(player.IsGrounded, "standing");
            Assert.AreEqual(0.6f, player.transform.position.y, 0.15f, "on top of the ledge");
        }

        [UnityTest]
        public IEnumerator WalksUpStairsWithoutMantling()
        {
            // Three 0.25 m steps with 0.3 m treads, under the 0.3 m Step Offset, up to a landing at 0.75 m. The riser
            // after the next one is in reach of the ledge probe but not against the capsule, so it is not climbed.
            rig.CreateGround();
            for (int i = 0; i < 3; i++)
            {
                float height = 0.25f * (i + 1);
                float near = 1f + 0.3f * i;
                rig.CreateBox("Step " + i, new Vector3(0f, height * 0.5f, (near + 4f) * 0.5f), new Vector3(4f, height, 4f - near));
            }
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            int mantles = 0;
            player.Mantled += height => mantles++;
            yield return new WaitForSeconds(0.3f);

            rig.SetMove(Vector2.up);
            yield return TestRig.WaitUntil(() => player.transform.position.z > 2.4f, 3f);
            rig.SetMove(Vector2.zero);
            yield return new WaitForSeconds(0.3f);

            Assert.AreEqual(0, mantles, "stairs are stepped up, not mantled");
            Assert.AreEqual(0.75f + TestRig.SkinWidth, player.transform.position.y, 0.08f, "on the landing");
        }

        [UnityTest]
        public IEnumerator NeedsAJumpForAHighLedge()
        {
            rig.CreateGround();
            CreateLedge(1.4f);   // above the Auto Mantle Height, below the Mantle Max Height
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            int mantles = 0;
            player.Mantled += height => mantles++;
            yield return new WaitForSeconds(0.3f);

            rig.SetMove(Vector2.up);
            yield return new WaitForSeconds(0.8f);
            Assert.AreEqual(0, mantles, "walking into it is not enough");

            yield return rig.Press(GamepadButton.South);
            yield return TestRig.WaitUntil(() => mantles > 0, 1.5f);
            Assert.AreEqual(1, mantles, "a jump at it climbs it");
            yield return TestRig.WaitUntil(() => !player.IsMantling, 1f);
            rig.SetMove(Vector2.zero);
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(1.4f, player.transform.position.y, 0.15f, "on top of the ledge");
        }

        [UnityTest]
        public IEnumerator IgnoresLedgesOutsideTheEnvironmentLayers()
        {
            rig.CreateGround();
            int water = LayerMask.NameToLayer("Water");
            CreateLedge(0.6f).gameObject.layer = water;
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            player.EnvironmentLayers = player.EnvironmentLayers & ~(1 << water);
            int mantles = 0;
            player.Mantled += height => mantles++;
            yield return new WaitForSeconds(0.3f);

            rig.SetMove(Vector2.up);
            yield return new WaitForSeconds(1.5f);

            Assert.AreEqual(0, mantles, "a ledge on a layer left out of Environment Layers is not climbed");
            Assert.Less(player.transform.position.y, 0.15f, "the player stayed on the ground in front of it");
        }

        [UnityTest]
        public IEnumerator PushesPropsInsteadOfClimbingThem()
        {
            rig.CreateGround();
            BoxCollider prop = CreateLedge(0.6f);
            Rigidbody body = prop.gameObject.AddComponent<Rigidbody>();
            body.mass = 5f;
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            int mantles = 0;
            player.Mantled += height => mantles++;
            yield return new WaitForSeconds(0.3f);
            float start = prop.transform.position.z;

            rig.SetMove(Vector2.up);
            yield return new WaitForSeconds(1.5f);

            Assert.AreEqual(0, mantles, "a pushable prop is not climbed");
            Assert.Greater(prop.transform.position.z - start, 0.2f, "it was pushed along");
        }
    }
}
