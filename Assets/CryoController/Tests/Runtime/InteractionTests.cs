using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace CryoController.Tests
{
    public class InteractionTests
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

        [UnityTest]
        public IEnumerator HighlightsWhatThePlayerAimsAt()
        {
            rig.CreateGround();
            GameObject cube = rig.Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            cube.SetActive(false);
            cube.transform.position = new Vector3(0f, TestRig.EyeHeight, 1.5f);   // straight ahead of the eyes
            cube.transform.localScale = Vector3.one * 0.5f;
            cube.AddComponent<Interactable>();
            InteractableHighlight highlight = cube.AddComponent<InteractableHighlight>();
            cube.SetActive(true);
            Physics.SyncTransforms();
            Renderer renderer = cube.GetComponent<Renderer>();
            MaterialPropertyBlock probe = new MaterialPropertyBlock();
            PlayerInteractor interactor = null;
            FirstPersonController player = rig.CreatePlayer(Vector3.zero, go => interactor = go.AddComponent<PlayerInteractor>());

            yield return new WaitForSeconds(0.4f);
            Assert.IsNotNull(interactor.Current, "aiming at the cube");
            Assert.IsTrue(highlight.IsHighlighted, "highlighted");
            renderer.GetPropertyBlock(probe, 0);
            Assert.IsFalse(probe.isEmpty, "tinted");

            player.Teleport(Vector3.zero, 180f);   // turn away
            yield return TestRig.WaitGameAndRealTime(0.4f);   // the highlight fades out in real time
            Assert.IsNull(interactor.Current, "aiming at nothing");
            Assert.IsFalse(highlight.IsHighlighted, "no longer highlighted");
            renderer.GetPropertyBlock(probe, 0);
            Assert.IsTrue(probe.isEmpty, "back to its own colour");
        }

        [UnityTest]
        public IEnumerator HoldInteractableNeedsTheHold()
        {
            rig.CreateGround();
            BoxCollider lever = rig.CreateBox("Lever", new Vector3(0f, TestRig.EyeHeight, 1.5f), Vector3.one * 0.5f);
            Interactable interactable = lever.gameObject.AddComponent<Interactable>();
            TestRig.SetField(interactable, "holdDuration", 0.5f);
            PlayerInteractor interactor = null;
            rig.CreatePlayer(Vector3.zero, go => interactor = go.AddComponent<PlayerInteractor>());
            int used = 0;
            interactor.Interacted += target => used++;
            yield return new WaitForSeconds(0.3f);

            yield return rig.Press(GamepadButton.North);
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(0, used, "a tap doesn't use it");

            rig.Hold(GamepadButton.North);
            yield return new WaitForSeconds(0.25f);
            Assert.That(interactor.HoldProgress, Is.InRange(0.3f, 0.7f), "half way through the hold");
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(1, used, "held long enough, it is used");
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(1, used, "once per hold");
            rig.Hold(GamepadButton.North, false);
        }

        [UnityTest]
        public IEnumerator SmallObjectUnderTheCrosshairBeatsABiggerOneBeside()
        {
            rig.CreateGround();
            // The drawer stands just right of the line of sight, nearer than the key: the sweep would touch it first,
            // but the ray goes past it to the key.
            BoxCollider drawer = rig.CreateBox("Drawer", new Vector3(0.25f, TestRig.EyeHeight, 1.0f), new Vector3(0.3f, 0.3f, 0.3f));
            drawer.gameObject.AddComponent<Interactable>();
            // Thin across the view but tall, so where the eye settles doesn't matter.
            BoxCollider key = rig.CreateBox("Key", new Vector3(0f, TestRig.EyeHeight, 1.6f), new Vector3(0.08f, 0.6f, 0.08f));
            Interactable keyInteractable = key.gameObject.AddComponent<Interactable>();
            PlayerInteractor interactor = null;
            rig.CreatePlayer(Vector3.zero, go => interactor = go.AddComponent<PlayerInteractor>());

            yield return new WaitForSeconds(0.3f);
            Assert.AreSame(keyInteractable, interactor.Current, "the key under the crosshair");
        }

        [UnityTest]
        public IEnumerator HighlightKeepsOtherPropertyBlocks()
        {
            GameObject cube = rig.Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            InteractableHighlight highlight = cube.AddComponent<InteractableHighlight>();
            Renderer renderer = cube.GetComponent<Renderer>();
            int colorId = Shader.PropertyToID(renderer.sharedMaterial.HasProperty("_BaseColor") ? "_BaseColor" : "_Color");
            Color own = new Color(0.2f, 0.4f, 0.6f, 0.5f);
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            block.SetFloat("_CryoControllerTest", 3f);   // another script's block on the slot
            block.SetColor(colorId, own);
            renderer.SetPropertyBlock(block, 0);

            highlight.SetHighlighted(true);
            yield return TestRig.WaitGameAndRealTime(0.3f);   // the highlight fades in real time
            MaterialPropertyBlock probe = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(probe, 0);
            Assert.AreEqual(3f, probe.GetFloat("_CryoControllerTest"), "the other script's value stays while lit");
            Color tinted = probe.GetColor(colorId);
            Assert.Greater(Distance(own, tinted), 0.02f, "tinted");
            Assert.AreEqual(own.a, tinted.a, 0.001f, "the alpha is kept");

            highlight.SetHighlighted(false);
            yield return TestRig.WaitGameAndRealTime(0.3f);
            renderer.GetPropertyBlock(probe, 0);
            Assert.AreEqual(3f, probe.GetFloat("_CryoControllerTest"), "the other script's value stays afterwards");
            Assert.Less(Distance(own, probe.GetColor(colorId)), 0.01f, "its colour is put back");
        }

        // Colours in a property block may go through a colour-space conversion, so they are compared loosely.
        private static float Distance(Color a, Color b)
        {
            return Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a));
        }
    }
}
