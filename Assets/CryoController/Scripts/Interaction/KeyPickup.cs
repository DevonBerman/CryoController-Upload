using UnityEngine;
using UnityEngine.Events;

namespace CryoController
{
    /// <summary>
    /// A key lying in the world: using it gives the key to the player's <see cref="PlayerKeys"/> and hides it. Doors
    /// locked with the same Key Id then open for that player.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Key Pickup")]
    public class KeyPickup : MonoBehaviour, IInteractable, ISaveable
    {
        [Tooltip("The key's id. A Door with this Key Id opens for a player who holds it.")]
        [SerializeField] private string keyId = "key";
        [Tooltip("The key's name for the player, in the prompt and the message when it is taken: \"shed key\".")]
        [SerializeField] private string displayName = "key";
        [Tooltip("The prompt's verb, before the name: \"Take\".")]
        [SerializeField] private string prompt = "Take";
        [Tooltip("Switch this object off once the key is taken.")]
        [SerializeField] private bool hideWhenTaken = true;
        [Tooltip("Invoked when the key is taken.")]
        [SerializeField] private UnityEvent onTaken;

        private string fullPrompt;   // "Take shed key", built once

        /// <summary>True once the key has been taken.</summary>
        public bool IsTaken { get; private set; }

        /// <summary>The key's id.</summary>
        public string KeyId => keyId;

        /// <summary>Raised when the key is taken, with the interactor that took it.</summary>
        public event System.Action<PlayerInteractor> Taken;

        public string Prompt => fullPrompt ?? (fullPrompt = string.IsNullOrEmpty(displayName) ? prompt : prompt + " " + displayName);

        public bool CanInteract(PlayerInteractor interactor)
        {
            return isActiveAndEnabled && !IsTaken && interactor != null && interactor.GetComponent<PlayerKeys>() != null;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }
            IsTaken = true;
            interactor.GetComponent<PlayerKeys>().Add(keyId);
            if (!string.IsNullOrEmpty(displayName))
            {
                interactor.Notify("Picked up the " + displayName + ".");
            }
            Taken?.Invoke(interactor);
            onTaken?.Invoke();
            if (hideWhenTaken)
            {
                gameObject.SetActive(false);
            }
        }

        void IInteractable.OnFocusChanged(bool focused)
        {
        }

        [System.Serializable]
        private struct SavedState
        {
            public bool taken;
        }

        /// <summary>For <see cref="SaveSystem"/>: whether the key has been taken.</summary>
        public string CaptureState()
        {
            return JsonUtility.ToJson(new SavedState { taken = IsTaken });
        }

        /// <summary>For <see cref="SaveSystem"/>.</summary>
        public void RestoreState(string state)
        {
            IsTaken = JsonUtility.FromJson<SavedState>(state).taken;
            if (hideWhenTaken)
            {
                gameObject.SetActive(!IsTaken);
            }
        }

        private void OnValidate()
        {
            fullPrompt = null;
        }
    }
}
