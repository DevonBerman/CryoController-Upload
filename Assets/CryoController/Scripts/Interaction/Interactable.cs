using UnityEngine;
using UnityEngine.Events;

namespace CryoController
{
    /// <summary>
    /// Ready-made <see cref="IInteractable"/>: a prompt, an optional hold, optional single use and an On Interact event
    /// that can be wired in the Inspector without code. Derive from it and override <see cref="OnInteract"/> / <see cref="OnFocusChanged"/>
    /// for behaviour in code. Needs a Collider on this object or a child for the player to aim at.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Interactable")]
    public class Interactable : MonoBehaviour, IHoldInteractable, ISaveable
    {
        [Tooltip("Shown in the interaction prompt, e.g. 'Open' or 'Pick up'. Empty hides the prompt but still allows " +
                 "the interaction.")]
        [SerializeField] private string prompt = "Interact";
        [Tooltip("Seconds Interact must be held to use this. 0 uses it on a press.")]
        [SerializeField, Min(0f)] private float holdDuration;
        [Tooltip("After the first interaction this object stops responding and prompting.")]
        [SerializeField] private bool singleUse;
        [Tooltip("Invoked on every interaction. Wire scene objects here (SetActive, Play, your own methods) without code.")]
        [SerializeField] private UnityEvent onInteract;

        private bool used;

        /// <summary>The prompt text; override for prompts that change with state ("Open" / "Close").</summary>
        public virtual string Prompt => prompt;

        /// <summary>Seconds Interact must be held to use this; 0 uses it on a press.</summary>
        public virtual float HoldDuration => holdDuration;

        /// <summary>True while the player is aiming at this object.</summary>
        public bool IsFocused { get; private set; }

        /// <summary>True once a single-use object has been used.</summary>
        public bool IsUsed => used;

        public virtual bool CanInteract(PlayerInteractor interactor)
        {
            return isActiveAndEnabled && !(singleUse && used);
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            used = true;
            OnInteract(interactor);
            onInteract?.Invoke();
        }

        void IInteractable.OnFocusChanged(bool focused)
        {
            IsFocused = focused;
            OnFocusChanged(focused);
        }

        [System.Serializable]
        private struct SavedState
        {
            public bool used;
            public string extra;
        }

        /// <summary>For <see cref="SaveSystem"/>: whether it has been used, and what a subclass adds.</summary>
        public string CaptureState()
        {
            return JsonUtility.ToJson(new SavedState { used = used, extra = CaptureExtraState() });
        }

        /// <summary>For <see cref="SaveSystem"/>.</summary>
        public void RestoreState(string state)
        {
            SavedState saved = JsonUtility.FromJson<SavedState>(state);
            used = saved.used;
            RestoreExtraState(saved.extra);
        }

        /// <summary>Override to save more of a subclass's state alongside whether it has been used.</summary>
        protected virtual string CaptureExtraState()
        {
            return null;
        }

        /// <summary>Override to restore what <see cref="CaptureExtraState"/> returned.</summary>
        protected virtual void RestoreExtraState(string state)
        {
        }

        /// <summary>Behaviour in code, run before the On Interact event.</summary>
        protected virtual void OnInteract(PlayerInteractor interactor)
        {
        }

        /// <summary>Called when the player starts or stops aiming at this object.</summary>
        protected virtual void OnFocusChanged(bool focused)
        {
        }
    }
}
