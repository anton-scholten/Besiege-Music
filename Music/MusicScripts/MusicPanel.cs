using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MusicMod
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
    public class MusicPanel : DockedPanel
    {
        /// <summary>The toggles are drawn half again as tall as a slider row. They
        /// are the only thing on their line, and they are what a hand goes for
        /// while the other hand is on the keys.</summary>
        private const float SwitchHeight = RowHeight * 1.5f;

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

        /// <summary>The picture on the speaker button, lit while it sounds.</summary>
        private Image listenFace;

        private class Switch
        {
            public GameObject Button;
            public UnityEngine.UI.Toggle Control;
            public Text Caption;
            public MToggle Bound;
        }

        private readonly List<Switch> switches = new List<Switch>();
        private Chooser typeOption;
        private int shownType = -1;

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
            // window built to the wrong one would be seen at it for a frame.
            Rect frame;
            if (MapperFrame(out frame) && Widen(frame))
            {
                built = false;          // the rows are laid out to a width
            }
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
            DestroyWindow();
            built = false;
        }

        private void BuildWindow()
        {
            OpenWindow("Music panel");

            float y = Margin;
            y = BuildTypes(y);
            y = BuildSliders(y);
            y = BuildSwitches(y);
            // A short foot: the row below the sliders is buttons, which are tall
            // enough to sit against the frame without looking crowded.
            y += RowGap;
            CloseWindow(y);
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
            // The prefab's own swell is left on: this is a button the size of a
            // button, and growing 15% under the pointer is what the rest of
            // Besiege's interface does. It comes off the toggles beside it, where
            // growing a full-width row carries its lettering out of the window.

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
            typeOption = AddSelector("INSTRUMENT", y, Choices(), block.SelectedType);
            return y + RowHeight + RowGap * 2f;
        }

        /// <summary>The instruments this block holds.</summary>
        private List<string> Choices()
        {
            List<string> choices = new List<string>();
            for (int i = 0; i < block.TypeCount; i++)
            {
                choices.Add(block.TypeName(i));
            }
            return choices;
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

        /// <summary>What a control is called, as the panel writes it.</summary>
        private static string Caption(MapperType control)
        {
            return control == null ? "" : control.DisplayName.ToUpper();
        }

        /// <summary>
        /// The part of a slider's range this block's handle runs through. RANGE
        /// will take any distance a level is wide, and a handle that had to cover
        /// all of it would be useless for the fifty metres anybody wants, so the
        /// block says what is worth dragging through and the box takes the rest.
        /// </summary>
        protected override void Span(MSlider bound, out float min, out float max)
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
                    // The same size as the text boxes: a toggle's word is read at a
                    // glance across the panel, and the prefab's own size is small
                    // for that.
                    item.Caption.fontSize = FieldFont;
                    item.Caption.resizeTextForBestFit = false;
                    UIF.EnsureFont(item.Caption);
                    // The lettering grows under the pointer, and the row does not:
                    // the same answer the selectors give, and the reason the
                    // prefab's own swell came off above -- that one grows the whole
                    // toggle, which on a full-width row carries its words out of
                    // the window.
                    Swell swell = go.AddComponent<Swell>();
                    swell.grows = item.Caption.transform;
                    swell.grown = 1.15f;
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
            if (typeOption != null)
            {
                typeOption.Set(Choices(), shownType);
            }

            ShowListen();
            filling = false;
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
            int index = typeOption == null ? -1 : typeOption.Index;
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
            SettleDrags();
        }

        /// <summary>
        /// The mapper changed width under the panel, so the rows have to be laid
        /// out again before it can be placed against the new one.
        /// </summary>
        protected override bool Rebuild()
        {
            built = false;
            if (!Build())
            {
                return false;
            }
            ReadFromBlock();
            return true;
        }
    }
}
