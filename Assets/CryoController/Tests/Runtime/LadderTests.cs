using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CryoController.Tests
{
    public class LadderTests
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

        // A 4 m tower whose near face is 2 m ahead, and a thin ladder zone against it reaching a little above its top.
        private Ladder CreateTowerWithLadder()
        {
            rig.CreateBox("Tower", new Vector3(0f, 2f, 3f), new Vector3(4f, 4f, 2f));
            BoxCollider zone = rig.CreateBox("Ladder", new Vector3(0f, 2.2f, 1.85f), new Vector3(1f, 4.4f, 0.3f));
            return zone.gameObject.AddComponent<Ladder>();
        }

        // Walks the player into the ladder and climbs until its feet are this high.
        private IEnumerator ClimbTo(FirstPersonController player, float height)
        {
            yield return new WaitForSeconds(0.3f);
            rig.SetMove(Vector2.up);
            yield return TestRig.WaitUntil(() => player.IsClimbing, 2f);
            Assert.IsTrue(player.IsClimbing, "walking into the ladder grabbed it");
            yield return TestRig.WaitUntil(() => player.transform.position.y > height, 2f);
            Assert.IsTrue(player.IsClimbing, "still climbing");
        }

        [UnityTest]
        public IEnumerator ClimbsOntoThePlatformAtTheTop()
        {
            rig.CreateGround();
            CreateTowerWithLadder();
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);

            yield return ClimbTo(player, 1f);
            yield return TestRig.WaitUntil(() => !player.IsClimbing && !player.IsMantling, 3f);
            rig.SetMove(Vector2.zero);
            yield return new WaitForSeconds(0.3f);

            Assert.IsTrue(player.IsGrounded, "standing");
            Assert.AreEqual(4f, player.transform.position.y, 0.15f, "on top of the tower");
        }

        [UnityTest]
        public IEnumerator LetsGoWhenTheLadderIsDestroyed()
        {
            rig.CreateGround();
            Ladder ladder = CreateTowerWithLadder();
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);

            yield return ClimbTo(player, 1f);
            Object.Destroy(ladder.gameObject);   // a destroyed trigger never reports the player leaving it
            rig.SetMove(Vector2.zero);
            yield return null;
            yield return null;
            Assert.IsFalse(player.IsClimbing, "let go of the destroyed ladder");

            yield return TestRig.WaitUntil(() => player.IsGrounded, 2f);
            Assert.Less(player.transform.position.y, 0.2f, "fell back to the ground");
        }

        [UnityTest]
        public IEnumerator LetsGoWhenTheLadderIsSwitchedOff()
        {
            rig.CreateGround();
            Ladder ladder = CreateTowerWithLadder();
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);

            yield return ClimbTo(player, 1f);
            ladder.gameObject.SetActive(false);
            rig.SetMove(Vector2.zero);
            yield return null;
            yield return null;
            Assert.IsFalse(player.IsClimbing, "let go of the switched-off ladder");

            // Walking back into where it was does not grab it again.
            yield return TestRig.WaitUntil(() => player.IsGrounded, 2f);
            rig.SetMove(Vector2.up);
            yield return new WaitForSeconds(1f);
            Assert.IsFalse(player.IsClimbing, "a switched-off ladder is not grabbed");
        }
    }
}
