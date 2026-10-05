using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace CryoController.Tests
{
    public class ReadableTests
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

        // A two-page note straight ahead of the player's eyes.
        private Readable CreateNote()
        {
            BoxCollider paper = rig.CreateBox("Note", new Vector3(0f, TestRig.EyeHeight, 1.5f), new Vector3(0.4f, 0.5f, 0.02f));
            Readable note = paper.gameObject.AddComponent<Readable>();
            note.SetText("Diary", "First page.\n---\nSecond page.");
            return note;
        }

        private FirstPersonController CreateReader(out PlayerReader reader)
        {
            PlayerReader added = null;
            FirstPersonController player = rig.CreatePlayer(Vector3.zero, go =>
            {
                go.AddComponent<PlayerInteractor>();
                added = go.AddComponent<PlayerReader>();
            });
            reader = added;
            return player;
        }

        [Test]
        public void SplitsPagesOnTheBreakLine()
        {
            var go = new GameObject("Note");
            try
            {
                Readable note = go.AddComponent<Readable>();
                note.SetText("T", "One\n---\nTwo\r\n  ---  \r\nThree\n---\n");
                CollectionAssert.AreEqual(new[] { "One", "Two", "Three" }, note.Pages);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [UnityTest]
        public IEnumerator InteractOpensTurnsAndClosesTheNote()
        {
            rig.CreateGround();
            Readable note = CreateNote();
            FirstPersonController player = CreateReader(out PlayerReader reader);
            PlayerInteractor interactor = player.GetComponent<PlayerInteractor>();
            int closed = 0;
            reader.Closed += n => closed++;
            yield return new WaitForSeconds(0.3f);

            Assert.AreEqual("Read", interactor.CurrentPrompt, "the prompt offers to read it");
            yield return rig.Press(GamepadButton.North);
            Assert.IsTrue(reader.IsReading, "open");
            Assert.AreEqual(0, reader.Page, "at the first page");
            Assert.IsTrue(note.HasBeenRead, "marked as read");
            Assert.IsFalse(player.MoveAndLookEnabled, "the player holds still while reading");
            Assert.IsNull(interactor.Current, "nothing is focused while reading");

            yield return rig.Press(GamepadButton.North);
            Assert.AreEqual(1, reader.Page, "Interact turns the page");
            yield return rig.Press(GamepadButton.North);
            Assert.IsFalse(reader.IsReading, "and closes it after the last page");
            Assert.AreEqual(1, closed, "Closed once");
            Assert.IsTrue(player.MoveAndLookEnabled, "the player can move again");

            yield return new WaitForSeconds(0.2f);
            Assert.IsFalse(reader.IsReading, "the press that closed it didn't open it again");
            Assert.IsNotNull(interactor.Current, "aiming at the note again once the button is up");
        }

        [UnityTest]
        public IEnumerator ClosesWhenTheNoteGoesAway()
        {
            rig.CreateGround();
            Readable note = CreateNote();
            FirstPersonController player = CreateReader(out PlayerReader reader);
            yield return new WaitForSeconds(0.3f);

            reader.Open(note);
            Assert.IsTrue(reader.IsReading, "open");
            note.gameObject.SetActive(false);
            yield return null;
            Assert.IsFalse(reader.IsReading, "closed with the note gone");
            Assert.IsTrue(player.MoveAndLookEnabled, "the player can move again");
        }
    }
}
