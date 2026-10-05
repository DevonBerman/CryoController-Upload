using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CryoController.Tests
{
    public class MovingGroundTests
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

        private Rigidbody CreatePlatform(Vector3 size, Vector3 travel, float speed, float turnSpeed)
        {
            GameObject go = rig.Track(new GameObject("Platform"));
            go.SetActive(false);
            go.transform.position = new Vector3(0f, -size.y * 0.5f, 0f);   // top at y = 0
            go.AddComponent<BoxCollider>().size = size;
            Rigidbody body = go.AddComponent<Rigidbody>();
            MovingPlatform mover = go.AddComponent<MovingPlatform>();
            TestRig.SetField(mover, "travel", travel);
            TestRig.SetField(mover, "speed", speed);
            TestRig.SetField(mover, "waitAtEnds", 0f);
            TestRig.SetField(mover, "turnSpeed", turnSpeed);
            go.SetActive(true);
            Physics.SyncTransforms();
            return body;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        [UnityTest]
        public IEnumerator RidesAMovingPlatform()
        {
            Rigidbody platform = CreatePlatform(new Vector3(4f, 0.25f, 4f), new Vector3(0f, 0f, 8f), 2f, 0f);
            FirstPersonController player = rig.CreatePlayer(new Vector3(0f, 0.05f, 0f));
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(player.IsGrounded, "standing on the platform");
            Vector3 playerStart = player.transform.position;
            Vector3 platformStart = platform.transform.position;

            yield return new WaitForSeconds(1f);

            float platformMoved = platform.transform.position.z - platformStart.z;
            float playerMoved = player.transform.position.z - playerStart.z;
            Assert.Greater(platformMoved, 1.5f, "the platform moved");
            Assert.AreEqual(platformMoved, playerMoved, 0.1f, "the player moved with it");
            Assert.IsTrue(player.IsGrounded, "still on it");
            Assert.AreEqual(platform.GetComponent<Collider>(), player.Ground, "the platform is the ground");
            Assert.AreEqual(2f, player.GroundVelocity.z, 0.5f, "GroundVelocity reports the platform's speed");
        }

        [UnityTest]
        public IEnumerator TurnsWithATurntable()
        {
            Rigidbody table = CreatePlatform(new Vector3(6f, 0.25f, 6f), Vector3.zero, 0f, 90f);
            FirstPersonController player = rig.CreatePlayer(new Vector3(1.5f, 0.05f, 0f));
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(player.IsGrounded, "standing on the turntable");
            float yawStart = player.transform.eulerAngles.y;
            Vector3 fromCentre = Flat(player.transform.position - table.transform.position);

            yield return new WaitForSeconds(1f);

            Vector3 toCentre = Flat(player.transform.position - table.transform.position);
            float carriedAround = Vector3.SignedAngle(fromCentre, toCentre, Vector3.up);
            float turned = Mathf.DeltaAngle(yawStart, player.transform.eulerAngles.y);
            Assert.AreEqual(90f, carriedAround, 12f, "carried a quarter turn around the centre");
            Assert.AreEqual(90f, turned, 12f, "turned a quarter turn with it");
            Assert.AreEqual(1.5f, toCentre.magnitude, 0.15f, "at the same distance from the centre");
        }
    }
}
