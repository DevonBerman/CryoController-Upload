using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CryoController.Tests
{
    public class PauseMenuTests
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

        private FirstPersonController CreatePlayerWithMenu(out PauseMenu menu)
        {
            rig.CreateGround();
            PauseMenu added = null;
            FirstPersonController player = rig.CreatePlayer(Vector3.zero, go => added = go.AddComponent<PauseMenu>());
            menu = added;
            return player;
        }

        [UnityTest]
        public IEnumerator PauseFreezesAndResumeRestores()
        {
            FirstPersonController player = CreatePlayerWithMenu(out PauseMenu menu);
            yield return null;

            menu.Pause();
            Assert.IsTrue(menu.IsPaused, "paused");
            Assert.AreEqual(0f, Time.timeScale, "time stopped");
            Assert.IsFalse(player.InputEnabled, "player input off");

            menu.Resume();
            Assert.IsFalse(menu.IsPaused, "resumed");
            Assert.AreEqual(1f, Time.timeScale, "time running");
            Assert.IsTrue(player.InputEnabled, "player input on");
        }

        [UnityTest]
        public IEnumerator ResumeLeavesInputOffWhenItWasOff()
        {
            FirstPersonController player = CreatePlayerWithMenu(out PauseMenu menu);
            yield return null;

            player.InputEnabled = false;   // a cutscene has the player
            menu.Pause();
            menu.Resume();

            Assert.IsFalse(player.InputEnabled, "the cutscene still has the player");
        }
    }
}
