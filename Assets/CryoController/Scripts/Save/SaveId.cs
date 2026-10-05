using UnityEngine;

namespace CryoController
{
    /// <summary>
    /// Optional: gives an object a fixed id in saves. Without it, <see cref="SaveSystem"/> keys an object by where it is
    /// in the hierarchy, so renaming or moving it in the Editor makes old saves miss it. Give objects you expect to
    /// rename or move between versions of your game a Save Id, and each a different one.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Save Id")]
    public class SaveId : MonoBehaviour
    {
        [Tooltip("This object's id in saves. Unique in the scene. Reset fills in a new one.")]
        [SerializeField] private string id = "";

        /// <summary>The id, or empty for none.</summary>
        public string Id => id;

        private void Reset()
        {
            id = System.Guid.NewGuid().ToString("N");
        }
    }
}
