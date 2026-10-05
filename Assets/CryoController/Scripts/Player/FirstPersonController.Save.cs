using UnityEngine;

namespace CryoController
{
    // FirstPersonController: what a save keeps of the player (where they stand and look). The settings, state and frame
    // loop are in FirstPersonController.cs.
    public partial class FirstPersonController : ISaveable
    {
        [System.Serializable]
        private struct SavedState
        {
            public Vector3 position;
            public float yaw;
            public float pitch;
        }

        /// <summary>For <see cref="SaveSystem"/>: where the player stands and looks. They are restored standing.</summary>
        public string CaptureState()
        {
            return JsonUtility.ToJson(new SavedState { position = transform.position, yaw = yaw, pitch = pitch });
        }

        /// <summary>For <see cref="SaveSystem"/>.</summary>
        public void RestoreState(string state)
        {
            SavedState saved = JsonUtility.FromJson<SavedState>(state);
            if (controller == null)
            {
                transform.SetPositionAndRotation(saved.position, Quaternion.Euler(0f, saved.yaw, 0f));
                return;   // not awake yet: it picks up the rotation when it wakes
            }
            Teleport(saved.position, saved.yaw);
            Pitch = saved.pitch;
        }
    }
}
