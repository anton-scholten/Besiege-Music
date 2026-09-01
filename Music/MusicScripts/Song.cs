using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusicMod
{
    /// <summary>How a score is turned into blocks. The panel's settings, and the
    /// defaults `tools/make-song.py` uses for the same job.</summary>
    public class SongOptions
    {
        /// <summary>The block everything that is not percussion goes to. Either a
        /// family -- "Piano" -- or a family and one of its instruments, as
        /// "Strings:Cello".</summary>
        public string Instrument = "Piano";

        /// <summary>Semitones, added to every pitched note.</summary>
        public int Transpose = 0;

        /// <summary>Seconds of quiet before the first note. Nought, as the
        /// loader block's DELAY and `tools/make-song.py --offset` both are: a key
        /// pressed is a song started.</summary>
        public float Offset = 0f;

        /// <summary>Silence left between two notes on one block. An emulated key is
        /// reference counted, so a repeat that arrives while the name is still held
        /// raises no press at all; the score is separated instead.</summary>
        public float Gap = 0.06f;

        /// <summary>Most notes to place. A timer apiece, so this is most of the
        /// block count -- and with each part on its own instrument, a block per
        /// instrument per pitch as well.</summary>
        public int Limit = 700;

        /// <summary>Blocks per row; 0 for roughly square.</summary>
        public int Columns = 0;

        /// <summary>Block widths between neighbours.</summary>
        public float Spacing = 1f;

        public float Volume = 0.7f;
        public float Range = 300f;

        /// <summary>Unity's name for the key that starts the song, or null to start
        /// with the simulation.</summary>
        public string StartKey;

        /// <summary>The variable the song waits for instead of a keypress, or null.
        /// Takes precedence over <see cref="StartKey"/>, which is then only the
        /// keycode the timers carry to be counted -- see the note in Plan.</summary>
        public string StartVariable;

        /// <summary>Treat channel 10 as pitched rather than as a drum kit.</summary>
        public bool NoDrums = false;

        /// <summary>Beats per minute to play at, or 0 to follow the file.</summary>
        public float Tempo = 0f;

        /// <summary>What every variable this song uses is named after: the blocks
        /// listen to `&lt;prefix&gt;000`, `&lt;prefix&gt;001` and so on. Worth
        /// changing when two songs share a machine, since two songs on one set of
        /// names is one song playing both parts.</summary>
        public string Prefix = DefaultPrefix;

        /// <summary>The name a song uses unless it is told another.</summary>
        public const string DefaultPrefix = "orch_";
    }

    /// <summary>One block of the machine being written, before it is either
    /// dropped into the world or written to a file.</summary>
    public class SongBlock
    {
        public int Type;
        public int LocalId;         // 0 for one of Besiege's own blocks
        public Vector3 Position;
        public Quaternion Rotation;
        public XDataHolder Data;
    }

    /// <summary>What a conversion came to: the blocks, and what to tell the
    /// player about them before they commit to it.</summary>
    public class SongPlan
    {
        public string Name = "Song";
        public readonly List<SongBlock> Blocks = new List<SongBlock>();

        public int Notes;
        public int Voices;          // instrument blocks
        public int Timers;
        public float Seconds;

        /// <summary>The tempo the file itself starts at, whatever this was
        /// converted at: the number the panel's TEMPO slider goes back to.</summary>
        public float FileBpm = 120f;

        /// <summary>This machine holds Braids blocks. It once meant a save had to
        /// name that mod as well as this one; the block is this mod's now, so what
        /// is left is the summary telling the player what was written.</summary>
        public bool NeedsBraids;

        /// <summary>Notes that fell inside another note on the same block and went.</summary>
        public int Crowded;

        /// <summary>Notes past <see cref="SongOptions.Limit"/> that were dropped.</summary>
        public int Dropped;

        /// <summary>Instruments used, as "Piano (Grand piano) x11" a piece, for
        /// the summary the panel shows.</summary>
        public readonly List<string> Parts = new List<string>();
    }

    /// <summary>
    /// The converter: a MIDI score becomes a flat field of blocks, an instrument
    /// block per distinct voice and a timer block per note.
    ///
    /// The same machine `tools/make-song.py` writes, and the reasoning is written
    /// out there and in docs/SONGS.md. In short: a Music block plays one
    /// note, so a tune is a row of blocks; the timers are joined to them by
    /// Besiege's variable system, which has no limit on how many names there are
    /// where the keyboard has about a hundred keys; and nothing is connected to
    /// anything, because a field of blocks loads and falls exactly as well as a
    /// machine that was built.
    /// </summary>
    public static class Song
    {
        /// <summary>Besiege's own timer block.</summary>
        public const int TimerBlock = 66;

        /// <summary>The starting block every saved machine has one of.</summary>
        public const int StartingBlock = 0;

        /// <summary>What a missing Music block shows instead: a ballast.</summary>
        public const int Fallback = 35;

        /// <summary>A quarter turn about X, which stands a block up on flat ground
        /// -- an instrument on its feet rather than on its side.</summary>
        public static readonly Quaternion FaceUp =
            new Quaternion(-0.7071068f, 0f, 0f, 0.7071068f);

        // The timer's own mapper keys, from TimerBlock.Awake.
        private const string TimerWait = "bmt-wait";
        private const string TimerHold = "bmt-emulation-time";
        private const string TimerAuto = "bmt-automatic";
        private const string TimerEmulate = "bmt-emulate";
        private const string TimerStart = "bmt-activate";

        /// <summary>General MIDI percussion, mapped onto the struck families. Only
        /// the common half of the kit; anything else is a snare.</summary>
        private static readonly int[] DrumNotes =
        {
            35, 36,                             // kick
            37, 38, 40,                         // snare
            41, 43, 45, 47, 48, 50,             // toms
            42, 44, 46,                         // hi-hat
            49, 57,                             // crash
            51, 59                              // ride
        };

        private static readonly string[] DrumFamilies =
        {
            "Drums", "Drums",
            "Drums", "Drums", "Drums",
            "Drums", "Drums", "Drums", "Drums", "Drums", "Drums",
            "Cymbals", "Cymbals", "Cymbals",
            "Cymbals", "Cymbals",
            "Cymbals", "Cymbals"
        };

        private static readonly string[] DrumPieces =
        {
            "Kick", "Kick",
            "Snare", "Snare", "Snare",
            "Tom", "Tom", "Tom", "Tom", "Tom", "Tom",
            "Hi-hat", "Hi-hat", "Hi-hat",
            "Crash", "Crash",
            "Ride", "Ride"
        };

        /// <summary>
        /// What note each kit piece is asked for. Sixty for all of them, because a
        /// kit block plays a recording and sixty is the note it was published at:
        /// the block plays it as it was recorded rather than transposed.
        ///
        /// It was not always so. These blocks were synthesised, and a synthesised
        /// kick wants to be lower than a synthesised tom, so each piece carried its
        /// own pitch -- 36 for a kick, 78 for a hi-hat. Against a recording those
        /// numbers are two octaves down and an octave and a half up, which is a
        /// kick that arrives late and a hi-hat that whistles.
        /// </summary>
        private static readonly string[] PieceNames =
        { "Kick", "Snare", "Tom", "Hi-hat", "Crash", "Ride" };
        private static readonly int[] PieceNotes = { 60, 60, 60, 60, 60, 60 };

        /// <summary>
        /// Where the toms sit around that, by their General MIDI note. A kit really
        /// is tuned: the six toms General MIDI names run low to high, they all come
        /// to one block here, and this is what keeps them apart -- the recording is
        /// a mid tom, so the low ones are pitched down from it and the high ones up.
        /// </summary>
        private const int TomRoot = 45;

        /// <summary>General MIDI's open hi-hat, and the note the Cymbals block
        /// keeps that recording at. The block carries both hats -- a closed one is
        /// not an open one with the ring taken off -- and picks between them by
        /// note, so this is how a score asks for the open one.</summary>
        private const int OpenHatNote = 46;
        private const int OpenHat = 72;

        /// <summary>Which block plays a note, as family, instrument and pitch.</summary>
        private class Voice
        {
            public Family Block;
            public int TypeIndex;
            public int Pitch;
            public string Key;      // what makes two voices the same one
            public int Index;       // its place in the machine, and its variable
            public bool Placed;     // whether a block has been laid out for it
            public float Loudness;  // the velocities sent to it, added up
            public int Hits;
        }

        // ---- the whole job ---------------------------------------------------

        /// <summary>
        /// Reads a MIDI file and lays out the machine that plays it. Throws with a
        /// sentence worth showing when the file is not one, or holds nothing this
        /// can use.
        /// </summary>
        public static SongPlan Convert(byte[] file, SongOptions options)
        {
            Midi midi = new Midi(file);
            List<MidiNote> notes = midi.Notes(options.Tempo);
            if (notes.Count == 0)
            {
                throw new Exception("there are no notes in that file");
            }
            SongPlan plan = Plan(notes, options);
            plan.FileBpm = midi.StartBpm;
            return plan;
        }

        /// <summary>The conversion proper, from notes already read.</summary>
        public static SongPlan Plan(List<MidiNote> notes, SongOptions options)
        {
            if (Catalogue.Families.Count == 0)
            {
                throw new Exception("the instrument blocks could not be read");
            }
            // Checked for every block a note might go to, not just the first: an
            // id that was never resolved is 0, and writing that into a machine
            // fetches whatever block happens to own it -- another mod's, which is
            // how a song came out played on somebody else's sound blocks.
            for (int i = 0; i < Catalogue.Families.Count; i++)
            {
                if (Catalogue.Families[i].BlockType <= 0)
                {
                    throw new Exception("this game has no id for the "
                        + Catalogue.Families[i].Name + " block, so a machine "
                        + "written now would hold the wrong blocks");
                }
            }

            // The clock starts at the first note, not at the file's own zero: a
            // score exported with an empty bar at the front would otherwise be a
            // machine that stands still for it.
            float first = notes[0].Start;
            for (int i = 0; i < notes.Count; i++)
            {
                notes[i].Start -= first;
            }

            SongPlan plan = new SongPlan();
            Dictionary<string, Voice> voices = new Dictionary<string, Voice>();

            // Every note's voice, worked out once: it is wanted three times over,
            // and picking one involves a name lookup each time.
            Voice[] on = new Voice[notes.Count];
            for (int i = 0; i < notes.Count; i++)
            {
                on[i] = Assign(notes[i], options, voices);
            }

            plan.Crowded = Separate(notes, on, options.Gap);
            List<MidiNote> kept = new List<MidiNote>();
            List<Voice> keptOn = new List<Voice>();
            for (int i = 0; i < notes.Count; i++)
            {
                if (on[i] != null)
                {
                    kept.Add(notes[i]);
                    keptOn.Add(on[i]);
                }
            }

            if (options.Limit > 0 && kept.Count > options.Limit)
            {
                plan.Dropped = kept.Count - options.Limit;
                kept.RemoveRange(options.Limit, kept.Count - options.Limit);
                keptOn.RemoveRange(options.Limit, keptOn.Count - options.Limit);
            }
            if (kept.Count == 0)
            {
                throw new Exception("nothing was left of that score to play");
            }

            // Only the voices that survived get a block, and they are numbered in
            // the order they are first heard -- so orch_000 is the first note.
            List<Voice> playing = new List<Voice>();
            for (int i = 0; i < keptOn.Count; i++)
            {
                Voice voice = keptOn[i];
                if (!voice.Placed)
                {
                    voice.Placed = true;
                    voice.Index = playing.Count;
                    playing.Add(voice);
                }
                voice.Loudness += kept[i].Velocity;
                voice.Hits++;
            }

            int columns = options.Columns;
            if (columns <= 0)
            {
                columns = Mathf.Max(1, Mathf.CeilToInt(
                    Mathf.Sqrt(kept.Count + playing.Count + 1)));
            }
            float spacing = options.Spacing > 0f ? options.Spacing : 1f;
            int placed = 0;

            for (int i = 0; i < playing.Count; i++)
            {
                Voice voice = playing[i];
                SongBlock block = Place(plan, voice.Block.BlockType, voice.Block.LocalId,
                                        placed++, columns, spacing,
                                        playing.Count + kept.Count);
                // One block, one note, one loudness: a block cannot be struck
                // harder, so the velocities sent to it are averaged. Onto a third
                // of the way up and no further than full -- a passage set to its
                // raw velocity is a passage nobody hears, and the dynamics that
                // matter are between the parts rather than within one.
                float mean = voice.Loudness / Mathf.Max(1, voice.Hits);
                float level = Mathf.Clamp(
                    options.Volume * (0.35f + 0.65f * mean / 127f), 0.05f, 1f);
                string name = Variable(options.Prefix, voice.Index);

                if (voice.Block.Name == Braids.FamilyName)
                {
                    // Another mod's block, filled in with its own mapper keys.
                    // Everything else about it -- where it sits, which variable it
                    // listens to, how loud it is -- is the same decision as for
                    // one of ours.
                    Braids.Fill(block.Data, voice.TypeIndex, voice.Pitch, name,
                                level, options.Range);
                    plan.NeedsBraids = true;
                }
                else
                {
                    VariableKey(block.Data, "bmt-Activate", name, "N");
                    block.Data.Write(new XInteger("bmt-TypeKey", voice.TypeIndex));
                    block.Data.Write(new XSingle("bmt-NoteKey", voice.Pitch));
                    block.Data.Write(new XSingle("bmt-VolumeKey", level));
                    block.Data.Write(new XSingle("bmt-RangeKey", options.Range));
                }

                plan.Parts.Add(voice.Block.Name
                    + (voice.TypeIndex < voice.Block.Types.Count
                        ? " (" + voice.Block.Types[voice.TypeIndex] + ")" : ""));
            }

            float last = 0f;
            for (int i = 0; i < kept.Count; i++)
            {
                MidiNote note = kept[i];
                SongBlock block = Place(plan, TimerBlock, 0, placed++, columns, spacing,
                                        playing.Count + kept.Count);
                if (!string.IsNullOrEmpty(options.StartVariable))
                {
                    // The block's own key listens to a variable rather than to the
                    // keyboard, so the timers have to listen to the same name --
                    // whatever presses that variable is what starts the song.
                    //
                    // A keycode goes in the array as well, and is never answered
                    // to: `Machine.InitSimBlock` files a key with
                    // `KeyInputController` once per keycode it holds, so a key with
                    // none is registered under no name and hears nothing. With
                    // `Use=True` the keycode itself is ignored -- it is there to be
                    // counted.
                    VariableKey(block.Data, TimerStart, options.StartVariable,
                                string.IsNullOrEmpty(options.StartKey)
                                    ? "C" : options.StartKey);
                }
                else if (string.IsNullOrEmpty(options.StartKey))
                {
                    block.Data.Write(new XBoolean(TimerAuto, true));
                }
                else
                {
                    // Every timer waits its own time from the moment the key is
                    // pressed, so one press starts the song.
                    block.Data.Write(new XStringArray(TimerStart,
                                                      new string[] { options.StartKey }));
                }
                block.Data.Write(new XSingle(TimerWait, note.Start + options.Offset));
                block.Data.Write(new XSingle(TimerHold, Mathf.Max(0.05f, note.Length)));
                VariableKey(block.Data, TimerEmulate,
                            Variable(options.Prefix, keptOn[i].Index), "C");
                last = Mathf.Max(last, note.End);
            }

            plan.Notes = kept.Count;
            plan.Voices = playing.Count;
            plan.Timers = kept.Count;
            plan.Seconds = last + options.Offset;
            Tidy(plan.Parts);
            return plan;
        }

        // ---- laying them out -------------------------------------------------

        /// <summary>
        /// Blocks are laid out, not built: a flat field, a row at a time, centred
        /// on where the machine goes. Flat rather than upright so the band is
        /// spread over the ground instead of stacked into a wall, and so nothing
        /// has far to fall -- none of it is attached to anything.
        /// </summary>
        private static SongBlock Place(SongPlan plan, int type, int localId, int index,
                                       int columns, float spacing, int total)
        {
            int rows = Mathf.Max(1, Mathf.CeilToInt(total / (float)columns));
            SongBlock block = new SongBlock();
            block.Type = type;
            block.LocalId = localId;
            block.Position = new Vector3(
                ((index % columns) - (columns - 1) * 0.5f) * spacing,
                0f,
                ((index / columns) - (rows - 1) * 0.5f) * spacing);
            block.Rotation = FaceUp;
            block.Data = new XDataHolder();
            // Written on every block by the game itself; one less difference
            // between a machine this made and one somebody built.
            block.Data.Write(new XInteger("bmt-version", 1));
            plan.Blocks.Add(block);
            return block;
        }

        /// <summary>The variable one instrument block listens to.</summary>
        private static string Variable(string prefix, int index)
        {
            return Named(prefix) + index.ToString("000");
        }

        /// <summary>
        /// A prefix that can safely be a variable name, or the default.
        ///
        /// `MKey` joins several names with `;` and spells the whole thing
        /// `Message=a;b`, so a name carrying either character would be read back as
        /// two names or as none. Letters, digits, `_` and `-` are the whole of what
        /// is allowed through; anything else means the box was mistyped, and a song
        /// that plays is better than a song that does not.
        /// </summary>
        public static string Named(string prefix)
        {
            string wanted = prefix == null ? "" : prefix.Trim();
            if (wanted.Length == 0 || wanted.Length > 24)
            {
                return SongOptions.DefaultPrefix;
            }
            for (int i = 0; i < wanted.Length; i++)
            {
                char c = wanted[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                       || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok)
                {
                    return SongOptions.DefaultPrefix;
                }
            }
            return wanted;
        }

        /// <summary>
        /// A mapper key driven by a variable rather than by the keyboard.
        ///
        /// The keycode is not decoration. `Machine.InitSimBlock` files a block's
        /// keys with `KeyInputController` from inside
        /// `for (i = 0; i &lt; key.KeysCount; i++)`, and it is `AddMKey` that puts a
        /// key into the table variable names are looked up in. A key with a name
        /// and no keycodes is therefore never registered, hears nothing, and looks
        /// exactly like a block that does not support emulation. `AddMKey` files a
        /// key under its name *or* its keys and never both, so the keycode kept
        /// here stays inert.
        /// </summary>
        private static void VariableKey(XDataHolder data, string key, string name,
                                        string keycode)
        {
            data.Write(new XStringArray(key, new string[]
            {
                keycode, "Message=" + name, "Use=True"
            }));
        }

        // ---- which block plays what ------------------------------------------

        private static Voice Assign(MidiNote note, SongOptions options,
                                    Dictionary<string, Voice> voices)
        {
            string familyName;
            string typeName;
            int pitch;

            if (note.Channel == 9 && !options.NoDrums)
            {
                int piece = Kit(note.Pitch);
                familyName = DrumFamilies[piece];
                typeName = DrumPieces[piece];
                pitch = PieceNote(typeName);
                if (typeName == "Tom")
                {
                    pitch += note.Pitch - TomRoot;
                }
                else if (note.Pitch == OpenHatNote)
                {
                    pitch = OpenHat;
                }
            }
            else
            {
                // "As the file says" means the note's own program change decides,
                // which is how a score with a violin part, a bass part and a sax
                // part comes out as three different blocks rather than three
                // tracks of piano. Anything else names one block for everything.
                if (options.Instrument == Gm.FromFile && Braids.Available
                    && Synth(note.Program))
                {
                    // A synth part, and the mod that renders those is installed.
                    Braids.Note();
                    Family synth = Braids.AsFamily();
                    int model = Braids.TypeFor(note.Program);
                    int at = Mathf.Clamp(note.Pitch + options.Transpose, 0, 127);
                    return Keep(voices, synth, model, at);
                }
                string wanted = options.Instrument == Gm.FromFile
                    ? Gm.Instrument(note.Program) : options.Instrument;
                Split(wanted, out familyName, out typeName);
                pitch = Mathf.Clamp(note.Pitch + options.Transpose, 0, 127);
            }

            Family family = Catalogue.Find(familyName);
            if (family == null)
            {
                // A score that names a block this mod does not have still plays:
                // the first block in the list is better than an exception thrown
                // at somebody who only asked for a song.
                family = Catalogue.Families[0];
            }
            int typeIndex = Catalogue.TypeIndex(family, typeName);
            return Keep(voices, family, typeIndex, pitch);
        }

        /// <summary>The voice for one block, instrument and note -- made the first
        /// time that combination is asked for, and the same one every time
        /// after, which is how a tune becomes a row of blocks rather than a block
        /// per note.</summary>
        private static Voice Keep(Dictionary<string, Voice> voices, Family family,
                                  int typeIndex, int pitch)
        {
            string key = family.Name + "|" + typeIndex + "|" + pitch;
            Voice voice;
            if (!voices.TryGetValue(key, out voice))
            {
                voice = new Voice();
                voice.Block = family;
                voice.TypeIndex = typeIndex;
                voice.Pitch = pitch;
                voice.Key = key;
                voices[key] = voice;
            }
            return voice;
        }

        /// <summary>Whether a General MIDI program is one of the synth ranges: the
        /// leads, the pads and the effects, which are the parts with no home among
        /// nine acoustic blocks.</summary>
        private static bool Synth(int program)
        {
            return program >= 80 && program <= 103;
        }

        /// <summary>"Strings:Cello" as its two halves.</summary>
        private static void Split(string wanted, out string family, out string type)
        {
            family = wanted == null ? "" : wanted.Trim();
            type = "";
            int colon = family.IndexOf(':');
            if (colon >= 0)
            {
                type = family.Substring(colon + 1).Trim();
                family = family.Substring(0, colon).Trim();
            }
            if (family.Length == 0)
            {
                family = "Piano";
            }
        }

        /// <summary>Which kit piece a percussion note is, as an index into the
        /// tables above. Anything not in the common half of the kit is a snare.</summary>
        private static int Kit(int pitch)
        {
            for (int i = 0; i < DrumNotes.Length; i++)
            {
                if (DrumNotes[i] == pitch)
                {
                    return i;
                }
            }
            return 2;                   // the first snare
        }

        private static int PieceNote(string piece)
        {
            for (int i = 0; i < PieceNames.Length; i++)
            {
                if (PieceNames[i] == piece)
                {
                    return PieceNotes[i];
                }
            }
            return 50;
        }

        // ---- keeping two notes off one another -------------------------------

        /// <summary>
        /// Cuts each note short of the next one on its own block, and drops a note
        /// that would start inside that gap. Returns how many were dropped, and
        /// clears their entry in <paramref name="on"/>.
        ///
        /// Necessary because an emulated key counts its emulators:
        /// `MKey.UpdateEmulation` adds one on press and takes one away on release,
        /// and `Emulating` is "the count is above nought". A second timer firing
        /// while the first still holds the same name takes the count from one to
        /// two, which is not a press -- so the repeated note is silently swallowed
        /// and the note does not end until the last timer lets go. Repeated notes
        /// are half of most tunes.
        /// </summary>
        private static int Separate(List<MidiNote> notes, Voice[] on, float gap)
        {
            Dictionary<string, int> last = new Dictionary<string, int>();
            int dropped = 0;

            for (int i = 0; i < notes.Count; i++)
            {
                if (on[i] == null)
                {
                    continue;
                }
                string key = on[i].Key;
                int before;
                if (last.TryGetValue(key, out before))
                {
                    float since = notes[i].Start - notes[before].Start;
                    if (since < gap)
                    {
                        on[i] = null;           // too close to be heard as its own
                        dropped++;
                        continue;
                    }
                    if (notes[before].End > notes[i].Start - gap)
                    {
                        notes[before].Length = notes[i].Start - gap - notes[before].Start;
                    }
                }
                last[key] = i;
            }
            return dropped;
        }

        /// <summary>Collapses a list of parts to one line each, with a count.</summary>
        private static void Tidy(List<string> parts)
        {
            List<string> seen = new List<string>();
            List<int> counts = new List<int>();
            for (int i = 0; i < parts.Count; i++)
            {
                int at = seen.IndexOf(parts[i]);
                if (at < 0)
                {
                    seen.Add(parts[i]);
                    counts.Add(1);
                }
                else
                {
                    counts[at]++;
                }
            }
            parts.Clear();
            for (int i = 0; i < seen.Count; i++)
            {
                parts.Add(seen[i] + " x" + counts[i]);
            }
        }
    }
}
