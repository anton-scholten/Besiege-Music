using System;
using System.Collections.Generic;
using Modding.Modules;
using UnityEngine;

namespace OrchestraMod
{
    /// <summary>
    /// The MIDI loader block: hand it a score and it writes the machine that plays
    /// it, either into the machine being built or into a saved machine of its own.
    ///
    /// The block does nothing at all in a simulation -- it is a tool, and it sits
    /// there as a ballast would. Its settings are ordinary mapper controls, so
    /// they are saved, undone and rebound like any block's; the file, the summary
    /// and the two buttons are in <see cref="LoaderPanel"/>, which needs UI Factory
    /// and says so when it is not there.
    /// </summary>
    public class LoaderBehaviour : BlockModuleBehaviour<LoaderModule>
    {
        private MMenu InstrumentMenu;
        private MMenu TypeMenu;
        private MSlider VolumeSlider;
        private MSlider RangeSlider;
        private MSlider TransposeSlider;
        private MSlider DelaySlider;
        private MSlider TempoSlider;
        private MSlider LimitSlider;
        private MKey StartKeyBinding;

        /// <summary>Set when somebody has put a tempo in by hand, which is the
        /// opposite of <see cref="TempoFromFile"/> and the same thing. A control
        /// rather than a field so it is saved with the machine, undone with it and
        /// sent over multiplayer with it, as every other setting here is; hidden
        /// from Besiege's mapper with the rest while the panel is up.</summary>
        private MToggle TempoSetToggle;

        /// <summary>
        /// The TEMPO slider is showing the file's own tempo and should follow it.
        ///
        /// True until somebody moves the slider or types into its box, and true
        /// again the moment another file is picked -- so a score is played as it
        /// was written unless you say otherwise, and saying otherwise lasts as long
        /// as you are working on that file.
        /// </summary>
        public bool TempoFromFile
        {
            get { return TempoSetToggle == null || !TempoSetToggle.IsActive; }
            set
            {
                if (TempoSetToggle != null)
                {
                    TempoSetToggle.IsActive = !value;
                }
            }
        }

        /// <summary>The last value this block wrote to that slider, which is how a
        /// value somebody else put there is recognised: `MSlider` raises no event
        /// worth hooking, and the panel polls its other controls the same way.</summary>
        private float tempoWritten = -1f;

        private readonly List<string> families = new List<string>();

        /// <summary>What the block last told Besiege's mapper to show, so the flags
        /// are written when the answer changes and not every frame.</summary>
        private bool mapperShown = true;
        private float mapperAskAt;
        private const float MapperAskEvery = 0.5f;

        /// <summary>Which song this block is set to, held in a mapper control.
        ///
        /// A plain field would have been simpler and was wrong: mapper controls are
        /// what Besiege saves, loads, undoes and sends over multiplayer, so a field
        /// makes the one setting that matters most the one setting a saved machine
        /// forgets -- and two loader blocks on the same machine would have been
        /// handed the same remembered file rather than each keeping its own. `MText`
        /// is the mapper type for a string, and `ModBlockBehaviour.AddText` adds one.
        /// Hidden from Besiege's own mapper with the rest; the panel is where a file
        /// is chosen.</summary>
        private MText FileText;

        /// <summary>What the song's variables are named after. A control, like the
        /// file, so it belongs to the block and is saved with the machine: two
        /// loader blocks on one machine writing two songs need two names, or the
        /// second song's timers press the first song's blocks.</summary>
        private MText PrefixText;

        /// <summary>
        /// What this song's variables are named after, as the panel shows it.
        ///
        /// Read back through `Song.Named`, so what the block reports is what the
        /// machine will actually hold: a box with a semicolon in it cannot be a
        /// variable name -- `MKey` joins names with one -- and comes back as the
        /// default rather than as a song that writes itself out of tune with its
        /// own timers.
        /// </summary>
        public string Prefix
        {
            get
            {
                return Song.Named(PrefixText == null ? null : PrefixText.Value);
            }
            set
            {
                if (PrefixText != null)
                {
                    PrefixText.Value = Song.Named(value);
                }
            }
        }

        /// <summary>The file the panel is showing, as picked.</summary>
        public string Path
        {
            get { return FileText == null ? "" : (FileText.Value ?? ""); }
            set
            {
                if (FileText != null)
                {
                    FileText.Value = value ?? "";
                }
            }
        }

        /// <summary>What the last conversion came to, or null if there has not been
        /// one since the file changed.</summary>
        public SongPlan Plan;

        /// <summary>Why the last attempt did not work, or null.</summary>
        public string Trouble;

        public override void SafeAwake()
        {
            // A block whose shape says what it is has nothing to repaint.
            Skins.Hide(BlockBehaviour);

            // Which ids this Besiege gave the instrument blocks. Looked up among
            // the prefabs rather than worked out from this block's own id: they are
            // not numbered contiguously, and a guess lands on another mod's block.
            Catalogue.Resolve();

            families.Clear();
            for (int i = 0; i < Catalogue.Families.Count; i++)
            {
                families.Add(Catalogue.Families[i].Name);
            }
            if (families.Count == 0)
            {
                families.Add("Piano");
            }
            // Last, not first: the menu is saved as an index, so putting this at
            // the top would make every loader block already in a machine play on
            // whatever the file says instead of what it was set to.
            families.Add(Gm.FromFile);
            InstrumentMenu = AddMenu("InstrumentKey", Wanted(), families, false);
            // The instruments within that block. Its list is swapped when the
            // block above changes -- `MMenu.Items` has a public setter -- so what
            // is saved is an index into whichever list is current, exactly as an
            // instrument block's own type menu is.
            TypeMenu = AddMenu("TypeKey", DefaultTypeOf(Wanted()),
                               TypesOf(Wanted()), false);

            VolumeSlider = AddSlider("Volume", "VolumeKey", 0.7f, 0f, 1f);
            // Wider than an instrument block's own default. A song is a field of
            // sixty blocks rather than one, and a machine is usually looked at from
            // further away than it is built at.
            RangeSlider = AddSlider("Range", "RangeKey", 300f, 0.5f, 2000f);
            TransposeSlider = AddSlider("Transpose", "TransposeKey", 0f, -24f, 24f);
            // Seconds between the key being pressed -- or emulated by something
            // else on the machine -- and the first note. The timers each wait their
            // own time from that press, so this shifts all of them together.
            //
            // Nought by default: a key pressed is a song started, which is what
            // anybody expects of it. The seconds are here for the machine that is
            // still falling into a level when its key goes, and that is a thing to
            // ask for rather than a thing to be given.
            DelaySlider = AddSlider("Delay", "LeadKey", 0f, 0f, 10f);

            // Beats per minute. Set to whatever the file says as soon as one is
            // read, and left alone after that until another file is picked -- so
            // it is a readout most of the time and a setting when it is wanted.
            // The range takes anything a file can hold: a MIDI tempo is three bytes
            // of microseconds, and files in the wild carry some absurd ones -- the
            // score this was written for arrived claiming 999.
            TempoSlider = AddSlider("Tempo", "TempoKey", 120f, 5f, 999f);
            TempoSetToggle = AddToggle("Tempo set by hand", "TempoSetKey", false);

            // Most notes to place. A timer apiece, so this is most of the block
            // count and most of what a long song costs to run. It does not follow
            // the file: the number that matters is how many blocks this machine
            // should have, not how many notes somebody wrote.
            LimitSlider = AddSlider("Note limit", "LimitKey", 700f, 50f, 10000f);

            // The one control left in Besiege's own mapper: every timer this block
            // writes waits for this key, so binding it here binds the whole song.
            StartKeyBinding = AddKey("Start", "StartKey", KeyCode.M);

            // The last song converted is the *default* for a block placed now, not
            // something written over one being loaded: a machine coming back out of
            // a save brings its own, and DeSerialize puts it here after this runs.
            FileText = AddText("File", "FileKey", Files.Remembered());
            PrefixText = AddText("Variable prefix", "PrefixKey",
                                 SongOptions.DefaultPrefix);

            RetypeMenu();
        }

        /// <summary>Which instrument that block starts on -- the same one a block
        /// of it placed by hand starts on, rather than always the first.</summary>
        private int DefaultTypeOf(int family)
        {
            return family >= 0 && family < Catalogue.Families.Count
                ? Catalogue.Families[family].DefaultType : 0;
        }

        /// <summary>The instruments in one block, for the type menu.</summary>
        private List<string> TypesOf(int family)
        {
            List<string> names = new List<string>();
            if (family >= 0 && family < families.Count
                && families[family] == Gm.FromFile)
            {
                // The last entry is not a block, so it has no instruments to
                // choose between; the selector says so rather than going blank.
                names.Add("(each part its own)");
                return names;
            }
            if (family >= 0 && family < Catalogue.Families.Count)
            {
                names.AddRange(Catalogue.Families[family].Types);
            }
            if (names.Count == 0)
            {
                names.Add("None");
            }
            return names;
        }

        /// <summary>
        /// Puts the type menu on the block that is chosen now, keeping the choice
        /// where the new block has one that far along. Called by the panel when the
        /// first selector moves, and here when the block is loaded.
        /// </summary>
        public void RetypeMenu()
        {
            if (InstrumentMenu == null || TypeMenu == null)
            {
                return;
            }
            List<string> names = TypesOf(InstrumentMenu.Value);
            TypeMenu.Items = names;
            if (TypeMenu.Value >= names.Count)
            {
                TypeMenu.Value = 0;
            }
        }

        /// <summary>
        /// Two menus for one block is one too many. With UI Factory there, the
        /// panel draws every setting and Besiege's mapper keeps only the key --
        /// which the panel does not draw, rebinding being the mapper's own
        /// business. Asked on a timer rather than every frame: while UI Factory is
        /// absent the answer costs a caught exception.
        /// </summary>
        private void Update()
        {
            if (mapperShown)
            {
                if (Time.unscaledTime < mapperAskAt)
                {
                    return;
                }
                mapperAskAt = Time.unscaledTime + MapperAskEvery;
                if (UIF.Available && !LoaderPanel.Failed)
                {
                    ShowInMapper(false);
                }
            }
            else if (LoaderPanel.Failed)
            {
                // The panel gave up after hiding them. Without it there is nothing
                // else to set this block with, so they all come back.
                ShowInMapper(true);
            }
        }

        private void ShowInMapper(bool show)
        {
            mapperShown = show;
            InstrumentMenu.DisplayInMapper = show;
            TypeMenu.DisplayInMapper = show;
            VolumeSlider.DisplayInMapper = show;
            RangeSlider.DisplayInMapper = show;
            TransposeSlider.DisplayInMapper = show;
            DelaySlider.DisplayInMapper = show;
            FileText.DisplayInMapper = show;
            PrefixText.DisplayInMapper = show;
            TempoSlider.DisplayInMapper = show;
            TempoSetToggle.DisplayInMapper = show;
            LimitSlider.DisplayInMapper = show;
            // StartKeyBinding is deliberately untouched: the panel shows no key,
            // and Besiege's own key capture is the only thing that can rebind one.
        }

        /// <summary>The block the XML asks for, as an index into the menu.</summary>
        private int Wanted()
        {
            string wanted = Module == null || Module.Instrument == null
                ? "Piano" : Module.Instrument.Trim().ToLower();
            for (int i = 0; i < families.Count; i++)
            {
                if (families[i].ToLower() == wanted)
                {
                    return i;
                }
            }
            return 0;
        }

        // ---- what the panel needs --------------------------------------------

        /// <summary>The blocks a song can be written for, in the panel's order.</summary>
        public List<string> Families
        {
            get { return families; }
        }

        public MMenu Instruments { get { return InstrumentMenu; } }
        public MMenu Types { get { return TypeMenu; } }
        public MSlider Volume { get { return VolumeSlider; } }
        public MSlider Range { get { return RangeSlider; } }
        public MSlider Transpose { get { return TransposeSlider; } }
        public MSlider Delay { get { return DelaySlider; } }
        public MSlider Tempo { get { return TempoSlider; } }
        public MSlider Limit { get { return LimitSlider; } }

        /// <summary>The instrument every pitched part goes to, as the converter
        /// wants it: the block, and the instrument within it after a colon.</summary>
        public string Instrument
        {
            get
            {
                // The saved values are deserialised after SafeAwake, so the type
                // menu may still be holding the list the *default* block had.
                // Asked here rather than only when the panel opens, because a
                // conversion can be asked for before the panel has drawn a thing.
                RetypeMenu();
                int at = InstrumentMenu == null ? 0 : InstrumentMenu.Value;
                string family = at >= 0 && at < families.Count ? families[at] : "Piano";
                if (family == Gm.FromFile)
                {
                    // No type to add: which instrument each part gets is the file's
                    // to say, one part at a time.
                    return Gm.FromFile;
                }
                if (TypeMenu == null || TypeMenu.Items == null
                    || TypeMenu.Value < 0 || TypeMenu.Value >= TypeMenu.Items.Count)
                {
                    return family;
                }
                return family + ":" + TypeMenu.Items[TypeMenu.Value];
            }
        }

        /// <summary>The conversion settings, as the block's own controls are set.</summary>
        public SongOptions Options()
        {
            SongOptions options = new SongOptions();
            options.Instrument = Instrument;
            options.Volume = VolumeSlider == null ? 0.7f : VolumeSlider.Value;
            options.Range = RangeSlider == null ? 300f : RangeSlider.Value;
            options.Transpose = TransposeSlider == null
                ? 0 : Mathf.RoundToInt(TransposeSlider.Value);
            options.Offset = DelaySlider == null ? 0f : DelaySlider.Value;
            // Nought means "follow the file", which is not the same as asking for
            // the tempo the file starts at: a score that changes tempo part way
            // through keeps every one of its changes, where a number here flattens
            // the whole of it to one speed.
            options.Tempo = TempoFromFile || TempoSlider == null
                ? 0f : TempoSlider.Value;
            options.Limit = LimitSlider == null
                ? 700 : Mathf.RoundToInt(LimitSlider.Value);
            options.Prefix = Prefix;
            // Every timer waits its own time from that key, so one press -- by hand
            // or emulated by anything else on the machine -- starts the whole song.
            // With no key bound there is nothing to wait for, and the timers start
            // with the simulation instead.
            options.StartKey = KeyName();
            options.StartVariable = KeyVariable();
            return options;
        }

        /// <summary>Whether the song starts with the simulation rather than on a
        /// keypress, which is what an unbound key comes to.</summary>
        public bool OnSimulationStart
        {
            get { return KeyName() == null && KeyVariable() == null; }
        }

        /// <summary>
        /// The variable this block's Start key listens to, or null.
        ///
        /// A Besiege key can carry a *message* -- one or more variable names --
        /// instead of answering the keyboard, which is how one block on a machine
        /// starts another. `useMessage` is the flag that says listen to the name;
        /// `message` holds the names, and `MKey.CombineVariables` joins them the
        /// way a save spells them.
        ///
        /// When there is one, that is what the song has to wait for: a block set to
        /// a variable does not answer to its own keycode at all, so timers written
        /// with the keycode would be a song nothing starts.
        /// </summary>
        public string KeyVariable()
        {
            if (StartKeyBinding == null || !StartKeyBinding.useMessage
                || StartKeyBinding.message == null)
            {
                return null;
            }
            string joined = MKey.CombineVariables(StartKeyBinding.message);
            return string.IsNullOrEmpty(joined) ? null : joined;
        }

        /// <summary>
        /// The key the timers wait for, spelled as Unity spells it -- which is what
        /// a save has to hold: `KeyCodeConverter` parses the name back, and a name
        /// it cannot parse is dropped as the machine loads.
        /// </summary>
        public string KeyName()
        {
            if (StartKeyBinding == null || StartKeyBinding.KeysCount == 0)
            {
                return null;
            }
            KeyCode code = StartKeyBinding.GetKey(0);
            return code == KeyCode.None ? null : code.ToString();
        }

        /// <summary>Where the machine's blocks are laid out around, in the machine's
        /// own space: this block, so a song appears where the player is looking.</summary>
        public Vector3 Origin()
        {
            // Fully qualified: `Modding.Blocks` has a Machine of its own.
            global::Machine machine = global::Machine.Active();
            if (machine == null || machine.BuildingMachine == null)
            {
                return transform.position;
            }
            return machine.BuildingMachine.InverseTransformPoint(transform.position);
        }

        // ---- the work --------------------------------------------------------

        /// <summary>
        /// Reads the file at <see cref="Path"/> and works out the machine for it.
        /// Never throws: what went wrong is left in <see cref="Trouble"/>, because
        /// every caller is a button and every failure is something to show.
        /// </summary>
        public void Analyse()
        {
            Plan = null;
            Trouble = null;
            if (string.IsNullOrEmpty(Path))
            {
                return;
            }
            NoticeTempo();
            try
            {
                SongPlan plan = Song.Convert(Files.Read(Path), Options());
                plan.Name = Files.NameOf(Path);
                Plan = plan;
                Files.Remember(Path);
                ShowFileTempo(plan.FileBpm);
            }
            catch (Exception e)
            {
                Trouble = e.Message;
                // The panel shows the message; the log gets the whole thing, which
                // is the difference between "no file" and a parse that fell over
                // in a way worth reporting.
                Log.Warn("could not read " + Path + ": " + e.ToString());
            }
        }

        /// <summary>
        /// Points the block at another song, and reads it.
        ///
        /// A new file brings its own tempo with it: the slider goes back to
        /// following whatever the file says, whatever it was set to for the last
        /// one. Same file, no change -- so redrawing the panel does not undo a
        /// tempo somebody typed.
        /// </summary>
        public void SetFile(string path)
        {
            string wanted = path == null ? "" : path;
            if (wanted == Path)
            {
                return;
            }
            Path = wanted;
            TempoFromFile = true;
            // Nothing has been written to the slider for *this* file yet. Without
            // this the value left over from the last one reads as somebody having
            // just moved the slider, and the tempo would go back to being set by
            // hand the moment a new file was picked.
            tempoWritten = -1f;
            Analyse();
        }

        /// <summary>Whether the slider has been moved since this block last wrote
        /// to it, which is the only sign there is that somebody set it by hand.</summary>
        private void NoticeTempo()
        {
            // `tempoWritten` below nought means nothing has been put in the slider
            // this session, so there is nothing yet to have been moved away from --
            // a block just loaded out of a machine is in that state, and its toggle
            // already says which of the two it is.
            if (TempoFromFile && TempoSlider != null && tempoWritten >= 0f
                && Mathf.Abs(TempoSlider.Value - tempoWritten) > 0.01f)
            {
                TempoFromFile = false;
            }
        }

        /// <summary>Puts the file's own tempo in the slider, while it is following
        /// one. Remembered as well as written, so the next look can tell this value
        /// from one somebody dragged to.</summary>
        private void ShowFileTempo(float bpm)
        {
            if (!TempoFromFile || TempoSlider == null || bpm <= 0f)
            {
                return;
            }
            float shown = Mathf.Clamp(bpm, TempoSlider.Min, TempoSlider.Max);
            tempoWritten = shown;
            if (Mathf.Abs(TempoSlider.Value - shown) > 0.01f)
            {
                TempoSlider.Value = shown;
            }
        }

        /// <summary>
        /// Adds the converted song to the machine being built, selected so the
        /// player can move it. Returns how many blocks went in, or -1 with
        /// <see cref="Trouble"/> set.
        /// </summary>
        public int AddToMachine()
        {
            // Read again rather than trusting what is cached: every setting on the
            // block -- volume, range, transpose, delay, which instrument -- is
            // baked into the blocks this writes, and any of them may have moved
            // since the summary was drawn.
            Analyse();
            if (Plan == null)
            {
                return -1;
            }
            try
            {
                Trouble = null;
                return Drop.Into(Plan, Origin());
            }
            catch (Exception e)
            {
                Trouble = e.Message;
                Log.Warn("could not add the song to the machine: " + e.Message);
                return -1;
            }
        }

        /// <summary>
        /// Writes the converted song out as a `.bsg` in the mod's own data folder.
        /// Returns where it went, or null with <see cref="Trouble"/> set. The
        /// panel prefers Besiege's own save screen and only falls back to this.
        /// </summary>
        public string SaveAs(string name)
        {
            Analyse();
            if (Plan == null)
            {
                return null;
            }
            try
            {
                Trouble = null;
                return Files.SaveMachine(name, Bsg.Write(Plan, name));
            }
            catch (Exception e)
            {
                Trouble = e.Message;
                Log.Warn("could not save the song as a machine: " + e.Message);
                return null;
            }
        }
    }
}
