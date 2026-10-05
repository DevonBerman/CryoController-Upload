using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace CryoController
{
    /// <summary>
    /// A note, letter, sign or book page the player reads: using it opens its text on screen through the player's
    /// <see cref="PlayerReader"/>, one page at a time. A line holding only <c>---</c> starts a new page.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Readable")]
    public class Readable : MonoBehaviour, IInteractable, ISaveable
    {
        /// <summary>The line that separates pages in the text.</summary>
        public const string PageBreak = "---";

        [Tooltip("Heading at the top of every page. Empty shows none.")]
        [SerializeField] private string title = "Note";
        [Tooltip("The text. A line holding only --- starts a new page.")]
        [SerializeField, TextArea(6, 20)] private string text = "";
        [Tooltip("Prompt shown while the player aims at it.")]
        [SerializeField] private string prompt = "Read";
        [Tooltip("Invoked when the player starts reading it.")]
        [SerializeField] private UnityEvent onRead;

        private List<string> pages;   // the text split into pages, made when first needed

        /// <summary>The heading shown at the top of every page.</summary>
        public string Title => title;

        /// <summary>The pages of the text.</summary>
        public IReadOnlyList<string> Pages => pages ?? (pages = Split(text));

        /// <summary>True once the player has read it.</summary>
        public bool HasBeenRead { get; private set; }

        /// <summary>Raised when the player starts reading it.</summary>
        public event System.Action<PlayerReader> Read;

        public string Prompt => prompt;

        public bool CanInteract(PlayerInteractor interactor)
        {
            return isActiveAndEnabled && interactor != null && interactor.GetComponent<PlayerReader>() != null;
        }

        public void Interact(PlayerInteractor interactor)
        {
            PlayerReader reader = interactor != null ? interactor.GetComponent<PlayerReader>() : null;
            if (reader != null)
            {
                reader.Open(this);
            }
        }

        void IInteractable.OnFocusChanged(bool focused)
        {
        }

        [System.Serializable]
        private struct SavedState
        {
            public bool read;
        }

        /// <summary>For <see cref="SaveSystem"/>: whether it has been read.</summary>
        public string CaptureState()
        {
            return JsonUtility.ToJson(new SavedState { read = HasBeenRead });
        }

        /// <summary>For <see cref="SaveSystem"/>.</summary>
        public void RestoreState(string state)
        {
            HasBeenRead = JsonUtility.FromJson<SavedState>(state).read;
        }

        /// <summary>Replaces the text, for notes written at runtime.</summary>
        public void SetText(string newTitle, string newText)
        {
            title = newTitle;
            text = newText;
            pages = null;
        }

        // Called by the reader when it opens this.
        internal void NotifyRead(PlayerReader reader)
        {
            HasBeenRead = true;
            Read?.Invoke(reader);
            onRead?.Invoke();
        }

        private static List<string> Split(string source)
        {
            var result = new List<string>();
            var page = new System.Text.StringBuilder();
            foreach (string line in (source ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                if (line.Trim() == PageBreak)
                {
                    result.Add(page.ToString().Trim());
                    page.Clear();
                }
                else
                {
                    page.Append(line).Append('\n');
                }
            }
            result.Add(page.ToString().Trim());
            result.RemoveAll(string.IsNullOrEmpty);
            if (result.Count == 0)
            {
                result.Add("");
            }
            return result;
        }

        private void OnValidate()
        {
            pages = null;
        }
    }
}
