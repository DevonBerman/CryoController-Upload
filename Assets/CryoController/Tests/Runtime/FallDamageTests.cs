using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;

namespace CryoController.Tests
{
    public class FallDamageTests
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

        private FirstPersonController CreatePlayer(float height, out PlayerFallDamage fallDamage)
        {
            rig.CreateGround();
            PlayerFallDamage added = null;
            FirstPersonController player = rig.CreatePlayer(new Vector3(0f, height, 0f),
                                                            go => added = go.AddComponent<PlayerFallDamage>());
            fallDamage = added;
            return player;
        }

        [Test]
        public void DamageScalesFromSafeToLethalSpeed()
        {
            CreatePlayer(0f, out PlayerFallDamage fallDamage);   // defaults: safe 11 m/s, lethal 20 m/s, 100 damage

            Assert.AreEqual(0f, fallDamage.DamageFor(8f), "slower than safe");
            Assert.AreEqual(0f, fallDamage.DamageFor(11f), "exactly safe");
            Assert.AreEqual(50f, fallDamage.DamageFor(15.5f), 0.001f, "half way");
            Assert.AreEqual(100f, fallDamage.DamageFor(20f), 0.001f, "lethal");
            Assert.AreEqual(100f, fallDamage.DamageFor(40f), 0.001f, "capped");
        }

        [UnityTest]
        public IEnumerator ShortFallsDoNoHarm()
        {
            FirstPersonController player = CreatePlayer(2f, out PlayerFallDamage fallDamage);   // lands at about 9 m/s
            int hurts = 0;
            fallDamage.FallDamaged += (damage, speed) => hurts++;
            bool landed = false;
            player.Landed += speed => landed = true;

            yield return TestRig.WaitUntil(() => landed, 2f);
            yield return null;

            Assert.IsTrue(landed, "landed");
            Assert.AreEqual(0, hurts, "a 2 m drop does no harm");
        }

        [UnityTest]
        public IEnumerator LongFallsHurtInProportionAndShakeTheCamera()
        {
            FirstPersonController player = CreatePlayer(6f, out PlayerFallDamage fallDamage);   // lands at about 15.5 m/s
            float damage = 0f;
            float impact = 0f;
            int hurts = 0;
            fallDamage.FallDamaged += (amount, speed) =>
            {
                hurts++;
                damage = amount;
                impact = speed;
            };
            float wired = 0f;
            var onFallDamage = (UnityEvent<float>)typeof(PlayerFallDamage)
                .GetField("onFallDamage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(fallDamage);
            onFallDamage.AddListener(amount => wired = amount);

            yield return TestRig.WaitUntil(() => hurts > 0, 3f);

            Assert.AreEqual(1, hurts, "one harmful landing");
            Assert.AreEqual(fallDamage.DamageFor(impact), damage, 0.001f, "damage follows the impact speed");
            Assert.Greater(damage, 25f, "a 6 m drop hurts");
            Assert.Less(damage, 75f, "but is not lethal");
            Assert.AreEqual(damage, wired, 0.001f, "On Fall Damage gets the same damage");
            Assert.Greater(player.CameraShake, 0f, "the camera shakes");
        }

        [UnityTest]
        public IEnumerator VeryLongFallsDoMaxDamage()
        {
            CreatePlayer(14f, out PlayerFallDamage fallDamage);   // lands at about 23.7 m/s
            float damage = 0f;
            fallDamage.FallDamaged += (amount, speed) => damage = amount;

            yield return TestRig.WaitUntil(() => damage > 0f, 3f);

            Assert.AreEqual(100f, damage, 0.001f, "Max Damage");
        }
    }
}
