namespace CryoController
{
    /// <summary>
    /// Something the player can use with the Interact action. Implement this directly for code-driven objects, or derive
    /// from <see cref="Interactable"/> to get a prompt, single-use handling and an Inspector event for free.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Short text for the on-screen prompt, e.g. "Open" or "Pick up". Empty hides the prompt.</summary>
        string Prompt { get; }

        /// <summary>Whether the player can use this right now. False also hides the prompt.</summary>
        bool CanInteract(PlayerInteractor interactor);

        /// <summary>Called once per completed Interact input while this object is in focus.</summary>
        void Interact(PlayerInteractor interactor);

        /// <summary>Called when the player starts or stops aiming at this object, for highlights and the like.</summary>
        void OnFocusChanged(bool focused);
    }
}
