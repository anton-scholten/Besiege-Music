namespace OrchestraMod
{
    /// <summary>
    /// General MIDI's 128 instruments, each pointed at the nearest block this mod
    /// has.
    ///
    /// A MIDI file says what each of its parts is -- a program change per channel,
    /// `Violin`, `Jazz Guitar`, `Tenor Sax` -- and until this table existed the
    /// converter threw all of that away and played every melodic part on whichever
    /// single block the panel was set to. A file naming eight instruments came out
    /// as eight tracks of piano.
    ///
    /// (Percussion never went through here. Channel 10 is a kit by convention
    /// rather than by program change, and <see cref="Song"/> has always mapped its
    /// note *numbers* onto the Drums and Cymbals blocks.)
    ///
    /// **Every entry is a real answer, and some are approximations.** Nine blocks
    /// against 128 instruments means the organs, the synth leads and the pads have
    /// no home of their own; they go to the nearest thing that sustains or cuts the
    /// same way, and the comments say which of those are guesses. A part on the
    /// wrong end of a family is better than a part on a piano.
    /// </summary>
    public static class Gm
    {
        /// <summary>The name a song asks for when it wants this table rather than
        /// one instrument for everything.</summary>
        public const string FromFile = "As the file says";

        private static readonly string[] Table = new string[]
        {
            // 0-7, pianos
            "Piano:Grand piano",        // Acoustic Grand
            "Piano:Upright piano",      // Bright Acoustic -- the brighter of ours
            "Piano:Grand piano",        // Electric Grand
            "Piano:Honky-tonk",         // Honky-tonk
            "Piano:Electric piano",     // Electric Piano 1 (Rhodes)
            "Piano:Electric piano",     // Electric Piano 2
            "Piano:Honky-tonk",         // Harpsichord -- plucked and bright; a guess
            "Piano:Electric piano",     // Clavi

            // 8-15, chromatic percussion
            "Mallets:Glockenspiel",     // Celesta
            "Mallets:Glockenspiel",     // Glockenspiel
            "Mallets:Glockenspiel",     // Music Box
            "Mallets:Vibraphone",       // Vibraphone
            "Mallets:Marimba",          // Marimba
            "Mallets:Xylophone",        // Xylophone
            "Mallets:Tubular bells",    // Tubular Bells
            "Mallets:Marimba",          // Dulcimer

            // 16-23, organs. Nothing here is an organ; what an organ shares with
            // this mod is that it holds a note without decaying, which is the
            // string ensemble. The reeds go to a reed.
            "Strings:Ensemble",         // Drawbar Organ
            "Strings:Ensemble",         // Percussive Organ
            "Strings:Ensemble",         // Rock Organ
            "Strings:Ensemble",         // Church Organ
            "Strings:Ensemble",         // Reed Organ
            "Woodwind:Clarinet",        // Accordion
            "Woodwind:Clarinet",        // Harmonica
            "Woodwind:Clarinet",        // Tango Accordion

            // 24-31, guitars -- the one family that maps across exactly
            "Guitar:Nylon",             // Nylon
            "Guitar:Steel",             // Steel
            "Guitar:Jazz",              // Jazz
            "Guitar:Clean",             // Clean
            "Guitar:Clean",             // Muted
            "Guitar:Overdriven",        // Overdriven
            "Guitar:Overdriven",        // Distortion
            "Guitar:Clean",             // Harmonics

            // 32-39, basses
            "Bass:Acoustic",            // Acoustic Bass
            "Bass:Fingered",            // Finger Bass
            "Bass:Picked",              // Pick Bass
            "Bass:Fretless",            // Fretless
            "Bass:Picked",              // Slap Bass 1
            "Bass:Picked",              // Slap Bass 2
            "Bass:Synth",               // Synth Bass 1
            "Bass:Synth",               // Synth Bass 2

            // 40-47, solo strings
            "Strings:Violin",           // Violin
            "Strings:Viola",            // Viola
            "Strings:Cello",            // Cello
            "Strings:Double bass",      // Contrabass
            "Strings:Ensemble",         // Tremolo Strings
            "Strings:Violin",           // Pizzicato -- plucked, and no plucked string here
            "Guitar:Nylon",             // Orchestral Harp -- plucked and soft
            "Drums:Tom",                // Timpani

            // 48-55, ensembles and voices
            "Strings:Ensemble",         // String Ensemble 1
            "Strings:Ensemble",         // String Ensemble 2
            "Strings:Ensemble",         // Synth Strings 1
            "Strings:Ensemble",         // Synth Strings 2
            "Strings:Ensemble",         // Choir Aahs
            "Strings:Ensemble",         // Voice Oohs
            "Strings:Ensemble",         // Synth Voice
            "Brass:Section",            // Orchestra Hit

            // 56-63, brass
            "Brass:Trumpet",            // Trumpet
            "Brass:Trombone",           // Trombone
            "Brass:Tuba",               // Tuba
            "Brass:Trumpet",            // Muted Trumpet
            "Brass:French horn",        // French Horn
            "Brass:Section",            // Brass Section
            "Brass:Section",            // Synth Brass 1
            "Brass:Section",            // Synth Brass 2

            // 64-71, reeds
            "Woodwind:Sax",             // Soprano Sax
            "Woodwind:Sax",             // Alto Sax
            "Woodwind:Sax",             // Tenor Sax
            "Woodwind:Sax",             // Baritone Sax
            "Woodwind:Oboe",            // Oboe
            "Woodwind:Oboe",            // English Horn
            "Woodwind:Bassoon",         // Bassoon
            "Woodwind:Clarinet",        // Clarinet

            // 72-79, pipes
            "Woodwind:Flute",           // Piccolo
            "Woodwind:Flute",           // Flute
            "Woodwind:Flute",           // Recorder
            "Woodwind:Flute",           // Pan Flute
            "Woodwind:Flute",           // Blown Bottle
            "Woodwind:Flute",           // Shakuhachi
            "Woodwind:Flute",           // Whistle
            "Woodwind:Flute",           // Ocarina

            // 80-87, synth leads. No synth block: a lead is the part that cuts
            // through, and the overdriven guitar is what cuts through here. The
            // Braids Synth mod is where these really belong.
            "Guitar:Overdriven",        // Square Lead
            "Guitar:Overdriven",        // Saw Lead
            "Woodwind:Flute",           // Calliope -- a pipe, whatever the bank says
            "Woodwind:Flute",           // Chiff
            "Guitar:Overdriven",        // Charang
            "Strings:Ensemble",         // Voice Lead
            "Guitar:Overdriven",        // Fifths
            "Bass:Synth",               // Bass + Lead

            // 88-95, synth pads: sustained washes, so the ensemble
            "Strings:Ensemble", "Strings:Ensemble", "Strings:Ensemble",
            "Strings:Ensemble", "Strings:Ensemble", "Strings:Ensemble",
            "Strings:Ensemble", "Strings:Ensemble",

            // 96-103, synth effects: the same, and just as approximate
            "Strings:Ensemble", "Strings:Ensemble", "Strings:Ensemble",
            "Strings:Ensemble", "Strings:Ensemble", "Strings:Ensemble",
            "Strings:Ensemble", "Strings:Ensemble",

            // 104-111, ethnic
            "Guitar:Steel",             // Sitar
            "Guitar:Steel",             // Banjo
            "Guitar:Nylon",             // Shamisen
            "Guitar:Nylon",             // Koto
            "Mallets:Marimba",          // Kalimba
            "Woodwind:Oboe",            // Bagpipe
            "Strings:Violin",           // Fiddle
            "Woodwind:Oboe",            // Shanai

            // 112-119, percussive
            "Mallets:Glockenspiel",     // Tinkle Bell
            "Mallets:Marimba",          // Agogo
            "Mallets:Marimba",          // Steel Drums
            "Drums:Rim",                // Woodblock
            "Drums:Kick",               // Taiko
            "Drums:Tom",                // Melodic Tom
            "Drums:Tom",                // Synth Drum
            "Cymbals:Crash",            // Reverse Cymbal

            // 120-127, sound effects. Nothing sensible is possible; these are the
            // least silly places to put them, and a score using them as music is
            // rare enough that being wrong here costs nothing.
            "Guitar:Clean",             // Guitar Fret Noise
            "Woodwind:Flute",           // Breath Noise
            "Cymbals:Crash",            // Seashore
            "Woodwind:Flute",           // Bird Tweet
            "Mallets:Glockenspiel",     // Telephone Ring
            "Drums:Tom",                // Helicopter
            "Cymbals:Crash",            // Applause
            "Drums:Kick",               // Gunshot
        };

        /// <summary>The block and instrument for one GM program number, as
        /// "Family:Type". A number outside 0-127 -- which includes the -1 a note on
        /// a channel nobody ever set stands at -- comes back as the grand piano,
        /// which is what General MIDI itself says a channel starts on.</summary>
        public static string Instrument(int program)
        {
            if (program < 0 || program >= Table.Length)
            {
                return Table[0];
            }
            return Table[program];
        }

        /// <summary>How many instruments this knows, for a test to hold it to.</summary>
        public static int Count { get { return Table.Length; } }
    }
}
