using UnityEngine;
using UnityEngine.InputSystem;

namespace CryoController
{
    /// <summary>
    /// Shows <see cref="Readable"/> notes to the player: the page on a paper card over the dimmed game, with its title,
    /// the page number and the button that turns the page or closes the note. The player holds still while reading
    /// (the game carries on), and Interact turns the pages. Drawn with IMGUI and scaled with the screen height; replace
    /// the drawing with your own UI through the Opened, PageTurned and Closed events.
    /// </summary>
    [RequireComponent(typeof(PlayerInteractor))]
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Player Reader")]
    public class PlayerReader : MonoBehaviour
    {
        // Sizes for a 1080-line screen, scaled with the real one.
        private const float CardWidth = 720f;
        private const float Padding = 44f;

        [Tooltip("Size of the note's text, in pixels on a 1080-line screen; it scales with the screen height.")]
        [SerializeField, Min(8)] private int fontSize = 24;
        [Tooltip("Colour of the paper.")]
        [SerializeField] private Color paperColor = new Color(0.95f, 0.93f, 0.87f, 1f);
        [Tooltip("Colour of the writing.")]
        [SerializeField] private Color inkColor = new Color(0.13f, 0.12f, 0.11f, 1f);
        [Tooltip("Draw the notes. Off, only the events and properties work, for a UI of your own.")]
        [SerializeField] private bool drawNotes = true;

        /// <summary>The note being read, or null.</summary>
        public Readable Current { get; private set; }

        /// <summary>True while a note is open.</summary>
        public bool IsReading => Current != null;

        /// <summary>The page showing, from 0.</summary>
        public int Page { get; private set; }

        /// <summary>Raised when a note opens.</summary>
        public event System.Action<Readable> Opened;

        /// <summary>Raised when the page turns, with the new page.</summary>
        public event System.Action<int> PageTurned;

        /// <summary>Raised when a note closes.</summary>
        public event System.Action<Readable> Closed;

        private FirstPersonController controller;
        private PlayerInteractor interactor;
        private bool moveAndLookWasEnabled;
        private int openedFrame = -1;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle footerStyle;
        private GUIStyle keyStyle;
        private string pageText;    // "2 / 3", rebuilt when the page changes
        private int pageTextFor = -1;
        private string keyText;     // the Interact button, worked out when a note opens or the device changes
        private bool keyForGamepad;

        private void Awake()
        {
            controller = GetComponent<FirstPersonController>();
            interactor = GetComponent<PlayerInteractor>();
        }

        private void OnDisable()
        {
            Close();
        }

        /// <summary>Opens the note at its first page. Does nothing while another is open.</summary>
        public void Open(Readable note)
        {
            if (note == null || IsReading)
            {
                return;
            }
            Current = note;
            Page = 0;
            openedFrame = Time.frameCount;
            keyText = null;   // the bindings may have changed since the last note
            moveAndLookWasEnabled = controller.MoveAndLookEnabled;
            controller.MoveAndLookEnabled = false;
            interactor.Busy = true;
            note.NotifyRead(this);
            Opened?.Invoke(note);
        }

        /// <summary>Turns to the next page, or closes the note after the last one.</summary>
        public void NextPage()
        {
            if (!IsReading)
            {
                return;
            }
            if (Page + 1 < Current.Pages.Count)
            {
                Page++;
                PageTurned?.Invoke(Page);
            }
            else
            {
                Close();
            }
        }

        /// <summary>Closes the note, if one is open.</summary>
        public void Close()
        {
            if (!IsReading)
            {
                return;
            }
            Readable note = Current;
            Current = null;
            if (controller != null)
            {
                controller.MoveAndLookEnabled = moveAndLookWasEnabled;
            }
            if (interactor != null)
            {
                interactor.Busy = false;
                interactor.IgnoreCurrentPress();   // the press that closed it doesn't open it again
            }
            Closed?.Invoke(note);
        }

        private void Update()
        {
            if (!IsReading)
            {
                return;
            }
            if (Current == null || !Current.isActiveAndEnabled)
            {
                Close();   // destroyed or switched off while open
                return;
            }
            // Input is off under a menu; the press that opened the note doesn't also turn its page.
            if (!controller.InputEnabled || controller.PressesSuppressed || Time.frameCount == openedFrame)
            {
                return;
            }
            InputAction interact = interactor.InteractAction;
            if (interact != null && interact.WasPressedThisFrame())
            {
                NextPage();
            }
        }

        // ------------------------------------------------------------------ Drawing

        private void OnGUI()
        {
            if (!drawNotes || !IsReading || !controller.InputEnabled || Event.current.type != EventType.Repaint)
            {
                return;   // nothing open, or a menu is up
            }

            float scale = HudDrawing.Scale;
            EnsureStyles(scale);
            HudDrawing.Box(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));

            float padding = Padding * scale;
            float width = Mathf.Min(Screen.width * 0.86f, CardWidth * scale);
            float inner = width - padding * 2f;
            string title = Current.Title;
            string body = Current.Pages[Mathf.Clamp(Page, 0, Current.Pages.Count - 1)];
            float titleHeight = string.IsNullOrEmpty(title) ? 0f : titleStyle.fontSize * 1.5f;
            float rule = string.IsNullOrEmpty(title) ? 0f : 18f * scale;
            float bodyHeight = HudDrawing.MeasureHeight(bodyStyle, body, inner);
            float footer = footerStyle.fontSize * 3f;
            float height = Mathf.Min(padding * 2f + titleHeight + rule + bodyHeight + footer, Screen.height * 0.9f);
            Rect card = new Rect(Mathf.Round((Screen.width - width) * 0.5f), Mathf.Round((Screen.height - height) * 0.5f),
                                 width, height);

            HudDrawing.Box(new Rect(card.x + 4f * scale, card.y + 6f * scale, card.width, card.height),
                           new Color(0f, 0f, 0f, 0.35f), 6f * scale);   // a soft shadow under the paper
            HudDrawing.Box(card, paperColor, 6f * scale);

            float top = card.y + padding;
            if (!string.IsNullOrEmpty(title))
            {
                HudDrawing.Text(new Rect(card.x + padding, top, inner, titleHeight), title, titleStyle, inkColor, shadow: false);
                top += titleHeight;
                HudDrawing.Box(new Rect(card.x + padding, top + 4f * scale, inner, Mathf.Max(1f, scale)),
                               HudDrawing.Fade(inkColor, 0.25f));
                top += rule;
            }
            float bodyRoom = card.yMax - padding - footer - top;
            HudDrawing.Text(new Rect(card.x + padding, top, inner, Mathf.Min(bodyHeight, bodyRoom)), body, bodyStyle, inkColor,
                            shadow: false);

            // Footer: the page number on the left, the button and what it does on the right.
            float centerY = card.yMax - padding - footer * 0.35f;
            int pages = Current.Pages.Count;
            if (pages > 1)
            {
                if (pageTextFor != Page * 1000 + pages)
                {
                    pageTextFor = Page * 1000 + pages;
                    pageText = (Page + 1) + " / " + pages;
                }
                HudDrawing.Text(new Rect(card.x + padding, centerY - footerStyle.fontSize, inner * 0.5f, footerStyle.fontSize * 2f),
                                pageText, footerStyle, HudDrawing.Fade(inkColor, 0.6f), shadow: false);
            }
            string action = Page + 1 < pages ? "Next" : "Close";
            float actionWidth = HudDrawing.Measure(footerStyle, action).x;
            bool gamepad = controller.UsingGamepad || controller.ControllerOnly;
            if (keyText == null || keyForGamepad != gamepad)
            {
                keyForGamepad = gamepad;
                keyText = InputRebinder.BindingDisplay(interactor.InteractAction,
                                                       gamepad ? InputRebinder.GamepadScheme : InputRebinder.KeyboardScheme);
            }
            string key = keyText;
            float capWidth = HudDrawing.KeyCapWidth(key, keyStyle);
            float x = card.xMax - padding - actionWidth - (capWidth > 0f ? capWidth + 8f * scale : 0f);
            if (capWidth > 0f)
            {
                x += HudDrawing.KeyCap(x, centerY, key, keyStyle, inkColor, HudDrawing.Fade(inkColor, 0.08f),
                                       HudDrawing.Fade(inkColor, 0.5f)) + 8f * scale;
            }
            HudDrawing.Text(new Rect(x, centerY - footerStyle.fontSize, actionWidth + 4f, footerStyle.fontSize * 2f), action,
                            footerStyle, inkColor, shadow: false);
        }

        private void EnsureStyles(float scale)
        {
            if (titleStyle == null)
            {
                // GUI.skin is only available inside OnGUI.
                titleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontStyle = FontStyle.Bold,
                    padding = new RectOffset(0, 0, 0, 0),
                    clipping = TextClipping.Clip,
                };
                bodyStyle = new GUIStyle(titleStyle) { fontStyle = FontStyle.Normal, alignment = TextAnchor.UpperLeft, wordWrap = true };
                footerStyle = new GUIStyle(titleStyle) { fontStyle = FontStyle.Normal, clipping = TextClipping.Overflow };
                keyStyle = new GUIStyle(footerStyle) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }
            int size = Mathf.RoundToInt(fontSize * scale);
            titleStyle.fontSize = Mathf.RoundToInt(size * 1.3f);
            bodyStyle.fontSize = size;
            footerStyle.fontSize = Mathf.RoundToInt(size * 0.8f);
            keyStyle.fontSize = Mathf.RoundToInt(size * 0.7f);
        }
    }
}
