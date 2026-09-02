using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using MusicMod;

namespace BraidsSynth
{
    /// <summary>
    /// The synth block's own panel, built out of UI Factory's prefabs so it looks
    /// like part of Besiege rather than like a mod.
    ///
    /// It exists because the block mapper cannot say what this block does. A
    /// macro-oscillator is twenty-three models whose two controls mean something
    /// different under each of them, and a stack of sliders called TIMBRE and COLOR
    /// tells you none of that. The panel names the models, says what the controls do
    /// in the one that is chosen, draws the wave coming out, and will play it while
    /// the machine is still being built -- so a model can be chosen by ear.
    ///
    /// The furniture is <see cref="MusicMod.DockedPanel"/>'s, the same an instrument
    /// block's panel is made of: this one says which mapper control each row stands
    /// for and adds the two things only it has -- the trace, and the pair of lines
    /// saying what TIMBRE and COLOR mean under the model in force. It carried its
    /// own copy of the rest while it was a mod of its own, and the two drifted.
    ///
    /// UI Factory is a soft dependency. Everything here goes through
    /// <see cref="UIF"/>, and if it is not installed the panel never appears; the
    /// block keeps its ordinary mapper, which is what it saves through either way.
    ///
    /// The panel opens with the block mapper and closes with it, which is Besiege's
    /// own idea of when a block's settings are being looked at.
    /// </summary>
    public class BraidsPanel : DockedPanel
    {
        private const float ScopeHeight = 96f;

        /// <summary>How often the trace is redrawn. Fast enough to look live.</summary>
        private const float ScopeInterval = 0.05f;

        private const int MeaningFontSize = 15;

        /// <summary>Lettering for a control the chosen model makes no use of: the
        /// only grey left in the panel, and grey for a reason.</summary>
        private static readonly Color Idle = new Color(0.45f, 0.45f, 0.48f, 1f);

        private BraidsBehaviour block;
        private bool hooked;
        private bool built;
        private bool failed;

        private Scope scope;
        private float[] samples;
        private float nextScope;

        private Chooser modelPicker;

        /// <summary>The selector's list, built once; it never changes.</summary>
        private readonly List<string> modelNames = BraidsModels.MenuItems();
        private int shownModel = -1;

        private Text timbreMeaning;
        private Text colourMeaning;

        /// <summary>The two rows the model can render meaningless, held so they can
        /// be dimmed when it does.</summary>
        private Row timbre;
        private Row colour;

        // ---- lifetime ----------------------------------------------------------

        private void Start()
        {
            Hook();
        }

        private void OnDestroy()
        {
            Unhook();
            if (scope != null)
            {
                scope.Dispose();
                scope = null;
            }
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
                Log.Warn("could not watch the block mapper, so the panel will not open: "
                         + e.Message);
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
            BraidsBehaviour opened = null;
            try
            {
                BlockMapper mapper = BlockMapper.CurrentInstance;
                if (mapper != null && mapper.Block != null)
                {
                    opened = mapper.Block.GetComponent<BraidsBehaviour>();
                }
            }
            catch (Exception e)
            {
                Log.Warn("could not tell which block the mapper opened on: " + e.Message);
            }

            if (opened == null || opened.IsSimulating)
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

        private void Show(BraidsBehaviour on)
        {
            block = on;
            // Before building, not after: the rows are laid out to a width, and a
            // window built to the wrong one would be seen at it for a frame.
            Rect frame;
            if (MapperFrame(out frame) && Widen(frame))
            {
                built = false;
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
                block.SetPreview(false);
            }
            block = null;
            CommitPending();
            if (window != null)
            {
                // The selector's open list hangs off the canvas rather than the
                // window, and follows it down through the Chooser's own OnDisable.
                window.SetActive(false);
            }
        }

        // ---- building ----------------------------------------------------------

        /// <summary>
        /// Builds the window once. Unlike the instrument panel there is no second
        /// shape to fit: every synth block carries the same controls, so a built
        /// window is only ever rebound to the block that opened, and rebuilt when
        /// the mapper changes width under it.
        /// </summary>
        private bool Build()
        {
            if (failed)
            {
                return false;
            }
            if (!UIF.Available)
            {
                Log.Info("UI Factory 3 is not available, so the synth block uses Besiege's "
                         + "own mapper. Subscribe to Workshop item 2913469777 for the panel.");
                failed = true;
                return false;
            }
            if (built)
            {
                Rebind();
                return true;
            }

            try
            {
                // Every rebuild starts from nothing. Without this the width change
                // that triggers one spawns a second Window prefab and leaves the
                // first parented to the canvas, active, docked to nothing -- a
                // stray panel loose on the screen.
                Teardown();
                BuildWindow();
                built = true;
            }
            catch (Exception e)
            {
                Log.Warn("could not build the synth panel, so the stock mapper stands: "
                         + e.Message);
                failed = true;
                Teardown();
            }
            return built;
        }

        /// <summary>Drops the built window and everything that pointed into it.
        /// Called before a rebuild and when the panel gives up.</summary>
        private void Teardown()
        {
            modelPicker = null;
            timbreMeaning = null;
            colourMeaning = null;
            timbre = null;
            colour = null;
            shownModel = -1;
            if (scope != null)
            {
                scope.Dispose();
                scope = null;
            }
            host = null;
            content = null;
            DestroyWindow();
            built = false;
        }

        private void BuildWindow()
        {
            OpenWindow("Braids panel");

            float y = Margin;
            y = BuildScope(y);
            y = BuildModel(y);
            y = BuildMeanings(y);
            y = BuildSliders(y);
            y = BuildFoot(y, Toggles());
            // A short foot: the row below the sliders is buttons, which are tall
            // enough to sit against the frame without looking crowded.
            y += RowGap;
            CloseWindow(y);
        }

        /// <summary>The wave coming out of the block, drawn a few times a second.
        /// The one thing in this panel no other block has.</summary>
        private float BuildScope(float y)
        {
            GameObject go = new GameObject("Scope", typeof(RectTransform));
            go.transform.SetParent(host, false);
            Place(go, Margin, y, width - Margin * 2f, ScopeHeight);

            scope = new Scope(Mathf.RoundToInt(width - Margin * 2f),
                              Mathf.RoundToInt(ScopeHeight));
            RawImage face = go.AddComponent<RawImage>();
            face.texture = scope.Texture;
            samples = new float[BraidsBehaviour.ScopeSize];

            return y + ScopeHeight + Margin;
        }

        /// <summary>The model, drawn as the instrument blocks draw their
        /// INSTRUMENT. Its middle opens the whole list rather than stepping through
        /// twenty-three models one press at a time.</summary>
        private float BuildModel(float y)
        {
            modelPicker = AddSelector("MODEL", y, modelNames,
                                      block.Model == null ? 0 : block.Model.Value);
            return y + RowHeight + RowGap * 2f;
        }

        /// <summary>What TIMBRE and COLOR do under the model in force: two lines of
        /// prose, and the whole reason this block has a panel.</summary>
        private float BuildMeanings(float y)
        {
            // A larger face on an 18-unit pitch: read once per model change, and
            // the panel has no height to spare. The labels overflow vertically, so
            // the box stays 16 and the glyphs draw past it.
            timbreMeaning = Label("", Margin, y, width - Margin * 2f, 16f,
                                  MeaningFontSize, TextAnchor.MiddleLeft, UIF.Ink);
            y += 18f;
            colourMeaning = Label("", Margin, y, width - Margin * 2f, 16f,
                                  MeaningFontSize, TextAnchor.MiddleLeft, UIF.Ink);
            return y + 18f + Margin;
        }

        /// <summary>The eight sliders, in the shared rows. Each caption is its
        /// control's own name, so the panel and Besiege's mapper call the same
        /// setting the same thing.</summary>
        private float BuildSliders(float y)
        {
            // NOTE is the one row not written as a number: a pitch reads as C4 and
            // means nothing as 60.
            AddDial(ref y, block.Note, true, false);
            // Cents are counted: a third of a cent is not a tuning anybody meant.
            AddDial(ref y, block.Fine, false, true);
            timbre = AddDial(ref y, block.TimbreSlider, false, false);
            colour = AddDial(ref y, block.ColourSlider, false, false);
            AddDial(ref y, block.Volume, false, false);
            AddDial(ref y, block.Attack, false, false);
            AddDial(ref y, block.Release, false, false);
            AddDial(ref y, block.Range, false, true);
            return y + RowGap;
        }

        /// <summary>Adds a slider and hands back its row, so the two the model can
        /// render meaningless are known later. Null where the prefab did not come:
        /// a panel missing a row, not a panel that throws.</summary>
        private Row AddDial(ref float y, MSlider bound, bool isNote, bool whole)
        {
            int at = rows.Count;
            y = AddSlider(y, Caption(bound), bound, isNote, whole);
            return rows.Count > at ? rows[at] : null;
        }

        /// <summary>The one toggle this block has, for the foot of the panel.</summary>
        private List<MToggle> Toggles()
        {
            List<MToggle> all = new List<MToggle>();
            if (block.Latch != null)
            {
                all.Add(block.Latch);
            }
            return all;
        }

        /// <summary>
        /// The part of a slider's range this block's handle runs through. ATTACK,
        /// RELEASE and RANGE take far more than anybody drags to, so the block says
        /// what is worth dragging through and the box takes the rest.
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

        // ---- binding and reading ------------------------------------------------

        /// <summary>Points the rows at this block's settings. One window serves
        /// whichever synth block opens next, so this runs every time it is shown --
        /// without it every synth block on the machine drives the first one.
        /// </summary>
        private void Rebind()
        {
            MSlider[] bound = new MSlider[]
            {
                block.Note, block.Fine, block.TimbreSlider, block.ColourSlider,
                block.Volume, block.Attack, block.Release, block.Range
            };
            for (int i = 0; i < rows.Count && i < bound.Length; i++)
            {
                rows[i].Bound = bound[i];
            }
            if (switches.Count > 0)
            {
                switches[0].Bound = block.Latch;
            }
        }

        /// <summary>Pulls every control's state out of the block and shows it.</summary>
        private void ReadFromBlock()
        {
            if (!built || block == null)
            {
                return;
            }
            ReadRows(true);

            if (block.Model != null)
            {
                if (modelPicker != null)
                {
                    modelPicker.Set(modelNames, block.Model.Value);
                }
                ShowModel(block.Model.Value);
            }
        }

        /// <summary>
        /// Says what the two ambiguous controls do under this model, and dims the
        /// pair of them where it makes no use of one.
        /// </summary>
        private void ShowModel(int model)
        {
            shownModel = model;

            bool usesTimbre = BraidsModels.UsesTimbre(model);
            bool usesColour = BraidsModels.UsesColour(model);

            if (timbreMeaning != null)
            {
                timbreMeaning.text = "TIMBRE  " + BraidsModels.Timbre(model);
                timbreMeaning.color = usesTimbre ? UIF.Ink : Idle;
            }
            if (colourMeaning != null)
            {
                colourMeaning.text = "COLOR  " + BraidsModels.Colour(model);
                colourMeaning.color = usesColour ? UIF.Ink : Idle;
            }

            Dim(timbre, usesTimbre);
            Dim(colour, usesColour);
        }

        /// <summary>
        /// Greys a row out and takes it out of use with the colour: the handle will
        /// not drag and the box will not take a caret. A panel that lets you set
        /// what it is telling you nothing reads is a panel arguing with itself. The
        /// value survives -- it is the block's setting either way -- and comes back
        /// with the next model that reads it.
        /// </summary>
        private static void Dim(Row row, bool live)
        {
            if (row == null)
            {
                return;
            }
            Color ink = live ? UIF.Ink : Idle;
            if (row.Caption != null)
            {
                row.Caption.color = ink;
            }
            if (row.Value != null)
            {
                row.Value.color = ink;
            }
            if (row.Control != null)
            {
                row.Control.interactable = live;
            }
            if (row.Box != null)
            {
                // Not `readOnly`: an uninteractable field cannot be focused at all,
                // so the box does not take a caret and then refuse the keys.
                row.Box.interactable = live;
            }
        }

        // ---- input --------------------------------------------------------------

        /// <summary>Notices the player working the MODEL selector. Polled rather
        /// than subscribed, as the instrument panel polls its own: one integer a
        /// frame is cheaper than binding to a signature that could change beneath
        /// the mod.</summary>
        private void WatchModel()
        {
            int index = modelPicker == null ? -1 : modelPicker.Index;
            if (index < 0 || index == shownModel || index >= modelNames.Count
                || block.Model == null)
            {
                return;
            }
            block.Model.Value = index;
            Queue(block.Model);
            ShowModel(index);
        }

        /// <summary>The speaker: hold the block sounding so a model can be chosen by
        /// ear, and light the button red while it is.</summary>
        protected override void Listen()
        {
            if (block == null)
            {
                return;
            }
            block.SetPreview(!block.IsPreviewing);
            ShowListen();
        }

        protected override bool Sounding
        {
            get { return block != null && block.IsPreviewing; }
        }

        /// <summary>Redraws the trace, a few times a second rather than every
        /// frame: it is a picture of a sound, not an instrument reading.</summary>
        private void Trace()
        {
            if (scope == null || samples == null || Time.unscaledTime < nextScope)
            {
                return;
            }
            nextScope = Time.unscaledTime + ScopeInterval;
            int count = block.ReadScope(samples);
            scope.Draw(samples, block.IsPlaying ? count : 0);
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
                // A simulation owns the block: the key gates it, the panel does not,
                // and Besiege's own mapper steps aside too rather than floating over
                // the run.
                if (StatMaster.levelSimulating)
                {
                    Hide();
                    return;
                }
                WatchModel();
                // The preview is switched off by more than this button -- a
                // simulation starting, the panel closing -- so the light on it is
                // polled rather than set by whatever ended it.
                ShowListen();
                Trace();
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
