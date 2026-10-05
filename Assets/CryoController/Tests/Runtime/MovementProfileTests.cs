using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace CryoController.Tests
{
    public class MovementProfileTests
    {
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

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

        private MovementProfile CreateProfile(float jumpHeight)
        {
            MovementProfile profile = rig.Track(ScriptableObject.CreateInstance<MovementProfile>());
            TestRig.SetField(profile, "jumpHeight", jumpHeight);
            return profile;
        }

        // How high the next jump rises above where it starts.
        private IEnumerator MeasureJump(FirstPersonController player, System.Action<float> result)
        {
            float start = player.transform.position.y;
            float peak = start;
            yield return rig.Press(GamepadButton.South);
            float end = Time.time + 1.5f;
            while (Time.time < end)
            {
                peak = Mathf.Max(peak, player.transform.position.y);
                yield return null;
            }
            Assert.IsTrue(player.IsGrounded, "landed again");
            result(peak - start);
        }

        [UnityTest]
        public IEnumerator AppliesTheProfileOnStart()
        {
            rig.CreateGround();
            MovementProfile high = CreateProfile(2f);
            FirstPersonController player = rig.CreatePlayer(Vector3.zero, go =>
                TestRig.SetField(go.GetComponent<FirstPersonController>(), "profile", high));
            yield return new WaitForSeconds(0.3f);

            float height = 0f;
            yield return MeasureJump(player, value => height = value);

            Assert.AreSame(high, player.Profile);
            Assert.AreEqual(2f, height, 0.1f, "jumps as high as the profile says");
        }

        [UnityTest]
        public IEnumerator SwapsProfilesAtRuntimeAndBack()
        {
            rig.CreateGround();
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            yield return new WaitForSeconds(0.3f);
            float height = 0f;

            yield return MeasureJump(player, value => height = value);
            Assert.AreEqual(1.2f, height, 0.1f, "the authored Jump Height");

            player.Profile = CreateProfile(2f);
            yield return MeasureJump(player, value => height = value);
            Assert.AreEqual(2f, height, 0.1f, "the profile's");

            player.Profile = null;
            yield return MeasureJump(player, value => height = value);
            Assert.AreEqual(1.2f, height, 0.1f, "the authored one again");
        }

        [Test]
        public void EverySettingGoesBothWays()
        {
            rig.CreateGround();
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            List<FieldInfo> settings = ProfileSettings();
            var authored = new Dictionary<string, object>();
            foreach (FieldInfo setting in settings)
            {
                authored[setting.Name] = ControllerField(setting.Name).GetValue(player);
            }

            // A profile whose every value differs from the controller's.
            MovementProfile profile = rig.Track(ScriptableObject.CreateInstance<MovementProfile>());
            for (int i = 0; i < settings.Count; i++)
            {
                settings[i].SetValue(profile, Distinct(settings[i], i, authored[settings[i].Name]));
            }

            player.Profile = profile;
            foreach (FieldInfo setting in settings)
            {
                Assert.AreEqual(setting.GetValue(profile), ControllerField(setting.Name).GetValue(player),
                                $"{setting.Name} is applied from the profile");
            }

            MovementProfile copy = rig.Track(MovementProfile.CreateFrom(player));
            foreach (FieldInfo setting in settings)
            {
                Assert.AreEqual(setting.GetValue(profile), setting.GetValue(copy), $"{setting.Name} is read into a new profile");
            }

            player.Profile = null;
            foreach (FieldInfo setting in settings)
            {
                Assert.AreEqual(authored[setting.Name], ControllerField(setting.Name).GetValue(player),
                                $"{setting.Name} is back to the authored value");
            }
        }

        private static List<FieldInfo> ProfileSettings()
        {
            var settings = new List<FieldInfo>();
            foreach (FieldInfo field in typeof(MovementProfile).GetFields(InstanceFields))
            {
                if (field.IsDefined(typeof(SerializeField), false))
                {
                    settings.Add(field);
                }
            }
            Assert.IsNotEmpty(settings);
            return settings;
        }

        private static FieldInfo ControllerField(string name)
        {
            FieldInfo field = typeof(FirstPersonController).GetField(name, InstanceFields);
            Assert.IsNotNull(field, $"FirstPersonController has a {name} setting");
            return field;
        }

        private static object Distinct(FieldInfo setting, int index, object current)
        {
            if (setting.FieldType == typeof(bool))
            {
                return !(bool)current;
            }
            if (setting.FieldType == typeof(int))
            {
                return (int)current + index + 1;
            }
            return (float)current + 0.5f + index * 0.25f;
        }
    }
}
