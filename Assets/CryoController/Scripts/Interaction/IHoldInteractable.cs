namespace CryoController
{
    /// <summary>
    /// An <see cref="IInteractable"/> that wants Interact held, not just pressed, before it is used: a heavy lever, a
    /// door being forced, a valve. <see cref="PlayerInteractor"/> times the hold itself and reports it through
    /// HoldProgress; everything else is used on a press.
    /// </summary>
    public interface IHoldInteractable : IInteractable
    {
        /// <summary>Seconds Interact must be held to use this right now. 0 or less is used on a press.</summary>
        float HoldDuration { get; }
    }
}
