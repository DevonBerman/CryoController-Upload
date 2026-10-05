using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace CryoController.Tests
{
    public class InspectionTests
    {
        private TestRig rig;
        private FirstPersonController player;
        private PlayerCarry carry;

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

        private void CreateCarrier()
        {
            rig.CreateGround();
            PlayerCarry added = null;
            player = rig.CreatePlayer(Vector3.zero, go =>
            {
                go.AddComponent<PlayerInteractor>();
                added = go.AddComponent<PlayerCarry>();
            });
            carry = added;
        }

        private Carryable CreateBox(bool inspectable = true)
        {
            GameObject go = rig.Track(new GameObject("Box"));
            go.SetActive(false);
            go.transform.position = new Vector3(0f, 0.2f, 1.5f);
            go.AddComponent<BoxCollider>().size = Vector3.one * 0.4f;
            go.AddComponent<Rigidbody>().mass = 1f;
            Carryable box = go.AddComponent<Carryable>();
            TestRig.SetField(box, "inspectable", inspectable);
            go.SetActive(true);
            Physics.SyncTransforms();
            return box;
        }

        private Transform Eyes => player.GetComponentInChildren<Camera>(true).transform;

        [UnityTest]
        public IEnumerator LookTurnsTheObjectWhileThePlayerHoldsStill()
        {
            CreateCarrier();
            Carryable box = CreateBox();
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(carry.PickUp(box), "picked up");
            yield return new WaitForSeconds(0.5f);

            int started = 0;
            carry.InspectionStarted += item => started++;
            yield return rig.Press(GamepadButton.DpadUp);
            Assert.IsTrue(carry.IsInspecting, "Inspect starts an inspection");
            Assert.AreEqual(1, started, "InspectionStarted was raised");
            Assert.IsFalse(player.MoveAndLookEnabled, "the player holds still");

            float yaw = player.transform.eulerAngles.y;
            float pitch = Eyes.localEulerAngles.x;
            Vector3 feet = player.transform.position;
            Quaternion before = box.transform.rotation;
            rig.SetLook(new Vector2(1f, 0.5f));
            rig.SetMove(new Vector2(0f, 1f));
            yield return TestRig.WaitGameAndRealTime(0.5f);
            rig.SetLook(Vector2.zero);
            rig.SetMove(Vector2.zero);

            Assert.AreEqual(0f, Mathf.DeltaAngle(yaw, player.transform.eulerAngles.y), 0.01f, "the view does not turn");
            Assert.AreEqual(0f, Mathf.DeltaAngle(pitch, Eyes.localEulerAngles.x), 0.01f, "nor tilt");
            Assert.Less(Vector3.Distance(feet, player.transform.position), 0.05f, "the player does not walk");
            Assert.Greater(Quaternion.Angle(before, box.transform.rotation), 20f, "the box turns instead");

            yield return rig.Press(GamepadButton.DpadUp);
            Assert.IsFalse(carry.IsInspecting, "Inspect again ends it");
            Assert.IsTrue(carry.IsCarrying, "the box is still carried");
            Assert.IsTrue(player.MoveAndLookEnabled, "the player can move again");

            rig.SetLook(new Vector2(1f, 0f));
            yield return TestRig.WaitGameAndRealTime(0.2f);
            rig.SetLook(Vector2.zero);
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(yaw, player.transform.eulerAngles.y)), 5f, "the view turns again");
        }

        [UnityTest]
        public IEnumerator HoldsTheObjectCloserWhileInspecting()
        {
            CreateCarrier();
            Carryable box = CreateBox();
            float radius = box.GetComponent<Collider>().bounds.extents.magnitude;
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(carry.PickUp(box), "picked up");
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(0.6f + radius, Vector3.Distance(Eyes.position, box.Body.worldCenterOfMass), 0.15f,
                            "carried at Hold Distance");

            Assert.IsTrue(carry.StartInspecting(), "started");
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(0.35f + radius, Vector3.Distance(Eyes.position, box.Body.worldCenterOfMass), 0.15f,
                            "inspected at Inspect Distance");

            carry.StopInspecting();
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(0.6f + radius, Vector3.Distance(Eyes.position, box.Body.worldCenterOfMass), 0.15f,
                            "back at Hold Distance");
        }

        [UnityTest]
        public IEnumerator InteractDropsButThrowWaits()
        {
            CreateCarrier();
            Carryable box = CreateBox();
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(carry.PickUp(box), "picked up");
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(carry.StartInspecting(), "started");

            yield return rig.Press(GamepadButton.West);   // Throw
            Assert.IsTrue(carry.IsCarrying, "nothing is thrown mid-inspection");
            Assert.IsTrue(carry.IsInspecting, "still inspecting");

            int ended = 0;
            carry.InspectionEnded += item => ended++;
            yield return rig.Press(GamepadButton.North);   // Interact
            Assert.IsFalse(carry.IsCarrying, "Interact puts it down");
            Assert.IsFalse(carry.IsInspecting, "which ends the inspection");
            Assert.AreEqual(1, ended, "InspectionEnded was raised");
            Assert.IsTrue(player.MoveAndLookEnabled, "the player can move again");
        }

        [UnityTest]
        public IEnumerator ACrouchedPlayerStaysCrouchedWhileInspecting()
        {
            CreateCarrier();
            Carryable box = CreateBox();
            rig.Hold(GamepadButton.East);   // Crouch, held
            yield return TestRig.WaitUntil(() => player.IsCrouching, 2f);
            Assert.IsTrue(player.IsCrouching, "crouched");
            Assert.IsTrue(carry.PickUp(box), "picked up");
            yield return new WaitForSeconds(0.3f);

            Assert.IsTrue(carry.StartInspecting(), "started");
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(player.IsCrouching, "still crouched while inspecting");

            carry.StopInspecting();
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(player.IsCrouching, "and after, with Crouch still held");

            rig.Hold(GamepadButton.East, false);
            yield return TestRig.WaitUntil(() => !player.IsCrouching, 2f);
            Assert.IsFalse(player.IsCrouching, "stands once Crouch is let go");
        }

        [UnityTest]
        public IEnumerator OnlyACarriedInspectableObjectCanBeInspected()
        {
            CreateCarrier();
            Carryable box = CreateBox(inspectable: false);
            yield return new WaitForSeconds(0.3f);

            yield return rig.Press(GamepadButton.DpadUp);
            Assert.IsFalse(carry.IsInspecting, "nothing to inspect with empty hands");
            Assert.IsTrue(player.MoveAndLookEnabled, "and the player still moves");

            Assert.IsTrue(carry.PickUp(box), "picked up");
            yield return new WaitForSeconds(0.2f);
            Assert.IsFalse(carry.CanInspect, "not with Inspectable off");
            yield return rig.Press(GamepadButton.DpadUp);
            Assert.IsFalse(carry.IsInspecting, "Inspect does nothing");
            Assert.IsFalse(carry.StartInspecting(), "nor does StartInspecting");
        }
    }
}
