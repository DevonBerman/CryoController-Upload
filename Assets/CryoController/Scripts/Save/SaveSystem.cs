using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CryoController
{
    /// <summary>
    /// Saves and loads the state of a scene: every <see cref="ISaveable"/> component in it, switched-off ones included.
    /// Each is kept under a key made from where its object is in the hierarchy (or its <see cref="SaveId"/>) and its
    /// type, so a save made in one session loads into the same scene in another. <see cref="Save"/> and
    /// <see cref="Load"/> use a file per slot in Application.persistentDataPath (IndexedDB in a web build);
    /// <see cref="Capture"/> and <see cref="Restore"/> work with the text, for a save system of your own. Loading puts
    /// back the state of objects that are in the scene: load the scene first, then the save.
    /// </summary>
    public static class SaveSystem
    {
        [Serializable]
        private class Entry
        {
            public string key;
            public string state;
        }

        [Serializable]
        private class SaveFile
        {
            public int version = 1;
            public string scene;
            public List<Entry> entries = new List<Entry>();
        }

        /// <summary>Raised after a save is written, with its slot.</summary>
        public static event Action<string> Saved;

        /// <summary>Raised after a save is loaded, with its slot.</summary>
        public static event Action<string> Loaded;

        // With domain reloading off (Enter Play Mode Settings), statics keep their values from the last play.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Saved = null;
            Loaded = null;
        }

        /// <summary>The file a slot is saved to.</summary>
        public static string PathFor(string slot)
        {
            return Path.Combine(Application.persistentDataPath, (string.IsNullOrEmpty(slot) ? "save" : slot) + ".json");
        }

        /// <summary>Whether the slot has a save.</summary>
        public static bool HasSave(string slot)
        {
            return File.Exists(PathFor(slot));
        }

        /// <summary>Saves the active scene's state to the slot, replacing what was there.</summary>
        public static void Save(string slot)
        {
            File.WriteAllText(PathFor(slot), Capture(SceneManager.GetActiveScene()));
            Saved?.Invoke(slot);
        }

        /// <summary>
        /// Loads the slot's save into the active scene. Returns false, changing nothing, when the slot is empty or holds
        /// a save of another scene.
        /// </summary>
        public static bool Load(string slot)
        {
            if (!HasSave(slot) || Restore(SceneManager.GetActiveScene(), File.ReadAllText(PathFor(slot))) < 0)
            {
                return false;
            }
            Loaded?.Invoke(slot);
            return true;
        }

        /// <summary>Deletes the slot's save, if there is one.</summary>
        public static void Delete(string slot)
        {
            string path = PathFor(slot);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>The state of every <see cref="ISaveable"/> in the scene, as text.</summary>
        public static string Capture(Scene scene)
        {
            var file = new SaveFile { scene = scene.name };
            foreach (KeyValuePair<string, ISaveable> pair in Find(scene))
            {
                file.entries.Add(new Entry { key = pair.Key, state = pair.Value.CaptureState() });
            }
            return JsonUtility.ToJson(file);
        }

        /// <summary>
        /// Puts back the state that <see cref="Capture"/> returned for the same scene. Returns how many components it
        /// restored, or -1 when the text isn't a save of this scene. Components without an entry are left as they are.
        /// </summary>
        public static int Restore(Scene scene, string text)
        {
            SaveFile file;
            try
            {
                file = JsonUtility.FromJson<SaveFile>(text);
            }
            catch (ArgumentException)
            {
                return -1;
            }
            if (file == null || file.scene != scene.name)
            {
                return -1;
            }

            var states = new Dictionary<string, string>();
            foreach (Entry entry in file.entries)
            {
                states[entry.key] = entry.state;
            }
            int restored = 0;
            foreach (KeyValuePair<string, ISaveable> pair in Find(scene))
            {
                if (states.TryGetValue(pair.Key, out string state))
                {
                    pair.Value.RestoreState(state);
                    restored++;
                }
            }
            return restored;
        }

        // Every ISaveable in the scene with its key, in hierarchy order.
        private static List<KeyValuePair<string, ISaveable>> Find(Scene scene)
        {
            var found = new List<KeyValuePair<string, ISaveable>>();
            var components = new List<MonoBehaviour>();
            var counts = new Dictionary<string, int>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                root.GetComponentsInChildren(true, components);
                foreach (MonoBehaviour component in components)
                {
                    if (component is ISaveable saveable)
                    {
                        string key = ObjectKey(component.transform) + "#" + component.GetType().Name;
                        counts.TryGetValue(key, out int seen);
                        counts[key] = seen + 1;
                        found.Add(new KeyValuePair<string, ISaveable>(seen == 0 ? key : key + "." + seen, saveable));
                    }
                }
            }
            return found;
        }

        // "Playground/Shed.12/Shed Door.7": names with sibling indices, or the Save Id when the object has one.
        private static string ObjectKey(Transform target)
        {
            SaveId id = target.GetComponent<SaveId>();
            if (id != null && !string.IsNullOrEmpty(id.Id))
            {
                return id.Id;
            }
            var path = new StringBuilder();
            for (Transform t = target; t != null; t = t.parent)
            {
                path.Insert(0, t.parent != null ? "/" + t.name + "." + t.GetSiblingIndex() : t.name + "." + t.GetSiblingIndex());
            }
            return path.ToString();
        }
    }
}
