using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OrchestraMod
{
    /// <summary>
    /// The block's settings, drawn in Besiege's own interface through UI Factory.
    ///
    /// One panel serves all nine blocks. It is built from whatever controls the
    /// block actually registered rather than from a list written here, which is
    /// what keeps a new instrument to XML alone: declare an Extra and a row for it
    /// appears.
    ///
    /// UI Factory is a **soft** dependency. Without it there is no panel and the
    /// stock mapper does the job; every mention of `Besiege.UI` is confined to
    /// <see cref="UIF"/> so that one guarded call decides whether this can exist.
    /// </summary>
    public class OrchestraPanel : MonoBehaviour
    {
        private const int CanvasOrder = 2400;
        /// <summary>What the panel is drawn at before a mapper has been measured.
        /// It ends up whatever Besiege's own window is wide, the two being docked
        /// edge to edge.</summary>
        private const float DefaultWidth = 434f;
        private const float Margin = 12f;
        private const float RowHeight = 26f;
        private const float RowGap = 4f;

        /// <summary>The column the captions are written in. Wide enough for the
        /// longest of them -- INSTRUMENT, and PIZZICATO among the extras -- and no
        /// wider, the gap between a name and its slider being the width of the
        /// whole window.</summary>
        private const float LabelWidth = 96f;
        private const float ValueWidth = 62f;

        /// <summary>The toggles are drawn half again as tall as a slider row. They
        /// are the only thing on their line, and they are what a hand goes for
        /// while the other hand is on the keys.</summary>
        private const float SwitchHeight = RowHeight * 1.5f;

        /// <summary>The instrument selector, which is narrower than the row it sits
        /// in: its arrows are anchored to its own ends, so the way to bring them in
        /// beside the name is to make the thing they are anchored to smaller. It is
        /// centred on the slider and its number together rather than started where
        /// the sliders start, being the odd row out.</summary>
        private const float TypeWidth = 250f;

        /// <summary>How wide the panel is drawn, which is how wide Besiege's mapper
        /// is: the two are one window with a seam.</summary>
        private float width = DefaultWidth;

        /// <summary>How far each arrow is pushed back off the name between them.
        /// Scaling them up grew them inwards, which closed the gap the prefab
        /// leaves; this opens it again without making them smaller.</summary>
        private const float ArrowGap = 9f;

        /// <summary>How much bigger the selector's arrows are drawn than the prefab
        /// draws them. Scaled rather than resized: they are anchored inside a
        /// control this panel did not lay out, and scale is the one change that
        /// cannot land them on top of the name between them.</summary>
        private const float ArrowScale = 1.2f;
        private const int TypeColumns = 2;

        private static readonly Vector2 Reference = new Vector2(1920f, 1080f);

        /// <summary>
        /// The panel gave up, so Besiege's own mapper is the only way to set a
        /// block and must keep its controls.
        ///
        /// Static because it is the blocks that act on it -- each one hides its
        /// sliders from the mapper while this panel is drawing them instead -- and
        /// there is one panel for all of them.
        /// </summary>
        public static bool Failed { get; private set; }

        private InstrumentBehaviour block;
        private bool hooked;
        private bool built;
        private bool failed;

        /// <summary>True while ReadFromBlock is writing, so the controls' own
        /// change events do not echo back into the block.</summary>
        private bool filling;

        private GameObject window;
        private RectTransform windowRect;

        /// <summary>Where the rows are put: the Window prefab's own scroll content,
        /// or the window itself if this UI Factory has none.</summary>
        private Transform host;

        /// <summary>The scroll content, when that is what <see cref="host"/> is.</summary>
        private RectTransform content;

        private ClickShield shield;
        /// <summary>The picture on the speaker button, lit while it sounds.</summary>
        private Image listenFace;

        /// <summary>The camera Besiege's mapper is drawn by, held so the layer
        /// search is not repeated every frame.</summary>
        private Camera mapperEye;

        private class Row
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

        private class Switch
        {
            public GameObject Button;
            public UnityEngine.UI.Toggle Control;
            public Text Caption;
            public MToggle Bound;
        }

        private readonly List<Row> rows = new List<Row>();
        private readonly List<Switch> switches = new List<Switch>();
        private GameObject typeOption;
        private int shownType = -1;

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

        // ---- lifetime --------------------------------------------------------

        private void Start()
        {
            Hook();
        }

        private void OnDestroy()
        {
            Unhook();
        }

        private void Hook()
        {
            if (hooked)
            {
                return;
            }
            try
            {
                BlockMapper.onMapperOpen += OnMapperOpen;
                BlockMapper.onMapperClose += OnMapperClose;
                hooked = true;
            }
            catch (Exception e)
            {
                Log.Warn("could not watch the block mapper, so no panel: " + e.Message);
            }
        }

        private void Unhook()
        {
            if (!hooked)
            {
                return;
            }
            try
            {
                BlockMapper.onMapperOpen -= OnMapperOpen;
                BlockMapper.onMapperClose -= OnMapperClose;
            }
            catch (Exception)
            {
                // Nothing useful to do while the game is being torn down.
            }
            hooked = false;
        }

        private void OnMapperOpen()
        {
            InstrumentBehaviour opened = null;
            try
            {
                BlockMapper mapper = BlockMapper.CurrentInstance;
                if (mapper != null && mapper.Block != null)
                {
                    opened = mapper.Block.GetComponent<InstrumentBehaviour>();
                }
            }
            catch (Exception e)
            {
                Log.Warn("could not tell which block the mapper opened on: " + e.Message);
            }

            if (opened == null)
            {
                Hide();
                return;
            }
            Show(opened);
        }

        private void OnMapperClose()
        {
            Hide();
        }

        private void Show(InstrumentBehaviour on)
        {
            block = on;
            // Before building, not after: the rows are laid out to a width, and a
            // window built to the wrong one would be seen at it for a frame and
            // rebuilt the next time the mapper opened.
            Measure();
            if (!Build())
            {
                return;
            }
            window.SetActive(true);
            ReadFromBlock();
            // Against the mapper before the first frame is drawn, or the panel is
            // seen in the middle of the screen on its way to the join.
            Canvas.ForceUpdateCanvases();
            Dock();
        }

        private void Hide()
        {
            if (block != null)
            {
                // Leaving a note ringing behind a closed panel is not something the
                // player has any way to stop.
                block.StopAudition();
            }
            block = null;
            CommitPending();
            if (window != null)
            {
                window.SetActive(false);
            }
        }

        // ---- building --------------------------------------------------------

        /// <summary>
        /// Builds the window once, for the block the mapper first opened on.
        ///
        /// Rebuilt when the block that opens has a different set of controls,
        /// because the rows *are* that set: a Cymbals panel has no Sustain row to
        /// rebind to a Piano's.
        /// </summary>
        private bool Build()
        {
            if (failed || !UIF.Available)
            {
                return false;
            }
            if (built && SameShape())
            {
                Rebind();
                return true;
            }

            try
            {
                Teardown();
                BuildWindow();
                built = true;
            }
            catch (Exception e)
            {
                Log.Warn("could not build the panel, so the stock mapper stands: " + e.Message);
                failed = true;
                Failed = true;
                Teardown();
            }
            return built;
        }

        /// <summary>
        /// Whether the built window still fits the block being opened. Counting the
        /// controls is enough: two blocks with the same shape have the same rows in
        /// the same order, because both were built from the same walk.
        /// </summary>
        private bool SameShape()
        {
            return rows.Count == 3 + block.ExtraSliders.Count
                && switches.Count == (block.Latch == null ? 0 : 1) + block.ExtraToggles.Count;
        }

        private void Teardown()
        {
            rows.Clear();
            switches.Clear();
            typeOption = null;
            host = null;
            content = null;
            listenFace = null;
            if (window != null)
            {
                Destroy(window);
                window = null;
            }
            built = false;
        }

        private void BuildWindow()
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
                // UI Factory authors against 1920x1080 and matches on height;
                // anything else draws Besiege's own widgets at the wrong size
                // beside the game's.
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
            window.name = "Orchestra panel";

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
            float barHeight = 0f;

            // The prefab's scroll view starts below the bar that is no longer there,
            // so it is stretched over the whole window: otherwise the panel opens
            // with a bar's worth of empty frame above its first row.
            ScrollRect scroll = window.GetComponentInChildren<ScrollRect>(true);
            RectTransform view = scroll == null ? null : scroll.transform as RectTransform;
            if (view != null)
            {
                view.anchorMin = new Vector2(0f, 0f);
                view.anchorMax = new Vector2(1f, 1f);
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
            // instead left that scroll view holding the prefab's own 500-unit
            // placeholder, taller than any panel -- and a scroll view whose contents
            // do not fit shows a scrollbar, which is the one this panel had. Besiege's
            // scroll view hides both bars for contents that fit, so filling it
            // properly is also what takes the bar away.
            //
            // The window itself already carries StopsZoomWhenHovered, so the wheel
            // over the panel does not zoom the level.
            content = ScrollContent();
            host = content != null ? (Transform)content : window.transform;

            // Inside the content the rows start at the top; on the bare window they
            // have to clear the title bar themselves.
            float y = content != null ? Margin : barHeight + Margin;
            y = BuildTypes(y);
            y = BuildSliders(y);
            y = BuildSwitches(y);
            // A short foot: the row below the sliders is buttons, which are tall
            // enough to sit against the frame without looking crowded.
            y += RowGap;

            if (content != null)
            {
                content.sizeDelta = new Vector2(content.sizeDelta.x, y);
                windowRect.sizeDelta = new Vector2(width, barHeight + y);
            }
            else
            {
                windowRect.sizeDelta = new Vector2(width, y);
            }

            // So the canvas has a size before the window is placed against it, and
            // the scroll view has measured what it now holds.
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>
        /// The Window prefab's own scroll content, which is what its rows belong in.
        /// Null if this UI Factory's window has no scroll view, in which case they go
        /// straight on the window and nothing is lost but the scrolling.
        /// </summary>
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

        /// <summary>
        /// The speaker at the foot of the panel: plays the block's note as it is
        /// set, so an instrument can be chosen by ear while the machine is being
        /// built.
        ///
        /// UI Factory's Icon Button, the control Besiege's own window corners are
        /// made of, drawn square at the height of the toggles it sits beside. The
        /// picture is drawn rather than asked for: UI Factory's sprite set cannot be
        /// listed, so naming a speaker in it would be a guess.
        /// </summary>
        private void BuildListen(float x, float y, float side)
        {
            GameObject button = UIF.Spawn(UIF.IconButtonPrefab, host);
            if (button == null)
            {
                return;
            }
            button.name = "Listen";
            Place(button, x, y, side, side);
            UIF.NoSwell(button);

            Transform icon = button.transform.FindChild("Icon");
            listenFace = icon == null ? null : icon.GetComponent<Image>();
            if (listenFace == null)
            {
                listenFace = button.GetComponentInChildren<Image>(true);
            }
            if (listenFace != null)
            {
                listenFace.sprite = IconArt.Speaker();
                listenFace.color = UIF.QuietInk;
                // The drawing is inset within its own square, so the picture is the
                // right size while the whole button stays the thing that is clicked.
                listenFace.preserveAspect = false;
            }

            Button click = button.GetComponent<Button>();
            if (click != null)
            {
                click.onClick.AddListener(Listen);
            }
        }

        private float BuildTypes(float y)
        {
            Label("INSTRUMENT", Margin, y, LabelWidth, RowHeight, 13,
                  TextAnchor.MiddleLeft, UIF.QuietInk);

            typeOption = UIF.Spawn(UIF.OptionPrefab, host);
            if (typeOption == null)
            {
                return y + RowHeight + RowGap;
            }
            // The middle of the slider column and the number beside it, which is
            // what the selector is centred on.
            float span = width - Margin * 2f - LabelWidth;
            Place(typeOption, Margin + LabelWidth + (span - TypeWidth) / 2f, y,
                  TypeWidth, RowHeight);
            Enlarge(typeOption, "Previous", -ArrowGap);
            Enlarge(typeOption, "Next", ArrowGap);

            List<string> choices = new List<string>();
            for (int i = 0; i < block.TypeCount; i++)
            {
                choices.Add(block.TypeName(i));
            }
            UIF.SetOption(typeOption, choices, block.SelectedType);
            return y + RowHeight + RowGap * 2f;
        }

        /// <summary>
        /// Draws one of the selector's arrows a little bigger than UI Factory does.
        /// Named children rather than a search for buttons: the prefab calls them
        /// Previous and Next, and a control it renamed should go back to its own
        /// size rather than to this panel's guess at which button is which.
        /// </summary>
        private static void Enlarge(GameObject option, string child, float shift)
        {
            Transform arrow = option.transform.FindChild(child);
            if (arrow == null)
            {
                return;
            }
            arrow.localScale = new Vector3(ArrowScale, ArrowScale, 1f);
            RectTransform rect = arrow as RectTransform;
            if (rect != null)
            {
                // Away from the middle, whichever end this one is anchored to:
                // anchoredPosition is measured from its own anchor, so the sign is
                // the caller's to decide and not this method's to work out.
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x + shift,
                                                    rect.anchoredPosition.y);
            }
        }

        /// <summary>
        /// Every row's caption is its control's own name, the fixed three included,
        /// because that is what lets <see cref="ReadFromBlock"/> write them all again
        /// from the block when a window is reused for another instrument.
        /// </summary>
        private float BuildSliders(float y)
        {
            y = AddSlider(y, Caption(block.Note), block.Note, true);
            y = AddSlider(y, Caption(block.Volume), block.Volume, false);
            y = AddSlider(y, Caption(block.Range), block.Range, false);
            for (int i = 0; i < block.ExtraSliders.Count; i++)
            {
                y = AddSlider(y, Caption(block.ExtraSliders[i]), block.ExtraSliders[i], false);
            }
            return y + RowGap;
        }

        /// <summary>
        /// The part of a slider's range the handle runs through, which is not always
        /// the whole of what the setting accepts: RANGE will take any distance a
        /// level is wide, and a handle that had to cover all of it would be useless
        /// for the fifty metres anybody actually wants. Typing is not limited to
        /// this -- <see cref="Typed"/> clamps to the setting's own bounds -- and a
        /// value beyond it parks the handle at that end.
        /// </summary>
        private void Span(MSlider bound, out float min, out float max)
        {
            min = bound.Min;
            max = bound.Max;
            if (block == null)
            {
                return;
            }
            float low, high;
            if (block.Comfortable(bound, out low, out high))
            {
                min = low;
                max = high;
            }
        }

        /// <summary>What a control is called, as the panel writes it.</summary>
        private static string Caption(MapperType control)
        {
            return control == null ? "" : control.DisplayName.ToUpper();
        }

        private float AddSlider(float y, string caption, MSlider bound, bool isNote)
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

        /// <summary>
        /// Squares one of the prefab's own labels up to the box it is in: the padding
        /// it was authored with is for a box of the size UI Factory drew, and these
        /// are a row high and hold six characters.
        /// </summary>
        private static Text Style(Text label, TextAnchor align, Color ink)
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
            label.fontSize = 13;
            label.resizeTextForBestFit = false;
            // A field edits plain text, and a number that does not fit is better read
            // over the edge of its box than wrapped out of sight.
            label.supportRichText = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        /// <summary>The block's own toggles, two to a row, lit when on.</summary>
        private float BuildSwitches(float y)
        {
            List<MToggle> all = new List<MToggle>();
            if (block.Latch != null)
            {
                // A struck instrument has no Toggle: nothing to hold. The row is
                // not built rather than built dead, so the window is the shape of
                // what the block can actually do.
                all.Add(block.Latch);
            }
            for (int i = 0; i < block.ExtraToggles.Count; i++)
            {
                all.Add(block.ExtraToggles[i]);
            }

            // LISTEN sits at the left of the row and the toggles share what is left
            // of it, equally, however many there are. One line, because this is the
            // foot of the panel: the button belongs at a corner and the toggles
            // belong beside it.
            float listen = SwitchHeight;
            BuildListen(Margin, y, listen);
            float rest = width - Margin * 2f - listen - RowGap;
            float cell = all.Count > 0
                ? (rest - RowGap * (all.Count - 1)) / all.Count : rest;
            float left = Margin + listen + RowGap;
            for (int i = 0; i < all.Count; i++)
            {
                float top = y;
                // UI Factory's Text Toggle is Besiege's own, so the tick and its
                // states come from the game rather than being painted here.
                GameObject go = UIF.Spawn(UIF.TogglePrefab, host);
                if (go == null)
                {
                    continue;
                }
                Place(go, left + i * (cell + RowGap), top, cell, SwitchHeight);
                UIF.NoSwell(go);

                Switch item = new Switch();
                item.Button = go;
                item.Control = go.GetComponent<UnityEngine.UI.Toggle>();
                if (item.Control == null)
                {
                    item.Control = go.GetComponentInChildren<UnityEngine.UI.Toggle>(true);
                }
                item.Caption = go.GetComponentInChildren<Text>(true);
                item.Bound = all[i];
                if (item.Caption != null)
                {
                    UIF.Untranslate(item.Caption);
                }
                if (item.Control != null)
                {
                    Switch captured = item;
                    item.Control.onValueChanged.AddListener(
                        delegate(bool on) { Flip(captured, on); });
                }
                switches.Add(item);
            }
            // No closing gap of its own: the extra height the toggles took came out
            // of the space that was under them, so the window is the height it was.
            return y + SwitchHeight + RowGap;
        }

        // ---- binding and reading --------------------------------------------

        private void Rebind()
        {
            int i = 0;
            rows[i++].Bound = block.Note;
            rows[i++].Bound = block.Volume;
            rows[i++].Bound = block.Range;
            for (int e = 0; e < block.ExtraSliders.Count && i < rows.Count; e++)
            {
                rows[i++].Bound = block.ExtraSliders[e];
            }

            int s = 0;
            if (block.Latch != null && s < switches.Count)
            {
                switches[s++].Bound = block.Latch;
            }
            for (int e = 0; e < block.ExtraToggles.Count && s < switches.Count; e++)
            {
                switches[s++].Bound = block.ExtraToggles[e];
            }
        }

        /// <summary>Pulls every control's state out of the block and shows it.</summary>
        private void ReadFromBlock()
        {
            if (!built || block == null)
            {
                return;
            }
            filling = true;

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                if (row.Bound == null)
                {
                    continue;
                }
                if (row.Caption != null)
                {
                    // Written every time, not once when the row was made: a window
                    // is kept for the next block with the same shape, and that block
                    // calls its extras something else -- which is how a piano came to
                    // have a PALM MUTE where its SUSTAIN is.
                    row.Caption.text = Caption(row.Bound);
                }
                if (row.Control != null)
                {
                    float low, high;
                    Span(row.Bound, out low, out high);
                    row.Control.minValue = low;
                    row.Control.maxValue = high;
                    row.Control.value = row.Bound.Value;
                }
                Write(row);
            }

            for (int i = 0; i < switches.Count; i++)
            {
                Paint(switches[i]);
            }

            shownType = block.SelectedType;
            List<string> choices = new List<string>();
            for (int i = 0; i < block.TypeCount; i++)
            {
                choices.Add(block.TypeName(i));
            }
            UIF.SetOption(typeOption, choices, shownType);

            ShowListen();
            filling = false;
        }

        private void Write(Row row)
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
            if (block != null && row.Bound != null && !filling)
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

        private void Paint(Switch item)
        {
            if (item == null || item.Bound == null)
            {
                return;
            }
            if (item.Control != null)
            {
                item.Control.isOn = item.Bound.IsActive;
            }
            if (item.Caption != null)
            {
                // Rewritten rather than set once, for the reason in ReadFromBlock.
                item.Caption.text = Caption(item.Bound);
                item.Caption.color = item.Bound.IsActive ? Color.white : UIF.QuietInk;
            }
        }

        // ---- input -----------------------------------------------------------

        private void Dragged(Row row, float value)
        {
            if (block == null || row.Bound == null || filling)
            {
                return;
            }
            row.Bound.Value = value;
            Write(row);
            Queue(row.Bound);
        }

        private void Flip(Switch item, bool on)
        {
            // Also raised while the panel is filling itself in, which would write
            // the block's own value straight back at it and queue a needless commit.
            if (block == null || item.Bound == null || filling || item.Bound.IsActive == on)
            {
                return;
            }
            item.Bound.IsActive = on;
            Paint(item);
            Queue(item.Bound);
        }

        /// <summary>
        /// Notices the player working the Option selector.
        ///
        /// Polled rather than subscribed: `onValueChanged` is UI Factory's own event
        /// type, and reading one integer while the panel is open is cheaper than
        /// binding to a signature that could change beneath the mod.
        /// </summary>
        private void WatchType()
        {
            int index = UIF.OptionIndex(typeOption);
            if (index < 0 || index == shownType || index >= block.TypeCount)
            {
                return;
            }
            shownType = index;
            block.Types.Value = index;
            Queue(block.Types);
        }

        /// <summary>The speaker: audition the block, and light the button while it
        /// is sounding.</summary>
        private void Listen()
        {
            if (block == null)
            {
                return;
            }
            block.Audition();
            ShowListen();
        }

        private void ShowListen()
        {
            if (listenFace == null)
            {
                return;
            }
            bool on = block != null && block.IsAuditioning;
            listenFace.color = on ? Color.white : UIF.QuietInk;
        }

        private void Queue(MapperType changed)
        {
            if (changed != null && !pending.Contains(changed))
            {
                pending.Add(changed);
            }
        }

        /// <summary>
        /// Docking runs here rather than in Update: the mapper is dragged by its own
        /// behaviour, and a panel placed before it has moved is a panel one frame
        /// behind it -- which reads as the join coming apart while it is dragged.
        /// </summary>
        private void LateUpdate()
        {
            if (block != null && built)
            {
                Dock();
            }
        }

        private void Update()
        {
            if (block != null && built)
            {
                WatchType();
                // The audition ends on its own, so the light on the button is polled
                // rather than switched off by whatever ended it.
                ShowListen();
            }

            // Committed when the mouse comes up rather than on every change: each
            // commit reserialises the block and adds an undo entry, which during a
            // drag would be one per frame.
            if (pending.Count > 0 && !Input.GetMouseButton(0))
            {
                CommitPending();
            }
        }

        private void CommitPending()
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
        private void Commit(MapperType changed)
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

        // ---- where the window sits -------------------------------------------

        /// <summary>
        /// Puts the panel against the bottom edge of Besiege's own mapper, the same
        /// width as it, so the two read as one window with a seam.
        ///
        /// The mapper is NGUI in world space -- its widgets are not on any canvas
        /// this could be parented into -- so the join is made by measuring: the
        /// mapper publishes `upperLeft` and `lowerRight`, and where those two land
        /// on screen is where its frame is. Every frame, because the mapper is
        /// draggable and the panel has to go with it.
        /// </summary>
        private void Dock()
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

            float scale = Scale();
            float wide = frame.width * scale;
            if (Mathf.Abs(wide - width) > 0.5f)
            {
                // The rows are laid out to a width, so a mapper of a different one
                // is a rebuild. Done here and now rather than left for the next
                // open: the window has to be *somewhere* this frame, and a panel
                // that returns without placing itself is a panel that never docks
                // and never follows -- which is exactly what happened.
                width = wide;
                built = false;
                if (!Build())
                {
                    return;
                }
                ReadFromBlock();
                Canvas.ForceUpdateCanvases();
            }

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
        private float Scale()
        {
            return Screen.height > 0 ? Reference.y / Screen.height : 1f;
        }

        /// <summary>
        /// Takes the panel's width from the mapper's, before the rows are laid out
        /// to it. Returns whether it changed.
        /// </summary>
        private bool Measure()
        {
            Rect frame;
            if (!MapperFrame(out frame))
            {
                return false;
            }
            float wide = frame.width * Scale();
            if (Mathf.Abs(wide - width) <= 0.5f)
            {
                return false;
            }
            width = wide;
            built = false;
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
        ///     Background   874.80 x 389.88   at y 1540.87   <- the window
        ///     Background   874.80 x 281.88   at y 1540.87
        ///     Background   874.80 x 174.96   at y 1658.59
        ///     WideShadow   972.00 x 194.40   at y 1638.37   <- 11% wider, and higher
        ///     Mask         874.80 x 1555.20  at y  267.55   <- the scroll region
        ///     Visual        93.31 x  93.31                  <- a button
        ///
        /// So: the window is a `Background`, all of which are its width, and the
        /// tallest of them is the frame -- the others are sections inside it. Taking
        /// the widest thing drawn lands on `WideShadow`, which is how the panel came
        /// to be an eleventh wider than the mapper and to sit over its lower half;
        /// taking `Visual` by name lands on a 93-pixel button, which is how it came
        /// to be a narrow strip. Both were shipped, and both are in the log above.
        /// </summary>
        private bool MapperFrame(out Rect frame)
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
        /// than by name: NGUI puts its interface in the world, and only the camera
        /// that renders that layer knows where on screen it ends up.
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
        private void Place(GameObject go, float x, float y, float w, float h)
        {
            RectTransform rect = go.transform as RectTransform;
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

        private Text Label(string text, float x, float y, float w, float h,
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
    }
}
