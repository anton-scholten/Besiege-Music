using System;
using System.Collections.Generic;

namespace MusicMod
{
    /// <summary>
    /// The Braids block: Mutable Instruments' macro-oscillator, ported.
    ///
    /// It was a mod of its own -- Braids Synth -- and a song's synth parts were
    /// written for it only where somebody had installed it. It is one of this
    /// mod's blocks now: the sources are under `MusicScripts/Braids`,
    /// unchanged from the mod they came from, and `Mod.OnLoad` registers the
    /// module and its panel alongside this mod's own.
    ///
    /// This class is what the converter needs of it, which is not what the block
    /// needs of itself: the id to write, the five models a General MIDI synth part
    /// asks for, and the mapper keys to fill in. The block's own behaviour knows
    /// nothing about any of it.
    ///
    /// The settings written are Braids' own mapper keys, read off
    /// `BraidsBehaviour.SafeAwake`. They are ordinary `XData`, exactly as the
    /// instrument blocks' are.
    /// </summary>
    public static class Braids
    {
        /// <summary>This mod's own id now, and the local id of `SynthBlock.xml`.
        /// A registered prefab is named `&lt;mod guid&gt;-&lt;local id&gt;`, which
        /// is how it is found in the table -- the same way `Catalogue` finds the
        /// instrument blocks.</summary>
        public const string ModId = "aca735ea-a614-4aef-9676-67ec1edd5059";
        private const int LocalId = 13;

        /// <summary>
        /// The family name a song uses to ask for it, and what the summary calls
        /// it. It matches the block's own name, and it has to differ from the FM
        /// block's: a family name is how `Song` decides which mapper keys to
        /// write, and two families called the same thing would fill an FM block
        /// with Braids' keys and leave it silent.
        /// </summary>
        public const string FamilyName = "Braids";

        // Braids' own mapper keys, from BraidsBehaviour.SafeAwake.
        private const string Activate = "bmt-Activate";
        private const string Shape = "bmt-ShapeKey";
        private const string Pitch = "bmt-PitchKey";
        private const string Timbre = "bmt-TimbreKey";
        private const string Colour = "bmt-ColorKey";
        private const string Volume = "bmt-VolumeKey";
        private const string Attack = "bmt-AttackKey";
        private const string Release = "bmt-ReleaseKey";
        private const string Range = "bmt-RangeKey";

        // The models this uses, by the numbers in MacroOscillator. Only these
        // five: they are the ones a General MIDI synth part is asking for, and a
        // table of all twenty-three would be a table of guesses.
        private const int SawSwarm = 14;    // fat, detuned: pads
        private const int RawSaw = 16;      // the plain saw lead
        private const int RawSquare = 18;   // the plain square lead
        private const int TripleSaw = 9;    // three saws: a wide pad
        private const int RawSine = 20;     // soft, for the quiet ones

        private static int blockType;
        private static bool looked;
        private static bool said;

        /// <summary>Whether the Braids block is registered, so a song can be
        /// written for it.</summary>
        public static bool Available
        {
            get { Look(); return blockType > 0; }
        }

        /// <summary>The block id to write, or 0 when the mod is not here.</summary>
        public static int BlockType
        {
            get { Look(); return blockType; }
        }

        /// <summary>Looks for the block once, and again while it is not found:
        /// prefabs are registered after mods load, exactly as this mod's own
        /// are.</summary>
        public static void Look()
        {
            if (blockType > 0)
            {
                return;
            }
            try
            {
                string wanted = ModId + "-" + LocalId;
                foreach (KeyValuePair<int, BlockPrefab> pair in PrefabMaster.BlockPrefabs)
                {
                    BlockPrefab prefab = pair.Value;
                    if (prefab == null || prefab.name == null)
                    {
                        continue;
                    }
                    if (string.Compare(prefab.name, wanted, true) == 0)
                    {
                        blockType = (int)prefab.Type;
                        break;
                    }
                }
            }
            catch (Exception)
            {
                // No prefab table to read: either this is being asked before the
                // game has built one, or it is `tools/tests/SongCheck.cs` running
                // the converter outside Besiege entirely. Neither is worth saying
                // anything about -- there is nothing to say yet, and saying it
                // would mean calling into Unity from a process that has none.
                return;
            }
            if (!looked)
            {
                looked = true;
                Log.Info(blockType > 0
                    ? "the Braids block is registered (block " + blockType + "), so "
                      + "a score's synth parts will be written for it."
                    : "the Braids block is not in the prefab table yet; until it is, "
                      + "a score's synth parts go to the FM synth block instead.");
            }
        }

        /// <summary>
        /// A family standing for Braids' block, so the rest of the converter can
        /// treat it as one of ours: voices are keyed by family and pitch, blocks
        /// are laid out in one grid, and only the data written differs.
        ///
        /// Its "types" are the models a General MIDI part is asking for, in the
        /// order <see cref="Model"/> chooses between them.
        /// </summary>
        public static Family AsFamily()
        {
            Family family = new Family();
            family.Name = FamilyName;
            family.LocalId = LocalId;
            family.BlockType = BlockType;
            family.Types.Add("Saw lead");
            family.Types.Add("Square lead");
            family.Types.Add("Saw swarm");
            family.Types.Add("Triple saw");
            family.Types.Add("Sine");
            return family;
        }

        /// <summary>Which of those five a GM program wants.</summary>
        public static int TypeFor(int program)
        {
            if (program == 80 || program == 87) { return 1; }    // square leads
            if (program == 81 || program == 84 || program == 86) { return 0; }
            if (program >= 88 && program <= 95) { return 2; }    // pads
            if (program >= 96 && program <= 103) { return 3; }   // effects: wide
            if (program == 82 || program == 83 || program == 85) { return 4; }
            return 0;
        }

        /// <summary>
        /// How loud each of the five models is, as the RMS of one second of it at
        /// middle C with the volume full open. Measured, not guessed: the
        /// oscillator was rendered out of its own source and measured
        /// beside this mod's voices, because a raw saw and a struck bar at the same
        /// nominal volume are nothing like the same loudness.
        ///
        /// In the same measurement this mod's own blocks come to 0.04 (banjo)
        /// through 0.29 (kick), with the modal instruments around 0.2. So a synth
        /// block written at the same volume as the orchestra was three to four
        /// times as loud as the orchestra, which is what it sounded like.
        /// </summary>
        private static readonly float[] Loudness = new float[]
        {
            0.574f,     // raw saw
            0.863f,     // raw square
            0.162f,     // saw swarm -- three detuned saws cancel more than they add
            0.524f,     // triple saw
            0.704f,     // sine, which is a full-scale sine and so nearly all energy
        };

        /// <summary>What the orchestra's own middle comes to in that measurement,
        /// and so what a synth part is trimmed to.</summary>
        private const float Reference = 0.20f;

        /// <summary>
        /// The factor a model's volume is written with, so that a synth part sits
        /// in the band rather than over it. Never above 1: the swarm is already
        /// quieter than the orchestra, and a block cannot be turned up past its
        /// own slider.
        /// </summary>
        public static float Trim(int type)
        {
            float loud = type >= 0 && type < Loudness.Length
                ? Loudness[type] : Loudness[0];
            float trim = Reference / loud;
            return trim > 1f ? 1f : trim;
        }

        /// <summary>The model number for one of those five.</summary>
        public static int Model(int type)
        {
            switch (type)
            {
                case 1: return RawSquare;
                case 2: return SawSwarm;
                case 3: return TripleSaw;
                case 4: return RawSine;
                default: return RawSaw;
            }
        }

        /// <summary>
        /// Fills one Braids block in, as `Song` fills an instrument block.
        ///
        /// The note goes in `PitchKey`, which Braids clamps to 24..96 -- five
        /// octaves, against the eleven a MIDI file can name -- so anything outside
        /// is folded by octaves rather than clamped flat, which would turn a bass
        /// line into a drone.
        /// </summary>
        public static void Fill(XDataHolder data, int type, int note, string variable,
                                float volume, float range)
        {
            int pitch = note;
            while (pitch < 24) { pitch += 12; }
            while (pitch > 96) { pitch -= 12; }

            data.Write(new XStringArray(Activate, new string[]
            {
                "N", "Message=" + variable, "Use=True"
            }));
            data.Write(new XInteger(Shape, Model(type)));
            data.Write(new XSingle(Pitch, pitch));
            // Middle of the road for both: Braids' own defaults, which are what its
            // block sounds like when somebody places one by hand.
            data.Write(new XSingle(Timbre, 0.5f));
            data.Write(new XSingle(Colour, 0.5f));
            // Trimmed to the orchestra: see Trim, and the measurement behind it.
            data.Write(new XSingle(Volume, volume * Trim(type)));
            // Short but not clicky, and a release that lets a pad breathe.
            data.Write(new XSingle(Attack, type >= 2 ? 0.25f : 0.01f));
            data.Write(new XSingle(Release, type >= 2 ? 0.6f : 0.12f));
            data.Write(new XSingle(Range, range));
        }

        /// <summary>Said once per session, when a song actually uses it.</summary>
        public static void Note()
        {
            if (said)
            {
                return;
            }
            said = true;
            Log.Info("this song has synth parts, and they are being written for "
                     + "the Braids block.");
        }
    }
}
