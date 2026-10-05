using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CryoController.EditorTests
{
    /// <summary>Checks <see cref="MovementProfile"/> against the controller, and the profiles that ship with the package.</summary>
    public class MovementProfileAssetTests
    {
        private const string PackageFolderGuid = "c6b8b56f9d0c40ee99ba3c5937eb9243";
        private const string PlayerPrefabGuid = "829e985716bc4bb288ddcee1fbe9a05e";
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly string[] ShippedProfiles = { "Default", "Realistic", "Arcade", "Tactical" };

        private static List<FieldInfo> Settings()
        {
            var settings = new List<FieldInfo>();
            foreach (FieldInfo field in typeof(MovementProfile).GetFields(InstanceFields))
            {
                if (field.IsDefined(typeof(SerializeField), false))
                {
                    settings.Add(field);
                }
            }
            return settings;
        }

        private static Dictionary<string, MovementProfile> LoadShipped()
        {
            string package = AssetDatabase.GUIDToAssetPath(PackageFolderGuid);
            var profiles = new Dictionary<string, MovementProfile>();
            foreach (string guid in AssetDatabase.FindAssets("t:MovementProfile", new[] { package }))
            {
                MovementProfile profile = AssetDatabase.LoadAssetAtPath<MovementProfile>(AssetDatabase.GUIDToAssetPath(guid));
                profiles[profile.name] = profile;
            }
            return profiles;
        }

        [Test]
        public void EveryProfileSettingIsAControllerSetting()
        {
            foreach (FieldInfo setting in Settings())
            {
                FieldInfo field = typeof(FirstPersonController).GetField(setting.Name, InstanceFields);
                Assert.IsNotNull(field, $"FirstPersonController has no {setting.Name} setting");
                Assert.AreEqual(setting.FieldType, field.FieldType, $"{setting.Name} has the same type on both");
                Assert.IsTrue(field.IsDefined(typeof(SerializeField), false), $"{setting.Name} is serialized on the controller");
            }
        }

        [Test]
        public void ShipsDefaultRealisticArcadeAndTactical()
        {
            CollectionAssert.AreEquivalent(ShippedProfiles, LoadShipped().Keys);
        }

        [Test]
        public void ShippedProfilesAreSensible()
        {
            foreach (MovementProfile profile in LoadShipped().Values)
            {
                string name = profile.name;
                Assert.Greater(profile.WalkSpeed, 0f, $"{name}: walks");
                Assert.GreaterOrEqual(profile.SprintSpeed, profile.WalkSpeed, $"{name}: sprinting is not slower than walking");
                Assert.Greater(profile.CrouchSpeed, 0f, $"{name}: moves while crouched");
                Assert.LessOrEqual(profile.CrouchSpeed, profile.WalkSpeed, $"{name}: crouching is not faster than walking");
                Assert.Greater(profile.JumpHeight, 0f, $"{name}: jumps");
                Assert.Greater(profile.Gravity, 0f, $"{name}: falls");
                Assert.LessOrEqual(profile.AutoMantleHeight, profile.MantleMaxHeight, $"{name}: walk-in climbs are within reach");
                Assert.Greater(profile.ZoomFovScale, 0f, $"{name}: zoom keeps a field of view");
                Assert.LessOrEqual(profile.ZoomFovScale, 1f, $"{name}: zoom narrows the view");
            }
        }

        [Test]
        public void DefaultProfileMatchesThePlayerPrefab()
        {
            MovementProfile shipped = LoadShipped()["Default"];
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(PlayerPrefabGuid));
            MovementProfile fromPrefab = MovementProfile.CreateFrom(prefab.GetComponent<FirstPersonController>());
            try
            {
                foreach (FieldInfo setting in Settings())
                {
                    Assert.AreEqual(setting.GetValue(fromPrefab), setting.GetValue(shipped), $"Default {setting.Name}");
                }
            }
            finally
            {
                Object.DestroyImmediate(fromPrefab);
            }
        }
    }
}
