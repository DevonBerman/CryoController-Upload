using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CryoController.Tests
{
    public class RumbleTests
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

        private FirstPersonController CreatePlayer(out PlayerRumble rumble)
        {
            rig.CreateGround();
            PlayerRumble added = null;
            FirstPersonController player = rig.CreatePlayer(Vector3.zero, go => added = go.AddComponent<PlayerRumble>());
            rumble = added;
            return player;
        }

        // Touches the stick so the gamepad is the device in use.
        private IEnumerator UseTheGamepad()
        {
            rig.SetMove(new Vector2(0f, 0.6f));
            yield return null;
            yield return null;
            rig.SetMove(Vector2.zero);
            yield return null;
        }

        // Samples for this much game time (the fall) and real time (rumble pulses are timed in real time).
        private static IEnumerator Strongest(PlayerRumble rumble, float seconds, System.Action<float> result)
        {
            float strongest = 0f;
            float gameEnd = Time.time + seconds;
            float realEnd = Time.realtimeSinceStartup + seconds;
            while (Time.time < gameEnd || Time.realtimeSinceStartup < realEnd)
            {
                strongest = Mathf.Max(strongest, rumble.LowFrequency);
                yield return null;
            }
            result(strongest);
        }

        [UnityTest]
        public IEnumerator RumblesOnAHardLandingThenStops()
        {
            FirstPersonController player = CreatePlayer(out PlayerRumble rumble);
            yield return new WaitForSeconds(0.3f);
            yield return UseTheGamepad();
            Assert.IsTrue(player.UsingGamepad, "the gamepad is in use");

            player.Teleport(new Vector3(0f, 2f, 0f), 0f);   // a 2 m fall lands at about 9 m/s
            float strongest = 0f;
            yield return Strongest(rumble, 1f, value => strongest = value);

            Assert.Greater(strongest, 0.1f, "rumbled on landing");
            Assert.AreEqual(0f, rumble.LowFrequency, 0.001f, "stopped afterwards");
        }

        [UnityTest]
        public IEnumerator StaysStillWhileInputIsOff()
        {
            FirstPersonController player = CreatePlayer(out PlayerRumble rumble);
            yield return new WaitForSeconds(0.3f);
            yield return UseTheGamepad();

            player.InputEnabled = false;   // a menu or cutscene
            player.Teleport(new Vector3(0f, 2f, 0f), 0f);
            float strongest = 0f;
            yield return Strongest(rumble, 1f, value => strongest = value);

            Assert.AreEqual(0f, strongest, 0.001f, "no rumble while input is off");
        }
    }
}
