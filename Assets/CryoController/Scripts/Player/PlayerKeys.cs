using System.Collections.Generic;
using UnityEngine;

namespace CryoController
{
    /// <summary>
    /// The keys the player carries, by id: a key ring for <see cref="Door"/> locks. <see cref="KeyPickup"/> adds to it;
    /// your own scripts can add, take and check keys, and save them with the rest of the game.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Player Keys")]
    public class PlayerKeys : MonoBehaviour, ISaveable
    {
        [Tooltip("Keys the player starts with, by id.")]
        [SerializeField] private string[] startingKeys = new string[0];

        private readonly HashSet<string> keys = new HashSet<string>();

        /// <summary>The keys held now.</summary>
        public IReadOnlyCollection<string> Keys => keys;

        /// <summary>Raised when a key is added, with its id.</summary>
        public event System.Action<string> KeyAdded;

        /// <summary>Raised when a key is taken away, with its id.</summary>
        public event System.Action<string> KeyRemoved;

        private void Awake()
        {
            if (startingKeys == null)
            {
                return;
            }
            foreach (string id in startingKeys)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    keys.Add(id);
                }
            }
        }

        /// <summary>Whether the player holds the key.</summary>
        public bool Has(string id)
        {
            return !string.IsNullOrEmpty(id) && keys.Contains(id);
        }

        /// <summary>Gives the player the key. Returns false if they already had it.</summary>
        public bool Add(string id)
        {
            if (string.IsNullOrEmpty(id) || !keys.Add(id))
            {
                return false;
            }
            KeyAdded?.Invoke(id);
            return true;
        }

        /// <summary>Takes the key from the player. Returns false if they didn't have it.</summary>
        public bool Remove(string id)
        {
            if (string.IsNullOrEmpty(id) || !keys.Remove(id))
            {
                return false;
            }
            KeyRemoved?.Invoke(id);
            return true;
        }

        [System.Serializable]
        private struct SavedState
        {
            public string[] keys;
        }

        /// <summary>For <see cref="SaveSystem"/>: the keys held.</summary>
        public string CaptureState()
        {
            var held = new string[keys.Count];
            keys.CopyTo(held);
            return JsonUtility.ToJson(new SavedState { keys = held });
        }

        /// <summary>For <see cref="SaveSystem"/>: exactly the saved keys, with KeyAdded and KeyRemoved for the differences.</summary>
        public void RestoreState(string state)
        {
            SavedState saved = JsonUtility.FromJson<SavedState>(state);
            var wanted = new HashSet<string>(saved.keys ?? new string[0]);
            foreach (string id in new List<string>(keys))
            {
                if (!wanted.Contains(id))
                {
                    Remove(id);
                }
            }
            foreach (string id in wanted)
            {
                Add(id);
            }
        }

        /// <summary>Takes every key away.</summary>
        public void Clear()
        {
            var removed = new List<string>(keys);
            keys.Clear();
            foreach (string id in removed)
            {
                KeyRemoved?.Invoke(id);
            }
        }
    }
}
