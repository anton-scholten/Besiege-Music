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
        private MKey StartKeyBinding;

        private readonly List<string> families = new List<string>();

        /// <summary>What the block last told Besiege's mapper to show, so the flags
        /// are written when the answer changes and not every frame.</summary>
        private bool mapperShown = true;
        private float mapperAskAt;
        private const float MapperAskEvery = 0.5f;

        /// <summary>The file the panel is showing, as typed or as picked.</summary>
        public string Path = "";

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
            InstrumentMenu = AddMenu("InstrumentKey", Wanted(), families, false);
            // The instruments within that block. Its list is swapped when the
            // block above changes -- `MMenu.Items` has a public setter -- so what
            // is saved is an index into whichever list is current, exactly as an
            // instrument block's own type menu is.
            TypeMenu = AddMenu("TypeKey", 0, TypesOf(Wanted()), false);

            VolumeSlider = AddSlider("Volume", "VolumeKey", 1f, 0f, 1f);
            RangeSlider = AddSlider("Range", "RangeKey", 120f, 0.5f, 2000f);
            TransposeSlider = AddSlider("Transpose", "TransposeKey", 0f, -24f, 24f);
            // Seconds between the key being pressed -- or emulated by something
            // else on the machine -- and the first note. The timers each wait their
            // own time from that press, so this shifts all of them together, and a
            // machine dropped into a level is usually still falling for the first
            // second of it.
            DelaySlider = AddSlider("Delay", "LeadKey", 1f, 0f, 10f);

            // The one control left in Besiege's own mapper: every timer this block
            // writes waits for this key, so binding it here binds the whole song.
            StartKeyBinding = AddKey("Start", "StartKey", KeyCode.M);

            RetypeMenu();
            Path = Files.Remembered();
        }

        /// <summary>The instruments in one block, for the type menu.</summary>
        private List<string> TypesOf(int family)
        {
            List<string> names = new List<string>();
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
            options.Volume = VolumeSlider == null ? 1f : VolumeSlider.Value;
            options.Range = RangeSlider == null ? 120f : RangeSlider.Value;
            options.Transpose = TransposeSlider == null
                ? 0 : Mathf.RoundToInt(TransposeSlider.Value);
            options.Offset = DelaySlider == null ? 1f : DelaySlider.Value;
            // Every timer waits its own time from that key, so one press -- by hand
            // or emulated by anything else on the machine -- starts the whole song.
            // With no key bound there is nothing to wait for, and the timers start
            // with the simulation instead.
            options.StartKey = KeyName();
            return options;
        }

        /// <summary>Whether the song starts with the simulation rather than on a
        /// keypress, which is what an unbound key comes to.</summary>
        public bool OnSimulationStart
        {
            get { return KeyName() == null; }
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
            try
            {
                SongPlan plan = Song.Convert(Files.Read(Path), Options());
                plan.Name = Files.NameOf(Path);
                Plan = plan;
                Files.Remember(Path);
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
