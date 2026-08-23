using System;
using System.Collections.Generic;
using Modding;
using UnityEngine;

namespace OrchestraMod
{
    /// <summary>
    /// The recorded notes for one instrument type, read out of Besiege's resources
    /// once and kept as plain float arrays.
    ///
    /// Reading them out matters: the audio thread cannot call into Unity, so
    /// AudioClip.GetData has to happen on the game thread, at load, and the voices
    /// then index a managed array. It is also the only way in at all -- System.IO
    /// is blacklisted, so ModResource is the sole route from disk to memory.
    ///
    /// A sample's own pitch comes from its name: `piano_grand_60` is middle C.
    /// Keeping it in the name means the key map needs no second data file, and a
    /// sample can be dropped in without editing code.
    /// </summary>
    public class SampleBank
    {
        public class Entry
        {
            public float[] Data;
            public int Note;
            public int Rate;

            /// <summary>Loop bounds in samples, or -1 for a sample that does not loop.</summary>
            public int LoopStart = -1;
            public int LoopEnd = -1;

            /// <summary>
            /// The window at the end that a struck note rings on with, or -1 where
            /// there is none. See <see cref="FindTail"/>.
            /// </summary>
            public int TailStart = -1;
            public int TailEnd = -1;

            /// <summary>
            /// What the ring-out is turned down by each time round that window, which
            /// is how much the recording itself falls across it. Without it the level
            /// would step back up at every turn.
            /// </summary>
            public float TailGain = 1f;
        }

        /// <summary>How much of the end of a recording the ring-out plays round.</summary>
        private const float TailSeconds = 0.15f;

        /// <summary>
        /// What `extract-samples.py` fades out at the end of an unlooped cut,
        /// measured off the shipped samples rather than read off the script -- what
        /// arrives back through Ogg is nearer thirty milliseconds than the ten it
        /// asks for. The ring-out has to stop short of it: a fade to nothing inside
        /// the window would be heard as a dip on every turn.
        /// </summary>
        private const float FadeSeconds = 0.03f;

        /// <summary>
        /// How much quieter the end of the window may be than its start. A window the
        /// recording falls a long way across is one that steps the level up every
        /// time it turns round, so it is halved until it is level enough.
        /// </summary>
        private const float Steady = 0.8f;

        /// <summary>
        /// How far either side of the nominal window length the seam is searched
        /// for. A sample is not filed under exactly the pitch it was recorded at --
        /// fonts carry a correction -- and over a hundred periods a fraction of a
        /// percent is half a wavelength out.
        /// </summary>
        private const float Spread = 0.06f;

        /// <summary>
        /// Every bank read so far, by the samples it was asked for.
        ///
        /// A bank is a few hundred kilobytes of immutable float, and `LoadBanks` runs
        /// for every block placed -- twenty pianos would otherwise read the same
        /// twelve clips twenty times, and now search each of them for its ring-out as
        /// well. Shared rather than copied: nothing writes to an entry after it is
        /// made.
        /// </summary>
        private static readonly Dictionary<string, SampleBank> banks =
            new Dictionary<string, SampleBank>();

        private readonly List<Entry> entries = new List<Entry>();

        public int Count { get { return entries.Count; } }

        /// <summary>
        /// Loads every clip named in a type's `samples` attribute. Missing clips
        /// are logged and skipped rather than thrown: one absent sample should
        /// cost that instrument its top octave, not the whole mod.
        /// </summary>
        public static SampleBank Load(string names, string loops)
        {
            SampleBank bank;
            string key = names + "|" + loops;
            if (banks.TryGetValue(key, out bank))
            {
                return bank;
            }
            bank = new SampleBank();
            banks[key] = bank;
            if (string.IsNullOrEmpty(names))
            {
                return bank;
            }

            string[] parts = names.Split(' ');
            string[] loopParts = string.IsNullOrEmpty(loops)
                ? new string[0] : loops.Split(' ');
            int loopIndex = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                string name = parts[i].Trim();
                if (name.Length == 0)
                {
                    continue;
                }
                loopIndex++;

                int note = NoteFromName(name);
                if (note < 0)
                {
                    ModConsole.Log("Orchestra: sample '" + name + "' has no note suffix; skipped.");
                    continue;
                }

                AudioClip clip = null;
                try
                {
                    clip = ModResource.GetAudioClip(name);
                }
                catch (Exception)
                {
                    clip = null;
                }
                if (clip == null)
                {
                    ModConsole.Log("Orchestra: sample '" + name + "' did not load; skipped.");
                    continue;
                }

                float[] data = new float[clip.samples * clip.channels];
                clip.GetData(data, 0);
                if (clip.channels > 1)
                {
                    data = ToMono(data, clip.channels);
                }

                Entry entry = new Entry();
                entry.Data = data;
                entry.Note = note;
                entry.Rate = clip.frequency;
                ReadLoop(entry, loopParts, loopIndex, data.Length);
                FindTail(entry);
                bank.entries.Add(entry);
            }
            return bank;
        }

        /// <summary>
        /// Parses one `start-end` pair, positionally. A malformed or out-of-range
        /// pair leaves the sample unlooped rather than throwing: a bad number in a
        /// generated attribute should cost that note its sustain, nothing more.
        /// </summary>
        private static void ReadLoop(Entry entry, string[] loops, int index, int length)
        {
            if (index < 0 || index >= loops.Length)
            {
                return;
            }
            string spec = loops[index];
            int dash = spec.IndexOf('-');
            if (dash <= 0 || dash == spec.Length - 1)
            {
                return;
            }
            int start, end;
            if (!int.TryParse(spec.Substring(0, dash), out start) ||
                !int.TryParse(spec.Substring(dash + 1), out end))
            {
                return;
            }
            // Three samples short of the end: the interpolation reads that far
            // ahead, so a voice stops there, and a loop that ends any later would
            // never be reached to turn round in.
            int usable = length - 3;
            if (end > usable)
            {
                // Vorbis does not hand back quite the number of samples it was
                // given, and the extractor's numbers are the encoder's. Trimmed
                // rather than thrown away: a loop a few samples shorter is a note
                // that still sustains, and no loop at all is one that stops.
                int shift = end - usable;
                start -= shift;
                end -= shift;
            }
            if (start < 0 || end - start < 16)
            {
                return;
            }
            entry.LoopStart = start;
            entry.LoopEnd = end;
        }

        /// <summary>
        /// Finds a window at the end of an unlooped recording that can be played
        /// round and round while it fades, so a struck note rings out instead of
        /// stopping where the cut was made.
        ///
        /// It has to: the recordings end at two seconds or wherever the font's own
        /// sample did, and a guitar or a piano is nowhere near silent by then --
        /// the shortest of these is still at two thirds of its body level when it
        /// runs out, which is heard as a note that stops rather than one that ends.
        ///
        /// The window is a whole number of periods of the note the sample *is*, so
        /// what it wraps onto is the same part of the same waveform, and the seam is
        /// not heard. Sustaining instruments are left alone -- they have a real loop,
        /// and the playhead never reaches the end.
        /// </summary>
        private static void FindTail(Entry entry)
        {
            if (entry.LoopEnd > entry.LoopStart || entry.Rate <= 0)
            {
                return;
            }
            int end = entry.Data.Length - (int)(entry.Rate * FadeSeconds) - 3;
            if (end <= 0)
            {
                return;
            }
            float hz = 440f * Mathf.Pow(2f, (entry.Note - 69f) / 12f);
            float period = entry.Rate / hz;
            if (period < 2f)
            {
                return;
            }

            // As many periods as make up the wanted window, halved until what is
            // left of the recording holds it and the note does not fall too far
            // across it.
            int cycles = Mathf.Max(2, Mathf.RoundToInt(TailSeconds * entry.Rate / period));
            int window = 0;
            int compare = 0;
            while (true)
            {
                window = Mathf.RoundToInt(cycles * period);
                compare = Mathf.Min((int)(2f * period) + 1, window);
                if (window >= 32 && window <= end / 2 && end - window >= compare)
                {
                    float near = Loudness(entry.Data, end, compare);
                    float far = Loudness(entry.Data, end - window, compare);
                    if (far <= 0f)
                    {
                        return;
                    }
                    if (near / far >= Steady)
                    {
                        break;
                    }
                }
                if (cycles <= 2)
                {
                    // Nothing left to shorten: this recording is too short to ring
                    // on, and stops where it stops as it always did.
                    return;
                }
                cycles = Mathf.Max(2, cycles / 2);
            }

            entry.TailStart = end - Seam(entry.Data, end, window, compare);
            entry.TailEnd = end;
            entry.TailGain = Mathf.Min(1f, Loudness(entry.Data, end, compare)
                                         / Mathf.Max(1e-9f, Loudness(entry.Data, entry.TailStart, compare)));
        }

        /// <summary>
        /// The window length whose end matches what comes before the wrap best, near
        /// the whole number of periods asked for. Correlation rather than difference,
        /// so a quieter stretch cannot win by being quiet.
        /// </summary>
        private static int Seam(float[] data, int end, int window, int compare)
        {
            int low = Mathf.Max(32, (int)(window * (1f - Spread)));
            int high = Mathf.Min((int)(window * (1f + Spread)), end - compare - 1);
            float best = -2f;
            int found = window;
            for (int w = low; w <= high; w++)
            {
                float sum = 0f, here = 0f, there = 0f;
                for (int k = 1; k <= compare; k++)
                {
                    float a = data[end - k];
                    float b = data[end - w - k];
                    sum += a * b;
                    here += a * a;
                    there += b * b;
                }
                if (here <= 0f || there <= 0f)
                {
                    continue;
                }
                float score = sum / Mathf.Sqrt(here * there);
                if (score > best)
                {
                    best = score;
                    found = w;
                }
            }
            return found;
        }

        /// <summary>RMS of the samples ending at <paramref name="at"/>.</summary>
        private static float Loudness(float[] data, int at, int count)
        {
            if (count <= 0 || at - count < 0 || at > data.Length)
            {
                return 0f;
            }
            float sum = 0f;
            for (int k = 1; k <= count; k++)
            {
                sum += data[at - k] * data[at - k];
            }
            return Mathf.Sqrt(sum / count);
        }

        /// <summary>The recorded note closest to the one asked for.</summary>
        public Entry Nearest(float note)
        {
            Entry best = null;
            float bestDistance = 1e9f;
            for (int i = 0; i < entries.Count; i++)
            {
                float d = entries[i].Note - note;
                if (d < 0f)
                {
                    d = -d;
                }
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = entries[i];
                }
            }
            return best;
        }

        /// <summary>Everything downstream is mono; the source spatialises it.</summary>
        private static float[] ToMono(float[] data, int channels)
        {
            int frames = data.Length / channels;
            float[] mono = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++)
                {
                    sum += data[i * channels + c];
                }
                mono[i] = sum / channels;
            }
            return mono;
        }

        /// <summary>Trailing `_60` is MIDI note 60. Returns -1 when absent.</summary>
        private static int NoteFromName(string name)
        {
            int underscore = name.LastIndexOf('_');
            if (underscore < 0 || underscore == name.Length - 1)
            {
                return -1;
            }
            int note = 0;
            for (int i = underscore + 1; i < name.Length; i++)
            {
                char c = name[i];
                if (c < '0' || c > '9')
                {
                    return -1;
                }
                note = note * 10 + (c - '0');
            }
            return note > 127 ? -1 : note;
        }
    }
}
