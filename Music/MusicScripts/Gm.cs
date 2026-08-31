namespace MusicMod
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
    /// **Every entry is a real answer, and some are approximations.** Eleven blocks
    /// against 128 instruments, and what is left over is mostly the sound effects:
    /// the leads and pads have blocks of their own now, where they used to go to an
    /// overdriven guitar because that is what cut through. The comments say which
    /// entries are guesses. A part on the wrong end of a family is better than a
    /// part on a piano.
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
            "Piano:Electric piano",     // Electric Piano 1 (Rhodes), a real Rhodes
            "FM Synth:Electric piano",     // Electric Piano 2, which is the FM one
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
            "Plucked:Harp",             // Dulcimer -- struck strings, but strings

            // 16-23, organs. The Woodwind block has one -- a flue pipe is a
            // whistle, and the sample is written rather than cut. The reeds and
            // the free reeds go to a reed.
            "Woodwind:Organ",           // Drawbar Organ
            "Woodwind:Organ",           // Percussive Organ
            "Woodwind:Organ",           // Rock Organ
            "Woodwind:Organ",           // Church Organ
            "Woodwind:Organ",           // Reed Organ
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
            "Plucked:Pizzicato",        // Pizzicato
            "Plucked:Harp",             // Orchestral Harp
            "Drums:Tom",                // Timpani

            // 48-55, ensembles and voices
            "Strings:Ensemble",         // String Ensemble 1
            "Strings:Ensemble",         // String Ensemble 2
            "Strings:Ensemble",         // Synth Strings 1
            "Strings:Ensemble",         // Synth Strings 2
            "Strings:Choir",            // Choir Aahs
            "Strings:Choir",            // Voice Oohs
            "Strings:Choir",            // Synth Voice
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

            // 80-87, synth leads. These have a block of their own now: two-operator
            // FM, which is what most of these presets were on the machines they
            // came from. They went to an overdriven guitar before, because a lead
            // is the part that cuts through and that is what cut through here.
            "FM Synth:Square lead",        // Square Lead
            "FM Synth:Lead",               // Saw Lead
            "Woodwind:Flute",           // Calliope -- a pipe, whatever the bank says
            "Woodwind:Flute",           // Chiff
            "FM Synth:Lead",               // Charang
            "FM Synth:Choir pad",          // Voice Lead
            "FM Synth:Lead",               // Fifths
            "FM Synth:Bass",               // Bass + Lead

            // 88-95, synth pads: sustained washes, and the one General MIDI itself
            // calls a choir keeps its voices.
            "FM Synth:Pad",                // New Age
            "FM Synth:Pad",                // Warm
            "FM Synth:Pad",                // Polysynth
            "FM Synth:Choir pad",          // Choir
            "FM Synth:Pad",                // Bowed
            "FM Synth:Bell",               // Metallic -- the one pad that is a bell
            "FM Synth:Choir pad",          // Halo
            "FM Synth:Pad",                // Sweep

            // 96-103, synth effects. Nothing is really these, but a bell and a pad
            // between them cover the ones a score uses as music.
            "FM Synth:Bell",               // Rain
            "FM Synth:Pad",                // Soundtrack
            "FM Synth:Bell",               // Crystal
            "FM Synth:Pad",                // Atmosphere
            "FM Synth:Bell",               // Brightness
            "FM Synth:Pad",                // Goblins
            "FM Synth:Pad",                // Echoes
            "FM Synth:Pad",                // Sci-fi

            // 104-111, ethnic
            "Plucked:Sitar",            // Sitar
            "Plucked:Banjo",            // Banjo
            "Plucked:Koto",             // Shamisen
            "Plucked:Koto",             // Koto
            "Mallets:Marimba",          // Kalimba
            "Woodwind:Oboe",            // Bagpipe
            "Strings:Violin",           // Fiddle
            "Woodwind:Oboe",            // Shanai

            // 112-119, percussive
            "Mallets:Glockenspiel",     // Tinkle Bell
            "Mallets:Marimba",          // Agogo
            "Mallets:Steel drum",       // Steel Drums
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
