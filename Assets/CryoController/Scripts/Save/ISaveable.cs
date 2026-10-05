namespace CryoController
{
    /// <summary>
    /// A component whose state goes into a save. <see cref="SaveSystem"/> finds every component in the scene that
    /// implements this, switched-off ones included, keeps what <see cref="CaptureState"/> returns under a key for the
    /// component, and hands it back to <see cref="RestoreState"/> on load. The package's player, doors, keys, carryables,
    /// notes and interactables implement it; implement it on your own components to save them too.
    /// </summary>
    public interface ISaveable
    {
        /// <summary>The component's state as a string, usually <c>JsonUtility.ToJson</c> of a small struct.</summary>
        string CaptureState();

        /// <summary>Puts back a state that <see cref="CaptureState"/> returned.</summary>
        void RestoreState(string state);
    }
}
