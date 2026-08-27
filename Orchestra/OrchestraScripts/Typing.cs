using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OrchestraMod
{
    /// <summary>
    /// Makes one of UI Factory's text boxes behave like a text box.
    ///
    /// Two things are wrong with it out of the prefab, and both are about what a
    /// click does. Clicking a field whose text is long enough to be worth editing
    /// leaves the whole of it selected, so the next key typed wipes it -- which
    /// looks like the box having thrown the path away. And a double click, which
    /// everywhere else means "all of it", selects a word.
    ///
    /// So: a single click collapses the selection to where it was clicked and
    /// leaves a caret there; a double click takes the lot. Unity's own
    /// `onFocusSelectAll` would do the first half, and this Unity is older than
    /// that property.
    /// </summary>
    public class Typing : MonoBehaviour, IPointerClickHandler
    {
        private InputField field;

        /// <summary>The last thing the field held that was worth holding. A box
        /// that comes back empty under the pointer has not been edited -- nobody
        /// has typed yet -- so what it had is put back rather than lost.</summary>
        private string kept = "";

        /// <summary>Sets a field up and returns it, for use in a build chain.</summary>
        public static InputField On(InputField field)
        {
            if (field == null)
            {
                return null;
            }
            Typing typing = field.gameObject.GetComponent<Typing>();
            if (typing == null)
            {
                typing = field.gameObject.AddComponent<Typing>();
            }
            typing.field = field;

            // A caret worth seeing: the prefab's is a hairline in the text's own
            // colour, which against Besiege's dark boxes is nearly nothing.
            field.caretWidth = 2;
            field.customCaretColor = true;
            field.caretColor = Color.white;
            field.caretBlinkRate = 0.85f;
            field.selectionColor = new Color(0.35f, 0.55f, 0.75f, 0.6f);
            return field;
        }

        private void LateUpdate()
        {
            if (field != null && !field.isFocused
                && !string.IsNullOrEmpty(field.text))
            {
                kept = field.text;
            }
        }

        public void OnPointerClick(PointerEventData pointer)
        {
            if (field == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(field.text) && kept.Length > 0)
            {
                field.text = kept;
                field.caretPosition = kept.Length;
            }
            if (pointer.clickCount >= 2)
            {
                field.selectionAnchorPosition = 0;
                field.selectionFocusPosition = field.text == null
                    ? 0 : field.text.Length;
                return;
            }
            // Collapse whatever the focus left selected onto the caret, so the
            // first keystroke inserts rather than replaces.
            field.selectionAnchorPosition = field.caretPosition;
            field.selectionFocusPosition = field.caretPosition;
        }
    }
}
