using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OrchestraMod
{
    /// <summary>
    /// A UI Factory window that sits against the bottom edge of Besiege's block
    /// mapper, the same width as it, so the two read as one window with a seam.
    ///
    /// The mapper is mesh UI drawn in world space: its widgets are not on any
    /// canvas a mod could parent into, and there is no API for adding a row to it.
    /// So a panel that wants to look like part of it has to be a separate window
    /// *measured against it*, in screen space, every frame -- which is what this
    /// does, and all it does. What goes in the window is the subclass's business.
    ///
    /// Two panels are built on this: <see cref="OrchestraPanel"/>, which draws an
    /// instrument block's settings, and <see cref="LoaderPanel"/>, which draws the
    /// MIDI loader's. Neither can exist without UI Factory, and both fall back to
    /// Besiege's own mapper when it is absent -- see <see cref="UIF"/>.
    /// </summary>
    public abstract class DockedPanel : MonoBehaviour
    {
        /// <summary>Over the game, under nothing this mod draws.</summary>
        protected const int CanvasOrder = 2400;

        /// <summary>What the panel is drawn at before a mapper has been measured.
        /// It ends up whatever Besiege's own window is wide.</summary>
        protected const float DefaultWidth = 434f;

        protected const float Margin = 12f;
        protected const float RowHeight = 26f;
        protected const float RowGap = 4f;

        /// <summary>UI Factory authors against 1920x1080 and matches on height;
        /// anything else draws Besiege's own widgets at the wrong size beside the
        /// game's.</summary>
        protected static readonly Vector2 Reference = new Vector2(1920f, 1080f);

        /// <summary>How wide the panel is drawn, which is how wide the mapper is:
        /// the two are one window with a seam.</summary>
        protected float width = DefaultWidth;

        protected GameObject window;
        protected RectTransform windowRect;

        /// <summary>Where the rows go: the Window prefab's own scroll content, or
        /// the window itself if this UI Factory has none.</summary>
        protected Transform host;

        /// <summary>The scroll content, when that is what <see cref="host"/> is.</summary>
        protected RectTransform content;

        private ClickShield shield;

        /// <summary>The camera Besiege's mapper is drawn by, held so the layer
        /// search is not repeated every frame.</summary>
        private Camera mapperEye;

        /// <summary>The column the captions are written in.</summary>
        protected float LabelWidth = 96f;

        /// <summary>The number at the end of a slider row.</summary>
        protected float ValueWidth = 62f;

        /// <summary>True while a panel is writing its controls from the block, so
        /// their own change events do not echo back into it.</summary>
        protected bool filling;

        /// <summary>The rows this panel drew, in the order it drew them.</summary>
        protected readonly List<Row> rows = new List<Row>();

        /// <summary>
        /// Settings changed here that Besiege has not been told about yet.
        ///
        /// A mapper setting is stored twice: the live value, and the one the block
        /// is *loaded* from. Assigning `MapperType.Value` writes only the first, so
        /// a panel that stopped there would be heard now and forgotten on save.
        /// Committing reconciles them, and it is not free -- Besiege reserialises
        /// the block and adds an undo entry -- so a drag writes live every frame
        /// and commits once, when the mouse comes up.
        /// </summary>
        private readonly List<MapperType> pending = new List<MapperType>();

        /// <summary>The selectors are drawn narrower than the row they sit in, and
        /// centred on it: the same 250 for both panels, so the two read as one
        /// menu.</summary>
        protected const float SelectorWidth = 250f;

        /// <summary>What is typed into a box, and what a toggle says: one size for
        /// every one of them, in either panel.</summary>
        protected const int FieldFont = 15;

        // ---- rows ------------------------------------------------------------

        /// <summary>
        /// A row of the panel: a caption, a slider, and the number at the end of
        /// it, which can be typed into.
        ///
        /// Both panels are made of these -- the instrument's settings and the
        /// loader's -- so the widget, the formatting, the typing and the
        /// committing are written once here, and the panels say only which mapper
        /// control each row stands for.
        /// </summary>
        protected class Row
        {
            public UnityEngine.UI.Slider Control;
            public Text Caption;

            /// <summary>The number at the end of the row, which can be typed into.
            /// Value is the label inside it -- or the whole of it, on a UI Factory
            /// with no text box to borrow.</summary>
            public UnityEngine.UI.InputField Box;
            public Text Value;

            public MSlider Bound;
            public bool Note;       // shown as a note name rather than a number
        }

        /// <summary>
        /// A caption and a `&lt; choice &gt;` selector, whose middle opens the whole
        /// list rather than stepping through it.
        ///
        /// <see cref="Chooser"/> rather than UI Factory's `Options` prefab: the
        /// prefab steps only, which is a press per entry through nine blocks or
        /// forty files, and its drop-down cousin draws its list inside whatever
        /// mask it is under -- which, in a window built on a scroll view, means
        /// clipped. The Chooser hangs its list off the canvas instead. It is the
        /// same control Braids Synth and Special Effects use, kept in step with
        /// them.
        /// </summary>
        protected Chooser AddSelector(string caption, float y, List<string> choices,
                                      int picked)
        {
            Label(caption, Margin, y, LabelWidth, RowHeight, 13,
                  TextAnchor.MiddleLeft, UIF.QuietInk);
            // Centred on the slider column and the number beside it together,
            // rather than started where the sliders start: it is the odd row out.
            float span = width - Margin * 2f - LabelWidth;
            return Chooser.Make(host, transform,
                                Margin + LabelWidth + (span - SelectorWidth) / 2f, y,
                                SelectorWidth, RowHeight, choices, picked);
        }

        /// <summary>
        /// The part of a slider's range the handle runs through, which is not always
        /// the whole of what the setting accepts: RANGE will take any distance a
        /// level is wide, and a handle that had to cover all of it would be useless
        /// for the fifty metres anybody actually wants. Typing is not limited to
        /// this -- <see cref="Typed"/> clamps to the setting's own bounds -- and a
        /// value beyond it parks the handle at that end.
        /// </summary>
        protected virtual void Span(MSlider bound, out float min, out float max)
        {
            min = bound.Min;
            max = bound.Max;
        }


        protected float AddSlider(float y, string caption, MSlider bound, bool isNote)
        {
            Text label = Label(caption, Margin, y, LabelWidth, RowHeight, 13,
                               TextAnchor.MiddleLeft, UIF.QuietInk);

            GameObject go = UIF.Spawn(UIF.SliderPrefab, host);
            if (go == null)
            {
                return y + RowHeight + RowGap;
            }
            float x = Margin + LabelWidth;
            float w = width - Margin * 2f - LabelWidth - ValueWidth - RowGap;
            Place(go, x, y, w, RowHeight);

            UnityEngine.UI.Slider control = go.GetComponent<UnityEngine.UI.Slider>();
            if (control == null)
            {
                control = go.GetComponentInChildren<UnityEngine.UI.Slider>(true);
            }
            Row row = new Row();
            row.Control = control;
            row.Caption = label;
            row.Bound = bound;
            row.Note = isNote;
            AddValue(row, width - Margin - ValueWidth, y);
            if (control != null)
            {
                float low, high;
                Span(bound, out low, out high);
                control.minValue = low;
                control.maxValue = high;
                // Notes snap: dragged freely a block lands a quarter-tone sharp and
                // is unplayable beside another.
                control.wholeNumbers = isNote;
                Row captured = row;
                control.onValueChanged.AddListener(delegate(float v) { Dragged(captured, v); });
            }
            rows.Add(row);
            return y + RowHeight + RowGap;
        }

        /// <summary>
        /// The number at the end of a row: Besiege's own text box, so a setting can
        /// be typed exactly rather than found by dragging.
        ///
        /// UI Factory's Input Field, which brings the game's own look and -- the part
        /// that matters -- the behaviour that stops Besiege's hotkeys firing at what
        /// is being typed. Without that prefab the number is a plain label again, and
        /// the slider is the only way to set it.
        /// </summary>
        private void AddValue(Row row, float x, float y)
        {
            GameObject go = UIF.Spawn(UIF.InputPrefab, host);
            if (go == null)
            {
                row.Value = Label("", x, y, ValueWidth, RowHeight, 13,
                                  TextAnchor.MiddleRight, Color.white);
                return;
            }
            Place(go, x, y, ValueWidth, RowHeight);

            UnityEngine.UI.InputField field =
                go.GetComponent<UnityEngine.UI.InputField>();
            if (field == null)
            {
                field = go.GetComponentInChildren<UnityEngine.UI.InputField>(true);
            }
            if (field == null)
            {
                return;
            }

            row.Box = field;
            row.Value = Style(field.textComponent, TextAnchor.MiddleRight, Color.white);
            // The prefab's placeholder is a word in whatever language the game is in;
            // an empty box that says nothing is what a number wants.
            Text ghost = field.placeholder as Text;
            if (ghost != null)
            {
                UIF.Untranslate(ghost);
                ghost.text = "";
            }
            field.lineType = UnityEngine.UI.InputField.LineType.SingleLine;
            field.characterLimit = 8;

            Row captured = row;
            field.onEndEdit.AddListener(delegate(string typed) { Typed(captured, typed); });
        }


        protected void Write(Row row)
        {
            if (row.Bound == null)
            {
                return;
            }
            string shown = row.Note
                ? NoteName(Mathf.RoundToInt(row.Bound.Value))
                : row.Bound.Value.ToString("0.00");
            if (row.Box != null)
            {
                // Not while it is being typed in: the box is the player's until they
                // are finished with it, and a drag elsewhere must not take the caret
                // out from under them.
                if (!row.Box.isFocused)
                {
                    row.Box.text = shown;
                }
                return;
            }
            if (row.Value != null)
            {
                row.Value.text = shown;
            }
        }

        /// <summary>
        /// A number was typed. Unlike a drag this is a finished edit, so it commits
        /// at once rather than waiting for a mouse button that was never held.
        /// Anything unreadable leaves the setting alone, and the box goes back to
        /// showing what it is.
        /// </summary>
        private void Typed(Row row, string text)
        {
            if (row.Bound != null && !filling)
            {
                float value;
                if (Read(row, text, out value))
                {
                    if (row.Note)
                    {
                        value = Mathf.Round(value);
                    }
                    value = Mathf.Clamp(value, row.Bound.Min, row.Bound.Max);
                    row.Bound.Value = value;
                    if (row.Control != null)
                    {
                        // The slider's own change event would only write back what
                        // was just written, and queue a commit this does itself.
                        filling = true;
                        row.Control.value = value;
                        filling = false;
                    }
                    Commit(row.Bound);
                }
            }
            Write(row);
        }

        /// <summary>
        /// Reads back what <see cref="Write"/> puts out, and what someone would type
        /// instead of it: a note as a name or as a number, and a bare number anywhere.
        /// </summary>
        private static bool Read(Row row, string text, out float value)
        {
            value = 0f;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            text = text.Trim();
            if (float.TryParse(text, out value))
            {
                return true;
            }
            return row.Note && NoteNumber(text, out value);
        }

        /// <summary>
        /// "C4", "F#3", "Bb5" as the MIDI number the slider holds. C4 is middle C,
        /// which is what <see cref="NoteName"/> writes.
        /// </summary>
        private static bool NoteNumber(string text, out float value)
        {
            value = 0f;
            // C  D  E  F  G  A  B
            int[] steps = new int[] { 9, 11, 0, 2, 4, 5, 7 };
            char letter = char.ToUpper(text[0]);
            if (letter < 'A' || letter > 'G')
            {
                return false;
            }
            int semitone = steps[letter - 'A'];
            int at = 1;
            if (at < text.Length && (text[at] == '#' || text[at] == 's'))
            {
                semitone++;
                at++;
            }
            else if (at < text.Length && (text[at] == 'b' || text[at] == 'B'))
            {
                semitone--;
                at++;
            }
            int octave;
            if (at >= text.Length || !int.TryParse(text.Substring(at), out octave))
            {
                return false;
            }
            value = (octave + 1) * 12 + semitone;
            return true;
        }

        /// <summary>C4 is middle C, which is the convention Besiege's players use.</summary>
        private static string NoteName(int note)
        {
            string[] names = new string[]
            {
                "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"
            };
            if (note < 0 || note > 127)
            {
                return note.ToString();
            }
            return names[note % 12] + (note / 12 - 1);
        }


        protected void Queue(MapperType changed)
        {
            if (changed != null && !pending.Contains(changed))
            {
                pending.Add(changed);
            }
        }


        protected void CommitPending()
        {
            for (int i = 0; i < pending.Count; i++)
            {
                Commit(pending[i]);
            }
            pending.Clear();
        }


        /// <summary>
        /// Writes a setting through the mapper, so it survives a save and can be
        /// undone. `ApplyValue` is the fallback where that machinery is not up: the
        /// live value is set either way, so the block sounds right regardless.
        /// </summary>
        protected void Commit(MapperType changed)
        {
            if (changed == null)
            {
                return;
            }
            try
            {
                BlockMapper mapper = BlockMapper.CurrentInstance;
                if (mapper != null && mapper.Current != null)
                {
                    BlockMapper.OnEditField(mapper.Current, changed);
                    return;
                }
            }
            catch (Exception e)
            {
                Log.Warn("could not commit " + changed.Key + " (" + e.Message
                         + "); applying it directly.");
            }
            try
            {
                changed.ApplyValue();
            }
            catch (Exception)
            {
                // Nothing further to try.
            }
        }


        /// <summary>
        /// A slider was dragged. The value is written live so it is heard at once,
        /// and queued rather than committed: a commit reserialises the block, and
        /// one per frame of a drag is a bill for nothing.
        /// </summary>
        private void Dragged(Row row, float value)
        {
            if (row.Bound == null || filling)
            {
                return;
            }
            row.Bound.Value = value;
            Write(row);
            Queue(row.Bound);
        }

        /// <summary>Commits anything a drag left outstanding, once the mouse is
        /// up. Called from each panel's own Update.</summary>
        protected void SettleDrags()
        {
            if (pending.Count > 0 && !Input.GetMouseButton(0))
            {
                CommitPending();
            }
        }

        // ---- the window ------------------------------------------------------

        /// <summary>
        /// Makes the canvas and the window, ready for rows. Throws if UI Factory
        /// will not supply the prefab, which is the caller's cue to give up and
        /// leave Besiege's own mapper alone.
        /// </summary>
        protected void OpenWindow(string name)
        {
            Canvas canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = CanvasOrder;
                canvas.pixelPerfect = false;

                CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = Reference;
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 1f;

                gameObject.AddComponent<GraphicRaycaster>();
            }

            window = UIF.Spawn(UIF.WindowPrefab, canvas.transform);
            if (window == null)
            {
                throw new Exception("UI Factory gave no Window prefab");
            }
            window.name = name;

            windowRect = window.transform as RectTransform;
            windowRect.anchorMin = new Vector2(0.5f, 0.5f);
            windowRect.anchorMax = new Vector2(0.5f, 0.5f);
            windowRect.pivot = new Vector2(0.5f, 0.5f);

            // The bar goes: it held a name for a window that is now the lower half
            // of Besiege's own, which is already titled, and a cross for a window
            // the mapper opens and closes. What is left is the frame and its rows,
            // which is what docking wants -- a seam, not two headers.
            RectTransform bar = window.transform.FindChild("TopBar") as RectTransform;
            if (bar != null)
            {
                bar.gameObject.SetActive(false);
            }

            // The prefab's scroll view starts below the bar that is no longer
            // there, so it is stretched over the whole window: otherwise the panel
            // opens with a bar's worth of empty frame above its first row.
            ScrollRect scroll = window.GetComponentInChildren<ScrollRect>(true);
            RectTransform view = scroll == null ? null : scroll.transform as RectTransform;
            if (view != null)
            {
                view.anchorMin = Vector2.zero;
                view.anchorMax = Vector2.one;
                view.offsetMin = Vector2.zero;
                view.offsetMax = Vector2.zero;
            }

            shield = gameObject.GetComponent<ClickShield>();
            if (shield == null)
            {
                shield = gameObject.AddComponent<ClickShield>();
            }
            shield.Guard(windowRect);

            // The rows go in the scroll view the Window prefab ships with, which is
            // where a window's contents are meant to go. Putting them on the window
            // instead leaves that scroll view holding the prefab's own 500-unit
            // placeholder -- taller than any panel, so its scrollbar sits there
            // permanently beside an empty scroll area. Besiege's scroll view hides
            // both bars for contents that fit, so filling it properly is also what
            // takes the bar away.
            //
            // The window itself already carries StopsZoomWhenHovered, so the wheel
            // over the panel does not zoom the level.
            content = ScrollContent();
            host = content != null ? (Transform)content : window.transform;
        }

        /// <summary>Sizes the window to the rows that were built into it.</summary>
        protected void CloseWindow(float height)
        {
            if (content != null)
            {
                content.sizeDelta = new Vector2(content.sizeDelta.x, height);
            }
            if (windowRect != null)
            {
                windowRect.sizeDelta = new Vector2(width, height);
            }
            // So the canvas has a size before the window is placed against it, and
            // the scroll view has measured what it now holds.
            Canvas.ForceUpdateCanvases();
        }

        protected void DestroyWindow()
        {
            // The rows are widgets under the window: keeping them past it would
            // leave a rebuilt panel writing into destroyed controls, and a second
            // set of rows appended to the first.
            rows.Clear();
            host = null;
            content = null;
            if (window != null)
            {
                Destroy(window);
                window = null;
                windowRect = null;
            }
        }

        private RectTransform ScrollContent()
        {
            ScrollRect scroll = window.GetComponentInChildren<ScrollRect>(true);
            if (scroll == null || scroll.content == null)
            {
                Log.Warn("UI Factory's Window prefab has no scroll view; the panel's "
                         + "rows go straight on the window.");
                return null;
            }
            return scroll.content;
        }

        // ---- where the window sits -------------------------------------------

        /// <summary>
        /// Rebuilds the rows for a width that has just changed. Called from within
        /// docking, so it must both rebuild *and* leave the window ready to be
        /// placed; returning false abandons the placement for this frame.
        /// </summary>
        protected abstract bool Rebuild();

        /// <summary>
        /// Puts the panel against the bottom edge of the mapper. Called from
        /// LateUpdate rather than Update: the mapper is dragged by its own
        /// behaviour, and a panel placed before it has moved is a panel one frame
        /// behind it -- which reads as the join coming apart while it is dragged.
        /// </summary>
        protected void Dock()
        {
            if (windowRect == null || window == null || !window.activeSelf)
            {
                return;
            }

            Rect frame;
            if (!MapperFrame(out frame))
            {
                return;
            }

            if (Widen(frame))
            {
                // Rebuilt and placed in the same frame. Returning here instead --
                // which is what this did -- leaves the window wherever it was and
                // clears the flag that lets docking run at all, so it never docks
                // and never follows a drag again.
                if (!Rebuild())
                {
                    return;
                }
                Canvas.ForceUpdateCanvases();
            }

            float scale = Scale();
            Vector2 size = windowRect.sizeDelta;
            float left = (frame.xMin - Screen.width * 0.5f) * scale;
            float bottom = (frame.yMin - Screen.height * 0.5f) * scale;
            windowRect.anchoredPosition =
                new Vector2(left + size.x * 0.5f, bottom - size.y * 0.5f);
        }

        /// <summary>
        /// Canvas units per screen pixel. The scaler matches on height against a
        /// 1080-tall reference, so one unit is one pixel at 1080p.
        /// </summary>
        protected float Scale()
        {
            return Screen.height > 0 ? Reference.y / Screen.height : 1f;
        }

        /// <summary>
        /// Takes the panel's width from the mapper's. True if it changed, in which
        /// case the rows -- which are laid out to it -- no longer fit.
        /// </summary>
        protected bool Widen(Rect frame)
        {
            float wide = frame.width * Scale();
            if (Mathf.Abs(wide - width) <= 0.5f)
            {
                return false;
            }
            width = wide;
            return true;
        }

        /// <summary>
        /// Besiege's mapper window in screen pixels, or false if it cannot be found
        /// -- in which case the panel stays where it is rather than jumping to a
        /// corner.
        ///
        /// The window has to be picked out of everything the mapper draws, and the
        /// list was read out of the game rather than guessed at -- the panel logs
        /// what it measures, and this is what it saw with a piano open at 4K:
        ///
        ///     Background   874.80 x 389.88   at y 1540.87   &lt;- the window
        ///     Background   874.80 x 281.88   at y 1540.87
        ///     Background   874.80 x 174.96   at y 1658.59
        ///     WideShadow   972.00 x 194.40   at y 1638.37   &lt;- 11% wider, and higher
        ///     Mask         874.80 x 1555.20  at y  267.55   &lt;- the scroll region
        ///     Visual        93.31 x  93.31                  &lt;- a button
        ///
        /// So: the window is a `Background`, all of which are its width, and the
        /// tallest of them is the frame -- the others are sections inside it. Taking
        /// the widest thing drawn lands on `WideShadow`, which is how the panel came
        /// to be an eleventh wider than the mapper and to sit over its lower half;
        /// taking `Visual` by name lands on a 93-pixel button, which is how it came
        /// to be a narrow strip. Both were shipped, and both are in the log above.
        /// </summary>
        protected bool MapperFrame(out Rect frame)
        {
            frame = new Rect();
            try
            {
                BlockMapper mapper = BlockMapper.CurrentInstance;
                if (mapper == null)
                {
                    return false;
                }
                Renderer[] parts = mapper.GetComponentsInChildren<Renderer>(false);
                Camera eye = null;
                Rect best = new Rect();
                string bestName = null;
                bool named = false;

                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i] == null || !parts[i].enabled)
                    {
                        continue;
                    }
                    if (eye == null)
                    {
                        eye = MapperCamera(parts[i].gameObject.layer);
                        if (eye == null)
                        {
                            Explain("no camera draws layer "
                                    + parts[i].gameObject.layer.ToString());
                            return false;
                        }
                    }
                    Rect here = ScreenRect(parts[i], eye);
                    if (here.width < 1f || here.height < 1f)
                    {
                        continue;
                    }

                    if (parts[i].name == "Background")
                    {
                        // The frame, and the sections drawn inside it, all share its
                        // width; the tallest is the frame itself.
                        if (!named || here.height > best.height
                            || (here.height == best.height && here.yMin < best.yMin))
                        {
                            named = true;
                            best = here;
                            bestName = parts[i].name;
                        }
                        continue;
                    }
                    if (named || here.width > Screen.width * 0.95f)
                    {
                        continue;
                    }
                    if (bestName == null || here.width > best.width)
                    {
                        best = here;
                        bestName = parts[i].name;
                    }
                }

                if (bestName == null)
                {
                    Explain(parts.Length == 0
                        ? "the mapper draws nothing this can measure"
                        : "none of the mapper's " + parts.Length.ToString()
                          + " parts look like its window");
                    return false;
                }
                Explain("docking to '" + bestName + "' at " + best.ToString());
                frame = best;
                return true;
            }
            catch (Exception e)
            {
                Explain("could not measure the mapper: " + e.Message);
                return false;
            }
        }

        /// <summary>One renderer's world box, in screen pixels.</summary>
        private static Rect ScreenRect(Renderer part, Camera eye)
        {
            Bounds box = part.bounds;
            Vector3 a = eye.WorldToScreenPoint(new Vector3(box.min.x, box.min.y, box.center.z));
            Vector3 b = eye.WorldToScreenPoint(new Vector3(box.max.x, box.max.y, box.center.z));
            float xMin = Mathf.Min(a.x, b.x);
            float yMin = Mathf.Min(a.y, b.y);
            return new Rect(xMin, yMin, Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
        }

        /// <summary>
        /// What the panel found to dock to, said once. Docking is measured off
        /// another mod-less mod's furniture, so when it is wrong the only way to
        /// know why is to have it say what it saw -- and the only way to make that
        /// bearable is to say it once a session.
        /// </summary>
        private static bool explained;

        private static void Explain(string what)
        {
            if (explained)
            {
                return;
            }
            explained = true;
            Log.Info(what);
        }

        /// <summary>
        /// The camera the mapper is drawn by, found by the layer it is on rather
        /// than by name: the mapper is drawn in the world, and only the camera that
        /// renders that layer knows where on screen it ends up.
        /// </summary>
        private Camera MapperCamera(int layer)
        {
            if (mapperEye != null && mapperEye.isActiveAndEnabled
                && (mapperEye.cullingMask & (1 << layer)) != 0)
            {
                return mapperEye;
            }
            mapperEye = null;
            Camera[] all = Camera.allCameras;
            for (int i = 0; i < all.Length; i++)
            {
                if ((all[i].cullingMask & (1 << layer)) != 0
                    && (mapperEye == null || all[i].depth > mapperEye.depth))
                {
                    // The topmost camera drawing that layer: Besiege renders its
                    // interface last, over the level.
                    mapperEye = all[i];
                }
            }
            return mapperEye;
        }

        // ---- helpers ---------------------------------------------------------

        /// <summary>Places a rect from its parent's top-left, in UI Factory's units.</summary>
        protected static void Place(GameObject go, float x, float y, float w, float h)
        {
            RectTransform rect = go == null ? null : go.transform as RectTransform;
            if (rect == null)
            {
                return;
            }
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(w, h);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        protected Text Label(string text, float x, float y, float w, float h,
                             int size, TextAnchor align, Color ink)
        {
            GameObject go = UIF.Spawn(UIF.TextPrefab, host);
            if (go == null)
            {
                return null;
            }
            Place(go, x, y, w, h);
            Text label = go.GetComponent<Text>();
            if (label == null)
            {
                label = go.GetComponentInChildren<Text>(true);
            }
            if (label == null)
            {
                return null;
            }
            UIF.Untranslate(label);
            label.text = text;
            label.fontSize = size;
            label.resizeTextForBestFit = false;
            label.alignment = align;
            label.color = ink;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        /// <summary>
        /// Squares one of the prefab's own labels up to the box it is in: the
        /// padding it was authored with is for a box of the size UI Factory drew,
        /// and these are a row high.
        /// </summary>
        protected static Text Style(Text label, TextAnchor align, Color ink)
        {
            if (label == null)
            {
                return null;
            }
            RectTransform rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(4f, 0f);
            rect.offsetMax = new Vector2(-4f, 0f);
            label.alignment = align;
            label.color = ink;
            // Larger than the captions beside it: this is the text somebody types
            // and reads back, and the prefab's own size is small for that.
            label.fontSize = FieldFont;
            label.resizeTextForBestFit = false;
            // A field edits plain text, and a value that does not fit is better
            // read over the edge of its box than wrapped out of sight.
            label.supportRichText = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            UIF.EnsureFont(label);
            return label;
        }
    }
}
