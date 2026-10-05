using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace CryoController.Tests
{
    public class DoorTests
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

        // A 1 m wide, 2 m tall door across z = 2, hinged at x = -0.5, its leaf reaching along +x to x = 0.5. Configure sets
        // fields before it wakes up.
        private Door CreateDoor(System.Action<Door> configure = null, Door.DoorMotion motion = Door.DoorMotion.Hinged)
        {
            GameObject hinge = rig.Track(new GameObject("Door"));
            hinge.SetActive(false);
            hinge.transform.position = new Vector3(-0.5f, 0f, 2f);
            GameObject leaf = new GameObject("Leaf");
            leaf.transform.SetParent(hinge.transform, false);
            leaf.transform.localPosition = new Vector3(0.5f, 1f, 0f);
            leaf.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 0.08f);
            Door door = hinge.AddComponent<Door>();
            TestRig.SetField(door, "motion", motion);
            TestRig.SetField(door, "openTime", 0.4f);
            configure?.Invoke(door);
            hinge.SetActive(true);
            Physics.SyncTransforms();
            return door;
        }

        private FirstPersonController CreatePlayer(Vector3 feet, float yaw = 0f)
        {
            FirstPersonController player = rig.CreatePlayer(feet, go =>
            {
                go.AddComponent<PlayerInteractor>();
                go.AddComponent<PlayerKeys>();
            });
            player.Teleport(feet, yaw);
            return player;
        }

        private static Vector3 LeafCenter(Door door)
        {
            return door.GetComponentInChildren<BoxCollider>().bounds.center;
        }

        [UnityTest]
        public IEnumerator OpensAndClosesWithInteract()
        {
            rig.CreateGround();
            Door door = CreateDoor();
            CreatePlayer(Vector3.zero);
            int opened = 0, closed = 0;
            door.Opened += () => opened++;
            door.Closed += () => closed++;
            yield return new WaitForSeconds(0.3f);

            yield return rig.Press(GamepadButton.North);
            Assert.IsTrue(door.IsOpen, "opening");
            yield return TestRig.WaitUntil(() => door.OpenAmount >= 1f, 1.5f);
            Assert.AreEqual(1f, door.OpenAmount, "fully open");
            Assert.AreEqual(1, opened, "Opened once");

            door.SetOpen(false);
            yield return TestRig.WaitUntil(() => door.OpenAmount <= 0f, 1.5f);
            Assert.AreEqual(0f, door.OpenAmount, "closed again");
            Assert.AreEqual(1, closed, "Closed once");
        }

        [UnityTest]
        public IEnumerator SwingsAwayFromThePlayer()
        {
            rig.CreateGround();
            Door door = CreateDoor();
            CreatePlayer(Vector3.zero);   // on the -z side
            yield return new WaitForSeconds(0.3f);

            yield return rig.Press(GamepadButton.North);
            yield return TestRig.WaitUntil(() => door.OpenAmount >= 1f, 1.5f);
            Assert.Greater(LeafCenter(door).z, 2.3f, "swung to the far side, away from the player");

            door.SetOpen(false, instant: true);
            FirstPersonController other = CreatePlayer(new Vector3(0f, 0f, 4f), 180f);   // on the +z side, facing it
            yield return new WaitForSeconds(0.2f);
            door.Interact(other.GetComponent<PlayerInteractor>());
            yield return TestRig.WaitUntil(() => door.OpenAmount >= 1f, 1.5f);
            Assert.Less(LeafCenter(door).z, 1.7f, "swung to the near side, away from the other player");
        }

        [UnityTest]
        public IEnumerator WaitsForAPlayerInTheWay()
        {
            rig.CreateGround();
            // Swings towards -z whoever opens it, where the player stands inside its arc.
            Door door = CreateDoor(d =>
            {
                TestRig.SetField(d, "swingAway", false);
                TestRig.SetField(d, "openAngle", 95f);
            });
            FirstPersonController player = CreatePlayer(new Vector3(0.1f, 0f, 1.3f));
            yield return new WaitForSeconds(0.3f);
            Vector3 standing = player.transform.position;

            door.SetOpen(true);
            yield return new WaitForSeconds(1f);
            Assert.Less(door.OpenAmount, 0.9f, "stopped short");
            Assert.IsTrue(door.IsBlocked, "waiting for the player");
            Assert.Less(Vector3.Distance(standing, player.transform.position), 0.05f, "the player wasn't pushed");

            player.Teleport(new Vector3(0f, 0f, -2f), 0f);
            yield return TestRig.WaitUntil(() => door.OpenAmount >= 1f, 1.5f);
            Assert.AreEqual(1f, door.OpenAmount, "carried on once the player moved");
        }

        [UnityTest]
        public IEnumerator LockedDoorOpensOnlyWithItsKey()
        {
            rig.CreateGround();
            Door door = CreateDoor(d =>
            {
                TestRig.SetField(d, "locked", true);
                TestRig.SetField(d, "keyId", "red");
            });
            FirstPersonController player = CreatePlayer(Vector3.zero);
            PlayerInteractor interactor = player.GetComponent<PlayerInteractor>();
            int tried = 0;
            door.LockedTried += () => tried++;
            yield return new WaitForSeconds(0.3f);

            Assert.AreEqual("Locked", interactor.CurrentPrompt, "the prompt says it is locked");
            yield return rig.Press(GamepadButton.North);
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(door.IsOpen, "still shut");
            Assert.AreEqual(1, tried, "the lock was tried");
            Assert.AreEqual("It's locked.", interactor.Message, "the player is told");

            player.GetComponent<PlayerKeys>().Add("red");
            yield return null;
            Assert.AreEqual("Unlock", interactor.CurrentPrompt, "the prompt offers to unlock it");
            yield return rig.Press(GamepadButton.North);
            Assert.IsFalse(door.IsLocked, "unlocked");
            Assert.IsTrue(door.IsOpen, "and opening");
            Assert.IsTrue(player.GetComponent<PlayerKeys>().Has("red"), "the key is kept");
        }

        [UnityTest]
        public IEnumerator DrawerSlidesItsDistance()
        {
            rig.CreateGround();
            Door drawer = CreateDoor(d => TestRig.SetField(d, "openDistance", -0.4f), Door.DoorMotion.Sliding);
            Vector3 closed = drawer.transform.position;
            yield return null;

            drawer.SetOpen(true);
            yield return TestRig.WaitUntil(() => drawer.OpenAmount >= 1f, 1.5f);
            Assert.AreEqual(-0.4f, drawer.transform.position.z - closed.z, 0.01f, "slid 0.4 m back, towards the player");
            Assert.Less(Quaternion.Angle(Quaternion.identity, drawer.transform.rotation), 0.01f, "without turning");
        }

        [UnityTest]
        public IEnumerator KeyPickupGivesTheKey()
        {
            rig.CreateGround();
            BoxCollider keyBox = rig.CreateBox("Key", new Vector3(0f, 1.6f, 1.5f), Vector3.one * 0.3f);
            KeyPickup key = keyBox.gameObject.AddComponent<KeyPickup>();
            TestRig.SetField(key, "keyId", "shed");
            TestRig.SetField(key, "displayName", "shed key");
            FirstPersonController player = CreatePlayer(Vector3.zero);
            yield return new WaitForSeconds(0.3f);

            Assert.AreEqual("Take shed key", player.GetComponent<PlayerInteractor>().CurrentPrompt);
            yield return rig.Press(GamepadButton.North);
            Assert.IsTrue(player.GetComponent<PlayerKeys>().Has("shed"), "the player has the key");
            Assert.IsFalse(keyBox.gameObject.activeSelf, "the key is gone from the world");
        }
    }
}
