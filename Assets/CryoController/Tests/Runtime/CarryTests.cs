using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CryoController.Tests
{
    public class CarryTests
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

        private FirstPersonController CreateCarrier(Vector3 feet, out PlayerCarry carry)
        {
            PlayerCarry added = null;
            FirstPersonController player = rig.CreatePlayer(feet, go =>
            {
                go.AddComponent<PlayerInteractor>();
                added = go.AddComponent<PlayerCarry>();
            });
            carry = added;
            return player;
        }

        private Carryable CreateBox(Vector3 center, Vector3 size, float mass, int layer = 0, bool twoColliders = false)
        {
            GameObject go = rig.Track(new GameObject("Box"));
            go.SetActive(false);
            go.layer = layer;
            go.transform.position = center;
            go.AddComponent<BoxCollider>().size = size;
            if (twoColliders)
            {
                BoxCollider second = go.AddComponent<BoxCollider>();
                second.size = size * 0.5f;
                second.center = new Vector3(0f, size.y * 0.25f, 0f);
            }
            go.AddComponent<Rigidbody>().mass = mass;
            Carryable box = go.AddComponent<Carryable>();
            go.SetActive(true);
            Physics.SyncTransforms();
            return box;
        }

        private static Transform Eyes(FirstPersonController player)
        {
            return player.GetComponentInChildren<Camera>(true).transform;
        }

        [UnityTest]
        public IEnumerator ATapPicksUpAndTheNextTapDrops()
        {
            rig.CreateGround();
            rig.CreateBox("Pedestal", new Vector3(0f, 0.7f, 1.4f), new Vector3(0.6f, 1.4f, 0.6f));
            FirstPersonController player = CreateCarrier(Vector3.zero, out PlayerCarry carry);
            CreateBox(new Vector3(0f, TestRig.EyeHeight, 1.4f), Vector3.one * 0.4f, 1f);
            yield return new WaitForSeconds(0.3f);

            yield return rig.Press(UnityEngine.InputSystem.LowLevel.GamepadButton.North);
            Assert.IsTrue(carry.IsCarrying, "one tap picks it up");
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(carry.IsCarrying, "letting go of the button doesn't drop it");

            yield return rig.Press(UnityEngine.InputSystem.LowLevel.GamepadButton.North);
            Assert.IsFalse(carry.IsCarrying, "the next tap drops it");
        }

        [UnityTest]
        public IEnumerator HoldsInFrontAndRestoresEverythingOnDrop()
        {
            rig.CreateGround();
            FirstPersonController player = CreateCarrier(Vector3.zero, out PlayerCarry carry);
            int water = LayerMask.NameToLayer("Water");
            Carryable box = CreateBox(new Vector3(0f, 0.2f, 1.5f), Vector3.one * 0.4f, 1f, water);
            Collider collider = box.GetComponent<Collider>();
            CharacterController capsule = player.GetComponent<CharacterController>();
            yield return new WaitForSeconds(0.3f);

            Assert.IsTrue(carry.PickUp(box), "picked up");
            Assert.IsTrue(carry.IsCarrying, "carrying");
            Assert.IsTrue(box.IsCarried, "the box knows it is carried");
            Assert.AreEqual(PlayerCarry.CarriedLayer, box.gameObject.layer, "on Ignore Raycast while carried");
            Assert.IsFalse(box.Body.useGravity, "no gravity while carried");
            Assert.IsTrue(Physics.GetIgnoreCollision(capsule, collider), "no collision with the player while carried");

            yield return new WaitForSeconds(0.6f);
            Transform eyes = Eyes(player);
            float distance = 0.6f + collider.bounds.extents.magnitude;   // Hold Distance plus the box's half diagonal
            Vector3 holdPoint = eyes.position + eyes.forward * distance;
            Assert.Less(Vector3.Distance(box.Body.worldCenterOfMass, holdPoint), 0.15f, "floats at the hold point");

            carry.Drop();
            Assert.IsFalse(carry.IsCarrying, "let go");
            Assert.IsFalse(box.IsCarried, "the box knows it was let go");
            Assert.AreEqual(water, box.gameObject.layer, "layer restored");
            Assert.IsTrue(box.Body.useGravity, "gravity restored");
            Assert.IsFalse(Physics.GetIgnoreCollision(capsule, collider), "collides with the player again");
        }

        [UnityTest]
        public IEnumerator LetsGoWhenPulledFarFromTheHoldPoint()
        {
            rig.CreateGround();
            FirstPersonController player = CreateCarrier(Vector3.zero, out PlayerCarry carry);
            Carryable box = CreateBox(new Vector3(0f, 0.2f, 1.5f), Vector3.one * 0.4f, 1f);
            yield return new WaitForSeconds(0.3f);

            Assert.IsTrue(carry.PickUp(box), "picked up");
            yield return new WaitForSeconds(0.6f);
            Assert.IsTrue(carry.IsCarrying, "still held after lifting it off the floor, further than Break Distance away");

            player.Teleport(new Vector3(0f, 0f, -6f), 0f);   // the hold point jumps 6 m in one step
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(carry.IsCarrying, "let go once far from the hold point");
            Assert.IsFalse(box.IsCarried, "the box knows it was let go");
        }

        [UnityTest]
        public IEnumerator AnObjectWithTwoCollidersKeepsItsLayer()
        {
            rig.CreateGround();
            CreateCarrier(Vector3.zero, out PlayerCarry carry);
            int water = LayerMask.NameToLayer("Water");
            Carryable box = CreateBox(new Vector3(0f, 0.2f, 1.5f), Vector3.one * 0.4f, 1f, water, twoColliders: true);
            yield return new WaitForSeconds(0.3f);

            Assert.IsTrue(carry.PickUp(box), "picked up");
            yield return new WaitForFixedUpdate();
            carry.Drop();

            Assert.AreEqual(water, box.gameObject.layer, "both colliders share the object, and its layer comes back");
        }

        [UnityTest]
        public IEnumerator DroppedInsideThePlayerCollidesAgainOnceClear()
        {
            rig.CreateGround();
            FirstPersonController player = CreateCarrier(Vector3.zero, out PlayerCarry carry);
            Carryable box = CreateBox(new Vector3(0f, 0.2f, 1.5f), Vector3.one * 0.4f, 1f);
            Collider collider = box.GetComponent<Collider>();
            CharacterController capsule = player.GetComponent<CharacterController>();
            yield return new WaitForSeconds(0.3f);

            Assert.IsTrue(carry.PickUp(box), "picked up");
            yield return new WaitForFixedUpdate();
            box.transform.position = player.transform.position + Vector3.up * 0.9f;   // inside the capsule
            Physics.SyncTransforms();
            carry.Drop();
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(Physics.GetIgnoreCollision(capsule, collider), "still ignored while inside the player");

            box.transform.position = new Vector3(5f, 0.2f, 0f);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(Physics.GetIgnoreCollision(capsule, collider), "collides again once clear of the player");
        }

        [UnityTest]
        public IEnumerator ThrowSpeedFallsWithMass()
        {
            rig.CreateGround();
            FirstPersonController player = CreateCarrier(Vector3.zero, out PlayerCarry carry);
            Carryable light = CreateBox(new Vector3(-1f, 0.2f, 1.5f), Vector3.one * 0.4f, 1f);
            Carryable heavy = CreateBox(new Vector3(1f, 0.2f, 1.5f), Vector3.one * 0.4f, 4f);
            Transform eyes = Eyes(player);
            yield return new WaitForSeconds(0.3f);

            Assert.IsTrue(carry.PickUp(light), "picked up the light box");
            yield return new WaitForSeconds(0.5f);
            carry.Throw();
            Assert.AreEqual(12f, Vector3.Dot(light.Body.linearVelocity, eyes.forward), 1.5f, "1 kg leaves at Throw Impulse / mass");

            yield return new WaitForSeconds(0.2f);
            Assert.IsTrue(carry.PickUp(heavy), "picked up the heavy box");
            yield return new WaitForSeconds(0.5f);
            carry.Throw();
            Assert.AreEqual(3f, Vector3.Dot(heavy.Body.linearVelocity, eyes.forward), 1.5f, "4 kg leaves a quarter as fast");
        }

        [UnityTest]
        public IEnumerator RefusesWhatIsTooHeavyOrUnderfoot()
        {
            rig.CreateGround();
            Carryable underfoot = CreateBox(new Vector3(0f, 0.25f, 0f), new Vector3(2f, 0.5f, 2f), 5f);
            Carryable tooHeavy = CreateBox(new Vector3(3f, 0.5f, 0f), Vector3.one, 30f);
            FirstPersonController player = CreateCarrier(new Vector3(0f, 0.55f, 0f), out PlayerCarry carry);
            yield return new WaitForSeconds(0.5f);

            Assert.AreEqual(underfoot.GetComponent<Collider>(), player.Ground, "standing on the box");
            Assert.IsFalse(carry.CanPickUp(underfoot), "not what the player stands on");
            Assert.IsFalse(carry.CanPickUp(tooHeavy), "not heavier than Max Mass");
            Assert.IsFalse(carry.PickUp(tooHeavy), "PickUp refuses too");
            Assert.IsFalse(carry.IsCarrying, "carrying nothing");
        }
    }
}
