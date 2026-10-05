using UnityEngine;

namespace CryoController
{
    /// <summary>
    /// An <see cref="Interactable"/> that flips the active state of some objects on each use: a lever that opens a gate, a
    /// switch for a light, a door made of two poses.
    /// </summary>
    [AddComponentMenu("CryoController/Toggle Interactable")]
    public class ToggleInteractable : Interactable
    {
        [Tooltip("Objects whose active state is flipped on every interaction.")]
        [SerializeField] private GameObject[] targets;

        [System.Serializable]
        private struct SavedTargets
        {
            public bool[] active;
        }

        /// <summary>Saves whether each target is switched on.</summary>
        protected override string CaptureExtraState()
        {
            if (targets == null)
            {
                return null;
            }
            var active = new bool[targets.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                active[i] = targets[i] != null && targets[i].activeSelf;
            }
            return JsonUtility.ToJson(new SavedTargets { active = active });
        }

        /// <summary>Puts each target back on or off.</summary>
        protected override void RestoreExtraState(string state)
        {
            if (targets == null || string.IsNullOrEmpty(state))
            {
                return;
            }
            bool[] active = JsonUtility.FromJson<SavedTargets>(state).active;
            for (int i = 0; i < targets.Length && active != null && i < active.Length; i++)
            {
                if (targets[i] != null)
                {
                    targets[i].SetActive(active[i]);
                }
            }
        }

        protected override void OnInteract(PlayerInteractor interactor)
        {
            if (targets == null)
            {
                return;
            }

            foreach (GameObject target in targets)
            {
                if (target != null)
                {
                    target.SetActive(!target.activeSelf);
                }
            }
        }
    }
}
