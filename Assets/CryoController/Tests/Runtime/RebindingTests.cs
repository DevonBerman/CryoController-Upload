using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace CryoController.Tests
{
    public class RebindingTests
    {
        private const string BindingsKey = PlayerPreferences.BindingsKey;

        private TestRig rig;
        private InputRebinder rebinder;
        private bool hadSavedBindings;
        private string savedBindings;

        [SetUp]
        public void SetUp()
        {
            // Everything that can fail comes first: TearDown only runs when SetUp finishes, and it is what puts the
            // saved bindings back.
            rig = new TestRig();
            rebinder = new InputRebinder();

            // The player's real saved bindings are put aside for the test and put back afterwards.
            hadSavedBindings = PlayerPrefs.HasKey(BindingsKey);
            savedBindings = PlayerPrefs.GetString(BindingsKey);
            PlayerPrefs.DeleteKey(BindingsKey);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                rebinder?.Cancel();   // a rebind a failed test left listening must not finish in a later test
                rig?.Dispose();
            }
            finally
            {
                if (hadSavedBindings)
                {
                    PlayerPrefs.SetString(BindingsKey, savedBindings);
                }
                else
                {
                    PlayerPrefs.DeleteKey(BindingsKey);
                }
                PlayerPrefs.Save();
            }
        }

        private FirstPersonController CreatePlayer(out PlayerPreferences preferences)
        {
            rig.CreateGround();
            PlayerPreferences added = null;
            FirstPersonController player = rig.CreatePlayer(Vector3.zero, go => added = go.AddComponent<PlayerPreferences>());
            preferences = added;
            return player;
        }

        private InputRebinder.Row JumpRow(FirstPersonController player)
        {
            rebinder.Refresh(player.Actions, new[] { "Jump" }, InputRebinder.GamepadScheme);
            Assert.AreEqual(1, rebinder.Rows.Count, "Jump is listed");
            return rebinder.Rows[0];
        }

        private static string Path(InputRebinder.Row row)
        {
            return row.Action.bindings[row.BindingIndex].effectivePath;
        }

        [UnityTest]
        public IEnumerator RebindsFromAPressAndSurvivesSaveAndLoad()
        {
            FirstPersonController player = CreatePlayer(out PlayerPreferences preferences);
            yield return null;   // PlayerPreferences.Start
            InputRebinder.Row row = JumpRow(player);
            string displayBefore = row.Display;
            bool finished = false;

            rebinder.StartRebind(row, InputRebinder.GamepadScheme, () => finished = true);
            yield return null;
            rig.Hold(GamepadButton.RightStick);
            yield return TestRig.WaitUntilRealTime(() => finished, 2f);   // rebinding waits in real time
            rig.Hold(GamepadButton.RightStick, false);

            Assert.IsTrue(finished, "the rebind finished");
            string rebound = Path(row);
            StringAssert.EndsWith("rightStickPress", rebound, "Jump is now the right stick press");
            Assert.AreNotEqual(displayBefore, row.Display, "the row shows the new control");

            preferences.SaveBindings();
            player.Actions.RemoveAllBindingOverrides();
            StringAssert.EndsWith("buttonSouth", Path(row), "back to the default without the override");
            preferences.LoadBindings();
            Assert.AreEqual(rebound, Path(row), "the override comes back from PlayerPrefs");
        }

        [UnityTest]
        public IEnumerator SwapsAControlAnotherActionAlreadyUses()
        {
            FirstPersonController player = CreatePlayer(out _);
            yield return null;
            rebinder.Refresh(player.Actions, new[] { "Jump", "Crouch" }, InputRebinder.GamepadScheme);
            Assert.AreEqual(2, rebinder.Rows.Count, "Jump and Crouch are listed");
            InputRebinder.Row jump = rebinder.Rows[0];
            InputRebinder.Row crouch = rebinder.Rows[1];
            string jumpBefore = Path(jump);
            string crouchDisplayBefore = crouch.Display;
            bool finished = false;

            rebinder.StartRebind(jump, InputRebinder.GamepadScheme, () => finished = true);
            yield return null;
            rig.Hold(GamepadButton.East);   // Crouch's button
            yield return TestRig.WaitUntilRealTime(() => finished, 2f);   // rebinding waits in real time
            rig.Hold(GamepadButton.East, false);

            Assert.IsTrue(finished, "the rebind finished");
            StringAssert.EndsWith("buttonEast", Path(jump), "Jump is now on Crouch's button");
            Assert.AreEqual(jumpBefore, Path(crouch), "Crouch took Jump's old button");
            Assert.AreNotEqual(crouchDisplayBefore, crouch.Display, "the Crouch row shows its new button");
        }

        [UnityTest]
        public IEnumerator GamepadStartCancelsARebind()
        {
            FirstPersonController player = CreatePlayer(out PlayerPreferences preferences);
            yield return null;
            InputRebinder.Row row = JumpRow(player);
            bool finished = false;

            rebinder.StartRebind(row, InputRebinder.GamepadScheme, () => finished = true);
            yield return null;
            rig.Hold(GamepadButton.Start);
            yield return TestRig.WaitUntilRealTime(() => finished, 2f);   // rebinding waits in real time
            rig.Hold(GamepadButton.Start, false);

            Assert.IsTrue(finished, "cancelled");
            Assert.IsFalse(rebinder.IsRebinding, "no longer listening");
            StringAssert.EndsWith("buttonSouth", Path(row), "the binding is unchanged");
        }
    }
}
