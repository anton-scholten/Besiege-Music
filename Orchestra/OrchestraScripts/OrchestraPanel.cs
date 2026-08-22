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
        private const float Width = 470f;
        private const float Margin = 12f;
        private const float RowHeight = 26f;
        private const float RowGap = 4f;
        private const float LabelWidth = 132f;
        private const float ValueWidth = 62f;
        private const int TypeColumns = 2;

        private static readonly Vector2 Reference = new Vector2(1920f, 1080f);

        private InstrumentBehaviour block;
        private bool hooked;
        private bool built;
        private bool failed;

        /// <summary>True while ReadFromBlock is writing, so the controls' own
        /// change events do not echo back into the block.</summary>
        private bool filling;

        private GameObject window;
        private RectTransform windowRect;
        private ClickShield shield;
        private Text title;

        private class Row
        {
            public UnityEngine.UI.Slider Control;
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
        private Text keyLine;

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
            if (!Build())
            {
                return;
            }
            window.SetActive(true);
            ReadFromBlock();
        }

        private void Hide()
        {
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
                && switches.Count == 1 + block.ExtraToggles.Count;
        }

        private void Teardown()
        {
            rows.Clear();
            switches.Clear();
            typeOption = null;
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

            RectTransform bar = window.transform.FindChild("TopBar") as RectTransform;
            float y = Margin;
            if (bar != null)
            {
                title = bar.GetComponentInChildren<Text>(true);
                if (title != null)
                {
                    UIF.Untranslate(title);
                    title.alignment = TextAnchor.MiddleCenter;
                    title.raycastTarget = false;
                }
                Transform close = bar.FindChild("CloseButton");
                if (close != null)
                {
                    Button button = close.GetComponent<Button>();
                    if (button != null)
                    {
                        // The mapper owns this window's life, so its cross closes
                        // the mapper rather than orphaning the panel.
                        button.onClick.AddListener(CloseMapper);
                    }
                }
                y = bar.rect.height + Margin;
            }

            shield = gameObject.GetComponent<ClickShield>();
            if (shield == null)
            {
                shield = gameObject.AddComponent<ClickShield>();
            }
            shield.Guard(windowRect);

            // Besiege zooms the level on the mouse wheel, and a panel that let it
            // through would zoom the world whenever you meant to scroll the panel.
            // UI Factory's own behaviour, which knows the game's zoom.
            UIF.StopZoom(window);

            y = BuildTypes(y);
            y = BuildSliders(y);
            y = BuildSwitches(y);
            y = BuildKeyLine(y);

            windowRect.sizeDelta = new Vector2(Width, y + Margin);
        }

        /// <summary>
        /// The instrument choice, as Besiege's own `&lt; Grand piano &gt;` selector.
        ///
        /// UI Factory ships the game's Option widget, which is the same control the
        /// block mapper uses for a menu -- so this reads as part of Besiege rather
        /// than as a row of buttons that resemble it, and takes one line instead of
        /// three.
        /// </summary>
        private float BuildTypes(float y)
        {
            Label("INSTRUMENT", Margin, y, LabelWidth, RowHeight, 13,
                  TextAnchor.MiddleLeft, UIF.QuietInk);

            typeOption = UIF.Spawn(UIF.OptionPrefab, window.transform);
            if (typeOption == null)
            {
                return y + RowHeight + RowGap;
            }
            Place(typeOption, Margin + LabelWidth, y,
                  Width - Margin * 2f - LabelWidth, RowHeight);

            List<string> choices = new List<string>();
            for (int i = 0; i < block.TypeCount; i++)
            {
                choices.Add(block.TypeName(i));
            }
            UIF.SetOption(typeOption, choices, block.SelectedType);
            return y + RowHeight + RowGap * 2f;
        }

        private float BuildSliders(float y)
        {
            y = AddSlider(y, "NOTE", block.Note, true);
            y = AddSlider(y, "VOLUME", block.Volume, false);
            y = AddSlider(y, "RANGE", block.Range, false);
            for (int i = 0; i < block.ExtraSliders.Count; i++)
            {
                MSlider s = block.ExtraSliders[i];
                y = AddSlider(y, s.DisplayName.ToUpper(), s, false);
            }
            return y + RowGap;
        }

        private float AddSlider(float y, string caption, MSlider bound, bool isNote)
        {
            Label(caption, Margin, y, LabelWidth, RowHeight, 13, TextAnchor.MiddleLeft, UIF.QuietInk);

            GameObject go = UIF.Spawn(UIF.SliderPrefab, window.transform);
            if (go == null)
            {
                return y + RowHeight + RowGap;
            }
            float x = Margin + LabelWidth;
            float w = Width - Margin * 2f - LabelWidth - ValueWidth - RowGap;
            Place(go, x, y, w, RowHeight);

            UnityEngine.UI.Slider control = go.GetComponent<UnityEngine.UI.Slider>();
            if (control == null)
            {
                control = go.GetComponentInChildren<UnityEngine.UI.Slider>(true);
            }
            Row row = new Row();
            row.Control = control;
            row.Bound = bound;
            row.Note = isNote;
            row.Value = Label("", Width - Margin - ValueWidth, y, ValueWidth, RowHeight,
                              13, TextAnchor.MiddleRight, Color.white);
            if (control != null)
            {
                control.minValue = bound.Min;
                control.maxValue = bound.Max;
                // Notes snap: dragged freely a block lands a quarter-tone sharp and
                // is unplayable beside another.
                control.wholeNumbers = isNote;
                Row captured = row;
                control.onValueChanged.AddListener(delegate(float v) { Dragged(captured, v); });
            }
            rows.Add(row);
            return y + RowHeight + RowGap;
        }

        /// <summary>The block's own toggles, two to a row, lit when on.</summary>
        private float BuildSwitches(float y)
        {
            List<MToggle> all = new List<MToggle>();
            all.Add(block.Latch);
            for (int i = 0; i < block.ExtraToggles.Count; i++)
            {
                all.Add(block.ExtraToggles[i]);
            }

            float cell = (Width - Margin * 2f - RowGap) / TypeColumns;
            for (int i = 0; i < all.Count; i++)
            {
                int column = i % TypeColumns;
                int line = i / TypeColumns;
                // UI Factory's Text Toggle is Besiege's own, so the tick and its
                // states come from the game rather than being painted here.
                GameObject go = UIF.Spawn(UIF.TogglePrefab, window.transform);
                if (go == null)
                {
                    continue;
                }
                Place(go, Margin + column * (cell + RowGap), y + line * (RowHeight + RowGap),
                      cell, RowHeight);
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
                    item.Caption.text = all[i].DisplayName.ToUpper();
                }
                if (item.Control != null)
                {
                    Switch captured = item;
                    item.Control.onValueChanged.AddListener(
                        delegate(bool on) { Flip(captured, on); });
                }
                switches.Add(item);
            }
            int lines = (all.Count + TypeColumns - 1) / TypeColumns;
            return y + lines * (RowHeight + RowGap) + RowGap;
        }

        /// <summary>
        /// The key, shown but not editable. Rebinding needs Besiege's own capture,
        /// which lives in the mapper's KeySelector -- and the mapper is open behind
        /// this window, so the place to change it is right there.
        /// </summary>
        private float BuildKeyLine(float y)
        {
            keyLine = Label("", Margin, y, Width - Margin * 2f, RowHeight, 12,
                            TextAnchor.MiddleLeft, UIF.QuietInk);
            return y + RowHeight;
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
            switches[s++].Bound = block.Latch;
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

            if (title != null)
            {
                title.text = block.SelectedTypeName.ToUpper();
            }

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                if (row.Bound == null)
                {
                    continue;
                }
                if (row.Control != null)
                {
                    row.Control.minValue = row.Bound.Min;
                    row.Control.maxValue = row.Bound.Max;
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

            if (keyLine != null)
            {
                keyLine.text = "PLAY  " + block.KeyDescription
                             + "   —  rebind in the mapper behind";
            }
            filling = false;
        }

        private void Write(Row row)
        {
            if (row.Value == null || row.Bound == null)
            {
                return;
            }
            row.Value.text = row.Note
                ? NoteName(Mathf.RoundToInt(row.Bound.Value))
                : row.Bound.Value.ToString("0.00");
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
            if (title != null)
            {
                title.text = block.SelectedTypeName.ToUpper();
            }
        }

        private void Queue(MapperType changed)
        {
            if (changed != null && !pending.Contains(changed))
            {
                pending.Add(changed);
            }
        }

        private void Update()
        {
            if (block != null && built)
            {
                WatchType();
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

        private void CloseMapper()
        {
            try
            {
                BlockMapper.Close();
            }
            catch (Exception)
            {
                Hide();
            }
        }

        // ---- helpers ---------------------------------------------------------

        /// <summary>Places a rect from the window's top-left, in UI Factory's units.</summary>
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
            GameObject go = UIF.Spawn(UIF.TextPrefab, window.transform);
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
