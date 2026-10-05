using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CryoController.Tests
{
    public class SaveTests
    {
        private const string Slot = "cryocontroller-test-save";

        private TestRig rig;

        [SetUp]
        public void SetUp()
        {
            rig = new TestRig();
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.Delete(Slot);
            rig?.Dispose();
        }

        // A small world with one of everything that saves.
        private class World
        {
            public FirstPersonController Player;
            public PlayerInteractor Interactor;
            public PlayerKeys Keys;
            public Door Door;
            public KeyPickup Key;
            public Carryable Box;
            public GameObject GateTarget;
            public ToggleInteractable Lever;
        }

        private World CreateWorld()
        {
            var world = new World();
            rig.CreateGround();

            GameObject hinge = rig.Track(new GameObject("Save Door"));
            hinge.SetActive(false);
            hinge.transform.position = new Vector3(4.5f, 0f, 4f);
            GameObject leaf = new GameObject("Leaf");
            leaf.transform.SetParent(hinge.transform, false);
            leaf.transform.localPosition = new Vector3(0.5f, 1f, 0f);
            leaf.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 0.08f);
            world.Door = hinge.AddComponent<Door>();
            TestRig.SetField(world.Door, "locked", true);
            TestRig.SetField(world.Door, "keyId", "save");
            hinge.SetActive(true);

            world.Key = rig.CreateBox("Save Key", new Vector3(-4f, 0.5f, 4f), Vector3.one * 0.2f).gameObject.AddComponent<KeyPickup>();
            TestRig.SetField(world.Key, "keyId", "save");

            GameObject box = rig.CreateBox("Save Box", new Vector3(-2f, 0.2f, 6f), Vector3.one * 0.4f).gameObject;
            box.AddComponent<Rigidbody>();
            world.Box = box.AddComponent<Carryable>();

            world.GateTarget = rig.Track(new GameObject("Gate"));
            GameObject lever = rig.CreateBox("Lever", new Vector3(6f, 0.5f, -3f), Vector3.one * 0.3f).gameObject;
            lever.SetActive(false);
            world.Lever = lever.AddComponent<ToggleInteractable>();
            TestRig.SetField(world.Lever, "targets", new[] { world.GateTarget });
            lever.SetActive(true);

            world.Player = rig.CreatePlayer(Vector3.zero, go =>
            {
                go.AddComponent<PlayerInteractor>();
                go.AddComponent<PlayerKeys>();
            });
            world.Interactor = world.Player.GetComponent<PlayerInteractor>();
            world.Keys = world.Player.GetComponent<PlayerKeys>();
            Physics.SyncTransforms();
            return world;
        }

        [UnityTest]
        public IEnumerator RestorePutsEverythingBack()
        {
            World world = CreateWorld();
            yield return new WaitForSeconds(0.5f);   // the box settles
            Vector3 boxAt = world.Box.transform.position;
            Vector3 playerAt = world.Player.transform.position;
            string before = SaveSystem.Capture(SceneManager.GetActiveScene());

            // Play a little: take the key, unlock and open the door, move the box, pull the lever, walk off.
            world.Key.Interact(world.Interactor);
            world.Door.Interact(world.Interactor);
            world.Door.SetOpen(true, instant: true);
            world.Box.transform.position = new Vector3(2f, 3f, 2f);
            world.Lever.Interact(world.Interactor);
            world.Player.Teleport(new Vector3(3f, 0f, -2f), 90f);
            world.Player.Pitch = 20f;
            yield return null;
            string after = SaveSystem.Capture(SceneManager.GetActiveScene());

            Assert.Greater(SaveSystem.Restore(SceneManager.GetActiveScene(), before), 0, "restored something");
            yield return null;
            Assert.IsFalse(world.Keys.Has("save"), "the key is off the ring");
            Assert.IsTrue(world.Key.gameObject.activeSelf, "and back in the world");
            Assert.IsFalse(world.Key.IsTaken, "untaken");
            Assert.IsTrue(world.Door.IsLocked, "the door is locked again");
            Assert.AreEqual(0f, world.Door.OpenAmount, "and shut");
            Assert.Less(Vector3.Distance(boxAt, world.Box.transform.position), 0.05f, "the box is back");
            Assert.IsTrue(world.GateTarget.activeSelf, "the lever's gate is back");
            Assert.Less(Vector3.Distance(playerAt, world.Player.transform.position), 0.05f, "the player is back");
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, world.Player.Yaw), 0.01f, "facing the way they did");

            Assert.Greater(SaveSystem.Restore(SceneManager.GetActiveScene(), after), 0, "restored the later save");
            yield return null;
            Assert.IsTrue(world.Keys.Has("save"), "the key is on the ring");
            Assert.IsFalse(world.Key.gameObject.activeSelf, "and gone from the world");
            Assert.IsFalse(world.Door.IsLocked, "the door is unlocked");
            Assert.AreEqual(1f, world.Door.OpenAmount, "and open");
            Assert.IsFalse(world.GateTarget.activeSelf, "the gate is switched off");
            Assert.AreEqual(20f, world.Player.Pitch, 0.01f, "looking down as they were");
            Assert.AreEqual(90f, world.Player.Yaw, 0.01f, "facing +x as they were");
        }

        [UnityTest]
        public IEnumerator SaveAndLoadUseASlot()
        {
            World world = CreateWorld();
            yield return null;
            Assert.IsFalse(SaveSystem.HasSave(Slot), "nothing saved yet");
            Assert.IsFalse(SaveSystem.Load(Slot), "an empty slot doesn't load");

            SaveSystem.Save(Slot);
            Assert.IsTrue(SaveSystem.HasSave(Slot), "saved");
            world.Door.SetOpen(true, instant: true);
            Assert.IsTrue(SaveSystem.Load(Slot), "loaded");
            Assert.AreEqual(0f, world.Door.OpenAmount, "the door is shut as saved");

            SaveSystem.Delete(Slot);
            Assert.IsFalse(SaveSystem.HasSave(Slot), "deleted");
        }

        [Test]
        public void RestoreRefusesAnotherScenesSave()
        {
            string other = "{\"version\":1,\"scene\":\"Some Other Scene\",\"entries\":[]}";
            Assert.AreEqual(-1, SaveSystem.Restore(SceneManager.GetActiveScene(), other));
            Assert.AreEqual(-1, SaveSystem.Restore(SceneManager.GetActiveScene(), "not a save"));
        }
    }
}
