using System;
using System.Collections.Generic;

namespace MusicMod
{
    /// <summary>One note of a score, in seconds rather than in ticks.</summary>
    public class MidiNote
    {
        public float Start;
        public float Length;
        public int Pitch;
        public int Velocity;
        public int Channel;
        public int Track;

        /// <summary>The General MIDI program in force on this note's channel when
        /// it was struck, or -1 where the file never said. What
        /// <see cref="Gm.Instrument"/> turns into a block.</summary>
        public int Program = -1;

        public float End
        {
            get { return Start + Length; }
        }
    }

    /// <summary>
    /// A standard MIDI file, read down to the notes.
    ///
    /// The same parse as `tools/make-song.py`, in the language the game speaks, so
    /// a file can be turned into a machine from inside Besiege. Written out rather
    /// than taken from a library because there is no library to take: a mod
    /// assembly may reference the game's assemblies and nothing else.
    ///
    /// `System.IO.File` is blacklisted, so this is handed the bytes -- see
    /// <see cref="Files"/> for where they come from.
    /// </summary>
    public class Midi
    {
        // Event kinds. Constants rather than an enum: Besiege's own C# compiler
        // segfaults on an enum declaration.
        private const int Tempo = 0;

        /// <summary>A program change: `A` is the GM instrument number.</summary>
        private const int Program = 4;
        private const int NoteOn = 1;
        private const int NoteOff = 2;

        private class Event
        {
            public int Tick;
            public int Kind;
            public int Channel;
            public int A;       // pitch, or microseconds per beat for a tempo
            public int B;       // velocity
        }

        private int division;
        private readonly List<List<Event>> tracks = new List<List<Event>>();

        /// <summary>
        /// Reads a file. Throws with a sentence worth showing the player: this is
        /// reached from a text box, so "not a MIDI file" is the common case rather
        /// than an internal error.
        /// </summary>
        public Midi(byte[] data)
        {
            if (data == null || data.Length < 14 || data[0] != 'M' || data[1] != 'T'
                || data[2] != 'h' || data[3] != 'd')
            {
                throw new Exception("that is not a MIDI file");
            }
            int length = Int32At(data, 4);
            int wanted = Int16At(data, 10);
            division = Int16At(data, 12);
            if (division <= 0)
            {
                throw new Exception("SMPTE timecode MIDI is not supported; "
                                    + "export with ticks per beat instead");
            }

            int at = 8 + length;
            while (at + 8 <= data.Length && tracks.Count < wanted)
            {
                if (data[at] != 'M' || data[at + 1] != 'T'
                    || data[at + 2] != 'r' || data[at + 3] != 'k')
                {
                    break;
                }
                int size = Int32At(data, at + 4);
                if (size < 0 || at + 8 + size > data.Length)
                {
                    break;                  // a truncated file still plays what it has
                }
                tracks.Add(ReadTrack(data, at + 8, size));
                at += 8 + size;
            }
            if (tracks.Count == 0)
            {
                throw new Exception("that MIDI file has no tracks");
            }
        }

        private static int Int32At(byte[] data, int at)
        {
            return (data[at] << 24) | (data[at + 1] << 16)
                 | (data[at + 2] << 8) | data[at + 3];
        }

        private static int Int16At(byte[] data, int at)
        {
            return (data[at] << 8) | data[at + 1];
        }

        /// <summary>MIDI's seven-bits-at-a-time integer.</summary>
        private static int VarLen(byte[] data, ref int at)
        {
            int value = 0;
            while (at < data.Length)
            {
                byte b = data[at++];
                value = (value << 7) | (b & 0x7F);
                if ((b & 0x80) == 0)
                {
                    break;
                }
            }
            return value;
        }

        /// <summary>
        /// One track's events, with running status resolved: a byte without its
        /// top bit set repeats the previous message's kind, which is most of a
        /// real file.
        /// </summary>
        private List<Event> ReadTrack(byte[] data, int from, int size)
        {
            List<Event> out_ = new List<Event>();
            int at = from;
            int end = from + size;
            int tick = 0;
            int status = 0;

            while (at < end)
            {
                tick += VarLen(data, ref at);
                if (at >= end)
                {
                    break;
                }
                byte b = data[at];
                if ((b & 0x80) != 0)
                {
                    status = b;
                    at++;
                }

                if (status == 0xFF)
                {
                    if (at >= end)
                    {
                        break;
                    }
                    int kind = data[at++];
                    int meta = VarLen(data, ref at);
                    if (kind == 0x51 && meta == 3 && at + 3 <= end)
                    {
                        Event e = new Event();
                        e.Tick = tick;
                        e.Kind = Tempo;
                        e.A = (data[at] << 16) | (data[at + 1] << 8) | data[at + 2];
                        out_.Add(e);
                    }
                    at += meta;
                    if (kind == 0x2F)
                    {
                        break;              // end of track
                    }
                }
                else if (status == 0xF0 || status == 0xF7)
                {
                    at += VarLen(data, ref at);
                }
                else
                {
                    int high = status & 0xF0;
                    int channel = status & 0x0F;
                    if (high == 0x80 || high == 0x90 || high == 0xA0
                        || high == 0xB0 || high == 0xE0)
                    {
                        if (at + 2 > end)
                        {
                            break;
                        }
                        int a = data[at];
                        int v = data[at + 1];
                        at += 2;
                        if (high == 0x90 || high == 0x80)
                        {
                            Event e = new Event();
                            e.Tick = tick;
                            // A note-on with no velocity is how most files write a
                            // note-off, and both spellings are in common use.
                            e.Kind = (high == 0x90 && v > 0) ? NoteOn : NoteOff;
                            e.Channel = channel;
                            e.A = a;
                            e.B = v;
                            out_.Add(e);
                        }
                    }
                    else if (high == 0xC0)
                    {
                        // A program change: what this channel is from here on.
                        // Kept, where it used to be stepped over -- it is the whole
                        // of how a file says which part is which instrument.
                        Event e = new Event();
                        e.Tick = tick;
                        e.Kind = Program;
                        e.Channel = channel;
                        e.A = data[at];
                        out_.Add(e);
                        at += 1;
                    }
                    else if (high == 0xD0)
                    {
                        at += 1;
                    }
                    else
                    {
                        at += 1;            // an unknown status byte: step over it
                    }
                }
            }
            return out_;
        }

        // ---- ticks to seconds ------------------------------------------------

        private class Mark
        {
            public int Tick;
            public double Seconds;      // where this segment starts
            public int Micros;          // microseconds per beat through it
        }

        /// <summary>
        /// The file's tempo map, walked once, so a tick can be turned into seconds
        /// by a binary search rather than by replaying the score.
        /// </summary>
        private List<Mark> Tempos(float overrideBpm)
        {
            List<int[]> changes = new List<int[]>();
            if (overrideBpm > 0f)
            {
                changes.Add(new int[] { 0, (int)Math.Round(60000000.0 / overrideBpm), 0 });
            }
            else
            {
                // The third number is where the change was found, and it is not
                // decoration: `List.Sort` is not stable, and a file that sets its
                // tempo at tick 0 -- most of them -- has two entries there with
                // the assumed 120 bpm. Sorted without a tiebreak, the assumption
                // can end up last and win, and the whole score plays at the wrong
                // speed with nothing else out of place.
                changes.Add(new int[] { 0, 500000, 0 });    // MIDI's default: 120 bpm
                for (int t = 0; t < tracks.Count; t++)
                {
                    List<Event> track = tracks[t];
                    for (int i = 0; i < track.Count; i++)
                    {
                        if (track[i].Kind == Tempo)
                        {
                            changes.Add(new int[] { track[i].Tick, track[i].A,
                                                    changes.Count });
                        }
                    }
                }
                changes.Sort(ByTick);
            }

            List<Mark> marks = new List<Mark>();
            int lastTick = 0;
            int lastMicros = changes[0][1];
            double elapsed = 0.0;
            for (int i = 0; i < changes.Count; i++)
            {
                int tick = changes[i][0];
                if (tick > lastTick)
                {
                    elapsed += (tick - lastTick) * (double)lastMicros / 1e6 / division;
                    lastTick = tick;
                }
                lastMicros = changes[i][1];
                Mark mark = new Mark();
                mark.Tick = tick;
                mark.Seconds = elapsed;
                mark.Micros = lastMicros;
                marks.Add(mark);
            }
            return marks;
        }

        /// <summary>
        /// The tempo the file itself starts at, in beats per minute -- what the
        /// panel's TEMPO slider is set to when a file is picked, so the number it
        /// shows is the file's own until somebody changes it.
        ///
        /// Read through the same tempo map the notes are timed by, rather than by
        /// hunting for the first tempo event: the rules about which of two events
        /// at tick 0 wins are in there, and a second copy of them would be a second
        /// chance to get them wrong.
        /// </summary>
        public float StartBpm
        {
            get
            {
                List<Mark> marks = Tempos(0f);
                // The *last* mark at tick 0, not the first. The map is seeded with
                // MIDI's assumed 120 bpm and a file that sets its own tempo there
                // adds a second mark at the same tick; the later one is the one in
                // force, which is what the binary search in SecondsAt lands on and
                // so what every note is timed by. Taking marks[0] reported 120 for
                // every file in the world.
                int micros = 500000;
                for (int i = 0; i < marks.Count && marks[i].Tick <= 0; i++)
                {
                    micros = marks[i].Micros;
                }
                return micros <= 0 ? 120f : (float)(60000000.0 / micros);
            }
        }

        private static int ByTick(int[] a, int[] b)
        {
            int first = a[0].CompareTo(b[0]);
            return first != 0 ? first : a[2].CompareTo(b[2]);
        }

        private static double SecondsAt(List<Mark> marks, int tick, int division)
        {
            int lo = 0;
            int hi = marks.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (marks[mid].Tick <= tick)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            Mark mark = marks[lo];
            return mark.Seconds + (tick - mark.Tick) * (double)mark.Micros / 1e6 / division;
        }

        // ---- notes -----------------------------------------------------------

        /// <summary>
        /// Every note in the file, in seconds, sorted by when it starts.
        /// `overrideBpm` above zero replaces the file's own tempo map.
        /// </summary>
        public List<MidiNote> Notes(float overrideBpm)
        {
            List<Mark> marks = Tempos(overrideBpm);
            List<MidiNote> out_ = new List<MidiNote>();

            for (int t = 0; t < tracks.Count; t++)
            {
                List<Event> track = tracks[t];
                // (channel, pitch) -> the note-ons waiting for their note-off. A
                // dictionary of lists, because a file may strike a key again before
                // releasing it and both notes are real.
                Dictionary<int, List<int[]>> sounding = new Dictionary<int, List<int[]>>();

                // What each channel is set to as this track is read. A note carries
                // whatever its channel was on at the moment it was struck, so a
                // track that changes instrument part way through is followed.
                int[] program = new int[16];
                for (int c = 0; c < 16; c++)
                {
                    program[c] = -1;
                }

                for (int i = 0; i < track.Count; i++)
                {
                    Event e = track[i];
                    if (e.Kind == Program)
                    {
                        if (e.Channel >= 0 && e.Channel < 16)
                        {
                            program[e.Channel] = e.A;
                        }
                    }
                    else if (e.Kind == NoteOn)
                    {
                        int key = e.Channel * 128 + e.A;
                        List<int[]> held;
                        if (!sounding.TryGetValue(key, out held))
                        {
                            held = new List<int[]>();
                            sounding[key] = held;
                        }
                        held.Add(new int[] { e.Tick, e.B,
                            e.Channel >= 0 && e.Channel < 16 ? program[e.Channel] : -1 });
                    }
                    else if (e.Kind == NoteOff)
                    {
                        int key = e.Channel * 128 + e.A;
                        List<int[]> held;
                        if (sounding.TryGetValue(key, out held) && held.Count > 0)
                        {
                            int[] began = held[0];
                            held.RemoveAt(0);
                            MidiNote made = Make(marks, began[0], e.Tick, e.A,
                                                 began[1], e.Channel, t);
                            made.Program = began[2];
                            out_.Add(made);
                        }
                    }
                }

                // A note the file never released still gets to sound, for as long
                // as a note nobody ended reasonably can.
                foreach (KeyValuePair<int, List<int[]>> left in sounding)
                {
                    for (int i = 0; i < left.Value.Count; i++)
                    {
                        MidiNote note = Make(marks, left.Value[i][0], left.Value[i][0],
                                             left.Key % 128, left.Value[i][1],
                                             left.Key / 128, t);
                        note.Program = left.Value[i][2];
                        note.Length = 0.5f;
                        out_.Add(note);
                    }
                }
            }

            // A track that plays on a channel it never set takes whatever the file
            // said about that channel anywhere else -- some files put every program
            // change in one track and the notes in others.
            Fallback(out_);

            out_.Sort(ByStart);
            return out_;
        }

        private MidiNote Make(List<Mark> marks, int from, int to, int pitch,
                              int velocity, int channel, int track)
        {
            double start = SecondsAt(marks, from, division);
            MidiNote note = new MidiNote();
            note.Start = (float)start;
            note.Length = (float)(SecondsAt(marks, to, division) - start);
            note.Pitch = pitch;
            note.Velocity = velocity;
            note.Channel = channel;
            note.Track = track;
            return note;
        }

        /// <summary>
        /// Notes in playing order, ties broken all the way down.
        ///
        /// The same order `tools/make-song.py` gets, which sorts the tuple
        /// `(start, length, pitch, velocity, channel, track)` -- so this compares
        /// the same six things in the same order. It matters twice: the note limit
        /// keeps the first N of this order, and voices are numbered in it, so two
        /// notes struck at the same instant landing either way round is two
        /// different machines.
        ///
        /// It has to be a total order, not just a nearly-total one. `List.Sort` is
        /// not stable, so anything left tied here can come out differently between
        /// two runs of the same file -- comparing only the start and the pitch left
        /// that open, and left this converter disagreeing with the tool about which
        /// note was the 1200th.
        /// </summary>
        /// <summary>
        /// Fills in the program for notes whose own track never set one, from what
        /// the file said about that channel elsewhere.
        ///
        /// Left at -1 when nothing anywhere named it, which
        /// <see cref="Gm.Instrument"/> reads as the grand piano -- what General MIDI
        /// says a channel starts on.
        /// </summary>
        private void Fallback(List<MidiNote> notes)
        {
            int[] said = new int[16];
            for (int c = 0; c < 16; c++)
            {
                said[c] = -1;
            }
            for (int t = 0; t < tracks.Count; t++)
            {
                for (int i = 0; i < tracks[t].Count; i++)
                {
                    Event e = tracks[t][i];
                    if (e.Kind == Program && e.Channel >= 0 && e.Channel < 16
                        && said[e.Channel] < 0)
                    {
                        said[e.Channel] = e.A;
                    }
                }
            }
            for (int i = 0; i < notes.Count; i++)
            {
                if (notes[i].Program < 0 && notes[i].Channel >= 0
                    && notes[i].Channel < 16)
                {
                    notes[i].Program = said[notes[i].Channel];
                }
            }
        }

        private static int ByStart(MidiNote a, MidiNote b)
        {
            int order = a.Start.CompareTo(b.Start);
            if (order != 0) { return order; }
            order = a.Length.CompareTo(b.Length);
            if (order != 0) { return order; }
            order = a.Pitch.CompareTo(b.Pitch);
            if (order != 0) { return order; }
            order = a.Velocity.CompareTo(b.Velocity);
            if (order != 0) { return order; }
            order = a.Channel.CompareTo(b.Channel);
            return order != 0 ? order : a.Track.CompareTo(b.Track);
        }
    }
}
