using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CryoController.EditorTests
{
    public class InputRebinderTests
    {
        private InputActionAsset asset;

        [SetUp]
        public void SetUp()
        {
            asset = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap map = asset.AddActionMap("Player");

            InputAction move = map.AddAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w", "Keyboard&Mouse")
                .With("Down", "<Keyboard>/s", "Keyboard&Mouse")
                .With("Left", "<Keyboard>/a", "Keyboard&Mouse")
                .With("Right", "<Keyboard>/d", "Keyboard&Mouse");
            move.AddCompositeBinding("2DVector")   // a second set (arrows) must not add rows
                .With("Up", "<Keyboard>/upArrow", "Keyboard&Mouse")
                .With("Down", "<Keyboard>/downArrow", "Keyboard&Mouse")
                .With("Left", "<Keyboard>/leftArrow", "Keyboard&Mouse")
                .With("Right", "<Keyboard>/rightArrow", "Keyboard&Mouse");
            move.AddBinding("<Gamepad>/leftStick", groups: "Gamepad");

            InputAction lean = map.AddAction("LeanLeft", InputActionType.Button);
            lean.AddBinding("<Keyboard>/q", groups: "Keyboard&Mouse");
            lean.AddBinding("<Gamepad>/leftShoulder", groups: "Gamepad");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(asset);
        }

        [Test]
        public void ListsButtonsAndCompositePartsOfOneScheme()
        {
            var rebinder = new InputRebinder();
            rebinder.Refresh(asset, new[] { "Move", "LeanLeft", "NotAnAction" }, InputRebinder.KeyboardScheme);

            CollectionAssert.AreEqual(new[] { "Move Up", "Move Down", "Move Left", "Move Right", "Lean Left" },
                                      rebinder.Rows.Select(r => r.Label).ToArray());
            Assert.IsFalse(string.IsNullOrEmpty(rebinder.Rows[4].Display), "the lean row shows its key");
        }

        [Test]
        public void LeavesSticksOut()
        {
            var rebinder = new InputRebinder();
            rebinder.Refresh(asset, new[] { "Move", "LeanLeft" }, InputRebinder.GamepadScheme);

            CollectionAssert.AreEqual(new[] { "Lean Left" }, rebinder.Rows.Select(r => r.Label).ToArray(),
                                      "a stick is not rebound by pressing something");
        }

        [Test]
        public void BindingDisplayPicksTheScheme()
        {
            InputAction lean = asset.FindAction("Player/LeanLeft");

            // The key's text depends on the keyboard layout (Q is A on AZERTY), so only the choice of scheme is checked.
            string keyboard = InputRebinder.BindingDisplay(lean, InputRebinder.KeyboardScheme);
            string gamepad = InputRebinder.BindingDisplay(lean, InputRebinder.GamepadScheme);
            Assert.IsFalse(string.IsNullOrEmpty(keyboard), "keyboard binding shown");
            Assert.IsFalse(string.IsNullOrEmpty(gamepad), "gamepad binding shown");
            Assert.AreNotEqual(keyboard, gamepad, "each scheme shows its own binding");
            Assert.AreEqual("", InputRebinder.BindingDisplay(null, InputRebinder.KeyboardScheme), "no action, no text");
        }

        [Test]
        public void NicifySplitsActionNames()
        {
            Assert.AreEqual("Lean Left", InputRebinder.Nicify("LeanLeft"));
            Assert.AreEqual("Move", InputRebinder.Nicify("Move"));
            Assert.AreEqual("HUD", InputRebinder.Nicify("HUD"));
        }

        [Test]
        public void BlankKeyNamesFallBackToTheControlsName()
        {
            // Some keyboard layouts (Linux) name the Space key " ".
            Assert.AreEqual("Space", InputRebinder.Readable(" ", "<Keyboard>/space"));
            Assert.AreEqual("F", InputRebinder.Readable("F", "<Keyboard>/f"), "a real name is kept");
            Assert.AreEqual("", InputRebinder.Readable("", null), "nothing to fall back to");
        }
    }
}
